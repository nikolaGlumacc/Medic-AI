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

call :resolve_python
if errorlevel 1 goto fail

echo Using Python: %PYTHON_EXE%
"%PYTHON_EXE%" --version
if errorlevel 1 goto fail

if not defined SKIP_PIP (
    echo Installing Python dependencies...
    "%PYTHON_EXE%" -m pip install --disable-pip-version-check -r requirements.txt
    if errorlevel 1 (
        echo ERROR: Failed to install Python dependencies.
        goto fail
    )
)

echo Verifying bot imports...
"%PYTHON_EXE%" test_bot_imports.py
if errorlevel 1 (
    echo ERROR: Bot dependency verification failed.
    goto fail
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: dotnet SDK was not found.
    echo Install the .NET SDK, then rerun build.bat.
    goto fail
)

echo Building MedicAI GUI...
set "DOTNET_CLI_HOME=%CD%"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
dotnet build gui\MedicAIGUI.csproj -c Debug --no-restore
if errorlevel 1 (
    echo Cached build failed. Trying restore plus build...
    dotnet build gui\MedicAIGUI.csproj -c Debug
    if errorlevel 1 (
        echo ERROR: GUI build failed.
        goto fail
    )
)

echo.
echo Build complete.
echo Use start_server.bat to launch only the bot server.
echo Use start.bat to launch both the bot server and the GUI.
set "EXITCODE=0"
goto done

:resolve_python
if exist "c:\medicai_venv\Scripts\python.exe" (
    set "PYTHON_EXE=c:\medicai_venv\Scripts\python.exe"
    exit /b 0
)

if exist "%~dp0.venv\Scripts\python.exe" (
    set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"
    exit /b 0
)

where py >nul 2>nul
if not errorlevel 1 (
    echo No MedicAI virtual environment found. Creating .venv...
    py -3 -m venv .venv
    if errorlevel 1 (
        echo ERROR: Failed to create .venv with the py launcher.
        exit /b 1
    )

    set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"
    exit /b 0
)

where python >nul 2>nul
if not errorlevel 1 (
    echo No MedicAI virtual environment found. Creating .venv...
    python -m venv .venv
    if errorlevel 1 (
        echo ERROR: Failed to create .venv with python.
        exit /b 1
    )

    set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"
    exit /b 0
)

echo ERROR: Python 3.11 or newer was not found.
echo Install Python, then rerun build.bat.
exit /b 1

:fail
set "EXITCODE=1"

:done
popd
if not defined NO_PAUSE pause
exit /b %EXITCODE%
