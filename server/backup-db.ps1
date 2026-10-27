param(
    [string]$Server = "localhost",
    [string]$Database = "ai_client_hunter",
    [string]$OutputFolder = "$PSScriptRoot\backups"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   AI Client Hunter - Database Backup    " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Ensure local backup directory exists
if (-not (Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
    Write-Host "(+) Created local backup folder: $OutputFolder" -ForegroundColor Gray
}

# 2. Get SQL Server default backup directory
$cs = "Server=$Server;Database=master;Trusted_Connection=True;TrustServerCertificate=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)

try {
    $conn.Open()

    $cmdDir = $conn.CreateCommand()
    $cmdDir.CommandText = "EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'BackupDirectory'"
    $reader = $cmdDir.ExecuteReader()
    $sqlBackupDir = "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup"
    if ($reader.Read()) {
        $sqlBackupDir = $reader[1].ToString()
    }
    $reader.Close()

    # 3. Generate timestamped file name
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $bakFileName = "${Database}_backup_${timestamp}.bak"
    $sqlBakPath = Join-Path $sqlBackupDir $bakFileName
    $localBakPath = Join-Path $OutputFolder $bakFileName

    Write-Host "(*) Target SQL Backup Path: $sqlBakPath" -ForegroundColor Gray
    Write-Host "(*) Executing BACKUP DATABASE [$Database]..." -ForegroundColor Yellow

    # 4. Perform compressed full backup
    $cmdBackup = $conn.CreateCommand()
    $cmdBackup.CommandText = "BACKUP DATABASE [$Database] TO DISK = '$sqlBakPath' WITH FORMAT, INIT, COMPRESSION, NAME = '$Database full backup ($timestamp)'"
    $cmdBackup.ExecuteNonQuery() | Out-Null
    Write-Host "(OK) SQL Server backup completed successfully." -ForegroundColor Green

    # 5. Verify backup integrity
    Write-Host "(*) Verifying backup integrity with RESTORE VERIFYONLY..." -ForegroundColor Yellow
    $cmdVerify = $conn.CreateCommand()
    $cmdVerify.CommandText = "RESTORE VERIFYONLY FROM DISK = '$sqlBakPath'"
    $cmdVerify.ExecuteNonQuery() | Out-Null
    Write-Host "(OK) Backup verified: The backup set is valid." -ForegroundColor Green

    # 6. Copy to local backups folder
    Copy-Item -Path $sqlBakPath -Destination $localBakPath -Force
    $fileInfo = Get-Item $localBakPath

    Write-Host "=========================================" -ForegroundColor Green
    Write-Host "  BACKUP COMPLETED AND VERIFIED!" -ForegroundColor Green
    Write-Host "  File: $($fileInfo.Name)" -ForegroundColor White
    Write-Host "  Path: $($fileInfo.FullName)" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($fileInfo.Length / 1KB, 2)) KB" -ForegroundColor White
    Write-Host "=========================================" -ForegroundColor Green
}
catch {
    Write-Host "(ERR) Backup failed: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    if ($conn.State -eq [System.Data.ConnectionState]::Open) {
        $conn.Close()
    }
}
