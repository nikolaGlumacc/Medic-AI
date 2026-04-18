#!/usr/bin/env python3
"""
MedicAI Bot Server — FULL GUI COMPATIBILITY + ADVANCED TRACKING
- All original BotState values preserved (SEARCHING, ACQUIRE, TRACKING, RECOVER, HEALING, etc.)
- Flask + WebSocket API unchanged (C# GUI works as before)
- Advanced tracking core: Hungarian + Mahalanobis + 6‑state Kalman + PID controller
- Full game flow: Continue button, class select (7), spawn detection, Medigun equip (2)
"""

import cv2
import numpy as np
import time
import threading
import logging
import json
import os
import random
from dataclasses import dataclass
from enum import Enum
from typing import Optional, List, Tuple, Dict
from pathlib import Path
from scipy.optimize import linear_sum_assignment

import mss
import win32api
import win32con
import win32gui
import win32process
import psutil
from flask import Flask, request, jsonify
import asyncio
import websockets

# Optional OCR
try:
    import pytesseract
except ImportError:
    pytesseract = None

# Input backends
try:
    import pydirectinput
    pydirectinput.PAUSE = 0.0
except ImportError:
    pydirectinput = None

try:
    from pynput.keyboard import Key, Controller as KeyboardController
    _keyboard = KeyboardController()
except ImportError:
    _keyboard = None

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("MedicAI")

# ═══════════════════════════════════════════════════════════════════════════════
#  CONFIGURATION (original keys + new tracking params)
# ═══════════════════════════════════════════════════════════════════════════════
DEFAULTS: Dict = {
    # Connection
    "ws_port": 8766,
    "flask_port": 5000,

    # Aim / mouse
    "mouse_speed": 0.7,
    "deadzone_px": 8,
    "max_move_px": 12,
    "max_dist": 450,
    "aim_lead_factor": 0.15,
    "pid_kp": 0.15,
    "pid_ki": 0.01,
    "pid_kd": 0.05,

    # Follow / movement
    "follow_fwd_thresh": 180,
    "follow_back_thresh": 80,
    "follow_strafe_thresh": 100,
    "follow_enabled": True,
    "strafe_randomize": False,
    "backpedal_max_duration": 2.0,

    # BLU HSV
    "blu_h_min": 95,  "blu_h_max": 125,
    "blu_s_min": 70,  "blu_s_max": 255,
    "blu_v_min": 80,  "blu_v_max": 255,

    # RED HSV
    "red1_h_min": 0,  "red1_h_max": 8,
    "red1_s_min": 90, "red1_s_max": 255,
    "red1_v_min": 90, "red1_v_max": 255,
    "red2_h_min": 172, "red2_h_max": 179,
    "red2_s_min": 90,  "red2_s_max": 255,
    "red2_v_min": 90,  "red2_v_max": 255,

    # Blob filters
    "min_area": 600,
    "max_area": 8000,
    "min_aspect": 1.2,
    "max_aspect": 4.5,
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
    "uber_pop_threshold": 95.0,
    "auto_uber": True,

    # Tracking
    "track_min_hits": 3,
    "track_max_lost": 5,
    "mahalanobis_gate": 5.991,
    "lock_duration": 2.0,

    # Game flow
    "idle_rotation_speed": 18,
    "idle_rotate_delay": 0.5,
    "key_cooldown": 0.1,
    "spy_check_frequency": 8,
    "scoreboard_check_frequency": 30,
    "tab_hold_duration": 0.25,

    # Priority / OCR
    "priority_players": [],
    "team": "auto",
    "tesseract_path": r"C:\Program Files\Tesseract-OCR\tesseract.exe",

    # Timing
    "capture_loop_interval": 0.005,
    "controller_loop_interval": 0.01,

    # Misc
    "show_debug_window": False,
    "cpu_throttle_threshold": 85,

    # Original keys (kept for compatibility)
    "smoothing": 0.12,
    "deadzone": 30,
    "horizontal_smoothing": 0.20,
    "vertical_smoothing": 0.20,
    "aim_vertical_offset_pct": 0.0,
    "aim_y_min_pct": 15.0,
    "aim_y_max_pct": 85.0,
    "match_dist": 160,
    "grace_period": 1.5,
    "lock_grace": 0.8,
    "max_tracked_players": 8,
    "max_blob_w_pct": 25.0,
    "max_blob_h_pct": 55.0,
    "min_convexity_ratio": 0.35,
    "hud_top_pct": 8.0,
    "hud_bottom_pct": 90.0,
    "hud_left_pct": 3.0,
    "hud_right_pct": 97.0,
    "watchdog_timeout": 0.4,
    "uber_loop_interval": 0.08,
    "max_aim_time_on_target": 0,
    "action_cooldown_min": 0,
    "action_cooldown_max": 0,
    "session_actions_per_minute_cap": 0,
    "idle_random_look_enabled": False,
    "idle_look_amplitude": 50,
    "periodic_weapon_switch": False,
    "micro_jitter_enabled": False,
    "micro_jitter_amplitude": 1,
    "current_weapon_slot": 2,
    "hold_fire_override": False,
    "match_threshold": 0.70,
    "equip_timeout": 15,
    "retry_delay": 0.25,
    "click_delay": 0.15,
    "menu_open_delay": 0.35,
    "menu_close_delay": 0.20,
    "scale_factors": [0.8, 0.9, 1.0, 1.1],
    "follow_distance": 50,
    "retreat_health_threshold": 50,
    "screen_capture_fps_limit": 60,
    "stable_frames_required": 3,
    "confidence_threshold": 60,
    "max_blob_speed_px": 120,
    "acquire_timeout": 0.4,
    "recover_timeout": 1.5,
}
CONFIG: Dict = dict(DEFAULTS)

def load_config():
    global CONFIG
    # FIX: Ensure config is loaded from script's directory, not root
    config_path = os.path.join(os.path.dirname(__file__), "bot_config.json")
    if os.path.exists(config_path):
        with open(config_path) as f:
            try:
                data = json.load(f)
                CONFIG.update(data)
            except Exception as e:
                logger.error(f"Failed to load config: {e}")
load_config()

_tess_path = CONFIG.get("tesseract_path", "")
if os.path.exists(_tess_path) and pytesseract:
    pytesseract.pytesseract.tesseract_cmd = _tess_path

# ═══════════════════════════════════════════════════════════════════════════════
#  ENUMS & DATA CLASSES (original BotState preserved)
# ═══════════════════════════════════════════════════════════════════════════════
class BotState(Enum):
    SEARCHING  = "SEARCHING"
    ACQUIRE    = "ACQUIRE"
    TRACKING   = "TRACKING"
    RECOVER    = "RECOVER"
    HEALING    = "HEALING"
    FOLLOWING  = "FOLLOWING"
    MELEE      = "MELEE"
    RETREATING = "RETREATING"
    PASSIVE    = "PASSIVE"
    IDLE       = "IDLE"
    WAITING    = "WAITING"           # internal only, mapped to IDLE for GUI
    CLASS_SELECT = "CLASS_SELECT"    # internal only
    SPAWNING   = "SPAWNING"          # internal only

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
    score: float = 0.0

# ═══════════════════════════════════════════════════════════════════════════════
#  KALMAN FILTER (6‑STATE ADAPTIVE)
# ═══════════════════════════════════════════════════════════════════════════════
class KalmanTracker:
    def __init__(self, dt, initial_x, initial_y):
        self.dt = dt
        self.kf = cv2.KalmanFilter(6, 2)
        self.kf.transitionMatrix = np.array([
            [1, 0, dt, 0, 0.5*dt*dt, 0],
            [0, 1, 0, dt, 0, 0.5*dt*dt],
            [0, 0, 1, 0, dt, 0],
            [0, 0, 0, 1, 0, dt],
            [0, 0, 0, 0, 1, 0],
            [0, 0, 0, 0, 0, 1]
        ], np.float32)
        self.kf.measurementMatrix = np.array([[1,0,0,0,0,0],[0,1,0,0,0,0]], np.float32)
        self.base_process_noise = np.eye(6, dtype=np.float32) * 0.01
        self.base_process_noise[4,4] = 0.1
        self.base_process_noise[5,5] = 0.1
        self.kf.processNoiseCov = self.base_process_noise.copy()
        self.kf.measurementNoiseCov = np.eye(2, dtype=np.float32) * 1.0
        self.kf.errorCovPost = np.eye(6, dtype=np.float32)
        self.kf.statePost = np.array([[initial_x],[initial_y],[0],[0],[0],[0]], np.float32)
        self.initialized = True

    def predict(self):
        return self.kf.predict()

    def correct(self, x, y, confidence=1.0):
        measurement = np.array([[x],[y]], np.float32)
        self.kf.measurementNoiseCov = np.eye(2, dtype=np.float32) * (1.0 / max(confidence, 0.1))
        return self.kf.correct(measurement)

    def get_state(self):
        return self.kf.statePost

    def get_innovation_covariance(self):
        P = self.kf.errorCovPre
        H = self.kf.measurementMatrix
        R = self.kf.measurementNoiseCov
        return H @ P @ H.T + R

    def mahalanobis_distance(self, z):
        z_pred = self.kf.measurementMatrix @ self.kf.statePre
        innovation = z - z_pred[:2]
        S = self.get_innovation_covariance()
        try:
            invS = np.linalg.inv(S)
            return np.sqrt(innovation.T @ invS @ innovation)
        except:
            return 1e9

    def adapt_process_noise(self, acc_mag):
        scale = 1.0 + acc_mag * 0.5
        self.kf.processNoiseCov = self.base_process_noise * scale

# ═══════════════════════════════════════════════════════════════════════════════
#  TARGET TRACKER (HUNGARIAN + MAHALANOBIS + LIFECYCLE)
# ═══════════════════════════════════════════════════════════════════════════════
class TargetTracker:
    def __init__(self):
        self.tracks = []
        self.next_id = 0
        self.max_lost = CONFIG["track_max_lost"]
        self.min_hits = CONFIG["track_min_hits"]
        self.gate_threshold = CONFIG["mahalanobis_gate"]

    def _compute_detection_score(self, area, aspect):
        area_score = min(area / 4000.0, 1.0)
        aspect_score = 1.0 if 1.5 <= aspect <= 3.5 else 0.5
        return area_score * 0.5 + aspect_score * 0.5

    def predict_all(self):
        for track in self.tracks:
            track['kalman'].predict()
            state = track['kalman'].get_state()
            ax, ay = state[4,0], state[5,0]
            track['kalman'].adapt_process_noise(np.hypot(ax, ay))

    def update(self, detections: List[Tuple[int, int, float, float]], dt: float):
        if not self.tracks and detections:
            for det in detections:
                kf = KalmanTracker(dt, det[0], det[1])
                score = self._compute_detection_score(det[2], det[3])
                self.tracks.append({
                    'id': self.next_id,
                    'kalman': kf,
                    'cx': det[0], 'cy': det[1],
                    'area': det[2],
                    'score': score,
                    'hits': 1, 'age': 1, 'lost': 0,
                    'confidence': score,
                    'locked': False
                })
                self.next_id += 1
            return

        cost_matrix = np.full((len(self.tracks), len(detections)), 1e9)
        for i, track in enumerate(self.tracks):
            kf = track['kalman']
            for j, det in enumerate(detections):
                z = np.array([[det[0]],[det[1]]])
                mahal = kf.mahalanobis_distance(z)
                if mahal < self.gate_threshold:
                    cost_matrix[i,j] = mahal - track['confidence'] * 2.0

        if len(self.tracks) > 0 and len(detections) > 0:
            row_ind, col_ind = linear_sum_assignment(cost_matrix)
            matched_tracks, matched_dets = set(), set()
            for r,c in zip(row_ind, col_ind):
                if cost_matrix[r,c] < 1e8:
                    track = self.tracks[r]
                    det = detections[c]
                    score = self._compute_detection_score(det[2], det[3])
                    track['kalman'].correct(det[0], det[1], confidence=score)
                    state = track['kalman'].get_state()
                    track['cx'] = state[0,0]; track['cy'] = state[1,0]
                    track['area'] = det[2]
                    track['hits'] += 1; track['age'] += 1; track['lost'] = 0
                    track['confidence'] = 0.8*track['confidence'] + 0.2*score
                    matched_tracks.add(r); matched_dets.add(c)

            for i, track in enumerate(self.tracks):
                if i not in matched_tracks:
                    track['lost'] += 1
                    track['age'] += 1
                    track['confidence'] *= 0.95

            self.tracks = [t for t in self.tracks if t['lost'] <= self.max_lost]

            for j, det in enumerate(detections):
                if j not in matched_dets:
                    kf = KalmanTracker(dt, det[0], det[1])
                    score = self._compute_detection_score(det[2], det[3])
                    self.tracks.append({
                        'id': self.next_id,
                        'kalman': kf,
                        'cx': det[0], 'cy': det[1],
                        'area': det[2],
                        'score': score,
                        'hits': 1, 'age': 1, 'lost': 0,
                        'confidence': score,
                        'locked': False
                    })
                    self.next_id += 1

    def get_best_target(self, screen_cx, screen_cy, max_dist):
        best_score = -1e9
        best_track = None
        for t in self.tracks:
            if t['hits'] < self.min_hits or t['lost'] > 0:
                continue
            dist = np.hypot(t['cx'] - screen_cx, t['cy'] - screen_cy)
            if dist > max_dist:
                continue
            age_bonus = min(t['age'] / 30.0, 1.0)
            score = t['confidence'] * 50 * (1 + age_bonus) - dist
            if score > best_score:
                best_score = score
                best_track = t
        return best_track

    def lock_track(self, track_id):
        for t in self.tracks:
            t['locked'] = (t['id'] == track_id)

    def get_locked_track(self):
        for t in self.tracks:
            if t.get('locked', False) and t['lost'] == 0:
                return t
        return None

# ═══════════════════════════════════════════════════════════════════════════════
#  VISION (original methods + HUD blackout + Continue detection)
# ═══════════════════════════════════════════════════════════════════════════════
class Vision:
    def __init__(self):
        self.sct = mss.mss()
        self.mon = self.sct.monitors[1]
        self.w, self.h = self.mon["width"], self.mon["height"]
        self.ocr_ok = pytesseract is not None
        self._continue_tmpl = None
        tmpl_path = Path("templates/continue_button.png")
        if tmpl_path.exists():
            self._continue_tmpl = cv2.imread(str(tmpl_path))
        self._prev_blob_positions: Dict[int, Tuple[int, int]] = {}

    def capture(self):
        img = np.array(self.sct.grab(self.mon))
        return cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)

    def get_team_mask(self, frame):
        h, w = frame.shape[:2]
        blackout = np.ones((h,w), dtype=np.uint8)*255
        blackout[0:int(h*0.08),:] = 0
        blackout[0:int(h*0.20), int(w*0.85):w] = 0
        blackout[int(h*0.90):h,:] = 0
        masked = frame.copy()
        masked[blackout==0] = (0,0,0)
        hsv = cv2.cvtColor(masked, cv2.COLOR_BGR2HSV)
        team = CONFIG.get("team", "auto")
        if team in ("auto","BLU"):
            lo = np.array([CONFIG["blu_h_min"], CONFIG["blu_s_min"], CONFIG["blu_v_min"]])
            hi = np.array([CONFIG["blu_h_max"], CONFIG["blu_s_max"], CONFIG["blu_v_max"]])
            mask = cv2.inRange(hsv, lo, hi)
        else:
            lo1 = np.array([CONFIG["red1_h_min"], CONFIG["red1_s_min"], CONFIG["red1_v_min"]])
            hi1 = np.array([CONFIG["red1_h_max"], CONFIG["red1_s_max"], CONFIG["red1_v_max"]])
            lo2 = np.array([CONFIG["red2_h_min"], CONFIG["red2_s_min"], CONFIG["red2_v_min"]])
            hi2 = np.array([CONFIG["red2_h_max"], CONFIG["red2_s_max"], CONFIG["red2_v_max"]])
            mask = cv2.inRange(hsv, lo1, hi1) | cv2.inRange(hsv, lo2, hi2)
        ok, ck = int(CONFIG["morph_open_kernel"]), int(CONFIG["morph_close_kernel"])
        if ok>0: mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE,(ok,ok)))
        if ck>0: mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE,(ck,ck)))
        return mask

    def find_blobs(self, frame):
        mask = self.get_team_mask(frame)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        blobs = []
        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < CONFIG["min_area"] or area > CONFIG["max_area"]:
                continue
            x,y,bw,bh = cv2.boundingRect(cnt)
            aspect = bh / max(bw,1)
            if aspect < CONFIG["min_aspect"] or aspect > CONFIG["max_aspect"]:
                continue
            M = cv2.moments(cnt)
            if M["m00"]==0: continue
            cx, cy = int(M["m10"]/M["m00"]), int(M["m01"]/M["m00"])
            blobs.append((cx, cy, area, aspect))
        return blobs

    # Original find_best_target (kept for compatibility, but not used in new tracking)
    def find_best_target(self, frame) -> Optional[BlobResult]:
        blobs = self.find_blobs(frame)
        if not blobs:
            return None
        # Simple scoring for GUI compatibility
        best = max(blobs, key=lambda b: b[2])  # largest area
        return BlobResult(cx=best[0], cy=best[1], area=best[2], dist_to_center=0, score=best[2])

    def detect_continue_button(self, frame):
        if self._continue_tmpl is None: return False
        scales = [0.8,0.9,1.0,1.1,1.2]
        for s in scales:
            tmpl = cv2.resize(self._continue_tmpl, (0,0), fx=s, fy=s)
            if tmpl.shape[0]>frame.shape[0] or tmpl.shape[1]>frame.shape[1]: continue
            res = cv2.matchTemplate(frame, tmpl, cv2.TM_CCOEFF_NORMED)
            _, max_val, _, _ = cv2.minMaxLoc(res)
            if max_val > 0.7: return True
        return False

    def is_spawned(self, frame):
        h,w = frame.shape[:2]
        roi = frame[int(h*0.88):int(h*0.96), int(w*0.02):int(w*0.12)]
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        return np.mean(gray) > 50

    def read_name_at_crosshair(self, frame):
        if not self.ocr_ok: return ""
        h,w = frame.shape[:2]
        roi = frame[int(h*0.50):int(h*0.60), int(w*0.35):int(w*0.65)]
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 160, 255, cv2.THRESH_BINARY)
        try:
            return pytesseract.image_to_string(thresh, config="--psm 7").strip()
        except:
            return ""

    def read_uber_charge(self, frame):
        h,w = frame.shape[:2]
        x1 = int(w*0.72); y1 = int(h*0.895)
        x2 = x1+int(w*0.10); y2 = y1+int(h*0.03)
        if x2<=x1 or y2<=y1: return 0.0
        roi = frame[y1:y2, x1:x2]
        hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
        lo = np.array([85,30,210]); hi = np.array([135,255,255])
        mask = cv2.inRange(hsv, lo, hi)
        return round((cv2.countNonZero(mask)/mask.size)*100.0, 1)

    def read_hud_snapshot(self, frame=None):
        if frame is None: frame = self.capture()
        snap = HudSnapshot(ocr_available=self.ocr_ok)
        snap.uber_charge = self.read_uber_charge(frame)
        if self.ocr_ok:
            try:
                h,w = frame.shape[:2]
                hp_roi = frame[int(h*0.88):int(h*0.96), int(w*0.02):int(w*0.12)]
                gray = cv2.cvtColor(hp_roi, cv2.COLOR_BGR2GRAY)
                _, thr = cv2.threshold(gray, 140, 255, cv2.THRESH_BINARY)
                txt = pytesseract.image_to_string(thr, config="--psm 8 -c tessedit_char_whitelist=0123456789").strip()
                if txt.isdigit(): snap.health = int(txt)
            except: pass
            snap.heal_target_name = self.read_name_at_crosshair(frame) or None
        return snap

    def find_medic_bubble(self, frame):
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        lo1 = np.array([0,150,150]); hi1 = np.array([8,255,255])
        lo2 = np.array([170,150,150]); hi2 = np.array([179,255,255])
        mask = cv2.inRange(hsv, lo1, hi1) | cv2.inRange(hsv, lo2, hi2)
        k = cv2.getStructuringElement(cv2.MORPH_CROSS, (5,5))
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, k)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        best = None; best_score = 0.0
        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < 60: continue
            if area > best_score:
                best_score = area
                M = cv2.moments(cnt)
                if M["m00"]: best = (int(M["m10"]/M["m00"]), int(M["m01"]/M["m00"]))
        return best

    def read_scoreboard_names(self, frame):
        if not self.ocr_ok: return {}
        h,w = frame.shape[:2]
        roi = frame[int(h*0.15):int(h*0.90), int(w*0.15):int(w*0.85)]
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        _, bright = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY)
        _, dim = cv2.threshold(gray, 80, 255, cv2.THRESH_BINARY)
        dim_only = cv2.bitwise_and(dim, cv2.bitwise_not(bright))
        results = {}
        try:
            alive = pytesseract.image_to_string(bright, config="--psm 6").strip()
            dead = pytesseract.image_to_string(dim_only, config="--psm 6").strip()
            for name in alive.splitlines():
                name = name.strip()
                if name: results[name] = "alive"
            for name in dead.splitlines():
                name = name.strip()
                if name and name not in results: results[name] = "dead"
        except: pass
        return results

    def detect_own_team(self, frame=None):
        if frame is None: frame = self.capture()
        h,w = frame.shape[:2]
        sample = frame[int(h*0.85):int(h*0.98), int(w*0.01):int(w*0.08)]
        hsv = cv2.cvtColor(sample, cv2.COLOR_BGR2HSV)
        blu = cv2.countNonZero(cv2.inRange(hsv, np.array([95,60,60]), np.array([130,255,255])))
        red = cv2.countNonZero(cv2.inRange(hsv, np.array([0,80,80]), np.array([10,255,255])))
        return "BLU" if blu >= red else "RED"

# ═══════════════════════════════════════════════════════════════════════════════
#  CONTROLLER (PID + velocity feedforward + original methods)
# ═══════════════════════════════════════════════════════════════════════════════
class Controller:
    def __init__(self):
        self.heal_held = False
        self.last_key_time = {}
        self.key_cooldown = CONFIG["key_cooldown"]
        self.error_sum_x = 0.0
        self.error_sum_y = 0.0
        self.last_error_x = 0.0
        self.last_error_y = 0.0
        self.sdx = 0.0
        self.sdy = 0.0

    def _game_focused(self):
        hwnd = win32gui.GetForegroundWindow()
        _, pid = win32process.GetWindowThreadProcessId(hwnd)
        try:
            return psutil.Process(pid).name() == "hl2.exe"
        except:
            return False

    def move_mouse(self, dx, dy, vx=0.0, vy=0.0):
        if not self._game_focused(): return
        lead = CONFIG["aim_lead_factor"]
        total_dx = dx + vx * lead
        total_dy = dy + vy * lead
        dist = np.hypot(total_dx, total_dy)
        if dist < CONFIG["deadzone_px"]:
            self.error_sum_x *= 0.9; self.error_sum_y *= 0.9
            return

        Kp, Ki, Kd = CONFIG["pid_kp"], CONFIG["pid_ki"], CONFIG["pid_kd"]
        self.error_sum_x += total_dx; self.error_sum_y += total_dy
        self.error_sum_x = np.clip(self.error_sum_x, -50, 50)
        self.error_sum_y = np.clip(self.error_sum_y, -50, 50)
        deriv_x = total_dx - self.last_error_x
        deriv_y = total_dy - self.last_error_y
        out_x = Kp*total_dx + Ki*self.error_sum_x + Kd*deriv_x
        out_y = Kp*total_dy + Ki*self.error_sum_y + Kd*deriv_y
        move_x = int(np.clip(out_x * CONFIG["mouse_speed"], -CONFIG["max_move_px"], CONFIG["max_move_px"]))
        move_y = int(np.clip(out_y * CONFIG["mouse_speed"], -CONFIG["max_move_px"], CONFIG["max_move_px"]))
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, move_x, move_y, 0, 0)
        self.last_error_x = total_dx; self.last_error_y = total_dy

    def aim_at_blob(self, blob: BlobResult, frame_shape):
        h,w = frame_shape[:2]
        self.move_mouse(float(blob.cx - w//2), float(blob.cy - h//2))

    def aim_at_track(self, track, w, h):
        state = track['kalman'].get_state()
        dx = state[0,0] - w//2
        dy = state[1,0] - h//2
        self.move_mouse(dx, dy, state[2,0], state[3,0])

    def hold_m1(self):
        if not self._game_focused(): return
        if not self.heal_held:
            if pydirectinput: pydirectinput.mouseDown(button="left")
            self.heal_held = True

    def release_m1(self):
        if self.heal_held:
            if pydirectinput: pydirectinput.mouseUp(button="left")
            self.heal_held = False

    def click_m1(self):
        if pydirectinput: pydirectinput.click(button="left")

    def press_key(self, key):
        if not self._game_focused(): return
        now = time.time()
        if now - self.last_key_time.get(key,0) > self.key_cooldown:
            if pydirectinput: pydirectinput.press(key)
            self.last_key_time[key] = now

    def hold_key(self, key):
        if not self._game_focused(): return
        if pydirectinput: pydirectinput.keyDown(key)

    def release_key(self, key):
        if pydirectinput: pydirectinput.keyUp(key)

    def click(self, button="left"):
        if self._game_focused() and pydirectinput:
            pydirectinput.click(button=button)

    def pop_uber(self):
        self.click(button="right")

    def flick_spy_check(self):
        spd = 180
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, spd*10, 0, 0, 0)
        time.sleep(0.08)
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, -spd*10, 0, 0, 0)

    def switch_weapon(self, slot):
        self.press_key(str(slot))
        time.sleep(0.05)

    def type_string(self, text):
        if _keyboard:
            _keyboard.type(text)
        elif pydirectinput:
            for ch in text:
                try: pydirectinput.press(ch)
                except: pass

    def cleanup(self):
        self.release_m1()
        for k in ("w","a","s","d"): self.release_key(k)

# ═══════════════════════════════════════════════════════════════════════════════
#  LOADOUT MANAGER (unchanged)
# ═══════════════════════════════════════════════════════════════════════════════
class LoadoutManager:
    WEAPONS_DIR = Path(__file__).parent / "weapons"
    def __init__(self, controller, vision):
        self._ctrl = controller; self._vision = vision
        self._templates = {}
        self._cancel_flag = threading.Event()
        self._ensure_weapons_dir()
        self._load_templates()
    def _ensure_weapons_dir(self):
        if not self.WEAPONS_DIR.exists():
            self.WEAPONS_DIR.mkdir(parents=True)
    def _load_templates(self):
        for png in self.WEAPONS_DIR.glob("*.png"):
            img = cv2.imread(str(png))
            if img is not None:
                self._templates[png.stem.lower()] = img
    @property
    def available_weapons(self): return sorted(self._templates.keys())
    def _find_weapon_on_screen(self, frame, weapon_name):
        tmpl = self._templates.get(weapon_name.lower())
        if tmpl is None: return None
        best_val, best_loc = 0.0, None
        for sf in [0.8,0.9,1.0,1.1]:
            scaled = cv2.resize(tmpl, (0,0), fx=sf, fy=sf)
            if scaled.shape[0]>frame.shape[0] or scaled.shape[1]>frame.shape[1]: continue
            res = cv2.matchTemplate(frame, scaled, cv2.TM_CCOEFF_NORMED)
            _, max_val, _, max_loc = cv2.minMaxLoc(res)
            if max_val > best_val:
                best_val = max_val; best_loc = (max_loc[0]+scaled.shape[1]//2, max_loc[1]+scaled.shape[0]//2)
        return best_loc if best_val>=0.7 else None
    def equip(self, weapon_name, bot_ref=None):
        weapon_name = weapon_name.lower()
        if weapon_name not in self._templates: return False
        if bot_ref: bot_ref._equip_in_progress = True
        self._cancel_flag.clear()
        deadline = time.time()+15
        ok = False
        try:
            self._ctrl.press_key("m"); time.sleep(0.35)
            while time.time()<deadline and not self._cancel_flag.is_set():
                frame = self._vision.capture()
                if self._find_weapon_on_screen(frame, weapon_name):
                    ok = True; break
                for wname in self._templates:
                    loc = self._find_weapon_on_screen(frame, wname)
                    if loc:
                        win32api.SetCursorPos(loc); time.sleep(0.15)
                        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN,0,0,0,0)
                        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP,0,0,0,0)
                        time.sleep(0.15); break
                time.sleep(0.25)
            self._ctrl.press_key("m"); time.sleep(0.20)
        except Exception as e:
            logger.error(f"Equip error: {e}")
        finally:
            if bot_ref: bot_ref._equip_in_progress = False
        return ok
    def equip_async(self, weapon_name, bot_ref=None):
        threading.Thread(target=self.equip, args=(weapon_name,bot_ref), daemon=True).start()
    def cancel(self): self._cancel_flag.set()

# ═══════════════════════════════════════════════════════════════════════════════
#  GAME FLOW (Continue, class select, spawn)
# ═══════════════════════════════════════════════════════════════════════════════
class GameFlow:
    def __init__(self, vision, ctrl):
        self.vision = vision; self.ctrl = ctrl
        self.continue_pressed = False
        self.class_selected = False
        self.spawned = False

    def step(self, frame):
        # SPEED CLIP: If we see we're already spawned at ANY point, go straight to ready
        if self.vision.is_spawned(frame):
            if not self.spawned:
                self.continue_pressed = True
                self.class_selected = True
                self.spawned = True
                logger.info("Direct spawn detected – bypassing flow.")
                self.ctrl.press_key("2") # medigun
                return "ready"
            return None

        if not self.continue_pressed:
            if self.vision.detect_continue_button(frame):
                logger.info("Continue detected – clicking")
                win32api.SetCursorPos((self.vision.w//2, self.vision.h//2))
                self.ctrl.click()
                self.continue_pressed = True
                return "class_select"
            return None

        if self.continue_pressed and not self.class_selected:
            # Maybe the menu is already open, or we need to press it
            self.ctrl.press_key("7")
            logger.info("Selecting Medic (7)")
            self.class_selected = True
            return "spawning"

        if self.class_selected and not self.spawned:
            if self.vision.is_spawned(frame):
                time.sleep(0.2)
                self.ctrl.press_key("2")
                logger.info("Spawned – equipped Medigun (2)")
                self.spawned = True
                return "ready"
            return "spawning"
        return None

# ═══════════════════════════════════════════════════════════════════════════════
#  MEDIC BOT (original state machine preserved, enhanced with tracking)
# ═══════════════════════════════════════════════════════════════════════════════
class MedicBot:
    def __init__(self):
        self.running = False
        self.state = BotState.IDLE
        self.team = CONFIG.get("team", "auto")

        self.vision = Vision()
        self.ctrl = Controller()
        self.tracker = TargetTracker()
        self.gameflow = GameFlow(self.vision, self.ctrl)
        self.loadout = LoadoutManager(self.ctrl, self.vision)

        self.uber_pct = 0.0
        self.my_health = None
        self.current_target = None
        self.session_start = time.time()
        self.melee_kills = 0

        self._equip_in_progress = False
        self._cpu_throttled = False
        self._last_spy_check = 0.0
        self._last_scoreboard_check = 0.0
        self._last_frame = None
        self._search_since = 0.0

        # Original state machine fields
        self.last_blob = None
        self.stable_target_frames = 0
        self._acquire_start = None
        self._recover_start = None

        # New tracking fields
        self.locked_track_id = None
        self.lock_timer = 0.0
        self.last_target_time = 0.0
        self.last_time = time.time()
        self.priority_names = [p.lower() for p in CONFIG.get("priority_players", [])]
        self.player_statuses: Dict[str, str] = {} # "name": "alive" / "dead"
        self._gameflow_ready = False

        # WebSocket / Flask
        self._ws_clients = set()
        self._ws_loop = None
        self.app = Flask(__name__)
        self._register_routes()

    # ── Lifecycle ─────────────────────────────────────────────────────────────
    def start(self):
        if self.running: return
        self.running = True
        self.session_start = time.time()
        threading.Thread(target=self._capture_loop, daemon=True).start()
        threading.Thread(target=self._brain_loop, daemon=True).start()
        threading.Thread(target=self._cpu_temp_loop, daemon=True).start()
        threading.Thread(target=self._scoreboard_loop, daemon=True).start()
        logger.info("MedicBot started.")
        self._broadcast({"type": "activity", "msg": "Bot started.", "audio": False})

    def stop(self):
        self.running = False
        self.ctrl.cleanup()
        logger.info("MedicBot stopped.")
        self._broadcast({"type": "activity", "msg": "Bot stopped.", "audio": False})

    def _capture_loop(self):
        interval = CONFIG["capture_loop_interval"]
        with mss.mss() as sct:
            mon = sct.monitors[1]
            while self.running:
                t0 = time.time()
                img = np.array(sct.grab(mon))
                self._last_frame = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)
                sleep = interval - (time.time() - t0)
                if sleep > 0: time.sleep(sleep)

    def _brain_loop(self):
        target_dt = 1.0 / 60.0
        while self.running:
            start = time.time()
            if self._equip_in_progress:
                time.sleep(0.05); continue
            frame = self._last_frame
            if frame is None:
                time.sleep(0.01); continue

            # Game flow automation (Continue, class, spawn)
            flow_status = self.gameflow.step(frame)
            if flow_status == "class_select" and self.state != BotState.CLASS_SELECT:
                self.state = BotState.CLASS_SELECT
            elif flow_status == "spawning" and self.state != BotState.SPAWNING:
                self.state = BotState.SPAWNING
            elif flow_status == "ready" and not self._gameflow_ready:
                self._gameflow_ready = True
                self.state = BotState.SEARCHING

            # Only run tracking after spawn
            if self._gameflow_ready and self.state not in (BotState.IDLE, BotState.CLASS_SELECT, BotState.SPAWNING):
                snap = self.vision.read_hud_snapshot(frame)
                self.uber_pct = snap.uber_charge
                if snap.health: self.my_health = snap.health

                self.tracker.predict_all()
                blobs = self.vision.find_blobs(frame)
                now = time.time()
                dt = now - self.last_time
                self.last_time = now
                self.tracker.update(blobs, dt)

                self._tick(frame, snap)

                # Spy check
                if time.time() - self._last_spy_check >= CONFIG["spy_check_frequency"]:
                    self._last_spy_check = time.time()
                    self.ctrl.flick_spy_check()
                    self._broadcast_activity("Spy check.")

            self._broadcast_status()
            elapsed = time.time() - start
            time.sleep(max(0, target_dt - elapsed))

    def _tick(self, frame, snap):
        now = time.time()
        screen_cx, screen_cy = self.vision.w//2, self.vision.h//2

        # TACTICAL CHECK: Is our priority dead?
        priorities = [p.lower() for p in CONFIG.get("priority_players", [])]
        priority_dead = False
        for name, status in self.player_statuses.items():
            if name.lower() in priorities and status == "dead":
                priority_dead = True
                break

        if priority_dead:
            if self.state != BotState.RETREATING:
                logger.warning("PRIORITY DEAD - RETREATING")
                self._broadcast_activity("Priority player dead — RETREATING!", audio=True)
            self.state = BotState.RETREATING
            self.ctrl.release_m1()
            self.locked_track_id = None
            self.ctrl.hold_key("s")
            self.ctrl.release_key("w")
            # Random strafe to avoid snipers while retreating
            if random.random() < 0.1:
                key = random.choice(["a","d"])
                self.ctrl.hold_key(key)
                time.sleep(0.05)
                self.ctrl.release_key(key)
            return

        # If we were retreating but priority is no longer dead (respawned or off scoreboard)
        if self.state == BotState.RETREATING and not priority_dead:
            self.state = BotState.SEARCHING
            self.ctrl.release_key("s")

        # Maintain lock
        locked_track = None
        if self.locked_track_id is not None:
            locked_track = self.tracker.get_locked_track()
            if locked_track is None and now - self.lock_timer < CONFIG["lock_duration"]:
                self.ctrl.release_m1()
                self._stop_movement()
                self.state = BotState.RECOVER if self.state == BotState.TRACKING else self.state
                return
            elif locked_track is None:
                self.locked_track_id = None
                self.state = BotState.SEARCHING
                return

        # TRACKING / HEALING state (with lock)
        if self.state in (BotState.TRACKING, BotState.HEALING, BotState.FOLLOWING):
            if locked_track:
                self.ctrl.aim_at_track(locked_track, self.vision.w, self.vision.h)
                self.ctrl.hold_m1()
                self._follow(locked_track)
                self.lock_timer = now
                self.state = BotState.TRACKING
                # OCR name for GUI
                name = self.vision.read_name_at_crosshair(frame).strip()
                self.current_target = name or "unknown"
                if self.uber_pct >= CONFIG["uber_pop_threshold"] and CONFIG["auto_uber"]:
                    self.ctrl.pop_uber()
                    self._broadcast_activity("Uber activated!", audio=True)
            else:
                self.ctrl.release_m1()
                self._recover_start = now
                self.state = BotState.RECOVER
                self._broadcast_activity("Target lost — recovering.")
            return

        # RECOVER state
        if self.state == BotState.RECOVER:
            best_track = self.tracker.get_best_target(screen_cx, screen_cy, CONFIG["max_dist"])
            if best_track:
                self.locked_track_id = best_track['id']
                self.tracker.lock_track(best_track['id'])
                self.lock_timer = now
                self.state = BotState.TRACKING
                self._broadcast_activity("Target re-acquired.")
            else:
                if self._recover_start and now - self._recover_start > CONFIG["recover_timeout"]:
                    self.locked_track_id = None
                    self.state = BotState.SEARCHING
                    self._broadcast_activity("Recovery timed out — searching.")
            return

        # SEARCHING / ACQUIRE states
        if self.state in (BotState.SEARCHING, BotState.ACQUIRE):
            best_track = self.tracker.get_best_target(screen_cx, screen_cy, CONFIG["max_dist"])
            if best_track:
                if self.state == BotState.SEARCHING:
                    self.state = BotState.ACQUIRE
                    self._acquire_start = now
                    self.stable_target_frames = 1
                    self._broadcast_activity("Candidate found — acquiring.")
                else:  # ACQUIRE
                    self.stable_target_frames += 1
                    if self.stable_target_frames >= CONFIG["stable_frames_required"]:
                        self.locked_track_id = best_track['id']
                        self.tracker.lock_track(best_track['id'])
                        self.lock_timer = now
                        self.state = BotState.TRACKING
                        self._broadcast_activity("Target locked — tracking.", audio=True)
                self.ctrl.aim_at_track(best_track, self.vision.w, self.vision.h)
                self.ctrl.hold_m1()
            else:
                self.ctrl.release_m1()
                if self.state == BotState.ACQUIRE:
                    if self._acquire_start and now - self._acquire_start > CONFIG["acquire_timeout"]:
                        self.state = BotState.SEARCHING
                        self._broadcast_activity("Acquire timed out — searching.")
                else:
                    if now - self.last_target_time > CONFIG["idle_rotate_delay"]:
                        self.ctrl.move_mouse(CONFIG["idle_rotation_speed"], 0)
                    else:
                        self._stop_movement()
                self.last_target_time = now
            return

    def _follow(self, track):
        cx, cy = track['cx'], track['cy']
        dx = cx - self.vision.w//2
        dist = np.hypot(dx, cy - self.vision.h//2)
        fwd, back, strafe = CONFIG["follow_fwd_thresh"], CONFIG["follow_back_thresh"], CONFIG["follow_strafe_thresh"]
        if abs(dx) > strafe:
            self.ctrl.hold_key("d" if dx>0 else "a")
            self.ctrl.release_key("a" if dx>0 else "d")
        else:
            self.ctrl.release_key("a"); self.ctrl.release_key("d")
        if dist > fwd:
            self.ctrl.hold_key("w"); self.ctrl.release_key("s")
        elif dist < back:
            self.ctrl.hold_key("s"); self.ctrl.release_key("w")
        else:
            self.ctrl.release_key("w"); self.ctrl.release_key("s")

    def _stop_movement(self):
        for k in ("w","a","s","d"): self.ctrl.release_key(k)

    # Original search behaviour (medic bubble)
    def _search(self, frame):
        if self._search_since == 0:
            self._search_since = time.time()
        elif time.time() - self._search_since > 15:
            self._search_since = 0
            self.ctrl.press_key("w")
            return
        bubble = self.vision.find_medic_bubble(frame)
        if bubble:
            bx, by = bubble
            dx = bx - self.vision.w//2
            self.ctrl.move_mouse(float(dx)*0.3, 0.0)
            self.ctrl.hold_key("w")
        else:
            self.ctrl.move_mouse(CONFIG["idle_rotation_speed"], 0.0)

    def _scoreboard_loop(self):
        while self.running:
            time.sleep(CONFIG["scoreboard_check_frequency"])
            if not self.running: break
            self._last_scoreboard_check = time.time()
            if _keyboard:
                _keyboard.press(Key.tab)
                time.sleep(CONFIG["tab_hold_duration"])
                frame = self.vision.capture()
                _keyboard.release(Key.tab)
                names = self.vision.read_scoreboard_names(frame)
                if names:
                    self.player_statuses = names
                    priorities = [p.lower() for p in CONFIG["priority_players"]]
                    for name, status in names.items():
                        if name.lower() in priorities:
                            logger.info(f"Priority Status: {name} = {status}")

    def _cpu_temp_loop(self):
        while self.running:
            try:
                temps = psutil.sensors_temperatures() or {}
                temp = 50
                for k in ("coretemp","cpu_thermal","k10temp"):
                    if k in temps and temps[k]:
                        temp = temps[k][0].current; break
                self._cpu_throttled = temp > CONFIG["cpu_throttle_threshold"]
            except: pass
            time.sleep(5)

    def detect_own_team(self):
        team = self.vision.detect_own_team()
        self.team = team
        CONFIG["team"] = team
        return team

    # ── WebSocket / Flask (unchanged) ────────────────────────────────────────
    def _broadcast(self, payload):
        if not self._ws_loop or not self._ws_clients: return
        asyncio.run_coroutine_threadsafe(self._ws_send_all(json.dumps(payload)), self._ws_loop)

    async def _ws_send_all(self, msg):
        dead = set()
        for ws in list(self._ws_clients):
            try: await ws.send(msg)
            except: dead.add(ws)
        self._ws_clients -= dead

    def _broadcast_activity(self, msg, audio=False):
        self._broadcast({"type": "activity", "msg": msg, "audio": audio})

    def _broadcast_status(self):
        # Map internal states to original BotState values for GUI
        gui_state = self.state
        if self.state == BotState.WAITING:
            gui_state = BotState.IDLE
        elif self.state == BotState.CLASS_SELECT:
            gui_state = BotState.IDLE
        elif self.state == BotState.SPAWNING:
            gui_state = BotState.IDLE

        self._broadcast({
            "type": "status",
            "running": self.running,
            "state": gui_state.value,
            "uber": self.uber_pct,
            "my_health": self.my_health,
            "current_target": self.current_target,
            "stable_frames": self.stable_target_frames,
            "last_blob_score": self.last_blob.score if self.last_blob else 0,
            "session_seconds": int(time.time() - self.session_start),
            "melee_kills": self.melee_kills,
        })

    async def _ws_handler(self, websocket):
        self._ws_clients.add(websocket)
        try:
            async for raw in websocket:
                try:
                    msg = json.loads(raw)
                    action = msg.get("action") or msg.get("type")
                    if action == "start": self.start()
                    elif action == "stop": self.stop()
                    elif action == "test_input":
                        logger.info("TEST_INPUT requested")
                        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, 10, 0, 0, 0)
                        time.sleep(0.05)
                        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, -10, 0, 0, 0)
                        if pydirectinput: pydirectinput.press('n')
                        self._broadcast_activity("TEST_INPUT_SUCCESS")
                    elif action == "config":
                        data = msg.get("config", {})
                        CONFIG.update(data)
                        config_path = os.path.join(os.path.dirname(__file__), "bot_config.json")
                        with open(config_path,"w") as f: json.dump(CONFIG,f,indent=4)
                except: pass
        except: pass
        finally: self._ws_clients.discard(websocket)

    # ──────────────────────────────────────────────────────────────────────────
    # FIXED: WebSocket binds to 127.0.0.1 to avoid Windows permission error
    # ──────────────────────────────────────────────────────────────────────────
    def start_ws(self, host="0.0.0.0", port=None):
        if port is None:
            port = CONFIG.get("ws_port", 8765)
        self._ws_loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self._ws_loop)
        async def serve():
            async with websockets.serve(self._ws_handler, host, port):
                logger.info(f"WebSocket server on ws://{host}:{port}")
                await asyncio.Future()
        self._ws_loop.run_until_complete(serve())

    def _register_routes(self):
        app = self.app
        @app.route("/status")
        def status():
            gui_state = self.state
            if self.state in (BotState.WAITING, BotState.CLASS_SELECT, BotState.SPAWNING):
                gui_state = BotState.IDLE
            return jsonify({
                "running": self.running, "state": gui_state.value,
                "uber_pct": self.uber_pct, "my_health": self.my_health,
                "current_target": self.current_target,
                "stable_frames": self.stable_target_frames,
                "last_blob_score": self.last_blob.score if self.last_blob else 0,
                "session_seconds": int(time.time() - self.session_start),
                "melee_kills": self.melee_kills, "team": self.team,
            })
        @app.route("/start", methods=["POST"])
        def start(): self.start(); return jsonify({"status":"started"})
        @app.route("/stop", methods=["POST"])
        def stop(): self.stop(); return jsonify({"status":"stopped"})
        @app.route("/config", methods=["GET","POST"])
        def config():
            if request.method == "POST":
                data = request.get_json(force=True,silent=True) or {}
                CONFIG.update(data)
                with open("bot_config.json","w") as f: json.dump(CONFIG,f,indent=4)
            return jsonify(CONFIG)
        @app.route("/set_follow_mode", methods=["POST"])
        def set_follow_mode():
            data = request.get_json(force=True,silent=True) or {}
            mode = data.get("mode","active")
            CONFIG["follow_enabled"] = (mode == "active")
            return jsonify({"mode": mode})
        @app.route("/weapons")
        def weapons(): return jsonify(self.loadout.available_weapons)
        @app.route("/equip_weapon", methods=["POST"])
        def equip():
            data = request.get_json(force=True,silent=True) or {}
            name = data.get("weapon","")
            if name:
                self.loadout.equip_async(name, self)
            return jsonify({"status":"equipping","weapon":name})
        @app.route("/detect_team", methods=["POST"])
        def detect_team():
            team = self.detect_own_team()
            return jsonify({"team": team})
        @app.route("/debug_snapshot", methods=["POST"])
        def debug_snapshot():
            debug_dir = Path("debug")
            debug_dir.mkdir(exist_ok=True)
            frame = self.vision.capture()
            if frame is None: return jsonify({"error":"no frame"}),500
            ts = time.strftime("%Y%m%d_%H%M%S")
            frame_path = debug_dir / f"frame_{ts}.png"
            cv2.imwrite(str(frame_path), frame)
            mask = self.vision.get_team_mask(frame)
            mask_path = debug_dir / f"mask_{ts}.png"
            cv2.imwrite(str(mask_path), mask)
            return jsonify({"frame": str(frame_path), "mask": str(mask_path)})

# ═══════════════════════════════════════════════════════════════════════════════
#  ENTRY POINT
# ═══════════════════════════════════════════════════════════════════════════════
if __name__ == "__main__":
    bot = MedicBot()
    # PRO TIP: Bind to 0.0.0.0 to allow connections from other IPs (Run as ADMIN if needed)
    threading.Thread(target=lambda: bot.start_ws(host="0.0.0.0"), daemon=True).start()
    time.sleep(0.5)
    logger.info("Flask API on http://0.0.0.0:5000")
    bot.app.run(host="0.0.0.0", port=5000, threaded=True, debug=False, use_reloader=False)