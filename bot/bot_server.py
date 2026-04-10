#!/usr/bin/env python3
"""
Medic-AI Bot Server - With Follow Movement (WASD)
Tuned defaults for less jerky movement.
"""

import asyncio
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
import keyboard
import win32api
import win32con
import websockets
import json
import pyautogui

# -------------------------------------------------------------------
# Logging setup
# -------------------------------------------------------------------
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    datefmt='%H:%M:%S'
)
logger = logging.getLogger(__name__)

# -------------------------------------------------------------------
# Data structures
# -------------------------------------------------------------------
@dataclass
class Player:
    x: float
    y: float
    team: str = "unknown"
    health: int = 100
    width: float = 0.0
    height: float = 0.0
    track_id: int = -1

# -------------------------------------------------------------------
# Main Bot Class
# -------------------------------------------------------------------
class MedicBot:
    def __init__(self):
        self.running = False
        self.paused = False
        self.team = "unknown"
        self.priority_mode = "closest"
        
        # Screen capture
        self.monitor = None
        with mss.mss() as sct:
            self.monitor = sct.monitors[1]
        self.screen_width = self.monitor['width']
        self.screen_height = self.monitor['height']
        self.screen_center = (self.screen_width // 2, self.screen_height // 2)
        
        # Movement (aim)
        self.mouse_speed = 2.0
        self.smoothing = 0.3
        self.current_target: Optional[Tuple[float, float]] = None
        self.deadzone = 15
        
        # Follow movement (WASD) – tuned for less twitching
        self.follow_enabled = True
        self.follow_forward_threshold = 200
        self.follow_strafe_threshold = 120
        self.follow_backup_threshold = 100
        
        # Target lock
        self.locked_target_id: Optional[int] = None
        self.locked_target_lost_time: float = 0.0
        self.lock_grace_period = 1.0
        
        # Team detection pixel
        self.team_check_pixel = (552, 900)
        
        # Color detection HSV
        self.lower_blue = np.array([90, 80, 80])
        self.upper_blue = np.array([130, 255, 255])
        self.lower_red1 = np.array([0, 100, 100])
        self.upper_red1 = np.array([10, 255, 255])
        self.lower_red2 = np.array([160, 100, 100])
        self.upper_red2 = np.array([179, 255, 255])
        
        # Tracking
        self.tracked_players = {}
        self.next_track_id = 0
        self.tracking_grace_period = 1.5
        
        # Loadout
        self.primary_weapon = "Crusader's Crossbow"
        self.secondary_weapon = "Medi Gun"
        self.melee_weapon = "Ubersaw"
        self.current_weapon = 2
        
        # Threading
        self.frame_queue = queue.Queue(maxsize=2)
        self.result_queue = queue.Queue(maxsize=2)
        
        # WebSocket clients
        self.ws_clients = set()
        
        # Flask
        self.app = Flask(__name__)
        self._setup_routes()
        
        logger.info("MedicBot initialized. Follow movement enabled (tuned defaults).")
        logger.info("EMERGENCY STOP: Press F12")

    # -----------------------------------------------------------------
    # Team Detection (Tab + pixel)
    # -----------------------------------------------------------------
    def detect_own_team(self) -> str:
        logger.info("Detecting own team via Tab...")
        keyboard.press('tab')
        time.sleep(0.3)
        with mss.mss() as sct:
            img = sct.grab(self.monitor)
            frame = np.array(img)
            frame = cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR)
        keyboard.release('tab')
        
        x, y = self.team_check_pixel
        h, w = frame.shape[:2]
        x = min(max(x, 0), w-1)
        y = min(max(y, 0), h-1)
        pixel = frame[y, x]
        blue, green, red = int(pixel[0]), int(pixel[1]), int(pixel[2])
        
        logger.info(f"Pixel at ({x},{y}): R={red}, G={green}, B={blue}")
        
        if red > blue * 1.2:
            team = "RED"
        else:
            team = "BLU"
        logger.info(f"Detected team: {team}")
        return team

    # -----------------------------------------------------------------
    # Screen Capture Thread
    # -----------------------------------------------------------------
    def _capture_loop(self):
        with mss.mss() as sct:
            while self.running:
                if not self.paused:
                    img = sct.grab(self.monitor)
                    frame = np.array(img)
                    frame = cv2.cvtColor(frame, cv2.COLOR_BGRA2BGR)
                    if self.frame_queue.full():
                        try:
                            self.frame_queue.get_nowait()
                        except queue.Empty:
                            pass
                    self.frame_queue.put(frame)
                time.sleep(0.005)

    # -----------------------------------------------------------------
    # Detection Thread
    # -----------------------------------------------------------------
    def _detection_loop(self):
        while self.running:
            if self.paused:
                time.sleep(0.1)
                continue
            try:
                frame = self.frame_queue.get(timeout=1)
            except queue.Empty:
                continue
            
            players = self._detect_players_color(frame)
            tracked = self._update_tracking(players)
            target = self._select_target(tracked)
            
            if self.result_queue.full():
                try:
                    self.result_queue.get_nowait()
                except queue.Empty:
                    pass
            self.result_queue.put((target, tracked))

    # -----------------------------------------------------------------
    # Color Detection (with UI filtering)
    # -----------------------------------------------------------------
    def _detect_players_color(self, frame: np.ndarray) -> List[Player]:
        players = []
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        
        if self.team == "BLU":
            mask = cv2.inRange(hsv, self.lower_blue, self.upper_blue)
        else:
            mask1 = cv2.inRange(hsv, self.lower_red1, self.upper_red1)
            mask2 = cv2.inRange(hsv, self.lower_red2, self.upper_red2)
            mask = cv2.bitwise_or(mask1, mask2)
        
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((3,3), np.uint8))
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((7,7), np.uint8))
        
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < 800:
                continue
            x, y, w, h = cv2.boundingRect(cnt)
            if h < w * 1.4:
                continue
            if w > self.screen_width * 0.3 or h > self.screen_height * 0.5:
                continue
            center_x = x + w // 2
            center_y = y + h // 2
            players.append(Player(
                x=center_x, y=center_y, team=self.team,
                health=100, width=w, height=h
            ))
        return players

    # -----------------------------------------------------------------
    # Tracking
    # -----------------------------------------------------------------
    def _update_tracking(self, detections: List[Player]) -> List[Player]:
        current_time = time.time()
        updated = {}
        unmatched_dets = list(range(len(detections)))
        unmatched_tracks = list(self.tracked_players.keys())
        
        for det_idx, det in enumerate(detections):
            best_id, best_dist = None, float('inf')
            for tid in unmatched_tracks:
                last = self.tracked_players[tid]
                dist = ((det.x - last.x)**2 + (det.y - last.y)**2) ** 0.5
                if dist < 150 and dist < best_dist:
                    best_dist, best_id = dist, tid
            if best_id is not None:
                det.track_id = best_id
                updated[best_id] = det
                unmatched_dets.remove(det_idx)
                unmatched_tracks.remove(best_id)
        
        for det_idx in unmatched_dets:
            det = detections[det_idx]
            new_id = self.next_track_id
            self.next_track_id += 1
            det.track_id = new_id
            updated[new_id] = det
        
        for tid in unmatched_tracks:
            last = self.tracked_players[tid]
            if current_time - getattr(last, 'last_seen', current_time) < self.tracking_grace_period:
                updated[tid] = last
        
        for pid, p in updated.items():
            p.last_seen = current_time
        
        self.tracked_players = updated
        return list(updated.values())

    # -----------------------------------------------------------------
    # Target Selection (with lock persistence)
    # -----------------------------------------------------------------
    def _select_target(self, players: List[Player]) -> Optional[Tuple[float, float]]:
        current_time = time.time()
        
        if self.locked_target_id is not None:
            locked_player = next((p for p in players if p.track_id == self.locked_target_id), None)
            if locked_player:
                self.locked_target_lost_time = 0.0
                return (locked_player.x, locked_player.y)
            else:
                if self.locked_target_lost_time == 0.0:
                    self.locked_target_lost_time = current_time
                elif current_time - self.locked_target_lost_time > self.lock_grace_period:
                    self.locked_target_id = None
                    self.locked_target_lost_time = 0.0
                    logger.info("Locked target lost, searching for new target")
                else:
                    return None
        
        if not players:
            return None
        
        if self.priority_mode == "lowest_health":
            target = min(players, key=lambda p: p.health)
        else:
            target = min(players, key=lambda p: (p.x - self.screen_center[0])**2 + (p.y - self.screen_center[1])**2)
        
        self.locked_target_id = target.track_id
        self.locked_target_lost_time = 0.0
        logger.info(f"Locked onto target ID {target.track_id}")
        return (target.x, target.y)

    # -----------------------------------------------------------------
    # Aim Movement Thread
    # -----------------------------------------------------------------
    def _movement_loop(self):
        while self.running:
            if self.paused:
                time.sleep(0.1)
                continue
            try:
                target, _ = self.result_queue.get(timeout=1)
            except queue.Empty:
                target = None
            
            if target:
                self.current_target = target
                dx = target[0] - self.screen_center[0]
                dy = target[1] - self.screen_center[1]
                
                distance = (dx**2 + dy**2) ** 0.5
                if distance < self.deadzone:
                    if self.current_weapon == 2:
                        pyautogui.mouseDown(button='left')
                    time.sleep(0.01)
                    continue
                
                if target[1] < self.screen_height * 0.2:
                    dy = max(dy, 0)
                
                move_x = int(dx * self.smoothing * self.mouse_speed)
                move_y = int(dy * self.smoothing * self.mouse_speed)
                
                if abs(move_x) > 1 or abs(move_y) > 1:
                    win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, move_x, move_y, 0, 0)
                
                if self.current_weapon == 2:
                    pyautogui.mouseDown(button='left')
            else:
                pyautogui.mouseUp(button='left')
            time.sleep(0.01)

    # -----------------------------------------------------------------
    # Follow Movement Thread (WASD)
    # -----------------------------------------------------------------
    def _follow_loop(self):
        """Press WASD based on target position to follow the player."""
        while self.running:
            if self.paused or not self.follow_enabled:
                keyboard.release('w')
                keyboard.release('a')
                keyboard.release('s')
                keyboard.release('d')
                time.sleep(0.1)
                continue
            
            target = self.current_target
            if target is None:
                keyboard.release('w')
                keyboard.release('a')
                keyboard.release('s')
                keyboard.release('d')
                time.sleep(0.05)
                continue
            
            dx = target[0] - self.screen_center[0]
            dy = target[1] - self.screen_center[1]
            distance = (dx**2 + dy**2) ** 0.5
            
            # Strafe left/right
            if dx > self.follow_strafe_threshold:
                keyboard.press('d')
                keyboard.release('a')
            elif dx < -self.follow_strafe_threshold:
                keyboard.press('a')
                keyboard.release('d')
            else:
                keyboard.release('a')
                keyboard.release('d')
            
            # Move forward/backward
            if distance > self.follow_forward_threshold:
                keyboard.press('w')
                keyboard.release('s')
            elif distance < self.follow_backup_threshold and distance > self.deadzone:
                keyboard.release('w')
                keyboard.press('s')
            else:
                keyboard.release('w')
                keyboard.release('s')
            
            time.sleep(0.05)

    # -----------------------------------------------------------------
    # WebSocket Handler
    # -----------------------------------------------------------------
    async def ws_handler(self, websocket):
        self.ws_clients.add(websocket)
        logger.info("GUI connected via WebSocket")
        try:
            async for message in websocket:
                try:
                    data = json.loads(message)
                    msg_type = data.get('type')
                    if msg_type == 'config':
                        logger.info("Received config from GUI")
                        if 'follow_enabled' in data:
                            self.follow_enabled = data['follow_enabled']
                        if 'follow_forward_threshold' in data:
                            self.follow_forward_threshold = int(data['follow_forward_threshold'])
                        if 'follow_strafe_threshold' in data:
                            self.follow_strafe_threshold = int(data['follow_strafe_threshold'])
                        if 'follow_backup_threshold' in data:
                            self.follow_backup_threshold = int(data['follow_backup_threshold'])
                    elif msg_type == 'start':
                        if not self.running:
                            self.team = self.detect_own_team()
                            self.running = True
                            self.paused = False
                            threading.Thread(target=self._capture_loop, daemon=True).start()
                            threading.Thread(target=self._detection_loop, daemon=True).start()
                            threading.Thread(target=self._movement_loop, daemon=True).start()
                            threading.Thread(target=self._follow_loop, daemon=True).start()
                            logger.info("Bot started via WebSocket")
                            await websocket.send(json.dumps({"type": "activity", "msg": "Bot started"}))
                    elif msg_type == 'stop':
                        self.running = False
                        pyautogui.mouseUp(button='left')
                        keyboard.release('w')
                        keyboard.release('a')
                        keyboard.release('s')
                        keyboard.release('d')
                        logger.info("Bot stopped via WebSocket")
                        await websocket.send(json.dumps({"type": "activity", "msg": "Bot stopped"}))
                    elif msg_type == 'pause':
                        self.paused = True
                        pyautogui.mouseUp(button='left')
                        keyboard.release('w')
                        keyboard.release('a')
                        keyboard.release('s')
                        keyboard.release('d')
                        await websocket.send(json.dumps({"type": "activity", "msg": "Bot paused"}))
                    elif msg_type == 'resume':
                        self.paused = False
                        await websocket.send(json.dumps({"type": "activity", "msg": "Bot resumed"}))
                    else:
                        await websocket.send(json.dumps({"type": "activity", "msg": f"Unknown command: {msg_type}"}))
                except json.JSONDecodeError:
                    logger.warning("Invalid JSON received")
                except Exception as e:
                    logger.error(f"Error handling WebSocket message: {e}")
        except websockets.exceptions.ConnectionClosed:
            logger.info("GUI disconnected (connection closed)")
        finally:
            self.ws_clients.remove(websocket)

    # -----------------------------------------------------------------
    # Flask Routes
    # -----------------------------------------------------------------
    def _setup_routes(self):
        @self.app.route('/status', methods=['GET'])
        def status():
            return jsonify({
                'running': self.running,
                'paused': self.paused,
                'team': self.team,
                'priority_mode': self.priority_mode,
                'locked_target_id': self.locked_target_id,
                'follow_enabled': self.follow_enabled
            })
        
        @self.app.route('/start', methods=['POST'])
        def start():
            if not self.running:
                self.team = self.detect_own_team()
                self.running = True
                self.paused = False
                threading.Thread(target=self._capture_loop, daemon=True).start()
                threading.Thread(target=self._detection_loop, daemon=True).start()
                threading.Thread(target=self._movement_loop, daemon=True).start()
                threading.Thread(target=self._follow_loop, daemon=True).start()
                logger.info("Bot started via HTTP")
            return jsonify({'status': 'started', 'team': self.team})
        
        @self.app.route('/stop', methods=['POST'])
        def stop():
            self.running = False
            pyautogui.mouseUp(button='left')
            keyboard.release('w')
            keyboard.release('a')
            keyboard.release('s')
            keyboard.release('d')
            logger.info("Bot stopped via HTTP")
            return jsonify({'status': 'stopped'})
        
        @self.app.route('/pause', methods=['POST'])
        def pause():
            self.paused = True
            pyautogui.mouseUp(button='left')
            keyboard.release('w')
            keyboard.release('a')
            keyboard.release('s')
            keyboard.release('d')
            return jsonify({'status': 'paused'})
        
        @self.app.route('/resume', methods=['POST'])
        def resume():
            self.paused = False
            return jsonify({'status': 'resumed'})
        
        @self.app.route('/config', methods=['POST'])
        def set_config():
            data = request.json
            if data is None:
                return jsonify({'error': 'No JSON provided'}), 400
            # Brain config
            if 'retreat_health_threshold' in data:
                self.retreat_health_threshold = int(data['retreat_health_threshold'])
            if 'defend_enemy_distance' in data:
                self.defend_enemy_distance = int(data['defend_enemy_distance'])
            if 'uber_pop_threshold' in data:
                self.uber_pop_threshold = int(data['uber_pop_threshold'])
            if 'prefer_melee_for_retreat' in data:
                self.prefer_melee_for_retreat = bool(data['prefer_melee_for_retreat'])
            if 'auto_heal' in data:
                self.auto_heal = bool(data['auto_heal'])
            if 'auto_uber' in data:
                self.auto_uber = bool(data['auto_uber'])
            if 'priority_only_heal' in data:
                self.priority_only_heal = bool(data['priority_only_heal'])
            if 'priority_players' in data:
                self.priority_players = list(data['priority_players'])
            # Follow settings
            if 'follow_enabled' in data:
                self.follow_enabled = bool(data['follow_enabled'])
            if 'follow_forward_threshold' in data:
                self.follow_forward_threshold = int(data['follow_forward_threshold'])
            if 'follow_strafe_threshold' in data:
                self.follow_strafe_threshold = int(data['follow_strafe_threshold'])
            if 'follow_backup_threshold' in data:
                self.follow_backup_threshold = int(data['follow_backup_threshold'])
            return jsonify({'status': 'ok'})

        @self.app.route('/set_follow', methods=['POST'])
        def set_follow():
            data = request.json
            self.follow_enabled = data.get('enabled', self.follow_enabled)
            return jsonify({'follow_enabled': self.follow_enabled})

    def run_flask(self):
        self.app.run(host='0.0.0.0', port=5000, threaded=True, use_reloader=False)

    async def run_websocket(self):
        async with websockets.serve(self.ws_handler, "0.0.0.0", 8765):
            logger.info("WebSocket server running on ws://0.0.0.0:8765")
            await asyncio.Future()

    def start_servers(self):
        flask_thread = threading.Thread(target=self.run_flask, daemon=True)
        flask_thread.start()
        asyncio.run(self.run_websocket())

# -------------------------------------------------------------------
# Entry Point with Emergency Stop (F12)
# -------------------------------------------------------------------
if __name__ == '__main__':
    bot = MedicBot()
    
    def emergency_stop():
        logger.info("EMERGENCY STOP (F12) - Shutting down bot")
        bot.running = False
        pyautogui.mouseUp(button='left')
        keyboard.release('w')
        keyboard.release('a')
        keyboard.release('s')
        keyboard.release('d')
        time.sleep(0.5)
    
    keyboard.add_hotkey('f12', emergency_stop)
    logger.info("F12 emergency stop registered.")
    
    try:
        bot.start_servers()
    except KeyboardInterrupt:
        bot.running = False
        logger.info("Shutdown via Ctrl+C.")