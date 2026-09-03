<#
    test-db-connection-failures.ps1

    Launches the application against a deliberately broken database connection, to exercise the
    startup paths that are otherwise only reachable when something is genuinely wrong.

    Nothing on the server is touched. SDC_DB_CONNECTION takes precedence over every other
    setting, so each case is simulated purely through the environment of the launched process.

    Usage:
        .\scripts\test-db-connection-failures.ps1 -List
        .\scripts\test-db-connection-failures.ps1 -Case ServerDown

    Real credentials are read from run-local.ps1 for the cases that need a reachable server, and
    are never printed.
#>
[CmdletBinding()]
param(
    [ValidateSet('ServerDown', 'Timeout', 'WrongDatabase', 'WrongPassword', 'WrongUser', 'NotConfigured', 'Healthy')]
    [string]$Case,
    [switch]$List
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $repoRoot

$cases = [ordered]@{
    ServerDown    = 'Server name does not resolve. Expect: Database Unavailable, with a network/instance error, Retry and Cancel.'
    Timeout       = 'Unroutable address with a short timeout. Expect: Database Unavailable after roughly the connect timeout.'
    WrongDatabase = 'Real server and credentials, database that does not exist. Expect: Database Unavailable, "Cannot open database".'
    WrongPassword = 'Real server and user, wrong password. Expect: Database Unavailable, "Login failed for user".'
    WrongUser     = 'Real server, user that does not exist. Expect: Database Unavailable, "Login failed for user".'
    NotConfigured = 'Every SDC_DB_* variable cleared. Expect: the Database Configuration dialog, NOT the unavailable message.'
    Healthy       = 'Normal credentials, as a control. Expect: straight to the login screen, no dialog and no delay.'
}

if ($List -or -not $Case) {
    Write-Host "`nCases:`n" -ForegroundColor Cyan
    foreach ($name in $cases.Keys) {
        Write-Host ("  {0,-14} {1}" -f $name, $cases[$name]) -ForegroundColor Gray
    }
    Write-Host "`nRun one with:  .\scripts\test-db-connection-failures.ps1 -Case <name>`n"
    return
}

# Real values, for the cases that need a server that actually answers. Never echoed.
$local = Get-Content (Join-Path $repoRoot 'run-local.ps1') -Raw
function Read-LocalValue([string]$name) {
    $match = [regex]::Match($local, ('\$' + $name + '\s*=\s*"([^"]+)"'))
    if (-not $match.Success) { throw "Could not read `$$name from run-local.ps1" }
    return $match.Groups[1].Value
}

$server   = Read-LocalValue 'Server'
$user     = Read-LocalValue 'User'
$password = Read-LocalValue 'Password'
$database = Read-LocalValue 'Database'

$suffix = 'Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;'

$connection = switch ($Case) {
    'ServerDown'    { "Server=NO-SUCH-SERVER-XYZ;User Id=$user;Password=$password;Initial Catalog=$database;$suffix" }
    'Timeout'       { "Server=10.255.255.1;User Id=$user;Password=$password;Initial Catalog=$database;$suffix" }
    'WrongDatabase' { "Server=$server;User Id=$user;Password=$password;Initial Catalog=NoSuchDatabase_XYZ;$suffix" }
    'WrongPassword' { "Server=$server;User Id=$user;Password=definitely-not-the-password;Initial Catalog=$database;$suffix" }
    'WrongUser'     { "Server=$server;User Id=no_such_login_xyz;Password=$password;Initial Catalog=$database;$suffix" }
    'Healthy'       { "Server=$server;User Id=$user;Password=$password;Initial Catalog=$database;$suffix" }
    'NotConfigured' { $null }
}

# Clear every variable first, so a leftover value cannot mask the case being tested.
foreach ($name in 'SDC_DB_CONNECTION', 'SDC_DB_SERVER', 'SDC_DB_USER',
                  'SDC_DB_PASSWORD', 'SDC_DB_NAME', 'SDC_DB_ENCRYPT',
                  'SDC_DB_TRUST_SERVER_CERT') {
    Set-Item -Path "env:$name" -Value $null -ErrorAction SilentlyContinue
}

if ($connection) { $env:SDC_DB_CONNECTION = $connection }

# Saved credentials would satisfy the NotConfigured case and hide what it is meant to show.
$savedConfig = Join-Path $env:LOCALAPPDATA 'WXFramework\dbconfig.dat'
if ($Case -eq 'NotConfigured' -and (Test-Path $savedConfig)) {
    Write-Host "NOTE: saved credentials exist at $savedConfig" -ForegroundColor Yellow
    Write-Host "      The application will use them and go straight to login. Delete the file to see the dialog.`n" -ForegroundColor Yellow
}

Write-Host "`nCase: $Case" -ForegroundColor Cyan
Write-Host "$($cases[$Case])`n" -ForegroundColor Gray

$exe = Join-Path $repoRoot 'bin\Debug\net10.0-windows\SDC.Framework.exe'
if (-not (Test-Path $exe)) { throw "Build first - not found: $exe" }

& $exe
