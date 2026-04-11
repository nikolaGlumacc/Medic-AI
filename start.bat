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

cd /d "%~dp0publish_gui"
echo [SYSTEM] Launching Command Deck...
MedicAIGUI.exe

if exist "CRASH_LOG.txt" (
    echo.
    echo [FATAL ERROR] The app crashed on startup.
    echo --- ERROR DETAILS FROM CRASH_LOG.txt ---
    type "CRASH_LOG.txt"
    echo.
)

:end
pause
popd
