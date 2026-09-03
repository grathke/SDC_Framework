# New _B and _U Page Request Manual

This manual explains how to complete `.github/new-page-request-template.md` when requesting a new paired browse (`_B`) and maintenance (`_U`) page.

## Purpose

The request template supplies the page-specific information needed to create a standard pair of pages while preserving the shared framework behavior.

A normal request creates:

- One `_B` browse page based on `FW_Base_B`.
- One `_U` maintenance page based on `FW_Base_U`.
- Page-specific table and SQL ownership.
- A `FW_Pages` metadata record when the matching record is missing.

A normal request does not create roles or permissions.

## Required Page Identity

### Name to use for both pages

Supply one base name for the paired pages:

```text
Name to use for both pages:
Answer: Department
```

The prompt should simply say `No spaces.` If spaces are entered, remove them automatically and continue to the next question.

The normal result is:

```text
Department_B
Department_U
```

### Same base name

Answer `Yes` unless there is a specific reason for different names:

```text
Use the same base name for the Browse and Maintenance pages:
Answer: Yes
```

When the answer is `No`, provide and explain the separate base names before implementation.

### Browse page name

Example:

```text
Department_B
```

The name must end in `_B` and should match the page's class and file name.

### Maintenance page name

Example:

```text
Department_U
```

The name must end in `_U` and should match the page's class and file name.

### Underlying table

Example:

```text
dbo.FW_Department
```

This is the table that the `_U` page updates. The page must explicitly return the table name through `GetTableNameOverride()`.

If the SQL has one obvious base table, the table may be inferred. For joins, views, procedures, aggregates, or multiple possible update tables, supply the table explicitly.

### RegistrationID

Example:

```text
3
```

This must be an explicit numeric value supplied by the requester.

Do not use:

```text
Active session RegistrationID
```

The supplied value controls the page context and the matching `FW_Pages.RegistrationID` record. If it is missing or ambiguous, stop and ask before creating files or database metadata.

### Friendly name

Normally write:

```text
Derived automatically from the table name
```

The name is derived by removing `FW_`, splitting underscores, and formatting PascalCase words.

Examples:

```text
FW_Gender       -> Gender
FW_UserRoles    -> User Roles
FW_HD_Issues    -> HD Issues
```

The displayed page caption is determined at runtime. Do not provide a friendly-name answer.

## Browse SQL

The SQL must expose the maintenance key using an explicit alias named `PK`.

Example:

```sql
SELECT
    d.DepartmentID AS PK,
    d.DepartmentID,
    d.RegistrationID,
    d.DepartmentName,
    d.Description,
    d.IsActive,
    d.DeletedFlag,
    d.RowVersion
FROM dbo.FW_Department AS d
WHERE d.RegistrationID = @RegistrationID
  AND ISNULL(d.DeletedFlag, 0) = 0
ORDER BY d.DepartmentName;
```

### PK requirement

This is required:

```sql
d.DepartmentID AS PK
```

The internal `PK` alias is used to open maintenance pages. It is hidden from user-facing browse columns and QBE fields.

Do not rely on a fallback such as `ID`, `UserID`, or `RegistrationID` as the maintenance key.

### RegistrationID in SQL

For a registration-scoped table, include the registration column and filter:

```sql
d.RegistrationID,
...
WHERE d.RegistrationID = @RegistrationID
```

The page request still must provide the explicit numeric RegistrationID. Include `RegistrationID` and its filter in the SQL only when the table requires registration filtering. The framework uses the supplied value for the page context and `FW_Pages` metadata.

### DeletedFlag and RowVersion

Do not include `DeletedFlag` or `RowVersion` in the supplied browse SQL. The shared framework handles soft-delete state and concurrency tokens automatically.

## Maintenance Page Field Options

Keep optional prompts in the request when pasting the template. Answer them explicitly instead of deleting them:

```text
Answer: Not specified
Answer: None
Answer: Not applicable
```

These answers mean standard/default behavior, no special value, or that the option does not apply. Example placeholder text such as `<list fields>` is not treated as a real value.

### Editable fields

List the fields the user may change:

```text
DepartmentName, Description, IsActive
```

### Read-only fields

List keys, ownership fields, audit fields, and system fields:

```text
DepartmentID, RegistrationID, DeletedFlag, RowVersion, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn
```

### Lookup fields

Use this format:

```text
ManagerID -> FW_Users.UserID displayed as FirstLast
```

Lookup combos use the shared placeholder and selected-ID helpers.

### Admin Required fields

List fields that administrators must complete:

```text
DepartmentName
```

Or write:

```text
Database-driven rules
```

This is intentionally labeled `Admin Required fields` so it is not confused with ordinary user-facing required-field rules.

## Create Behavior Options

### Standard behavior

Use when no special workflow is needed:

```text
Standard behavior
```

The page opens `_U` in Create mode, applies the supplied RegistrationID, validates the form, saves only after the user confirms, writes audit information, and refreshes the browse grid.

### Create is not allowed

Use for reference tables maintained elsewhere:

```text
Create is not allowed
```

The Create action is disabled or hidden.

### Standard behavior with defaults

Use when fields should start with defaults:

```text
Standard behavior with defaults:
    IsActive = True
    SortOrder = 0
```

Defaults pre-fill the form but do not save automatically.

### Fixed RegistrationID

Use when the registration must not be changed:

```text
RegistrationID is fixed to 3 and read-only.
```

The supplied RegistrationID remains authoritative.

### Generated value

Use when a value needs a specific generation rule:

```text
Generate DepartmentCode using DEPT-0001 format.
```

Supply the exact rule. Do not expect the format to be guessed.

### Special validation

Use for additional business rules:

```text
DepartmentName must be unique within RegistrationID.
```

### Parent-child creation

Use only when a parent must be selected or created first:

```text
Parent table: dbo.FW_Company
Parent key: CompanyID
Child field: CompanyID
```

This may require transaction handling and additional workflow details.

## Update Behavior Options

### Standard behavior

Loads the selected record, preserves RowVersion, validates changes, saves with optimistic concurrency, writes audit information, and refreshes the same browse row.

### Update is not allowed

Disables the Update action while allowing read-only access.

### Special validation

Example:

```text
Email must remain unique within RegistrationID.
```

### Multi-table update

List every table and explain the relationship. Related writes must use one explicit transaction.

## Delete Behavior Options

### Standard soft delete behavior

Use for tables supporting `DeletedFlag`:

```text
Standard soft delete behavior
```

The record is marked deleted, not physically removed. Normal view hides it, deleted view shows it, and restore uses shared policy.

### Delete is not allowed

Disables Delete and leaves the record intact.

### Physical delete

Do not request this casually. Supply the reason, approval requirement, audit behavior, and confirmation wording. Physical deletion requires explicit confirmation and is not the default.

## `FW_Pages` Behavior

The metadata key is the page and registration combination:

```text
WindowOrPage   = Department_B
RegistrationID = 3
```

If that matching record does not exist, create it with:

```text
WindowOrPage   = Department_B
RegistrationID = 3
DB_Table       = FW_Department
Table_Alias    = Department
SQL            = supplied browse SQL
CreatedBy      = active session user
```

If it already exists, inspect it before changing it. Do not silently replace an existing user-configured SQL definition without confirming the intended change.

`FW_Pages` metadata does not grant access. It is separate from roles, role details, role fields, permissions, and access profiles.

## What Is Not Created Automatically

Do not create any of these unless the request explicitly asks for them:

- Roles.
- Role details.
- Role fields.
- Permission records.
- Access profiles.

## What Happens During Implementation

1. Read the supplied request.
2. Validate the page names, table, SQL, explicit RegistrationID, and PK alias.
3. Check whether SQL table inference is unambiguous.
4. Inspect similar existing pages and callers.
5. Create a restore point before changing shared framework files.
6. Create the `_B` and `_U` pages using existing patterns.
7. Wire standard CRUD actions and page-specific behavior.
8. Create or verify the matching `FW_Pages` record.
9. Build the project.
10. Run browse regression validation.
11. Verify the metadata record against the actual database.
12. Launch the application and manually test the workflow.
13. Create a good-baseline restore point only after runtime success is confirmed.

## When I Must Stop and Ask

I must stop before editing when:

- RegistrationID is missing, non-numeric, or ambiguous.
- The SQL does not expose a clear `PK` alias.
- The SQL has multiple possible maintenance tables.
- The requested page name conflicts with an existing page.
- An existing `FW_Pages` record has conflicting SQL or table metadata.
- The request would require roles or permissions that were not explicitly requested.
- A multi-table save requires transaction behavior that has not been specified.
- A destructive physical delete is requested without explicit confirmation and audit requirements.

## Minimal Valid Request

```text
Browse page name: Department_B
Maintenance page name: Department_U
Underlying table: dbo.FW_Department
RegistrationID: 3
Friendly name: Derived automatically from the table name

Browse SQL:
SELECT
    d.DepartmentID AS PK,
    d.DepartmentID,
    d.RegistrationID,
    d.DepartmentName,
    d.Description,
    d.IsActive,
    d.DeletedFlag,
    d.RowVersion
FROM dbo.FW_Department AS d
WHERE d.RegistrationID = @RegistrationID
  AND ISNULL(d.DeletedFlag, 0) = 0
ORDER BY d.DepartmentName;

Editable fields: DepartmentName, Description, IsActive
Read-only fields: DepartmentID, RegistrationID, DeletedFlag, RowVersion
Lookup fields: None
Admin Required fields: DepartmentName
Special Create behavior: Standard behavior
Special Update behavior: Standard behavior
Special Delete behavior: Standard soft delete behavior
Menu or caller: None

Do not create roles, role details, role fields, permissions, or access profiles.
```