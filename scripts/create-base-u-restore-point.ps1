[CmdletBinding()]
param(
    [string]$Description = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmmss"

# With no description given, take the one Claude wrote when proposing the change. See the matching
# comment in create-base-b-restore-point.ps1 for why an unnamed restore point is close to worthless.
$suggestionFile = Join-Path $PSScriptRoot "next-restore-point.txt"
if ([string]::IsNullOrWhiteSpace($Description) -and (Test-Path -LiteralPath $suggestionFile)) {
    $Description = (Get-Content -LiteralPath $suggestionFile -Raw).Trim()
    Remove-Item -LiteralPath $suggestionFile -Force
}

$safeDescription = ($Description.Trim() -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($safeDescription)) {
    $safeDescription = "UNNAMED"
}
$restorePoint = Join-Path $repoRoot "restore-points\$timestamp-base-u-$safeDescription"

New-Item -ItemType Directory -Path $restorePoint -Force | Out-Null

$files = @(
    "01 FW_Base_U.vb",
    "scripts\validate-browse-regression.ps1",
    "CLAUDE.md",
    "BASE_B_QBE_LAYOUT_GUIDE.md"
)

foreach ($relativePath in $files) {
    $sourcePath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing restore-point source: $relativePath"
    }

    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $restorePoint (Split-Path $relativePath -Leaf))
}

@"
# Base_U Restore Point

- Created: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
- Purpose: $Description
- Files captured:
    - 01 FW_Base_U.vb
    - scripts/validate-browse-regression.ps1
    - CLAUDE.md
    - BASE_B_QBE_LAYOUT_GUIDE.md
"@ | Set-Content -LiteralPath (Join-Path $restorePoint "RESTORE_POINT.md") -Encoding ascii

Write-Host "BASE_U RESTORE POINT CREATED: $restorePoint" -ForegroundColor Green