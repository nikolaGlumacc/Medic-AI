@echo off
setlocal EnableExtensions
pushd "%~dp0"
title MedicAI Bot Server

echo.
echo ============================================================
echo   MedicAI Bot Server
echo ============================================================

:: ── Verify venv ───────────────────────────────────────────────
if not exist "c:\medicai_venv\Scripts\python.exe" (
    echo [ERROR] Virtual environment not found at c:\medicai_venv
    echo         Please run build.bat first.
    pause
    exit /b 1
)

:: ── Verify bot_server.py exists ───────────────────────────────
if not exist "%~dp0bot\bot_server.py" (
    echo [ERROR] bot\bot_server.py not found.
    pause
    exit /b 1
)

:: ── Check if port 5000 already in use ─────────────────────────
netstat -ano 2>nul | findstr /R /C:":5000 .*LISTENING" >nul
if not errorlevel 1 (
    echo [WARN] Port 5000 is already in use.
    echo        Another instance may already be running.
    echo        Close the other instance or check Task Manager.
    pause
    exit /b 0
)

:: ── Check if port 8766 already in use ─────────────────────────
netstat -ano 2>nul | findstr /R /C:":8766 .*LISTENING" >nul
if not errorlevel 1 (
    echo [WARN] Port 8766 is already in use.
    echo        Another instance may already be running.
    pause
    exit /b 0
)

echo [OK] Starting MedicAI Bot Server...
echo      WebSocket : ws://0.0.0.0:8766
echo      Flask API : http://0.0.0.0:5000
echo.
echo      Press Ctrl+C to stop.
echo.

c:\medicai_venv\Scripts\python.exe "%~dp0bot\bot_server.py"

echo.
echo [INFO] Bot server stopped.
pause
popd