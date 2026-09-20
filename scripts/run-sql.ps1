# Runs a .sql file against the configured database.
#
# The repository had no way to apply a migration. Every one was run by hand in SSMS, which is
# fine until somebody has to remember which of the 140-odd files in sql\ have been applied.
#
# Credentials come from run-local.ps1, the same gitignored file the application uses, so no
# password is ever typed on a command line, echoed into a terminal history, or written here.
# Nothing is compiled in and nothing travels with the repository.
#
#   .\scripts\run-sql.ps1 -File sql\143_employee_alert_preferences.sql
#   .\scripts\run-sql.ps1 -Query "SELECT COUNT(*) FROM dbo.FW_Employees"
#   .\scripts\run-sql.ps1 -File sql\143_employee_alert_preferences.sql -WhatIf
#
# -WhatIf prints what would run and connects no further than a login check.

param(
	[string]$File,
	[string]$Query,
	[switch]$WhatIf
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\db-config.ps1"

if (-not $File -and -not $Query) {
	Write-Error "Give either -File <path.sql> or -Query <statement>."
	exit 2
}

if ($File -and -not (Test-Path $File)) {
	Write-Error "SQL file not found: $File"
	exit 2
}

$config   = Get-SdcDbConfig
$server   = $config.Server
$user     = $config.User
$password = $config.Password
$database = $config.Database

# The password is deliberately absent. Everything else is worth seeing before a migration runs
# against the wrong database, which is the mistake this line exists to prevent.
Write-Host "Server:   $server"
Write-Host "Database: $database"
Write-Host "User:     $user"

if ($File) {
	Write-Host "File:     $File"
} else {
	Write-Host "Query:    $Query"
}

if ($WhatIf) {
	Write-Host ""
	Write-Host "-WhatIf: testing the login only, running nothing."
	sqlcmd -S $server -U $user -P $password -d $database -C -Q "SELECT 1 AS LoginOk;"
	exit $LASTEXITCODE
}

Write-Host ""

if ($File) {
	sqlcmd -S $server -U $user -P $password -d $database -C -b -I -i $File
} else {
	sqlcmd -S $server -U $user -P $password -d $database -C -b -I -W -Q $Query
}

$code = $LASTEXITCODE

if ($code -ne 0) {
	Write-Host ""
	Write-Error "sqlcmd exited with $code. Nothing past the failing statement was run."
}

exit $code
