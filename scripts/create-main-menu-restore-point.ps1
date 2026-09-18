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
$restorePoint = Join-Path $repoRoot "restore-points\$timestamp-main-menu-$safeDescription"

New-Item -ItemType Directory -Path $restorePoint -Force | Out-Null

# Both halves of the menu, because the boundary between them is exactly what a layout change moves.
# MainMenu.vb holds the shell - the ribbon, the region shells and the content grid - and
# MenuFormInitializer.vb decides which tiles that ribbon has and which occupant each region gets.
# Capturing one without the other restores a shell that no longer matches its caller, which is the
# failure a restore point cannot afford.
#
# CLAUDE.md comes too: the Protected Areas rule that governs edits here is in it, and a restore
# point that loses the rule protecting the file is half a restore point.
$files = @(
    "000_FRAMEWORK\010_MAIN MENU\MainMenu.vb",
    "100_CTY\MenuFormInitializer.vb",
    "CLAUDE.md"
)

foreach ($relativePath in $files) {
    $sourcePath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing restore-point source: $relativePath"
    }

    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $restorePoint (Split-Path $relativePath -Leaf))
}

@"
# Main Menu Restore Point

- Created: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
- Purpose: $Description
- Files captured:
    - 000_FRAMEWORK\010_MAIN MENU\MainMenu.vb
    - 100_CTY\MenuFormInitializer.vb
    - CLAUDE.md

Restoring: copy MainMenu.vb and MenuFormInitializer.vb back to the paths above. They are a pair -
the shell and its caller - and restoring one alone leaves the menu calling a shell that has moved.
"@ | Set-Content -LiteralPath (Join-Path $restorePoint "RESTORE_POINT.md") -Encoding ascii

Write-Host "MAIN MENU RESTORE POINT CREATED: $restorePoint" -ForegroundColor Green
