@echo off
setlocal EnableDelayedExpansion
title MedicAI Setup - Deep Integration
color 0A

echo.
echo ============================================================
echo   MedicAI - Full Setup Script (Deep Integration)
echo   Choose your setup mode:
echo     [1] FULL BOT SETUP (Gaming Machine - Downloads Brain/OCR)
echo     [2] REMOTE DASHBOARD ONLY (Laptop - Fast, Single Folder)
echo ============================================================
echo.
set /p MODE="Select mode [1 or 2]: "

if "%MODE%"=="2" (
    echo [SYSTEM] Remote Dashboard Mode selected. Skipping heavy dependencies...
    goto :GUI_ONLY
)

:: ── Check admin (Required for Full Setup) ──────────────────────

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

:: ── Install VC++ Redistributable ────────────────────────────────
echo.
echo [0/7] Checking for Visual C++ Redistributable 2015-2022...
reg query "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" /v Version >nul 2>&1
if %errorlevel% NEQ 0 (
    echo [0/7] VC++ Redistributable not found. Downloading...
    curl -L -o "%TEMP%\vc_redist.x64.exe" "https://aka.ms/vs/17/release/vc_redist.x64.exe"
    echo Installing VC++ Redistributable (Quiet)...
    "%TEMP%\vc_redist.x64.exe" /quiet /norestart
    echo VC++ Redistributable installed.
) else (
    echo [0/7] VC++ Redistributable already installed.
)

:: ── Check Python 3.11 ─────────────────────────────────────────
set "PYTHON_CMD=python"
python --version 2>&1 | findstr "3.11" >nul
if %errorlevel% NEQ 0 (
    echo [1/7] Python 3.11 not found on default 'python' command. Checking 'py' launcher...
    py -3.11 --version >nul 2>&1
    if %errorlevel% EQU 0 (
        echo [1/7] Found Python 3.11 via 'py' launcher.
        set "PYTHON_CMD=py -3.11"
    ) else (
        echo [1/7] Python 3.11 not found. Downloading Python 3.11...
        curl -L -o "%TEMP%\python_installer.exe" "https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe"
        echo Installing Python 3.11.9 (Quiet Mode)...
        "%TEMP%\python_installer.exe" /quiet InstallAllUsers=1 PrependPath=1 Include_test=0
        
        :: Refresh PATH for current session
        for /f "tokens=2*" %%A in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "PATH=%%B"
        set "PYTHON_CMD=python"
    )
) else (
    echo [1/7] Python 3.11 already installed and default.
)

echo.
echo [2/7] Preparing Virtual Environment...
if not exist "c:\medicai_venv\Scripts\python.exe" (
    echo Creating virtual environment at c:\medicai_venv using Python 3.11...
    !PYTHON_CMD! -m venv c:\medicai_venv
)
c:\medicai_venv\Scripts\python.exe -m pip install --upgrade pip --quiet

echo.
echo [3/7] Installing Bot Dependencies...
if exist "%~dp0requirements.txt" (
    echo [INFO] Installing all dependencies from requirements.txt...
    c:\medicai_venv\Scripts\pip install -r "%~dp0requirements.txt" --quiet
) else if exist "%~dp0bot\requirements_minimal.txt" (
    echo [INFO] requirements.txt not found. Using requirements_minimal.txt...
    c:\medicai_venv\Scripts\pip install -r "%~dp0bot\requirements_minimal.txt" --quiet
) else (
    echo [WARN] No requirements file found. Falling back to manual install.
    c:\medicai_venv\Scripts\pip install websockets pynput psutil mss opencv-contrib-python numpy pytesseract flask pywin32 pydirectinput scipy pyautogui keyboard pyttsx3 transformers --quiet
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
echo [5/7] Checking .NET 10 (SDK & Desktop Runtime)...
dotnet --list-runtimes | findstr "Microsoft.WindowsDesktop.App 10." >nul
if %errorlevel% NEQ 0 (
    echo .NET 10 Desktop Runtime not found. Installing via dotnet-install...
    powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -Channel 10.0 -Runtime windowsdesktop"
)

dotnet --version | findstr "^10." >nul
if %errorlevel% NEQ 0 (
    echo .NET 10 SDK not found. Installing via dotnet-install...
    powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -Channel 10.0"
    
    :: Refresh PATH for dotnet
    set "PATH=%PATH%;%USERPROFILE%\.dotnet;%ProgramFiles%\dotnet"
) else (
    echo [5/7] .NET 10 is already correctly installed.
    dotnet --version
)

echo Folders ready.

:GUI_ONLY
echo.
echo [SYSTEM] Ensuring .NET 10 is available...
dotnet --version >nul 2>&1
if %errorlevel% NEQ 0 (
    echo [INFO] .NET SDK not found. Installing minimal runtime for Dashboard...
    powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -Channel 10.0 -Runtime windowsdesktop"
    set "PATH=%PATH%;%USERPROFILE%\.dotnet;%ProgramFiles%\dotnet"
)

echo.

 
echo.
echo [6.5/7] Configuring Windows Firewall...
netsh advfirewall firewall show rule name="MedicAI Flask API" >nul 2>&1
if %errorlevel% NEQ 0 (
    echo [INFO] Adding Firewall exception for Flask API (Port 5000)...
    netsh advfirewall firewall add rule name="MedicAI Flask API" dir=in action=allow protocol=TCP localport=5000 >nul
)
netsh advfirewall firewall show rule name="MedicAI WebSocket" >nul 2>&1
if %errorlevel% NEQ 0 (
    echo [INFO] Adding Firewall exception for WebSocket (Port 8766)...
    netsh advfirewall firewall add rule name="MedicAI WebSocket" dir=in action=allow protocol=TCP localport=8766 >nul
)
echo Firewall configured.


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