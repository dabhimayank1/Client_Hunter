<#
.SYNOPSIS
    Production Launcher for AI Client Hunter
.DESCRIPTION
    Runs the compiled release API on http://localhost:5000 and serves
    the optimized client production build on http://localhost:5173.
#>

param(
    [int]$ApiPort = 5000,
    [int]$ClientPort = 5173
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "    AI Client Hunter - Production Launcher       " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

$serverDir = "$PSScriptRoot\server\publish"
$clientDir = "$PSScriptRoot\client"

if (-not (Test-Path "$serverDir\AiClientHunter.Api.exe")) {
    Write-Host "[!] Production backend not published. Publishing now..." -ForegroundColor Yellow
    dotnet publish "$PSScriptRoot\server\AiClientHunter.Api\AiClientHunter.Api.csproj" -c Release -o $serverDir
}

if (-not (Test-Path "$clientDir\dist\index.html")) {
    Write-Host "[!] Production client build not found. Building now..." -ForegroundColor Yellow
    Push-Location $clientDir
    npm run build
    Pop-Location
}

Write-Host "[*] Starting Production Backend on http://localhost:$ApiPort..." -ForegroundColor Green
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://localhost:$ApiPort"
$apiProcess = Start-Process -FilePath "$serverDir\AiClientHunter.Api.exe" -PassThru -NoNewWindow

Start-Sleep -Seconds 2

Write-Host "[*] Starting Production Client on http://localhost:$ClientPort..." -ForegroundColor Green
Push-Location $clientDir
$clientProcess = Start-Process -FilePath "npx" -ArgumentList "vite preview --port $ClientPort" -PassThru -NoNewWindow
Pop-Location

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  AI Client Hunter is running in PRODUCTION mode!" -ForegroundColor Green
Write-Host "  API:    http://localhost:$ApiPort/api/health" -ForegroundColor White
Write-Host "  Web UI: http://localhost:$ClientPort" -ForegroundColor White
Write-Host "  Press Ctrl+C or close this window to stop." -ForegroundColor Gray
Write-Host "=================================================" -ForegroundColor Cyan

try {
    Wait-Process -Id $apiProcess.Id, $clientProcess.Id
}
finally {
    if (-not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id -Force }
    if (-not $clientProcess.HasExited) { Stop-Process -Id $clientProcess.Id -Force }
}
