#!/usr/bin/env python3
import sys
import os
import threading
import time

# Always resolve project root properly
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
BOT_DIR = os.path.join(BASE_DIR, "bot")

sys.path.insert(0, BOT_DIR)

from bot_server import MedicBot


def main():
    print("============================================")
    print("     MedicAI Bot Server - Launcher")
    print("============================================\n")

    print("[SYSTEM] Initializing MedicBot...")

    bot = MedicBot()

    try:
        # Start WebSocket server FIRST (important)
        print("[SYSTEM] Starting WebSocket server...")
        threading.Thread(target=bot.start_ws, daemon=True).start()

        time.sleep(0.5)  # allow WS loop to initialize

        print("[SYSTEM] Starting bot brain...")
        bot.start()

        print("[OK] MedicAI is running.")

        # Keep main thread alive (prevents instant exit)
        while True:
            time.sleep(1)

    except OSError as exc:
        if getattr(exc, "errno", None) == 10048:
            print("ERROR: Port already in use (likely 5000 or 8766).")
            print("Fix: close the other instance or change ports in config.")
            return 1

        print(f"ERROR: Failed to start server: {exc}")
        return 1

    except KeyboardInterrupt:
        print("\n[INFO] Shutting down MedicAI...")
        bot.stop()
        return 0

    except Exception as exc:
        print(f"FATAL ERROR: {exc}")
        return 1


if __name__ == "__main__":
    sys.exit(main())