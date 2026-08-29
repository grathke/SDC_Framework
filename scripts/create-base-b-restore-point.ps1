[CmdletBinding()]
param(
    [string]$Description = "Pre Base_B change"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
$safeDescription = ($Description.Trim() -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($safeDescription)) {
    $safeDescription = "pre-base-b-change"
}
$restorePoint = Join-Path $repoRoot "restore-points\$timestamp-base-b-$safeDescription"

New-Item -ItemType Directory -Path $restorePoint -Force | Out-Null

$files = @(
    "01 FW_Base_B.vb",
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
# Base_B Restore Point

- Created: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
- Purpose: $Description
- Files captured:
    - 01 FW_Base_B.vb
    - scripts/validate-browse-regression.ps1
    - CLAUDE.md
    - BASE_B_QBE_LAYOUT_GUIDE.md
"@ | Set-Content -LiteralPath (Join-Path $restorePoint "RESTORE_POINT.md") -Encoding ascii

Write-Host "BASE_B RESTORE POINT CREATED: $restorePoint" -ForegroundColor Green