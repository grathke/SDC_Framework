[CmdletBinding()]
param(
    [string]$Description = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmmss"

# With no description given, take the one Claude wrote when proposing the change. A restore point
# that does not say what it protects is close to worthless: running these scripts bare produced 46
# folders called "pre-base-b-change" in two weeks, and nothing could tell you which was the last
# known-good one. UNNAMED is deliberately loud, so an unnamed point looks wrong rather than normal.
$suggestionFile = Join-Path $PSScriptRoot "next-restore-point.txt"
if ([string]::IsNullOrWhiteSpace($Description) -and (Test-Path -LiteralPath $suggestionFile)) {
    $Description = (Get-Content -LiteralPath $suggestionFile -Raw).Trim()
    # Used once. A stale suggestion must not attach itself to the next, unrelated restore point.
    Remove-Item -LiteralPath $suggestionFile -Force
}

$safeDescription = ($Description.Trim() -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($safeDescription)) {
    $safeDescription = "UNNAMED"
}
$restorePoint = Join-Path $repoRoot "restore-points\$timestamp-base-b-$safeDescription"

New-Item -ItemType Directory -Path $restorePoint -Force | Out-Null

$files = @(
    "000_FRAMEWORK\000_BASECLASSES\Base_B.vb",
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
    - 000_FRAMEWORK\000_BASECLASSES\Base_B.vb
    - scripts/validate-browse-regression.ps1
    - CLAUDE.md
    - BASE_B_QBE_LAYOUT_GUIDE.md
"@ | Set-Content -LiteralPath (Join-Path $restorePoint "RESTORE_POINT.md") -Encoding ascii

Write-Host "BASE_B RESTORE POINT CREATED: $restorePoint" -ForegroundColor Green
