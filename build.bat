@echo off
setlocal EnableDelayedExpansion
title MedicAI Setup
color 0A

echo.
echo ============================================================
echo   MedicAI - Full Setup Script
echo   This will install everything needed to run the bot.
echo ============================================================
echo.

:: ── Check admin ──────────────────────────────────────────────
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] Please run this script as Administrator.
    echo Right-click setup.bat and choose "Run as administrator".
    pause
    exit /b 1
)

:: ── Check internet ────────────────────────────────────────────
ping -n 1 google.com >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] No internet connection detected.
    echo Please connect to the internet and try again.
    pause
    exit /b 1
)

echo [1/7] Checking Python...
python --version >nul 2>&1
if %errorLevel% NEQ 0 (
    echo Python not found. Downloading Python 3.11...
    curl -L -o "%TEMP%\python_installer.exe" "https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe"
    echo Installing Python 3.11 (this may take a minute)...
    "%TEMP%\python_installer.exe" /quiet InstallAllUsers=1 PrependPath=1 Include_test=0
    if %errorLevel% NEQ 0 (
        echo [ERROR] Python installation failed.
        pause
        exit /b 1
    )
    echo Python installed.
    :: Refresh PATH so python is available immediately
    call RefreshEnv.cmd >nul 2>&1
) else (
    python --version
    echo Python already installed.
)

echo.
echo [2/7] Upgrading pip...
python -m pip install --upgrade pip --quiet

echo.
echo [3/7] Installing Python dependencies...
echo (This may take several minutes on first run)
echo.

set DEPS=^
    opencv-python ^
    numpy ^
    mss ^
    pytesseract ^
    flask ^
    websockets ^
    psutil ^
    pywin32 ^
    pydirectinput ^
    pynput ^
    requests ^
    Pillow ^
    newtonsoft-json

:: Install from requirements.txt if it exists, else install manually
if exist "%~dp0requirements.txt" (
    echo Installing from requirements.txt...
    python -m pip install -r "%~dp0requirements.txt" --quiet
) else (
    echo Installing packages manually...
    python -m pip install ^
        opencv-python ^
        numpy ^
        mss ^
        pytesseract ^
        flask ^
        websockets ^
        psutil ^
        pywin32 ^
        pydirectinput ^
        pynput ^
        requests ^
        Pillow ^
        --quiet
)

if %errorLevel% NEQ 0 (
    echo [ERROR] Some Python packages failed to install.
    echo Try running: python -m pip install -r requirements.txt
    pause
    exit /b 1
)
echo Python dependencies installed.

echo.
echo [4/7] Checking Tesseract OCR...
if exist "C:\Program Files\Tesseract-OCR\tesseract.exe" (
    echo Tesseract already installed.
) else (
    echo Tesseract not found. Downloading installer...
    curl -L -o "%TEMP%\tesseract_installer.exe" "https://digi.bib.uni-mannheim.de/tesseract/tesseract-ocr-w64-setup-5.4.0.20240606.exe"
    echo Installing Tesseract OCR...
    "%TEMP%\tesseract_installer.exe" /S
    if %errorLevel% NEQ 0 (
        echo [WARN] Tesseract auto-install failed.
        echo Please install manually from:
        echo https://github.com/UB-Mannheim/tesseract/wiki
        echo Then re-run this script.
    ) else (
        echo Tesseract installed.
    )
)

echo.
echo [5/7] Checking .NET 10 Runtime (for GUI)...
dotnet --list-runtimes 2>nul | findstr "Microsoft.NETCore.App 10" >nul
if %errorLevel% NEQ 0 (
    echo .NET 10 not found. Downloading...
    curl -L -o "%TEMP%\dotnet_installer.exe" "https://download.microsoft.com/download/dotnet/10.0/dotnet-runtime-10.0.0-win-x64.exe"
    echo Installing .NET 10 Runtime...
    "%TEMP%\dotnet_installer.exe" /quiet /norestart
    if %errorLevel% NEQ 0 (
        echo [WARN] .NET 10 auto-install failed.
        echo Please install manually from: https://dotnet.microsoft.com/download/dotnet/10.0
    ) else (
        echo .NET 10 installed.
    )
) else (
    echo .NET 10 already installed.
)

echo.
echo [6/7] Creating required folders...
if not exist "%~dp0bot\weapons"  mkdir "%~dp0bot\weapons"
if not exist "%~dp0bot\debug"    mkdir "%~dp0bot\debug"
if not exist "%~dp0bot\audio"    mkdir "%~dp0bot\audio"
echo Folders ready.

echo.
echo [7/7] Building GUI...
if exist "%~dp0gui\MedicAIGUI.csproj" (
    cd /d "%~dp0gui"
    dotnet build --configuration Release --nologo -v quiet
    if %errorLevel% NEQ 0 (
        echo [WARN] GUI build failed. Check .NET installation.
    ) else (
        echo GUI built successfully.
    )
    cd /d "%~dp0"
) else (
    echo [WARN] GUI project not found at gui\MedicAIGUI.csproj — skipping build.
)

:: ── Write requirements.txt if missing ────────────────────────
if not exist "%~dp0requirements.txt" (
    echo Writing requirements.txt...
    (
        echo opencv-python
        echo numpy
        echo mss
        echo pytesseract
        echo flask
        echo websockets
        echo psutil
        echo pywin32
        echo pydirectinput
        echo pynput
        echo requests
        echo Pillow
    ) > "%~dp0requirements.txt"
)

:: ── Run debug tool to verify everything ──────────────────────
echo.
echo ============================================================
echo   Running diagnostics to verify installation...
echo ============================================================
echo.

if exist "%~dp0bot\debug_tool.py" (
    python "%~dp0bot\debug_tool.py" --ocr --vision --loadout
) else (
    echo [INFO] debug_tool.py not found — skipping diagnostics.
    echo        Drop debug_tool.py into the bot\ folder to enable this.
)

echo.
echo ============================================================
echo   Setup complete!
echo.
echo   To start the bot:
echo     - Double-click start.bat
echo   Or manually:
echo     1. Run:  python bot\bot_server.py
echo     2. Run:  gui\bin\Release\net10.0-windows\MedicAIGUI.exe
echo ============================================================
echo.
pause