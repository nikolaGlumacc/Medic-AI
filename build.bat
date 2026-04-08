@echo off
setlocal EnableExtensions

set "NO_PAUSE="
set "SKIP_PIP="

:parse_args
if "%~1"=="" goto args_done
if /I "%~1"=="--no-pause" set "NO_PAUSE=1"
if /I "%~1"=="--skip-pip" set "SKIP_PIP=1"
shift
goto parse_args

:args_done
pushd "%~dp0"

:: ─────────────────────────────────────────────────────────
:: 1. Resolve Python
:: ─────────────────────────────────────────────────────────
call :resolve_python
if errorlevel 1 goto fail

echo Using Python: %PYTHON_EXE%
"%PYTHON_EXE%" --version
if errorlevel 1 goto fail

:: ─────────────────────────────────────────────────────────
:: 2. Install Python dependencies
:: ─────────────────────────────────────────────────────────
if not defined SKIP_PIP (
    echo.
    echo Installing Python dependencies...
    "%PYTHON_EXE%" -m pip install --disable-pip-version-check --upgrade pip
    "%PYTHON_EXE%" -m pip install --disable-pip-version-check -r requirements.txt
    if errorlevel 1 (
        echo ERROR: Failed to install Python dependencies.
        goto fail
    )
)

:: ─────────────────────────────────────────────────────────
:: 3. Verify bot imports
:: ─────────────────────────────────────────────────────────
echo.
echo Verifying bot imports...
"%PYTHON_EXE%" test_bot_imports.py
if errorlevel 1 (
    echo ERROR: Bot dependency verification failed.
    goto fail
)

:: ─────────────────────────────────────────────────────────
:: 4. Check .NET SDK (need 10.x)
:: ─────────────────────────────────────────────────────────
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet SDK was not found in PATH.
    echo.
    echo  Download the .NET 10 SDK from:
    echo  https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    goto fail
)

:: Check that SDK version 10.x is present
dotnet --list-sdks 2>nul | findstr /R /C:"^10\." >nul
if errorlevel 1 (
    echo ERROR: .NET 10 SDK is not installed.
    echo        The GUI targets net10.0-windows and requires the .NET 10 SDK.
    echo.
    echo  Installed SDKs on this machine:
    dotnet --list-sdks
    echo.
    echo  Download .NET 10 SDK from:
    echo  https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    goto fail
)

echo .NET 10 SDK found.

:: ─────────────────────────────────────────────────────────
:: 5. Restore NuGet packages (this installs Newtonsoft.Json)
:: ─────────────────────────────────────────────────────────
echo.
echo Restoring NuGet packages (Newtonsoft.Json etc.)...
set "DOTNET_CLI_HOME=%CD%"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
dotnet restore gui\MedicAIGUI.csproj
if errorlevel 1 (
    echo ERROR: NuGet restore failed.
    echo        Check your internet connection and try again.
    goto fail
)

:: ─────────────────────────────────────────────────────────
:: 6. Build the GUI
:: ─────────────────────────────────────────────────────────
echo.
echo Building MedicAI GUI...
dotnet build gui\MedicAIGUI.csproj -c Debug --no-restore
if errorlevel 1 (
    echo Build failed – retrying with full restore...
    dotnet build gui\MedicAIGUI.csproj -c Debug
    if errorlevel 1 (
        echo ERROR: GUI build failed.
        goto fail
    )
)

echo.
echo ============================================================
echo  Build complete!
echo ============================================================
echo  Run start.bat       to launch the bot server AND the GUI.
echo  Run start_server.bat to launch only the bot server.
echo ============================================================
set "EXITCODE=0"
goto done

:: ─────────────────────────────────────────────────────────
:: Subroutines
:: ─────────────────────────────────────────────────────────

:resolve_python
:: Priority 1: dedicated venv at c:\medicai_venv
if exist "c:\medicai_venv\Scripts\python.exe" (
    set "PYTHON_EXE=c:\medicai_venv\Scripts\python.exe"
    exit /b 0
)

:: Priority 2: local .venv in repo folder
if exist "%~dp0.venv\Scripts\python.exe" (
    set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"
    exit /b 0
)

:: Priority 3: create .venv using py launcher
where py >nul 2>nul
if not errorlevel 1 (
    echo No MedicAI virtual environment found. Creating .venv with py launcher...
    py -3 -m venv .venv
    if errorlevel 1 (
        echo ERROR: Failed to create .venv with the py launcher.
        exit /b 1
    )
    set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"
    exit /b 0
)

:: Priority 4: create .venv using plain python
where python >nul 2>nul
if not errorlevel 1 (
    echo No MedicAI virtual environment found. Creating .venv with python...
    python -m venv .venv
    if errorlevel 1 (
        echo ERROR: Failed to create .venv with python.
        exit /b 1
    )
    set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"
    exit /b 0
)

echo ERROR: Python 3.11 or newer was not found on this machine.
echo        Install Python from https://python.org and rerun build.bat.
exit /b 1

:fail
set "EXITCODE=1"

:done
popd
if not defined NO_PAUSE pause
exit /b %EXITCODE%