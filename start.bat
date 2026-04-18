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

:: ── Launch GUI ─────────────────────────────────────────────────
cd /d "%~dp0publish_gui"
echo [SYSTEM] Launching Command Deck...
start MedicAIGUI.exe
popd
exit /b
