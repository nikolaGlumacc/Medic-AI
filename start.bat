@echo off
setlocal EnableExtensions

pushd "%~dp0"

call :resolve_python
if errorlevel 1 goto fail

REM Build the GUI
echo Building MedicAI GUI...
set "DOTNET_CLI_HOME=%CD%"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
dotnet build gui\MedicAIGUI.csproj -c Debug --no-restore
if %errorlevel% neq 0 (
    echo Cached build failed. Trying restore plus build...
    dotnet build gui\MedicAIGUI.csproj -c Debug
    if %errorlevel% neq 0 (
        echo ERROR: GUI build failed. Check that .NET 10 SDK is installed.
        goto fail
    )
)

REM Start bot server
call :check_server_port
if errorlevel 1 (
    echo Port 8765 is already in use. Skipping bot server launch.
    echo If that is an old process, close it before rerunning start.bat.
) else (
    echo Starting MedicAI Bot Server...
    start "MedicAI Bot Server" cmd /k ""%PYTHON_EXE%" "%~dp0start_server.py" --no-pause"
    timeout /t 2 /nobreak >nul
)

echo Launching MedicAI Dashboard...
start "" "%~dp0gui\bin\Debug\net10.0-windows\MedicAIGUI.exe"

popd
exit /b

:resolve_python
if exist "c:\medicai_venv\Scripts\python.exe" (
    set "PYTHON_EXE=c:\medicai_venv\Scripts\python.exe"
    exit /b 0
)

if exist "%~dp0.venv\Scripts\python.exe" (
    set "PYTHON_EXE=%~dp0.venv\Scripts\python.exe"
    exit /b 0
)

where python >nul 2>nul
if not errorlevel 1 (
    for /f "delims=" %%I in ('where python') do (
        set "PYTHON_EXE=%%I"
        goto start_python_found
    )
)

echo ERROR: Could not find a Python executable for MedicAI.
echo Run build.bat first to set up the project.
exit /b 1

:start_python_found
exit /b 0

:check_server_port
netstat -ano | findstr /R /C:":8765 .*LISTENING" >nul
if errorlevel 1 exit /b 0
exit /b 1

:fail
popd
pause
exit /b 1
