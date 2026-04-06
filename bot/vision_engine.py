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
        self.resolution = (1920, 1080)           # Change if your resolution is different
        self.template_path = "templates/medic_call.png"
        self.match_threshold = 0.68              # Lowered to make detection easier

        self.spin_speed = 6                      # ~3 second 360° spin
        self.aim_sensitivity = 0.58
        self.heal_offset_y = +40                 # Aim below center (name tag area)

        self.priority_names = set()              # Updated from GUI

        # Team colors (BLU teammates)
        self.team_lower = np.array([95, 80, 80])
        self.team_upper = np.array([130, 255, 255])

        # Load Medic call template
        self.medic_template = cv2.imread(self.template_path, cv2.IMREAD_COLOR)
        if self.medic_template is None:
            raise FileNotFoundError(f"Template not found: {self.template_path}\n"
                                  f"Make sure the file is in 'templates/medic_call.png'")

        self.template_h, self.template_w = self.medic_template.shape[:2]

        # State
        self.locked_target = None
        self.target_history = deque(maxlen=15)
        self.spinning = True
        self.last_medic_time = 0
        self.last_seen_target_time = 0

        print("✅ Medic Vision Classic Loaded")
        print(f"   Template loaded: {self.template_path}")
        print(f"   Match threshold: {self.match_threshold}")
        print("   Debug window active - Press 'q' to quit")

    async def send_command(self, command: str, data=None):
        msg = {"type": command}
        if data:
            msg["data"] = data
        try:
            await self.ws_sender(json.dumps(msg))
        except:
            pass

    def update_config(self, config_data):
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
        roi_y1 = max(0, y + 25)
        roi_y2 = min(frame.shape[0], y + 240)
        roi_x1 = max(0, x - 80)
        roi_x2 = min(frame.shape[1], x + 80)

        if roi_y1 >= roi_y2 or roi_x1 >= roi_x2:
            return None

        roi = frame[roi_y1:roi_y2, roi_x1:roi_x2]
        hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
        mask = cv2.inRange(hsv, self.team_lower, self.team_upper)

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
        cy = int(m["m01"] / m["m00"]) + roi_y1 + 25   # aim lower inside player
        return (cx, cy)

    async def run(self):
        print("Vision loop started - Waiting for medic calls...")
        
        while True:
            frame = self.capture_screen()

            # ================== DEBUG WINDOW ==================
            debug_frame = frame.copy()

            calls = self.detect_medic_calls(frame)
            
            if calls:
                print(f"[{time.strftime('%H:%M:%S')}] Found {len(calls)} potential medic call(s)")
                for i, cross in enumerate(calls):
                    cv2.circle(debug_frame, cross, 18, (0, 0, 255), 4)
                    cv2.putText(debug_frame, f"MEDIC CALL {i+1}", (cross[0]-60, cross[1]-35),
                               cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)

            if self.locked_target:
                tx, ty = self.locked_target
                cv2.circle(debug_frame, (tx, ty), 25, (0, 255, 0), 4)
                cv2.putText(debug_frame, "LOCKED TARGET", (tx-80, ty-45),
                           cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)

            cv2.imshow("Medic Bot Debug", debug_frame)
            if cv2.waitKey(1) & 0xFF == ord('q'):
                print("Debug window closed by user")
                break
            # ==================================================

            if self.locked_target is None:
                # Idle spinning
                if self.spinning:
                    await self.send_command("mouse_move", {"dx": self.spin_speed, "dy": 0})

                # Check for medic calls
                if calls and (time.time() - self.last_medic_time > 0.7):
                    cross = min(calls, key=lambda p: p[1])   # take highest one
                    target_pos = self.find_target_below(frame, cross)

                    if target_pos:
                        self.locked_target = target_pos
                        self.spinning = False
                        self.last_medic_time = time.time()
                        self.last_seen_target_time = time.time()
                        await self.send_command("medic_locked")
                        print("🔴 SUCCESS! TARGET LOCKED!")
                    else:
                        print("Detected medic call but no teammate color found below it")

            else:
                # Healing mode
                target_pos = self.find_target_below(frame, self.locked_target)

                if target_pos:
                    self.locked_target = target_pos
                    self.last_seen_target_time = time.time()

                    screen_cx = frame.shape[1] // 2
                    screen_cy = frame.shape[0] // 2 + self.heal_offset_y

                    dx = target_pos[0] - screen_cx
                    dy = target_pos[1] - screen_cy

                    await self.send_command("mouse_move", {
                        "dx": int(dx * self.aim_sensitivity),
                        "dy": int(dy * self.aim_sensitivity)
                    })
                elif time.time() - self.last_seen_target_time > 5.0:
                    print("Target lost → resuming spin")
                    self.locked_target = None
                    self.spinning = True
                    await self.send_command("medic_unlocked")

            await asyncio.sleep(0.01)


# For starting from bot_server.py
async def start_vision(ws_sender):
    vision = MedicVisionClassic(ws_sender)
    await vision.run()