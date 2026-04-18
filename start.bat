@echo off
pushd "%~dp0"
echo [SYSTEM] Performing Deep System Integration...

:: ── Check for .NET SDK ─────────────────────────────────────────
dotnet --version >nul 2>&1
if %errorlevel% NEQ 0 (
    echo .NET SDK not found. Downloading...
    curl -L -o "%TEMP%\dotnet_sdk.exe" "https://download.microsoft.com/download/dotnet/8.0/dotnet-sdk-8.0.410-win-x64.exe"
    "%TEMP%\dotnet_sdk.exe" /quiet /norestart
    :: Refresh PATH
    for /f "tokens=2*" %%A in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "PATH=%%B"
)

:: ── Publish GUI (Release) ──────────────────────────────────────
echo [SYSTEM] Bundling dependencies (Release)...
dotnet publish gui\MedicAIGUI.csproj -c Release -r win-x64 --self-contained true -o publish_gui /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
if %errorlevel% neq 0 (
    echo [ERROR] Publish failed.
    pause & exit /b 1
)

:: ── Launch Server ──────────────────────────────────────────────
echo [SYSTEM] Starting MedicBot Brain...
start "MedicAI Bot Server" cmd /c "start_server.bat"

:: ── Wait for port 8766 (WebSocket) ─────────────────────────────
echo Waiting for bot server to be ready...
set /a "attempts=0"
:WAIT_LOOP
timeout /t 2 /nobreak >nul
netstat -ano | findstr /R /C:":8766 .*LISTENING" >nul
if %errorlevel% EQU 0 goto :READY
set /a "attempts+=1"
if %attempts% LSS 60 goto :WAIT_LOOP
echo WARNING: Bot server did not start on port 8766 within 120s.
:READY

:: ── Launch GUI ─────────────────────────────────────────────────
cd /d "%~dp0publish_gui"
echo [SYSTEM] Launching Command Deck...
start MedicAIGUI.exe
popd
exit /b
