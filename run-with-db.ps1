param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [Parameter(Mandatory = $true)]
    [string]$User,

    [Parameter(Mandatory = $true)]
    [string]$Password,

    [Parameter(Mandatory = $false)]
    [string]$Database = 'WX_Framework',

    [Parameter(Mandatory = $false)]
    [string]$Encrypt = 'True',

    [Parameter(Mandatory = $false)]
    [string]$TrustServerCertificate = 'True'

    ,
    [Parameter(Mandatory = $false)]
    [switch]$Preview

    ,
    # Skip Thinfinity VirtualUI. Starting it costs a few seconds and a second server process, and
    # an ordinary development run has no browser waiting at the other end.
    [Parameter(Mandatory = $false)]
    [switch]$NoTF
)

$env:SDC_DB_SERVER = $Server
$env:SDC_DB_USER = $User
$env:SDC_DB_PASSWORD = $Password
$env:SDC_DB_NAME = $Database
$env:SDC_DB_ENCRYPT = $Encrypt
$env:SDC_DB_TRUST_SERVER_CERT = $TrustServerCertificate

Write-Host "Running with database settings:"
Write-Host "  Server: $($env:SDC_DB_SERVER)"
Write-Host "  User: $($env:SDC_DB_USER)"
Write-Host "  Database: $($env:SDC_DB_NAME)"
Write-Host "  Encrypt: $($env:SDC_DB_ENCRYPT)"
Write-Host "  TrustServerCertificate: $($env:SDC_DB_TRUST_SERVER_CERT)"

$appArgs = @()
if ($Preview) { $appArgs += '--preview' }
# --tf-dev, not merely the absence of --no-tf: dev mode is for a run started from here, where
# nobody is waiting in a browser yet. An application launched by VirtualUI Server must not set it,
# and that process gets neither switch.
if ($NoTF)    { $appArgs += '--no-tf' } else { $appArgs += '--tf-dev' }

if ($appArgs.Count -gt 0) {
    Write-Host "  App arguments: $($appArgs -join ' ')"
    & dotnet run --project ".\SDC.Framework.vbproj" -- @appArgs
}
else {
    & dotnet run --project ".\SDC.Framework.vbproj"
}
