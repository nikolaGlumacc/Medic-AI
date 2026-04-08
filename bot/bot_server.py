#!/usr/bin/env python3
"""
Medic-AI Bot Server
WebSocket-controlled TF2 Medic bot with YOLO vision, OCR, and weapon‑switch reversion.
"""

import asyncio
import json
import os
import sys
import time
import threading
import logging
import random
import math
from typing import Optional, Dict, Any, List, Tuple, Union
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path

# Networking
import websockets
from websockets.server import WebSocketServerProtocol, serve

# Input simulation
import pynput
from pynput.keyboard import Key, Controller as KeyboardController, KeyCode
from pynput.mouse import Button, Controller as MouseController

# Screen capture & vision
import mss
import numpy as np
import cv2

# System monitoring
import psutil

# Image recognition for weapon icons
import pyautogui

# Optional: YOLO (uncomment if using Ultralytics)
# from ultralytics import YOLO

# Optional: OCR (uncomment if using Tesseract)
# import pytesseract

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    datefmt='%H:%M:%S'
)
logger = logging.getLogger(__name__)

# ----------------------------------------------------------------------
# Constants & Enums
# ----------------------------------------------------------------------

class BotState(Enum):
    """Possible bot operational states."""
    IDLE = "idle"
    ACTIVE = "active"
    SEARCHING = "searching"
    HEALING = "healing"
    UBER_READY = "uber_ready"
    UBER_ACTIVE = "uber_active"
    RETREATING = "retreating"
    SWITCHING_LOADOUT = "switching_loadout"   # State for weapon reversion

class PlayerClass(Enum):
    """TF2 class IDs (simplified)."""
    SCOUT = 1
    SOLDIER = 2
    PYRO = 3
    DEMOMAN = 4
    HEAVY = 5
    ENGINEER = 6
    MEDIC = 7
    SNIPER = 8
    SPY = 9

@dataclass
class Player:
    """Represents a detected player."""
    x: float = 0.0
    y: float = 0.0
    w: float = 0.0
    h: float = 0.0
    health: int = 0
    max_health: int = 0
    class_id: int = 0
    is_ubered: bool = False
    distance: float = 0.0
    is_critical: bool = False

# ----------------------------------------------------------------------
# Main Bot Class
# ----------------------------------------------------------------------

class MedicBot:
    """TF2 Medic bot with WebSocket control and weapon‑switch reversion."""

    def __init__(self):
        # --- State ---
        self.running = False
        self.bot_state = BotState.IDLE
        self.config: Dict[str, Any] = {}

        # --- Input Controllers (thread‑safe via lock) ---
        self.keyboard = KeyboardController()
        self.mouse = MouseController()
        self.input_lock = threading.RLock()

        # --- Screen Capture ---
        self.sct = mss.mss()
        self.monitor = self.sct.monitors[1]  # Primary monitor
        self.screen_width = self.monitor["width"]
        self.screen_height = self.monitor["height"]

        # --- Vision Models (placeholders – replace with your actual models) ---
        self.yolo_model = None          # e.g., YOLO("best.pt")
        self.ocr_engine = None          # e.g., pytesseract

        # --- Game State ---
        self.players: List[Player] = []
        self.local_player: Optional[Player] = None
        self.uber_percent = 0.0
        self.uber_active = False
        self.crosshair_target: Optional[Player] = None

        # --- CPU Thermal Throttling ---
        self.cpu_temp = 0.0
        self.cpu_overheat = False
        self.temp_limit = 85.0

        # --- Activity Tracking ---
        self.last_action_time = time.time()
        self.last_position = (self.screen_width // 2, self.screen_height // 2)

        # --- WebSocket ---
        self.ws_server = None
        self.ws_clients: List[WebSocketServerProtocol] = []
        self.ws_host = "localhost"
        self.ws_port = 8765

        # --- Threading ---
        self.bot_thread: Optional[threading.Thread] = None
        self.ws_thread: Optional[threading.Thread] = None

        # ------------------------------------------------------------------
        # Weapon Switch Reversion (NEW)
        # ------------------------------------------------------------------
        self.weapon_switch_enabled = False
        self.weapon_switch_delay = 3.0           # seconds before reverting
        self.weapon_images_path = "weapons/"     # folder containing weapon PNGs
        self.current_weapon: Optional[str] = None
        self.weapon_change_detected = False
        self.last_weapon_change_time = 0.0
        self._last_weapon_check = 0.0

        # Create weapons directory if missing
        Path(self.weapon_images_path).mkdir(exist_ok=True)

        # ------------------------------------------------------------------
        # Configuration Defaults
        # ------------------------------------------------------------------
        self._init_default_config()

    def _init_default_config(self):
        """Set default configuration values."""
        self.config = {
            "healing": {
                "priority": "critical",            # critical, distance, class_priority
                "critical_threshold": 50,           # health percentage
                "class_priority_list": [3, 4, 6, 2, 1, 5, 7, 8, 9],
                "max_distance": 1000,
                "beam_hold_time": 0.1
            },
            "movement": {
                "enabled": True,
                "follow_distance": 200,
                "strafe_enabled": True
            },
            "uber": {
                "auto_pop": True,
                "pop_health_threshold": 40,
                "pop_on_fire": True
            },
            "combat": {
                "auto_attack": False,
                "melee_range": 150
            },
            "vision": {
                "confidence": 0.5,
                "ocr_confidence": 0.7
            },
            "weapon_switch": {
                "enabled": False,
                "delay_seconds": 3.0,
                "images_path": "weapons/"
            }
        }

    # ------------------------------------------------------------------
    # Configuration Helpers
    # ------------------------------------------------------------------
    def get_config_value(self, *keys, default=None):
        """Safely retrieve nested configuration values."""
        value = self.config
        for key in keys:
            if isinstance(value, dict):
                value = value.get(key)
            else:
                return default
            if value is None:
                return default
        return value

    # ------------------------------------------------------------------
    # Input Methods (Thread‑Safe)
    # ------------------------------------------------------------------
    def _tap(self, key: Union[str, Key, KeyCode], duration: float = 0.05):
        """Press and release a key."""
        with self.input_lock:
            self.keyboard.press(key)
            time.sleep(duration)
            self.keyboard.release(key)

    def _hold(self, key: Union[str, Key, KeyCode]):
        """Press and hold a key."""
        with self.input_lock:
            self.keyboard.press(key)

    def _release(self, key: Union[str, Key, KeyCode]):
        """Release a key."""
        with self.input_lock:
            self.keyboard.release(key)

    def _click(self, button: Button = Button.left, duration: float = 0.05):
        """Click a mouse button."""
        with self.input_lock:
            self.mouse.press(button)
            time.sleep(duration)
            self.mouse.release(button)

    def _hold_click(self, button: Button = Button.left):
        """Hold a mouse button."""
        with self.input_lock:
            self.mouse.press(button)

    def _release_click(self, button: Button = Button.left):
        """Release a mouse button."""
        with self.input_lock:
            self.mouse.release(button)

    def _move_mouse_relative(self, dx: int, dy: int):
        """Move mouse relative to current position."""
        with self.input_lock:
            self.mouse.move(dx, dy)

    def _move_mouse_absolute(self, x: int, y: int):
        """Move mouse to absolute screen coordinates (using pynput)."""
        # Note: pynput uses absolute coordinates if the OS allows.
        # This is a simplified version; actual absolute positioning requires
        # platform‑specific work. Use relative for now.
        with self.input_lock:
            self.mouse.position = (x, y)

    def release_all_inputs(self):
        """Release all held keys and mouse buttons."""
        with self.input_lock:
            # Movement keys
            for key in ['w', 'a', 's', 'd', Key.ctrl_l, Key.shift, Key.space]:
                try:
                    self.keyboard.release(key)
                except:
                    pass
            # Mouse buttons
            try:
                self.mouse.release(Button.left)
            except:
                pass
            try:
                self.mouse.release(Button.right)
            except:
                pass

    # ------------------------------------------------------------------
    # Vision & Game State (Placeholders – Replace with your logic)
    # ------------------------------------------------------------------
    def _capture_screen(self) -> np.ndarray:
        """Capture the primary monitor and return as BGR numpy array."""
        try:
            img = self.sct.grab(self.monitor)
            return np.array(img)[:, :, :3]  # BGRA -> BGR
        except Exception as e:
            logger.error(f"Screen capture failed: {e}")
            return np.zeros((self.screen_height, self.screen_width, 3), dtype=np.uint8)

    def _load_vision_models(self):
        """Load YOLO and OCR models. (Replace with your actual loading code)."""
        # Example: self.yolo_model = YOLO("models/players.pt")
        # Example: pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'
        logger.info("Vision models loaded (placeholders).")

    def _detect_players(self, frame: np.ndarray) -> List[Player]:
        """
        Run YOLO detection and OCR health reading.
        REPLACE THIS with your actual detection pipeline.
        """
        players = []
        # --- Stub: simulate detection ---
        # In a real implementation, you would:
        #   results = self.yolo_model(frame)
        #   for box in results[0].boxes:
        #       class_id = int(box.cls)
        #       x1, y1, x2, y2 = box.xyxy[0].tolist()
        #       # Crop region and run OCR for health
        #       health_text = pytesseract.image_to_string(crop, config='--psm 7')
        #       # Parse health numbers...
        return players

    def _detect_uber_percent(self, frame: np.ndarray) -> float:
        """Read uber percentage from HUD using OCR. Replace with your code."""
        # Stub: return random value for testing
        return min(100.0, self.uber_percent + random.uniform(0, 0.8))

    def _detect_local_player(self, frame: np.ndarray) -> Optional[Player]:
        """Determine local player position (usually center of screen)."""
        # For a bot, local player is always at the crosshair.
        # You may want to read health/class from HUD OCR.
        if self.local_player is None:
            self.local_player = Player(
                x=self.screen_width // 2,
                y=self.screen_height // 2,
                health=150,
                max_health=150,
                class_id=PlayerClass.MEDIC.value
            )
        return self.local_player

    def update_environment(self):
        """Capture screen and update all game state."""
        frame = self._capture_screen()
        self.players = self._detect_players(frame)
        self.local_player = self._detect_local_player(frame)
        self.uber_percent = self._detect_uber_percent(frame)

        # Calculate distances from local player
        if self.local_player:
            lx, ly = self.local_player.x, self.local_player.y
            for p in self.players:
                p.distance = math.hypot(p.x - lx, p.y - ly)
                p.is_critical = (p.health / p.max_health * 100) < self.get_config_value(
                    "healing", "critical_threshold", default=50
                )

    # ------------------------------------------------------------------
    # Healing Logic
    # ------------------------------------------------------------------
    def find_heal_target(self) -> Optional[Player]:
        """Select best player to heal based on configured priority."""
        if not self.players:
            return None

        priority_mode = self.get_config_value("healing", "priority", default="critical")
        max_dist = self.get_config_value("healing", "max_distance", default=1000)

        # Filter by distance
        candidates = [p for p in self.players if p.distance <= max_dist]
        if not candidates:
            return None

        if priority_mode == "critical":
            # Sort by health percentage ascending, then distance
            candidates.sort(key=lambda p: (p.health / p.max_health, p.distance))
            return candidates[0]
        elif priority_mode == "distance":
            return min(candidates, key=lambda p: p.distance)
        elif priority_mode == "class_priority":
            class_order = self.get_config_value("healing", "class_priority_list")
            for cls in class_order:
                for p in candidates:
                    if p.class_id == cls:
                        return p
            return min(candidates, key=lambda p: p.health / p.max_health)
        else:
            return candidates[0]

    def aim_at_target(self, target: Player):
        """Move crosshair toward target (simple relative movement)."""
        if not self.local_player:
            return
        dx = target.x - self.local_player.x
        dy = target.y - self.local_player.y
        # Scale movement (adjust sensitivity)
        self._move_mouse_relative(int(dx * 0.5), int(dy * 0.5))

    def handle_uber(self):
        """Auto‑pop uber if conditions are met."""
        if self.uber_percent < 100 or self.uber_active:
            return
        if not self.get_config_value("uber", "auto_pop", default=True):
            return

        # Check for critical teammate nearby
        pop_threshold = self.get_config_value("uber", "pop_health_threshold", default=40)
        for p in self.players:
            if p.health < pop_threshold and p.distance < 400:
                self._click(Button.right)  # Uber activation
                self.uber_active = True
                self.set_bot_state(BotState.UBER_ACTIVE, "Uber popped!")
                logger.info("🚨 UBER ACTIVATED!")
                return

    # ------------------------------------------------------------------
    # Weapon Switch Reversion (NEW)
    # ------------------------------------------------------------------
    def _get_weapon_image_map(self) -> Dict[str, str]:
        """Return mapping of weapon names to image file paths."""
        return {
            # Primary - Syringe Guns
            "syringe_gun": os.path.join(self.weapon_images_path, "syringe_gun.png"),
            "blutsauger": os.path.join(self.weapon_images_path, "blutsauger.png"),
            "crusaders_crossbow": os.path.join(self.weapon_images_path, "crusaders_crossbow.png"),
            "overdose": os.path.join(self.weapon_images_path, "overdose.png"),
            # Secondary - Mediguns
            "medigun": os.path.join(self.weapon_images_path, "medigun.png"),
            "kritzkrieg": os.path.join(self.weapon_images_path, "kritzkrieg.png"),
            "quick_fix": os.path.join(self.weapon_images_path, "quick_fix.png"),
            "vaccinator": os.path.join(self.weapon_images_path, "vaccinator.png"),
            # Melee
            "bonesaw": os.path.join(self.weapon_images_path, "bonesaw.png"),
            "ubersaw": os.path.join(self.weapon_images_path, "ubersaw.png"),
            "vita_saw": os.path.join(self.weapon_images_path, "vita_saw.png"),
            "amputator": os.path.join(self.weapon_images_path, "amputator.png"),
            "solemn_vow": os.path.join(self.weapon_images_path, "solemn_vow.png"),
        }

    def detect_current_weapon(self) -> Optional[str]:
        """Scan the screen for a weapon HUD icon and return its name."""
        if not self.weapon_switch_enabled:
            return None

        for weapon_name, img_path in self._get_weapon_image_map().items():
            if not os.path.exists(img_path):
                continue
            try:
                # confidence 0.8 works well; adjust if needed
                location = pyautogui.locateOnScreen(img_path, confidence=0.8)
                if location is not None:
                    return weapon_name
            except Exception as e:
                logger.debug(f"Weapon detection error {weapon_name}: {e}")
        return None

    def revert_weapon_loadout(self, old_weapon: str, new_weapon: str):
        """
        Open loadout menu (M), click old weapon slot, then new weapon slot,
        and close the menu. Temporarily pauses bot actions.
        """
        if not self.weapon_switch_enabled:
            return

        logger.info(f"🔄 Reverting weapon: {new_weapon} → {old_weapon}")
        self.release_all_inputs()
        self.set_bot_state(BotState.SWITCHING_LOADOUT, "Weapon reversion")

        # Open loadout (default key M)
        self._tap("m")
        time.sleep(0.8)

        # Click old weapon slot (equip)
        old_img = os.path.join(self.weapon_images_path, f"{old_weapon}.png")
        if os.path.exists(old_img):
            try:
                pos = pyautogui.locateCenterOnScreen(old_img, confidence=0.8)
                if pos:
                    pyautogui.click(pos)
                    time.sleep(0.3)
                else:
                    logger.warning(f"Could not locate {old_weapon} in loadout.")
            except Exception as e:
                logger.error(f"Error clicking {old_weapon}: {e}")

        # Click new weapon slot (unequip)
        new_img = os.path.join(self.weapon_images_path, f"{new_weapon}.png")
        if os.path.exists(new_img):
            try:
                pos = pyautogui.locateCenterOnScreen(new_img, confidence=0.8)
                if pos:
                    pyautogui.click(pos)
                    time.sleep(0.3)
                else:
                    logger.warning(f"Could not locate {new_weapon} in loadout.")
            except Exception as e:
                logger.error(f"Error clicking {new_weapon}: {e}")

        # Close loadout
        self._tap("m")
        time.sleep(0.5)

        self.set_bot_state(BotState.ACTIVE, "Weapon reverted")
        self.send_activity_sync(f"Weapon reverted to {old_weapon}")

    def check_weapon_switch(self):
        """Periodic check for weapon changes; trigger reversion if needed."""
        if not self.weapon_switch_enabled:
            return

        now = time.time()
        if now - self._last_weapon_check < 0.5:
            return
        self._last_weapon_check = now

        detected = self.detect_current_weapon()
        if detected is None:
            return

        if self.current_weapon is None:
            self.current_weapon = detected
            return

        if detected != self.current_weapon:
            if not self.weapon_change_detected:
                self.weapon_change_detected = True
                self.last_weapon_change_time = now
                logger.info(f"Weapon changed: {self.current_weapon} → {detected}")
        else:
            self.weapon_change_detected = False

        if (self.weapon_change_detected and
            now - self.last_weapon_change_time >= self.weapon_switch_delay):
            self.revert_weapon_loadout(self.current_weapon, detected)
            # Update current weapon after reversion
            self.current_weapon = self.detect_current_weapon() or detected
            self.weapon_change_detected = False

    # ------------------------------------------------------------------
    # CPU Temperature Monitoring
    # ------------------------------------------------------------------
    def check_cpu_temp(self):
        """Read CPU temperature using psutil."""
        try:
            temps = psutil.sensors_temperatures()
            if 'coretemp' in temps:
                self.cpu_temp = max(t.current for t in temps['coretemp'])
            elif 'cpu-thermal' in temps:
                self.cpu_temp = temps['cpu-thermal'][0].current
            else:
                self.cpu_temp = 0.0
        except:
            self.cpu_temp = 0.0

        self.cpu_overheat = self.cpu_temp >= self.temp_limit
        if self.cpu_overheat:
            logger.warning(f"🔥 CPU overheating: {self.cpu_temp:.1f}°C")

    # ------------------------------------------------------------------
    # Bot State Management
    # ------------------------------------------------------------------
    def set_bot_state(self, state: BotState, reason: str = ""):
        """Update bot state and notify WebSocket clients."""
        if self.bot_state != state:
            logger.info(f"State: {self.bot_state.value} → {state.value} ({reason})")
        self.bot_state = state
        self.send_status_sync()

    # ------------------------------------------------------------------
    # WebSocket Communication
    # ------------------------------------------------------------------
    async def _broadcast(self, message: str):
        """Send message to all connected WebSocket clients."""
        if not self.ws_clients:
            return
        # Use asyncio.gather to send concurrently
        await asyncio.gather(
            *[client.send(message) for client in self.ws_clients],
            return_exceptions=True
        )

    def send_status_sync(self):
        """Synchronous wrapper to broadcast status from bot thread."""
        if not self.ws_clients:
            return
        data = {
            "type": "status_update",
            "state": self.bot_state.value,
            "uber_percent": round(self.uber_percent, 1),
            "cpu_temp": round(self.cpu_temp, 1),
            "players": len(self.players),
            "weapon": self.current_weapon
        }
        asyncio.run_coroutine_threadsafe(
            self._broadcast(json.dumps(data)),
            asyncio.get_event_loop()
        )

    def send_activity_sync(self, activity: str):
        """Send an activity log message to clients."""
        if not self.ws_clients:
            return
        data = {"type": "activity", "message": activity}
        asyncio.run_coroutine_threadsafe(
            self._broadcast(json.dumps(data)),
            asyncio.get_event_loop()
        )

    async def ws_handler(self, websocket: WebSocketServerProtocol, path: str):
        """Handle individual WebSocket connection."""
        self.ws_clients.append(websocket)
        client_id = id(websocket)
        logger.info(f"🔌 Client connected (id={client_id})")

        try:
            async for message in websocket:
                try:
                    data = json.loads(message)
                    await self._process_ws_message(websocket, data)
                except json.JSONDecodeError:
                    logger.error(f"Invalid JSON: {message}")
        except websockets.exceptions.ConnectionClosed:
            logger.info(f"🔌 Client disconnected (id={client_id})")
        finally:
            self.ws_clients.remove(websocket)

    async def _process_ws_message(self, websocket: WebSocketServerProtocol, data: Dict[str, Any]):
        """Route incoming WebSocket messages."""
        msg_type = data.get("type")

        if msg_type == "start":
            self.start_bot()
            await websocket.send(json.dumps({"type": "status", "running": self.running}))

        elif msg_type == "stop":
            self.stop_bot()
            await websocket.send(json.dumps({"type": "status", "running": self.running}))

        elif msg_type == "config":
            # Update configuration
            new_config = data.get("config", {})
            self._update_config(new_config)
            logger.info("Configuration updated")
            await websocket.send(json.dumps({"type": "config_ack", "status": "ok"}))

        elif msg_type == "get_status":
            await websocket.send(json.dumps({
                "type": "status",
                "running": self.running,
                "state": self.bot_state.value,
                "uber_percent": self.uber_percent,
                "cpu_temp": self.cpu_temp,
                "players": len(self.players)
            }))

        elif msg_type == "set_weapon":
            # Force weapon switch (for testing)
            weapon = data.get("weapon")
            if weapon:
                logger.info(f"Manual weapon override: {weapon}")
                self.current_weapon = weapon

    def _update_config(self, new_config: Dict[str, Any]):
        """Deep merge new configuration into self.config."""
        def merge(a, b):
            for key in b:
                if key in a and isinstance(a[key], dict) and isinstance(b[key], dict):
                    merge(a[key], b[key])
                else:
                    a[key] = b[key]
        merge(self.config, new_config)

        # Extract weapon‑switch settings
        self.weapon_switch_enabled = bool(self.get_config_value("weapon_switch", "enabled", default=False))
        self.weapon_switch_delay = float(self.get_config_value("weapon_switch", "delay_seconds", default=3.0))
        self.weapon_images_path = str(self.get_config_value("weapon_switch", "images_path", default="weapons/"))
        Path(self.weapon_images_path).mkdir(exist_ok=True)

        # Update other runtime settings
        self.temp_limit = float(self.get_config_value("system", "temp_limit", default=85.0))

    # ------------------------------------------------------------------
    # Main Bot Loop (Runs in separate thread)
    # ------------------------------------------------------------------
    def bot_main_loop(self):
        """Primary bot logic."""
        logger.info("🤖 Bot thread started")
        self._load_vision_models()

        while self.running:
            try:
                # Thermal throttle
                self.check_cpu_temp()
                if self.cpu_overheat:
                    self.release_all_inputs()
                    time.sleep(1.0)
                    continue

                # Update game state (vision)
                self.update_environment()

                # Uber management
                self.handle_uber()

                # Only perform healing if not switching loadout
                if self.bot_state != BotState.SWITCHING_LOADOUT:
                    target = self.find_heal_target()
                    if target:
                        self.set_bot_state(BotState.HEALING, f"Healing class {target.class_id}")
                        self.aim_at_target(target)
                        self._hold_click(Button.left)   # Medigun beam
                    else:
                        self.set_bot_state(BotState.SEARCHING, "No targets")
                        self._release_click(Button.left)

                # Check for unwanted weapon switches
                self.check_weapon_switch()

                # Periodic status broadcast
                self.send_status_sync()

                time.sleep(0.05)  # ~20 Hz

            except Exception as e:
                logger.exception(f"Error in bot loop: {e}")
                time.sleep(0.5)

        # Cleanup
        self.release_all_inputs()
        logger.info("🤖 Bot thread stopped")

    # ------------------------------------------------------------------
    # Bot Control
    # ------------------------------------------------------------------
    def start_bot(self):
        """Start the bot thread."""
        if self.running:
            return
        self.running = True
        self.bot_state = BotState.ACTIVE
        self.bot_thread = threading.Thread(target=self.bot_main_loop, daemon=True)
        self.bot_thread.start()
        self.send_activity_sync("Bot started")

    def stop_bot(self):
        """Stop the bot thread."""
        self.running = False
        if self.bot_thread and self.bot_thread.is_alive():
            self.bot_thread.join(timeout=2.0)
        self.bot_state = BotState.IDLE
        self.release_all_inputs()
        self.send_activity_sync("Bot stopped")

    # ------------------------------------------------------------------
    # WebSocket Server
    # ------------------------------------------------------------------
    async def _run_ws_server(self):
        """Async WebSocket server runner."""
        self.ws_server = await serve(self.ws_handler, self.ws_host, self.ws_port)
        logger.info(f"🌐 WebSocket server running on ws://{self.ws_host}:{self.ws_port}")
        await self.ws_server.wait_closed()

    def _ws_thread_target(self):
        """Thread target for WebSocket server."""
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        try:
            loop.run_until_complete(self._run_ws_server())
        except Exception as e:
            logger.error(f"WebSocket server error: {e}")
        finally:
            loop.close()

    def start(self):
        """Start WebSocket server and prepare bot."""
        self.ws_thread = threading.Thread(target=self._ws_thread_target, daemon=True)
        self.ws_thread.start()
        logger.info("Medic-AI Bot Server ready.")

    def shutdown(self):
        """Clean shutdown of all components."""
        logger.info("Shutting down...")
        self.stop_bot()
        if self.ws_server:
            self.ws_server.close()
        # Give threads a moment to finish
        time.sleep(0.5)
        logger.info("Server stopped.")

# ----------------------------------------------------------------------
# Entry Point
# ----------------------------------------------------------------------
def main():
    bot = MedicBot()
    bot.start()

    try:
        # Keep main thread alive
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\n🛑 Shutdown requested.")
        bot.shutdown()
        sys.exit(0)

if __name__ == "__main__":
    main()