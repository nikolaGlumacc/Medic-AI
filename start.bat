@echo off
pushd "%~dp0"
echo [SYSTEM] Performing Deep System Integration...
echo [SYSTEM] Bundling all dependencies into self-contained folder...
echo.

:: ── Check for .NET SDK ─────────────────────────────────────────
where dotnet >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] .NET SDK not found. 
    echo Please install .NET 8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:: ── Publish GUI ───────────────────────────────────────────────
echo [SYSTEM] Bundling all dependencies...
set RETRY_COUNT=0
:publish_loop
dotnet publish gui\MedicAIGUI.csproj -c Debug -r win-x64 --self-contained true -o publish_gui
if %errorlevel% neq 0 (
    set /a RETRY_COUNT+=1
    if !RETRY_COUNT! lss 3 (
        echo [WARN] Publish failed. Retrying (!RETRY_COUNT!/3)...
        timeout /t 2 >nul
        goto publish_loop
    )
    echo [ERROR] Build/Publish failed after 3 attempts.
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
