"""
vision_engine.py  –  MedicAI vision back-end
=============================================
Two public classes are exported:

  VisionEngine       – used by bot_server.py for HUD reading and screen capture.
                       Works in two modes:
                         • Full mode  – mss + cv2 + pytesseract installed.
                                        Reads HP / heal-target / Uber from the TF2 HUD.
                         • Stub mode  – dependencies missing or Tesseract not found.
                                        Returns empty HudSnapshot so the bot still runs
                                        (just without automatic target detection).

  MedicVisionClassic – legacy template-matching loop used by test_vision.py.
"""

from __future__ import annotations

import asyncio
import json
import time
from collections import deque
from dataclasses import dataclass, field
from typing import Optional

# ──────────────────────────────────────────────────────────────────────────────
# HudSnapshot  (what bot_server.update_environment() reads)
# ──────────────────────────────────────────────────────────────────────────────

@dataclass
class HudSnapshot:
    health: Optional[int] = None
    heal_target_health: Optional[int] = None
    uber_charge: Optional[float] = None
    heal_target_name: Optional[str] = None
    ocr_available: bool = False


# ──────────────────────────────────────────────────────────────────────────────
# VisionEngine
# ──────────────────────────────────────────────────────────────────────────────

class VisionEngine:
    """
    Screen-capture + HUD-reading back-end for bot_server.py.

    Dependency chain (all optional – the bot degrades gracefully):
        mss          – screen capture
        cv2/numpy    – image processing
        pytesseract  – OCR for HP / name / Uber text
    """

    # TF2 default 1080p HUD regions  (left, top, width, height)
    _REGION_HEALTH      = (45,  880, 120, 55)   # player HP  (bottom-left)
    _REGION_TARGET_HP   = (700, 840, 160, 50)   # heal-target HP bar text
    _REGION_TARGET_NAME = (620, 800, 320, 40)   # heal-target name
    _REGION_UBER        = (765, 890, 130, 35)   # Uber % text

    def __init__(self) -> None:
        self.sct   = None      # mss screen-capture handle
        self.model = None      # reserved for future YOLO integration
        self._ocr_available = False
        self._np  = None
        self._cv2 = None
        self._tess = None

        self._init_libs()

    # ── initialisation ──────────────────────────────────────────────────────

    def _init_libs(self) -> None:
        try:
            import mss as _mss
            self.sct = _mss.mss()
        except ImportError:
            return  # no screen capture – stub mode

        try:
            import numpy as _np
            import cv2 as _cv2
            self._np  = _np
            self._cv2 = _cv2
        except ImportError:
            return  # numpy/cv2 missing – capture works but no image processing

        try:
            import pytesseract as _tess
            _tess.get_tesseract_version()   # raises if Tesseract binary missing
            self._tess = _tess
            self._ocr_available = True
        except Exception:
            self._tess = None
            self._ocr_available = False

    # ── public API used by bot_server.py ────────────────────────────────────

    def capture_screen(self):
        """Return a BGR numpy array of the primary monitor, or a blank frame."""
        np = self._np
        cv2 = self._cv2

        if self.sct is None or np is None or cv2 is None:
            if np is not None:
                return np.zeros((1080, 1920, 3), dtype=np.uint8)
            try:
                import numpy as _np
                return _np.zeros((1080, 1920, 3), dtype=_np.uint8)
            except ImportError:
                return None

        monitor = self.sct.monitors[1]   # index 1 = primary display
        raw = self.sct.grab(monitor)
        frame = np.array(raw)
        return cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR)

    def read_hud_snapshot(self) -> HudSnapshot:
        """
        Read HP, heal-target HP, Uber %, and heal-target name from the TF2 HUD.
        Falls back to an empty HudSnapshot when OCR is unavailable.
        """
        snap = HudSnapshot(ocr_available=self._ocr_available)

        if not self._ocr_available:
            return snap

        frame = self.capture_screen()
        if frame is None:
            return snap

        snap.health            = self._read_int_region(frame, self._REGION_HEALTH)
        snap.heal_target_health= self._read_int_region(frame, self._REGION_TARGET_HP)
        snap.uber_charge       = self._read_float_region(frame, self._REGION_UBER)
        snap.heal_target_name  = self._read_text_region(frame, self._REGION_TARGET_NAME)

        return snap

    # ── private helpers ──────────────────────────────────────────────────────

    def _crop(self, frame, region):
        x, y, w, h = region
        return frame[y:y + h, x:x + w]

    def _ocr_int(self, roi) -> Optional[int]:
        cv2, np, tess = self._cv2, self._np, self._tess
        if roi is None or cv2 is None:
            return None
        gray    = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        _, bw   = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        cfg     = "--psm 8 --oem 1 -c tessedit_char_whitelist=0123456789"
        raw = tess.image_to_string(bw, config=cfg).strip()
        digits = "".join(ch for ch in raw if ch.isdigit())
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


# ──────────────────────────────────────────────────────────────────────────────
# MedicVisionClassic  (legacy – used by bot/test_vision.py)
# ──────────────────────────────────────────────────────────────────────────────

class MedicVisionClassic:
    """
    Template-matching vision loop used by test_vision.py.
    Detects the 'MEDIC!' call overlay and locks onto the nearest teammate.
    """

    def __init__(self, ws_sender) -> None:
        self.ws_sender = ws_sender

        self.resolution       = (1920, 1080)
        self.template_path    = "templates/medic_call.png"
        self.match_threshold  = 0.68

        self.spin_speed       = 6
        self.aim_sensitivity  = 0.58
        self.heal_offset_y    = +40

        self.priority_names: set = set()

        # FIX #3: guard numpy array initialisation – only create arrays when
        # numpy is actually available, otherwise default to None so callers
        # can check before using them.
        self.team_lower = None
        self.team_upper = None
        try:
            import numpy as np
            self.team_lower = np.array([95, 80, 80])
            self.team_upper = np.array([130, 255, 255])
        except ImportError:
            pass

        # Load template
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

        # State
        self.locked_target          = None
        self.target_history         = deque(maxlen=15)
        self.spinning               = True
        self.last_medic_time        = 0.0
        self.last_seen_target_time  = 0.0

        print("✅ MedicVisionClassic loaded")
        print(f"   Template: {self.template_path}  threshold: {self.match_threshold}")

    # ── helpers ──────────────────────────────────────────────────────────────

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
            self.priority_names = {
                n.lower().strip() for n in config_data["priority_names"]
            }
            print(f"Priority names: {self.priority_names}")

    def capture_screen(self):
        import mss, numpy as np, cv2
        with mss.mss() as sct:
            mon = {"top": 0, "left": 0,
                   "width": self.resolution[0], "height": self.resolution[1]}
            img = np.array(sct.grab(mon))
            return cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)

    def detect_medic_calls(self, frame):
        import cv2, numpy as np
        gray  = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        tmpl  = cv2.cvtColor(self.medic_template, cv2.COLOR_BGR2GRAY)
        res   = cv2.matchTemplate(gray, tmpl, cv2.TM_CCOEFF_NORMED)
        locs  = np.where(res >= self.match_threshold)
        calls = []
        for pt in zip(*locs[::-1]):
            cx = pt[0] + self.template_w // 2
            cy = pt[1] + self.template_h // 2 - 25
            calls.append((cx, cy))
        return calls

    def find_target_below(self, frame, cross_pos):
        # FIX #3: guard against uninitialised team_lower / team_upper which
        # would cause an AttributeError (NoneType has no attribute 'dtype')
        # when cv2.inRange is called.
        if self.team_lower is None or self.team_upper is None:
            return None

        import cv2
        x, y     = cross_pos
        roi_y1   = max(0, y + 25)
        roi_y2   = min(frame.shape[0], y + 240)
        roi_x1   = max(0, x - 80)
        roi_x2   = min(frame.shape[1], x + 80)

        if roi_y1 >= roi_y2 or roi_x1 >= roi_x2:
            return None

        roi     = frame[roi_y1:roi_y2, roi_x1:roi_x2]
        hsv     = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
        mask    = cv2.inRange(hsv, self.team_lower, self.team_upper)
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

    # ── main loop ────────────────────────────────────────────────────────────

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
                    cv2.putText(debug, f"MEDIC CALL {i + 1}",
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


# ── convenience entry-point ──────────────────────────────────────────────────

async def start_vision(ws_sender) -> None:
    vision = MedicVisionClassic(ws_sender)
    await vision.run()
