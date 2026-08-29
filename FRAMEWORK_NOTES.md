# Framework Notes

Working knowledge of the framework that is not obvious from the code. Guardrails live in
`CLAUDE.md` and `.github/copilot-instructions.md`; the icon catalog lives in `ICON_CATALOG.md`.

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
| `SetTimezoneControlsVisible(isVisible)` | optional timezone controls |

- `MenuFormInitializer.Configure(menu, user, forceRefresh)` applies table-level access to actions
  and regions. Regions with no access show an access-denied panel.
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

## Browse (_B) Page Patterns

- **SQL loading:** the page calls `LoadSqlFromRoleTable()` in the constructor, then the shown
  handler. With no `FW_RoleTables` row the user picks a table from a dropdown (UPPERCASE names);
  the choice is written back to `FW_RoleTables`. Cancelling returns to the main menu without
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
  `BTN_Update_Caption`, `BTN_Delete_Caption`) and must be applied before user interaction.
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

### Required-field styling

Two paths, distinguished by the label background:

| Label | Owner | Border panel tag | Red border timing |
|---|---|---|---|
| **Yellow** `255,255,224` | metadata (`ApplyControlUpdates`) | `RequiredBorder_<control>` | live, on every change |
| **Blue** `221,235,247` | the page itself | `LocalRequiredBorder_<field>` | after the first save attempt |

A blue label is an **opt-out switch**: `ShouldSkipBrRequiredStyling` matches that exact ARGB and
makes the metadata path skip the field entirely — no border panel, no asterisk, no yellow repaint.
Help Desk uses this for its conditional required rules.

Focus and hover, wired by `WireFocusIndicators` for TextBoxBase, ComboBox, CheckBox,
DateTimePicker and NumericUpDown:

- focus border green `#37B469` (`Color.FromArgb(55, 180, 105)`)
- required and empty: red (`Color.Red`) instead, re-evaluated on change **while focused**
- hover background `221,235,247`, applied only when the control is not focused
- combos treat `Make a Selection` as empty via `SelectedIndex < 0 OrElse value <= 0 OrElse
  String.IsNullOrWhiteSpace(Text)`

Note the blue constant does double duty as both the hover background and the required-label
opt-out. Painting a label that blue for cosmetic reasons silently disables its required styling.

## Hardcode Guardrail For Generated Pages

Never put page-local SELECT/INSERT/UPDATE/DELETE SQL or duplicated field lists in a standard
`_B`/`_U` page. Browse SQL comes only from `FW_RoleTables.Table_SQL` through `FW_Base_B`; `_U`
controls and persistence are schema and shared-layer driven. Preflight fails on page-local DML,
missing `TextBox_`/`ComboBox_`/`CheckBox_` names, or missing data bindings.

## Physical Table Naming

Framework tables use the `FW_` prefix. For page generation the physical table is
`dbo.FW_PageGeneration_B_U`; the logical page keys are `PageGeneration_B` and `PageGeneration_U`.
Do not propagate the unprefixed `PageGeneration_B_U` name.

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
  which exports `HELLOWORLD_DB_*` environment variables.
- A bare `dotnet run` has no credentials and now stops at startup with a configuration message.
- Local development target: Server `BEELINK`, Database `WX_Framework`.

## Override Captions

Captions are role- and registration-specific. A company that calls Gender "Pronoun" changes it
once and it follows through the menu, the browse grid and the maintenance page. `Roles_U` is where
they are maintained, in two grids:

| Roles_U grid | Table | Scope | Applies to |
|---|---|---|---|
| top | `FW_RoleDetails` | one caption per **table** | menu and dashboard button captions, page titles, entity aliases |
| bottom | `FW_RoleFields` | one caption per **field** | `_B` grid column headings and `_U` control labels |

Resolution paths:

- **Table level** — `DataAccess.GetRoleDetailOverrideCaption(roleId, registrationId, tableName)`,
  consumed by `EntityDisplayNameHelper.ResolveEntityAlias`, which falls back to the supplied alias.
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
pixels. Both dashboards place their buttons through it — `02 FW Dashboard_Application.vb` and
`02 FW Dashboard_Company.vb`. Position a new button by cell, never by literal coordinates.

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
