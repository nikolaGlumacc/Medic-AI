#!/usr/bin/env python3
"""
MedicAI Bot Server
Screen-capture + simulated input bot for TF2 Medic.
No memory reading, no injection — purely HSV vision + pynput/win32 inputs.
"""

import cv2
import numpy as np
import time
import threading
import queue
import logging
import json
import os
import pytesseract
import random
from dataclasses import dataclass, field
from enum import Enum
from typing import Optional, List, Tuple, Dict
from pathlib import Path
import mss
import win32api
import win32con
from flask import Flask, request, jsonify
import asyncio
import websockets
import psutil

# ── Optional input backends ──────────────────────────────────────────────────
try:
    import pydirectinput
    pydirectinput.PAUSE = 0.0
except ImportError:
    logging.warning("pydirectinput not installed — keyboard/mouse inputs disabled.")
    pydirectinput = None

try:
    from pynput.keyboard import Key, Controller as KeyboardController
    _keyboard = KeyboardController()
except ImportError:
    logging.warning("pynput not installed — keyboard inputs disabled.")
    _keyboard = None

# ── Logging ───────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s"
)
logger = logging.getLogger("MedicAI")


# ═══════════════════════════════════════════════════════════════════════════════
#  CONSTANTS / DEFAULTS
# ═══════════════════════════════════════════════════════════════════════════════

DEFAULTS: Dict = {
    # Connection
    "ws_port": 8765,
    "flask_port": 5000,

    # Aim / mouse
    "mouse_speed": 0.6,
    "smoothing": 0.12,
    "deadzone": 30,
    "max_move_px": 10,
    "horizontal_smoothing": 0.12,
    "vertical_smoothing": 0.12,
    "aim_vertical_offset_pct": 0.0,
    "aim_y_min_pct": 15.0,
    "aim_y_max_pct": 85.0,
    "max_dist": 450,

    # Follow / movement
    "follow_fwd_thresh": 180,
    "follow_back_thresh": 80,
    "follow_strafe_thresh": 100,
    "follow_enabled": True,
    "strafe_randomize": False,
    "backpedal_max_duration": 2.0,

    # Tracking
    "match_dist": 160,
    "grace_period": 1.5,
    "lock_grace": 0.8,
    "max_tracked_players": 8,

    # BLU HSV mask
    "blu_h_min": 95,  "blu_h_max": 125,
    "blu_s_min": 70,  "blu_s_max": 255,
    "blu_v_min": 80,  "blu_v_max": 255,

    # RED HSV mask (low)
    "red1_h_min": 0,  "red1_h_max": 8,
    "red1_s_min": 90, "red1_s_max": 255,
    "red1_v_min": 90, "red1_v_max": 255,

    # RED HSV mask (high)
    "red2_h_min": 172, "red2_h_max": 179,
    "red2_s_min": 90,  "red2_s_max": 255,
    "red2_v_min": 90,  "red2_v_max": 255,

    # Blob filters
    "min_area": 600,
    "min_aspect": 1.0,
    "max_aspect": 5.0,
    "max_blob_w_pct": 25.0,
    "max_blob_h_pct": 55.0,
    "min_convexity_ratio": 0.35,

    # HUD mask regions (fraction of screen)
    "hud_top_pct": 8.0,
    "hud_bottom_pct": 90.0,
    "hud_left_pct": 3.0,
    "hud_right_pct": 97.0,

    # Morphology
    "morph_open_kernel": 3,
    "morph_close_kernel": 5,

    # Uber detection
    "uber_roi_left_pct": 72.0,
    "uber_roi_top_pct": 89.5,
    "uber_roi_width_pct": 10.0,
    "uber_roi_height_pct": 3.0,
    "uber_h_min": 85,  "uber_h_max": 135,
    "uber_s_min": 30,  "uber_s_max": 255,
    "uber_v_min": 210, "uber_v_max": 255,
    "uber_full_thresh": 0.92,
    "uber_scale_factor": 2.5,

    # Timing
    "watchdog_timeout": 0.4,
    "uber_loop_interval": 0.08,
    "capture_loop_interval": 0.005,
    "controller_loop_interval": 0.01,

    # Humanization
    "max_aim_time_on_target": 0,
    "action_cooldown_min": 0,
    "action_cooldown_max": 0,
    "session_actions_per_minute_cap": 0,
    "idle_random_look_enabled": False,
    "idle_look_amplitude": 50,
    "periodic_weapon_switch": False,
    "micro_jitter_enabled": False,
    "micro_jitter_amplitude": 1,

    # Weapon / fire
    "current_weapon_slot": 2,
    "hold_fire_override": False,

    # Loadout switcher
    "match_threshold": 0.70,
    "equip_timeout": 15,
    "retry_delay": 0.25,
    "click_delay": 0.15,
    "menu_open_delay": 0.35,
    "menu_close_delay": 0.20,
    "scale_factors": [0.8, 0.9, 1.0, 1.1],

    # Bot behaviour
    "priority_players": [],
    "follow_distance": 50,
    "spy_check_frequency": 8,
    "idle_rotation_speed": 18,
    "scoreboard_check_frequency": 30,
    "tab_hold_duration": 0.25,
    "uber_pop_threshold": 95.0,
    "auto_uber": True,
    "retreat_health_threshold": 50,
    "cpu_throttle_threshold": 85,
    "screen_capture_fps_limit": 60,
    "tesseract_path": r"C:\Program Files\Tesseract-OCR\tesseract.exe",
    "team": "auto",

    # Debug
    "show_debug_window": False,
    "debug_log_level": "INFO",
}

CONFIG: Dict = dict(DEFAULTS)


def load_config() -> None:
    global CONFIG
    if os.path.exists("bot_config.json"):
        try:
            with open("bot_config.json") as f:
                CONFIG.update(json.load(f))
        except Exception as e:
            logger.error(f"Error loading config: {e}")


def save_config() -> None:
    try:
        with open("bot_config.json", "w") as f:
            json.dump(CONFIG, f, indent=4)
    except Exception as e:
        logger.error(f"Error saving config: {e}")


load_config()

# Tesseract path
_tess_path = CONFIG.get("tesseract_path", "")
if os.path.exists(_tess_path):
    pytesseract.pytesseract.tesseract_cmd = _tess_path


# ═══════════════════════════════════════════════════════════════════════════════
#  DATACLASSES
# ═══════════════════════════════════════════════════════════════════════════════

@dataclass
class HudSnapshot:
    health: Optional[int] = None
    uber_charge: float = 0.0
    heal_target_name: Optional[str] = None
    ocr_available: bool = False


@dataclass
class BlobResult:
    cx: int = 0
    cy: int = 0
    area: float = 0.0
    dist_to_center: float = 0.0


class BotState(Enum):
    SEARCHING  = "SEARCHING"
    HEALING    = "HEALING"
    FOLLOWING  = "FOLLOWING"
    MELEE      = "MELEE"
    RETREATING = "RETREATING"
    PASSIVE    = "PASSIVE"
    IDLE       = "IDLE"


# ═══════════════════════════════════════════════════════════════════════════════
#  VISION MODULE  (HSV + distance transform — no YOLO / transformers)
# ═══════════════════════════════════════════════════════════════════════════════

class Vision:
    """
    All screen-reading logic:
      - Team-colour blob detection via HSV masking + cv2.distanceTransform
      - Medic-call bubble detection (red cross icon)
      - OCR name reading at crosshair
      - Kill-feed OCR
      - Scoreboard OCR
      - Uber-meter fill reading
    """

    def __init__(self) -> None:
        # NOTE: mss handles are thread-local on Windows.
        # Never store an mss instance — always open a fresh context per capture call.
        self._ocr_ok = self._check_tesseract()

    # ── Internal helpers ──────────────────────────────────────────────────────

    def _check_tesseract(self) -> bool:
        try:
            pytesseract.get_tesseract_version()
            return True
        except Exception:
            logger.warning("Tesseract not found — OCR features disabled.")
            return False

    def capture(self) -> np.ndarray:
        """Always opens a fresh mss context — safe to call from any thread."""
        with mss.mss() as sct:
            img = np.array(sct.grab(sct.monitors[1]))
        return cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)

    def _build_team_mask(self, frame: np.ndarray) -> np.ndarray:
        """Build a binary mask for the own-team colour (BLU or RED)."""
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        team = CONFIG.get("team", "auto")

        if team in ("auto", "BLU"):
            lo = np.array([CONFIG["blu_h_min"], CONFIG["blu_s_min"], CONFIG["blu_v_min"]])
            hi = np.array([CONFIG["blu_h_max"], CONFIG["blu_s_max"], CONFIG["blu_v_max"]])
            mask = cv2.inRange(hsv, lo, hi)
        else:
            lo1 = np.array([CONFIG["red1_h_min"], CONFIG["red1_s_min"], CONFIG["red1_v_min"]])
            hi1 = np.array([CONFIG["red1_h_max"], CONFIG["red1_s_max"], CONFIG["red1_v_max"]])
            lo2 = np.array([CONFIG["red2_h_min"], CONFIG["red2_s_min"], CONFIG["red2_v_min"]])
            hi2 = np.array([CONFIG["red2_h_max"], CONFIG["red2_s_max"], CONFIG["red2_v_max"]])
            mask = cv2.inRange(hsv, lo1, hi1) | cv2.inRange(hsv, lo2, hi2)

        # Mask out HUD regions
        h, w = frame.shape[:2]
        top    = int(h * CONFIG["hud_top_pct"]    / 100)
        bottom = int(h * CONFIG["hud_bottom_pct"] / 100)
        left   = int(w * CONFIG["hud_left_pct"]   / 100)
        right  = int(w * CONFIG["hud_right_pct"]  / 100)
        hud_mask = np.zeros_like(mask)
        hud_mask[top:bottom, left:right] = 255
        mask = cv2.bitwise_and(mask, hud_mask)

        # Morphology
        ok = int(CONFIG["morph_open_kernel"])
        ck = int(CONFIG["morph_close_kernel"])
        if ok > 0:
            mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN,
                                    cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (ok, ok)))
        if ck > 0:
            mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE,
                                    cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (ck, ck)))
        return mask

    def _filter_blobs(self, mask: np.ndarray, frame_shape: Tuple) -> List[BlobResult]:
        """Extract valid player blobs using contour + convexity filters."""
        h, w = frame_shape[:2]
        cx_screen, cy_screen = w // 2, h // 2

        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        blobs: List[BlobResult] = []

        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < CONFIG["min_area"]:
                continue

            x, y, bw, bh = cv2.boundingRect(cnt)
            aspect = bh / max(bw, 1)
            if not (CONFIG["min_aspect"] <= aspect <= CONFIG["max_aspect"]):
                continue
            if bw / w * 100 > CONFIG["max_blob_w_pct"]:
                continue
            if bh / h * 100 > CONFIG["max_blob_h_pct"]:
                continue

            hull = cv2.convexHull(cnt)
            hull_area = cv2.contourArea(hull)
            if hull_area > 0 and area / hull_area < CONFIG["min_convexity_ratio"]:
                continue

            M = cv2.moments(cnt)
            if M["m00"] == 0:
                continue
            bcx = int(M["m10"] / M["m00"])
            bcy = int(M["m01"] / M["m00"])
            dist = np.hypot(bcx - cx_screen, bcy - cy_screen)
            blobs.append(BlobResult(cx=bcx, cy=bcy, area=area, dist_to_center=dist))

        return blobs

    # ── Public API ────────────────────────────────────────────────────────────

    def find_best_target(self, frame: np.ndarray) -> Optional[BlobResult]:
        """
        Use HSV masking → distance transform to find the nearest/best target.
        Returns the BlobResult closest to screen centre within max_dist.
        """
        mask = self._build_team_mask(frame)

        # Distance transform: find the 'centre mass' of each blob
        dist_transform = cv2.distanceTransform(mask, cv2.DIST_L2, 5)
        cv2.normalize(dist_transform, dist_transform, 0, 1.0, cv2.NORM_MINMAX)

        blobs = self._filter_blobs(mask, frame.shape)
        if not blobs:
            return None

        # Pick closest to crosshair within max_dist
        valid = [b for b in blobs if b.dist_to_center <= CONFIG["max_dist"]]
        if not valid:
            return None
        return min(valid, key=lambda b: b.dist_to_center)

    def find_medic_bubble(self, frame: np.ndarray) -> Optional[Tuple[int, int]]:
        """
        Detect the red-cross medic-call bubble visible through walls.
        Returns (cx, cy) screen position or None.
        """
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        # Red cross is a bright saturated red
        lo1 = np.array([0,   150, 150])
        hi1 = np.array([8,   255, 255])
        lo2 = np.array([170, 150, 150])
        hi2 = np.array([179, 255, 255])
        mask = cv2.inRange(hsv, lo1, hi1) | cv2.inRange(hsv, lo2, hi2)

        k = cv2.getStructuringElement(cv2.MORPH_CROSS, (5, 5))
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, k)

        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        best = None
        best_score = 0.0
        h, w = frame.shape[:2]

        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < 60 or area > (w * h * 0.005):
                continue
            x, y, bw, bh = cv2.boundingRect(cnt)
            # Cross-shaped: aspect roughly 1:1
            aspect = max(bw, bh) / max(min(bw, bh), 1)
            if aspect > 2.5:
                continue
            if area > best_score:
                best_score = area
                M = cv2.moments(cnt)
                if M["m00"]:
                    best = (int(M["m10"] / M["m00"]), int(M["m01"] / M["m00"]))

        return best

    def read_name_at_crosshair(self, frame: np.ndarray) -> str:
        """OCR the player name displayed below the crosshair when healing."""
        if not self._ocr_ok:
            return ""
        h, w = frame.shape[:2]
        # Name appears roughly 5-15% below centre
        y1 = int(h * 0.50)
        y2 = int(h * 0.60)
        x1 = int(w * 0.35)
        x2 = int(w * 0.65)
        roi = frame[y1:y2, x1:x2]
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 160, 255, cv2.THRESH_BINARY)
        try:
            text = pytesseract.image_to_string(thresh, config="--psm 7").strip()
            return text
        except Exception:
            return ""

    def read_kill_feed(self, frame: np.ndarray) -> List[Tuple[str, str]]:
        """OCR the top-right kill feed. Returns list of (killer, victim) pairs."""
        if not self._ocr_ok:
            return []
        h, w = frame.shape[:2]
        roi = frame[0:int(h * 0.25), int(w * 0.60):]
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 140, 255, cv2.THRESH_BINARY)
        try:
            raw = pytesseract.image_to_string(thresh, config="--psm 6").strip()
        except Exception:
            return []
        pairs: List[Tuple[str, str]] = []
        for line in raw.splitlines():
            parts = line.split()
            if len(parts) >= 2:
                pairs.append((parts[0], parts[-1]))
        return pairs

    def read_scoreboard_names(self, frame: np.ndarray) -> Dict[str, str]:
        """
        OCR scoreboard (called while Tab is held).
        Returns {player_name: 'alive'|'dead'}.
        Grayed-out text = dead.
        """
        if not self._ocr_ok:
            return {}
        h, w = frame.shape[:2]
        roi = frame[int(h * 0.15):int(h * 0.90), int(w * 0.15):int(w * 0.85)]
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)

        # Bright pixels = alive, dim = dead
        _, bright = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY)
        _, dim    = cv2.threshold(gray, 80,  255, cv2.THRESH_BINARY)
        dim_only  = cv2.bitwise_and(dim, cv2.bitwise_not(bright))

        results: Dict[str, str] = {}
        try:
            alive_text = pytesseract.image_to_string(bright, config="--psm 6").strip()
            dead_text  = pytesseract.image_to_string(dim_only, config="--psm 6").strip()
            for name in alive_text.splitlines():
                name = name.strip()
                if name:
                    results[name] = "alive"
            for name in dead_text.splitlines():
                name = name.strip()
                if name and name not in results:
                    results[name] = "dead"
        except Exception:
            pass
        return results

    def read_uber_charge(self, frame: np.ndarray) -> float:
        """
        Read uber-charge fill percentage (0–100) via HSV on the uber bar ROI.
        """
        h, w = frame.shape[:2]
        x1 = int(w * CONFIG["uber_roi_left_pct"]   / 100)
        y1 = int(h * CONFIG["uber_roi_top_pct"]    / 100)
        x2 = x1 + int(w * CONFIG["uber_roi_width_pct"]  / 100)
        y2 = y1 + int(h * CONFIG["uber_roi_height_pct"] / 100)
        x1, y1 = max(0, x1), max(0, y1)
        x2, y2 = min(w, x2), min(h, y2)
        if x2 <= x1 or y2 <= y1:
            return 0.0

        roi = frame[y1:y2, x1:x2]
        sf  = CONFIG.get("uber_scale_factor", 2.5)
        roi = cv2.resize(roi, (int(roi.shape[1] * sf), int(roi.shape[0] * sf)),
                         interpolation=cv2.INTER_LINEAR)
        hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)

        lo = np.array([CONFIG["uber_h_min"], CONFIG["uber_s_min"], CONFIG["uber_v_min"]])
        hi = np.array([CONFIG["uber_h_max"], CONFIG["uber_s_max"], CONFIG["uber_v_max"]])
        mask = cv2.inRange(hsv, lo, hi)

        total = mask.size
        filled = cv2.countNonZero(mask)
        pct = (filled / total) * 100.0 if total else 0.0

        # Clamp: if pct > uber_full_thresh * 100 → treat as full
        if pct / 100.0 >= CONFIG["uber_full_thresh"]:
            pct = 100.0
        return round(pct, 1)

    def read_hud_snapshot(self, frame: Optional[np.ndarray] = None) -> HudSnapshot:
        """Read health + uber + heal-target name in one pass."""
        snap = HudSnapshot(ocr_available=self._ocr_ok)
        if frame is None:
            frame = self.capture()

        snap.uber_charge = self.read_uber_charge(frame)

        if self._ocr_ok:
            try:
                h, w = frame.shape[:2]
                # Health: bottom-left corner
                hp_roi = frame[int(h * 0.88):int(h * 0.96), int(w * 0.02):int(w * 0.12)]
                gray   = cv2.cvtColor(hp_roi, cv2.COLOR_BGR2GRAY)
                _, thr = cv2.threshold(gray, 140, 255, cv2.THRESH_BINARY)
                hp_txt = pytesseract.image_to_string(
                    thr, config="--psm 8 -c tessedit_char_whitelist=0123456789"
                ).strip()
                if hp_txt.isdigit():
                    snap.health = int(hp_txt)
            except Exception:
                pass

            snap.heal_target_name = self.read_name_at_crosshair(frame) or None

        return snap

    def detect_own_team(self, frame: Optional[np.ndarray] = None) -> str:
        """
        Sample pixels around the HUD health-cross colour to guess team.
        Returns 'BLU' or 'RED'.
        """
        if frame is None:
            frame = self.capture()
        h, w = frame.shape[:2]
        # Sample bottom-left HUD area
        sample = frame[int(h * 0.85):int(h * 0.98), int(w * 0.01):int(w * 0.08)]
        hsv = cv2.cvtColor(sample, cv2.COLOR_BGR2HSV)
        blu_px = cv2.countNonZero(cv2.inRange(
            hsv,
            np.array([95,  60, 60]),
            np.array([130, 255, 255])
        ))
        red_px = cv2.countNonZero(cv2.inRange(
            hsv,
            np.array([0,  80, 80]),
            np.array([10, 255, 255])
        ))
        return "BLU" if blu_px >= red_px else "RED"


# ═══════════════════════════════════════════════════════════════════════════════
#  CONTROLLER  (mouse + keyboard inputs)
# ═══════════════════════════════════════════════════════════════════════════════

class Controller:
    """Wraps win32api mouse movement and pydirectinput / pynput key presses."""

    def __init__(self) -> None:
        self._heal_held = False
        self._prev_dx = 0.0
        self._prev_dy = 0.0

    # ── Mouse ─────────────────────────────────────────────────────────────────

    def move_mouse(self, dx: float, dy: float) -> None:
        hs = CONFIG["horizontal_smoothing"]
        vs = CONFIG["vertical_smoothing"]
        dz = CONFIG["deadzone"]
        spd = CONFIG["mouse_speed"]
        mx  = CONFIG["max_move_px"]

        sdx = self._prev_dx * (1 - hs) + dx * hs
        sdy = self._prev_dy * (1 - vs) + dy * vs
        self._prev_dx, self._prev_dy = sdx, sdy

        if abs(sdx) < dz and abs(sdy) < dz:
            return

        move_x = int(np.clip(sdx * spd, -mx, mx))
        move_y = int(np.clip(sdy * spd, -mx, mx))
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, move_x, move_y, 0, 0)

        if CONFIG.get("micro_jitter_enabled"):
            amp = CONFIG["micro_jitter_amplitude"]
            jx = random.randint(-amp, amp)
            jy = random.randint(-amp, amp)
            win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, jx, jy, 0, 0)

    def aim_at_blob(self, blob: BlobResult, frame_shape: Tuple) -> None:
        h, w = frame_shape[:2]
        cx, cy = w // 2, h // 2
        offset_y = int(h * CONFIG["aim_vertical_offset_pct"] / 100)
        target_y = np.clip(
            blob.cy - offset_y,
            int(h * CONFIG["aim_y_min_pct"] / 100),
            int(h * CONFIG["aim_y_max_pct"] / 100)
        )
        dx = blob.cx - cx
        dy = target_y - cy
        self.move_mouse(float(dx), float(dy))

    def hold_m1(self) -> None:
        if not self._heal_held:
            if pydirectinput:
                pydirectinput.mouseDown(button="left")
            self._heal_held = True

    def release_m1(self) -> None:
        if self._heal_held:
            if pydirectinput:
                pydirectinput.mouseUp(button="left")
            self._heal_held = False

    def click_m1(self) -> None:
        if pydirectinput:
            pydirectinput.click(button="left")

    def pop_uber(self) -> None:
        if pydirectinput:
            pydirectinput.click(button="right")

    def press_key(self, key: str) -> None:
        if pydirectinput:
            pydirectinput.press(key)

    def hold_key(self, key: str) -> None:
        if pydirectinput:
            pydirectinput.keyDown(key)

    def release_key(self, key: str) -> None:
        if pydirectinput:
            pydirectinput.keyUp(key)

    def type_string(self, text: str) -> None:
        """Type a string correctly including uppercase / special chars."""
        if _keyboard:
            _keyboard.type(text)
        elif pydirectinput:
            for ch in text:
                try:
                    pydirectinput.press(ch)
                except Exception:
                    pass

    def flick_spy_check(self) -> None:
        spd = int(CONFIG.get("spy_check_camera_flick_speed", 180))
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, spd * 10, 0, 0, 0)
        time.sleep(0.08)
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, -spd * 10, 0, 0, 0)

    def switch_weapon(self, slot: int) -> None:
        self.press_key(str(slot))
        time.sleep(0.05)

    def cleanup(self) -> None:
        self.release_m1()


# ═══════════════════════════════════════════════════════════════════════════════
#  LOADOUT MANAGER
# ═══════════════════════════════════════════════════════════════════════════════

class LoadoutManager:
    """
    Template-matching based weapon equip.
    weapon PNGs go in   bot/weapons/<name>.png
    """

    WEAPONS_DIR = Path(__file__).parent / "weapons"

    def __init__(self, controller: Controller, vision: Vision) -> None:
        self._ctrl = controller
        self._vision = vision
        self._templates: Dict[str, np.ndarray] = {}
        self._cancel_flag = threading.Event()
        self._equip_thread: Optional[threading.Thread] = None
        self._ensure_weapons_dir()
        self._load_templates()

    def _ensure_weapons_dir(self) -> None:
        if not self.WEAPONS_DIR.exists():
            self.WEAPONS_DIR.mkdir(parents=True)
            logger.info(f"Created weapons/ folder at {self.WEAPONS_DIR}")

    def _load_templates(self) -> None:
        self._templates.clear()
        for png in self.WEAPONS_DIR.glob("*.png"):
            img = cv2.imread(str(png))
            if img is not None:
                self._templates[png.stem.lower()] = img
                logger.info(f"Loaded weapon template: {png.stem}")
        if not self._templates:
            logger.warning("weapons/ folder is empty — loadout switching unavailable.")

    @property
    def available_weapons(self) -> List[str]:
        return sorted(self._templates.keys())

    def _find_weapon_on_screen(
        self,
        frame: np.ndarray,
        weapon_name: str
    ) -> Optional[Tuple[int, int]]:
        tmpl = self._templates.get(weapon_name.lower())
        if tmpl is None:
            return None

        best_val = 0.0
        best_loc: Optional[Tuple[int, int]] = None

        for sf in CONFIG.get("scale_factors", [0.8, 0.9, 1.0, 1.1]):
            scaled = cv2.resize(tmpl, (0, 0), fx=sf, fy=sf)
            th, tw = scaled.shape[:2]
            if th > frame.shape[0] or tw > frame.shape[1]:
                continue
            result = cv2.matchTemplate(frame, scaled, cv2.TM_CCOEFF_NORMED)
            _, max_val, _, max_loc = cv2.minMaxLoc(result)
            if max_val > best_val:
                best_val = max_val
                best_loc = (max_loc[0] + tw // 2, max_loc[1] + th // 2)

        thresh = CONFIG.get("match_threshold", 0.70)
        return best_loc if best_val >= thresh else None

    def equip(self, weapon_name: str, bot_ref=None) -> bool:
        """Blocking equip. Returns True on success."""
        weapon_name = weapon_name.lower()
        if weapon_name not in self._templates:
            logger.warning(f"No template for '{weapon_name}' — aborting equip.")
            return False

        if bot_ref is not None:
            bot_ref._equip_in_progress = True

        self._cancel_flag.clear()
        deadline = time.time() + CONFIG.get("equip_timeout", 15)
        ok = False

        try:
            # Open loadout menu
            self._ctrl.press_key("m")
            time.sleep(CONFIG.get("menu_open_delay", 0.35))

            while time.time() < deadline and not self._cancel_flag.is_set():
                frame = self._vision.capture()

                # Confirm target weapon is equipped
                target_loc = self._find_weapon_on_screen(frame, weapon_name)
                if target_loc:
                    logger.info(f"Weapon '{weapon_name}' confirmed equipped.")
                    ok = True
                    break

                # Click where the target weapon appears
                if target_loc is None:
                    # Scan all templates to find target
                    for wname in self._templates:
                        loc = self._find_weapon_on_screen(frame, wname)
                        if loc:
                            win32api.SetCursorPos(loc)
                            time.sleep(CONFIG.get("click_delay", 0.15))
                            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
                            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
                            time.sleep(CONFIG.get("click_delay", 0.15))
                            break

                time.sleep(CONFIG.get("retry_delay", 0.25))

            # Close loadout menu
            self._ctrl.press_key("m")
            time.sleep(CONFIG.get("menu_close_delay", 0.20))

        except Exception as e:
            logger.error(f"Equip error: {e}")
        finally:
            if bot_ref is not None:
                bot_ref._equip_in_progress = False

        return ok

    def equip_async(self, weapon_name: str, bot_ref=None) -> None:
        """Fire-and-forget daemon thread equip."""
        t = threading.Thread(
            target=self.equip, args=(weapon_name, bot_ref), daemon=True
        )
        t.start()
        self._equip_thread = t

    def cancel(self) -> None:
        self._cancel_flag.set()


# ═══════════════════════════════════════════════════════════════════════════════
#  BOT BRAIN
# ═══════════════════════════════════════════════════════════════════════════════

class MedicBot:
    """
    Main bot logic:
      - Capture loop feeds frames
      - Brain loop runs state machine
      - WebSocket broadcasts status to GUI
      - Flask serves config/command API
    """

    def __init__(self) -> None:
        self.running = False
        self.state = BotState.SEARCHING
        self.team: str = CONFIG.get("team", "auto")

        # Sub-modules
        self.vision     = Vision()
        self.ctrl       = Controller()
        self.loadout    = LoadoutManager(self.ctrl, self.vision)

        # State
        self.uber_pct: float = 0.0
        self.my_health: Optional[int] = None
        self.current_target: Optional[str] = None
        self.session_start = time.time()
        self.melee_kills = 0

        self._equip_in_progress = False
        self._cpu_throttled = False
        self._last_spy_check = 0.0
        self._last_scoreboard_check = 0.0
        self._last_frame: Optional[np.ndarray] = None
        self._last_known_dir: Tuple[int, int] = (0, 0)
        self._search_since: float = 0.0
        self._lost_target_since: Optional[float] = None

        # WebSocket
        self._ws_clients: set = set()
        self._ws_loop: Optional[asyncio.AbstractEventLoop] = None

        # Flask
        self.app = Flask(__name__)
        self._register_routes()

    # ── Lifecycle ─────────────────────────────────────────────────────────────

    def start(self) -> None:
        if self.running:
            return
        self.running = True
        self.session_start = time.time()
        threading.Thread(target=self._capture_loop,   daemon=True).start()
        threading.Thread(target=self._brain_loop,     daemon=True).start()
        threading.Thread(target=self._cpu_temp_loop,  daemon=True).start()
        threading.Thread(target=self._scoreboard_loop, daemon=True).start()
        logger.info("MedicBot started.")
        self._broadcast({"type": "activity", "msg": "Bot started.", "audio": False})

    def stop(self) -> None:
        self.running = False
        self.ctrl.cleanup()
        logger.info("MedicBot stopped.")
        self._broadcast({"type": "activity", "msg": "Bot stopped.", "audio": False})

    # ── Capture loop ──────────────────────────────────────────────────────────

    def _capture_loop(self) -> None:
        interval = CONFIG.get("capture_loop_interval", 0.005)
        with mss.mss() as sct:
            mon = sct.monitors[1]
            while self.running:
                t0 = time.time()
                img = np.array(sct.grab(mon))
                self._last_frame = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)
                sleep = interval - (time.time() - t0)
                if sleep > 0:
                    time.sleep(sleep)

    # ── Brain loop ────────────────────────────────────────────────────────────

    def _brain_loop(self) -> None:
        interval = CONFIG.get("controller_loop_interval", 0.01)
        logger.info("Brain loop started.")

        while self.running:
            t0 = time.time()

            if self._equip_in_progress:
                time.sleep(0.05)
                continue

            frame = self._last_frame
            if frame is None:
                time.sleep(interval)
                continue

            # Read HUD
            snap = self.vision.read_hud_snapshot(frame)
            self.uber_pct  = snap.uber_charge
            if snap.health is not None:
                self.my_health = snap.health

            # Broadcast status
            self._broadcast_status()

            # State machine
            self._tick(frame, snap)

            # Spy check (proper timestamp, not modulo)
            freq = max(1, int(CONFIG.get("spy_check_frequency", 8)))
            if time.time() - self._last_spy_check >= freq:
                self._last_spy_check = time.time()
                self.ctrl.flick_spy_check()
                self._broadcast_activity("Spy check.")

            elapsed = time.time() - t0
            sleep = (interval * (2 if self._cpu_throttled else 1)) - elapsed
            if sleep > 0:
                time.sleep(sleep)

    def _tick(self, frame: np.ndarray, snap: HudSnapshot) -> None:
        blob = self.vision.find_best_target(frame)

        if blob is not None:
            # Aim toward blob
            self.ctrl.aim_at_blob(blob, frame.shape)
            self.ctrl.hold_m1()

            # OCR name
            name = self.vision.read_name_at_crosshair(frame).strip()
            priorities = [p.strip().lower() for p in CONFIG.get("priority_players", [])]

            if priorities:
                if name.lower() in priorities:
                    self.current_target = name
                    self.state = BotState.HEALING
                    self._lost_target_since = None

                    if CONFIG.get("follow_enabled", True):
                        self._follow_blob(blob, frame.shape)

                    if self.uber_pct >= CONFIG.get("uber_pop_threshold", 95.0) and \
                            CONFIG.get("auto_uber", True):
                        self.ctrl.pop_uber()
                        self._broadcast_activity("Uber activated!", audio=True)
                else:
                    # Not priority — drop and search
                    self.ctrl.release_m1()
                    self.current_target = None
                    self.state = BotState.SEARCHING
            else:
                # No priority list → heal anyone, no follow
                self.current_target = name or "unknown"
                self.state = BotState.HEALING
                self._lost_target_since = None

                if self.uber_pct >= CONFIG.get("uber_pop_threshold", 95.0) and \
                        CONFIG.get("auto_uber", True):
                    self.ctrl.pop_uber()
                    self._broadcast_activity("Uber activated!", audio=True)

        else:
            # No blob found
            self.ctrl.release_m1()
            now = time.time()

            if self._lost_target_since is None and self.current_target:
                self._lost_target_since = now
                self._broadcast_activity("Lost target — searching.", audio=True)

            self.current_target = None

            if self._lost_target_since and \
                    now - self._lost_target_since > CONFIG.get("grace_period", 1.5):
                self.state = BotState.SEARCHING
                self._search(frame)

    def _follow_blob(self, blob: BlobResult, frame_shape: Tuple) -> None:
        h, w = frame_shape[:2]
        cx = w // 2
        dx = blob.cx - cx
        fwd_thresh  = CONFIG.get("follow_fwd_thresh",    180)
        back_thresh = CONFIG.get("follow_back_thresh",    80)
        strafe_thresh = CONFIG.get("follow_strafe_thresh", 100)

        if abs(dx) > strafe_thresh:
            self.ctrl.hold_key("d" if dx > 0 else "a")
            self.ctrl.release_key("a" if dx > 0 else "d")
        else:
            self.ctrl.release_key("a")
            self.ctrl.release_key("d")

        if blob.dist_to_center > fwd_thresh:
            self.ctrl.hold_key("w")
            self.ctrl.release_key("s")
        elif blob.dist_to_center < back_thresh:
            self.ctrl.hold_key("s")
            self.ctrl.release_key("w")
        else:
            self.ctrl.release_key("w")
            self.ctrl.release_key("s")

    def _search(self, frame: np.ndarray) -> None:
        """Rotate slowly and look for medic bubble when no target."""
        # Check if we've been searching too long
        if self._search_since == 0:
            self._search_since = time.time()
        elif time.time() - self._search_since > 15:
            self._search_since = 0
            self._broadcast_activity("Search timeout — returning to spawn.")
            self.ctrl.press_key("w")  # walk toward spawn
            return

        # Check for medic bubble
        bubble = self.vision.find_medic_bubble(frame)
        if bubble:
            bx, by = bubble
            h, w = frame.shape[:2]
            dx = bx - w // 2
            self.ctrl.move_mouse(float(dx) * 0.3, 0.0)
            self.ctrl.hold_key("w")
            self._broadcast_activity("Medic bubble detected — navigating.")
        else:
            # Slow rotation scan
            spd = CONFIG.get("idle_rotation_speed", 18)
            self.ctrl.move_mouse(float(spd), 0.0)
            self.ctrl.release_key("w")

    # ── Scoreboard loop ───────────────────────────────────────────────────────

    def _scoreboard_loop(self) -> None:
        while self.running:
            freq = CONFIG.get("scoreboard_check_frequency", 30)
            time.sleep(freq)
            if not self.running:
                break

            self._last_scoreboard_check = time.time()
            if _keyboard:
                _keyboard.press(Key.tab)
                time.sleep(CONFIG.get("tab_hold_duration", 0.25))
                frame = self.vision.capture()
                _keyboard.release(Key.tab)

                names = self.vision.read_scoreboard_names(frame)
                if CONFIG.get("auto_whitelist_detection") and names:
                    priorities = [p.lower() for p in CONFIG.get("priority_players", [])]
                    for name in names:
                        if name.lower() in priorities:
                            logger.info(f"Confirmed on scoreboard: {name} = {names[name]}")
                self._broadcast_activity("Scoreboard checked.")

    # ── CPU temp loop ─────────────────────────────────────────────────────────

    def _cpu_temp_loop(self) -> None:
        while self.running:
            try:
                temps = psutil.sensors_temperatures() or {}
                core_temp = 50.0
                for key in ("coretemp", "cpu_thermal", "k10temp"):
                    if key in temps and temps[key]:
                        core_temp = temps[key][0].current
                        break
                threshold = CONFIG.get("cpu_throttle_threshold", 85)
                self._cpu_throttled = core_temp > threshold
            except Exception:
                self._cpu_throttled = False
            time.sleep(5)

    # ── Team detection ────────────────────────────────────────────────────────

    def detect_own_team(self) -> str:
        team = self.vision.detect_own_team()
        self.team = team
        CONFIG["team"] = team
        logger.info(f"Detected team: {team}")
        return team

    # ── WebSocket ─────────────────────────────────────────────────────────────

    def _broadcast(self, payload: dict) -> None:
        if not self._ws_loop or not self._ws_clients:
            return
        msg = json.dumps(payload)
        asyncio.run_coroutine_threadsafe(self._ws_send_all(msg), self._ws_loop)

    async def _ws_send_all(self, msg: str) -> None:
        dead = set()
        for ws in list(self._ws_clients):
            try:
                await ws.send(msg)
            except Exception:
                dead.add(ws)
        self._ws_clients -= dead

    def _broadcast_activity(self, msg: str, audio: bool = False) -> None:
        self._broadcast({"type": "activity", "msg": msg, "audio": audio})

    def _broadcast_status(self) -> None:
        self._broadcast({
            "type": "status",
            "running": self.running,
            "state": self.state.value,
            "uber": self.uber_pct,
            "my_health": self.my_health,
            "current_target": self.current_target,
            "session_seconds": int(time.time() - self.session_start),
            "melee_kills": self.melee_kills,
        })

    async def _ws_handler(self, websocket) -> None:
        self._ws_clients.add(websocket)
        try:
            async for raw in websocket:
                try:
                    msg = json.loads(raw)
                    action = msg.get("action") or msg.get("type")
                    if action == "start":
                        self.start()
                    elif action == "stop":
                        self.stop()
                    elif action == "config":
                        cfg = msg.get("config", {})
                        CONFIG.update(cfg)
                        save_config()
                except Exception as e:
                    logger.error(f"WS message error: {e}")
        except websockets.exceptions.ConnectionClosed:
            pass
        finally:
            self._ws_clients.discard(websocket)

    def start_ws(self, host: str = "0.0.0.0", port: int = 8765) -> None:
        self._ws_loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self._ws_loop)

        async def _serve():
            async with websockets.serve(self._ws_handler, host, port):
                logger.info(f"WebSocket server on ws://{host}:{port}")
                await asyncio.Future()

        self._ws_loop.run_until_complete(_serve())

    # ── Flask routes ──────────────────────────────────────────────────────────

    def _register_routes(self) -> None:
        app = self.app

        @app.route("/status", methods=["GET"])
        def status():
            return jsonify({
                "running": self.running,
                "state": self.state.value,
                "uber_pct": self.uber_pct,
                "my_health": self.my_health,
                "current_target": self.current_target,
                "session_seconds": int(time.time() - self.session_start),
                "melee_kills": self.melee_kills,
                "team": self.team,
            })

        @app.route("/start", methods=["POST"])
        def start():
            self.start()
            return jsonify({"status": "started"})

        @app.route("/stop", methods=["POST"])
        def stop():
            self.stop()
            return jsonify({"status": "stopped"})

        @app.route("/config", methods=["GET", "POST"])
        def config():
            if request.method == "POST":
                data = request.get_json(force=True, silent=True) or {}
                CONFIG.update(data)
                save_config()
                logger.info(f"Config patched: {list(data.keys())}")
            return jsonify(CONFIG)

        @app.route("/set_follow_mode", methods=["POST"])
        def set_follow_mode():
            data = request.get_json(force=True, silent=True) or {}
            mode = data.get("mode", "active")
            CONFIG["follow_enabled"] = (mode == "active")
            self._broadcast_activity(f"Follow mode: {mode}")
            return jsonify({"mode": mode})

        @app.route("/weapons", methods=["GET"])
        def weapons():
            return jsonify(self.loadout.available_weapons)

        @app.route("/equip_weapon", methods=["POST"])
        def equip_weapon():
            data = request.get_json(force=True, silent=True) or {}
            name = data.get("weapon", "")
            if not name:
                return jsonify({"error": "weapon name required"}), 400
            self.loadout.equip_async(name, bot_ref=self)
            self._broadcast_activity(f"Equip requested: {name}")
            return jsonify({"status": "equipping", "weapon": name})

        @app.route("/detect_team", methods=["POST"])
        def detect_team():
            team = self.detect_own_team()
            return jsonify({"team": team})

        @app.route("/debug_snapshot", methods=["POST"])
        def debug_snapshot():
            debug_dir = Path("debug")
            debug_dir.mkdir(exist_ok=True)
            frame = self.vision.capture()
            if frame is None:
                return jsonify({"error": "no frame available"}), 500
            ts = time.strftime("%Y%m%d_%H%M%S")
            frame_path = debug_dir / f"frame_{ts}.png"
            cv2.imwrite(str(frame_path), frame)

            # Also save team mask
            try:
                mask = self.vision._build_team_mask(frame)
                mask_path = debug_dir / f"mask_{ts}.png"
                cv2.imwrite(str(mask_path), mask)
                logger.info(f"Debug snapshot saved: {frame_path}, {mask_path}")
                return jsonify({"frame": str(frame_path), "mask": str(mask_path)})
            except Exception as e:
                return jsonify({"frame": str(frame_path), "mask_error": str(e)})


# ═══════════════════════════════════════════════════════════════════════════════
#  ENTRY POINT
# ═══════════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    bot = MedicBot()

    # WebSocket in background thread
    threading.Thread(target=bot.start_ws, daemon=True).start()
    time.sleep(0.5)  # Let WS loop initialise

    # Flask (blocking)
    logger.info("Flask API on http://0.0.0.0:5000")
    bot.app.run(host="0.0.0.0", port=5000, threaded=True, debug=False, use_reloader=False)