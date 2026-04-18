@echo off
pushd "%~dp0"
echo [SYSTEM] Performing Deep System Integration...
echo [SYSTEM] Bundling all dependencies into self-contained folder...
echo.

REM Publish as self-contained to eliminate all "missing framework" issues
dotnet publish gui\MedicAIGUI.csproj -c Debug -r win-x64 --self-contained true -o publish_gui

if %errorlevel% neq 0 (
    echo [ERROR] Build/Publish failed. Please check the logs above.
    pause
    goto end
)

REM Launch the Python Bot Server in a separate window
echo [SYSTEM] Starting MedicBot Brain...
start "MedicAI Bot Server" cmd /c "start_server.bat"

REM Wait a few seconds for the server to bind ports
timeout /t 3 /nobreak >nul

cd /d "%~dp0publish_gui"
echo [SYSTEM] Launching Command Deck...
MedicAIGUI.exe

:end
pause
popd
