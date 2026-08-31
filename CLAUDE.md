# CLAUDE.md

Guidance for Claude Code when working in this repository.

This file is the single source of truth for the guardrails. There is no second copy to keep in
sync. `FRAMEWORK_NOTES.md` and the other companion documents below hold reference detail — how the
framework actually behaves — and are read on demand rather than loaded every session.

## Session Protocol

Hand-off happens on explicit markers so the user is never left guessing whether work is finished or
whether something is expected of them.

- **`READY TO RUN`** — end the response with this marker when the change is complete, builds, and is
  ready for manual testing. Follow it with the exact command to run and the specific things to verify,
  since the guardrails require manual workflow verification rather than compilation alone. Example:

  ```
  READY TO RUN — .\run-local.ps1
  Verify: Department_B opens from the Company dashboard, CRUD buttons match the role,
  save preserves RowVersion.
  ```

- **Do not launch the application until the user replies `run`.** They may prefer to run it themselves.
- **`BLOCKED`** — end with this marker instead when work cannot continue without a decision from the
  user. State what is needed and what is assumed if they choose nothing.
- **`What's Next`** — end with this heading whenever the response hands control back and something
  is expected from the user: a decision, a test result, an answer, or a choice between options.
  List the open items as a short numbered list, most immediate first, so nothing is left buried in
  prose. Include threads still open from earlier in the session, not only the current one. Omit it
  when genuinely nothing is pending — a finished answer with no follow-up needs no list.
- Finish every part of the work that is not blocked before reporting either marker, and say explicitly
  what was left out and why.
- Read the full relevant file before changing it. Never apply a generic solution without first
  understanding the existing code.
- Before a change that touches multiple files, affects working behavior, or is more than a small
  targeted edit: describe the plan and get confirmation before proceeding.
- When something looks wrong, investigate the actual page, control or data first. If it is still
  unclear, ask. Do not presume intent or build a theory on an unconfirmed premise.

### Trigger phrases

- **"Make it so"** — proceed with the proposal just made, without restating it or asking again.
  Treat it as a full go-ahead, including for work that would otherwise wait for confirmation.
  It does **not** waive the guardrails that require their own approval: database view changes,
  physical deletes, creating roles or permissions, and restore points still need an explicit ask.
  If more than one proposal is open, do not guess — list what is pending and ask which one.
- **"run"** — launch the application, in reply to a `READY TO RUN` marker.
- **"run duplicate check first"** — apply the Consolidation Guardrail before making changes.

## End Of Day Protection Check (Required)

When the user signals they are finished — "I'm finished for the day", "good night", "that's it for
today", "signing off", or anything else that plainly means the session is ending — run this check
before saying goodnight. Do not wait to be asked.

Report only what actually needs attention, in a few lines. If nothing needs attention, reply with
exactly **`Everything Has Been Saved`** and nothing else — no list of checks that passed, no
summary of the day. Use that phrase only when every item below is genuinely clear; if anything
needs attention, say what it is instead.

1. **Uncommitted work** — run `git status --short`. If the working tree is dirty, say how many
   files and offer to commit. Branch first if on the default branch.
2. **Broken state** — if the last build failed, or a change was left half-finished, say so plainly
   and name the file. Never end the day implying work is complete when it is not.
3. **Unverified work** — list anything built but never exercised in the running application, so it
   is not mistaken for tested.
4. **Off-machine backup** — while the repository has no remote, commits protect against bad edits
   but not against losing the machine. Say this once when there is new committed work and no
   remote; do not repeat it every night.
5. **Restore points** — only mention these if a broad or high-risk change is planned for next time.
   They are a pre-change safety net, never an end-of-day ritual.

Ask before committing. Do not commit, push, or create restore points on the strength of a goodnight
alone.

## Project

VB.NET Windows Forms line-of-business application targeting `net10.0-windows` (.NET SDK 10 preview).
Source files live flat in the repository root.

Key areas:

- Page framework: `01 FW_Base_B.vb` (browse pages) and `01 FW_Base_U.vb` (maintenance pages).
- Data access: `DataAccess.vb`, plus `HelpDeskDataAccess.vb` and `MessagingDataAccess.vb`.
- Models: `Models.vb`.
- Entry point and shell: `Program.vb`, `LoginForm.vb`, `MainMenu.vb`.
- Naming convention: `*_B` = browse page, `*_U` = maintenance page.
- SQL migrations: `sql/`. Validation and restore-point scripts: `scripts/`.

## Companion Documents

- `FRAMEWORK_NOTES.md` — how the menu shell, browse pages, maintenance pages, required-field
  styling and layout persistence actually work. Read before changing framework behavior.
- `ICON_CATALOG.md` — the repository icon catalog required by the Action Icon Guardrail.
- `TEST_CASES.md` — manual and scripted test cases with stable IDs. Add a case when a defect is
  found, and record pass or fail with a date.
- `BASE_B_QBE_LAYOUT_GUIDE.md`, `COMBO_CHECKLIST.md` — existing area-specific guides.
- `.github/new-page-request-template.md` and `.github/new-page-request-manual.md` — the page
  request template and how to interpret a filled-in one.

## Naming Conventions (Required)

- Database tables that belong to the framework are prefixed `FW_`, for example `dbo.FW_Entity`.
- Pages that belong to the framework are prefixed `FW_`, for example `FW_Registration_B`.
- Application-specific tables and pages built on top of the framework do **not** take the prefix.
- `_B` = browse page: a grid listing rows, from which a record is selected to view, edit or delete.
- `_U` = maintenance page: create, update, read and delete of a single record.
- `_B` and `_U` pages are normally created as a pair against the same underlying table.
- Display text is derived from names by `DisplayNameFormatter.ToDisplayName`. Do not add a second
  formatter; extend the acronym list in `DisplayNameFormatter.vb` instead.
- Control naming, which the framework depends on to map controls to columns: `Label_<FieldName>`,
  `TextBox_<FieldName>`, `ComboBox_<FieldName>`, where `<FieldName>` is the database column name.

Known exceptions in the current codebase, to be resolved rather than copied:

- `Entity_B/_U`, `Roles_B/_U`, `Users_AppAdmin_B/_U` and `PageGeneration_B/_U` are framework pages
  without the `FW_` prefix.
- Browse-only pages with no `_U` partner: `FW_AuditTrail_B`, `FW_HD_Admin_B`,
  `FW_HD_AdminDashboard_B`, `FW_UserAccessDiagnostic_B`, `FW_UserAccessExplanation_B`.
- `FW_HD_Issues_B` and `FW_HD_Issues_Support_B` share a single `FW_HD_Issues_U`.

## Build, run, validate

Build:

```
dotnet build .\HelloWorld.vbproj
```

Hotfix/side build that does not disturb the normal output:

```
dotnet build .\HelloWorld.vbproj -p:UseAppHost=false -p:OutputPath=bin\Debug\net10.0-windows-hotfix\
```

Run: `powershell -ExecutionPolicy Bypass -File .\run-local.ps1` (VS Code task `Run HelloWorld`).
With database: `.\run-with-db.ps1`.

### Database configuration

No credentials are compiled into the application. `DataAccess.BuildConnectionString` reads them
from the environment:

| Variable | Default |
|---|---|
| `HELLOWORLD_DB_CONNECTION` | none — a full connection string; takes precedence over everything below |
| `HELLOWORLD_DB_PASSWORD` | none — **required**, and the one the startup check tests |
| `HELLOWORLD_DB_SERVER` | `BEELINK` |
| `HELLOWORLD_DB_USER` | `sa` |
| `HELLOWORLD_DB_NAME` | `WX_Framework` |
| `HELLOWORLD_DB_ENCRYPT` | `False` |
| `HELLOWORLD_DB_TRUST_SERVER_CERT` | `True` |

With nothing configured, `Program.vb` shows "Database Configuration Required" and exits with code 2
before the login screen. `run-local.ps1` supplies these for local development and is in
`.gitignore`, so it does not travel with the repository — `run-local.ps1.example` documents what to
set. Away from this machine, prefer `HELLOWORLD_DB_CONNECTION` so a wrong server name cannot be
picked up silently from a default.

Browse regression validation:

```
powershell -ExecutionPolicy Bypass -File .\scripts\validate-browse-regression.ps1
```

Static checks only, no build (VS Code task `Preflight Browse Framework`):

```
powershell -ExecutionPolicy Bypass -File .\scripts\validate-browse-regression.ps1 -SkipBuild
```

Maintenance regression validation, the `_U` counterpart (VS Code task
`Preflight Maintenance Framework`; add `-SkipBuild` for static checks only):

```
powershell -ExecutionPolicy Bypass -File .\scripts\validate-maintenance-regression.ps1
```

Unit tests (VS Code task `Run Unit Tests`). No database and no message loop, so they run in about
a second:

```
dotnet test .\tests\HelloWorld.Tests\HelloWorld.Tests.vbproj
```

They cover logic only — permissions, display-name formatting, the keyed hash, credential
precedence, the empty-combo test. Page behavior is **not** covered: every `_B` and `_U` constructor
reaches the database, and `Control.Visible` reports `False` until a form is shown, so UI rules
cannot be asserted headlessly. Those stay with the manual checklists the validation scripts print.

Restore points (VS Code tasks `Create Base_B Restore Point` / `Create Base_U Restore Point`):

```
powershell -ExecutionPolicy Bypass -File .\scripts\create-base-b-restore-point.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\create-base-u-restore-point.ps1
```

Restore points are written to `restore-points/`.

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

## Restore Point Decision Guardrail (Required)

- The user controls explicit restore-point requests.
- Before a broad, cross-cutting, or high-risk change, use engineering judgment to decide whether a restore point is warranted.
- If a restore point is warranted, pause before editing and explain why, identify every file in scope, and ask the user for approval.
- Do not create an additional restore point silently or after a failed change merely because the work became difficult.
- Prefer the last known-good restore point unless the user approves a new one or the current restore point does not cover the files being changed.
- After approval, create the restore point before the first substantive edit and report its location.

## Protected Areas (Required)

- Do not change the login screen or the main menu screen unless the user explicitly requests it and explains the reason for the change.
- If a change to the login screen or main menu is requested, first confirm the intent and describe the expected impact before editing those files.
- Preserve the current working login behavior and the current menu structure/layout unless the user approves a specific change.
- Keep any future edits to the login flow and main menu minimal and focused on the requested issue.
- When working on related features, avoid unnecessary visual or behavioral changes to the login screen and main menu.
- Do not change database views, view definitions, or view-based logic unless the user explicitly requests it and explains the reason for the change.
- If a change to a database view is requested, first confirm the intended impact and preserve the existing view contract unless the user approves a specific change.

## Application-Wide Change Gate (Required)

- For any change that affects shared behavior, database schema, permissions, navigation, soft delete, concurrency, saving, or base classes, trace the complete contract before editing.
- The trace must cover: database schema and migrations; shared base classes; models; data-access methods; adapters; standard pages; one-off pages; and every caller/entry point.
- Write one falsifiable application-wide hypothesis and one discriminating check before the first substantive edit.
- Build a behavior matrix before implementation when the change crosses more than one layer. Include create, update, delete, cancel, missing-schema support, conflict/failure, retry, and special-page paths where applicable.
- Choose one owner for shared behavior. Page code may supply page-specific data but must not create a second implementation of shared policy.
- After editing, perform a cleanup scan for stale callers, duplicate paths, obsolete validators, model reconstruction paths, and documentation that still describes the old contract.
- Do not declare a cross-cutting change complete until the application builds, focused checks pass, all affected callers are searched, and the actual user workflow is manually verified.
- If a special page intentionally does not follow the shared contract, document it explicitly and add a validation check for that exception.

## New Page Regression Guardrail (Required)

- Every new `_B` or `_U` page requires a regression pass before completion.
- Verify inheritance, naming, constructor context, table and RegistrationID scope, Base_B SQL/`AS PK` behavior, Base_U identity and RowVersion preservation, all callers/action icons, permissions, and missing-schema behavior.
- Check create, update, delete, cancel, conflict, retry, and special-page paths where applicable.
- Search for duplicate or page-local paths that bypass shared behavior.
- Build the project, run focused regression checks, search all affected callers, and manually verify the actual page workflow.
- Run `scripts\validate-browse-regression.ps1` for a `_B` page and `scripts\validate-maintenance-regression.ps1` for a `_U` page, then work through the manual checklist each one prints.
- Run `dotnet test .\tests\HelloWorld.Tests\HelloWorld.Tests.vbproj` whenever the change touches permissions, display-name formatting, hashing, credential resolution, or the empty-combo test. Passing tests do not substitute for the manual workflow check.
- Do not declare a new page complete from compilation alone.
- For every new standard `_B` page, verify the no-row `FW_RoleTables` path against the actual database: opening the page must create the correct `WindowOrPage`, `DB_Table`, friendly alias, session `CreatedBy`, and PK-safe fallback SQL.

## QBE Visibility Guardrail (Required)

- Before the first substantive edit to `01 FW_Base_B.vb`, run the `Create Base_B Restore Point` task. Do not edit Base_B until its timestamped restore point is created.
- QBE fields must be derived only from visible browse-grid columns after all standard hiding and saved-layout rules have been applied.
- Internal maintenance aliases, including `PK`, must never appear in the browse grid, QBE, columns manager, or user-facing field lists.
- A real ID column explicitly selected by page SQL, such as `IssueID`, is distinct from the internal `PK` alias and may appear when visible.
- For Start Empty pages, where QBE is derived from SQL schema before a grid exists, exclude `PK`, soft-delete fields, and other internal aliases.
- Any change to Base_B grid/QBE loading must run the browse regression script and manually verify that hiding a browse column also removes it from QBE.

See also `BASE_B_QBE_LAYOUT_GUIDE.md`.

## Copied Page Regression Guardrail (Required)

- A copied page is a new page, not a shortcut.
- Verify the new file/class name, base inheritance, constructor context, table name, SQL source, registration/user scope, permissions, action handlers, callers, titles, and model paths independently.
- Remove copied page-specific overrides and stale references unless explicitly required by the new page contract.
- Run the New Page Regression Guardrail for the copied page.
- Add a validation check proving the copied page does not inherit from or call the source page.

## Consolidation Guardrail (Required)

- Before implementing any fix or feature, check related files/classes for duplicate or near-duplicate logic.
- If similar logic exists in 2 or more places, stop and ask: "I found repeated logic in [files]. Do you want me to consolidate into a shared function/class now?"
- If the user says yes, consolidate first, then apply the requested change.
- If the user says no, implement the change minimally and explicitly note the duplication risk.
- Do not introduce a new duplicate path when an existing shared helper/class can be extended.
- For UI behavior parity across pages/forms, prefer shared controller/helper classes over page-local handlers.

## Existing Owner Gate (Required)

- Before adding any handler, override, callback, helper, or page-local lookup, identify the existing method, command, or control event that owns the requested behavior.
- If an existing command already performs the requested workflow, invoke that command or its click event. Do not create a second path that repeats permissions, selection, PK resolution, validation, saving, navigation, or refresh behavior.
- A page-specific override is allowed only when the existing owner cannot satisfy a concrete page contract. State that contract and the reason before editing.
- For browse pages, double-click must invoke the visible, enabled command that performs the equivalent user action. It must not introduce a separate maintenance-key lookup or maintenance-open path.
- Before completing a change, search for any new duplicate handler, override, callback, or model reconstruction path and remove it unless the documented exception requires it.

## Action Icon Guardrail (Required)

- Every actionable icon on a dashboard, menu, toolbar, or page must have a unique ActionKey, a registered caption, an icon file, a target page or command, and an explicit click handler.
- Before adding the icon, verify that the target class and constructor exist and follow the page naming convention (`*_B` for browse pages and `*_U` for maintenance pages).
- Target page constructors must accept the active `UserContext` or session context and an optional `AccessProfile` when the target uses role-based access.
- Click handlers must pass the owning page's existing `currentUser` and `accessProfile` to the target page. Do not create the target with a parameterless constructor when that would discard access context.
- Use this pattern for role-aware pages:
	`Using page As New Target_B(currentUser, accessProfile)`
- Decorative or status-only icons are excluded; this guardrail applies only when an icon performs navigation or a command.
- After adding or changing an actionable icon, search all target-page callers for profile-dropping calls, build the project, open the icon manually, and verify the target title and permission-controlled controls.
- Update the repository icon catalog in the same change, including ActionKey, placement, target, icon file, visibility rule, and click behavior.

## Security And Access Guardrail (Required)

- UI visibility is not authorization. Every protected command and database write must enforce access at its action or data boundary.
- Any page opened from a menu or dashboard must receive the active user, role, registration, and access profile context.
- Role or registration changes must invalidate affected access and metadata caches before another protected action is evaluated.
- Never use a demo profile, parameterless page constructor, or client-side visibility rule as the only protection for a real database operation.

## Transaction Guardrail (Required)

- Related writes must share one explicit database transaction. This includes parent/child inserts, permission/detail changes, delete plus audit, and multi-table saves.
- A transaction must commit only after every required write succeeds and must roll back on any failure.
- Do not report success or close a page before the transaction result is known.
- Existing immediate-write one-off pages must document each write boundary and must not be silently treated as transactional.

## Save And Model Contract Guardrail (Required)

- Before the first substantive edit to `01 FW_Base_U.vb`, run the `Create Base_U Restore Point` task. Do not edit Base_U until its timestamped restore point is created.
- Standard `_U` pages must use the shared save result contract and must distinguish success, conflict, deleted record, unavailable concurrency protection, and failure.
- Record identity, registration context, and concurrency tokens must survive every load, clone, form-bind, validation, and record-rebuild path.
- Model and data-reader nullability must match the database contract. A nullable database column must map to a nullable model property and safe `DBNull` conversion; never make it required merely because a current page does not display it.
- Every new-record workflow must load successfully before its first update. In particular, update audit fields may be null until an actual update occurs and must not block maintenance-page loading.
- A save conflict must keep the page open and require an explicit reload, cancel, or overwrite decision. Never silently apply last-saved-wins.
- A page must not close until its save has succeeded.

## Soft Delete And Audit Guardrail (Required)

- Delete, restore, normal view, deleted view, and audit behavior must use shared policy helpers.
- Physical deletion requires an explicit reason and approval; soft-delete-capable tables must use the shared DeletedFlag path.
- Create, update, delete, restore, permission, and security changes must record actor, timestamp, table, record key, operation, before state, after state, and result.
- Audit behavior and failure policy must be identified before implementation; do not add an audit call that can leave the business write in an unknown state.

## Destructive Action And Verification Guardrail (Required)

- Delete, remove permission, restore, role changes, schema synchronization, and overwrite-after-conflict require a confirmation that identifies the target and impact.
- Every cross-cutting change must include a behavior matrix for create, update, delete, cancel, failure, retry, missing schema, and special-page paths where applicable.
- After editing, search all affected callers and model reconstruction paths, run focused validation, build the project, and manually verify the actual workflow.
- Do not declare completion from compilation alone when the change affects user-facing behavior or database writes.

## Browse Framework Change Checklist

Before merging any browse or grid behavior change (`_B` pages), verify all items below:

0. Preflight before editing: run the `Preflight Browse Framework` task. It runs static browse-contract
   checks without rebuilding, including QBE visibility, no Entity-specific QBE defaults, and
   double-click invoking Modify.
1. Shared-first implementation: place data behavior in shared data access helpers; place deleted-view
   button enablement rules in shared guard helpers; keep page-local logic for page-specific UX only.
2. Coverage check: confirm base browse and custom browse pages (Entity, Users, Roles) all use the
   shared behavior.
3. Build check (see hotfix build command above).
4. Regression check: run `scripts\validate-browse-regression.ps1` and the UI checks it lists.

## Trigger Phrase

- If the user says "run duplicate check first", treat it as a hard request to run the consolidation guardrail before making changes.
- On that trigger, check for duplicate or near-duplicate logic first and ask whether to consolidate if repeated logic exists.
- Before any medium to large change, ask the user if they want me to run the duplicate check first.
