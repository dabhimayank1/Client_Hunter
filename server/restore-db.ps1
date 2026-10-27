param(
    [string]$Server = "localhost",
    [string]$Database = "ai_client_hunter",
    [string]$BackupFilePath
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "   AI Client Hunter - Database Restore   " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

if (-not $BackupFilePath) {
    # Pick the latest .bak file in server/backups
    $backups = Get-ChildItem -Path "$PSScriptRoot\backups\*.bak" | Sort-Object LastWriteTime -Descending
    if ($backups.Count -eq 0) {
        Write-Host "(ERR) No .bak file found in $PSScriptRoot\backups" -ForegroundColor Red
        return
    }
    $BackupFilePath = $backups[0].FullName
}

if (-not (Test-Path $BackupFilePath)) {
    Write-Host "(ERR) Backup file does not exist: $BackupFilePath" -ForegroundColor Red
    return
}

Write-Host "(*) Source Backup File: $BackupFilePath" -ForegroundColor Yellow

$cs = "Server=$Server;Database=master;Trusted_Connection=True;TrustServerCertificate=True;"
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)

try {
    $conn.Open()

    # Get SQL Server default backup directory where SQL service has full read permissions
    $cmdDir = $conn.CreateCommand()
    $cmdDir.CommandText = "EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'BackupDirectory'"
    $reader = $cmdDir.ExecuteReader()
    $sqlBackupDir = "C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup"
    if ($reader.Read()) {
        $sqlBackupDir = $reader[1].ToString()
    }
    $reader.Close()

    $bakFileName = Split-Path $BackupFilePath -Leaf
    $targetSqlBakPath = Join-Path $sqlBackupDir $bakFileName

    # Ensure file is in SQL server accessible directory
    if (-not (Test-Path $targetSqlBakPath)) {
        Write-Host "(*) Copying backup to SQL Server directory: $targetSqlBakPath" -ForegroundColor Gray
        Copy-Item -Path $BackupFilePath -Destination $targetSqlBakPath -Force
    }

    # 1. Verify backup file header
    Write-Host "(*) Verifying backup header..." -ForegroundColor Yellow
    $cmdVerify = $conn.CreateCommand()
    $cmdVerify.CommandText = "RESTORE VERIFYONLY FROM DISK = '$targetSqlBakPath'"
    $cmdVerify.ExecuteNonQuery() | Out-Null
    Write-Host "(OK) Backup file verified valid." -ForegroundColor Green

    # 2. Set database to SINGLE_USER to kill open connections, then restore with REPLACE
    Write-Host "(*) Restoring database [$Database] with REPLACE..." -ForegroundColor Yellow
    $restoreSql = @"
IF EXISTS (SELECT name FROM sys.databases WHERE name = N'$Database')
BEGIN
    ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END

RESTORE DATABASE [$Database] FROM DISK = '$targetSqlBakPath' WITH REPLACE;

ALTER DATABASE [$Database] SET MULTI_USER;
"@
    $cmdRestore = $conn.CreateCommand()
    $cmdRestore.CommandText = $restoreSql
    $cmdRestore.ExecuteNonQuery() | Out-Null

    Write-Host "=========================================" -ForegroundColor Green
    Write-Host "  DATABASE RESTORED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "=========================================" -ForegroundColor Green
}
catch {
    Write-Host "(ERR) Restore failed: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    if ($conn.State -eq [System.Data.ConnectionState]::Open) {
        $conn.Close()
    }
}
