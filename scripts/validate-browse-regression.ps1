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

    $match = Select-String -Path $Path -Pattern $Pattern -SimpleMatch -Quiet
    if (-not $match) {
        throw "Missing expected pattern for $Description in ${Path}: $Pattern"
    }

    Write-Host "PASS: $Description" -ForegroundColor Green
}

function Assert-NotPattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Description
    )

    if (-not (Test-Path $Path)) {
        throw "Missing expected file: $Path"
    }

    if (Select-String -Path $Path -Pattern $Pattern -SimpleMatch -Quiet) {
        throw "Unexpected pattern for $Description in ${Path}: $Pattern"
    }

    Write-Host "PASS: $Description" -ForegroundColor Green
}

# Assert-Pattern matches one line at a time, so it cannot say what a function *returns* - only that
# the function exists. A default that has been flipped from False to True is exactly the case that
# needs the body, hence a raw whole-file comparison.
function Assert-Block {
    param(
        [string]$Path,
        [string]$Block,
        [string]$Description
    )

    if (-not (Test-Path $Path)) {
        throw "Missing expected file: $Path"
    }

    $content = (Get-Content -LiteralPath $Path -Raw) -replace "`r`n", "`n"
    if (-not $content.Contains(($Block.TrimEnd() -replace "`r`n", "`n"))) {
        throw "Missing expected block for $Description in ${Path}"
    }

    Write-Host "PASS: $Description" -ForegroundColor Green
}

Write-Step "Browse framework static validation"

if (-not $SkipBuild) {
    Write-Step "Build"
    dotnet build .\SDC.Framework.vbproj -p:UseAppHost=false -p:OutputPath=bin\Debug\net10.0-windows-hotfix\
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
}

Write-Step "Shared deleted-view guard wiring"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "DeletedViewGuard.ResultHasDeletedFlagColumn(browseGrid)" -Description "Base browse uses shared result-column guard"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "DeletedViewGuard.TableSupportsDeletedView(tableName)" -Description "Base browse uses shared table deleted support guard"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "If col Is Nothing OrElse Not col.Visible Then" -Description "QBE derives fields only from visible browse columns"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "If IsPkAliasColumn(col) OrElse IsSoftDeleteColumnName(fieldName) Then" -Description "Grid-derived QBE excludes the internal PK alias"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern 'String.Equals(dc.ColumnName, "PK", StringComparison.OrdinalIgnoreCase)' -Description "Start Empty QBE excludes the internal PK alias"
if (Select-String -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern 'AssignedManagerID' -SimpleMatch -Quiet) {
    throw "Base_B must not seed page-specific QBE fields; QBE fields must come from the active page grid. AssignedManagerID was the original offender, from the since-removed FW_Entity."
}
Write-Host "PASS: Base browse does not seed page-specific QBE fields" -ForegroundColor Green
# Was Users_AppAdmin_B until 2026-09-08, when the FW_Users pages were removed. The check follows the
# contract rather than the page: a standard browse page inherits the shared guard.
Assert-Pattern -Path ".\000_FRAMEWORK\050_REGISTRATION\FW_Registration_B.vb" -Pattern "Inherits FW_Base_B" -Description "A standard browse page inherits the shared deleted-view guard"
Assert-Pattern -Path ".\000_FRAMEWORK\060_ROLES\Roles_B.vb" -Pattern "DeletedViewGuard.TableSupportsDeletedView(ResolveCurrentRoleFieldTableName()) AndAlso DeletedViewGuard.ResultHasDeletedFlagColumn(rolesGrid)" -Description "Roles browse uses shared deleted-view guard"
if (Select-String -Path ".\000_FRAMEWORK\080_HELP DESK\FW_HD_Issues_B.vb" -Pattern "Overrides Function GetActiveBaseSql" -SimpleMatch -Quiet) {
    throw "FW_HD_Issues_B must use FW_Base_B SQL loading; remove the page-local GetActiveBaseSql override."
}
Write-Host "PASS: Help Desk browse uses shared Base_B SQL loading" -ForegroundColor Green
if (Select-String -Path ".\000_FRAMEWORK\080_HELP DESK\FW_HD_Issues_Support_B.vb" -Pattern "Inherits FW_HD_Issues_B|New FW_HD_Issues_B" -SimpleMatch -Quiet) {
    throw "FW_HD_Issues_Support_B must be independent of FW_HD_Issues_B."
}
Assert-Pattern -Path ".\000_FRAMEWORK\080_HELP DESK\FW_HD_Issues_Support_B.vb" -Pattern "Inherits FW_Base_B" -Description "Support browse independently inherits shared Base_B"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "Return Me.GetType().Name" -Description "Browse pages use runtime class names for FW_RoleTables keys"

Write-Step "DeletedFlag hydration fallback"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "Private Shared Function ApplyBrowseDeletedFilterFallback" -Description "Browse deleted fallback exists"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "ApplyBrowseDeletedFilterFallback(table, showDeletedOnly, sourceTableName)" -Description "Browse deleted fallback hydrates from the page table, not a hardcoded one"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern 'withDeletedFlag.Columns.Add("DeletedFlag", GetType(Boolean))' -Description "Fallback adds DeletedFlag to returned DataTable"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "ApplyDeletedFilterWithSourceHydration(table," -Description "Shared hydration helper is used by browse data paths"
Assert-Pattern -Path ".\000_FRAMEWORK\060_ROLES\Roles_B.vb" -Pattern "ResolveCurrentRoleFieldTableName()," -Description "Roles custom query passes source table for hydration"

Write-Step "Action icon access propagation"
Assert-Pattern -Path ".\000_FRAMEWORK\020_DASHBOARDS\Dashboard_Application.vb" -Pattern "New FW_UserAccessDiagnostic_B(currentUser, accessProfile)" -Description "Application action icon passes user and access profile to the page it opens"
Assert-Pattern -Path ".\000_FRAMEWORK\020_DASHBOARDS\Dashboard_Company.vb" -Pattern "New FW_UserAccessDiagnostic_B(currentUser, accessProfile)" -Description "Company action icon passes user and access profile to the page it opens"
# The Admin tile opened Users_AppAdmin_B until 2026-09-06 and now opens a dashboard, chosen by role
# at click time. The check follows the destination rather than being dropped: what it is really
# asserting is the Action Icon Guardrail - a tile passes the live user and profile to whatever it
# opens, and never constructs it bare.
#
# The Admin tile itself was removed on 2026-09-15 and the dashboards now open from the menu's
# application-settings tile. These two checks still named the old tile's code and failed - and
# because a failed assertion throws, the script stopped here, at line 125 of 278, and every check
# after it went unrun without anybody noticing. Followed to where the dashboards are opened now.
Assert-Pattern -Path ".\000_FRAMEWORK\010_MAIN MENU\FW_MainMenu.vb" -Pattern "New Dashboard_Application(currentUser, activeAccessProfile)" -Description "Main menu settings tile passes access context to the application dashboard"
Assert-Pattern -Path ".\000_FRAMEWORK\010_MAIN MENU\FW_MainMenu.vb" -Pattern "New Dashboard_Company(currentUser, activeAccessProfile)" -Description "Main menu settings tile passes access context to the company dashboard"
Assert-Pattern -Path ".\000_FRAMEWORK\090_DIAGNOSTICS\FW_UserAccessDiagnostic_B.vb" -Pattern "Optional profile As AccessProfile = Nothing" -Description "A page opened by an action icon accepts the access profile"

Write-Step "Shared concurrency contract"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\Models.vb" -Pattern "Public Enum SaveResult" -Description "Shared save result contract exists"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\Models.vb" -Pattern "Public Property RowVersion As Byte()" -Description "Editable models carry RowVersion"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "Public Shared Function TableHasRowVersion" -Description "Data layer checks RowVersion schema"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern 'WHERE WindowOrPage = @WindowOrPage' -Description "RoleTables metadata lookups use WindowOrPage only"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern 'WHERE WindowOrPage = @WindowOrPage) ' -Description "RoleTables upsert existence uses WindowOrPage only"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern 'VALUES (NULL, @WindowOrPage, @DBTable' -Description "New RoleTables metadata rows are not RegistrationID-specific"

Write-Step "PageGeneration_U presentation contract"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern '"1. Request Name"' -Description "Page Generation question 1 uses Pascal-style display text"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern '"10. Use RegistrationID from selected table (N/A)"' -Description "Page Generation question 10 uses the correct RegistrationID SQL caption"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern '"2. Pages To Generate"' -Description "Page Generation question 2 uses Pascal-style display text"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'CheckBox_UseRegistrationID' -Description "Page Generation exposes the Use Registration ID checkbox"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'WHERE [RegistrationID] = @RegistrationID' -Description "Page Generation generates a parameterized RegistrationID filter when enabled"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern '9. Browse SQL' -Description "Page Generation SQL question uses Pascal-style display text"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern '10. Use RegistrationID from selected table' -Description "Page Generation Use RegistrationID question follows Browse SQL"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'New String() {"RequestName", "PageBaseName", "BrowsePageName", "MaintenancePageName", "UnderlyingTableName", "MenuCaller", "IconFileName", "BrowseSql"}' -Description "Page Generation Admin Required fields exclude the Use Registration ID checkbox"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'Admin Required labels use a trailing *' -Description "Page Generation directions document star-only Admin Required labels"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'labelControl.Text &= " *"' -Description "Page Generation Admin Required labels show an asterisk"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'textBox.Tag = "Required"' -Description "Page Generation Admin Required controls participate in shared validation"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'borderPanel.BackColor = If(isEmpty, Color.Red, SystemColors.Control)' -Description "Page Generation Admin Required controls have red empty-value borders"
# The height is MaintenanceLayout.FieldHeight now, not a literal 26. The contract is unchanged - a
# caption is exactly as tall as the control beside it - but both are read from one place, so the
# check reads the same place rather than a number that used to agree with it.
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern '.Size = New Size(MaintenanceLayout.LabelWidth, MaintenanceLayout.FieldHeight)' -Description "Shared _U captions match the edit-control height"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Helpers\MaintenanceLayout.vb" -Pattern 'Public Const FieldHeight As Integer = 26' -Description "The shared edit-control height is still 26"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'caption height matches the height of its edit control' -Description "Page Generation directions document the shared _U caption-height rule"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern '1-pixel red outline' -Description "Page Generation directions document the shared _U required-border rule"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern 'RefreshLocalRequiredBorders()' -Description "Shared _U refreshes local required borders during validation"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern 'THE FOLLOWING ARE REQUIRED:' -Description "Required-control validation uses one clear heading"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern 'errors.Add(ResolveRequiredControlCaption(form, ctrl).ToUpperInvariant())' -Description "Required-control validation lists uppercase captions without repeated wording"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'Run the browse framework preflight' -Description "Page Generation directions require browse framework preflight"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'Run a duplicate-logic check before generating the pages' -Description "Page Generation directions require duplicate-logic review"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'Run the applicable _B/_U regression checks' -Description "Page Generation directions require _B/_U regression checks"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'Build the application after the regression checks pass' -Description "Page Generation directions require a post-regression build"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern 'touchedRequiredControls.Add(control)' -Description "Shared AddField required borders activate only once a required control is visited"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern 'border.BackColor = If(showWarning, Color.Red, SystemColors.Control)' -Description "Shared AddField required borders refresh color with control values"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern 'If Not IsEmptyRequiredControl(control) Then Return False' -Description "Shared AddField required borders stay hidden while a required control holds a value"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern 'Return touchedRequiredControls.Contains(control) OrElse hoveredRequiredControls.Contains(control)' -Description "Shared AddField required borders show only once a required control is visited or hovered"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'Generated _U pages must inherit required-field styling and validation from FW_Base_U' -Description "Directions require generated pages to inherit required styling from Base_U"
Assert-Pattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern 'must not create page-local red-border panels' -Description "Directions prohibit generated page-local required-border duplication"
if (Select-String -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGeneration_U.vb" -Pattern '"[0-9]+\. (RequestName|PageBaseName|BrowsePageName|MaintenancePageName|UnderlyingTableName|RegistrationID|EditableFields|ReadOnlyFields|LookupFields|AdminRequiredFields|CreateBehavior|UpdateBehavior|DeleteBehavior|MenuCaller|BrowseSql)"' -Quiet) {
    throw "PageGeneration_U must not expose raw database field names as question labels."
}
Write-Host "PASS: Page Generation question labels are not raw database field names" -ForegroundColor Green

Write-Step "Standard _U page hard-code guardrails"
# Recursive since 2026-09-04: the pages moved into 000_FRAMEWORK\<band>\ folders. A
# non-recursive scan of the root found none and this guardrail passed by checking nothing,
# which is the worst way for a check to fail - the count below is printed so it cannot
# happen quietly again.
$standardUpdatePages = Get-ChildItem -Path $repoRoot -Filter "*_U.vb" -File -Recurse |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|restore-points|project-backup|tests)\\' } |
    Where-Object { $_.Name -ne "FW_Base_U.vb" } |
    Where-Object { $_.Name -notin @("FW_Registration_U.vb") } |
    Where-Object { Select-String -Path $_.FullName -Pattern "Inherits FW_Base_U" -SimpleMatch -Quiet }

Write-Host "Standard _U pages in scope: $($standardUpdatePages.Count)" -ForegroundColor Gray
if ($standardUpdatePages.Count -eq 0) { throw "No standard _U pages found - the scan is looking in the wrong place." }
Write-Host "PASS: FW_Registration_U.vb is an approved legacy model-backed maintenance-page exception" -ForegroundColor Yellow

foreach ($page in $standardUpdatePages) {
    # A generated page is split into a hand-written half and a generated partial beside it, and the
    # bindings live in the partial. Reading only the first half reported Employees_U as having no
    # bindings at all - never seen, because the script used to stop before it got this far.
    $pageText = Get-Content -Path $page.FullName -Raw
    $generatedHalf = Join-Path $page.DirectoryName ($page.BaseName + ".Generated.vb")
    if (Test-Path $generatedHalf) {
        $pageText += [Environment]::NewLine + (Get-Content -Path $generatedHalf -Raw)
    }
    if ($pageText -match "New\s+SqlCommand|\b(INSERT\s+INTO|UPDATE\s+dbo\.|DELETE\s+FROM)\b") {
        throw "Standard _U page $($page.Name) contains page-local SQL/DML. Use the shared schema/data-access contract."
    }
    if ($pageText -notmatch "DataBindings\.Add") {
        throw "Standard _U page $($page.Name) has no data bindings. Every bound entry control must map to its database field."
    }
    Write-Host "PASS: $($page.Name) uses shared SQL ownership and data bindings" -ForegroundColor Green
}
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "Public Shared Function UpsertPageRecord" -Description "Data layer persists missing browse SQL records"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "UpsertPageRecord" -Description "Base browse persists generated fallback SQL"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "EnsureDefaultLayoutExists(registrationId)" -Description "Base browse ensures a shared default layout after the first successful grid load"
# Double-click invokes a command button rather than repeating what it does - still the contract, and
# still checked. It is no longer always the Modify button: a page whose point is something else says
# so by overriding DoubleClickCommandButton, which Switch User does. So the check is that the gesture
# performs a click on the command, and that the default command is Modify.
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "command.PerformClick()" -Description "Base browse double-click invokes a visible command button"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "Protected Overridable Function DoubleClickCommandButton() As Button" -Description "The double-click command is nameable by a page"
Assert-Block -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Block @"
        Protected Overridable Function DoubleClickCommandButton() As Button
            Return updateButton
        End Function
"@ -Description "Base browse double-click still defaults to the Modify button"
Assert-Pattern -Path ".\000_FRAMEWORK\060_ROLES\Roles_B.vb" -Pattern "modifyButton.PerformClick()" -Description "Roles double-click invokes its visible Modify button"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "If Not layoutChanged AndAlso Not String.IsNullOrWhiteSpace(existingLastUsed) Then" -Description "Base browse does not rewrite Last Used without a user layout change"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern '"LastUsed",' -Description "Base browse upserts Last Used when the grid JSON changed"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "WarnIfMissingRowVersion" -Description "Base maintenance page warns on missing RowVersion"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "CaptureOriginalRowVersion" -Description "Base maintenance page owns the original RowVersion"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "CopyOriginalRowVersion" -Description "Base maintenance page provides RowVersion copies"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "TableHasRowVersion" -Description "Base browse page checks RowVersion schema"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "WHERE UserID = @UserID AND RowVersion = @OriginalRowVersion" -Description "User update uses optimistic concurrency"
Assert-Pattern -Path ".\000_FRAMEWORK\050_REGISTRATION\FW_Registration_U.vb" -Pattern "ConfirmConcurrencyOverwrite" -Description "Registration handles concurrency conflicts"
# Users_AppAdmin_U carried these two checks until 2026-09-08. Registration_U asserts the same
# contract below and is the surviving standard _U page; nothing is lost by dropping the pair.
Assert-Pattern -Path ".\000_FRAMEWORK\050_REGISTRATION\FW_Registration_U.vb" -Pattern "CaptureOriginalRowVersion(currentRecord.RowVersion)" -Description "Registration captures its original RowVersion through Base_U"
Assert-Pattern -Path ".\000_FRAMEWORK\060_ROLES\Roles_U.vb" -Pattern "DataAccess.UpdateRoleField" -Description "Roles_U remains the documented custom immediate-write page"
Assert-Pattern -Path ".\000_FRAMEWORK\080_HELP DESK\HelpDeskDataAccess.vb" -Pattern "Public Property UpdatedBy As Integer?" -Description "Help Desk nullable update actor maps to a nullable model property"
Assert-Pattern -Path ".\000_FRAMEWORK\080_HELP DESK\HelpDeskDataAccess.vb" -Pattern "Public Property UpdatedOn As DateTime?" -Description "Help Desk nullable update timestamp maps to a nullable model property"
Assert-Pattern -Path ".\000_FRAMEWORK\080_HELP DESK\HelpDeskDataAccess.vb" -Pattern '.UpdatedBy = NullableInt(reader("UpdatedBy"))' -Description "Help Desk safely reads a null update actor"
Assert-Pattern -Path ".\000_FRAMEWORK\080_HELP DESK\HelpDeskDataAccess.vb" -Pattern '.UpdatedOn = NullableDate(reader("UpdatedOn"))' -Description "Help Desk safely reads a null update timestamp"

Write-Step "Default engineering guardrails"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Security\AccessSecurity.vb" -Pattern "Public Class AccessProfile" -Description "Access profile enforcement model exists"
Assert-Pattern -Path ".\100_CTY\MenuFormInitializer.vb" -Pattern "menu.SetAccessProfile(profile)" -Description "Menu receives the active access profile"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "BeginTransaction" -Description "Data layer contains explicit transaction boundaries"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "trans.Rollback()" -Description "Data layer rolls back failed transactions"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "ConfirmConcurrencyOverwrite" -Description "Base maintenance flow requires explicit conflict choice"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "bypassCancelCloseCheck" -Description "Base maintenance page controls close-after-save behavior"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Helpers\DeletedViewGuard.vb" -Pattern "TableSupportsDeletedView" -Description "Soft-delete policy is shared"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "LogUpdateAudit" -Description "Write operations have audit integration"
Assert-Pattern -Path ".\000_FRAMEWORK\060_ROLES\Roles_U.vb" -Pattern "Confirm Delete" -Description "Role permission removal requires confirmation"

# The three browse actions moved out of every generated page and into FW_Base_B on 2026-09-05. The
# hooks that reach them must stay off by default: pages written before they existed - Registration,
# AuditTrail, HD_Admin - reach Delete with no handler and say so. Flip either default to on and each
# of them silently gains a live command it never had, with nothing failing to show it.
Write-Step "Browse action hooks default to off"
Assert-Block -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Block @"
        Protected Overridable Function CreateMaintenancePage(recordId As Integer) As FW_Base_U
            Return Nothing
        End Function
"@ -Description "No maintenance page unless a page supplies one"
Assert-Block -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Block @"
        Protected Overridable Function UsesStandardSoftDelete() As Boolean
            Return False
        End Function
"@ -Description "No standard delete unless a page opts in"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "SoftDeleteGeneratedPageRecord" -Description "FW_Base_B owns the standard delete"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "RestoreGeneratedPageRecord" -Description "FW_Base_B owns the standard restore"
# Both halves of the soft-delete lifecycle answer to one hook. A page that could delete a record and
# not put it back would leave the deleted view showing a row whose only undo button says "not wired".
Assert-Block -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Block @"
        Protected Overridable Function HandleDefaultRestoreAction(recordId As Integer) As Boolean
            If Not UsesStandardSoftDelete() Then Return False
"@ -Description "Restore answers to the same opt-in as delete"
Assert-NotPattern -Path ".\000_FRAMEWORK\050_REGISTRATION\FW_Registration_B.vb" -Pattern "UsesStandardSoftDelete" -Description "Registration did not silently gain a delete"
# The generated page this asserted against went with the FW_Users pages on 2026-09-08, and
# 999_GENERATED is empty. The template is what actually decides it, so the check moves there
# and now covers every page generated in future rather than the one that happened to exist.
Assert-NotPattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGenerator.vb" -Pattern "SoftDeleteGeneratedPageRecord" -Description "The generated browse template does not copy the delete logic"

Write-Step "Main menu message workspace"
Assert-Pattern -Path ".\000_FRAMEWORK\010_MAIN MENU\FW_MainMenu.vb" -Pattern "SizeType.Percent, 34.0F" -Description "Messages region is widened"
Assert-Pattern -Path ".\000_FRAMEWORK\010_MAIN MENU\FW_MainMenu.vb" -Pattern "SizeType.Percent, 33.0F" -Description "Other main-menu regions share the reduced width"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessagesWindowControl.vb" -Pattern 'inboxButton = CreateTab("Inbox (0)"' -Description "Messages control shows unread count on Inbox"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessagesWindowControl.vb" -Pattern "MarkSelectedMessageRead" -Description "Messages control marks selected Inbox messages read"
Assert-Pattern -Path ".\000_FRAMEWORK\080_HELP DESK\FW_HD_Issues_U.vb" -Pattern "Conversation History" -Description "Help Desk update page provides conversation history"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessagesWindowControl.vb" -Pattern "SelectionChanged" -Description "Messages mark the selected Inbox item read"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessagesWindowControl.vb" -Pattern "GetMessageBody" -Description "Messages provide a quick body preview"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessagesWindowControl.vb" -Pattern "Unread messages must be viewed before they can be deleted." -Description "Unread messages cannot be deleted"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessagesWindowControl.vb" -Pattern "RestoreRecipient" -Description "Trash messages can be restored"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessagingDataAccess.vb" -Pattern "MarkRecipientRead" -Description "Read state is persisted per recipient"
Assert-Pattern -Path ".\000_FRAMEWORK\085_MESSAGING\MessageComposeForm.vb" -Pattern "SelectionMode.MultiExtended" -Description "Compose supports multi-select recipients"

Write-Step "Acting user: a change is recorded against whoever made it"

# While an administrator views as somebody else the session belongs to that person, so a write
# path reading the session's user id stamps the wrong name on the change. SessionState.ActingUserID
# is the one answer; these checks fail when a new write path reaches past it.
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\Models.vb" -Pattern "Public ReadOnly Property ActingUserID" -Description "The session can say who is acting"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\Models.vb" -Pattern "SwitchedUser.Original.UserId" -Description "Acting user is the administrator while switched"
Assert-NotPattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "SessionState.Current.Value.UserID" -Description "Base browse stamps the acting user, not the session"
Assert-NotPattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "SessionState.Current.Value.UserID" -Description "Base maintenance stamps the acting user, not the session"
Assert-NotPattern -Path ".\000_FRAMEWORK\070_PAGE GENERATION\PageGenerator.vb" -Pattern "Dim updatedBy = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)" -Description "Generated pages stamp the acting user"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "Dim acting = SessionState.ActingUserID" -Description "Audit rows fall back to the acting user"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Data\DataAccess.vb" -Pattern "SwitchedUserAuditNote" -Description "An audit row written while switched says so"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb" -Pattern "SwitchedUserGuard.AllowWrite" -Description "Saving while switched is refused"
Assert-Pattern -Path ".\000_FRAMEWORK\000_BASE CLASSES\FW_Base_B.vb" -Pattern "SwitchedUserGuard.AllowWrite" -Description "Delete and restore while switched are refused"
Assert-Pattern -Path ".\000_FRAMEWORK\500_INFRASTRUCTURE\Security\SwitchedUserGuard.vb" -Pattern "RETURN TO YOURSELF FROM THE ROLE BUTTON FIRST." -Description "One wording for the refusal"


Write-Step "Manual verification checklist"
Write-Host "Run these UI checks in Registration_B, Roles_B:" -ForegroundColor Yellow
Write-Host "  1) Custom SQL without DeletedFlag: Show Deleted should be disabled when grid lacks DeletedFlag." -ForegroundColor Yellow
Write-Host "  2) Delete a row where soft-delete is supported: row should disappear from normal view." -ForegroundColor Yellow
Write-Host "  3) Show Deleted: only deleted rows should appear." -ForegroundColor Yellow
Write-Host "  4) Restore row: row should return to normal view after Show Normal." -ForegroundColor Yellow

Write-Host "`nValidation completed successfully." -ForegroundColor Green
