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

Write-Step "Browse framework static validation"

if (-not $SkipBuild) {
    Write-Step "Build"
    dotnet build .\HelloWorld.vbproj -p:UseAppHost=false -p:OutputPath=bin\Debug\net10.0-windows-hotfix\
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
}

Write-Step "Shared deleted-view guard wiring"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "DeletedViewGuard.ResultHasDeletedFlagColumn(browseGrid)" -Description "Base browse uses shared result-column guard"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "DeletedViewGuard.TableSupportsDeletedView(tableName)" -Description "Base browse uses shared table deleted support guard"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "If col Is Nothing OrElse Not col.Visible Then" -Description "QBE derives fields only from visible browse columns"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "If IsPkAliasColumn(col) OrElse IsSoftDeleteColumnName(fieldName) Then" -Description "Grid-derived QBE excludes the internal PK alias"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern 'String.Equals(dc.ColumnName, "PK", StringComparison.OrdinalIgnoreCase)' -Description "Start Empty QBE excludes the internal PK alias"
if (Select-String -Path ".\01 FW_Base_B.vb" -Pattern 'AssignedManagerID' -SimpleMatch -Quiet) {
    throw "Base_B must not seed Entity-specific QBE fields; QBE fields must come from the active page grid."
}
Write-Host "PASS: Base browse does not seed Entity-specific QBE fields" -ForegroundColor Green
Assert-Pattern -Path ".\Users_AppAdmin_B.vb" -Pattern "Inherits FW_Base_B" -Description "Users browse inherits shared deleted-view guard"
Assert-Pattern -Path ".\Roles_B.vb" -Pattern "DeletedViewGuard.TableSupportsDeletedView(ResolveCurrentRoleFieldTableName()) AndAlso DeletedViewGuard.ResultHasDeletedFlagColumn(rolesGrid)" -Description "Roles browse uses shared deleted-view guard"
if (Select-String -Path ".\FW_HD_Issues_B.vb" -Pattern "Overrides Function GetActiveBaseSql" -SimpleMatch -Quiet) {
    throw "FW_HD_Issues_B must use FW_Base_B SQL loading; remove the page-local GetActiveBaseSql override."
}
Write-Host "PASS: Help Desk browse uses shared Base_B SQL loading" -ForegroundColor Green
if (Select-String -Path ".\FW_HD_Issues_Support_B.vb" -Pattern "Inherits FW_HD_Issues_B|New FW_HD_Issues_B" -SimpleMatch -Quiet) {
    throw "FW_HD_Issues_Support_B must be independent of FW_HD_Issues_B."
}
Assert-Pattern -Path ".\FW_HD_Issues_Support_B.vb" -Pattern "Inherits FW_Base_B" -Description "Support browse independently inherits shared Base_B"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "Return Me.GetType().Name" -Description "Browse pages use runtime class names for FW_RoleTables keys"

Write-Step "DeletedFlag hydration fallback"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "Private Shared Function ApplyEntityDeletedFilterFallback" -Description "Entity deleted fallback exists"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern 'withDeletedFlag.Columns.Add("DeletedFlag", GetType(Boolean))' -Description "Fallback adds DeletedFlag to returned DataTable"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "ApplyDeletedFilterWithSourceHydration(table," -Description "Shared hydration helper is used by browse data paths"
Assert-Pattern -Path ".\Roles_B.vb" -Pattern "ResolveCurrentRoleFieldTableName()," -Description "Roles custom query passes source table for hydration"

Write-Step "Action icon access propagation"
Assert-Pattern -Path ".\02 FW Dashboard_Application.vb" -Pattern "New Users_AppAdmin_B(accessProfile)" -Description "Application action icon passes access profile to Users browse"
Assert-Pattern -Path ".\02 FW Dashboard_Company.vb" -Pattern "New Users_AppAdmin_B(accessProfile)" -Description "Company action icon passes access profile to Users browse"
Assert-Pattern -Path ".\MenuFormInitializer.vb" -Pattern "New Users_AppAdmin_B(profile)" -Description "Main menu action icon passes access profile to Users browse"
Assert-Pattern -Path ".\Users_AppAdmin_B.vb" -Pattern "Optional profile As AccessProfile = Nothing" -Description "Users browse accepts action icon access profile"

Write-Step "Shared concurrency contract"
Assert-Pattern -Path ".\Models.vb" -Pattern "Public Enum SaveResult" -Description "Shared save result contract exists"
Assert-Pattern -Path ".\Models.vb" -Pattern "Public Property RowVersion As Byte()" -Description "Editable models carry RowVersion"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "Public Shared Function TableHasRowVersion" -Description "Data layer checks RowVersion schema"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern 'WHERE WindowOrPage = @WindowOrPage' -Description "RoleTables metadata lookups use WindowOrPage only"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern 'WHERE WindowOrPage = @WindowOrPage) ' -Description "RoleTables upsert existence uses WindowOrPage only"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern 'VALUES (NULL, @WindowOrPage, @DBTable' -Description "New RoleTables metadata rows are not RegistrationID-specific"

Write-Step "PageGeneration_U presentation contract"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern '"1. Request Name"' -Description "Page Generation question 1 uses Pascal-style display text"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern '"10. Use RegistrationID from selected table (N/A)"' -Description "Page Generation question 10 uses the correct RegistrationID SQL caption"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern '"2. Pages To Generate"' -Description "Page Generation question 2 uses Pascal-style display text"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'CheckBox_UseRegistrationID' -Description "Page Generation exposes the Use Registration ID checkbox"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'WHERE [RegistrationID] = @RegistrationID' -Description "Page Generation generates a parameterized RegistrationID filter when enabled"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern '9. Browse SQL' -Description "Page Generation SQL question uses Pascal-style display text"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern '10. Use RegistrationID from selected table' -Description "Page Generation Use RegistrationID question follows Browse SQL"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'New String() {"RequestName", "PageBaseName", "BrowsePageName", "MaintenancePageName", "UnderlyingTableName", "MenuCaller", "IconFileName", "BrowseSql"}' -Description "Page Generation Admin Required fields exclude the Use Registration ID checkbox"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'Admin Required labels use a trailing *' -Description "Page Generation directions document star-only Admin Required labels"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'labelControl.Text &= " *"' -Description "Page Generation Admin Required labels show an asterisk"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'textBox.Tag = "Required"' -Description "Page Generation Admin Required controls participate in shared validation"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'borderPanel.BackColor = If(isEmpty, Color.Red, SystemColors.Control)' -Description "Page Generation Admin Required controls have red empty-value borders"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern '.Size = New Size(120, 26)' -Description "Shared _U captions match the 26-pixel edit-control height"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'caption height matches the height of its edit control' -Description "Page Generation directions document the shared _U caption-height rule"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern '1-pixel red outline' -Description "Page Generation directions document the shared _U required-border rule"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern 'RefreshLocalRequiredBorders()' -Description "Shared _U refreshes local required borders during validation"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern 'THE FOLLOWING ARE REQUIRED:' -Description "Required-control validation uses one clear heading"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern 'errors.Add(ResolveRequiredControlCaption(form, ctrl).ToUpperInvariant())' -Description "Required-control validation lists uppercase captions without repeated wording"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'Run the browse framework preflight' -Description "Page Generation directions require browse framework preflight"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'Run a duplicate-logic check before generating the pages' -Description "Page Generation directions require duplicate-logic review"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'Run the applicable _B/_U regression checks' -Description "Page Generation directions require _B/_U regression checks"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'Build the application after the regression checks pass' -Description "Page Generation directions require a post-regression build"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern 'touchedRequiredControls.Add(control)' -Description "Shared AddField required borders activate only once a required control is visited"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern 'pair.Value.BackColor = If(showWarning, Color.Red, SystemColors.Control)' -Description "Shared AddField required borders refresh color with control values"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern 'Return touchedRequiredControls.Contains(control) AndAlso IsEmptyRequiredControl(control)' -Description "Shared AddField required borders stay hidden until a visited required control is empty"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'Generated _U pages must inherit required-field styling and validation from FW_Base_U' -Description "Directions require generated pages to inherit required styling from Base_U"
Assert-Pattern -Path ".\PageGeneration_U.vb" -Pattern 'must not create page-local red-border panels' -Description "Directions prohibit generated page-local required-border duplication"
if (Select-String -Path ".\PageGeneration_U.vb" -Pattern '"[0-9]+\. (RequestName|PageBaseName|BrowsePageName|MaintenancePageName|UnderlyingTableName|RegistrationID|EditableFields|ReadOnlyFields|LookupFields|AdminRequiredFields|CreateBehavior|UpdateBehavior|DeleteBehavior|MenuCaller|BrowseSql)"' -Quiet) {
    throw "PageGeneration_U must not expose raw database field names as question labels."
}
Write-Host "PASS: Page Generation question labels are not raw database field names" -ForegroundColor Green

Write-Step "Standard _U page hard-code guardrails"
$standardUpdatePages = Get-ChildItem -Path $repoRoot -Filter "*_U.vb" -File |
    Where-Object { $_.Name -ne "01 FW_Base_U.vb" } |
    Where-Object { $_.Name -notin @("FW_Registration_U.vb") } |
    Where-Object { Select-String -Path $_.FullName -Pattern "Inherits FW_Base_U" -SimpleMatch -Quiet }

Write-Host "PASS: FW_Registration_U.vb is an approved legacy model-backed maintenance-page exception" -ForegroundColor Yellow

foreach ($page in $standardUpdatePages) {
    $pageText = Get-Content -Path $page.FullName -Raw
    if ($pageText -match "New\s+SqlCommand|\b(INSERT\s+INTO|UPDATE\s+dbo\.|DELETE\s+FROM)\b") {
        throw "Standard _U page $($page.Name) contains page-local SQL/DML. Use the shared schema/data-access contract."
    }
    if ($pageText -notmatch "DataBindings\.Add") {
        throw "Standard _U page $($page.Name) has no data bindings. Every bound entry control must map to its database field."
    }
    Write-Host "PASS: $($page.Name) uses shared SQL ownership and data bindings" -ForegroundColor Green
}
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "Public Shared Function UpsertRoleTableRecord" -Description "Data layer persists missing browse SQL records"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "UpsertRoleTableRecord" -Description "Base browse persists generated fallback SQL"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "EnsureDefaultLayoutExists(registrationId)" -Description "Base browse ensures a shared default layout after the first successful grid load"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "updateButton.PerformClick()" -Description "Base browse double-click invokes the visible Modify button"
Assert-Pattern -Path ".\Roles_B.vb" -Pattern "modifyButton.PerformClick()" -Description "Roles double-click invokes its visible Modify button"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "If Not layoutChanged AndAlso Not String.IsNullOrWhiteSpace(existingLastUsed) Then" -Description "Base browse does not rewrite Last Used without a user layout change"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern '"LastUsed",' -Description "Base browse upserts Last Used when the grid JSON changed"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern "WarnIfMissingRowVersion" -Description "Base maintenance page warns on missing RowVersion"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern "CaptureOriginalRowVersion" -Description "Base maintenance page owns the original RowVersion"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern "CopyOriginalRowVersion" -Description "Base maintenance page provides RowVersion copies"
Assert-Pattern -Path ".\01 FW_Base_B.vb" -Pattern "TableHasRowVersion" -Description "Base browse page checks RowVersion schema"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "WHERE ID = @ID AND RowVersion = @OriginalRowVersion" -Description "Entity update uses optimistic concurrency"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "WHERE UserID = @UserID AND RowVersion = @OriginalRowVersion" -Description "User update uses optimistic concurrency"
Assert-Pattern -Path ".\FW_Registration_U.vb" -Pattern "ConfirmConcurrencyOverwrite" -Description "Registration handles concurrency conflicts"
Assert-Pattern -Path ".\Users_AppAdmin_U.vb" -Pattern "ConfirmConcurrencyOverwrite" -Description "Users handles concurrency conflicts"
Assert-Pattern -Path ".\FW_EntityCrudAdapter.vb" -Pattern "RecordChanged" -Description "Entity adapter handles concurrency conflicts"
Assert-Pattern -Path ".\Entity_U.vb" -Pattern "CaptureOriginalRowVersion(EntityData.RowVersion)" -Description "Entity captures its original RowVersion through Base_U"
Assert-Pattern -Path ".\Users_AppAdmin_U.vb" -Pattern "CaptureOriginalRowVersion(UserData.RowVersion)" -Description "Users captures its original RowVersion through Base_U"
Assert-Pattern -Path ".\FW_Registration_U.vb" -Pattern "CaptureOriginalRowVersion(currentRecord.RowVersion)" -Description "Registration captures its original RowVersion through Base_U"
Assert-Pattern -Path ".\Roles_U.vb" -Pattern "DataAccess.UpdateRoleField" -Description "Roles_U remains the documented custom immediate-write page"
Assert-Pattern -Path ".\HelpDeskDataAccess.vb" -Pattern "Public Property UpdatedBy As Integer?" -Description "Help Desk nullable update actor maps to a nullable model property"
Assert-Pattern -Path ".\HelpDeskDataAccess.vb" -Pattern "Public Property UpdatedOn As DateTime?" -Description "Help Desk nullable update timestamp maps to a nullable model property"
Assert-Pattern -Path ".\HelpDeskDataAccess.vb" -Pattern '.UpdatedBy = NullableInt(reader("UpdatedBy"))' -Description "Help Desk safely reads a null update actor"
Assert-Pattern -Path ".\HelpDeskDataAccess.vb" -Pattern '.UpdatedOn = NullableDate(reader("UpdatedOn"))' -Description "Help Desk safely reads a null update timestamp"

Write-Step "Default engineering guardrails"
Assert-Pattern -Path ".\AccessSecurity.vb" -Pattern "Public Class AccessProfile" -Description "Access profile enforcement model exists"
Assert-Pattern -Path ".\MenuFormInitializer.vb" -Pattern "menu.SetAccessProfile(profile)" -Description "Menu receives the active access profile"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "BeginTransaction" -Description "Data layer contains explicit transaction boundaries"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "trans.Rollback()" -Description "Data layer rolls back failed transactions"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern "ConfirmConcurrencyOverwrite" -Description "Base maintenance flow requires explicit conflict choice"
Assert-Pattern -Path ".\01 FW_Base_U.vb" -Pattern "bypassCancelCloseCheck" -Description "Base maintenance page controls close-after-save behavior"
Assert-Pattern -Path ".\DeletedViewGuard.vb" -Pattern "TableSupportsDeletedView" -Description "Soft-delete policy is shared"
Assert-Pattern -Path ".\DataAccess.vb" -Pattern "LogUpdateAudit" -Description "Write operations have audit integration"
Assert-Pattern -Path ".\Roles_U.vb" -Pattern "Confirm Delete" -Description "Role permission removal requires confirmation"

Write-Step "Main menu message workspace"
Assert-Pattern -Path ".\MainMenu.vb" -Pattern "SizeType.Percent, 34.0F" -Description "Messages region is widened"
Assert-Pattern -Path ".\MainMenu.vb" -Pattern "SizeType.Percent, 33.0F" -Description "Other main-menu regions share the reduced width"
Assert-Pattern -Path ".\MessagesWindowControl.vb" -Pattern 'inboxButton = CreateTab("Inbox (0)"' -Description "Messages control shows unread count on Inbox"
Assert-Pattern -Path ".\MessagesWindowControl.vb" -Pattern "MarkSelectedMessageRead" -Description "Messages control marks selected Inbox messages read"
Assert-Pattern -Path ".\FW_HD_Issues_U.vb" -Pattern "Conversation History" -Description "Help Desk update page provides conversation history"
Assert-Pattern -Path ".\MessagesWindowControl.vb" -Pattern "SelectionChanged" -Description "Messages mark the selected Inbox item read"
Assert-Pattern -Path ".\MessagesWindowControl.vb" -Pattern "GetMessageBody" -Description "Messages provide a quick body preview"
Assert-Pattern -Path ".\MessagesWindowControl.vb" -Pattern "Unread messages must be viewed before they can be deleted." -Description "Unread messages cannot be deleted"
Assert-Pattern -Path ".\MessagesWindowControl.vb" -Pattern "RestoreRecipient" -Description "Trash messages can be restored"
Assert-Pattern -Path ".\MessagingDataAccess.vb" -Pattern "MarkRecipientRead" -Description "Read state is persisted per recipient"
Assert-Pattern -Path ".\MessageComposeForm.vb" -Pattern "SelectionMode.MultiExtended" -Description "Compose supports multi-select recipients"

Write-Step "Manual verification checklist"
Write-Host "Run these UI checks in Entity_B, Users_AppAdmin_B, Roles_B:" -ForegroundColor Yellow
Write-Host "  1) Custom SQL without DeletedFlag: Show Deleted should be disabled when grid lacks DeletedFlag." -ForegroundColor Yellow
Write-Host "  2) Delete a row where soft-delete is supported: row should disappear from normal view." -ForegroundColor Yellow
Write-Host "  3) Show Deleted: only deleted rows should appear." -ForegroundColor Yellow
Write-Host "  4) Restore row: row should return to normal view after Show Normal." -ForegroundColor Yellow

Write-Host "`nValidation completed successfully." -ForegroundColor Green
