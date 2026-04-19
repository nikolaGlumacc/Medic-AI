@echo off
setlocal EnableDelayedExpansion
title MedicAI Setup
color 0A

echo.
echo ============================================================
echo   MedicAI - Setup Script
echo ============================================================
echo.

:: ── Must be admin ─────────────────────────────────────────────
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] Please run this script as Administrator.
    echo Right-click build.bat and select "Run as administrator".
    pause
    exit /b 1
)

:: ── Internet check ────────────────────────────────────────────
ping -n 1 8.8.8.8 >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] No internet connection detected.
    pause
    exit /b 1
)

:: ── VC++ Redistributable ──────────────────────────────────────
echo [1/7] Checking Visual C++ Redistributable...
reg query "HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" /v Version >nul 2>&1
if %errorlevel% NEQ 0 (
    echo       Downloading VC++ Redistributable...
    powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile '$env:TEMP\vc_redist.x64.exe'"
    "%TEMP%\vc_redist.x64.exe" /quiet /norestart
    echo       VC++ Redistributable installed.
) else (
    echo       Already installed.
)

:: ── Python 3.11 ───────────────────────────────────────────────
echo [2/7] Checking Python 3.11...

set "PYTHON_EXE="

:: Check if venv already exists and works — skip all Python detection
if exist "c:\medicai_venv\Scripts\python.exe" (
    echo       Virtual environment already exists, skipping Python install.
    set "PYTHON_EXE=c:\medicai_venv\Scripts\python.exe"
    goto :VENV_DEPS
)

:: Try python command
python --version >nul 2>&1
if %errorlevel% EQU 0 (
    for /f "tokens=2" %%V in ('python --version 2^>^&1') do set "PY_VER=%%V"
    echo !PY_VER! | findstr "^3\.11" >nul
    if !errorlevel! EQU 0 (
        set "PYTHON_EXE=python"
        echo       Found Python !PY_VER! on PATH.
        goto :CREATE_VENV
    )
)

:: Try py launcher
py -3.11 --version >nul 2>&1
if %errorlevel% EQU 0 (
    set "PYTHON_EXE=py -3.11"
    echo       Found Python 3.11 via py launcher.
    goto :CREATE_VENV
)

:: Download and install Python 3.11.9
echo       Python 3.11 not found. Downloading...
powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe' -OutFile '$env:TEMP\python_installer.exe'"
echo       Installing Python 3.11.9...
"%TEMP%\python_installer.exe" /quiet InstallAllUsers=1 PrependPath=1 Include_test=0 /norestart
if %errorlevel% NEQ 0 (
    echo [ERROR] Python installation failed.
    pause
    exit /b 1
)

:: Refresh PATH from registry after install
for /f "skip=2 tokens=3*" %%A in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "SYS_PATH=%%A %%B"
for /f "skip=2 tokens=3*" %%A in ('reg query "HKCU\Environment" /v Path 2^>nul') do set "USR_PATH=%%A %%B"
set "PATH=%SYS_PATH%;%USR_PATH%;%PATH%"

:: Verify installation worked
python --version >nul 2>&1
if %errorlevel% NEQ 0 (
    echo [ERROR] Python installed but still not found on PATH.
    echo         Please close this window, reopen as Administrator, and run build.bat again.
    pause
    exit /b 1
)
set "PYTHON_EXE=python"
echo       Python 3.11.9 installed successfully.

:CREATE_VENV
echo [3/7] Creating virtual environment at c:\medicai_venv...
%PYTHON_EXE% -m venv c:\medicai_venv
if %errorlevel% NEQ 0 (
    echo [ERROR] Failed to create virtual environment.
    pause
    exit /b 1
)
echo       Virtual environment created.

:VENV_DEPS
echo [4/7] Installing Python dependencies...
c:\medicai_venv\Scripts\python.exe -m pip install --upgrade pip --quiet

:: Install only what the bot actually needs — no torch, no transformers
c:\medicai_venv\Scripts\pip install ^
    "websockets>=12.0" ^
    "pynput>=1.7.0" ^
    "psutil>=6.0.0" ^
    "mss>=9.0.0" ^
    "opencv-contrib-python>=4.8.0" ^
    "numpy>=1.24.0" ^
    "pytesseract>=0.3.10" ^
    "flask>=3.0.0" ^
    "pywin32>=306" ^
    "pydirectinput>=1.0.4" ^
    "scipy>=1.10.0" ^
    --quiet

if %errorlevel% NEQ 0 (
    echo [ERROR] Dependency installation failed.
    pause
    exit /b 1
)

:: Register pywin32 DLLs (requires admin — already checked above)
echo       Registering pywin32 DLLs...
c:\medicai_venv\Scripts\python.exe c:\medicai_venv\Scripts\pywin32_postinstall.py -install >nul 2>&1
echo       Dependencies installed.

:: ── Tesseract OCR ─────────────────────────────────────────────
echo [5/7] Checking Tesseract OCR...
if exist "C:\Program Files\Tesseract-OCR\tesseract.exe" (
    echo       Already installed.
) else (
    echo       Downloading Tesseract...
    powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://digi.bib.uni-mannheim.de/tesseract/tesseract-ocr-w64-setup-5.4.0.20240606.exe' -OutFile '$env:TEMP\tesseract_installer.exe'"
    "%TEMP%\tesseract_installer.exe" /S
    echo       Tesseract installed.
)

:: ── .NET 10 ───────────────────────────────────────────────────
echo [6/7] Checking .NET 10...

:: Check for desktop runtime first (what the WPF app needs)
dotnet --list-runtimes 2>nul | findstr "Microsoft.WindowsDesktop.App 10." >nul
if %errorlevel% EQU 0 (
    echo       .NET 10 Desktop Runtime already installed.
    goto :DOTNET_SDK_CHECK
)

echo       Installing .NET 10 Desktop Runtime...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; ^
    $s = (Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1').Content; ^
    &([scriptblock]::Create($s)) -Channel 10.0 -Runtime windowsdesktop -InstallDir '$env:ProgramFiles\dotnet'"

:DOTNET_SDK_CHECK
dotnet --version 2>nul | findstr "^10\." >nul
if %errorlevel% EQU 0 (
    echo       .NET 10 SDK already installed.
    goto :DOTNET_DONE
)

echo       Installing .NET 10 SDK...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; ^
    $s = (Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1').Content; ^
    &([scriptblock]::Create($s)) -Channel 10.0 -InstallDir '$env:ProgramFiles\dotnet'"

:: Refresh PATH so dotnet is visible in this session
set "PATH=%ProgramFiles%\dotnet;%USERPROFILE%\.dotnet;%PATH%"

:DOTNET_DONE
echo       .NET 10 ready.

:: ── Firewall rules ────────────────────────────────────────────
echo [6.5/7] Configuring firewall...
netsh advfirewall firewall show rule name="MedicAI Flask API" >nul 2>&1
if %errorlevel% NEQ 0 (
    netsh advfirewall firewall add rule name="MedicAI Flask API" dir=in action=allow protocol=TCP localport=5000 >nul
)
netsh advfirewall firewall show rule name="MedicAI WebSocket" >nul 2>&1
if %errorlevel% NEQ 0 (
    netsh advfirewall firewall add rule name="MedicAI WebSocket" dir=in action=allow protocol=TCP localport=8766 >nul
)
echo       Firewall rules set.

:: ── Build GUI ─────────────────────────────────────────────────
echo [7/7] Building GUI...
if not exist "%~dp0gui\MedicAIGUI.csproj" (
    echo [ERROR] gui\MedicAIGUI.csproj not found.
    pause
    exit /b 1
)

dotnet publish "%~dp0gui\MedicAIGUI.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -o "%~dp0publish_gui" ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true

if %errorlevel% NEQ 0 (
    echo [ERROR] GUI build failed. Check output above for details.
    pause
    exit /b 1
)
echo       GUI built to publish_gui\

:: ── Create required bot folders ───────────────────────────────
if not exist "%~dp0bot\templates" mkdir "%~dp0bot\templates"
if not exist "%~dp0bot\weapons"   mkdir "%~dp0bot\weapons"
echo       Bot resource folders ready.

echo.
echo ============================================================
echo   Setup complete!
echo   Run start_server.bat to start the bot brain.
echo   Run start.bat to launch the GUI dashboard.
echo   NOTE: TF2 must be running in windowed/borderless mode.
echo ============================================================
echo.
pause