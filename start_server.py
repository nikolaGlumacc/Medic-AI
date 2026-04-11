#!/usr/bin/env python3
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'bot'))

from bot_server import MedicBot


def main():
    print("Starting MedicAI Bot Server...")
    bot = MedicBot()
    try:
        bot.run()
    except OSError as exc:
        if getattr(exc, "errno", None) == 10048:
            print("ERROR: Port 5000 is already in use.")
            print("Close the existing listener or connect the GUI to the running server.")
            return 1

        print(f"ERROR: Failed to start bot server: {exc}")
        return 1
    except KeyboardInterrupt:
        print("MedicAI bot server stopped.")
        return 0

    return 0


if __name__ == '__main__':
    sys.exit(main())
