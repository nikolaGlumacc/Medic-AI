import mss
import mss.tools
import pytesseract
import cv2
import numpy as np
from ultralytics import YOLO

class VisionEngine:
    def __init__(self):
        self.sct = mss.mss()
        try:
            self.model = YOLO('yolov8n.pt')  # Fallback to base YOLO if no custom model
        except Exception as e:
            print(f"Failed to load YOLO model: {e}")
            self.model = None

    def capture_screen(self):
        # Capture the primary monitor
        monitor = self.sct.monitors[1]
        sct_img = self.sct.grab(monitor)
        # Convert to numpy array for cv2
        img = np.array(sct_img)
        # Drop the alpha channel
        img = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)
        return img

    def run_inference(self):
        frame = self.capture_screen()
        if self.model:
            results = self.model(frame, verbose=False)
            return results, frame
        return None, frame

    def read_text_center(self):
        frame = self.capture_screen()
        h, w, _ = frame.shape
        # Crop center region where player names appear
        center_region = frame[h//2:h//2+100, w//2-200:w//2+200]
        # Grayscale and threshold for OCR
        gray = cv2.cvtColor(center_region, cv2.COLOR_BGR2GRAY)
        _, thresh = cv2.threshold(gray, 150, 255, cv2.THRESH_BINARY_INV)
        text = pytesseract.image_to_string(thresh).strip()
        return text

    def parse_scoreboard(self):
        frame = self.capture_screen()
        # Stub logic: In a real scenario, this would detect gray vs white row text
        return {"stub_player": "alive"}
