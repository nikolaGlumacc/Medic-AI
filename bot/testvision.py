import asyncio
import cv2
from bot_server import MedicVisionClassic

async def dummy_sender(msg):
    print(f"[BOT] {msg}")

async def main():
    print("=== MEDIC VISION DEBUG MODE ===")
    print("• TF2 must be open (preferably windowed or borderless)")
    print("• Make someone call 'medic!'")
    print("• A window named 'Medic Bot Debug' will open")
    print("• Press 'q' in the debug window to stop\n")

    vision = MedicVisionClassic(dummy_sender)
    vision.update_config({"priority_names": ["yourname", "friend"]})

    try:
        await vision.run()
    except KeyboardInterrupt:
        print("\nTest stopped.")
    finally:
        cv2.destroyAllWindows()

if __name__ == "__main__":
    asyncio.run(main())