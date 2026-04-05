import sys
import os

# Add bot directory to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'bot'))

try:
    from vision_engine import VisionEngine
    print("✅ VisionEngine import successful")

    # Try to initialize
    try:
        vision = VisionEngine()
        print("✅ VisionEngine initialization successful")
        print(f"✅ YOLO model loaded: {vision.model is not None}")
    except Exception as e:
        print(f"⚠️  VisionEngine initialization failed: {e}")

except ImportError as e:
    print(f"❌ VisionEngine import failed: {e}")

try:
    from watchdog import WatchdogProcess
    print("✅ WatchdogProcess import successful")
except ImportError as e:
    print(f"❌ WatchdogProcess import failed: {e}")

print("✅ Import test completed")