# Framework Notes

Working knowledge of the framework that is not obvious from the code. Guardrails live in
`CLAUDE.md`; the icon catalog lives in `ICON_CATALOG.md`.

Merged from Copilot's machine-local `framework-notes.md` on 2026-08-28. Every claim below was
checked against the source at that time; corrections to the original notes are marked.

## Menu Framework

`MainMenu.vb` is the reusable ribbon plus pluggable regions shell. Public API:

| Member | Purpose |
|---|---|
| `LoadRegionControl(region, content)` | inject a UserControl into a region |
| `LoadRegionForm(region, childForm)` | inject a child Form into a region |
| `ConfigureActionVisibility(actionKey, isVisible, isEnabled)` | role-driven ribbon visibility |
| `UpsertActionTile(...)` | add or update an action tile |
| `SetActionIconFromFile(actionKey, iconFileName, fallbackIcon)` | swap a tile's PNG |
| `ConfigureArrangement(surfaceName)` | wire tile rearranging and chosen pictures |
| `SetTimezoneControlsVisible(isVisible)` | optional timezone controls |

- `MenuFormInitializer.Configure(menu, user, forceRefresh)` applies table-level access to actions
  and regions. Regions with no access show an access-denied panel. It runs **six times** over a
  session — every role change and several dialog returns — so anything it wires must be created
  once and re-applied thereafter, never re-wired. `ConfigureArrangement` is called last, after the
  tiles it arranges exist.
- The menu form is not application specific. It receives its initializer from `LoginForm`, and the
  initializer supplies the surface name that its arrangement and pictures are stored under. A
  second application writes its own initializer rather than a second menu form. Do not put a
  surface name, an application name, or an exe name inside the form.
- An App Admin can drag the tiles in the left flow panel into a different order; the panel shows
  a bar under the movable stretch of the row only while a drag is under way. What is saved is a
  **rank**, never a coordinate — the panel closes the
  gap when a permission hides a tile, and a coordinate would leave a hole. See `ICON_CATALOG.md`
  for the storage and the App Admin boundary.
- The head of that row is anchored — `close`, `dashboard`, `application-settings` — by a list the
  initializer passes in. Anchored tiles get no drag handlers and no saved rank, and an anchor beats
  anything already written down. Anchoring is independent of visibility: an anchored tile hidden by
  a permission still lets the row close up around it.
- Reusable region controls implement `IAccessControlledControl` so they adjust their own internal
  controls by capability at initialization.
- Region controls split for reuse: `MessagesWindowControl.vb`, `UsersListsWindowControl.vb`,
  `GeneralDashboardWindowControl.vb`, `AcmeDashboardWindowControl.vb`.

**Correction:** the original notes referred to `MenuForm.vb`. That file no longer exists; the shell
is `MainMenu.vb`.

**Menu icon request template** — ask for these before implementing a new icon: ActionType
(Table/Page/SubMenu/Command), target (DB table such as `FW_Gender`, or a page/submenu key), caption
source (OverrideCaption / Table_Alias / fixed), icon file, display order and left/right section,
visibility rule, click behavior.

## Form Sizing

Always size forms with `Me.ClientSize`, never `Me.Size`. `ClientSize` is the exact usable area, so
layout maths works: equal margins means grid right edge + margin = `ClientSize.Width`. Using
`Me.Size` includes the window chrome and leaves the layout off by the border and title bar.

## Browse (_B) Page Patterns

- **SQL loading:** the page calls `LoadSqlFromRoleTable()` in the constructor, then the shown
  handler. With no `FW_Pages` row the user picks a table from a dropdown (UPPERCASE names);
  the choice is written back to `FW_Pages`. Cancelling returns to the main menu without
  showing the page. `Table_Alias` drives the page title.
- **Layout precedence** (`DataAccess.GetPreferredTableLayout`): `LastUsed` for the user, else the
  shared `Default` (`userId = 0`), else the physical SQL-derived columns.
  `EnsureDefaultLayoutExists` seeds `Default` on first load. On close the current layout is saved
  as `Last Used`, skipped only when unchanged and a row already exists.
- **Grid state persistence:** all `_B` grids must return to the same row and scroll position after
  a `_U` edit. Reload the whole grid and capture/restore view state — never patch single cells.
  `CaptureGridViewState()` before reload, `RestoreGridViewState()` after. The key column is
  detected dynamically, not hardcoded. Reference implementations: `Entity_B.vb`,
  `Users_AppAdmin_B.vb`.
- **DataGridView real-time events:** checkboxes use `CurrentCellDirtyStateChanged` + `CommitEdit`;
  text columns use `EditingControlShowing` with `TextChanged` wired on the editing TextBox.
- **CRUD captions** come from `FW_Registration` (`BTN_Create_Caption`, `BTN_Read_Caption`,
  `BTN_Update_Caption`, `BTN_Delete_Caption`) and must be applied before user interaction — on
  first open, without needing a combo click. The session carries the active registration's captions
  for runtime context, but the database stays the source of truth and can refresh them.
- **`Users_AppAdmin_B` initial load order:** preselect `SessionState.Current.RegistrationID` in the
  registration combo, resolve the selected registration ID, then apply CRUD captions, then load the
  grid.
- **PK contract:** only an explicit SQL alias named `PK` is accepted as the row key. Fallbacks such
  as `ID`, `RoleID`, `UserID` are not used. Resolution and missing-PK warnings are centralized in
  `MaintenanceKeyGuard.vb`; do not duplicate them page-locally.
- **QBE** is derived from the *visible* browse-grid columns (`PopulateQbeFromGridColumns`), with PK
  and soft-delete excluded. Start Empty pages derive it from the SQL schema instead
  (`PopulateQbeFromSqlSchema`).
- **Standard inheritance rule:** a new standard `_B` page must not override `GetActiveBaseSql()` or
  create a second SQL-loading path. Any intentional override is a documented special-page exception
  and needs a validation check.
- **Deleted view:** use `DeletedViewGuard` plus the shared DataAccess helpers; no page-local
  deleted-view logic. Run `scripts\validate-browse-regression.ps1` after browse changes.

### Load order, from opening the page to rows on screen

**Load** — `BrowsePage_Load` calls `LoadSqlFromRoleTable`, which is the only thing that decides
what SQL the page runs:

1. Reads `RegistrationID` from the session and the page name from `ResolveBrowsePageName()`.
2. `GetDbTableFromRoleTableByWindowOrPage` resolves the physical table, and the title comes from
   the `Table_Alias`.
3. `GetTableSqlFromRoleTableByWindowOrPage` fetches the SQL for this page. If a row exists, that
   SQL is used and `sqlLoadedFromRoleTable` is set.
4. With no row, it falls back: SQL for the same *table* under a different page, else
   `BuildDefaultSqlForBrowse`. Either way it writes an `FW_Pages` row back through
   `UpsertRoleTableRecord` and tells the user which happened. A failure to create the row throws
   rather than leaving the page half-registered.
5. `EnsureSqlOrClose` closes the page if there is still no SQL.

**Then the page either starts empty or loads.** `StartsEmptyOnInitialLoad()` decides. Start Empty
runs `InitializeEmptyBrowseState`, which binds an empty table and derives QBE from the SQL schema
instead of from a grid that does not exist yet. Everything else calls `RefreshGrid(Nothing, True)`.

**`RefreshGrid` is the whole pipeline** and runs on every reload, not just the first:

1. `CaptureGridViewState()` — selected row and scroll position, restored at the end. This is why a
   `_B` page returns to the same row after a `_U` edit.
2. Resolve the registration, capture the current column visibility, and get the SQL through
   `GetActiveBaseSql()`.
3. Apply the user scope: `ViewOnlyMyRecords` becomes `UserID = @UserID`, or warns and stops when
   the result has no `UserID` to scope by.
4. `DataAccess.GetBrowseRowsByRegistration` runs the SQL with the QBE filters, the deleted-only
   flag, the scope predicate and the row cap.
5. Bind to the grid and set the record count.
6. **Headers** — `ApplyFriendlyColumnHeaders`. An `OverrideCaption` on `FW_RoleFields` wins;
   otherwise `ToFriendlyCaption` splits the column name into words. Formatting is owned by
   `DisplayNameFormatter.ToDisplayName`; do not add a second formatter.
7. **Hiding**, in order: `ApplyPkColumnHiding`, `HideRegistrationIdColumn`, `HideSoftDeleteColumns`,
   `HideInvisibleRoleFieldColumns`. Then the saved visibility map is applied and all four run
   **again** — the map can otherwise re-show a column that must never be visible.
8. `EnsureAtLeastOneManageableVisibleColumn`, `UpdateMaintenanceKeyAvailability`, fit columns to
   width, refresh the columns manager and the deleted-view button.
9. **Layout**, first load only: `EnsureDefaultLayoutExists` seeds the shared `Default`, then
   `ApplySavedLayoutIfAvailable` applies `LastUsed` for this user, else `Default`.
10. **QBE**, only when the SQL text or the visible-column set has actually changed — both are
    compared as signatures, so a plain refresh does not rebuild it and lose what the user typed.
11. `RestoreGridViewState` puts the selection and scroll position back.

**QBE has two sources, and they are not interchangeable:**

| | When | Excludes |
|---|---|---|
| `PopulateQbeFromGridColumns` | normal pages, from the **visible** grid columns after all hiding | invisible columns, the `PK` alias, soft-delete columns |
| `PopulateQbeFromSqlSchema` | Start Empty pages, before a grid exists | same internal aliases, derived from the SQL schema |

The grid-derived path is why hiding a browse column also removes it from QBE. Existing operator and
value entries are keyed by field name and restored after a rebuild, so re-deriving QBE does not
discard a filter the user was in the middle of typing.

**A custom `_B` page changes none of this.** `Users_AppAdmin_B`, `Entity_B` and `Roles_B` inherit
the same pipeline; they override presentation hooks such as `ApplyPageSpecificLayout`,
`ApplyFriendlyColumnHeaders` or `HideSoftDeleteColumns`. Overriding `GetActiveBaseSql()` to create a
second SQL-loading path is a documented special-page exception, not a normal option, and needs its
own validation check.

### CRUD buttons and permissions

Captions and permissions are separate concerns and come from different places.

- **Captions** — `ApplyCrudButtonCaptions(registrationId)`, from `FW_Registration`
  (`BTN_Create_Caption` and friends). Applied on every `RefreshGrid`, so they are right on first
  open without waiting for a registration change.
- **Permissions** — `accessProfile.Can(accessTableName, AccessCapability.X)` for `Create`, `Read`,
  `Update`, `Delete`, `UseQbe`, `ExpandQbe`, `ViewAllRecords` and `ViewOnlyMyRecords`.

A CRUD button is visible only when **all** of these hold: the capability is granted, the page is not
`OnlyUseQbe()`, the deleted view is off, and — for Read, Update and Delete — the result actually
carries a usable maintenance key. That last condition is why the buttons disappear on a page whose
SQL has no `AS PK`: without a key there is no record to open.

`ViewOnlyMyRecords` is a data filter, not a button rule. It rewrites the query scope to
`UserID = @UserID`. If the result set has no `UserID` to scope by, the page warns once and returns
rather than silently showing everything.

Button visibility is a convenience, never the enforcement point. The write itself must check access
at its own boundary.

**What the buttons actually do** is owned by `FW_Base_B`, through two hooks a page overrides:

| Hook | Default | Effect when supplied |
|---|---|---|
| `CreateMaintenancePage(recordId)` | `Nothing` | Create and Update open the returned `FW_Base_U`. `recordId` 0 means Create |
| `UsesStandardSoftDelete()` | `False` | Delete **and Restore** work, through `SoftDeleteGeneratedPageRecord` and `RestoreGeneratedPageRecord` |

**One hook governs both halves of the soft-delete lifecycle.** A page that could delete a record but
not put it back is a trap: the row still exists, the deleted view shows it, and the only button
offering to undo reports the page as unwired. Restore was a stub for *every* browse page until
2026-09-05 — `RestoreButton_Click` ran the whole sequence and then said "not wired".

`RestoreButton_Click` runs its guards first — deleted view, usable key, selected row, confirmation —
and calls `HandleDefaultRestoreAction(recordId)` last, so the handler owns only the write. The hook
used to be called before all of that and took no arguments, which left any page implementing restore
with no choice but to repeat all four checks.

**Both default to off, and that is deliberate.** A page that supplies neither reaches the existing
"not wired for this browse page yet" message, exactly as before — which is what `FW_Registration_B`,
`FW_AuditTrail_B` and `FW_HD_Admin_B` still do for Delete. Defaulting either to on would hand every
page in the application a live command it never had.

They are independent of each other: a browse page generated without a `_U` partner still deletes.

Delete reads the primary key from the database via `GetPrimaryKeyFieldName` rather than having it
written into the page, so a renamed key cannot leave a page deleting against a column that is gone.

Until 2026-09-05 each generated `_B` carried its own copy of all three handlers — about fifty lines,
identical in every page but the `_U` type name. `Users_AppAdmin_B` and `FW_Registration_B` still
override `HandleDefault*` directly, which is the older path and still wins where present.

### QBE and the Find button

`TryBuildFiltersFromQbe` turns the QBE grid into filters, and it is overridable for pages with
their own criteria. Rows with an empty value are skipped, so a blank QBE means no filter. Each row
is validated against its field kind: an unknown field, an operator the field kind does not support,
a non-numeric value in a numeric field or an unparseable boolean each produce a message and stop the
search rather than running a broken query.

Find then sets `currentFilters` and calls `RefreshGrid`, and reports the outcome in the retrieval
status line — record count, "No records found", or the row-cap message.

**The empty-QBE row cap** matters on large tables: with rows present but no criteria entered,
`GetEmptyQbeRowLimit()` caps the result and the status line says so, inviting a criterion. The cap
applies only to that case; a real filter is never truncated.

Filter values entered by the user survive a QBE rebuild — they are keyed by field name and restored
— so refreshing the grid does not wipe what someone was typing.

### Column layout

Layouts live in the table-layout store, keyed by registration, user, page, table and layout type.

| Type | Owner | Notes |
|---|---|---|
| `Default` | shared, `userId = 0` | seeded by `EnsureDefaultLayoutExists` on first load |
| `LastUsed` / "Last Used" | the individual user | written on close |
| named layouts | the user, or Company Admin | chosen through the layout combo |

**Precedence on load** (`GetPreferredTableLayout`): the user's `LastUsed`, else the shared
`Default`, else the physical column order the SQL produced.

**Saving** — `Save My Layout` prompts for a name and refuses reserved system names, refuses a
leading `*` unless the session is Company Admin, and refuses to save with no visible data column.
Saving as Default writes the shared `userId = 0` row.

**On close** — `BrowsePage_FormClosing` writes the current layout as `LastUsed`, but only when the
layout actually changed or no `LastUsed` row exists yet, so simply opening and closing a page does
not churn the store. Failures there are swallowed deliberately: layout persistence must never block
a form from closing.

**Columns manager** — the panel drives visibility and order for the current session; changes flow
back into the grid and are picked up by the next layout snapshot. `EnsureAtLeastOneManageableVisibleColumn`
stops the user hiding everything, and the layout controls disable entirely when a page has no
manageable columns.

**Correction:** the original notes said "hide only PK; keep all other columns visible including
soft-delete and registration columns". The code also calls `HideRegistrationIdColumn` and
`HideSoftDeleteColumns`, so all three are hidden.

**Correction:** the original notes named the base classes `FW_Base_Browse_B` and
`FW_Base_Update_U`. They are `FW_Base_B` and `FW_Base_U`.

## Maintenance (_U) Page Patterns

**Every data control needs both of these:**

1. `.Name = "TextBox_<FieldName>"` (also `ComboBox_`, `CheckBox_`, `DateTimePicker_`,
   `NumericUpDown_`, `MaskedTextBox_`, `RichTextBox_`) — without it, business rules cannot find or
   style the control, and column-length resolution falls back to nothing.
2. `control.DataBindings.Add("Text", DataObject, "FieldName", True)` — without it, enumeration
   stores the live data value as the caption instead of the field name.

- Foreign-key controls use the exact DB field suffix, for example `Label_GenderID` +
  `ComboBox_GenderID`, even when the visible caption is friendly ("Gender").
- Non-standard class names override `GetTableNameOverride()` to return the real table, for example
  `Users_AppAdmin_U` -> `FW_Users`.
- Combos use `ConfigureLookupCombo(...)` so every lookup gets a `Make a Selection` placeholder, and
  `GetComboSelectedIdOrZero(...)` on save so placeholder/null/<=0 maps to 0.
- `AddField(caption, ...)` creates the `Label_<caption>` + `TextBox_<caption>` pair, and a
  `LocalRequiredBorder_<caption>` panel when required. It only produces TextBoxes — combos,
  checkboxes and pickers are hand-built and do not get the pairing automatically.
- Text column lengths come from the database (`GetTextColumnMaxLengths`, INFORMATION_SCHEMA) and
  are applied at bind and again before save. Do not hardcode `MaxLength` on a page.
- Page-local validation lines go through `GetAdditionalValidationMessageLines()`; never add a
  page-local first-error MessageBox.

**Correction:** the original notes named the metadata source `vw_FW_UpdateControls`. It is
`dbo.vw_FW_ControlUpdates_U`, filtered by `PageName` **and** `RegistrationID`
(`DataAccess.GetControlUpdates`).

**Correction:** the original notes described required styling as a red `BorderStyle` on the
TextBox, and validation as enumerating controls with `BorderStyle = FixedSingle`. Neither is
accurate: the red border is a `Panel` placed behind the control, and
`DataAccess.ValidateRequiredControls` selects controls by `Tag = "Required"`.

### What a page must supply, and what it gets

Three members are `MustOverride`. Everything else has a working default.

| Member | Purpose |
|---|---|
| `BindToFormInternal()` | load the record, set control values, then add `DataBindings`. Values first, bindings second. |
| `ApplyMode()` | apply read-only / enabled state for the page's mode |
| `TryBuildRecord()` | build the record from the form; return False to stop the save |

The hooks worth knowing, all overridable with a safe default:

| Hook | Default | Override when |
|---|---|---|
| `SaveRecord()` | returns True, persists nothing | the page writes to the database |
| `IsViewOnly()` | False | Read mode — OK closes without saving |
| `IsCreatingNewRecord()` | False | the page has a create mode; field permissions use it to choose `Can_Create` over `Can_Update` |
| `ShouldWarnOnCancel()` | **False** | the page can lose edits — without this, Cancel never warns |
| `OkButtonText()` | "OK" | a different verb, such as Delete |
| `GetTableNameOverride()` | derived from the page name | the class name does not match the table, e.g. `Users_AppAdmin_U` to `FW_Users` |
| `GetAdditionalValidationMessageLines()` | empty | page-specific validation — never a page-local first-error MessageBox |
| `ResolveAuditRecordKey()` / `ResolveAuditOperationType()` | empty, inferred | the key or operation cannot be inferred |
| `GetLayoutColumnLefts()` | single column | the page is laid out in more than one column |

`ShouldWarnOnCancel` defaulting to False is the one that catches people: a page that edits real data
and forgets it will discard changes silently.

### Save pipeline

`OK` runs `ExecuteSaveWorkflow`, which is `ValidateAndBuildForSave` then `SaveRecordWithAudit`. The
page closes only after the save has actually succeeded — a failed save leaves it open.

**In view-only mode OK does not save at all**; it closes.

`ValidateAndBuildForSave`:

1. `NormalizeTextInputsForSave` — trim, and apply database column lengths.
2. Mark every required field visited and refresh the borders, so the red matches the message.
3. `ValidateRequiredControls` in tab order, then `ValidateUniqueFields`, then
   `GetAdditionalValidationMessageLines()`. All three contribute to **one** message.
4. On failure: one message box, focus the first missing field, and stop.
5. On success: `UnmaskForSave` → `TryBuildRecord()` → `RemaskAfterSave`. The unmask is essential —
   without it a masked field would write `••••••` to the database.

`SaveRecordWithAudit` writes a `BeforeSave` audit row, calls `SaveRecord()`, then writes an
`AfterSave` row carrying the outcome. Both snapshots are the control state, so the audit records
what the user saw. An exception inside `SaveRecord` is caught and reported as a failed save rather
than escaping. On success the dirty baselines are reset so the page is clean again.

### Concurrency

`WarnIfMissingRowVersion` runs during `BindToForm` and warns once if the table has no `RowVersion`
column, because nothing below can protect the record.

The page captures the token with `CaptureOriginalRowVersion` on load and passes
`CopyOriginalRowVersion()` on save — copies, never the array itself, so nothing can mutate it
mid-flight. The save result must distinguish success, conflict, deleted record, unavailable
protection, and failure:

- **Conflict** — `ConfirmConcurrencyOverwrite()` asks explicitly whether to overwrite the newer
  version. There is no last-saved-wins path.
- **No protection available** — `ShowConcurrencyUnavailable()` and the save stops.
- After an approved overwrite, the page re-reads the record for a fresh token before retrying.

### Cancel and unsaved changes

`Cancel` and the window X both go through the same check, so neither can bypass it.
`HasNetUnsavedChanges` compares a snapshot of every field against a baseline, which means changing a
value and changing it back is **not** a change and does not prompt.

`bypassCancelCloseCheck` is set on the paths that close deliberately — a successful save, or
`CloseAfterSuccessfulCommand` — so a completed action never asks whether to discard.

The prompt only appears at all when `ShouldWarnOnCancel()` is True.

### Other shared behavior

- **`AddField`** builds the `Label_` + `TextBox_` pair, sets `Tag = "Required"` where required, and
  creates the hidden border panel. It produces TextBoxes only — combos, checkboxes and pickers are
  hand-built.
- **`ConfigureLookupCombo`** gives every lookup a `Make a Selection` placeholder;
  `GetComboSelectedIdOrZero` maps placeholder, null and non-positive to 0 on save.
- **Tab order manager** — available to Application Admin. Reordering and ticking apply to the live
  form; OK persists them and Cancel, or collapsing the panel, reverts both. Selecting a row focuses
  that field on the page.
- **Enum button** — writes the page's controls to the metadata store, which is what populates
  `FW_Enumerations_U` and therefore what field-level permissions can be configured against.

### Which role and registration apply — both page types

**Field-level metadata always follows the logged-in session's `RoleID` *and* `RegistrationID`.**
Both, together, on `_B` and `_U` alike. Neither page type lets you ask for another role's or
another registration's field rules.

| What | Scoped by | Where |
|---|---|---|
| `_U` required fields, field override captions, `Make_Invisible` / `Can_Read` / `Can_Create` / `Can_Update` / `IsUnique` | session RoleID + session RegistrationID, keyed by **PageName** | `GetControlUpdates` filtering `vw_FW_ControlUpdates_U` |
| `_B` column captions and hidden columns | session RoleID + session RegistrationID, keyed by **TableName** | `GetPageInitMetadata(roleId, registrationId, tableName)` |
| Unique-field validation scope | session RegistrationID | `ValidateUniqueFields` |

Note the keying difference: maintenance metadata is per **page**, browse metadata is per **table**.

**CRUD buttons are session-scoped too:**

| What | Scoped by |
|---|---|
| Whether a CRUD button is visible and enabled | session RoleID **and** RegistrationID, through `accessProfile.Can(...)` - the profile is built from the session role's `FW_RoleDetails` rows, which are themselves per registration |
| The CRUD button captions (`BTN_Create_Caption` and friends from `FW_Registration`) | session RegistrationID |

So "caption" means two different things, but both default to the session: a **field** caption from
`FW_RoleFields`, a **CRUD button** caption from `FW_Registration`.

**The registration selector is the one narrow exception, and it is admin-initiated.** Every `_B`
page uses it by default - only `FW_Registration_B` opts out - but it is shown *and populated* only
when the session has `ViewAllRecords` on that table and admin controls are visible, and it is
**seeded to the session's registration**. Until someone actively changes it,
`TryGetActiveRegistrationId` finds nothing selected and falls through to the session. Changing it
re-scopes the **data, QBE Find and the CRUD button captions** only.

**Field-level metadata never follows the selector.** An Application Admin who switches to another
registration sees that registration's rows and its Create/Read/Update/Delete wording, but still
their **own** role and registration's required fields, field captions, hidden columns and
permissions.

One more that follows neither: the record's own `RegistrationID`, and data scoped to the record such
as the role lists on `Users_AppAdmin_U`, come from the **record**, falling back to the session for a
new record (`GetEffectiveRegistrationId()`). Roles are per registration, so the list has to load for
the user being edited rather than the person editing.

Do not "fix" this by plumbing the browse page's selected registration into `ApplyControlUpdates`.
Field rules follow the logged-in role and registration; data follows the record.

`ApplyControlUpdates` is called from exactly one place, `Base_U.BindToForm`, and takes only the page
name and the is-new-record flag. There is no per-page override.

**Correction:** the view once hardcoded `RegistrationID = 1` and exposed no `RoleID`, so field
attributes were inert outside registration 1 and a field configured for two roles returned two rows
with an arbitrary winner. `sql/026_control_updates_role_scope.sql` moved both filters up into
`GetControlUpdates`, where they read from the session.

### Documented exceptions

**`Roles_U` does not inherit `FW_Base_U`, and should not.** It is a permission administration
console, not a single-record editor: three grids (available tables, granted permissions, role
fields) with immediate per-row writes, no `SaveRecord`, no RowVersion, no single OK/Cancel save.
Base_U's contract — one record, one concurrency token, one save that must succeed before the page
closes — has nothing to bind to. Its writes are immediate and individually confirmed; do not treat
them as transactional.

`scripts\validate-maintenance-regression.ps1` records this exception by name. Every other `_U` page
must inherit `FW_Base_U`, and the script fails if one does not.

### Field lifecycle order

The order below is load-bearing. Several steps only work where they are, and moving one breaks
something that is not obvious from reading it in isolation.

**Page constructor**

1. The page builds its controls. `AddField` creates the `Label_` + `TextBox_` pair and, when
   required, sets `Tag = "Required"` and a hidden `LocalRequiredBorder_<field>` panel.
2. `BindToForm()`:
   1. `WarnIfMissingRowVersion()`
   2. `BindToFormInternal()` — the page loads the record, assigns control values, then adds
      `DataBindings`. Values first, bindings second.
   3. `DataAccess.ApplyControlUpdates()` — `ApplyFieldPermissions` runs **before** required
      styling, and returns whether it hid the field so a hidden field gets no border panel.
   4. `AdoptRequiredBorderPanels()` — Base_U takes ownership of the `RequiredBorder_<control>`
      panels metadata created, and wires their change events.
   5. `NormalizeTextInputsForSave()` — trims and applies database column lengths.
   6. `ResetPendingRecordBaseline()` — provisional.
3. The page constructs its controllers (`SmartyAddressLookupController`, `ZipCoderController`)
   **after** `BindToForm`, so control sizes and positions are final.

**On `Shown`**

4. `ApplySharedPageCaption()`
5. `RemoveReadOnlyControlsFromTabOrder()`
6. `WireFocusIndicators()` — reuses a field's existing required border panel as its focus panel,
   so the green ring and the red ring are the same control.
7. Action buttons get `TabStop = False`.
8. `CollapseHiddenFieldRows()` — **must** be here. `Control.Visible` returns `False` for every
   child until the form is displayed, so testing visibility any earlier collapses the whole page.
9. `RefreshLocalRequiredBorders()`
10. `ApplySavedTabOrder()` — applies saved `TabIndex` values, overriding `SetManualTabOrder`.
11. `InitializeTabOrderManager()`
12. Then, on a second `BeginInvoke`: `SetInitialFieldFocus()` followed by
    `ResetPendingRecordBaseline()`. The baseline is captured **last**, after every step above has
    settled, or it will not match what the controls hold.

**On save** — `ValidateAndBuildForSave()` normalizes, marks every required field visited, refreshes
the borders, runs `ValidateRequiredControls` then `ValidateUniqueFields` then
`GetAdditionalValidationMessageLines()`, and on failure shows one message and focuses the first
missing field. On success: `UnmaskForSave` → `TryBuildRecord` → `RemaskAfterSave`.

### Required-field styling

Two paths, distinguished by the label background. **App Admin required always beats permission
required.**

| Label | Means | Set by | Border panel tag |
|---|---|---|---|
| **Blue** `221,235,247` | **App Admin required** - declared on the page, so it applies to every role | `AddField(required:=True)` in Base_U | `LocalRequiredBorder_<field>` |
| **Yellow** `255,255,224` | **Permission required** - varies by who is logged in | `ApplyControlUpdates`, from `FW_RoleFields.IsRequired` for the session role and registration | `RequiredBorder_<control>` |

Permissions are processed in full for every field. `ApplyControlUpdates` calls
`ShouldSkipBrRequiredStyling`, which matches the blue ARGB exactly, and a match suppresses **only**
the required styling: the metadata required border, and the asterisk-and-yellow repaint. Everything
else on the row still applies - the field permissions, and the `OverrideCaption`.

That scope matters. The helper used to `Continue For` and abandon the whole row, so a field that
was both App Admin required and given an `OverrideCaption` silently never received its caption.

The blue is load-bearing, not decoration. A page that declares a field required and leaves the
label unpainted silently gives up its precedence, and the field is restyled by whatever the role
says. `FW_HD_Issues_U` paints these labels by hand as well; that predates `AddField` doing it and
is now redundant rather than special.

Both paths use the **same** red-border rule, applied by `RefreshLocalRequiredBorders` in Base_U.
Data access creates the metadata panel hidden and does not manage its visibility.

**When a field is red:** it has been *visited* and is empty. Visited means entered, left, or
edited. The focus the page sets for itself on open does not count, so a blank new record shows no
red until the user touches something. A failed save marks every required field visited, so the red
borders match the message. Red clears on the first keystroke or a real selection.

Focus and hover, wired by `WireFocusIndicators` for TextBoxBase, ComboBox, CheckBox,
DateTimePicker, NumericUpDown and Button (excluding Save, Cancel and the enum button):

- focus border green `#37B469` (`Color.FromArgb(55, 180, 105)`)
- visited and empty: red (`Color.Red`) instead, re-evaluated on enter, leave and change
- hover background `221,235,247`, applied only when the control is not focused

**Controls inside a `FlowLayoutPanel`.** The focus border is a sibling panel sitting behind the
control, given its z-order by `SendToBack` / `BringToFront`. A FlowLayoutPanel reads child index as
*flow position*, so both of those are layout changes there, not z-order changes: the border shows up
as a green block occupying its own slot in the row, and every wired child is dragged to the front of
the flow, which renders the row in reverse. `WireFocusIndicators` therefore calls
`HostFlowChildForFocusBorder` first, which moves the control into a plain auto-sizing `Panel` at the
same flow index and carries its `Margin` and `TabIndex` across. The border then lives inside that
host, where z-order means z-order. Pages that wrap their own required fields already do this by
hand — see `ApplyPageRequiredFieldStyling` in `PageGeneration_U.vb`.

Note the blue constant does double duty as both the hover background and the required-label
opt-out. Painting a label that blue for cosmetic reasons silently disables its required styling.

**Empty combo:** `DataAccess.IsEmptyComboSelection` is the single owner of that test. The
`Make a Selection` placeholder carries `0` for numeric lookups and `""` for text-keyed ones; both
count as empty, as do `SelectedIndex < 0`, a null value and blank `Text`. A value that is text
rather than a number — `BR_RoleBased`, say — is a genuine selection. Do not re-derive this test.

**Validation order:** `ValidateRequiredControls` lists missing fields in **tab order** — tab stops
by `TabIndex` first, then everything else by position — and hands back the first one so the caller
can focus it. Reordering fields in the Tab Order manager changes the message order too.

### Hidden fields

`FieldPermissions.HideField` clears the control's data bindings before hiding it, keeping the
loaded value.

This is not optional. A control hidden before the form is created never gets a window handle, but
its binding still takes part in validation: on the first focus change WinForms pulls an empty value
out of the handle-less control, writes that into the bound record, and pushes the blank back to the
control. The loaded value is lost, the page reads as permanently dirty, and a save writes the empty
value over the real one.

The record still carries the field, and the page still writes the column — with its original value.
Hidden fields are not excluded from the `UPDATE`.

`CollapseHiddenFieldRows` closes the gap a hidden field leaves. It moves whole rows, never single
controls, and only when every control on the row is invisible. Pages laid out in more than one
column override `GetLayoutColumnLefts()` so each column closes its own gaps; a row is held back if
rising would collide with another column.

## Page Generation

There are **two** ways a page pair gets built, and they are not the same thing.

| Path | Driven by | Documented in |
|---|---|---|
| By hand | a filled-in page request read by Claude Code | `.github/new-page-request-manual.md` — page identity, SQL rules, field options, create/update/delete behavior, and a 13-step implementation procedure |
| Automated | `PageGenerator.Generate`, from an `FW_GeneratedPages` row | this section |

The manual's "What Happens During Implementation" describes the **by hand** path. The generator
does none of it: no restore point, no build, no regression run, no manual test.

### The two previews on the page request

The footer carries two buttons that sound alike and answer different questions.

| Button | Shows | Answers |
|---|---|---|
| `Preview` | `BuildGeneratedPageRequest()` — the filled-in request document | is the *request* well formed |
| `Preview Code` | `PageGenerator.Preview` — the exact source that would be written | is the *template* going to produce these pages |

`Preview Code` requires a saved request, because the generator reads the saved row rather than the
form; if there are unsaved edits it offers to save first rather than writing silently. It never
writes a page file, an `FW_Pages` row or a dashboard icon. Its Summary tab lists all three as
*would be* actions, including whether an existing file would be overwritten and whether the
`FW_Pages` row would be inserted, left alone, have its SQL updated, or be replaced.

`Preview` and `Generate` share one owner: `PageGenerator.BuildPlan` performs every validation and
emits both sources, `Generate` writes what it produced, and `Preview` displays it. A preview
therefore cannot disagree with what generation writes. `ClassifyRoleTableAction` is shared the same
way — `Generate` performs the action and the preview describes it.

### Compile Check

Reading the previewed source does not prove it compiles. The **Compile Check** button inside the
preview writes both sources plus a scratch `PageGenPreview.vbproj` to `obj\pagegen-preview\`, which
references the built `bin\Debug\net10.0-windows\SDC.Framework.dll`, and runs `dotnet build` on it. The
generated pages inherit `FW_Base_B` / `FW_Base_U` from that assembly, so this is a real compile and
reports real `BC` errors with file, line and column. Nothing in the workspace is touched — the SDK
excludes `obj\` from the application's own compile items.

It compiles **both pages together**, not the tab you are looking at, and the result names what it
compiled. That is the only scope that works: the generated browse page constructs the maintenance
page to open a record, so compiling it alone would fail on a type that is not there.

It needs the application to have been built at least once. If the assembly is missing it says so
instead of failing obscurely.

### What the generator refuses

Generation stops and reports every problem at once rather than emitting a broken page:

- neither page target selected, or a name missing its `_B` / `_U` suffix
- no underlying table, or a table with no primary key
- no `_B` or `_U` fields when that page was requested
- a field that does not exist in the table's schema
- **browse SQL with no `AS PK` alias.** Only an explicit `PK` alias is accepted as the row key, so
  without it the page opens with a missing-key warning and Read, Update and Delete hidden
- a lookup entry not matching `<Field> -> <Table>.<ValueColumn> displayed as <DisplayColumn>`

### What the generated `_B` page contains

Twenty lines: `Inherits FW_Base_B`, a `New(user, profile)` constructor naming the table, an override
of `CreateMaintenancePage` returning the `_U` partner, and `UsesStandardSoftDelete` returning True.
`OnlyUseQbe` is added when the request asks for it, and `CreateMaintenancePage` is omitted when no
maintenance page was requested.

That is the whole page. Everything else lives in `FW_Base_B` — see **CRUD buttons and permissions**.

### What the generated `_U` page contains

`Inherits FW_Base_U`, a `New(id, user, profile)` constructor, and an override of `SavedRecordId` so
the browse page can reselect the saved row after a Create. Overrides emitted: `GetPageName`,
`GetTableNameOverride`,
`BindToFormInternal`, `ApplyMode`, `TryBuildRecord`, `SaveRecord`, `ResolveAuditRecordKey`,
`ShouldWarnOnCancel` (True) and `IsCreatingNewRecord` (`recordId <= 0`).

Fields become `TextBox_<Field>` through `AddField`, except lookups, which become
`ComboBox_<Field>` filled by `ConfigureLookupCombo` over `DataAccess.GetLookupTable` and saved
through `GetComboSelectedIdOrZero`. RowVersion is captured on load, and the save uses
`TrySaveGeneratedPageRecord`, so a conflict offers overwrite, reloads for a fresh token and retries
rather than dead-ending.

### What the generator writes outside the source files

- **`FW_Pages`** for the browse page. No row: inserted. A row for the same table: its SQL is
  updated if it differs, otherwise skipped. A row for a *different* table: overwritten. Registration
  is passed as `0`, which `UpsertRoleTableRecord` stores as `NULL` — shared across registrations.
- **The maintenance source baseline** on the request row, so later drift can be compared.
- **A dashboard icon**, but only when the request's `MenuCaller` is `Dashboard_Application`. An icon
  failure is reported as `ICON WARNING` and does not fail generation.

### Changing a page's button surface leaves the old button behind

**A button is only ever added, never moved and never removed.** Placement is one
`If dashboard … ElseIf main menu …`, and neither branch looks at the other surface.

Generate a page onto a dashboard, then regenerate it naming Main Menu, and you have **two** buttons.
The dashboard keeps its field declaration, constructor block, layout line and click handler — all
compiled source — and the ribbon gains a tile. The `FW_DashboardLayouts` row keyed
`ActionKey_<page>` stays too, holding the old position and picture.

Each surface is idempotent about *itself*, which is what makes this easy to miss: the ribbon skips a
key already in `MenuFormInitializer.vb`, the dashboard skips an icon already present. It behaves
perfectly until the surface changes.

**It happens without anyone changing the request, too.** When the ribbon is full or unwritable the
page falls back to the App Admin dashboard, deliberately, so a generated page is never reachable
from nowhere. Free a ribbon slot, regenerate, and the fallback icon and the new tile both exist.

Since 2026-09-05 both the preview and the generation report say so —
`ALREADY ON DASHBOARD_APPLICATION: UsersY_B has an icon there from an earlier generation` — naming
the file to edit. **It is a warning, not a fix.** Removing the old button is still by hand.

Removing it automatically means deleting four separate pieces of generated code from a `.vb` file by
text manipulation, which is a good deal harder than inserting at an anchor. It is also section 4 of
`PAGE_GENERATION_SIMPLIFICATION.md` in its sharpest form: were a button's existence a row, moving one
between surfaces would be an update and the question would not arise.

### What it still leaves to you

- **`ICON_CATALOG.md`** is never updated, even when it adds the dashboard button. The Action Icon
  Guardrail requires the catalogue entry, so that is a manual step.
- **No Read or Delete mode.** `IsViewOnly` is never generated, and `ApplyMode` only makes the
  primary key read-only.
- **`TryBuildRecord` is a stub** returning `True`. Page-specific validation means overriding
  `GetAdditionalValidationMessageLines` by hand.
- **No roles, role details, role fields, permissions or access profiles** — deliberately, per the
  request manual.
- **Nothing is built, run or tested.** The generator emits source text, so a template mistake only
  surfaces when the project is compiled.

## Hardcode Guardrail For Generated Pages

Never put page-local SELECT/INSERT/UPDATE/DELETE SQL or duplicated field lists in a standard
`_B`/`_U` page. Browse SQL comes only from `FW_Pages.Table_SQL` through `FW_Base_B`; `_U`
controls and persistence are schema and shared-layer driven. Preflight fails on page-local DML,
missing `TextBox_`/`ComboBox_`/`CheckBox_` names, or missing data bindings.

## Physical Table Naming

Framework tables use the `FW_` prefix. For page generation the physical table is
`dbo.FW_GeneratedPages`, keyed `GeneratedPageID`; the logical page keys are `PageGeneration_B` and
`PageGeneration_U`.

It was `FW_PageGeneration_B_U` until 2026-09-03 - a page name wearing a table's clothes, and named
after a page pair that had itself since been renamed. The table name lives in one place now,
`DataAccess.GeneratedPagesTable`, because it was a string literal in nine sites across three files
and that is the arrangement in which a rename reaches eight of them.

## Help Desk Rules

- `FW_HD_Issues_U` uses `FW_Base_U.ValidateAndBuildForSave` plus
  `GetAdditionalValidationMessageLines` for aggregate validation. No page-local first-error
  MessageBox.
- Required Help Desk fields use the page-owned pattern: label text with `*`, blue label background,
  red empty-control border, one combined validation message.
- New non-admin tickets display Status `New` and keep Status read-only. Support roles may change
  Status. A user response to a Closed ticket reopens it.

## Run And Database

- Run with `.\run-local.ps1` (gitignored, holds the local password). It calls `run-with-db.ps1`,
  which exports `SDC_DB_*` environment variables.
- A bare `dotnet run` has no credentials and now stops at startup with a configuration message.
- Local development target: Server `BEELINK`, Database `WX_Framework`.

## Role Permission Tables

Three levels, each scoped to a role and registration:

| Table | Grain | Drives |
|---|---|---|
| `FW_Roles` | role | the role itself |
| `FW_RoleDetails` | role + **table** | table-level CRUD, folded into `AccessCapability`, drives the `_B` CRUD buttons |
| `FW_RoleFields` | role + table + **field** | field-level attributes on `_B` columns and `_U` controls |

**The parent link is RoleID + table name, not `RoleDetailID`.** Both tables carry a `RoleDetailID`
column, but it is populated only on `FW_RoleFields` and is blank on every `FW_RoleDetails` row, so
joining on it matches nothing. `FW_RoleFields.TableName` matches `FW_RoleDetails.DB_Table` for the
same `RoleID`. Verified 2026-08-29: zero orphans under that link, against 90 false orphans if
`RoleDetailID` is used.

Only one foreign key exists across the set, `FK_FW_RoleDetails_FW_RoleSchema`, so none of this is
enforced by the database. `RegistrationID` and `RoleID` are denormalized onto both child tables and
can drift from their parent without complaint.

Beware the same-named columns at different grains: `Can_Create` / `Can_Read` / `Can_Update` on
`FW_RoleDetails` are **table-level** and gate the CRUD buttons; on `FW_RoleFields` they are
**field-level** and gate an individual control.

## Override Captions

Captions are role- and registration-specific. A company that calls Gender "Pronoun" changes it
once and it follows through the menu, the browse grid and the maintenance page. `Roles_U` is where
they are maintained, in two grids:

| Roles_U grid | Table | Scope | Applies to |
|---|---|---|---|
| top | `FW_RoleDetails` | one caption per **table** | page titles — **and, intended but not built, menu and dashboard button captions** |
| bottom | `FW_RoleFields` | one caption per **field** | `_B` grid column headings and `_U` control labels |

Resolution paths:

- **Table level** — `DataAccess.GetRoleDetailOverrideCaption(roleId, registrationId, tableName)`,
  consumed by `PageTitleHelper.ResolveTableAlias`, which falls back to the supplied alias. This
  entry named `EntityDisplayNameHelper` until 2026-09-05; that module was deleted on 2026-09-03 with
  `FW_Entity` and `PageTitleHelper` took its work.
- **Field level, browse** — `GetRoleFieldCaptionMapForCurrentContext()` feeds
  `ApplyFriendlyColumnHeaders`. An override wins; otherwise the header falls back to
  `ToFriendlyCaption(sourceName)`.
- **Field level, maintenance** — `DataAccess.ApplyControlUpdates` sets
  `labelCtrl.Text = EnsureRequiredMarker(overrideCaption)` on the linked label, so the required
  asterisk survives the override.

Editing an `OverrideCaption` cell in `Roles_U` propagates the change
(`propagateCaptionOverride`) rather than leaving related rows stale.

**Relationship to `DisplayNameFormatter`:** the formatter is only the *fallback*. Any field or
table with an override caption never reaches it. So a caption that looks wrong is either a bad
override in `FW_RoleFields`/`FW_RoleDetails`, or a formatter fallback — check which before
changing either.

### Button captions do not follow the override yet

**Known gap, recorded 2026-09-05.** A table override reaches the page title and stops there. The
whole chain is one call:

```
GetRoleDetailOverrideCaption  →  PageTitleHelper  →  Roles_B.vb:167
```

Every menu tile and dashboard icon carries a **string literal** — `"UsersY"`, `"User Admin"`,
`"Application Settings"`, `"UserX"`. Nothing looks an override up for them. So a company that renames
Gender to Pronoun sees it on the page and not on the button that opens the page, which reads as a
half-applied setting rather than as a missing feature.

It was never built rather than lost: `EntityDisplayNameHelper`, which this section used to name,
built *titles* — `"... Listing"`, `"... Maintenance - Create"` — and no button ever consulted it.

**What building it needs.** The missing link is tile to table, and both halves already exist:

```
tile → target page name → DataAccess.GetPageDbTableByWindowOrPage → FW_Pages.DB_Table
     → PageTitleHelper → caption
```

So: tiles carry the page they open; the menu resolves and applies captions where
`ApplyRoleAffordances` already runs, which is exactly when role or registration changes the answer;
dashboards do the same. A tile with no page — Close, My Profile — keeps its literal.

Two things to settle rather than discover. A tile's caption is deliberately two lines to fit a 96px
button (`"User" & vbCrLf & "Admin"`), and an override arrives as one string, so it can overflow or
wrap badly. And the tile must keep its coded caption to put back, or a role change that *removes* an
override leaves the previous role's wording in place.

## Dashboard Grid And Generated Buttons

Dashboards are laid out on an imaginary grid so buttons line up consistently.
`DashboardGridLayout.vb` owns the maths — there are no per-button coordinates in the dashboards
themselves:

| Constant | Value |
|---|---|
| `FirstColumnLeft` | 70 |
| `ColumnGap` | 150 |
| `FirstRowTop` | 105 |
| `RowGap` | 130 |
| `StandardClientWidth` | 820 |
| `StandardClientHeight` | 560 |

`CellLeft(column)`, `CellTop(row)` and `CellLocation(row, column)` convert 1-based grid positions to
pixels. Both dashboards place their buttons through it — `000_FRAMEWORK\020_DASHBOARDS\Dashboard_Application.vb` and
`000_FRAMEWORK\020_DASHBOARDS\Dashboard_Company.vb`. Position a new button by cell, never by literal coordinates.

**Page generation can add the button for you.** When a browse page is generated with
`MenuCaller = Dashboard_Application`, `PageGenerator` edits the dashboard source directly:

- inserts a `Button` named `GeneratedPageActionKey_<BrowsePageName>`
- places it at the next free `DashboardGridLayout.CellLocation(row, column)`, found by scanning the
  dashboard source for cells already in use
- is idempotent — it skips if that action key already exists
- if a new row is needed it also edits `DashboardGridLayout.vb`, growing `StandardClientHeight` to
  `560 + ((requiredRow - 3) * RowGap)`
- throws `No dashboard grid position is available for the generated icon` when the grid is full

So generation writes to three places: the new `_B`/`_U` files, the dashboard, and possibly the grid
layout constants. Any actionable button it adds still needs a matching entry in `ICON_CATALOG.md`.

## Smarty Address Lookup

`FW_Registration` holds the Smarty credentials, all `varchar(50)` — deliberately sized to hold the
auth values with room to spare:

| Column | Type |
|---|---|
| `Smarty_AuthID` | `varchar(50)` |
| `Smarty_AuthToken` | `varchar(50)` |
| `Smarty_EmbeddedKey` | `varchar(50)` |
| `Smarty_UseEmbeddedKey` | `bit` |

**Maintained in `FW_Registration_U`.** That page is where the credentials are entered and where the
use of Smarty is validated — a registration is only cleared for real-time address verification once
its Smarty values are in place.

**Consumed by `SmartyAddressLookupController.vb`**, which drives type-ahead address lookup. It is
constructed with two callbacks, `isEnabled` and `getEmbeddedKey`, and does nothing unless both
gates pass: `IsLookupEnabled()` is true, the embedded key is non-blank, and the typed text is at
least 3 characters. A debounce timer starts on input rather than calling per keystroke.

**The values live in the session.** All four are properties on `UserContext`
(`Models.vb:37-40`) and are populated at login from the registration record. Pages and controllers
read them from session context — do not re-query `FW_Registration` for them.

## Planned Multi-Application Context (Not Yet Implemented)

Future direction only — do not implement.

- `RegistrationID` is selected at login; then `ApplicationID` available to that user and
  registration; then `RoleID` available for that ApplicationID + RegistrationID + UserID.
- Active context hierarchy: ApplicationID, RegistrationID, UserID, RoleID.
- The selected application decides which application-specific MainMenu loads; pages are reached
  through that menu.
- Prefer reusable roles with application-specific user-role assignments.
- Permission key becomes ApplicationID + RegistrationID + RoleID + table/page/action.
- Access-profile cache keys and audit records must both include all four context values.
- Likely new tables: applications, registration-application availability, user-application access,
  application-scoped user-role assignment.

## Unverified

Carried over from the original notes but not confirmed against source:

- `Users_AppAdmin_U` enforcing Email as required and unique across `FW_Users`, excluding the
  current `UserID` on update.
- The `dashboard` ribbon action's current target — see `ICON_CATALOG.md`.
