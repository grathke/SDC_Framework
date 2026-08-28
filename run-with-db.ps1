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

$env:HELLOWORLD_DB_SERVER = $Server
$env:HELLOWORLD_DB_USER = $User
$env:HELLOWORLD_DB_PASSWORD = $Password
$env:HELLOWORLD_DB_NAME = $Database
$env:HELLOWORLD_DB_ENCRYPT = $Encrypt
$env:HELLOWORLD_DB_TRUST_SERVER_CERT = $TrustServerCertificate

Write-Host "Running with database settings:"
Write-Host "  Server: $($env:HELLOWORLD_DB_SERVER)"
Write-Host "  User: $($env:HELLOWORLD_DB_USER)"
Write-Host "  Database: $($env:HELLOWORLD_DB_NAME)"
Write-Host "  Encrypt: $($env:HELLOWORLD_DB_ENCRYPT)"
Write-Host "  TrustServerCertificate: $($env:HELLOWORLD_DB_TRUST_SERVER_CERT)"

if ($Preview) {
    & dotnet run --project ".\HelloWorld.vbproj" -- --preview
}
else {
    & dotnet run --project ".\HelloWorld.vbproj"
}
