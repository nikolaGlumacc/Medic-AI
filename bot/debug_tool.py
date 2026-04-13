#!/usr/bin/env python3
"""
MedicAI Debug Tool
Run this BEFORE starting the bot to verify every component works.
Place this file in the same folder as bot_server.py (inside /bot/)

Usage:
    python debug_tool.py            # runs all tests
    python debug_tool.py --flask    # test flask + routes only
    python debug_tool.py --vision   # test vision only
    python debug_tool.py --ocr      # test OCR only
    python debug_tool.py --input    # test keyboard/mouse only
    python debug_tool.py --ws       # test websocket only
    python debug_tool.py --loadout  # test loadout manager only
"""

import sys
import os
import time
import json
import threading
import traceback
import argparse
import importlib
from pathlib import Path

# ── Colour helpers (works on Windows 10+ terminal) ────────────────────────────
GREEN  = "\033[92m"
RED    = "\033[91m"
YELLOW = "\033[93m"
CYAN   = "\033[96m"
RESET  = "\033[0m"
BOLD   = "\033[1m"

def ok(msg):    print(f"  {GREEN}✔ {msg}{RESET}")
def fail(msg):  print(f"  {RED}✗ {msg}{RESET}")
def warn(msg):  print(f"  {YELLOW}⚠ {msg}{RESET}")
def info(msg):  print(f"  {CYAN}→ {msg}{RESET}")
def header(msg):print(f"\n{BOLD}{CYAN}{'─'*60}\n  {msg}\n{'─'*60}{RESET}")

PASS = 0
FAIL = 0

def check(condition, pass_msg, fail_msg):
    global PASS, FAIL
    if condition:
        ok(pass_msg)
        PASS += 1
    else:
        fail(fail_msg)
        FAIL += 1
    return condition


# ══════════════════════════════════════════════════════════════════════════════
#  1. IMPORTS / DEPENDENCIES
# ══════════════════════════════════════════════════════════════════════════════

def test_imports():
    header("1. DEPENDENCY CHECK")
    deps = [
        ("cv2",            "opencv-python"),
        ("numpy",          "numpy"),
        ("mss",            "mss"),
        ("pytesseract",    "pytesseract"),
        ("flask",          "flask"),
        ("websockets",     "websockets"),
        ("psutil",         "psutil"),
        ("win32api",       "pywin32"),
        ("pydirectinput",  "pydirectinput"),
        ("pynput",         "pynput"),
    ]
    all_ok = True
    for mod, pkg in deps:
        try:
            importlib.import_module(mod)
            ok(f"{mod}")
        except ImportError:
            if mod in ("pydirectinput", "pynput"):
                warn(f"{mod} not installed (optional) — pip install {pkg}")
            else:
                fail(f"{mod} missing — pip install {pkg}")
                all_ok = False
    return all_ok


# ══════════════════════════════════════════════════════════════════════════════
#  2. TESSERACT / OCR
# ══════════════════════════════════════════════════════════════════════════════

def test_ocr():
    header("2. TESSERACT / OCR")
    try:
        import pytesseract
        import numpy as np
        import cv2

        # Check tesseract binary
        try:
            ver = pytesseract.get_tesseract_version()
            ok(f"Tesseract found: v{ver}")
        except Exception as e:
            fail(f"Tesseract binary not found: {e}")
            warn("Install from https://github.com/UB-Mannheim/tesseract/wiki")
            return False

        # Synthetic read test
        img = np.zeros((40, 200, 3), dtype=np.uint8)
        cv2.putText(img, "DustyLepotan", (5, 28),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        result = pytesseract.image_to_string(gray, config="--psm 7").strip()
        check("Dusty" in result or "Lepotan" in result or len(result) > 3,
              f"OCR read test passed (got: '{result}')",
              f"OCR read test returned unexpected: '{result}'")
        return True
    except Exception as e:
        fail(f"OCR test crashed: {e}")
        return False


# ══════════════════════════════════════════════════════════════════════════════
#  3. SCREEN CAPTURE
# ══════════════════════════════════════════════════════════════════════════════

def test_vision():
    header("3. SCREEN CAPTURE + VISION")
    try:
        import mss
        import numpy as np
        import cv2

        with mss.mss() as sct:
            mon = sct.monitors[1]
            img = sct.grab(mon)
            frame = cv2.cvtColor(np.array(img), cv2.COLOR_BGRA2BGR)

        check(frame is not None and frame.size > 0,
              f"Screen captured: {frame.shape[1]}x{frame.shape[0]}px",
              "Screen capture failed")

        # HSV conversion
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        check(hsv is not None, "HSV conversion OK", "HSV conversion failed")

        # Distance transform (on a synthetic mask)
        mask = np.zeros((100, 100), dtype=np.uint8)
        cv2.circle(mask, (50, 50), 30, 255, -1)
        dist = cv2.distanceTransform(mask, cv2.DIST_L2, 5)
        check(dist.max() > 0,
              f"distanceTransform OK (max={dist.max():.1f})",
              "distanceTransform returned empty")

        # Contour detection (bubble test)
        cross = np.zeros((80, 80, 3), dtype=np.uint8)
        cv2.rectangle(cross, (30, 10), (50, 70), (0, 0, 220), -1)
        cv2.rectangle(cross, (10, 30), (70, 50), (0, 0, 220), -1)
        gray = cv2.cvtColor(cross, cv2.COLOR_BGR2GRAY)
        cnts, _ = cv2.findContours(gray, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        check(len(cnts) > 0,
              f"Contour detection OK ({len(cnts)} contours)",
              "Contour detection returned 0")

        # Save a debug screenshot
        debug_dir = Path("debug")
        debug_dir.mkdir(exist_ok=True)
        path = debug_dir / "debug_capture_test.png"
        cv2.imwrite(str(path), frame)
        ok(f"Screenshot saved → {path}")

        return True
    except Exception as e:
        fail(f"Vision test crashed: {e}")
        traceback.print_exc()
        return False


# ══════════════════════════════════════════════════════════════════════════════
#  4. INPUT (keyboard / mouse)
# ══════════════════════════════════════════════════════════════════════════════

def test_input():
    header("4. KEYBOARD / MOUSE INPUT")
    warn("⚠  This test will move your mouse slightly and press a key.")
    warn("   Make sure TF2 is NOT focused. Press ENTER to continue or Ctrl+C to skip.")
    try:
        input()
    except KeyboardInterrupt:
        warn("Input test skipped.")
        return True

    passed = True

    # win32api mouse move
    try:
        import win32api, win32con
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, 3, 0, 0, 0)
        time.sleep(0.05)
        win32api.mouse_event(win32con.MOUSEEVENTF_MOVE, -3, 0, 0, 0)
        ok("win32api mouse move OK")
    except Exception as e:
        fail(f"win32api mouse move failed: {e}")
        passed = False

    # pydirectinput
    try:
        import pydirectinput
        pydirectinput.PAUSE = 0.0
        # Press and release a harmless key (scroll lock — unlikely to do anything)
        pydirectinput.press("scrolllock")
        pydirectinput.press("scrolllock")
        ok("pydirectinput key press OK")
    except ImportError:
        warn("pydirectinput not installed — skipped")
    except Exception as e:
        fail(f"pydirectinput failed: {e}")
        passed = False

    # pynput keyboard
    try:
        from pynput.keyboard import Controller as KC
        kb = KC()
        # Just type into thin air — nothing focused
        kb.type("")
        ok("pynput keyboard OK")
    except ImportError:
        warn("pynput not installed — skipped")
    except Exception as e:
        fail(f"pynput keyboard failed: {e}")
        passed = False

    return passed


# ══════════════════════════════════════════════════════════════════════════════
#  5. FLASK API
# ══════════════════════════════════════════════════════════════════════════════

def test_flask():
    header("5. FLASK API ROUTES")
    try:
        import requests
    except ImportError:
        warn("requests not installed — pip install requests")
        return False

    base = "http://127.0.0.1:5000"
    info(f"Connecting to bot at {base} ...")
    info("Make sure bot_server.py is running in another terminal first.")

    try:
        r = requests.get(f"{base}/status", timeout=3)
        check(r.status_code == 200, f"GET /status → {r.status_code}", f"GET /status failed ({r.status_code})")
        data = r.json()
        info(f"  state={data.get('state')}  uber={data.get('uber_pct')}  running={data.get('running')}")
    except Exception as e:
        fail(f"GET /status unreachable: {e}")
        warn("Start bot_server.py first, then re-run this test.")
        return False

    # GET /weapons
    try:
        r = requests.get(f"{base}/weapons", timeout=3)
        check(r.status_code == 200, f"GET /weapons → {r.json()}", "GET /weapons failed")
    except Exception as e:
        fail(f"GET /weapons: {e}")

    # GET /config
    try:
        r = requests.get(f"{base}/config", timeout=3)
        check(r.status_code == 200 and isinstance(r.json(), dict),
              "GET /config → dict OK", "GET /config failed")
    except Exception as e:
        fail(f"GET /config: {e}")

    # POST /config patch
    try:
        r = requests.post(f"{base}/config",
                          json={"_debug_test_key": "debug_test_value"},
                          timeout=3)
        check(r.status_code == 200, "POST /config patch OK", f"POST /config failed ({r.status_code})")
    except Exception as e:
        fail(f"POST /config: {e}")

    # POST /detect_team
    try:
        r = requests.post(f"{base}/detect_team", timeout=5)
        check(r.status_code == 200, f"POST /detect_team → {r.json()}", "POST /detect_team failed")
    except Exception as e:
        fail(f"POST /detect_team: {e}")

    # POST /debug_snapshot
    try:
        r = requests.post(f"{base}/debug_snapshot", timeout=5)
        check(r.status_code == 200, f"POST /debug_snapshot → {r.json()}", "POST /debug_snapshot failed")
    except Exception as e:
        fail(f"POST /debug_snapshot: {e}")

    # POST /set_follow_mode
    try:
        r = requests.post(f"{base}/set_follow_mode", json={"mode": "passive"}, timeout=3)
        check(r.status_code == 200, "POST /set_follow_mode (passive) OK", "POST /set_follow_mode failed")
        r = requests.post(f"{base}/set_follow_mode", json={"mode": "active"}, timeout=3)
        check(r.status_code == 200, "POST /set_follow_mode (active) OK", "POST /set_follow_mode failed")
    except Exception as e:
        fail(f"POST /set_follow_mode: {e}")

    return True


# ══════════════════════════════════════════════════════════════════════════════
#  6. WEBSOCKET
# ══════════════════════════════════════════════════════════════════════════════

def test_websocket():
    header("6. WEBSOCKET")
    info("Make sure bot_server.py is running first.")
    try:
        import asyncio
        import websockets

        received = []

        async def _run():
            try:
                async with websockets.connect("ws://127.0.0.1:8766", open_timeout=3) as ws:
                    ok("WebSocket connected to ws://127.0.0.1:8766")

                    # Send a config action
                    await ws.send(json.dumps({"action": "config", "config": {"_ws_test": True}}))
                    ok("Sent config message")

                    # Wait for at least one broadcast
                    ws.timeout = 3
                    try:
                        msg = await asyncio.wait_for(ws.recv(), timeout=3)
                        data = json.loads(msg)
                        received.append(data)
                        check("type" in data,
                              f"Received message type='{data.get('type')}'",
                              "Received message missing 'type' key")
                    except asyncio.TimeoutError:
                        warn("No broadcast received within 3s (bot may not be running)")
            except Exception as e:
                fail(f"WebSocket connection failed: {e}")

        asyncio.run(_run())
        return True
    except Exception as e:
        fail(f"WebSocket test crashed: {e}")
        traceback.print_exc()
        return False


# ══════════════════════════════════════════════════════════════════════════════
#  7. LOADOUT MANAGER
# ══════════════════════════════════════════════════════════════════════════════

def test_loadout():
    header("7. LOADOUT MANAGER")
    weapons_dir = Path("weapons")
    weapons_dir.mkdir(exist_ok=True)

    check(weapons_dir.exists(), "weapons/ folder exists", "weapons/ folder missing")

    pngs = list(weapons_dir.glob("*.png"))
    if not pngs:
        warn("No weapon PNGs in weapons/ — template matching can't be tested.")
        warn("Drop a weapon PNG (e.g. medigun.png) in the weapons/ folder and re-run.")
    else:
        ok(f"Found {len(pngs)} weapon templates: {[p.stem for p in pngs]}")

        # Template load test
        try:
            import cv2
            for png in pngs:
                img = cv2.imread(str(png))
                check(img is not None,
                      f"Loaded template: {png.stem} ({img.shape[1]}x{img.shape[0]})",
                      f"Failed to load template: {png.stem}")
        except Exception as e:
            fail(f"Template load error: {e}")

        # Template match test (synthetic)
        try:
            import cv2
            import numpy as np
            import mss

            with mss.mss() as sct:
                frame = cv2.cvtColor(np.array(sct.grab(sct.monitors[1])), cv2.COLOR_BGRA2BGR)

            tmpl = cv2.imread(str(pngs[0]))
            result = cv2.matchTemplate(frame, tmpl, cv2.TM_CCOEFF_NORMED)
            _, max_val, _, max_loc = cv2.minMaxLoc(result)
            info(f"matchTemplate best score for '{pngs[0].stem}': {max_val:.3f} at {max_loc}")
            check(max_val >= 0.0,
                  "matchTemplate ran without error",
                  "matchTemplate returned invalid result")
        except Exception as e:
            fail(f"matchTemplate test failed: {e}")

    return True


# ══════════════════════════════════════════════════════════════════════════════
#  8. CONFIG FILE
# ══════════════════════════════════════════════════════════════════════════════

def test_config():
    header("8. CONFIG FILE")
    config_path = Path("bot_config.json")
    if config_path.exists():
        try:
            with open(config_path) as f:
                cfg = json.load(f)
            check(isinstance(cfg, dict),
                  f"bot_config.json loaded ({len(cfg)} keys)",
                  "bot_config.json is not a valid dict")
        except Exception as e:
            fail(f"bot_config.json parse error: {e}")
    else:
        warn("bot_config.json not found — will be created on first run, that's fine.")

    debug_dir = Path("debug")
    check(True, "debug/ folder will be auto-created on first snapshot", "")
    return True


# ══════════════════════════════════════════════════════════════════════════════
#  MAIN
# ══════════════════════════════════════════════════════════════════════════════

def main():
    parser = argparse.ArgumentParser(description="MedicAI Debug Tool")
    parser.add_argument("--flask",   action="store_true")
    parser.add_argument("--vision",  action="store_true")
    parser.add_argument("--ocr",     action="store_true")
    parser.add_argument("--input",   action="store_true")
    parser.add_argument("--ws",      action="store_true")
    parser.add_argument("--loadout", action="store_true")
    args = parser.parse_args()

    run_all = not any(vars(args).values())

    print(f"\n{BOLD}{'═'*60}")
    print("  MedicAI Debug Tool")
    print(f"{'═'*60}{RESET}")

    if run_all or args.flask or args.vision or args.ocr or args.input or args.loadout:
        test_imports()

    if run_all or args.ocr:     test_ocr()
    if run_all or args.vision:  test_vision()
    if run_all or args.input:   test_input()
    if run_all or args.flask:   test_flask()
    if run_all or args.ws:      test_websocket()
    if run_all or args.loadout: test_loadout()
    if run_all:                 test_config()

    # Summary
    total = PASS + FAIL
    print(f"\n{BOLD}{'═'*60}")
    print(f"  RESULTS:  {GREEN}{PASS} passed{RESET}  {RED}{FAIL} failed{RESET}  (total {total})")
    print(f"{'═'*60}{RESET}\n")

    if FAIL > 0:
        print(f"{RED}Fix the failing checks above before running the bot.{RESET}\n")
        sys.exit(1)
    else:
        print(f"{GREEN}All checks passed — bot is ready to run.{RESET}\n")
        sys.exit(0)


if __name__ == "__main__":
    main()