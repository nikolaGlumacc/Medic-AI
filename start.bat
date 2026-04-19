@echo off
setlocal EnableDelayedExpansion
pushd "%~dp0"
title MedicAI Launcher

echo.
echo ============================================================
echo   MedicAI Launcher
echo ============================================================

:: ── Verify publish exists ─────────────────────────────────────
if not exist "%~dp0publish_gui\MedicAIGUI.exe" (
    echo [ERROR] GUI not built yet. Please run build.bat first.
    pause
    exit /b 1
)

:: ── Verify venv exists ────────────────────────────────────────
if not exist "c:\medicai_venv\Scripts\python.exe" (
    echo [ERROR] Python environment not set up. Please run build.bat first.
    pause
    exit /b 1
)

:: ── Start bot server in its own window ───────────────────────
echo [1/2] Starting bot server...
start "MedicAI Bot Server" cmd /k "c:\medicai_venv\Scripts\python.exe "%~dp0bot\bot_server.py""

:: ── Wait for server to initialize ────────────────────────────
echo       Waiting for server to initialize...
timeout /t 3 /nobreak >nul

:: ── Launch GUI ───────────────────────────────────────────────
echo [2/2] Launching MedicAI GUI...
start "" "%~dp0publish_gui\MedicAIGUI.exe"

popd
exit /b 0