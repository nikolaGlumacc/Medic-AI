#!/usr/bin/env python3
import cv2
import numpy as np
import time
import threading
import queue
from collections import deque
import logging
import json
import os
import pytesseract
import pyttsx3
import random
from dataclasses import dataclass, field
from enum import Enum
import mss
import win32api
import win32con
from flask import Flask, request, jsonify
import asyncio
import websockets
import psutil

try:
    import pydirectinput
except ImportError:
    logging.warning("pydirectinput not installed. Some input controls will be no-ops. pip install pydirectinput")
    pydirectinput = None

try:
    import pyautogui
except ImportError:
    pyautogui = None

from transformers import pipeline

logging.basicConfig(level=logging.INFO, format='%(asctime)s [%(levelname)s] %(message)s')
logger = logging.getLogger("MedicAI")

# ---------------- CONFIG AND STATE ----------------

class BotState(Enum):
    SEARCHING = 1
    HEALING = 2
    DEFENDING = 3
    RETREATING = 4

# Globals & Config
CONFIG = {
    "retreat_health_threshold": 50,
    "defend_enemy_distance": 300,
    "uber_pop_threshold": 95.0,
    "prefer_melee_for_retreat": True,
    "auto_heal": True,
    "auto_uber": True,
    "priority_only_heal": True,
    "priority_players": [],
    "follow_distance": 50,
    "mouse_speed": 2.0,
    "smoothing": 0.3,
    "spy_check_frequency": 8,
    "spy_alertness_duration": 18,
    "spy_check_camera_flick_speed": 180,
    "idle_rotation_speed": 18,
    "scoreboard_check_frequency": 30,
    "tab_hold_duration": 0.25,
    "auto_whitelist_detection": True,
    "cpu_throttle_threshold": 85,
    "screen_capture_fps_limit": 60,
    "tesseract_path": r"C:\Program Files\Tesseract-OCR\tesseract.exe",
    "stuck_detection_retry": 2.0
}

def load_config():
    global CONFIG
    if os.path.exists("bot_config.json"):
        try:
            with open("bot_config.json", "r") as f:
                loaded = json.load(f)
                CONFIG.update(loaded)
        except Exception as e:
            logger.error(f"Error loading config: {e}")

def save_config():
    try:
        with open("bot_config.json", "w") as f:
            json.dump(CONFIG, f, indent=4)
    except Exception as e:
        logger.error(f"Error saving config: {e}")

load_config()
if os.path.exists(CONFIG.get("tesseract_path", "")):
    pytesseract.pytesseract.tesseract_cmd = CONFIG["tesseract_path"]

# ---------------- THREAD-SAFE STATE ----------------
class StateManager:
    def __init__(self):
        self.lock = threading.Lock()
        self.running = False
        self.state = BotState.SEARCHING
        self.uber_percent = 0.0
        self.my_health = 150
        self.session_seconds = 0
        self.melee_kills = 0
        self.current_target = None
        self.last_stuck_pos = None
        self.stuck_time = 0
        self.start_time = time.time()
        self.cpu_throttled = False
        self.active_cooldown = 0
        self.last_spy_check = time.time()

# ---------------- PIPELINES ----------------

class VisionPipeline:
    def __init__(self):
        self.detector = pipeline("object-detection", model="google/owlv2-base-patch16-ensemble", device=-1)
        self.frame_buffer = queue.Queue(maxsize=1)
        self.running = False
        self.team_color_filter = "RED" # Simplified logic

    def capture_loop(self, state_mgr):
        # Thread-local mss instance
        with mss.mss() as sct:
            monitor = sct.monitors[1]  # primary
            fps_limit = CONFIG["screen_capture_fps_limit"]
            delay = 1.0 / fps_limit if fps_limit > 0 else 0.016
            
            while state_mgr.running:
                start_time = time.time()
                img = sct.grab(monitor)
                frame = cv2.cvtColor(np.array(img), cv2.COLOR_BGRA2BGR)
                if self.frame_buffer.full():
                    try: self.frame_buffer.get_nowait()
                    except: pass
                self.frame_buffer.put(frame)
                
                # CPU Throttling adjustment
                sleep_time = delay
                if state_mgr.cpu_throttled: sleep_time *= 2
                
                elapsed = time.time() - start_time
                if elapsed < sleep_time:
                    time.sleep(sleep_time - elapsed)

class InputManager:
    def __init__(self):
        self.current_slot = 1
        self._heal_held = False

    def check_pydirectinput(self):
        if pydirectinput is None: return False
        return True

    def switch_weapon(self, slot):
        if not self.check_pydirectinput(): return
        if self.current_slot != slot:
            if slot == 1: pydirectinput.press('1')
            elif slot == 2: pydirectinput.press('2')
            elif slot == 3: pydirectinput.press('3')
            self.current_slot = slot
            time.sleep(0.1)

    def hold_heal(self):
        if not self.check_pydirectinput(): return
        if not self._heal_held:
            pydirectinput.mouseDown(button='left')
            self._heal_held = True

    def release_heal(self):
        if not self.check_pydirectinput(): return
        if self._heal_held:
            pydirectinput.mouseUp(button='left')
            self._heal_held = False

    def pop_uber(self):
        if not self.check_pydirectinput(): return
        pydirectinput.click(button='right')

    def move_camera(self, dx, dy):
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, int(dx), int(dy), 0, 0)

    def flick_180(self):
        # Approximate 180 depending on sens
        self.move_camera(CONFIG["spy_check_camera_flick_speed"] * 10, 0)
        time.sleep(0.1)
        self.move_camera(-CONFIG["spy_check_camera_flick_speed"] * 10, 0)

    def cleanup(self):
        self.release_heal()

# ---------------- BOT BRAIN ----------------

class MedicBotManager:
    def __init__(self):
        self.state_mgr = StateManager()
        self.vision = VisionPipeline()
        self.inputs = InputManager()
        
        self.flask_app = Flask(__name__)
        self._setup_api()
        self.ws_connected = set()

    def update_cpu_temps(self):
        while self.state_mgr.running:
            try:
                # If psutil hardware sensors aren't supported on windows, fallback to mock
                temps = getattr(psutil, "sensors_temperatures", lambda: {})()
                core_temp = 50 # fallback
                if temps and 'coretemp' in temps:
                    core_temp = temps['coretemp'][0].current
                
                if core_temp > CONFIG["cpu_throttle_threshold"]:
                    if not self.state_mgr.cpu_throttled:
                        logger.warning("CPU Thermal throttling activated.")
                        self.state_mgr.cpu_throttled = True
                else:
                    if self.state_mgr.cpu_throttled:
                        logger.warning("CPU Thermal throttling deactivated.")
                        self.state_mgr.cpu_throttled = False
            except:
                pass
            time.sleep(5)

    def scoreboard_loop(self):
        while self.state_mgr.running:
            time.sleep(CONFIG["scoreboard_check_frequency"])
            if keyboard:
                keyboard.press('tab')
                time.sleep(CONFIG["tab_hold_duration"])
                # In real scenario, take screenshot and pass to pytesseract
                keyboard.release('tab')
                self.broadcast_activity("Checked Scoreboard")

    def brain_loop(self):
        logger.info("Bot Brain initialized (10Hz).")
        while self.state_mgr.running:
            start_tick = time.time()
            
            with self.state_mgr.lock:
                self.state_mgr.session_seconds = int(time.time() - self.state_mgr.start_time)
                my_health = self.state_mgr.my_health
                state = self.state_mgr.state
                
            frame = None
            if not self.vision.frame_buffer.empty():
                frame = self.vision.frame_buffer.get()
                
            # OpenCV HUD Read
            if frame is not None:
                h, w, _ = frame.shape
                # Health mock
                self.state_mgr.my_health = 150 # Placeholder mapping
                # Uber mock
                self.state_mgr.uber_percent = 50.0 # Placeholder mapping

            # Check Transitions (0.5s cooldown)
            if time.time() < self.state_mgr.active_cooldown:
                pass
            elif my_health < CONFIG["retreat_health_threshold"]:
                if state != BotState.RETREATING:
                    self.set_state(BotState.RETREATING)
            elif state == BotState.RETREATING and my_health > CONFIG["retreat_health_threshold"] + 20:
                self.set_state(BotState.SEARCHING)

            # Execution
            if self.state_mgr.state == BotState.HEALING:
                self.inputs.switch_weapon(2)
                self.inputs.hold_heal()
                if self.state_mgr.uber_percent >= CONFIG["uber_pop_threshold"] and CONFIG["auto_uber"]:
                    self.inputs.pop_uber()
                    self.broadcast_activity("Uber activated!")
                    
            elif self.state_mgr.state == BotState.DEFENDING:
                self.inputs.switch_weapon(1)
                self.inputs.release_heal()
                
            elif self.state_mgr.state == BotState.RETREATING:
                slot = 3 if CONFIG["prefer_melee_for_retreat"] else 1
                self.inputs.switch_weapon(slot)
                self.inputs.release_heal()

            elif self.state_mgr.state == BotState.SEARCHING:
                self.inputs.release_heal()
                self.inputs.move_camera(CONFIG["idle_rotation_speed"], 0)

            # Spy check
            if time.time() - self.state_mgr.last_spy_check > CONFIG["spy_check_frequency"]:
                self.inputs.flick_180()
                self.state_mgr.last_spy_check = time.time()
                self.broadcast_activity("Spy check executed")
                
            # Throttling
            delay = 0.1
            if self.state_mgr.cpu_throttled: delay *= 2
            elapsed = time.time() - start_tick
            if elapsed < delay:
                time.sleep(delay - elapsed)

    def set_state(self, new_state: BotState):
        with self.state_mgr.lock:
            self.state_mgr.state = new_state
            self.state_mgr.active_cooldown = time.time() + 0.5
            self.broadcast_activity(f"Transitioned to {new_state.name}")

    def _setup_api(self):
        @self.flask_app.route('/status', methods=['GET'])
        def status():
            return jsonify({
                "running": self.state_mgr.running,
                "paused": not self.state_mgr.running,
                "state": self.state_mgr.state.name,
                "current_target": self.state_mgr.current_target,
                "uber_pct": self.state_mgr.uber_percent,
                "my_health": self.state_mgr.my_health,
                "session_seconds": self.state_mgr.session_seconds,
                "melee_kills": self.state_mgr.melee_kills
            })

        @self.flask_app.route('/start', methods=['POST'])
        def start_bot():
            if not self.state_mgr.running:
                self.state_mgr.running = True
                self.state_mgr.start_time = time.time()
                threading.Thread(target=self.vision.capture_loop, args=(self.state_mgr,), daemon=True).start()
                threading.Thread(target=self.brain_loop, daemon=True).start()
                threading.Thread(target=self.update_cpu_temps, daemon=True).start()
                threading.Thread(target=self.scoreboard_loop, daemon=True).start()
                logger.info("Bot started via API.")
            return jsonify({"status": "started"})
            
        @self.flask_app.route('/stop', methods=['POST'])
        def stop_bot():
            self.state_mgr.running = False
            self.inputs.cleanup()
            logger.info("Bot stopped via API.")
            return jsonify({"status": "stopped"})

        @self.flask_app.route('/screenshot', methods=['GET'])
        def get_screenshot():
            with mss.mss() as sct:
                monitor = sct.monitors[1]
                img = sct.grab(monitor)
                frame = cv2.cvtColor(np.array(img), cv2.COLOR_BGRA2BGR)
                _, buffer = cv2.imencode('.jpg', frame)
                return buffer.tobytes(), 200, {'Content-Type': 'image/jpeg'}
            
        @self.flask_app.route('/config', methods=['GET', 'POST'])
        def handle_config():
            if request.method == 'POST':
                data = request.json
                if data:
                    CONFIG.update(data)
                    save_config()
                    logger.info("Config updated.")
            return jsonify(CONFIG)

    # ---------------- WEBSOCKET ----------------
    async def _ws_handler(self, websocket):
        self.ws_connected.add(websocket)
        try:
            while True:
                # Wait for occasional incoming (stop, start, config overrides if any)
                msg_str = await websocket.recv()
                msg = json.loads(msg_str)
                if msg.get("action") == "start":
                    self.flask_app.test_client().post("/start")
                elif msg.get("action") == "stop":
                    self.flask_app.test_client().post("/stop")
        except websockets.exceptions.ConnectionClosed:
            pass
        finally:
            self.ws_connected.remove(websocket)

    async def _ws_broadcast_loop(self):
        while True:
            await asyncio.sleep(1)
            payload = {
                "type": "status",
                "uber": self.state_mgr.uber_percent,
                "mode": "FOLLOW",
                "state": self.state_mgr.state.name,
                "my_health": self.state_mgr.my_health
            }
            msg = json.dumps(payload)
            disconnected = set()
            for ws in self.ws_connected:
                try:
                    await ws.send(msg)
                except:
                    disconnected.add(ws)
            if disconnected:
                self.ws_connected -= disconnected

    def broadcast_activity(self, msg, audio=False):
        payload = {"type": "activity", "msg": msg, "audio": audio}
        json_msg = json.dumps(payload)
        
        async def send_all():
            for ws in list(self.ws_connected):
                try: await ws.send(json_msg)
                except: pass
                
        # Fire-and-forget in event loop
        if hasattr(self, '_ws_loop') and self._ws_loop.is_running():
            asyncio.run_coroutine_threadsafe(send_all(), self._ws_loop)

            
    def start_ws(self):
        self._ws_loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self._ws_loop)
        start_server = websockets.serve(self._ws_handler, "0.0.0.0", 8765)
        self._ws_loop.run_until_complete(start_server)
        self._ws_loop.create_task(self._ws_broadcast_loop())
        self._ws_loop.run_forever()

if __name__ == "__main__":
    bot = MedicBotManager()
    threading.Thread(target=bot.start_ws, daemon=True).start()
    bot.flask_app.run(host='0.0.0.0', port=5000, threaded=True, debug=False, use_reloader=False)