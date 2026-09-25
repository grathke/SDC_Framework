[CmdletBinding()]
param(
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
Set-Location $repoRoot

function Write-Step {
    param([string]$Message)
    Write-Host "`n== $Message ==" -ForegroundColor Cyan
}

function Assert-Pattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Description
    )

    if (-not (Test-Path $Path)) {
        throw "Missing expected file: $Path"
    }

    if (-not (Select-String -Path $Path -Pattern $Pattern -SimpleMatch -Quiet)) {
        throw "Missing expected pattern for $Description in ${Path}: $Pattern"
    }

    Write-Host "PASS: $Description" -ForegroundColor Green
}

function Assert-Absent {
    param(
        [string[]]$Path,
        [string]$Pattern,
        [string]$Description,
        [string[]]$Except = @()
    )

    $hits = Select-String -Path $Path -Pattern $Pattern -SimpleMatch |
        Where-Object { $Except -notcontains (Split-Path $_.Path -Leaf) }

    if ($hits) {
        $where = ($hits | ForEach-Object { "$(Split-Path $_.Path -Leaf):$($_.LineNumber)" }) -join ", "
        throw "$Description  Found in: $where"
    }

    Write-Host "PASS: $Description" -ForegroundColor Green
}

# Every _U page, and the ones that own shared behavior.
# Recursive since 2026-09-04: the pages moved into 000_FRAMEWORK\<band>\ folders, and a
# non-recursive scan of the repository root silently found none - which surfaced as
# "Cannot bind argument to parameter 'Path'" rather than as zero pages checked.
$maintenancePages = @(Get-ChildItem -Path $repoRoot -Filter "*_U.vb" -File -Recurse |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|restore-points|project-backup|tests)\\' } |
    Where-Object { $_.Name -ne "FW_Base_U.vb" } |
    ForEach-Object { $_.FullName })

Write-Step "Maintenance framework static validation"
Write-Host "Pages in scope: $($maintenancePages.Count)" -ForegroundColor Gray

if (-not $SkipBuild) {
    Write-Step "Build"
    dotnet build .\SDC.Framework.vbproj -p:UseAppHost=false -p:OutputPath=bin\Debug\net10.0-windows-hotfix\
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
}

Write-Step "Field lifecycle order in Base_U"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "AdoptRequiredBorderPanels()" -Description "Base_U adopts the metadata required-border panels"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "CollapseHiddenFieldRows()" -Description "Hidden-field rows are collapsed"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "indicators.SuppressTouch = True" -Description "The page's own initial focus does not count as visiting a field"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Widgets\FieldIndicators.vb" -Pattern "Public Function ShouldShowWarning" -Description "Red required border uses the shared visited-and-empty rule"

# CollapseHiddenFieldRows and the dirty baseline both depend on Control.Visible, which lies until
# the form is shown. Guard the ordering rather than the mere presence of the calls.
$baseU = Get-Content ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Raw
$shownHandler = [regex]::Match($baseU, "(?s)Private Sub FW_Base_U_Shown.*?End Sub").Value
if (-not $shownHandler) {
    throw "Could not locate FW_Base_U_Shown; the lifecycle order can no longer be verified."
}
foreach ($call in @("CollapseHiddenFieldRows()", "ApplySavedTabOrder()", "SetInitialFieldFocus()", "ResetPendingRecordBaseline()")) {
    if ($shownHandler -notmatch [regex]::Escape($call)) {
        throw "FW_Base_U_Shown must call $call - see FRAMEWORK_NOTES.md 'Field lifecycle order'."
    }
}
if ($shownHandler.IndexOf("ResetPendingRecordBaseline()") -lt $shownHandler.IndexOf("CollapseHiddenFieldRows()")) {
    throw "The pending-changes baseline must be captured after the layout settles, not before."
}
Write-Host "PASS: Shown handler runs collapse, tab order, focus and baseline in that order" -ForegroundColor Green

Write-Step "Hidden fields keep their value"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Security\FieldPermissions.vb" -Pattern "FreezeBoundValue(ctrl)" -Description "HideField detaches bindings so a hidden field is not blanked by validation"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "If FlagOrDefault(row, ""Make_Invisible"", False) Then" -Description "Make_Invisible is applied before required styling"

Write-Step "Field permissions are derived from the page, not from a stored enumeration"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "CollectBoundControls(form, form, normalizedTable, columns, derived)" -Description "GetControlUpdates derives the control mapping from the live form"
Assert-Absent -Path @(".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb") -Pattern "FROM dbo.vw_FW_ControlUpdates_U" -Description "Nothing reads the enumeration view"
Assert-Absent -Path @(".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb") -Pattern "SELECT ControlName, FileLink FROM dbo.FW_Enumerations_U" -Description "The control field map is derived, not read from the enumeration"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "ReportUnmappedFieldControls()" -Description "A control mapping to no column is reported rather than failing silently"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "okButton.Enabled = False" -Description "An unmapped field stops the page saving"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "An unbound field declaration needs a reason." -Description "An intentional unbound control must state why"

Write-Step "Single owner for shared field tests"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "Public Shared Function IsEmptyComboSelection" -Description "One owner for the empty-combo test"
Assert-Absent -Path @(".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb") -Pattern "Integer.TryParse(combo.SelectedValue.ToString(), selectedValue)" -Description "Base_U does not re-derive the empty-combo test"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Controllers\SmartyAddressLookupController.vb" -Pattern "Public Shared Function IsSessionLookupEnabled" -Description "One owner for the Smarty session test"
Assert-Absent -Path $maintenancePages -Pattern "SessionState.Current.Value.Smarty_UseEmbeddedKey" -Description "No page re-derives the Smarty session test"

Write-Step "No page-local copies of shared behavior"
Assert-Absent -Path $maintenancePages -Pattern "LocalRequiredBorder_" -Description "No page builds its own required border; AddField owns it"
Assert-Absent -Path $maintenancePages -Pattern "You have unsaved changes" -Description "No page shows its own unsaved-changes prompt"
Assert-Absent -Path $maintenancePages -Pattern "THE FOLLOWING ARE REQUIRED" -Description "No page shows its own required-field message"
# Pages are expected to construct ZipCoderController; what they must not do is hand-build the
# button, which would duplicate its caption, placement and Smarty visibility rule.
Assert-Absent -Path $maintenancePages -Pattern '"Zip Coder"' -Description "No page hard-codes the Zip Coder caption; ZipCoderController owns it"

Write-Step "Focus and required borders have one owner"
# FieldIndicators owns the green focus ring and the red required ring since 2026-09-25. Until then
# FW_Base_U held its own copy, private to it, and a window that was not a maintenance page could
# not have the borders at all. These fail if a second copy is written: the colour anywhere else,
# or a new WireFocusIndicators. Roles_U keeps an older, different one (a background tint, not a
# border) until it is next worked on.
$allSource = @(Get-ChildItem -Path $repoRoot -Filter "*.vb" -File -Recurse |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|restore-points|project-backup|tests)\\' } |
    ForEach-Object { $_.FullName })
Assert-Absent -Path $allSource -Pattern "FromArgb(55, 180, 105)" -Description "The focus colour is written only in FieldIndicators." -Except @("FieldIndicators.vb")
Assert-Absent -Path $allSource -Pattern "Sub WireFocusIndicators" -Description "No page writes its own WireFocusIndicators; FieldIndicators.Wire is the owner." -Except @("Roles_U.vb")
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "New FieldIndicators(" -Description "Maintenance pages take their borders from FieldIndicators"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessageComposeForm.vb" -Pattern "New FieldIndicators(" -Description "Compose takes its borders from FieldIndicators"

Write-Step "Save and concurrency contract"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "CaptureOriginalRowVersion" -Description "RowVersion is captured for concurrency"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "ShowConcurrencyUnavailable" -Description "Missing concurrency protection is surfaced"

# Documented exception: Page Generation is a one-off page whose questions are numbered and laid out
# in the order they must be answered, so it opts out of the shared tab order manager. The default
# stays True in Base_U for every other _U page.
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "Protected Overridable Function SupportsTabOrderManager" -Description "Tab order manager is opt-out, defaulting to on"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern "Protected Overrides Function SupportsTabOrderManager" -Description "Page Generation declares its tab order manager exception"
foreach ($page in $maintenancePages) {
    if (Select-String -Path $page -Pattern "Overrides Function SaveRecord" -SimpleMatch -Quiet) {
        if (-not (Select-String -Path $page -Pattern "CaptureOriginalRowVersion" -SimpleMatch -Quiet) -and
            -not (Select-String -Path $page -Pattern "originalRowVersion" -SimpleMatch -Quiet) -and
            -not (Select-String -Path $page -Pattern "RowVersion" -SimpleMatch -Quiet)) {
            throw "$page saves a record but never touches RowVersion; concurrency protection is missing."
        }
    }
}
Write-Host "PASS: Every saving page carries a concurrency token" -ForegroundColor Green

# A form a page opens while saving is time somebody spends choosing, and the save clock must be
# held for it or the choosing is reported as a slow save. FW_HD_Issues_U's status chooser was
# fault #11 on 2026-09-25: 6.6 of a 7.3 second "save" was a person picking a status. Pages go
# through ShowDialogDuringSave, which holds the clock; a bare ShowDialog in these three methods
# fails here. Failure messages (MessageBox) are not asked about - they end a save already failed.
$saveMethodPattern = '(?s)Overrides Function (TryBuildRecord|SaveRecord|GetAdditionalValidationMessageLines)\(.*?End Function'
$saveDialogHits = @()
foreach ($page in $maintenancePages) {
    $code = Get-Content -LiteralPath $page -Raw
    foreach ($method in [regex]::Matches($code, $saveMethodPattern)) {
        if ($method.Value -match '\.ShowDialog\(') {
            $saveDialogHits += "$(Split-Path $page -Leaf) ($($method.Groups[1].Value))"
        }
    }
}
if ($saveDialogHits.Count -gt 0) {
    throw "A dialog is opened during a save without holding the clock - use ShowDialogDuringSave: $($saveDialogHits -join '; ')"
}
Assert-Pattern -Path ".\000_FRAMEWORK\080_HELP DESK\FW_HD_Issues_U.vb" -Pattern "ShowDialogDuringSave(statusChooser)" -Description "The Help Desk status chooser holds the save clock"
Write-Host "PASS: No page opens a dialog during a save with the clock running ($($maintenancePages.Count) pages)" -ForegroundColor Green

Write-Step "Page conventions"

# Documented exception: Roles_U is a permission administration console over three grids with
# immediate per-row writes, not a single-record editor, so Base_U's save/RowVersion/cancel
# contract does not apply. See FRAMEWORK_NOTES.md, "Documented exceptions".
$baseUExceptions = @("Roles_U.vb")

foreach ($page in $maintenancePages) {
    $name = Split-Path $page -Leaf
    $inherits = Select-String -Path $page -Pattern "Inherits FW_Base_U" -SimpleMatch -Quiet

    if ($baseUExceptions -contains $name) {
        if ($inherits) {
            throw "$name is recorded as a Base_U exception but now inherits FW_Base_U. Remove it from the exception list here and from FRAMEWORK_NOTES.md."
        }
        Write-Host "PASS: $name is a documented Base_U exception" -ForegroundColor Green
        continue
    }

    if (-not $inherits) {
        throw "$name does not inherit FW_Base_U. Either inherit it, or document the exception in FRAMEWORK_NOTES.md and add it to `$baseUExceptions in this script."
    }
}
Write-Host "PASS: Every other _U page inherits FW_Base_U" -ForegroundColor Green

Write-Step "Manual verification checklist"
Write-Host "Open the page you changed and confirm:" -ForegroundColor Yellow
Write-Host "  1) Create: nothing is red until a required field is visited or edited." -ForegroundColor Yellow
Write-Host "  2) Clear a required field: red appears immediately and survives tabbing away." -ForegroundColor Yellow
Write-Host "  3) Save with blanks: the message lists fields in tab order and focus lands on the first." -ForegroundColor Yellow
Write-Host "  4) Hide a field via FW_RoleFields: its row closes up, and saving preserves the hidden value." -ForegroundColor Yellow
Write-Host "  5) Change a value and change it back, then Cancel: no unsaved-changes prompt." -ForegroundColor Yellow
Write-Host "  6) Save conflict: the page stays open and demands an explicit choice." -ForegroundColor Yellow

Write-Host "`nValidation completed successfully." -ForegroundColor Green
