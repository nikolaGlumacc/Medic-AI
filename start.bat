@echo off
pushd "%~dp0"
echo [SYSTEM] Performing Deep System Integration...

:: ── Check for .NET 10 SDK ──────────────────────────────────────
dotnet --version | findstr "^10." >nul
if %errorlevel% NEQ 0 (
    echo .NET 10 SDK not found. Installing via build.bat logic...
    powershell -NoProfile -ExecutionPolicy unrestricted -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; &([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1'))) -Channel 10.0"
    set "PATH=%PATH%;%USERPROFILE%\.dotnet;%ProgramFiles%\dotnet"
)

:: ── Publish GUI (Release) ──────────────────────────────────────
echo [SYSTEM] Bundling dependencies (Release)...
dotnet publish gui\MedicAIGUI.csproj -c Release -r win-x64 --self-contained true -o publish_gui /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
if %errorlevel% neq 0 (
    echo [ERROR] Publish failed.
    pause & exit /b 1
)

:: ── Launch Server ──────────────────────────────────────────────
echo [SYSTEM] Detecting network configuration...
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /R /C:"IPv4 Address" /C:"IP Address"') do (
    set "LOCAL_IP=%%a"
    set "LOCAL_IP=!LOCAL_IP: =!"
    echo [INFO] LOCAL IP DETECTED: !LOCAL_IP!
    echo [TIPS] Use this IP in the Dashboard if connecting from another laptop.
    goto :IP_FOUND
)
:IP_FOUND
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
