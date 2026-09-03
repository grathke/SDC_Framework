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

if ($Preview) {
    & dotnet run --project ".\SDC.Framework.vbproj" -- --preview
}
else {
    & dotnet run --project ".\SDC.Framework.vbproj"
}
