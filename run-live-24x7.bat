@echo off
title AI Client Hunter - 24/7 Engine
color 0A
cls
echo ================================================================
echo    🏹 AI CLIENT HUNTER - 24/7 LIVE PRODUCTION RUNNER
echo ================================================================
echo.
echo [1/3] Building latest frontend bundle...
cd client
call npm run build
cd ..

echo.
echo [2/3] Launching .NET 9 Backend API & Background Worker on Port 5000...
start "AI Client Hunter - Backend API" cmd /k "dotnet run --project server\AiClientHunter.Api\AiClientHunter.Api.csproj --urls http://localhost:5000"

echo.
echo [3/3] Launching Production Web CRM UI on Port 5173...
start "AI Client Hunter - Frontend UI" cmd /k "cd client && npm run preview -- --port 5173 --host"

echo.
echo ================================================================
echo    SUCCESS! SYSTEM IS RUNNING LIVE 24/7
echo ================================================================
echo    - Web CRM Dashboard: http://localhost:5173/
echo    - Scraper & Jobs:    http://localhost:5173/search
echo    - Backend Health:    http://localhost:5000/api/health
echo    - Database:          SQL Server (ai_client_hunter)
echo ================================================================
echo.
echo IMPORTANT TIPS FOR 24/7 UNINTERRUPTED RUNNING:
echo 1. Keep your PC plugged into power and connected to Wi-Fi/LAN.
echo 2. Go to Windows Settings -> Power & Sleep -> Set 'Sleep' to 'Never'.
echo 3. Keep the 2 opened process windows minimized in background.
echo.
pause
