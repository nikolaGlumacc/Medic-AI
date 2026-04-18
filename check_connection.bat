@echo off
setlocal EnableDelayedExpansion
title MedicAI - Connection Debugger
color 0B

echo.
echo ============================================================
echo   MedicAI Connection Debugger v1.0
echo   Run this on your Laptop (2nd device) to test connectivity.
echo ============================================================
echo.

set /p TARGET_IP="Enter the IP of your Gaming Machine (e.g. 192.168.1.x): "

echo.
echo [1/3] Pinging %TARGET_IP%...
ping -n 2 %TARGET_IP% | findstr "TTL" >nul
if %errorlevel% EQU 0 (
    echo [OK] Gaming Machine is reachable on the network.
) else (
    echo [FAIL] Cannot reach %TARGET_IP%. 
    echo        Check if both devices are on the same Wi-Fi.
    echo        Check if the Gaming Machine is turned on.
    pause & exit /b 1
)

echo.
echo [2/3] Checking Bot API (Port 5000)...
powershell -Command "try { $c = New-Object System.Net.Sockets.TcpClient; $c.Connect('%TARGET_IP%', 5000); echo '[OK] API Port is OPEN'; $c.Close() } catch { echo '[FAIL] API Port is CLOSED' }" | findstr "OK" >nul
if %errorlevel% NEQ 0 (
    echo [FAIL] Port 5000 is closed or blocked by Firewall.
    echo        Run build.bat as Administrator on the GAMING MACHINE.
) else (
    echo [OK] API Port 5000 is open and ready.
)

echo.
echo [3/3] Checking WebSocket (Port 8766)...
powershell -Command "try { $c = New-Object System.Net.Sockets.TcpClient; $c.Connect('%TARGET_IP%', 8766); echo '[OK] WS Port is OPEN'; $c.Close() } catch { echo '[FAIL] WS Port is CLOSED' }" | findstr "OK" >nul
if %errorlevel% NEQ 0 (
    echo [FAIL] Port 8766 is closed or blocked by Firewall.
) else (
    echo [OK] WebSocket Port 8766 is open and ready.
)

echo.
echo ============================================================
if %errorlevel% EQU 0 (
    echo   RESULT: EVERYTHING LOOKS GOOD! 
    echo   You can now launch the MedicAI Dashboard.
) else (
    echo   RESULT: CONNECTION ISSUES DETECTED.
    echo   Please fix the errors above before launching.
)
echo ============================================================
echo.
pause
