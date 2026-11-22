@echo off
title Break Out Leaderboard (localhost)
cd /d "%~dp0"

set LB_SECRET=change-me
set LB_PORT=8000
set LB_PREFIX=http://localhost:8000/

echo ===============================================
echo  Break Out Leaderboard - localhost only
echo  http://127.0.0.1:8000   Secret: %LB_SECRET%
echo  Close this window to stop the server.
echo ===============================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0leaderboard_server.ps1"

echo.
echo [server stopped] press any key to close...
pause >nul
