@echo off
title AI Client Hunter - Launcher
echo ===================================================
echo           AI CLIENT HUNTER - STARTING
echo ===================================================
echo.
echo [*] Starting Backend API Server on http://localhost:5000 ...
start "AI Client Hunter - Backend API" cmd /k "cd /d %~dp0server\AiClientHunter.Api && dotnet run --urls http://localhost:5000"

timeout /t 3 /nobreak >nul

echo [*] Starting Frontend UI on http://localhost:5173 ...
start "AI Client Hunter - Frontend UI" cmd /k "cd /d %~dp0client && npm run dev -- --host"

timeout /t 2 /nobreak >nul

echo [*] Opening Web Browser...
start http://localhost:5173

echo.
echo ===================================================
echo   System running successfully!
echo   Frontend: http://localhost:5173
echo   Backend:  http://localhost:5000
echo ===================================================
