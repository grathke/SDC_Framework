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

# The maintenance counterparts, not the browse ones. This list was copied from
# create-base-b-restore-point.ps1 and kept its siblings' script and guide, so a Base_U restore point
# preserved the browse validation script and the Base_B QBE guide - neither of which a Base_U change
# can invalidate - while capturing nothing that describes the contract being changed. It restored
# cleanly and protected the wrong thing, which is the failure a restore point cannot afford.
$files = @(
    "000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb",
    "scripts\validate-maintenance-regression.ps1",
    "CLAUDE.md",
    "FRAMEWORK_NOTES.md",
    "COMBO_CHECKLIST.md"
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
    - 000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb
    - scripts/validate-maintenance-regression.ps1
    - CLAUDE.md
    - FRAMEWORK_NOTES.md
    - COMBO_CHECKLIST.md
"@ | Set-Content -LiteralPath (Join-Path $restorePoint "RESTORE_POINT.md") -Encoding ascii

Write-Host "BASE_U RESTORE POINT CREATED: $restorePoint" -ForegroundColor Green
