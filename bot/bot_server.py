"""
bot_server.py  –  MedicAI WebSocket bot server
================================================
Run directly:   python bot/bot_server.py
Or via start.bat / start_server.bat.

The GUI connects on ws://localhost:8765 and sends JSON messages to control
the bot.  The bot main-loop runs in a background thread so the async
WebSocket handler stays responsive.

Vision is *optional*: if mss / cv2 / pytesseract are not installed the bot
still runs – it holds W and the heal beam so it at least keeps moving forward
and firing the medigun.  Full auto-targeting requires Tesseract OCR to be
installed and the vision_engine dependencies present.
"""

import asyncio
import json
import time
import threading
from datetime import datetime

import psutil
import websockets
import websockets.exceptions

# ── optional vision & watchdog ───────────────────────────────────────────────
try:
    from vision_engine import VisionEngine
except (ImportError, Exception):
    VisionEngine = None

try:
    from watchdog import WatchdogProcess
except (ImportError, Exception):
    WatchdogProcess = None

from pynput.keyboard import Controller as KeyboardController, Key
from pynput.mouse    import Controller as MouseController, Button


# ─────────────────────────────────────────────────────────────────────────────

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

        # FIX #1: replace the broken time.time() % freq trick with a proper
        # last-check timestamp so spy checks actually fire on schedule.
        self.last_spy_check_time     = 0.0

        # Input controllers
        self.keyboard = KeyboardController()
        self.mouse    = MouseController()

        # Vision & watchdog (optional)
        self.vision   = None
        self.watchdog = WatchdogProcess() if WatchdogProcess else None

        if VisionEngine is not None:
            try:
                self.vision = VisionEngine()
            except Exception as exc:
                print(f"[vision] Could not init VisionEngine: {exc}")

    # ─────────────────────────────────────────────────────────────────────────
    # Messaging helpers
    # ─────────────────────────────────────────────────────────────────────────

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

    # ─────────────────────────────────────────────────────────────────────────
    # Config helpers
    # ─────────────────────────────────────────────────────────────────────────

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

    # ─────────────────────────────────────────────────────────────────────────
    # State helpers
    # ─────────────────────────────────────────────────────────────────────────

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
        self.last_spy_check_time       = 0.0  # FIX #1: reset spy check timer

    # ─────────────────────────────────────────────────────────────────────────
    # Input helpers
    # ─────────────────────────────────────────────────────────────────────────

    def select_heal_weapon(self, force: bool = False) -> None:
        now = time.time()
        if force or now - self.last_heal_weapon_select >= 1.0:
            # FIX #2: wrap pynput calls in try/except – they can throw if the
            # OS input subsystem is temporarily unavailable.
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
        """Safe wrapper around keyboard.tap()."""
        try:
            self.keyboard.tap(key)
        except Exception as exc:
            print(f"[_tap] {exc}")

    def _release(self, key) -> None:
        """Safe wrapper around keyboard.release()."""
        try:
            self.keyboard.release(key)
        except Exception as exc:
            print(f"[_release] {exc}")

    # ─────────────────────────────────────────────────────────────────────────
    # WebSocket handler
    # ─────────────────────────────────────────────────────────────────────────

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

    # ─────────────────────────────────────────────────────────────────────────
    # Bot main loop  (runs in a background daemon thread)
    # ─────────────────────────────────────────────────────────────────────────

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

            # ── update HUD state if vision available ──────────────────────
            self.update_environment()
            self.handle_uber()

            timeout               = self.get_return_to_spawn_delay()
            time_without_target   = time.time() - self.last_any_heal_target_time

            # ── movement / healing decision ───────────────────────────────
            if self.current_target:
                # OCR found a target – active healing mode
                self.set_bot_state("healing")
                self.select_heal_weapon()
                self.press_heal_beam()
                self._release("s")
                self._tap("w")
                if self.target_health and self.target_health > 142:
                    self._tap("s")   # back off when overhealed

            elif not vision_mode:
                # ── NO VISION: just hold W and the heal beam ──────────────
                self.set_bot_state("healing")
                self.select_heal_weapon()
                self.press_heal_beam()
                self._release("s")
                self._tap("w")
                self.check_spies()

            elif time_without_target < timeout:
                # Vision active but no target yet – scan
                self.set_bot_state("searching", "Scanning for a target…")
                self.release_heal_beam()
                self._tap("w")
                sweep = 8 if int(time_without_target * 4) % 2 == 0 else -8
                try:
                    self.mouse.move(sweep, 0)
                except Exception:
                    pass

            else:
                # Vision active, nobody seen for too long – drift back to spawn
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

    # ─────────────────────────────────────────────────────────────────────────
    # Startup routines
    # ─────────────────────────────────────────────────────────────────────────

    def skip_intro(self) -> None:
        """Click the Medic slot if the class-selection screen is detected."""
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
            h, w     = frame.shape[:2]
            medic_x  = int(w * 0.62)
            medic_y  = int(h * 0.55)
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
        """Bind F9 to 'kill' via the TF2 console and press it to respawn."""
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
        """Select the medigun (slot 2) on spawn."""
        self.select_heal_weapon(force=True)
        time.sleep(0.2)

    # ─────────────────────────────────────────────────────────────────────────
    # Environment / HUD update
    # ─────────────────────────────────────────────────────────────────────────

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

        # Warn once if OCR is unavailable
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

    # ─────────────────────────────────────────────────────────────────────────
    # Uber management
    # ─────────────────────────────────────────────────────────────────────────

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
                self.get_config_value("uber", "auto_pop_health_threshold", default=30)
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

    # ─────────────────────────────────────────────────────────────────────────
    # Spy check
    # ─────────────────────────────────────────────────────────────────────────

    def check_spies(self) -> None:
        # FIX #1: original code used `time.time() % freq < 0.1` which almost
        # never fires because time.time() returns a large Unix timestamp and
        # the fractional remainder only crosses 0 briefly.  Use a proper
        # elapsed-time check instead.
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

    # ─────────────────────────────────────────────────────────────────────────
    # System
    # ─────────────────────────────────────────────────────────────────────────

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

    # ─────────────────────────────────────────────────────────────────────────
    # Server entry-point
    # ─────────────────────────────────────────────────────────────────────────

    async def run_server(self) -> None:
        print("Bot WebSocket server running on port 8765")
        async with websockets.serve(self.handle_ws, "0.0.0.0", 8765):
            await asyncio.Future()   # run forever

    def run(self) -> None:
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        self.loop.run_until_complete(self.run_server())


# ─────────────────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    bot = MedicAIBot()
    bot.run()
