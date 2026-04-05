import cv2
import numpy as np
import mss
import time
from collections import deque
import asyncio
import json

class MedicVisionClassic:
    def __init__(self, ws_sender):
        self.ws_sender = ws_sender

        # ================== CONFIG ==================
        self.resolution = (1920, 1080)           # Change if your TF2 resolution is different
        self.template_path = "templates/medic_call.png"
        self.match_threshold = 0.72

        self.spin_speed = 6                      # ≈ 3 second full 360° spin
        self.aim_sensitivity = 0.55
        self.heal_offset_y = +35                 # Aim below center (name tag area)

        # Priority will come from GUI (updated via config message)
        self.priority_names = set()              # Will be updated from GUI

        # Team colors (BLU teammates - change if you're RED)
        self.team_lower = np.array([95, 80, 80])
        self.team_upper = np.array([130, 255, 255])

        # Load template
        self.medic_template = cv2.imread(self.template_path, cv2.IMREAD_COLOR)
        if self.medic_template is None:
            raise FileNotFoundError(f"Template not found: {self.template_path}\n"
                                  f"Create folder 'templates' and put medic_call.png there.")

        self.template_h, self.template_w = self.medic_template.shape[:2]

        # State
        self.locked_target = None
        self.target_history = deque(maxlen=20)
        self.spinning = True
        self.last_medic_time = 0
        self.last_seen_target_time = 0

        print("✅ Classic Medic Vision loaded (No YOLO)")
        print(f"   Spin speed: {self.spin_speed} → ~3s 360°")
        print("   Priority names will be received from GUI")

    async def send_command(self, command: str, data=None):
        msg = {"type": command}
        if data:
            msg["data"] = data
        try:
            await self.ws_sender(json.dumps(msg))
        except:
            pass

    def update_config(self, config_data):
        """Receive priority names from GUI"""
        if "priority_names" in config_data:
            self.priority_names = {name.lower().strip() for name in config_data["priority_names"]}
            print(f"Priority names updated from GUI: {self.priority_names}")

    def capture_screen(self):
        with mss.mss() as sct:
            monitor = {"top": 0, "left": 0, "width": self.resolution[0], "height": self.resolution[1]}
            img = np.array(sct.grab(monitor))
            return cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)

    def detect_medic_calls(self, frame):
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        template_gray = cv2.cvtColor(self.medic_template, cv2.COLOR_BGR2GRAY)

        result = cv2.matchTemplate(gray, template_gray, cv2.TM_CCOEFF_NORMED)
        locations = np.where(result >= self.match_threshold)

        calls = []
        for pt in zip(*locations[::-1]):
            cx = pt[0] + self.template_w // 2
            cy = pt[1] + self.template_h // 2 - 25
            calls.append((cx, cy))
        return calls

    def find_target_below(self, frame, cross_pos):
        x, y = cross_pos
        roi_y1 = max(0, y + 20)
        roi_y2 = min(frame.shape[0], y + 220)
        roi_x1 = max(0, x - 70)
        roi_x2 = min(frame.shape[1], x + 70)

        if roi_y1 >= roi_y2 or roi_x1 >= roi_x2:
            return None

        roi = frame[roi_y1:roi_y2, roi_x1:roi_x2]
        hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
        mask = cv2.inRange(hsv, self.team_lower, self.team_upper)

        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return None

        largest = max(contours, key=cv2.contourArea)
        if cv2.contourArea(largest) < 350:
            return None

        m = cv2.moments(largest)
        if m["m00"] == 0:
            return None

        cx = int(m["m10"] / m["m00"]) + roi_x1
        cy = int(m["m01"] / m["m00"]) + roi_y1
        return (cx, cy)

    async def run(self):
        while True:
            frame = self.capture_screen()

            if self.locked_target is None:
                # Idle slow spin
                if self.spinning:
                    await self.send_command("mouse_move", {"dx": self.spin_speed, "dy": 0})

                # Check medic calls
                calls = self.detect_medic_calls(frame)
                if calls and (time.time() - self.last_medic_time > 0.8):
                    cross = min(calls, key=lambda p: p[1])

                    target_pos = self.find_target_below(frame, cross)
                    if target_pos:
                        self.locked_target = target_pos
                        self.spinning = False
                        self.last_medic_time = time.time()
                        self.last_seen_target_time = time.time()
                        await self.send_command("medic_locked", {"target": target_pos})
                        print("🔴 MEDIC CALL - Target locked!")

            else:
                # Healing mode
                target_pos = self.find_target_below(frame, self.locked_target)

                if target_pos:
                    self.locked_target = target_pos
                    self.last_seen_target_time = time.time()
                    self.target_history.append(target_pos)

                    screen_cx = frame.shape[1] // 2
                    screen_cy = frame.shape[0] // 2 + self.heal_offset_y

                    dx = target_pos[0] - screen_cx
                    dy = target_pos[1] - screen_cy

                    await self.send_command("mouse_move", {
                        "dx": int(dx * self.aim_sensitivity),
                        "dy": int(dy * self.aim_sensitivity)
                    })

                # Unlock if lost
                if time.time() - self.last_seen_target_time > 6.0:
                    print("Target lost - resuming spin")
                    self.locked_target = None
                    self.spinning = True
                    await self.send_command("medic_unlocked")

            await asyncio.sleep(0.016)


async def start_vision(ws_sender):
    vision = MedicVisionClassic(ws_sender)
    await vision.run()