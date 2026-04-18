import asyncio
import cv2
from bot_server import Vision

async def dummy_sender(msg):
    print(f"[BOT] {msg}")

async def main():
    print("=== MEDIC VISION DEBUG MODE ===")
    print("• TF2 must be open (preferably windowed or borderless)")
    print("• Make someone call 'medic!'")
    print("• A window named 'Medic Bot Debug' will open")
    print("• Press 'q' in the debug window to stop\n")

    vision = Vision()

    try:
        while True:
            frame = vision.capture()
            if frame is not None:
                # Mirror what the bot sees
                mask = vision.get_team_mask(frame)
                blobs = vision.find_blobs(frame)
                
                # Draw blobs
                for (cx, cy, area, aspect) in blobs:
                    cv2.circle(frame, (cx, cy), 10, (0, 255, 0), 2)
                    cv2.putText(frame, f"A:{int(area)} Asp:{aspect:.1f}", (cx+15, cy), 
                                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)

                # Show HUD data
                snap = vision.read_hud_snapshot(frame)
                cv2.putText(frame, f"Uber: {snap.uber_charge}%", (50, 50), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 0), 2)
                if snap.health:
                    cv2.putText(frame, f"HP: {snap.health}", (50, 80), 
                                cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 255), 2)

                cv2.imshow("Medic Bot Debug", frame)
                cv2.imshow("Team Mask", mask)

            if cv2.waitKey(1) & 0xFF == ord('q'):
                break
            await asyncio.sleep(0.01)
    except KeyboardInterrupt:
        print("\nTest stopped.")
    finally:
        cv2.destroyAllWindows()


if __name__ == "__main__":
    asyncio.run(main())