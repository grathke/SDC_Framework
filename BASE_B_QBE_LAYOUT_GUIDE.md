# Base_B QBE and Layout Guide

This guide describes how a standard browse page (`*_B`) works when it inherits `FW_Base_B`. It is the wiring contract for new browse pages.

## Core Contract

A standard `_B` page must use the shared `FW_Base_B` behavior:

- Browse SQL comes from `FW_RoleTables.Table_SQL`.
- The page key is normally the runtime class name, such as `Entity_B`.
- Results are loaded through `DataAccess.GetBrowseRowsByRegistration`.
- The grid is the source for visible QBE fields after layout and internal-column hiding are complete.
- Saved layouts control column order, visibility, and width.
- Page-local code supplies context and actions; it must not create a second SQL, QBE, layout, or DML path.

## Database SQL Path

For a page opened in a session:

1. `FW_Base_B` receives the current `UserContext`, optional `AccessProfile`, and optional table context.
2. On the form `Load` event, `LoadSqlFromRoleTable()` resolves:
   - `RegistrationID` from the active session.
   - `WindowOrPage` from `ResolveBrowsePageName()`.
   - SQL from `FW_RoleTables.Table_SQL`.
3. If the page has no `FW_RoleTables` row or no SQL, Base_B builds a safe fallback such as `SELECT * FROM dbo.FW_Entity`, persists the row, and keeps the SQL in the shared SQL control.
4. The page closes if SQL remains unavailable.
5. Every browse refresh calls `DataAccess.GetBrowseRowsByRegistration` with:
   - Registration ID
   - Active QBE filters
   - Active SQL
   - Deleted-record state
   - User-scope predicate and user ID
   - Optional row limit
6. The returned `DataTable` becomes the `DataGridView` data source.

The SQL must include `PK` as an explicit maintenance key alias when update, delete, or restore actions require a record key. The internal `PK` column is hidden from users but remains available for maintenance operations.

Example SQL stored in `FW_RoleTables.Table_SQL`:

```sql
SELECT
    e.EntityID AS PK,
    e.LastName,
    e.FirstName,
    e.MiddleName,
    e.GenderID,
    e.AssignedManager,
    e.DeletedFlag
FROM dbo.FW_Entity AS e
```

Do not put page-local `SELECT`, `INSERT`, `UPDATE`, or `DELETE` SQL in a standard `_B` page.

## Initial Page Lifecycle

The normal lifecycle is:

```text
Constructor
  -> build shared Base_B shell
  -> page Load: load SQL from FW_RoleTables
  -> ensure SQL exists
  -> load registration and permissions
  -> if StartsEmptyOnInitialLoad() is True:
       show an empty result grid
       derive initial QBE fields from SQL schema
     otherwise:
       refresh the grid immediately
  -> page Shown: focus the browse grid
```

`FW_Base_B.StartsEmptyOnInitialLoad()` returns `True` by default. This means the first page display can show an empty result grid while QBE fields are created from the SQL schema. The first **Find** loads real rows and completes the grid-driven QBE setup.

## QBE Lifecycle

QBE fields are intentionally based on the final visible browse columns:

1. The result SQL is executed.
2. Standard hiding is applied:
   - `PK` is hidden.
   - Soft-delete maintenance columns are hidden from normal user field lists.
   - Registration hiding rules are applied by the shared Base_B path.
3. A saved layout is applied when the first result grid is built.
4. The final visible grid columns are used to populate QBE.
5. Internal aliases and soft-delete fields are excluded.

When the user clicks **Find**:

1. Base_B reads the QBE rows.
2. It validates field names, operators, and value types.
3. It stores the resulting dictionary in `currentFilters`.
4. It calls the shared SQL browse method.
5. It refreshes the grid without reloading the layout on every Find.

QBE values and operators are preserved if the QBE rows need to be rebuilt because the initial grid layout changes the visible-column set. This prevents the first Find from appearing to erase entered criteria.

An empty QBE is limited to the first 10 rows. Entering at least one criterion removes that empty-query limit.

## Layout Load Lifecycle

Layouts are stored in `dbo.FW_TableLayouts` and are scoped by:

```text
RegistrationID + UserID + PageName + TableName + LayoutType
```

For the last-used layout, the lookup is:

```text
RegistrationID
UserID
PageName = Me.GetType().Name
TableName = ResolveCurrentRoleFieldTableName()
LayoutType = LastUsed
LayoutName = Last Used
```

On the first real grid load, Base_B:

1. Ensures a registration default layout exists.
2. Looks for the current user's `LastUsed` layout.
3. If none applies, looks for the registration `Default` layout.
4. Verifies that the JSON has at least one matching current grid column.
5. Applies each matching column's:
   - `DisplayIndex`
   - `Visible`
   - `Width`
6. Reapplies the saved snapshot after grid auto-sizing and normalization so the persisted order remains authoritative.
7. Refreshes the columns manager and rebuilds QBE from the resulting visible columns.

Example layout JSON:

```json
[
  {"Key":"LastName","DisplayIndex":0,"Visible":true,"Width":184},
  {"Key":"PK","DisplayIndex":1,"Visible":false,"Width":131},
  {"Key":"MiddleName","DisplayIndex":2,"Visible":true,"Width":183},
  {"Key":"FirstName","DisplayIndex":3,"Visible":true,"Width":184}
]
```

The key must match the current grid column's `DataPropertyName` or column name. A valid JSON document can still be rejected if its keys do not match the current SQL result columns.

## Layout Save Lifecycle

Base_B saves `LastUsed` during `FormClosing`, covering both the Close button and the window X:

1. Capture the current grid snapshot.
2. Resolve the active registration, current user, page name, and table name.
3. Compare the current snapshot with the initial baseline.
4. Save through `DataAccess.UpsertTableLayout` when the layout changed or no previous `LastUsed` exists.

The snapshot comparison covers column order, visibility, and width. A change does not require a separate Save Layout button; closing the page persists the current layout automatically.

Named layouts and default layouts use the visible **Save Layout** workflow. They do not replace the automatic `LastUsed` save.

## Columns Manager Actions

The columns manager has intentionally separate actions:

- Click the column row text to select it.
- Use **Move Up** or **Move Down** to change display order.
- Click the checkbox itself to change visibility.
- Press **Space** to toggle visibility for the selected row.

Selecting a row must not toggle its checkbox. After a move or visibility change, Base_B updates the grid and marks the layout as changed for close-time persistence.

## Minimal New Page Wiring

A standard page normally needs only:

```vb
Namespace HelloWorld
    Public Class Customer_B
        Inherits FW_Base_B

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, "FW_Customer")
        End Sub
    End Class
End Namespace
```

Before using the page, configure the matching `FW_RoleTables` row:

```text
WindowOrPage = Customer_B
DB_Table     = FW_Customer
Table_SQL    = SELECT ... AS PK, ... FROM dbo.FW_Customer
RegistrationID = the target registration
ExposedToUser = the intended visibility value
```

Use the default page key when the physical class name matches `WindowOrPage`. Override `ResolveBrowsePageName()` only for a documented special-page contract.

## Optional Overrides

Use overrides only for concrete page-specific behavior:

- `BuildBrowseListingTitle`: custom title.
- `ResolveCurrentRoleFieldTableName`: custom table identity when the constructor table context is insufficient.
- `HandleDefaultCreateAction`, `HandleDefaultReadAction`, `HandleDefaultUpdateAction`, `HandleDefaultDeleteAction`: page-specific navigation through existing shared adapters.
- `NotifyBrowseSelectionChanged`: page-local controls that depend on the selected row.
- `ApplyPageSpecificLayout`: controls owned by the page that must be positioned after Base_B layout math.
- `StartsEmptyOnInitialLoad`: only when the page has a proven reason to load results immediately.
- `OnlyUseQbe`: only when the page is intentionally QBE-only.

Do not override `GetActiveBaseSql()` for a standard page. Do not duplicate QBE filtering, layout persistence, maintenance-key resolution, or page-local DML.

## Verification Checklist

For every new `_B` page:

- Confirm it inherits `FW_Base_B`.
- Confirm its file and class names match the page key.
- Confirm the constructor passes `UserContext`, `AccessProfile`, and the correct physical table.
- Confirm `FW_RoleTables.Table_SQL` is the source of browse SQL.
- Confirm SQL includes `AS PK` when maintenance actions need a key.
- Open with no role-table row and verify the fallback row is persisted correctly.
- Open with an existing SQL row and verify the grid uses that SQL.
- Confirm initial QBE fields exclude `PK` and soft-delete internals.
- Enter a QBE value before the first Find and verify it survives the first grid load.
- Change the registration selection and verify the existing grid rows clear immediately without a database refresh.
- Verify that only QBE Find reloads rows for the newly selected registration.
- Click QBE Clear and verify that criteria are removed and the grid clears without a database refresh.
- Move a column, change visibility, close, reopen, and verify `LastUsed` is applied.
- Verify the saved JSON keys match the current SQL result column names.
- Verify Move Up/Down selects a row without toggling its checkbox.
- Run the browse preflight and build the project.
- Manually test create, read, update, delete, cancel, missing SQL, missing PK, and permission-controlled paths as applicable.

## Keeping This Guide Current

This document is a living contract for the shared browse framework. Update it in the same change whenever work changes:

- The `FW_RoleTables` SQL contract or fallback behavior
- SQL control layout, including the rule that pages without a visible registration selector use the available space up to the Apply SQL button
- SQL textbox sizing: the shared SQL entry begins after the SQL label and ends 8 px before Apply SQL, keeping its left edge fixed
- Registration selector layout: visible registration controls are positioned responsively at the top-right of the browse page
- Admin action sizing: Apply SQL uses the same width as Close, and both remain right-aligned by the shared layout
- SQL display behavior, including showing the beginning of loaded SQL after assignment
- The order of SQL loading, grid binding, layout application, or QBE construction
- Layout JSON keys, save scope, load scope, or close-time persistence
- Shared layout fallback lookup, including LastUsed/Default preference and selection behavior
- Columns manager selection, visibility, ordering, or sizing behavior
- Required `_B` constructor arguments or supported overrides
- Missing SQL, missing `PK`, permissions, deleted-view, or registration behavior
- Registration combo data and labels, including the selected `RegistrationType` and `Registration` fallback

For every such change, update the affected explanation and the verification checklist, then run the browse preflight, build the project, and manually verify the changed workflow. If a page intentionally differs from this contract, document the exception in the page section or in the page's own guide rather than silently creating a second convention.

The shared registration ComboBox requires both `IsAppAdminSession() = True` and `ViewAllRecords = True`. Company Admin must never see this selector.
When both layouts exist, `LastUsed` always wins. `Default` is used only when no applicable `LastUsed` layout exists.
