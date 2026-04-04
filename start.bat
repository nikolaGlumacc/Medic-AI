@echo off
pushd "%~dp0"

REM Start bot server using virtual environment Python
echo Starting MedicAI Bot Server...
start "MedicAI Bot Server" cmd /c "c:\medicai_venv\Scripts\python.exe bot\bot_server.py"

REM Wait for server to start
timeout /t 2 /nobreak

echo Launching MedicAI Dashboard...
start "" "%~dp0gui\bin\Debug\net10.0-windows\MedicAIGUI.exe"

popd
exit /b
