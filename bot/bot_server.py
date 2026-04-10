#!/usr/bin/env python3
"""
Medic-AI Bot Server - Rewritten
F12: Emergency Stop | F11: Pause/Resume | D: Toggle Debug Window | U: Toggle Auto-Uber
"""

import cv2
import numpy as np
import time
import threading
import queue
import logging
from dataclasses import dataclass, field
from typing import List, Optional, Tuple, Dict
from enum import Enum
from flask import Flask, request, jsonify
import mss
import keyboard
import win32api
import win32con
import pyautogui

# -------------------------------------------------------------------
# Logging
# -------------------------------------------------------------------
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    datefmt='%H:%M:%S'
)
logger = logging.getLogger(__name__)

DEBUG_MODE      = True
SHOW_DEBUG_WINDOW = False
AUTO_UBER_ENABLED = True

# -------------------------------------------------------------------
# Intent
# -------------------------------------------------------------------
class ActionType(Enum):
    AIM  = 1
    STOP = 2

@dataclass
class Intent:
    type: ActionType
    target_x: float = 0.0
    target_y: float = 0.0
    hold_fire: bool = False
    move_forward: bool = False
    move_back: bool = False
    move_left: bool = False
    move_right: bool = False
    pop_uber: bool = False

# -------------------------------------------------------------------
# Thread-safe latest-frame holder
# -------------------------------------------------------------------
class LatestFrame:
    def __init__(self):
        self._frame = None
        self._lock  = threading.Lock()
        self._event = threading.Event()

    def put(self, frame: np.ndarray):
        with self._lock:
            self._frame = frame
            self._event.set()

    def get(self, timeout: float = 1.0) -> Optional[np.ndarray]:
        if self._event.wait(timeout):
            with self._lock:
                self._event.clear()
                return self._frame.copy() if self._frame is not None else None
        return None

# -------------------------------------------------------------------
# Player
# -------------------------------------------------------------------
@dataclass
class Player:
    x: float
    y: float
    team: str     = "unknown"
    health: int   = 100
    width: float  = 0.0
    height: float = 0.0
    track_id: int = -1
    last_seen: float = field(default_factory=time.time)

# -------------------------------------------------------------------
# MedicBot
# -------------------------------------------------------------------
class MedicBot:
    def __init__(self):
        self._lock = threading.Lock()

        # --- State ---
        self._running  = False
        self._paused   = False
        self._team     = "unknown"

        self._locked_target_id: Optional[int]   = None
        self._locked_lost_time: float            = 0.0
        self._tracked: Dict[int, Player]         = {}
        self._next_id: int                       = 0
        self._current_weapon: int                = 2   # 2 = medigun (hold-fire)

        self._uber_percent: float = 0.0
        self._uber_ready:   bool  = False

        # --- Screen ---
        with mss.mss() as sct:
            mon = sct.monitors[1]
        self.sw = mon['width']
        self.sh = mon['height']
        self.cx = self.sw // 2
        self.cy = self.sh // 2
        self._monitor = mon

        # --- Aim ---
        self.mouse_speed  = 0.6
        self.smoothing    = 0.12
        self.deadzone     = 30          # pixels — ignore tiny offsets
        self.max_move_px  = 10          # cap per-frame mouse delta

        # Vertical aim band: centre-of-detected-blob must land here
        # FIX: was 30-60% which rejected players who were crouching,
        #      close-up, or on slopes.  Widen to 15-85%.
        self.aim_y_min = int(self.sh * 0.15)
        self.aim_y_max = int(self.sh * 0.85)

        # Maximum pixel distance from crosshair to consider a target
        self.max_dist = 450

        # --- Follow thresholds (pixels from screen-centre) ---
        self.follow_fwd_thresh    = 180   # run toward if farther than this
        self.follow_back_thresh   = 80    # back up if closer than this
        self.follow_strafe_thresh = 100   # strafe if horizontally offset

        # --- Tracking ---
        self.match_dist      = 160   # px to associate det→track
        self.grace_period    = 1.5   # seconds to keep a lost track
        self.lock_grace      = 0.8   # seconds before unlocking lost target

        # --- Color detection HSV ranges ---
        # BLU team: cyan-blue outlines
        self.blu_lower = np.array([95,  70,  80])
        self.blu_upper = np.array([125, 255, 255])
        # RED team: red outlines (wraps around 0°)
        self.red1_lower = np.array([0,   90, 90])
        self.red1_upper = np.array([8,  255, 255])
        self.red2_lower = np.array([172, 90, 90])
        self.red2_upper = np.array([179, 255, 255])

        # Contour filters
        self.min_area     = 600    # px² — discard tiny noise blobs
        self.min_aspect   = 1.0    # h/w — must be taller than wide
        self.max_aspect   = 5.0
        self.max_blob_w   = self.sw * 0.25   # discard wall-size blobs
        self.max_blob_h   = self.sh * 0.55

        # --- Uber HUD region ---
        # Typical TF2 uber bar sits near bottom-centre-right.
        # These ratios work for most 16:9 resolutions; tune if needed.
        self.uber_roi = {
            'left':   int(self.sw * 0.72),
            'top':    int(self.sh * 0.895),
            'width':  int(self.sw * 0.10),
            'height': int(self.sh * 0.030)
        }
        # Uber bar HSV (bright white→cyan glow when charged)
        self.uber_h_lo = np.array([85,  30, 210])
        self.uber_h_hi = np.array([135, 255, 255])
        self.uber_full_thresh = 0.92

        # --- Communication ---
        self.latest_frame = LatestFrame()
        self.intent_queue = queue.Queue(maxsize=3)

        # Watchdog: if no intent for this long, release all keys
        self.watchdog_timeout = 0.4

        # --- Debug ---
        self._dbg_frame    = None
        self._dbg_mask     = None
        self._dbg_contours = []
        self._dbg_lock     = threading.Lock()

        # --- Flask ---
        self.app = Flask(__name__)
        self._setup_routes()

        logger.info("MedicBot ready. F12=Stop  F11=Pause  D=Debug  U=Auto-Uber")

    # ================================================================
    # Thread-safe properties
    # ================================================================
    def _g(self, attr):
        with self._lock: return getattr(self, attr)
    def _s(self, attr, val):
        with self._lock: setattr(self, attr, val)

    @property
    def running(self):  return self._g('_running')
    @running.setter
    def running(self, v): self._s('_running', v)

    @property
    def paused(self):   return self._g('_paused')
    @paused.setter
    def paused(self, v): self._s('_paused', v)

    @property
    def team(self):     return self._g('_team')
    @team.setter
    def team(self, v):  self._s('_team', v)

    @property
    def uber_percent(self): return self._g('_uber_percent')
    @uber_percent.setter
    def uber_percent(self, v): self._s('_uber_percent', v)

    @property
    def uber_ready(self): return self._g('_uber_ready')
    @uber_ready.setter
    def uber_ready(self, v): self._s('_uber_ready', v)

    # ================================================================
    # Team Detection
    # FIX: old code sampled pixel (552,900) — hardcoded, wrong on
    #      non-1080p screens.  New approach: open scoreboard, look for
    #      a *band* of pixels along the bottom-left corner where the
    #      team colour indicator is reliable across resolutions.
    # ================================================================
    def detect_own_team(self) -> str:
        logger.info("Detecting team (opening scoreboard)…")
        keyboard.press('tab')
        time.sleep(0.35)
        with mss.mss() as sct:
            img   = np.array(sct.grab(self._monitor))
            frame = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)
        keyboard.release('tab')

        # Sample a horizontal strip near the bottom-left where
        # the team score block appears in default TF2 HUD.
        # We look for dominant red vs blue in that region.
        # Region: left 15–35% width, bottom 85–95% height
        x1 = int(self.sw * 0.15); x2 = int(self.sw * 0.35)
        y1 = int(self.sh * 0.85); y2 = int(self.sh * 0.95)
        roi    = frame[y1:y2, x1:x2]
        hsv    = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)

        blu_px = cv2.countNonZero(cv2.inRange(hsv,
                    np.array([95, 80, 80]), np.array([125, 255, 255])))
        red_px = cv2.countNonZero(
                    cv2.bitwise_or(
                        cv2.inRange(hsv, np.array([0,  80, 80]), np.array([8,  255,255])),
                        cv2.inRange(hsv, np.array([172,80, 80]), np.array([179,255,255]))))

        team = "BLU" if blu_px > red_px else "RED"
        logger.info(f"Team detected: {team}  (blu_px={blu_px}, red_px={red_px})")
        return team

    # ================================================================
    # Uber Detection Thread
    # FIX: old code opened mss.mss() every iteration — expensive.
    #      Keep one context open for the lifetime of the thread.
    # ================================================================
    def _uber_loop(self):
        roi_mon = {
            'left':   self.uber_roi['left'],
            'top':    self.uber_roi['top'],
            'width':  self.uber_roi['width'],
            'height': self.uber_roi['height']
        }
        with mss.mss() as sct:
            while self.running:
                if self.paused:
                    time.sleep(0.2)
                    continue
                img   = np.array(sct.grab(roi_mon))
                frame = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)
                hsv   = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
                mask  = cv2.inRange(hsv, self.uber_h_lo, self.uber_h_hi)
                total = mask.shape[0] * mask.shape[1]
                if total > 0:
                    ratio = cv2.countNonZero(mask) / total
                    # Scale: a fully charged bar typically fills ~40 % of the
                    # region with matching colour; 2.5× puts that near 1.0
                    pct = min(1.0, ratio * 2.5)
                else:
                    pct = 0.0
                self.uber_percent = pct
                self.uber_ready   = (pct >= self.uber_full_thresh)
                time.sleep(0.08)

    # ================================================================
    # Capture Loop — keep one mss context open
    # ================================================================
    def _capture_loop(self):
        with mss.mss() as sct:
            while self.running:
                if not self.paused:
                    img   = np.array(sct.grab(self._monitor))
                    frame = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)
                    self.latest_frame.put(frame)
                time.sleep(0.005)

    # ================================================================
    # Detection Loop
    # ================================================================
    def _detection_loop(self):
        while self.running:
            if self.paused:
                time.sleep(0.1)
                continue

            frame = self.latest_frame.get(timeout=1.0)
            if frame is None:
                continue

            players, mask, contours = self._detect_players(frame)

            if SHOW_DEBUG_WINDOW:
                with self._dbg_lock:
                    self._dbg_frame    = frame
                    self._dbg_mask     = mask
                    self._dbg_contours = contours

            tracked = self._update_tracking(players)
            target  = self._select_target(tracked)
            intent  = self._build_intent(target)

            if AUTO_UBER_ENABLED and self.uber_ready and target is not None:
                intent.pop_uber = True
                logger.info("Uber ready — popping!")

            # Drop oldest intent if queue is full (keep it fresh)
            if self.intent_queue.full():
                try: self.intent_queue.get_nowait()
                except queue.Empty: pass
            self.intent_queue.put(intent)

    # ================================================================
    # Player Detection
    # FIX: added exclusion of HUD regions (top 8 % = killfeed / ammo,
    #      bottom 10 % = HUD bar) to avoid false positives.
    #      Also tightened aspect ratio and added convexity check.
    # ================================================================
    def _detect_players(self, frame: np.ndarray):
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)

        if self.team == "BLU":
            mask = cv2.inRange(hsv, self.blu_lower, self.blu_upper)
        elif self.team == "RED":
            m1   = cv2.inRange(hsv, self.red1_lower, self.red1_upper)
            m2   = cv2.inRange(hsv, self.red2_lower, self.red2_upper)
            mask = cv2.bitwise_or(m1, m2)
        else:
            # Team not yet known — return nothing
            return [], np.zeros(frame.shape[:2], np.uint8), []

        # --- Morphology: remove noise, close small gaps ---
        k3 = np.ones((3, 3), np.uint8)
        k5 = np.ones((5, 5), np.uint8)
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN,  k3)
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, k5)

        # --- Blank out HUD regions to avoid false positives ---
        # Top 8 %: killfeed, ammo counter, etc.
        mask[: int(self.sh * 0.08), :] = 0
        # Bottom 10 %: health, ammo, uber HUD bar
        mask[int(self.sh * 0.90): , :] = 0
        # Left/right 3 %: thin UI chrome
        mask[:, :int(self.sw * 0.03)]  = 0
        mask[:, int(self.sw * 0.97):]  = 0

        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL,
                                        cv2.CHAIN_APPROX_SIMPLE)
        players        = []
        valid_contours = []

        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < self.min_area:
                continue

            x, y, w, h = cv2.boundingRect(cnt)
            aspect = h / w if w > 0 else 0

            if not (self.min_aspect <= aspect <= self.max_aspect):
                continue
            if w > self.max_blob_w or h > self.max_blob_h:
                continue

            # Centre of bounding box
            cx = x + w // 2
            cy = y + h // 2

            # Vertical band filter (FIX: was 10-90%, now 8-90% to match HUD mask)
            if not (self.aim_y_min <= cy <= self.aim_y_max):
                continue

            # Convexity sanity check: real player silhouettes are fairly convex
            hull = cv2.convexHull(cnt)
            hull_area = cv2.contourArea(hull)
            if hull_area > 0 and area / hull_area < 0.35:
                continue   # blob is too jagged → likely a wall/texture

            players.append(Player(x=cx, y=cy, team=self.team,
                                  width=w, height=h))
            valid_contours.append(cnt)

        if DEBUG_MODE and players:
            logger.debug(f"Detected {len(players)} player(s)")

        return players, mask, valid_contours

    # ================================================================
    # Tracking — simple nearest-neighbour IoU-free tracker
    # ================================================================
    def _update_tracking(self, detections: List[Player]) -> List[Player]:
        now      = time.time()
        updated  = {}
        unmatched_tracks = list(self._tracked.keys())

        for det in detections:
            best_id, best_dist = None, float('inf')
            for tid in unmatched_tracks:
                prev = self._tracked[tid]
                d    = ((det.x - prev.x)**2 + (det.y - prev.y)**2) ** 0.5
                if d < self.match_dist and d < best_dist:
                    best_dist, best_id = d, tid
            if best_id is not None:
                det.track_id         = best_id
                updated[best_id]     = det
                unmatched_tracks.remove(best_id)
            else:
                nid              = self._next_id
                self._next_id   += 1
                det.track_id     = nid
                updated[nid]     = det

        # Keep recently-seen tracks alive during the grace period
        for tid in unmatched_tracks:
            prev = self._tracked[tid]
            if (now - prev.last_seen) < self.grace_period:
                updated[tid] = prev

        for p in updated.values():
            p.last_seen = now

        with self._lock:
            self._tracked = updated

        return list(updated.values())

    # ================================================================
    # Target Selection
    # ================================================================
    def _select_target(self, players: List[Player]) -> Optional[Tuple[float, float]]:
        now = time.time()

        candidates = [
            p for p in players
            if ((p.x - self.cx)**2 + (p.y - self.cy)**2) ** 0.5 <= self.max_dist
            and self.aim_y_min <= p.y <= self.aim_y_max
        ]

        with self._lock:
            lid  = self._locked_target_id
            llost = self._locked_lost_time

        # Try to keep current lock
        if lid is not None:
            locked = next((p for p in candidates if p.track_id == lid), None)
            if locked:
                with self._lock: self._locked_lost_time = 0.0
                return (locked.x, locked.y)
            else:
                if llost == 0.0:
                    with self._lock: self._locked_lost_time = now
                elif (now - llost) > self.lock_grace:
                    with self._lock:
                        self._locked_target_id  = None
                        self._locked_lost_time  = 0.0
                    logger.debug("Locked target lost — searching for new one")
                else:
                    return None   # still within grace period, don't flick away

        if not candidates:
            return None

        # Pick closest to crosshair
        best = min(candidates,
                   key=lambda p: (p.x - self.cx)**2 + (p.y - self.cy)**2)
        with self._lock:
            self._locked_target_id = best.track_id
            self._locked_lost_time = 0.0
        logger.debug(f"New lock: id={best.track_id} @ ({best.x:.0f},{best.y:.0f})")
        return (best.x, best.y)

    # ================================================================
    # Intent Builder
    # FIX: old code calculated `distance` with unclamped dy, then
    #      recalculated dy after clamping but never used the new
    #      distance — so forward/back follow decisions were based on
    #      the wrong distance.  Rewritten cleanly.
    # ================================================================
    def _build_intent(self, target: Optional[Tuple[float, float]]) -> Intent:
        if target is None:
            return Intent(type=ActionType.STOP)

        tx, ty = target
        dx     = tx - self.cx
        dy     = ty - self.cy
        dist   = (dx**2 + dy**2) ** 0.5

        move_fwd = move_back = move_left = move_right = False
        if dist > self.follow_fwd_thresh:
            move_fwd = True
        elif dist < self.follow_back_thresh and dist > self.deadzone:
            move_back = True
        if dx >  self.follow_strafe_thresh: move_right = True
        if dx < -self.follow_strafe_thresh: move_left  = True

        return Intent(
            type=ActionType.AIM,
            target_x=tx, target_y=ty,
            hold_fire=(self._current_weapon == 2),
            move_forward=move_fwd,
            move_back=move_back,
            move_left=move_left,
            move_right=move_right,
        )

    # ================================================================
    # Controller Loop
    # FIX: mouse movement now uses the correct dx/dy toward the target
    #      directly; the old code clamped target_y then recalculated dy
    #      but kept the original distance check — meaning the bot could
    #      aim well outside the valid band and spin to floor/ceiling.
    #      Now: aim at the detected centre directly; the detection mask
    #      already guarantees the blob is inside the valid band.
    # ================================================================
    def _controller_loop(self):
        keys = {'w': False, 'a': False, 's': False, 'd': False}
        fire_held   = False
        uber_popped = False
        last_intent_time = time.time()

        while self.running:
            if self.paused:
                self._release_all()
                keys = dict.fromkeys(keys, False)
                fire_held = False
                time.sleep(0.1)
                continue

            try:
                intent = self.intent_queue.get(timeout=0.05)
                last_intent_time = time.time()
            except queue.Empty:
                intent = None

            # Watchdog — if nothing arrives, release keys
            if (time.time() - last_intent_time) > self.watchdog_timeout:
                if any(keys.values()) or fire_held:
                    self._release_all()
                    keys = dict.fromkeys(keys, False)
                    fire_held = False
                time.sleep(0.05)
                continue

            if intent is None:
                time.sleep(0.01)
                continue

            # --- Uber ---
            if intent.pop_uber and not uber_popped:
                pyautogui.mouseDown(button='right')
                time.sleep(0.08)
                pyautogui.mouseUp(button='right')
                uber_popped = True
                self.uber_ready = False
            if not intent.pop_uber:
                uber_popped = False

            # --- Aim ---
            if intent.type == ActionType.AIM:
                dx   = intent.target_x - self.cx
                dy   = intent.target_y - self.cy
                dist = (dx**2 + dy**2) ** 0.5

                if dist > self.deadzone:
                    mx = int(dx * self.smoothing * self.mouse_speed)
                    my = int(dy * self.smoothing * self.mouse_speed)
                    mx = max(-self.max_move_px, min(mx, self.max_move_px))
                    my = max(-self.max_move_px, min(my, self.max_move_px))
                    if abs(mx) > 0 or abs(my) > 0:
                        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, mx, my, 0, 0)

                if intent.hold_fire and not fire_held:
                    pyautogui.mouseDown(button='left')
                    fire_held = True
                elif not intent.hold_fire and fire_held:
                    pyautogui.mouseUp(button='left')
                    fire_held = False
            else:
                if fire_held:
                    pyautogui.mouseUp(button='left')
                    fire_held = False

            # --- WASD ---
            desired = {
                'w': intent.move_forward,
                's': intent.move_back,
                'a': intent.move_left,
                'd': intent.move_right,
            }
            for k, want in desired.items():
                if want != keys[k]:
                    if want: keyboard.press(k)
                    else:    keyboard.release(k)
                    keys[k] = want

            time.sleep(0.01)

    # ================================================================
    # Debug Window
    # ================================================================
    def _debug_loop(self):
        global SHOW_DEBUG_WINDOW
        while self.running:
            if not SHOW_DEBUG_WINDOW:
                cv2.destroyAllWindows()
                time.sleep(0.5)
                continue

            with self._dbg_lock:
                if self._dbg_frame is None:
                    time.sleep(0.1)
                    continue
                frame    = self._dbg_frame.copy()
                mask     = self._dbg_mask.copy() if self._dbg_mask is not None else None
                contours = list(self._dbg_contours)

            disp = frame.copy()
            for cnt in contours:
                x, y, w, h = cv2.boundingRect(cnt)
                cv2.rectangle(disp, (x, y), (x+w, y+h), (0, 255, 0), 2)
                cv2.circle(disp, (x+w//2, y+h//2), 4, (0, 0, 255), -1)

            # Crosshair
            cv2.line(disp, (self.cx-12, self.cy), (self.cx+12, self.cy), (255,255,255), 1)
            cv2.line(disp, (self.cx, self.cy-12), (self.cx, self.cy+12), (255,255,255), 1)

            # Aim band
            cv2.line(disp, (0, self.aim_y_min), (self.sw, self.aim_y_min), (0,255,255), 1)
            cv2.line(disp, (0, self.aim_y_max), (self.sw, self.aim_y_max), (0,255,255), 1)

            # Uber ROI
            ur = self.uber_roi
            cv2.rectangle(disp,
                          (ur['left'], ur['top']),
                          (ur['left']+ur['width'], ur['top']+ur['height']),
                          (0, 0, 255), 2)

            # Overlay text
            cv2.putText(disp, f"Team: {self.team}", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0,255,255), 2)
            cv2.putText(disp, f"Uber: {self.uber_percent*100:.0f}%", (10, 60),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0,255,0), 2)

            if mask is not None:
                mask_bgr = cv2.cvtColor(mask, cv2.COLOR_GRAY2BGR)
                combined = np.hstack((
                    cv2.resize(disp,      (800, 450)),
                    cv2.resize(mask_bgr,  (800, 450))
                ))
            else:
                combined = cv2.resize(disp, (800, 450))

            cv2.namedWindow("MedicBot Debug", cv2.WINDOW_NORMAL)
            cv2.imshow("MedicBot Debug", combined)
            if cv2.waitKey(1) & 0xFF == ord('q'):
                SHOW_DEBUG_WINDOW = False
            time.sleep(0.033)

        cv2.destroyAllWindows()

    # ================================================================
    # Helpers
    # ================================================================
    def _release_all(self):
        try:
            pyautogui.mouseUp(button='left')
            pyautogui.mouseUp(button='right')
            for k in ('w', 'a', 's', 'd'):
                keyboard.release(k)
        except Exception:
            pass

    # ================================================================
    # Flask API
    # ================================================================
    def _setup_routes(self):
        app = self.app

        @app.route('/status')
        def status():
            return jsonify({
                'running':          self.running,
                'paused':           self.paused,
                'team':             self.team,
                'locked_target_id': self._locked_target_id,
                'uber_percent':     self.uber_percent,
                'uber_ready':       self.uber_ready,
            })

        @app.route('/start', methods=['POST'])
        def start():
            if not self.running:
                self.team    = self.detect_own_team()
                self.running = True
                self.paused  = False
                for target_fn in (
                    self._capture_loop,
                    self._detection_loop,
                    self._controller_loop,
                    self._uber_loop,
                    self._debug_loop,
                ):
                    threading.Thread(target=target_fn, daemon=True).start()
                logger.info(f"Bot started — team={self.team}")
            return jsonify({'status': 'started', 'team': self.team})

        @app.route('/stop', methods=['POST'])
        def stop():
            self.running = False
            self._release_all()
            return jsonify({'status': 'stopped'})

        @app.route('/pause', methods=['POST'])
        def pause():
            self.paused = True
            self._release_all()
            return jsonify({'status': 'paused'})

        @app.route('/resume', methods=['POST'])
        def resume():
            self.paused = False
            return jsonify({'status': 'resumed'})

        @app.route('/set_team', methods=['POST'])
        def set_team():
            t = (request.json or {}).get('team', '').upper()
            if t in ('RED', 'BLU'):
                self.team = t
                return jsonify({'team': t})
            return jsonify({'error': 'Invalid team — use RED or BLU'}), 400

        @app.route('/uber_status')
        def uber_status():
            return jsonify({'percent': self.uber_percent, 'ready': self.uber_ready})

    def run(self):
        """Block until killed."""
        self.app.run(host='0.0.0.0', port=5000, threaded=True, use_reloader=False)


# ===================================================================
# Entry Point
# ===================================================================
if __name__ == '__main__':
    bot = MedicBot()

    def _stop():
        logger.info("EMERGENCY STOP (F12)")
        bot.running = False
        bot._release_all()

    def _pause():
        bot.paused = not bot.paused
        logger.info(f"{'Paused' if bot.paused else 'Resumed'}")
        if bot.paused:
            bot._release_all()

    def _debug():
        global SHOW_DEBUG_WINDOW
        SHOW_DEBUG_WINDOW = not SHOW_DEBUG_WINDOW
        logger.info(f"Debug window: {SHOW_DEBUG_WINDOW}")

    def _uber():
        global AUTO_UBER_ENABLED
        AUTO_UBER_ENABLED = not AUTO_UBER_ENABLED
        logger.info(f"Auto-Uber: {AUTO_UBER_ENABLED}")

    keyboard.add_hotkey('f12', _stop)
    keyboard.add_hotkey('f11', _pause)
    keyboard.add_hotkey('d',   _debug)
    keyboard.add_hotkey('u',   _uber)

    logger.info("Hotkeys: F12=Stop  F11=Pause/Resume  D=Debug  U=Auto-Uber")
    logger.info("POST to http://localhost:5000/start to begin")

    try:
        bot.run()
    except KeyboardInterrupt:
        bot.running = False
        logger.info("Shutdown via Ctrl+C")