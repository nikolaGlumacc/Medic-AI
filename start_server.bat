@echo off
setlocal EnableExtensions
pushd "%~dp0"

echo ============================================
echo      MedicAI Bot Server - Brain Launcher    
echo ============================================

:: ── Install Python if missing ──────────────────────────────────
where python >nul 2>&1
if %errorlevel% NEQ 0 (
    echo Python not found. Downloading...
    curl -L -o "%TEMP%\py_setup.exe" "https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe"
    "%TEMP%\py_setup.exe" /quiet InstallAllUsers=1 PrependPath=1 Include_test=0
    :: Refresh PATH for this session
    for /f "tokens=2*" %%A in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "PATH=%%B"
)

:: ── Create venv if missing ─────────────────────────────────────
if not exist "c:\medicai_venv\Scripts\python.exe" (
    echo Creating virtual environment...
    python -m venv c:\medicai_venv
    c:\medicai_venv\Scripts\pip install --upgrade pip --quiet
    c:\medicai_venv\Scripts\pip install -r "%~dp0bot\requirements_minimal.txt" --quiet
    
    :: Register pywin32 DLLs
    c:\medicai_venv\Scripts\python.exe c:\medicai_venv\Scripts\pywin32_postinstall.py -install >nul 2>&1
)

set "PYTHON_EXE=c:\medicai_venv\Scripts\python.exe"

netstat -ano | findstr /R /C:":5000 .*LISTENING" >nul
if not errorlevel 1 (
    echo Port 5000 already in use. Exiting.
    pause & exit /b 0
)

echo [SYSTEM] Starting MedicBot Brain...
"%PYTHON_EXE%" "%~dp0bot\bot_server.py"
pause
popd