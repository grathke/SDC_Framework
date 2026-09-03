# Hello World VB.NET Windows Forms

A simple VB.NET Windows Forms application with one window and one button. Click the button to display a Hello World message.

## Build

Requires the .NET SDK.
Use `dotnet build` in the project directory.

## Run

Use `dotnet run` in the project directory.

## Browse Framework Change Checklist

Before merging any browse or grid behavior change (_B pages), verify all items below:

0. Preflight before editing:
	- Run VS Code task `Preflight Browse Framework`.
	- It runs static browse-contract checks without rebuilding, including QBE visibility, no Entity-specific QBE defaults, and double-click invoking Modify.

1. Shared-first implementation:
	- Place data behavior in shared data access helpers.
	- Place deleted-view button enablement rules in shared guard helpers.
	- Keep page-local logic for page-specific UX only.
2. Coverage check:
	- Confirm base browse and custom browse pages (Entity, Users, Roles) all use the shared behavior.
3. Build check:
	- `dotnet build .\SDC.Framework.vbproj -p:UseAppHost=false -p:OutputPath=bin\Debug\net10.0-windows-hotfix\`
4. Regression check:
	- Run `scripts\validate-browse-regression.ps1`
	- Run UI checks listed by the script.

## Cross-Cutting Change Gate

For changes involving shared behavior, schema, permissions, navigation, saving, soft delete, or concurrency, verify the full path before editing:

1. Database schema and migration
2. Shared base class
3. Models and record reconstruction/cloning
4. Data-access methods and affected adapters
5. Standard pages and one-off pages
6. Every menu/dashboard/page caller
7. Create, update, delete, cancel, failure, retry, and missing-schema behavior

After editing, run a cleanup scan for stale callers, duplicate implementations, obsolete validators, and documentation describing the previous behavior. Build the project and manually test the affected workflow before considering the change complete.

## Default Engineering Guardrails

These are baseline rules for this application, not optional task-specific suggestions:

- Enforce authorization at action and data-write boundaries; UI visibility is not security.
- Pass user, role, registration, and access-profile context through every menu, dashboard, page, adapter, and data operation.
- Use one transaction for related database writes and do not report success before commit.
- Preserve record identity and concurrency tokens through every load, clone, bind, validate, and rebuild path.
- Keep save conflicts visible and require an explicit user choice; never silently overwrite another user's work.
- Use shared soft-delete and audit policies for create, update, delete, restore, permission, and security changes.
- Require confirmation for destructive or irreversible actions.
- Validate the complete caller and model path, then build and manually test the real workflow.

## Validation Script

Run the shared browse validation script from the repository root:

`powershell -ExecutionPolicy Bypass -File .\scripts\validate-browse-regression.ps1`

Optional flags:

- Skip build and run static checks only:
  `powershell -ExecutionPolicy Bypass -File .\scripts\validate-browse-regression.ps1 -SkipBuild`

## Rowversion Migration

Run `sql\010_add_rowversion_to_fw_tables.sql` against the application database to add a `RowVersion` column to every `dbo.FW_*` table that does not already have one. The script is idempotent. Tables that already contain a SQL Server `rowversion` column under another name are reported and left unchanged.
