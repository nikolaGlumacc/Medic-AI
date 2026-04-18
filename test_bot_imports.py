#!/usr/bin/env python3
"""
Test script for MedicAI bot server startup
"""
import sys
import os

# Add bot directory to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'bot'))

def test_imports():
    """Test that all required imports work"""
    try:
        import asyncio
        import websockets
        import json
        import time
        import threading
        import psutil
        from pynput.keyboard import Controller as KeyboardController
        from pynput.mouse import Controller as MouseController, Button

        print("  [OK] All core dependencies imported successfully")

        # Test basic bot server import
        try:
            from bot_server import MedicBot
            print("  [OK] MedicBot class imported successfully")

            # Test basic instantiation
            bot = MedicBot()
            print("  [OK] MedicBot instance created successfully")

        except ImportError as e:
            print(f"  [FAIL] Failed to import MedicBot: {e}")
            return False
        except Exception as e:
            print(f"  [FAIL] Failed to create MedicBot instance: {e}")
            return False

    except ImportError as e:
        print(f"  [FAIL] Failed to import dependencies: {e}")
        return False
    except Exception as e:
        print(f"  [FAIL] Unexpected error during import test: {e}")
        return False

    return True

if __name__ == "__main__":
    success = test_imports()
    sys.exit(0 if success else 1)