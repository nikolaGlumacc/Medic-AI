@echo off
setlocal EnableExtensions
pushd "%~dp0"

echo ============================================
echo  MedicAI Bot Server - Laptop Launcher
echo ============================================

REM --- Find Python ---
set "PYTHON_EXE="

if exist "c:\medicai_venv\Scripts\python.exe" (
    set "PYTHON_EXE=c:\medicai_venv\Scripts\python.exe"
    goto found_python
)

if exist "%~dp0.venv\Scripts\python.exe" (
    set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"
    goto found_python
)

where python >nul 2>nul
if not errorlevel 1 (
    for /f "delims=" %%I in ('where python') do (
        set "PYTHON_EXE=%%I"
        goto found_python
    )
)

echo ERROR: Could not find Python.
echo Please create a venv at c:\medicai_venv\ and install requirements.txt
pause
exit /b 1

:found_python
echo Using Python: %PYTHON_EXE%

REM --- Check if port 8765 is already in use ---
netstat -ano | findstr /R /C:":8765 .*LISTENING" >nul
if not errorlevel 1 (
    echo.
    echo Port 8765 is already in use.
    echo MedicAI bot server may already be running.
    echo If not, close whatever is using port 8765 and try again.
    echo.
    netstat -ano | findstr :8765
    pause
    exit /b 0
)

REM --- Check Tesseract ---
where tesseract >nul 2>nul
if errorlevel 1 (
    echo.
    echo WARNING: Tesseract OCR not found in PATH.
    echo HUD reading will be disabled until you install it.
    echo Download: https://github.com/UB-Mannheim/tesseract/wiki
    echo.
)

echo.
echo Starting bot server on port 8765...
echo Connect the GUI on your main PC to this machine's IP.
echo Press Ctrl+C to stop.
echo.

"%PYTHON_EXE%" bot\bot_server.py

pause
popd
exit /b