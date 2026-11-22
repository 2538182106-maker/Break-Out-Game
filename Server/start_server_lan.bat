@echo off
title Break Out Leaderboard (LAN)
cd /d "%~dp0"

net session >nul 2>&1
if not %errorlevel%==0 (
    echo [!] Need administrator. Right-click this file and choose "Run as administrator".
    pause
    exit /b 1
)

REM ---- port occupied check ----
netstat -ano ^| findstr ":8000" ^| findstr "LISTENING" >nul
if not errorlevel 1 (
    echo.
    echo [!] Port 8000 is already in use - another leaderboard server is probably running.
    echo     Close the other window, or run:
    echo         netstat -ano ^| findstr :8000
    echo         taskkill /F /PID ^<the pid you see^>
    echo.
    pause
    exit /b 1
)

set LB_SECRET=change-me
set LB_PORT=8000
set LB_PREFIX=http://+:8000/

echo ===============================================
echo  Break Out Leaderboard - LAN mode
echo  Port: %LB_PORT%    Secret: %LB_SECRET%
echo ===============================================
echo.
echo Your IPv4 addresses - put one into the client's StreamingAssets\server.txt :
echo.
ipconfig | findstr /i "IPv4"
echo.
netsh advfirewall firewall add rule name="BreakOut8000" dir=in action=allow protocol=TCP localport=8000 >nul 2>&1
echo Firewall rule ready.
echo Close this window to stop the server.
echo -----------------------------------------------
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0leaderboard_server.ps1"

echo.
echo [server stopped] press any key to close...
pause >nul
