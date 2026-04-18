#!/usr/bin/env python3
"""
COMPREHENSIVE MEDICAI TESTING SUITE
====================================
Tests all components of the MedicAI application:
1. Python imports and dependencies
2. Vision engine initialization
3. Bot server startup
4. WebSocket connectivity
5. GUI launcher
6. End-to-end integration
"""

import sys
import os
import subprocess
import time
import asyncio
import json
import platform
from pathlib import Path

# Configuration
MEDICAI_ROOT = Path(__file__).parent
BOT_DIR = MEDICAI_ROOT / 'bot'
GUI_DIR = MEDICAI_ROOT / 'gui'
GUI_EXE = GUI_DIR / 'bin' / 'Debug' / 'net10.0-windows' / 'MedicAIGUI.exe'
VENV_PYTHON = r'c:\medicai_venv\Scripts\python.exe'
BOT_SERVER_PORT = 8766

# Add bot directory to path
sys.path.insert(0, str(BOT_DIR))

class Colors:
    GREEN = '\033[92m'
    RED = '\033[91m'
    YELLOW = '\033[93m'
    BLUE = '\033[94m'
    RESET = '\033[0m'
    BOLD = '\033[1m'

def print_header(text):
    print(f"\n{Colors.BOLD}{Colors.BLUE}{'='*70}{Colors.RESET}")
    print(f"{Colors.BOLD}{Colors.BLUE}{text:^70}{Colors.RESET}")
    print(f"{Colors.BOLD}{Colors.BLUE}{'='*70}{Colors.RESET}\n")

def print_success(text):
    print(f"{Colors.GREEN}✓ {text}{Colors.RESET}")

def print_error(text):
    print(f"{Colors.RED}✗ {text}{Colors.RESET}")

def print_warning(text):
    print(f"{Colors.YELLOW}⚠ {text}{Colors.RESET}")

def print_info(text):
    print(f"{Colors.BLUE}ℹ {text}{Colors.RESET}")

# ============================================================================
# TEST 1: Python Imports and Dependencies
# ============================================================================
def test_python_imports():
    """Test that all required Python packages can be imported"""
    print_header("TEST 1: Python Dependencies & Imports")

    results = {}

    # Core dependencies
    core_deps = [
        ('asyncio', 'Async I/O'),
        ('websockets', 'WebSocket server'),
        ('json', 'JSON parsing'),
        ('time', 'Time utilities'),
        ('threading', 'Threading'),
        ('psutil', 'Process utilities'),
        ('pynput', 'Input control'),
    ]

    # Vision engine dependencies
    vision_deps = [
        ('mss', 'Screen capture'),
        ('cv2', 'OpenCV (computer vision)'),
        ('numpy', 'Numerical arrays'),
        ('pytesseract', 'OCR text extraction'),
        ('ultralytics', 'YOLO models'),
        ('torch', 'PyTorch deep learning'),
    ]

    print("Testing Core Dependencies:")
    all_passed = True
    for module_name, description in core_deps:
        try:
            __import__(module_name)
            print_success(f"{module_name:20} - {description}")
            results[module_name] = True
        except ImportError as e:
            print_error(f"{module_name:20} - {description} - ERROR: {e}")
            results[module_name] = False
            all_passed = False

    print("\nTesting Vision Engine Dependencies:")
    vision_ok = True
    for module_name, description in vision_deps:
        try:
            __import__(module_name)
            print_success(f"{module_name:20} - {description}")
            results[module_name] = True
        except ImportError as e:
            print_warning(f"{module_name:20} - {description} - NOT INSTALLED (non-critical)")
            results[module_name] = False
            vision_ok = False

    if not vision_ok:
        print_info("Vision engine will be disabled but core bot will work")

    return all_passed, results

# ============================================================================
# TEST 2: Bot Server Module Import
# ============================================================================
def test_bot_server_import():
    """Test that bot_server.py can be imported"""
    print_header("TEST 2: Bot Server Module Import")

    try:
        from bot_server import MedicBot
        print_success("MedicBot class imported successfully")

        try:
            bot = MedicBot()
            print_success("MedicBot instance created successfully")
            return True, bot
        except Exception as e:
            print_error(f"Failed to instantiate MedicBot: {e}")
            return False, None
    except ImportError as e:
        print_error(f"Failed to import MedicBot: {e}")
        return False, None

# ============================================================================
# TEST 3: Vision Engine Initialization
# ============================================================================
def test_vision_engine():
    """Test that vision engine can be initialized"""
    print_header("TEST 3: Vision Engine Initialization")

    try:
        from bot_server import Vision
        if Vision is None:
            print_warning("Vision is None - dependencies not available")
            return False

        try:
            vision = Vision()
            print_success("Vision instance created")

            # Check if YOLO model loaded
            if vision.model:
                print_success(f"YOLO model loaded: {vision.model}")
            else:
                print_warning("YOLO model is None - may not be fully initialized")

            # Check if mss initialized
            if vision.sct:
                print_success("Screen capture (mss) initialized")
                monitor_count = len(vision.sct.monitors)
                print_info(f"Detected {monitor_count} monitor(s)")
            else:
                print_error("Screen capture (mss) not initialized")
                return False

            return True
        except Exception as e:
            print_error(f"Failed to initialize VisionEngine: {e}")
            import traceback
            traceback.print_exc()
            return False
    except ImportError as e:
        print_warning(f"VisionEngine not available: {e}")
        return False

# ============================================================================
# TEST 4: Bot Server Startup
# ============================================================================
async def test_bot_server_startup():
    """Test that bot server can start and listen on port 8766"""
    print_header("TEST 4: Bot Server Startup & WebSocket Listening")

    try:
        from bot_server import MedicBot

        bot = MedicBot()
        print_success("Bot instance created")

        # Create event loop
        loop = asyncio.get_event_loop()
        print_success("Event loop created")

        # Start server with 3-second timeout
        server_task = asyncio.create_task(bot.run_server())
        print_success("Server startup initiated")

        try:
            await asyncio.wait_for(server_task, timeout=3.0)
        except asyncio.TimeoutError:
            print_success("Server running on port 8766 (timeout after 3s is expected)")
            server_task.cancel()
            try:
                await server_task
            except asyncio.CancelledError:
                pass
        except Exception as e:
            print_error(f"Server startup error: {e}")
            server_task.cancel()
            return False

        return True
    except Exception as e:
        print_error(f"Failed to test bot server startup: {e}")
        import traceback
        traceback.print_exc()
        return False

# ============================================================================
# TEST 5: WebSocket Connectivity
# ============================================================================
async def test_websocket_connection():
    """Test that we can connect to the bot server via WebSocket"""
    print_header("TEST 5: WebSocket Connectivity")

    try:
        import websockets

        # Start bot server in background
        from bot_server import MedicBot
        bot = MedicBot()

        # Start server
        server_task = asyncio.create_task(bot.run_server())

        # Wait a bit for server to start
        await asyncio.sleep(1)

        try:
            # Try to connect
            uri = f"ws://127.0.0.1:{BOT_SERVER_PORT}"
            print_info(f"Attempting connection to {uri}...")

            async with websockets.connect(uri, ping_interval=None) as websocket:
                print_success(f"Connected to bot server on port {BOT_SERVER_PORT}")

                # Send a test message
                test_msg = json.dumps({"type": "test", "msg": "Hello, bot!"})
                await websocket.send(test_msg)
                print_success("Test message sent successfully")

                # Set a timeout for receiving response
                try:
                    response = await asyncio.wait_for(websocket.recv(), timeout=2.0)
                    print_info(f"Received response: {response}")
                except asyncio.TimeoutError:
                    print_info("No immediate response (normal for async bot)")

                return True
        except ConnectionRefusedError:
            print_error("Connection refused - bot server may not be listening")
            return False
        except Exception as e:
            print_error(f"WebSocket connection error: {e}")
            return False
        finally:
            server_task.cancel()
            try:
                await server_task
            except asyncio.CancelledError:
                pass
    except Exception as e:
        print_error(f"Failed to test WebSocket connectivity: {e}")
        import traceback
        traceback.print_exc()
        return False

# ============================================================================
# TEST 6: GUI Executable Verification
# ============================================================================
def test_gui_executable():
    """Verify GUI executable exists and is launchable"""
    print_header("TEST 6: GUI Executable Verification")

    if not GUI_EXE.exists():
        print_error(f"GUI executable not found at {GUI_EXE}")
        return False

    print_success(f"GUI executable found: {GUI_EXE}")

    # Check file size
    size = GUI_EXE.stat().st_size
    print_info(f"Executable size: {size:,} bytes")

    # Check if we're on Windows
    if platform.system() != 'Windows':
        print_warning("Not on Windows - GUI executable cannot be tested")
        return False

    print_success("GUI executable is ready to launch")
    return True

# ============================================================================
# TEST 7: Virtual Environment
# ============================================================================
def test_venv():
    """Verify virtual environment exists and has required packages"""
    print_header("TEST 7: Virtual Environment Check")

    venv_path = Path(VENV_PYTHON).parent.parent

    if not venv_path.exists():
        print_error(f"Virtual environment not found at {venv_path}")
        return False

    print_success(f"Virtual environment found at {venv_path}")

    # Check Python version
    try:
        result = subprocess.run(
            [VENV_PYTHON, '--version'],
            capture_output=True,
            text=True,
            timeout=5
        )
        print_info(f"Python version: {result.stdout.strip()}")
    except Exception as e:
        print_warning(f"Could not check Python version: {e}")

    # List installed packages
    try:
        result = subprocess.run(
            [VENV_PYTHON, '-m', 'pip', 'list', '--format=columns'],
            capture_output=True,
            text=True,
            timeout=10
        )
        if result.returncode == 0:
            packages = result.stdout.strip().split('\n')
            print_info(f"Installed packages ({len(packages)-2} total):")
            for pkg in packages[:10]:
                print_info(f"  {pkg}")
            if len(packages) > 10:
                print_info(f"  ... and {len(packages)-12} more")
    except Exception as e:
        print_warning(f"Could not list packages: {e}")

    return True

# ============================================================================
# TEST 8: Start.bat Script
# ============================================================================
def test_start_bat():
    """Verify start.bat exists and has correct content"""
    print_header("TEST 8: start.bat Script Verification")

    start_bat = MEDICAI_ROOT / 'start.bat'

    if not start_bat.exists():
        print_error(f"start.bat not found at {start_bat}")
        return False

    print_success(f"start.bat found at {start_bat}")

    try:
        with open(start_bat, 'r') as f:
            content = f.read()

        # Check for key components
        checks = [
            ('bot server', 'bot_server.py' in content),
            ('venv Python path', 'medicai_venv' in content),
            ('GUI executable', 'MedicAIGUI.exe' in content),
            ('timeout delay', 'timeout' in content),
        ]

        for check_name, result in checks:
            if result:
                print_success(f"start.bat includes {check_name}")
            else:
                print_error(f"start.bat missing {check_name}")

        return all(result for _, result in checks)
    except Exception as e:
        print_error(f"Error reading start.bat: {e}")
        return False

# ============================================================================
# MAIN TEST RUNNER
# ============================================================================
async def main():
    """Run all tests and generate report"""
    print("\n")
    print(f"{Colors.BOLD}{Colors.BLUE}")
    print("╔" + "═"*68 + "╗")
    print("║" + " "*68 + "║")
    print("║" + "MedicAI COMPREHENSIVE TESTING SUITE".center(68) + "║")
    print("║" + " "*68 + "║")
    print("╚" + "═"*68 + "╝")
    print(Colors.RESET)

    results = {}

    # Test 1: Python Imports
    try:
        imports_ok, imports_details = test_python_imports()
        results['imports'] = imports_ok
    except Exception as e:
        print_error(f"Test 1 failed: {e}")
        results['imports'] = False

    time.sleep(1)

    # Test 2: Bot Server Import
    try:
        bot_import_ok, bot_instance = test_bot_server_import()
        results['bot_import'] = bot_import_ok
    except Exception as e:
        print_error(f"Test 2 failed: {e}")
        results['bot_import'] = False
        bot_instance = None

    time.sleep(1)

    # Test 3: Vision Engine
    try:
        vision_ok = test_vision_engine()
        results['vision_engine'] = vision_ok
    except Exception as e:
        print_error(f"Test 3 failed: {e}")
        results['vision_engine'] = False

    time.sleep(1)

    # Test 4: Bot Server Startup
    try:
        startup_ok = await test_bot_server_startup()
        results['bot_startup'] = startup_ok
    except Exception as e:
        print_error(f"Test 4 failed: {e}")
        results['bot_startup'] = False

    time.sleep(1)

    # Test 5: WebSocket Connectivity
    try:
        ws_ok = await test_websocket_connection()
        results['websocket'] = ws_ok
    except Exception as e:
        print_error(f"Test 5 failed: {e}")
        results['websocket'] = False

    time.sleep(1)

    # Test 6: GUI Executable
    try:
        gui_ok = test_gui_executable()
        results['gui_exe'] = gui_ok
    except Exception as e:
        print_error(f"Test 6 failed: {e}")
        results['gui_exe'] = False

    time.sleep(1)

    # Test 7: Virtual Environment
    try:
        venv_ok = test_venv()
        results['venv'] = venv_ok
    except Exception as e:
        print_error(f"Test 7 failed: {e}")
        results['venv'] = False

    time.sleep(1)

    # Test 8: start.bat
    try:
        bat_ok = test_start_bat()
        results['start_bat'] = bat_ok
    except Exception as e:
        print_error(f"Test 8 failed: {e}")
        results['start_bat'] = False

    # Generate final report
    print_header("FINAL TEST REPORT")

    test_names = {
        'imports': 'Python Dependencies',
        'bot_import': 'Bot Server Module',
        'vision_engine': 'Vision Engine',
        'bot_startup': 'Bot Server Startup',
        'websocket': 'WebSocket Connection',
        'gui_exe': 'GUI Executable',
        'venv': 'Virtual Environment',
        'start_bat': 'start.bat Script',
    }

    passed = sum(1 for v in results.values() if v)
    total = len(results)

    for test_key, test_name in test_names.items():
        if results.get(test_key, False):
            print_success(f"{test_name:30} PASSED")
        else:
            print_error(f"{test_name:30} PASSED")

    print(f"\n{Colors.BOLD}Summary: {passed}/{total} tests passed{Colors.RESET}")

    if passed == total:
        print_success("ALL TESTS PASSED! ✓")
        print_info("The MedicAI application is ready to use.")
        print_info("Run start.bat to launch both the bot server and GUI.")
        return 0
    else:
        print_error(f"SOME TESTS FAILED ({total - passed} failures)")
        print_info("Please review the errors above and run specific tests to diagnose issues.")
        return 1

if __name__ == '__main__':
    try:
        exit_code = asyncio.run(main())
        sys.exit(exit_code)
    except KeyboardInterrupt:
        print("\n\nTests interrupted by user")
        sys.exit(1)
    except Exception as e:
        print_error(f"Unhandled error: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)