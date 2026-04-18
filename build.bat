@echo off
setlocal EnableDelayedExpansion
title MedicAI Setup - Deep Integration
color 0A

echo.
echo ============================================================
echo   MedicAI - Full Setup Script (Deep Integration)
echo   This will install everything needed to run the bot.
echo ============================================================
echo.

:: ── Check admin ──────────────────────────────────────────────
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] Please run this script as Administrator.
    echo Right-click build.bat and choose "Run as administrator".
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

:: ── Install Python 3.11 if missing ─────────────────────────────
where python >nul 2>&1
if %errorlevel% NEQ 0 (
    echo [1/7] Python not found. Downloading Python 3.11...
    curl -L -o "%TEMP%\python_installer.exe" "https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe"
    echo Installing Python 3.11.9 (Quiet Mode)...
    "%TEMP%\python_installer.exe" /quiet InstallAllUsers=1 PrependPath=1 Include_test=0
    
    :: Refresh PATH for current session
    for /f "tokens=2*" %%A in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "PATH=%%B"
    echo Python installed.
) else (
    echo [1/7] Python already installed.
    python --version
)

echo.
echo [2/7] Preparing Virtual Environment...
if not exist "c:\medicai_venv\Scripts\python.exe" (
    echo Creating virtual environment at c:\medicai_venv...
    python -m venv c:\medicai_venv
)
c:\medicai_venv\Scripts\python.exe -m pip install --upgrade pip --quiet

echo.
echo [3/7] Installing Bot Dependencies...
if exist "%~dp0bot\requirements_minimal.txt" (
    echo [INFO] Using requirements_minimal.txt
    c:\medicai_venv\Scripts\pip install -r "%~dp0bot\requirements_minimal.txt" --quiet
) else (
    echo [WARN] requirements_minimal.txt not found. Falling back to manual install.
    c:\medicai_venv\Scripts\pip install websockets pynput psutil mss opencv-contrib-python numpy pytesseract flask pywin32 pydirectinput scipy --quiet
)

:: ── pywin32 DLL registration fix ──────────────────────────────
echo Registering pywin32 DLLs...
c:\medicai_venv\Scripts\python.exe c:\medicai_venv\Scripts\pywin32_postinstall.py -install >nul 2>&1

echo.
echo [4/7] Checking Tesseract OCR...
if exist "C:\Program Files\Tesseract-OCR\tesseract.exe" (
    echo Tesseract already installed.
) else (
    echo Tesseract not found. Downloading installer...
    curl -L -o "%TEMP%\tesseract_installer.exe" "https://digi.bib.uni-mannheim.de/tesseract/tesseract-ocr-w64-setup-5.4.0.20240606.exe"
    echo Installing Tesseract OCR (Silent)...
    "%TEMP%\tesseract_installer.exe" /S
    echo Tesseract installation initiated.
)

echo.
echo [5/7] Checking .NET 8 SDK (for GUI Build)...
dotnet --version >nul 2>&1
if %errorlevel% NEQ 0 (
    echo .NET SDK not found. Downloading .NET 8 SDK...
    curl -L -o "%TEMP%\dotnet_sdk.exe" "https://download.microsoft.com/download/dotnet/8.0/dotnet-sdk-8.0.410-win-x64.exe"
    echo Installing .NET 8 SDK (Quiet Mode)...
    "%TEMP%\dotnet_sdk.exe" /quiet /norestart
    
    :: Refresh PATH
    for /f "tokens=2*" %%A in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "PATH=%%B"
) else (
    echo [5/7] .NET SDK already installed.
    dotnet --version
)

echo.
echo [6/7] Creating required folders...
if not exist "%~dp0bot\templates" mkdir "%~dp0bot\templates"
if not exist "%~dp0bot\weapons"   mkdir "%~dp0bot\weapons"
if not exist "%~dp0bot\debug"     mkdir "%~dp0bot\debug"
if not exist "%~dp0bot\logs"      mkdir "%~dp0bot\logs"
echo Folders ready.

echo.
echo [7/7] Building GUI (optimized Release)...
if exist "%~dp0gui\MedicAIGUI.csproj" (
    dotnet publish "%~dp0gui\MedicAIGUI.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0publish_gui" /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
    if %errorLevel% NEQ 0 (
        echo [ERROR] GUI build failed.
    ) else (
        echo GUI published to publish_gui/
    )
) else (
    echo [ERROR] GUI project not found.
)

echo.
echo ============================================================
echo   Setup complete!
echo.
echo   To start the bot:
echo     - Double-click start.bat
echo.
echo   Note: Ensure TF2 is running in Windowed/Borderless mode
echo         (-windowed -noborder in launch options)
echo ============================================================
echo.
pause