#!/usr/bin/env python3
"""
Medic-AI Bot Server - Stable Build
Fixes applied: MSS Thread-Safety, Detection Throttling, HTTP Method Correction
"""

import cv2
import numpy as np
import time
import threading
import queue
import logging
from dataclasses import dataclass
from typing import List, Optional, Tuple
from flask import Flask, request, jsonify
import mss
import pyautogui
import keyboard
import win32api
import win32con
from PIL import Image
import torch
from transformers import pipeline

# Logging configuration
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    datefmt='%H:%M:%S'
)
logger = logging.getLogger(__name__)

@dataclass
class Player:
    x: float
    y: float
    team: str = "unknown"
    health: int = 100
    width: float = 0.0
    height: float = 0.0

class MedicBot:
    def __init__(self):
        self.running = False
        self.paused = False
        self.team = "unknown"
        self.priority_mode = "lowest_health"

        # Screen info (Init is fine for single-use mss)
        with mss.mss() as sct:
            monitor = sct.monitors[1]
            self.monitor = monitor
            self.screen_width = monitor['width']
            self.screen_height = monitor['height']
            self.screen_center = (self.screen_width // 2, self.screen_height // 2)

        self.mouse_speed = 2.0
        self.smoothing = 0.3
        self.current_target = None

        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.detector = None
        self._load_vision_models()

        self.frame_queue = queue.Queue(maxsize=2)
        self.result_queue = queue.Queue(maxsize=2)

        self.app = Flask(__name__)
        self._setup_routes()

    def _load_vision_models(self):
        logger.info(f"Loading vision model on {self.device}...")
        self.detector = pipeline(
            model="google/owlv2-base-patch16-ensemble",
            task="zero-shot-object-detection",
            device=self.device
        )
        logger.info("Model loaded successfully.")

    # ---------------- CAPTURE (FIXED: Local MSS & Queue Management) ----------------
    def _capture_loop(self):
        logger.info("Capture thread started.")
        sct = mss.mss()  # CRITICAL: Local thread instance

        while self.running:
            if self.paused:
                time.sleep(0.1)
                continue
            try:
                img = sct.grab(self.monitor)
                frame = np.array(img)
                frame = cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR)

                if self.frame_queue.full():
                    try:
                        self.frame_queue.get_nowait()
                    except queue.Empty:
                        pass
                self.frame_queue.put(frame)

            except Exception as e:
                logger.error(f"Capture error: {e}")
            
            time.sleep(0.01)

    # ---------------- DETECTION (FIXED: Throttling) ----------------
    def _detection_loop(self):
        logger.info("Detection thread started.")
        while self.running:
            try:
                # Use timeout to allow the loop to check self.running status
                frame = self.frame_queue.get(timeout=1)
                
                players = self._detect_players(frame)
                target = self._select_target(players)

                if self.result_queue.full():
                    try:
                        self.result_queue.get_nowait()
                    except queue.Empty:
                        pass
                self.result_queue.put((target, players))

                # Optional: Debug window
                debug = self._draw_debug(frame, players, target)
                cv2.imshow("Medic-AI Debug", debug)
                cv2.waitKey(1)
                
                # FIXED: Throttling to prevent OWL-ViT from overwhelming the system
                time.sleep(0.05) 

            except queue.Empty:
                continue
            except Exception as e:
                logger.error(f"Detection error: {e}")

    def _detect_players(self, frame):
        players = []
        pil = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))

        preds = self.detector(pil, candidate_labels=["person"])

        for p in preds:
            if p["score"] < 0.15:
                continue

            box = p["box"]
            x1, y1, x2, y2 = box["xmin"], box["ymin"], box["xmax"], box["ymax"]

            players.append(Player(
                x=(x1+x2)/2,
                y=(y1+y2)/2,
                width=x2-x1,
                height=y2-y1
            ))
        return players

    def _select_target(self, players):
        if not players:
            return None
        # Finds the player closest to the center of the screen
        return min(players, key=lambda p: (p.x - self.screen_center[0])**2 + (p.y - self.screen_center[1])**2)

    # ---------------- MOVEMENT (FIXED: Dataclass Attribute Access) ----------------
    def _movement_loop(self):
        logger.info("Movement thread started.")
        while self.running:
            try:
                target, _ = self.result_queue.get(timeout=1)
            except queue.Empty:
                target = None

            if target:
                # FIXED: target is a Player object, use .x and .y
                dx = target.x - self.screen_center[0]
                dy = target.y - self.screen_center[1]

                move_x = int(dx * self.smoothing * self.mouse_speed)
                move_y = int(dy * self.smoothing * self.mouse_speed)

                if abs(move_x) > 1 or abs(move_y) > 1:
                    win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, move_x, move_y, 0, 0)

            time.sleep(0.016) # ~60Hz update rate

    # ---------------- TEAM DETECTION (FIXED: Local MSS) ----------------
    def detect_own_team(self):
        logger.info("Detecting team...")
        keyboard.press('tab')
        time.sleep(0.4)

        with mss.mss() as sct:
            img = sct.grab(self.monitor)

        keyboard.release('tab')
        # Placeholder for team color logic
        return "unknown"

    # ---------------- DEBUG DRAWING ----------------
    def _draw_debug(self, frame, players, target):
        debug = frame.copy()
        for p in players:
            x1 = int(p.x - p.width/2)
            y1 = int(p.y - p.height/2)
            x2 = int(p.x + p.width/2)
            y2 = int(p.y + p.height/2)
            cv2.rectangle(debug, (x1,y1),(x2,y2),(0,255,0),2)

        if target:
            cv2.circle(debug, (int(target.x), int(target.y)), 10, (0,255,255), 2)
        return debug

    # ---------------- API ROUTES (FIXED: Method & Local MSS) ----------------
    def _setup_routes(self):
        @self.app.route('/status')
        def status():
            return jsonify({"running": self.running, "paused": self.paused})

        @self.app.route('/start', methods=['POST']) # FIXED: Must be POST
        def start():
            if not self.running:
                self.running = True
                self.team = self.detect_own_team()

                threading.Thread(target=self._capture_loop, daemon=True).start()
                threading.Thread(target=self._detection_loop, daemon=True).start()
                threading.Thread(target=self._movement_loop, daemon=True).start()

                logger.info("Bot engine started via API.")
            return jsonify({"status": "started"})

        @self.app.route('/stop', methods=['POST'])
        def stop():
            self.running = False
            return jsonify({"status": "stopped"})

        @self.app.route('/screenshot')
        def screenshot():
            # FIXED: Local MSS for API thread
            with mss.mss() as sct:
                img = sct.grab(self.monitor)
                frame = np.array(img)
                frame = cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR)
                _, buf = cv2.imencode(".jpg", frame)
                return buf.tobytes(), 200, {'Content-Type': 'image/jpeg'}

    def run(self):
        logger.info("Starting Flask server on port 5000...")
        self.app.run(host="0.0.0.0", port=5000, threaded=True)

# ---------------- MAIN ----------------
if __name__ == "__main__":
    bot = MedicBot()
    bot.run()