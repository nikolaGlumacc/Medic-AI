"""
bot_server.py  –  MedicAI WebSocket bot server  (all-in-one)
=============================================================
vision_engine.py and watchdog.py are merged into this file.

Run directly:   python bot_server.py
Or via start.bat / start_server.bat.

The GUI connects on ws://localhost:8765 and sends JSON messages to control
the bot.  The bot main-loop runs in a background thread so the async
WebSocket handler stays responsive.

Vision is *optional*: if mss / cv2 / pytesseract are not installed the bot
still runs – it holds W and the heal beam so it at least keeps moving forward
and firing the medigun.  Full auto-targeting requires Tesseract OCR to be
installed and the optional vision dependencies present.
"""

from __future__ import annotations

import asyncio
import json
import os
import subprocess
import time
import threading
from collections import deque
from dataclasses import dataclass, field
from datetime import datetime
from typing import Optional

import psutil
import websockets
import websockets.exceptions

from pynput.keyboard import Controller as KeyboardController, Key
from pynput.mouse    import Controller as MouseController, Button


# ══════════════════════════════════════════════════════════════════════════════
# ░░  VISION ENGINE  (formerly vision_engine.py)
# ══════════════════════════════════════════════════════════════════════════════

@dataclass
class HudSnapshot:
    """What bot_main_loop reads from the HUD each tick."""
    health:             Optional[int]   = None
    heal_target_health: Optional[int]   = None
    uber_charge:        Optional[float] = None
    heal_target_name:   Optional[str]   = None
    ocr_available:      bool            = False


class VisionEngine:
    """
    Screen-capture + HUD-reading back-end.

    Dependency chain (all optional – degrades gracefully):
        mss          – screen capture
        cv2 / numpy  – image processing
        pytesseract  – OCR for HP / name / Uber text
    """

    # TF2 default 1080p HUD regions  (left, top, width, height)
    _REGION_HEALTH      = (45,  880, 120, 55)
    _REGION_TARGET_HP   = (700, 840, 160, 50)
    _REGION_TARGET_NAME = (620, 800, 320, 40)
    _REGION_UBER        = (765, 890, 130, 35)

    def __init__(self) -> None:
        self.sct   = None
        self.model = None          # reserved for future YOLO integration
        self._ocr_available = False
        self._np   = None
        self._cv2  = None
        self._tess = None
        self._init_libs()

    # ── initialisation ───────────────────────────────────────────────────────

    def _init_libs(self) -> None:
        try:
            import mss as _mss
            self.sct = _mss.mss()
        except ImportError:
            return

        try:
            import numpy as _np
            import cv2 as _cv2
            self._np  = _np
            self._cv2 = _cv2
        except ImportError:
            return

        try:
            import pytesseract as _tess
            _tess.get_tesseract_version()
            self._tess = _tess
            self._ocr_available = True
        except Exception:
            self._tess = None
            self._ocr_available = False

    # ── public API ───────────────────────────────────────────────────────────

    def capture_screen(self):
        """Return a BGR numpy array of the primary monitor, or a blank frame."""
        np = self._np
        cv2 = self._cv2

        if self.sct is None or np is None or cv2 is None:
            try:
                import numpy as _np
                return _np.zeros((1080, 1920, 3), dtype=_np.uint8)
            except ImportError:
                return None

        monitor = self.sct.monitors[1]
        raw = self.sct.grab(monitor)
        frame = np.array(raw)
        return cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR)

    def read_hud_snapshot(self) -> HudSnapshot:
        snap = HudSnapshot(ocr_available=self._ocr_available)
        if not self._ocr_available:
            return snap

        frame = self.capture_screen()
        if frame is None:
            return snap

        snap.health             = self._read_int_region(frame, self._REGION_HEALTH)
        snap.heal_target_health = self._read_int_region(frame, self._REGION_TARGET_HP)
        snap.uber_charge        = self._read_float_region(frame, self._REGION_UBER)
        snap.heal_target_name   = self._read_text_region(frame, self._REGION_TARGET_NAME)
        return snap

    # ── private helpers ──────────────────────────────────────────────────────

    def _crop(self, frame, region):
        x, y, w, h = region
        return frame[y:y + h, x:x + w]

    def _ocr_int(self, roi) -> Optional[int]:
        cv2, tess = self._cv2, self._tess
        if roi is None or cv2 is None:
            return None
        gray    = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        _, bw   = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        cfg     = "--psm 8 --oem 1 -c tessedit_char_whitelist=0123456789"
        raw     = tess.image_to_string(bw, config=cfg).strip()
        digits  = "".join(ch for ch in raw if ch.isdigit())
        return int(digits) if digits else None

    def _read_int_region(self, frame, region) -> Optional[int]:
        try:
            return self._ocr_int(self._crop(frame, region))
        except Exception:
            return None

    def _read_float_region(self, frame, region) -> Optional[float]:
        val = self._read_int_region(frame, region)
        return float(val) if val is not None else None

    def _read_text_region(self, frame, region) -> Optional[str]:
        if not self._ocr_available or self._cv2 is None:
            return None
        try:
            roi  = self._crop(frame, region)
            gray = self._cv2.cvtColor(roi, self._cv2.COLOR_BGR2GRAY)
            cfg  = "--psm 7 --oem 1"
            text = self._tess.image_to_string(gray, config=cfg).strip()
            return text if text else None
        except Exception:
            return None


# ══════════════════════════════════════════════════════════════════════════════
# ░░  WATCHDOG  (formerly watchdog.py)
# ══════════════════════════════════════════════════════════════════════════════

class WatchdogProcess:
    """Monitors the TF2 process and calls back on crash."""

    def __init__(self, executable: str = "hl2.exe") -> None:
        self.executable  = executable
        self.tf2_process = None

    def start_game(self, server_ip: Optional[str] = None) -> None:
        cmd = ["steam", "-applaunch", "440"]
        if server_ip:
            cmd.extend(["+connect", server_ip])
        print(f"Watchdog: Starting TF2 with {cmd}")
        subprocess.Popen(cmd)
        time.sleep(15)
        self.find_process()

    def find_process(self) -> bool:
        for proc in psutil.process_iter(["name", "pid"]):
            if proc.info["name"] == self.executable:
                self.tf2_process = proc
                return True
        self.tf2_process = None
        return False

    def is_running(self) -> bool:
        if self.tf2_process:
            return self.tf2_process.is_running()
        return self.find_process()

    def monitor_loop(self, callback_on_crash) -> None:
        while True:
            if not self.is_running():
                print("Watchdog: TF2 crash detected!")
                callback_on_crash()
            time.sleep(5)


# ══════════════════════════════════════════════════════════════════════════════
# ░░  MEDIC-VISION CLASSIC  (legacy – used by test_vision.py)
# ══════════════════════════════════════════════════════════════════════════════

class MedicVisionClassic:
    """Template-matching vision loop (test / debug use)."""

    def __init__(self, ws_sender) -> None:
        self.ws_sender       = ws_sender
        self.resolution      = (1920, 1080)
        self.template_path   = "templates/medic_call.png"
        self.match_threshold = 0.68
        self.spin_speed      = 6
        self.aim_sensitivity = 0.58
        self.heal_offset_y   = +40
        self.priority_names: set = set()

        self.team_lower = None
        self.team_upper = None
        try:
            import numpy as np
            self.team_lower = np.array([95, 80, 80])
            self.team_upper = np.array([130, 255, 255])
        except ImportError:
            pass

        try:
            import cv2
            self.medic_template = cv2.imread(self.template_path, cv2.IMREAD_COLOR)
            if self.medic_template is None:
                raise FileNotFoundError(
                    f"Template not found: {self.template_path}\n"
                    "Place 'medic_call.png' inside a 'templates/' folder."
                )
            self.template_h, self.template_w = self.medic_template.shape[:2]
        except ImportError:
            raise RuntimeError("cv2 (opencv-python) is required for MedicVisionClassic.")

        self.locked_target         = None
        self.target_history        = deque(maxlen=15)
        self.spinning              = True
        self.last_medic_time       = 0.0
        self.last_seen_target_time = 0.0
        print("MedicVisionClassic loaded")

    async def send_command(self, command: str, data=None) -> None:
        msg = {"type": command}
        if data:
            msg["data"] = data
        try:
            await self.ws_sender(json.dumps(msg))
        except Exception:
            pass

    def update_config(self, config_data: dict) -> None:
        if "priority_names" in config_data:
            self.priority_names = {n.lower().strip() for n in config_data["priority_names"]}

    def capture_screen(self):
        import mss
        import numpy as np
        import cv2
        with mss.mss() as sct:
            mon = {"top": 0, "left": 0,
                   "width": self.resolution[0], "height": self.resolution[1]}
            img = np.array(sct.grab(mon))
            return cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)

    def detect_medic_calls(self, frame):
        import cv2
        import numpy as np
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        tmpl = cv2.cvtColor(self.medic_template, cv2.COLOR_BGR2GRAY)
        res  = cv2.matchTemplate(gray, tmpl, cv2.TM_CCOEFF_NORMED)
        locs = np.where(res >= self.match_threshold)
        calls = []
        for pt in zip(*locs[::-1]):
            cx = pt[0] + self.template_w // 2
            cy = pt[1] + self.template_h // 2 - 25
            calls.append((cx, cy))
        return calls

    def find_target_below(self, frame, cross_pos):
        if self.team_lower is None or self.team_upper is None:
            return None
        import cv2
        x, y   = cross_pos
        roi_y1 = max(0, y + 25)
        roi_y2 = min(frame.shape[0], y + 240)
        roi_x1 = max(0, x - 80)
        roi_x2 = min(frame.shape[1], x + 80)
        if roi_y1 >= roi_y2 or roi_x1 >= roi_x2:
            return None
        roi  = frame[roi_y1:roi_y2, roi_x1:roi_x2]
        hsv  = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
        mask = cv2.inRange(hsv, self.team_lower, self.team_upper)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return None
        largest = max(contours, key=cv2.contourArea)
        if cv2.contourArea(largest) < 400:
            return None
        m = cv2.moments(largest)
        if m["m00"] == 0:
            return None
        cx = int(m["m10"] / m["m00"]) + roi_x1
        cy = int(m["m01"] / m["m00"]) + roi_y1 + 25
        return (cx, cy)

    async def run(self) -> None:
        import cv2
        print("Vision loop started – waiting for medic calls…")
        while True:
            frame = self.capture_screen()
            debug = frame.copy()
            calls = self.detect_medic_calls(frame)
            if calls:
                for i, cross in enumerate(calls):
                    cv2.circle(debug, cross, 18, (0, 0, 255), 4)
                    cv2.putText(debug, f"MEDIC CALL {i+1}",
                                (cross[0] - 60, cross[1] - 35),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
            if self.locked_target:
                tx, ty = self.locked_target
                cv2.circle(debug, (tx, ty), 25, (0, 255, 0), 4)
                cv2.putText(debug, "LOCKED TARGET", (tx - 80, ty - 45),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
            cv2.imshow("Medic Bot Debug", debug)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break
            if self.locked_target is None:
                if self.spinning:
                    await self.send_command("mouse_move", {"dx": self.spin_speed, "dy": 0})
                if calls and (time.time() - self.last_medic_time > 0.7):
                    cross      = min(calls, key=lambda p: p[1])
                    target_pos = self.find_target_below(frame, cross)
                    if target_pos:
                        self.locked_target         = target_pos
                        self.spinning              = False
                        self.last_medic_time       = time.time()
                        self.last_seen_target_time = time.time()
                        await self.send_command("medic_locked")
            else:
                target_pos = self.find_target_below(frame, self.locked_target)
                if target_pos:
                    self.locked_target         = target_pos
                    self.last_seen_target_time = time.time()
                    screen_cx = frame.shape[1] // 2
                    screen_cy = frame.shape[0] // 2 + self.heal_offset_y
                    dx = target_pos[0] - screen_cx
                    dy = target_pos[1] - screen_cy
                    await self.send_command("mouse_move", {
                        "dx": int(dx * self.aim_sensitivity),
                        "dy": int(dy * self.aim_sensitivity),
                    })
                elif time.time() - self.last_seen_target_time > 5.0:
                    self.locked_target = None
                    self.spinning      = True
                    await self.send_command("medic_unlocked")
            await asyncio.sleep(0.01)


async def start_vision(ws_sender) -> None:
    vision = MedicVisionClassic(ws_sender)
    await vision.run()


# ══════════════════════════════════════════════════════════════════════════════
# ░░  BOT SERVER
# ══════════════════════════════════════════════════════════════════════════════

class MedicAIBot:

    def __init__(self) -> None:
        self.config: dict = {}
        self.session_start = None
        self.running       = False
        self.follow_mode   = "active"
        self.bot_state     = "standby"
        self.ws            = None
        self.loop          = None

        # Live state
        self.current_target          = None
        self.target_health           = 100
        self.health                  = 100
        self.uber_charge             = 0.0
        self.uber_ready              = False
        self.uber_active             = False
        self.uber_start_time         = None

        # Misc flags
        self.cpu_overheat            = False
        self.heal_button_held        = False
        self.warned_about_ocr        = False
        self.last_environment_update = 0.0
        self.environment_update_interval = 0.25
        self.last_status_push        = 0.0
        self.status_push_interval    = 0.5
        self.last_any_heal_target_time = 0.0
        self.last_heal_weapon_select = 0.0
        self.last_spy_check_time     = 0.0

        # Input controllers
        self.keyboard = KeyboardController()
        self.mouse    = MouseController()

        # Vision & watchdog (optional)
        self.vision   = None
        self.watchdog = WatchdogProcess()

        try:
            self.vision = VisionEngine()
        except Exception as exc:
            print(f"[vision] Could not init VisionEngine: {exc}")

    # ─── Messaging helpers ────────────────────────────────────────────────────

    async def send_activity(self, msg: str, audio_trigger: bool = False,
                            audio_file: str = "none") -> None:
        if self.ws:
            try:
                await self.ws.send(json.dumps({
                    "type": "activity",
                    "msg": msg,
                    "audio": audio_trigger,
                    "audio_file": audio_file,
                }))
            except Exception as exc:
                print(f"[send_activity] {exc}")

    def _queue_payload(self, payload: dict) -> None:
        if self.ws and self.loop:
            try:
                asyncio.run_coroutine_threadsafe(
                    self.ws.send(json.dumps(payload)), self.loop
                )
            except Exception as exc:
                print(f"[queue_payload] {exc}")

    def send_activity_sync(self, msg: str, audio_trigger: bool = False,
                           audio_file: str = "none") -> None:
        self._queue_payload({
            "type": "activity",
            "msg": msg,
            "audio": audio_trigger,
            "audio_file": audio_file,
        })

    def build_status_payload(self) -> dict:
        return {
            "type":          "status",
            "mode":          self.bot_state,
            "uber":          int(round(self.uber_charge)),
            "health":        self.health,
            "target":        self.current_target or "",
            "target_health": self.target_health,
            "running":       self.running,
        }

    def send_status_sync(self, force: bool = False) -> None:
        now = time.time()
        if not force and now - self.last_status_push < self.status_push_interval:
            return
        self.last_status_push = now
        self._queue_payload(self.build_status_payload())

    # ─── Config helpers ───────────────────────────────────────────────────────

    def get_config_value(self, *path, default=None):
        cur = self.config
        for key in path:
            if not isinstance(cur, dict) or key not in cur:
                return default
            cur = cur[key]
        return cur

    def get_return_to_spawn_delay(self) -> float:
        raw = self.get_config_value("follow_behavior", "search_timeout", default=6)
        try:
            return max(1.0, float(raw))
        except (TypeError, ValueError):
            return 6.0

    # ─── State helpers ────────────────────────────────────────────────────────

    def set_bot_state(self, new_state: str, activity_msg: str = "",
                      audio_file: str = "none") -> None:
        if self.bot_state == new_state:
            return
        self.bot_state = new_state
        if activity_msg:
            self.send_activity_sync(activity_msg, audio_file != "none", audio_file)
        self.send_status_sync(force=True)

    def reset_runtime_tracking(self) -> None:
        self.bot_state                 = "starting"
        self.current_target            = None
        self.target_health             = 100
        self.health                    = 100
        self.uber_charge               = 0.0
        self.uber_ready                = False
        self.uber_active               = False
        self.uber_start_time           = None
        self.heal_button_held          = False
        self.last_environment_update   = 0.0
        self.last_status_push          = 0.0
        self.last_any_heal_target_time = 0.0
        self.last_heal_weapon_select   = 0.0
        self.last_spy_check_time       = 0.0

    # ─── Input helpers ────────────────────────────────────────────────────────

    def select_heal_weapon(self, force: bool = False) -> None:
        now = time.time()
        if force or now - self.last_heal_weapon_select >= 1.0:
            try:
                self.keyboard.tap("2")
            except Exception as exc:
                print(f"[select_heal_weapon] {exc}")
            self.last_heal_weapon_select = now

    def press_heal_beam(self) -> None:
        if not self.heal_button_held:
            try:
                self.mouse.press(Button.left)
            except Exception as exc:
                print(f"[press_heal_beam] {exc}")
                return
            self.heal_button_held = True

    def release_heal_beam(self) -> None:
        if self.heal_button_held:
            try:
                self.mouse.release(Button.left)
            except Exception as exc:
                print(f"[release_heal_beam] {exc}")
            self.heal_button_held = False

    def release_all_inputs(self) -> None:
        self.release_heal_beam()
        for key in ("w", "a", "s", "d"):
            try:
                self.keyboard.release(key)
            except Exception:
                pass
        try:
            self.keyboard.release(Key.space)
        except Exception:
            pass

    def _tap(self, key) -> None:
        try:
            self.keyboard.tap(key)
        except Exception as exc:
            print(f"[_tap] {exc}")

    def _release(self, key) -> None:
        try:
            self.keyboard.release(key)
        except Exception as exc:
            print(f"[_release] {exc}")

    # ─── WebSocket handler ────────────────────────────────────────────────────

    async def handle_ws(self, websocket) -> None:
        self.ws = websocket
        print("GUI connected.")
        try:
            async for message in websocket:
                data     = json.loads(message)
                msg_type = data.get("type")

                if msg_type == "config":
                    self.config      = data.get("config", data)
                    self.follow_mode = str(
                        self.get_config_value(
                            "passive_mode", "default_mode_on_startup",
                            default="active"
                        )
                    ).strip().lower()
                    print("Config received.")

                elif msg_type == "start":
                    if not self.running:
                        self.session_start = datetime.now()
                        self.running       = True
                        threading.Thread(
                            target=self.bot_main_loop, daemon=True
                        ).start()
                        await self.send_activity("Bot started!")
                        await websocket.send(json.dumps(self.build_status_payload()))
                    else:
                        await self.send_activity("Bot is already running.")

                elif msg_type == "set_follow_mode":
                    self.follow_mode = data.get("mode", "active")
                    await self.send_activity(
                        f"Follow mode → {self.follow_mode}", True, "mode_switch"
                    )

                elif msg_type == "stop":
                    self.running = False
                    self.release_all_inputs()
                    self.bot_state = "standby"
                    await self.send_activity("Bot stopped.")
                    await websocket.send(json.dumps(self.build_status_payload()))

                elif msg_type in {"ping", "test"}:
                    await websocket.send(json.dumps({
                        "type":    "pong",
                        "msg":     "Bot server ready",
                        "running": self.running,
                        "mode":    self.bot_state,
                        "uber":    int(round(self.uber_charge)),
                    }))

        except websockets.exceptions.ConnectionClosed:
            print("GUI disconnected. Awaiting new connection.")
        except Exception as exc:
            print(f"WebSocket error: {exc}")
        finally:
            self.ws = None

    # ─── Bot main loop ────────────────────────────────────────────────────────

    def bot_main_loop(self) -> None:
        self.reset_runtime_tracking()
        self.release_all_inputs()
        self.skip_intro()
        self.enter_spawn()
        self.equip_loadout()
        self.send_status_sync(force=True)

        vision_mode = self.vision is not None
        if vision_mode:
            self.send_activity_sync("Vision engine active – full HUD reading enabled.")
        else:
            self.send_activity_sync(
                "No vision engine – running in basic follow mode "
                "(W + heal beam held; medigun slot 2 selected)."
            )

        while self.running:
            self.check_cpu_temp()
            if self.cpu_overheat:
                self.release_heal_beam()
                self.set_bot_state("cooling_down", "CPU too hot. Pausing.")
                time.sleep(1)
                continue

            self.update_environment()
            self.handle_uber()

            timeout             = self.get_return_to_spawn_delay()
            time_without_target = time.time() - self.last_any_heal_target_time

            if self.current_target:
                self.set_bot_state("healing")
                self.select_heal_weapon()
                self.press_heal_beam()
                self._release("s")
                self._tap("w")
                if self.target_health and self.target_health > 142:
                    self._tap("s")

            elif not vision_mode:
                self.set_bot_state("healing")
                self.select_heal_weapon()
                self.press_heal_beam()
                self._release("s")
                self._tap("w")
                self.check_spies()

            elif time_without_target < timeout:
                self.set_bot_state("searching", "Scanning for a target…")
                self.release_heal_beam()
                self._tap("w")
                sweep = 8 if int(time_without_target * 4) % 2 == 0 else -8
                try:
                    self.mouse.move(sweep, 0)
                except Exception:
                    pass

            else:
                self.set_bot_state("returning_to_spawn",
                                   "No target. Returning to spawn.")
                self.release_heal_beam()
                self._release("w")
                self._tap("s")
                drift = 4 if int(time.time() * 3) % 2 == 0 else -4
                try:
                    self.mouse.move(drift, 0)
                except Exception:
                    pass

            if vision_mode:
                self.check_spies()
            self.send_status_sync()
            time.sleep(0.05)

        self.release_all_inputs()
        self.set_bot_state("standby")
        self.send_status_sync(force=True)

    # ─── Startup routines ─────────────────────────────────────────────────────

    def skip_intro(self) -> None:
        if not self.vision:
            time.sleep(3)
            return
        self.send_activity_sync("Checking for class-selection screen…")
        time.sleep(2)
        try:
            frame      = self.vision.capture_screen()
            brightness = frame.mean()
        except Exception:
            return
        if brightness < 60:
            h, w    = frame.shape[:2]
            medic_x = int(w * 0.62)
            medic_y = int(h * 0.55)
            try:
                self.mouse.position = (medic_x, medic_y)
                time.sleep(0.3)
                self.mouse.click(Button.left)
                time.sleep(0.4)
                self.mouse.click(Button.left)
            except Exception as exc:
                print(f"[skip_intro] mouse error: {exc}")
            self.send_activity_sync("Selected Medic class.")
            time.sleep(1)
        else:
            self.send_activity_sync("Already in-game – skipping class select.")

    def enter_spawn(self) -> None:
        if not self.vision:
            time.sleep(2)
            return
        self.send_activity_sync("Binding respawn key and respawning…")
        self._tap("`")
        time.sleep(0.5)
        for ch in "bind F9 kill":
            self._tap(ch)
            time.sleep(0.04)
        self._tap(Key.enter)
        time.sleep(0.2)
        self._tap("`")
        time.sleep(0.3)
        self._tap(Key.f9)
        time.sleep(3)
        self.send_activity_sync("Respawned at team spawn.")

    def equip_loadout(self) -> None:
        self.select_heal_weapon(force=True)
        time.sleep(0.2)

    # ─── Environment / HUD update ─────────────────────────────────────────────

    def update_environment(self) -> None:
        now = time.time()
        if now - self.last_environment_update < self.environment_update_interval:
            return
        self.last_environment_update = now
        if not self.vision:
            return
        try:
            snapshot = self.vision.read_hud_snapshot()
        except Exception as exc:
            print(f"[update_environment] Vision error: {exc}")
            return

        if not snapshot.ocr_available and not self.warned_about_ocr:
            self.warned_about_ocr = True
            self.send_activity_sync(
                "Tesseract OCR not found – HUD reading disabled. "
                "Install Tesseract to enable full auto-targeting."
            )

        if snapshot.health is not None:
            self.health = snapshot.health
        if snapshot.heal_target_health is not None:
            self.target_health = snapshot.heal_target_health
        if snapshot.uber_charge is not None:
            self.uber_charge = snapshot.uber_charge
        if snapshot.heal_target_name:
            self.current_target            = snapshot.heal_target_name
            self.last_any_heal_target_time = now
        else:
            self.current_target = None

        if self.health <= 0:
            self.send_activity_sync("Backstabbed!", True, "spy")

    # ─── Uber management ─────────────────────────────────────────────────────

    def handle_uber(self) -> None:
        if self.uber_active:
            if time.time() - self.uber_start_time > 8:
                self.uber_active = False
                self.send_activity_sync("Uber ended.", True, "uber_cooldown")
            return

        if self.uber_charge >= 100 and not self.uber_ready:
            self.uber_ready = True
            self.send_activity_sync("Uber ready!", True, "uber_ready")

        ub_behavior = self.config.get("uber_behavior", "Manual")
        threshold   = 30
        try:
            threshold = int(
                self.get_config_value(
                    "uber", "auto_pop_health_threshold", default=30
                )
            )
        except (TypeError, ValueError):
            pass

        if ub_behavior == "Auto-pop" and self.target_health < threshold and self.uber_ready:
            self.pop_uber()
        elif ub_behavior == "Suggest" and self.current_target and self.uber_ready:
            self.send_activity_sync("Suggesting Uber!", True, "uber_ready")

    def pop_uber(self) -> None:
        self.select_heal_weapon(force=True)
        try:
            self.mouse.click(Button.right)
        except Exception as exc:
            print(f"[pop_uber] {exc}")
        self.uber_ready      = False
        self.uber_active     = True
        self.uber_charge     = 0
        self.uber_start_time = time.time()
        self.send_activity_sync("Uber activated!", True, "uber_activated")

    # ─── Spy check ───────────────────────────────────────────────────────────

    def check_spies(self) -> None:
        freq = self.config.get("spy_check_frequency", 10)
        try:
            freq = max(1, int(freq))
        except (TypeError, ValueError):
            freq = 10

        now = time.time()
        if now - self.last_spy_check_time < freq:
            return

        self.last_spy_check_time = now
        flick = int(
            self.get_config_value(
                "spy_detection", "spy_check_camera_flick_speed", default=180
            )
        )
        try:
            self.mouse.move(flick, 0)
            time.sleep(0.05)
            self.mouse.move(-flick, 0)
        except Exception as exc:
            print(f"[check_spies] mouse error: {exc}")

    # ─── System ──────────────────────────────────────────────────────────────

    def check_cpu_temp(self) -> None:
        try:
            temps = psutil.sensors_temperatures()
            if temps and "coretemp" in temps:
                highest   = max(t.current for t in temps["coretemp"])
                threshold = float(
                    self.get_config_value(
                        "performance", "cpu_temperature_throttle_threshold",
                        default=85
                    )
                )
                self.cpu_overheat = highest > threshold
        except Exception:
            pass

    # ─── Server entry-point ───────────────────────────────────────────────────

    async def run_server(self) -> None:
        print("Bot WebSocket server running on port 8765")
        async with websockets.serve(self.handle_ws, "0.0.0.0", 8765):
            await asyncio.Future()

    def run(self) -> None:
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        self.loop.run_until_complete(self.run_server())


# ══════════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    bot = MedicAIBot()
    bot.run()