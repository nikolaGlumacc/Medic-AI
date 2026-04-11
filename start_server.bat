@echo off
setlocal EnableExtensions
pushd "%~dp0"

echo ============================================
echo      MedicAI Bot Server - Brain Launcher    
echo ============================================

REM Find Python
set "PYTHON_EXE="
if exist "c:\medicai_venv\Scripts\python.exe" (
    set "PYTHON_EXE=c:\medicai_venv\Scripts\python.exe"
) else (
    set "PYTHON_EXE=python"
)

REM Check if port 5000 is already in use
netstat -ano | findstr /R /C:":5000 .*LISTENING" >nul
if not errorlevel 1 (
    echo.
    echo [ERROR] Port 5000 is already in use.
    echo MedicAI bot server may already be running.
    echo.
    pause
    exit /b 0
)

echo.
echo [SYSTEM] Starting MedicBot Brain on Port 5000...
echo Ready for dashboard connection.
echo Press Ctrl+C to stop.
echo.

"%PYTHON_EXE%" start_server.py

pause
popd
exit /b