[CmdletBinding()]
param(
    [string]$Description = "",
    [Parameter(Mandatory = $true)]
    [string[]]$Files
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Called as -File, PowerShell hands the whole list over as one string: "a.vb,b.vb". Called from a
# session it arrives as a real array. Splitting here makes both work rather than one failing with
# "missing restore-point source: a.vb,b.vb".
$Files = @($Files | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })

# The two base classes have their own scripts, and those copy their guides and validation script
# alongside the code - a restore point for either that carried only the .vb file would be worse than
# the one that already exists. So this refuses them and points at the right script rather than
# quietly making an inferior copy.
$baseFiles = @{
    "01 FW_Base_B.vb" = "create-base-b-restore-point.ps1"
    "01 FW_Base_U.vb" = "create-base-u-restore-point.ps1"
}

foreach ($relativePath in $Files) {
    $leaf = Split-Path -Leaf $relativePath
    if ($baseFiles.ContainsKey($leaf)) {
        throw "$leaf has its own restore point script. Run scripts\$($baseFiles[$leaf]) instead, which also captures its guide and validation script."
    }
}

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

$restorePoint = Join-Path $repoRoot "restore-points\$timestamp-$safeDescription"
New-Item -ItemType Directory -Path $restorePoint -Force | Out-Null

foreach ($relativePath in $Files) {
    $sourcePath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Missing restore-point source: $relativePath"
    }

    $targetPath = Join-Path $restorePoint (Split-Path -Leaf $relativePath)
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
}

# What the files looked like is only half of it: the commit says what they matched.
$commit = & git -C $repoRoot log -1 --format="%h %s" 2>$null
if (-not $commit) { $commit = "(no commit information available)" }

@(
    "Restore point: $Description",
    "",
    "Created  $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
    "Commit   $commit",
    "",
    "Files captured:"
) + ($Files | ForEach-Object { "  $_" }) | Set-Content -Path (Join-Path $restorePoint "README.txt") -Encoding utf8

Write-Host "RESTORE POINT CREATED: $restorePoint"
