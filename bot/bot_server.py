#!/usr/bin/env python3
"""
Medic-AI Bot Server - Brain Update
Fixes applied:
  - MSS thread-safety, detection throttling, HTTP method correction (original)
  - FSM brain: HEALING / DEFENDING / RETREATING states
  - Weapon switching via pydirectinput (slots 1/2/3)
  - Priority-only heal mode
  - HUD health & uber reading via OpenCV colour sampling
  - GET /config + POST /config endpoints (GUI-customisable thresholds)
  - Config persistence to bot_config.json
"""

import cv2
import numpy as np
import time
import threading
import queue
import logging
import json
import os
from dataclasses import dataclass
from enum import Enum
from typing import List, Optional
from flask import Flask, request, jsonify
import mss
import pyautogui
import keyboard
import win32api
import win32con
from PIL import Image
import torch
from transformers import pipeline

# pydirectinput is required for reliable in-game key/mouse input
try:
    import pydirectinput
    PYDIRECTINPUT_AVAILABLE = True
except ImportError:
    PYDIRECTINPUT_AVAILABLE = False

# ── Logging ───────────────────────────────────────────────────────────────────

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    datefmt='%H:%M:%S'
)
logger = logging.getLogger(__name__)

if not PYDIRECTINPUT_AVAILABLE:
    logger.warning(
        "pydirectinput not installed – weapon switching and auto-heal are disabled.\n"
        "  Fix: pip install pydirectinput"
    )

CONFIG_FILE = "bot_config.json"

# ══════════════════════════════════════════════════════════════════════════════
# FSM state enum
# ══════════════════════════════════════════════════════════════════════════════

class BotState(Enum):
    HEALING    = 1   # actively healing priority target
    DEFENDING  = 2   # no target visible – primary weapon out
    RETREATING = 3   # own health critical – release beam, melee/run

# ══════════════════════════════════════════════════════════════════════════════
# Player dataclass
# ══════════════════════════════════════════════════════════════════════════════

@dataclass
class Player:
    x: float
    y: float
    team: str  = "unknown"
    health: int = 100
    width: float = 0.0
    height: float = 0.0

# ══════════════════════════════════════════════════════════════════════════════
# MedicBrain  –  10 Hz decision loop
# ══════════════════════════════════════════════════════════════════════════════

class MedicBrain:
    """
    Reads shared bot state every 100 ms and decides which weapon slot to hold
    and whether to press / release mouse buttons.

    All threshold values live on the parent MedicBot instance so the GUI can
    update them live via POST /config without restarting.
    """

    def __init__(self, bot: "MedicBot"):
        self.bot              = bot
        self.current_state    = BotState.HEALING
        self.current_weapon   = 2        # start with Medigun (slot 2)
        self._running         = False
        self._heal_held       = False

    # ── lifecycle ─────────────────────────────────────────────────────────────

    def start(self):
        self._running = True
        threading.Thread(target=self._loop, daemon=True, name="MedicBrain").start()
        logger.info("[Brain] Started.")

    def stop(self):
        self._running = False
        self._release_heal()          # always clean up mouse state on stop

    # ── main loop ─────────────────────────────────────────────────────────────

    def _loop(self):
        while self._running and self.bot.running:
            try:
                self._decide()
            except Exception as exc:
                logger.error(f"[Brain] Error: {exc}")
            time.sleep(0.1)

    def _decide(self):
        bot = self.bot

        # --- gather information ---
        target       = self._get_priority_target()
        has_target   = target is not None
        my_health    = self._read_my_health()
        uber_pct     = self._read_uber_percent()

        # --- choose next state ---
        if my_health < bot.retreat_health_threshold:
            next_state = BotState.RETREATING
        elif has_target:
            next_state = BotState.HEALING
        else:
            next_state = BotState.DEFENDING

        if next_state != self.current_state:
            logger.info(f"[Brain] {self.current_state.name} → {next_state.name}  "
                        f"(hp={my_health}, uber={uber_pct:.0f}%)")
            self.current_state = next_state

        # --- execute state actions ---
        if self.current_state == BotState.HEALING:
            self._switch_weapon(2)                          # Medigun
            if bot.auto_heal and has_target:
                self._hold_heal()
            else:
                self._release_heal()
            if bot.auto_uber and uber_pct >= bot.uber_pop_threshold:
                self._pop_uber()

        elif self.current_state == BotState.DEFENDING:
            self._release_heal()
            self._switch_weapon(1)                          # primary

        elif self.current_state == BotState.RETREATING:
            self._release_heal()
            slot = 3 if bot.prefer_melee_for_retreat else 1
            self._switch_weapon(slot)

    # ── information readers ───────────────────────────────────────────────────

    def _get_priority_target(self) -> Optional[Player]:
        """
        Returns current_target if it should be healed.
        When priority_only_heal is True and a priority list exists the bot
        will only track detected players (name-matching is deferred until
        OCR is wired in; for now the first detected player is accepted).
        """
        target = self.bot.current_target
        if target is None:
            return None
        if not self.bot.priority_only_heal or not self.bot.priority_players:
            return target
        # Name matching is a TODO once HUD OCR is integrated.
        # For now, accept any detected player when priority list is set.
        return target

    def _read_my_health(self) -> int:
        """
        Approximate own health by sampling the TF2 HUD health-cross region.
        The cross is white at full HP and turns red when low.
        Region: bottom-left corner of the screen.
        """
        try:
            with mss.mss() as sct:
                raw = sct.grab(self.bot.monitor)
            frame = np.array(raw)
            h, w  = frame.shape[:2]
            # Rough TF2 HUD health-cross location (bottom-left quadrant)
            region = frame[int(h * 0.83):int(h * 0.93),
                           int(w * 0.03):int(w * 0.11)]
            # White pixels (all channels high) indicate full health.
            # When health falls the cross dims; we measure mean brightness.
            brightness = float(np.mean(region))          # 0–255
            # Map 255 → 150 HP,  80 → 1 HP  (rough linear scale)
            hp = int(np.clip((brightness / 255.0) * 150, 1, 150))
            return hp
        except Exception:
            return 100  # safe fallback – don't retreat on read failure

    def _read_uber_percent(self) -> float:
        """
        Approximate ÜberCharge % from the HUD meter (bottom-right area).
        The bar glows bright blue/white when charged.
        """
        try:
            with mss.mss() as sct:
                raw = sct.grab(self.bot.monitor)
            frame = np.array(raw)
            h, w  = frame.shape[:2]
            # Uber bar region (bottom-right of HUD)
            region = frame[int(h * 0.87):int(h * 0.91),
                           int(w * 0.60):int(w * 0.78)]
            brightness = float(np.mean(region))
            return float(np.clip((brightness / 255.0) * 100, 0.0, 100.0))
        except Exception:
            return 0.0

    # ── input helpers ─────────────────────────────────────────────────────────

    def _switch_weapon(self, slot: int):
        if self.current_weapon == slot:
            return
        if PYDIRECTINPUT_AVAILABLE:
            pydirectinput.press(str(slot))
        self.current_weapon = slot
        logger.debug(f"[Brain] Weapon → slot {slot}")

    def _hold_heal(self):
        if not self._heal_held:
            if PYDIRECTINPUT_AVAILABLE:
                pydirectinput.mouseDown(button="left")
            self._heal_held = True

    def _release_heal(self):
        if self._heal_held:
            if PYDIRECTINPUT_AVAILABLE:
                pydirectinput.mouseUp(button="left")
            self._heal_held = False

    def _pop_uber(self):
        if PYDIRECTINPUT_AVAILABLE:
            pydirectinput.click(button="right")
        logger.info("[Brain] Über deployed!")

# ══════════════════════════════════════════════════════════════════════════════
# MedicBot
# ══════════════════════════════════════════════════════════════════════════════

class MedicBot:
    def __init__(self):
        self.running = False
        self.paused  = False
        self.team    = "unknown"
        self.priority_mode = "lowest_health"

        # ── Decision thresholds (all customisable via POST /config) ───────────
        self.retreat_health_threshold: int   = 50     # HP below which to retreat
        self.defend_enemy_distance:    int   = 300    # px – reserved for enemy detection
        self.uber_pop_threshold:       float = 95.0   # uber % to auto-pop
        self.prefer_melee_for_retreat: bool  = True   # melee = speed boost
        self.auto_heal:                bool  = True   # hold M1 on target
        self.auto_uber:                bool  = True   # auto right-click uber
        self.priority_only_heal:       bool  = True   # ignore non-priority players
        self.priority_players:         list  = []     # names from GUI priority list

        # Load saved config before anything else
        self._load_config()

        # ── Screen info ───────────────────────────────────────────────────────
        with mss.mss() as sct:
            monitor = sct.monitors[1]
            self.monitor       = monitor
            self.screen_width  = monitor['width']
            self.screen_height = monitor['height']
            self.screen_center = (self.screen_width // 2, self.screen_height // 2)

        self.mouse_speed    = 2.0
        self.smoothing      = 0.3
        self.current_target: Optional[Player] = None

        # ── Vision model ──────────────────────────────────────────────────────
        self.device   = "cuda" if torch.cuda.is_available() else "cpu"
        self.detector = None
        self._load_vision_models()

        # ── Thread queues ─────────────────────────────────────────────────────
        self.frame_queue  = queue.Queue(maxsize=2)
        self.result_queue = queue.Queue(maxsize=2)

        self.brain: Optional[MedicBrain] = None

        # ── Flask app ─────────────────────────────────────────────────────────
        self.app = Flask(__name__)
        self._setup_routes()

    # ── Config persistence ────────────────────────────────────────────────────

    def _load_config(self):
        if not os.path.exists(CONFIG_FILE):
            return
        try:
            with open(CONFIG_FILE, "r") as f:
                d = json.load(f)
            self.retreat_health_threshold = int(  d.get("retreat_health_threshold", 50))
            self.defend_enemy_distance    = int(  d.get("defend_enemy_distance",    300))
            self.uber_pop_threshold       = float(d.get("uber_pop_threshold",       95.0))
            self.prefer_melee_for_retreat = bool( d.get("prefer_melee_for_retreat", True))
            self.auto_heal                = bool( d.get("auto_heal",                True))
            self.auto_uber                = bool( d.get("auto_uber",                True))
            self.priority_only_heal       = bool( d.get("priority_only_heal",       True))
            self.priority_players         = list( d.get("priority_players",         []))
            logger.info("[Config] Loaded from bot_config.json")
        except Exception as exc:
            logger.warning(f"[Config] Could not load saved config: {exc}")

    def _save_config(self):
        try:
            with open(CONFIG_FILE, "w") as f:
                json.dump({
                    "retreat_health_threshold": self.retreat_health_threshold,
                    "defend_enemy_distance":    self.defend_enemy_distance,
                    "uber_pop_threshold":       self.uber_pop_threshold,
                    "prefer_melee_for_retreat": self.prefer_melee_for_retreat,
                    "auto_heal":                self.auto_heal,
                    "auto_uber":                self.auto_uber,
                    "priority_only_heal":       self.priority_only_heal,
                    "priority_players":         self.priority_players,
                }, f, indent=2)
        except Exception as exc:
            logger.warning(f"[Config] Save failed: {exc}")

    # ── Vision ────────────────────────────────────────────────────────────────

    def _load_vision_models(self):
        logger.info(f"Loading vision model on {self.device}...")
        self.detector = pipeline(
            model="google/owlv2-base-patch16-ensemble",
            task="zero-shot-object-detection",
            device=self.device
        )
        logger.info("Model loaded.")

    # ── Capture loop (FIXED: local MSS instance) ──────────────────────────────

    def _capture_loop(self):
        logger.info("Capture thread started.")
        sct = mss.mss()      # thread-local instance
        while self.running:
            if self.paused:
                time.sleep(0.1)
                continue
            try:
                img   = sct.grab(self.monitor)
                frame = np.array(img)
                frame = cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR)
                if self.frame_queue.full():
                    try: self.frame_queue.get_nowait()
                    except queue.Empty: pass
                self.frame_queue.put(frame)
            except Exception as exc:
                logger.error(f"Capture error: {exc}")
            time.sleep(0.01)

    # ── Detection loop (FIXED: throttled) ────────────────────────────────────

    def _detection_loop(self):
        logger.info("Detection thread started.")
        while self.running:
            try:
                frame   = self.frame_queue.get(timeout=1)
                players = self._detect_players(frame)
                target  = self._select_target(players)
                self.current_target = target        # shared with MedicBrain

                if self.result_queue.full():
                    try: self.result_queue.get_nowait()
                    except queue.Empty: pass
                self.result_queue.put((target, players))

                debug = self._draw_debug(frame, players, target)
                cv2.imshow("Medic-AI Debug", debug)
                cv2.waitKey(1)
                time.sleep(0.05)    # throttle OWL-ViT
            except queue.Empty:
                continue
            except Exception as exc:
                logger.error(f"Detection error: {exc}")

    def _detect_players(self, frame: np.ndarray) -> List[Player]:
        players = []
        pil   = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        preds = self.detector(pil, candidate_labels=["person"])
        for p in preds:
            if p["score"] < 0.15:
                continue
            b = p["box"]
            x1, y1, x2, y2 = b["xmin"], b["ymin"], b["xmax"], b["ymax"]
            players.append(Player(
                x=(x1+x2)/2, y=(y1+y2)/2,
                width=x2-x1, height=y2-y1
            ))
        return players

    def _select_target(self, players: List[Player]) -> Optional[Player]:
        if not players:
            return None
        cx, cy = self.screen_center
        return min(players, key=lambda p: (p.x - cx)**2 + (p.y - cy)**2)

    # ── Movement loop (FIXED: dataclass attribute access) ─────────────────────

    def _movement_loop(self):
        logger.info("Movement thread started.")
        while self.running:
            try:
                target, _ = self.result_queue.get(timeout=1)
            except queue.Empty:
                target = None
            if target:
                dx     = target.x - self.screen_center[0]
                dy     = target.y - self.screen_center[1]
                move_x = int(dx * self.smoothing * self.mouse_speed)
                move_y = int(dy * self.smoothing * self.mouse_speed)
                if abs(move_x) > 1 or abs(move_y) > 1:
                    win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, move_x, move_y, 0, 0)
            time.sleep(0.016)   # ~60 Hz

    # ── Team detection ────────────────────────────────────────────────────────

    def detect_own_team(self) -> str:
        logger.info("Detecting team...")
        keyboard.press('tab')
        time.sleep(0.4)
        with mss.mss() as sct:
            sct.grab(self.monitor)
        keyboard.release('tab')
        return "unknown"

    # ── Debug overlay ─────────────────────────────────────────────────────────

    def _draw_debug(self, frame: np.ndarray,
                    players: List[Player],
                    target: Optional[Player]) -> np.ndarray:
        debug = frame.copy()
        for p in players:
            x1 = int(p.x - p.width  / 2)
            y1 = int(p.y - p.height / 2)
            x2 = int(p.x + p.width  / 2)
            y2 = int(p.y + p.height / 2)
            cv2.rectangle(debug, (x1, y1), (x2, y2), (0, 255, 0), 2)
        if target:
            cv2.circle(debug, (int(target.x), int(target.y)), 10, (0, 255, 255), 2)
        if self.brain:
            state_color = {
                BotState.HEALING:   (0, 220, 255),
                BotState.DEFENDING: (0, 80, 255),
                BotState.RETREATING:(0, 100, 255),
            }.get(self.brain.current_state, (200, 200, 200))
            cv2.putText(debug, f"State: {self.brain.current_state.name}",
                        (10, 36), cv2.FONT_HERSHEY_SIMPLEX, 1.1, state_color, 2)
        return debug

    # ── API routes ────────────────────────────────────────────────────────────

    def _setup_routes(self):

        # ── /status ───────────────────────────────────────────────────────────
        @self.app.route('/status')
        def status():
            return jsonify({
                "running": self.running,
                "paused":  self.paused,
                "state":   self.brain.current_state.name if self.brain else "IDLE",
            })

        # ── /start (FIXED: POST) ──────────────────────────────────────────────
        @self.app.route('/start', methods=['POST'])
        def start():
            if not self.running:
                self.running = True
                self.team    = self.detect_own_team()

                threading.Thread(target=self._capture_loop,   daemon=True).start()
                threading.Thread(target=self._detection_loop, daemon=True).start()
                threading.Thread(target=self._movement_loop,  daemon=True).start()

                self.brain = MedicBrain(self)
                self.brain.start()

                logger.info("Bot started via API.")
            return jsonify({"status": "started"})

        # ── /stop ─────────────────────────────────────────────────────────────
        @self.app.route('/stop', methods=['POST'])
        def stop():
            self.running = False
            if self.brain:
                self.brain.stop()
                self.brain = None
            return jsonify({"status": "stopped"})

        # ── GET /config ───────────────────────────────────────────────────────
        @self.app.route('/config', methods=['GET'])
        def get_config():
            return jsonify({
                "retreat_health_threshold": self.retreat_health_threshold,
                "defend_enemy_distance":    self.defend_enemy_distance,
                "uber_pop_threshold":       self.uber_pop_threshold,
                "prefer_melee_for_retreat": self.prefer_melee_for_retreat,
                "auto_heal":                self.auto_heal,
                "auto_uber":                self.auto_uber,
                "priority_only_heal":       self.priority_only_heal,
                "priority_players":         self.priority_players,
            })

        # ── POST /config  (GUI customisation endpoint) ────────────────────────
        @self.app.route('/config', methods=['POST'])
        def set_config():
            data = request.json or {}

            if "retreat_health_threshold" in data:
                self.retreat_health_threshold = int(  data["retreat_health_threshold"])
            if "defend_enemy_distance" in data:
                self.defend_enemy_distance    = int(  data["defend_enemy_distance"])
            if "uber_pop_threshold" in data:
                self.uber_pop_threshold       = float(data["uber_pop_threshold"])
            if "prefer_melee_for_retreat" in data:
                self.prefer_melee_for_retreat = bool( data["prefer_melee_for_retreat"])
            if "auto_heal" in data:
                self.auto_heal                = bool( data["auto_heal"])
            if "auto_uber" in data:
                self.auto_uber                = bool( data["auto_uber"])
            if "priority_only_heal" in data:
                self.priority_only_heal       = bool( data["priority_only_heal"])
            if "priority_players" in data:
                self.priority_players         = list( data["priority_players"])

            self._save_config()
            logger.info(f"[Config] Updated: {data}")
            return jsonify({"status": "ok"})

        # ── /screenshot (FIXED: local MSS) ────────────────────────────────────
        @self.app.route('/screenshot')
        def screenshot():
            with mss.mss() as sct:
                img   = sct.grab(self.monitor)
                frame = np.array(img)
                frame = cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR)
                _, buf = cv2.imencode(".jpg", frame)
                return buf.tobytes(), 200, {'Content-Type': 'image/jpeg'}

    # ── entry point ───────────────────────────────────────────────────────────

    def run(self):
        logger.info("Starting Flask server on port 5000...")
        self.app.run(host="0.0.0.0", port=5000, threaded=True)


# ── main ──────────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    bot = MedicBot()
    bot.run()