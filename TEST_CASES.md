# Test Cases

Manual and scripted test cases, each with a stable ID so they can be referenced in commits, notes
and conversation. Automated coverage lives elsewhere and is listed at the bottom.

Add a case here when a defect is found, so the same thing can be checked again deliberately.

Status: `pass` with a date, `fail` with what happened, or `untested`.

---

## DB — Database connection and configuration

Run with `.\scripts\test-db-connection-failures.ps1 -Case <name>`. Nothing on the server changes;
each case is simulated through `HELLOWORLD_DB_CONNECTION` in the launched process.

| ID | Case | Expected | Status |
|---|---|---|---|
| DB-01 | `Healthy` | Straight to the login screen. No dialog, no perceptible delay. | pass 2026-08-31 — went straight to "Contacts Login" |
| DB-02 | `ServerDown` | **Database Unavailable** with a network/instance error, Retry and Cancel. Never a credentials prompt. | pass 2026-08-31 — window titled "Database Unavailable" |
| DB-03 | `Timeout` | Database Unavailable after roughly the 5s connect timeout — proves the probe does not stall startup. | pass 2026-08-31 — 9.7s, bounded by the give-up deadline. "Connecting" window shows at ~1.3s. Was 27s. |
| DB-04 | `WrongDatabase` | Database Unavailable, "Cannot open database". | pass 2026-08-31 |
| DB-05 | `WrongPassword` | Database Unavailable, "Login failed for user". | pass 2026-08-31 — no credentials prompt |
| DB-06 | `WrongUser` | Database Unavailable, "Login failed for user". | pass 2026-08-31 |
| DB-07 | `NotConfigured` | The **Database Configuration** dialog, not the unavailable message. Requires no saved `dbconfig.dat`. | pass 2026-08-31 — window titled "Database Configuration" |
| DB-08 | Retry recovers | On DB-02, start the server then choose Retry — reaches login without relaunching. | partial 2026-08-31 — Retry re-runs the check (6 times on a dead server); recovery after starting the server not yet tried |
| DB-09 | Save and reload | In the config dialog: wrong password → Test fails with the real SQL error, Save stays disabled. Correct password → Test succeeds, Save enables. Save → login. Relaunch bare exe → straight to login, no prompt. | pass 2026-08-31 — wrong password rejected with the real error and Save disabled; correct password and database saved; relaunch with no environment went straight to login |
| DB-10 | Edit invalidates test | After a successful Test, change any field — Save disables again. | untested |
| DB-11 | Database dropdown | Test success fills the list from `sys.databases`, keeping the current selection. | pass 2026-08-31 — 7 databases listed |
| DB-12 | Wrong database name only | Test with a bad database but valid credentials — falls back to `master`, populates the list, and says to pick one. | pass 2026-08-31 — master fallback populated the list and said to pick one |
| DB-13 | Clean up | Delete `%LOCALAPPDATA%\WXFramework\dbconfig.dat` — behavior returns to environment-only. | pass 2026-08-31 — no environment and no file gives the configuration dialog |
| DB-14 | `--configure-db` | Launching with the switch opens the dialog even when credentials already work. | pass 2026-08-31 |
| DB-15 | Dashboard tile | Application dashboard → Database Config → wrong password rejected, correct one opens the dialog. | untested |

---

## LOGIN — Authentication and role selection

Only four accounts can authenticate; the rest have no `PasswordHash`.

| ID | Case | Expected | Status |
|---|---|---|---|
| LOGIN-01 | Multi-role user (`grathke@sdcdev.net`, 5 roles) | `FW_RoleSelection` appears, titled ROLE SELECTION. | untested |
| LOGIN-02 | Single-role user (`jessica.anderson0015@example.com`) | No role selector — straight to the main menu. | untested |
| LOGIN-03 | Inactive user (`asawyer@asboinc.com`, `IsActive = N`) | Rejected with a clear message. | untested |
| LOGIN-04 | Other registration (`1@1.COM`, reg 2) | Logs in scoped to registration 2. | untested |
| LOGIN-05 | Role selector double-click | Double-clicking a row selects and continues, with no cell edit outline. | untested |
| LOGIN-06 | Role switch from the main menu | Changing role reconfigures menu access and the caption. | untested |

---

## U — Maintenance pages

Mostly verified on `Users_AppAdmin_U` during 2026-08-29/30. Re-run after changes to `01 FW_Base_U.vb`.

| ID | Case | Expected | Status |
|---|---|---|---|
| U-01 | Create, nothing touched | No red borders anywhere on a blank new record. | pass 2026-08-30 |
| U-02 | Enter an empty required field | Red on entry. | pass 2026-08-30 |
| U-03 | Leave it still empty | Stays red. | pass 2026-08-30 |
| U-04 | Type into it | Red clears, green focus ring returns. | pass 2026-08-30 |
| U-05 | Clear a field that had data | Red immediately, without tabbing away. | pass 2026-08-30 |
| U-06 | Save with blanks | One message listing fields **in tab order**; focus lands on the first. | pass 2026-08-30 |
| U-07 | Required combo | "Make a Selection" counts as empty; a real selection clears it. | pass 2026-08-30 |
| U-08 | Text-keyed combo | A non-numeric value such as `BR_RoleBased` is a valid selection and does not block save. | untested |
| U-09 | Hidden field row collapse | `Make_Invisible` on a field closes its row with no gap. | pass 2026-08-30 |
| U-10 | Two-column collapse | Hiding a left-column field pulls the left column up; the right column stays. | pass 2026-08-30 |
| U-11 | Hidden field value survives | Save with a field hidden — the stored value is unchanged, not blanked. | untested |
| U-12 | Change and change back, Cancel | No unsaved-changes prompt. | pass 2026-08-30 |
| U-13 | Real change, Cancel | Prompt appears; No keeps the page open. | untested |
| U-14 | Save conflict | Another user changes the record first — explicit overwrite choice, page stays open. | untested |
| U-15 | Password sentinel | Existing user shows `#####`; tabbing through it does not mark the page dirty. | pass 2026-08-30 |
| U-16 | Set a new password | Type over `#####`, save, log in with it, reopen — shows `#####` again. | untested |
| U-17 | Tab order focus | Selecting a row in the Tab Order panel focuses that field. | pass 2026-08-30 |
| U-18 | Tab order cancel | Reorder and untick, then Cancel or collapse — order and ticks revert. | pass 2026-08-30 |
| U-19 | Tab order OK | Changes persist, and reopening plus Cancel does not undo them. | pass 2026-08-30 |
| U-20 | Zip Coder visibility | Shown when Smarty embedded lookup is off, hidden when on. | pass 2026-08-30 |

---

## B — Browse pages

| ID | Case | Expected | Status |
|---|---|---|---|
| B-01 | SQL without `AS PK` | Missing-key warning; Read, Update and Delete hidden, Create still available. | untested |
| B-02 | Hide a column | It is absent from the grid, QBE and the columns manager - the column is removed from the result, not hidden. | pass 2026-08-31 |
| B-14 | Saved layout cannot resurrect a hidden field | A layout saved while the field was visible must not bring it back on first open. | pass 2026-08-31 |
| B-03 | QBE Find | Criteria filter the grid; the status line reports the count. | untested |
| B-04 | Empty QBE row cap | With no criteria, the result is capped and the status line says so. | untested |
| B-05 | Invalid QBE value | Text in a numeric field is reported and the search does not run. | untested |
| B-06 | Filters survive a refresh | Re-running Find does not clear what was typed. | untested |
| B-07 | Grid position after edit | After a `_U` edit the grid returns to the same row and scroll position. | untested |
| B-08 | Layout precedence | `LastUsed` wins over the shared `Default`. | untested |
| B-09 | Save My Layout | Reserved names refused; `*` names refused unless Company Admin. | untested |
| B-10 | Layout saved on close | Closing after a change writes `LastUsed`; closing unchanged does not churn. | untested |
| B-11 | Deleted view | Show Deleted lists only deleted rows; disabled when the result has no `DeletedFlag`. | untested |
| B-12 | Registration selector | Visible only with `ViewAllRecords`; seeded to the session; changing it re-scopes data and CRUD captions but **not** field captions. | untested |
| B-13 | No `FW_RoleTables` row | Opening creates the row with the right `WindowOrPage`, `DB_Table`, alias and `CreatedBy`. | untested |

---

## GEN — Page generation

Not yet exercised. The generator was corrected on 2026-08-31 and **its output has never been
compiled**.

| ID | Case | Expected | Status |
|---|---|---|---|
| GEN-01 | Generated pages compile | The emitted `_B` and `_U` build with no errors. | untested |
| GEN-02 | Browse SQL without `AS PK` | Generation is refused with a message naming the expected form. | untested |
| GEN-03 | Malformed lookup | An entry not matching `<Field> -> <Table>.<Value> displayed as <Display>` is reported. | untested |
| GEN-04 | Valid lookup | The field becomes a combo with a "Make a Selection" placeholder and saves the selected ID. | untested |
| GEN-05 | Cancel warns | Editing a generated page then cancelling prompts before discarding. | untested |
| GEN-06 | Save conflict | A conflict offers overwrite rather than a dead-end failure. | untested |
| GEN-07 | `FW_RoleTables` | A row is created, or an existing row's SQL updated when the table matches. | untested |
| GEN-08 | Dashboard icon | Added when `MenuCaller` is `Dashboard_Application`. `ICON_CATALOG.md` is **not** updated — still manual. | untested |

---

## Automated coverage

These run without a database and are not duplicated above.

| What | Command |
|---|---|
| Unit tests — permissions, display names, keyed hash, credential precedence, empty combo | `dotnet test .\tests\HelloWorld.Tests\HelloWorld.Tests.vbproj` |
| Browse contract checks | `powershell -File .\scripts\validate-browse-regression.ps1 -SkipBuild` |
| Maintenance contract checks | `powershell -File .\scripts\validate-maintenance-regression.ps1 -SkipBuild` |
