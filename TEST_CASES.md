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
| U-14 | Save conflict | Another user changes the record first — explicit overwrite choice, page stays open. | pass 2026-08-31 |
| U-21 | Save onto a deleted record | One uppercase message naming who deleted it and when; page closes; no overwrite offered; grid returns without the record. | pass 2026-08-31 |
| U-15 | Password sentinel | Existing user shows `#####`; tabbing through it does not mark the page dirty. | pass 2026-08-30 |
| U-16 | Set a new password | Type over `#####`, save, log in with it, reopen — shows `#####` again. | untested |
| U-17 | Tab order focus | Selecting a row in the Tab Order panel focuses that field. | pass 2026-08-30 |
| U-18 | Tab order cancel | Reorder and untick, then Cancel or collapse — order and ticks revert. | pass 2026-08-30 |
| U-19 | Tab order OK | Changes persist, and reopening plus Cancel does not undo them. | pass 2026-08-30 |
| U-20 | Zip Coder visibility | Shown when Smarty embedded lookup is off, hidden when on. | pass 2026-08-30 |
| U-21 | Focus border in a flow row | A green border on a control inside a `FlowLayoutPanel` sits behind that control, adds no gap, and leaves the row in its authored left-to-right order. | untested |
| U-22 | Permission on a control added since the page was written | Set a field required by permission on any `_U` page and open it: the yellow label and override caption apply immediately, with no Enum press. Field permissions are derived from the control name, so no stored enumeration has to be refreshed. | pass 2026-09-01 — `FW_Users.Address1` on `Users_AppAdmin_U`, inert since July, showed `Address X` and the yellow |
| U-23 | Renamed column leaves no silent gap | Rename a column and re-open its page. Any `FW_RoleFields` row still naming the old column stops matching, and a control still named for it is reported rather than ignored. | pass 2026-09-01 — the four `FW_Users.Address` rows were found by query; the report path is untested against a live mismatch |
| U-24 | Control mapping to no column | A field-shaped control whose name is not a column shows `N/A` in place of the control with its label intact, OK is disabled with the tooltip `SAVE IS NOT AVAILABLE, FIELD ON PAGE NOT MAPPED`, and an admin session also gets a copyable report. | untested — no page currently has an undeclared mismatch, so this needs one introduced deliberately |
| U-25 | Intentional unbound control | A control declared with `DeclareUnboundField` is not reported and does not disable saving. Declaring one without a reason throws. | pass 2026-09-01 — `TextBox_Response` on `FW_HD_Issues_U` |
| U-26 | Case-differing table declaration | A page declaring its table in different casing from the stored rows still resolves its permissions. | pass 2026-09-01 — `EntityX_U` declares `FW_ENTITY`, rows say `FW_Entity`; hidden Address1 and required First X both held |
| U-28 | Required border on hover | Hovering an empty required field shows the red border; moving away clears it. Hovering a required field that has a value shows nothing — red never appears on a field that is filled. | pass 2026-09-01 |
| U-29 | Hover cannot clear an earned border | Visit a required field and leave it empty, then hover it and move away: the border stays. Hover and visit are tracked separately because a hover ends and a visit does not. Filling the field clears it either way. | pass 2026-09-01 |
| U-27 | Silent `Controls.Find` miss | A page looking up a control by a name that no longer exists must not fail silently. `Controls.Find` returns empty and most callers just return, so a renamed control leaves layout or captions quietly unapplied. | untested — found by inspection on 2026-09-01, when `Users_AppAdmin_U` had been positioning `Label_Address` since the rename to `Address1` |

---

## B — Browse pages

| ID | Case | Expected | Status |
|---|---|---|---|
| B-01 | SQL without `AS PK` | Missing-key warning; Read, Update and Delete hidden, Create still available. | untested |
| B-02 | Hide a column | It is absent from the grid, QBE and the columns manager - the column is removed from the result, not hidden. | pass 2026-08-31 |
| B-14 | Saved layout cannot resurrect a hidden field | A layout saved while the field was visible must not bring it back on first open. | pass 2026-08-31 |
| B-15 | Column order drives QBE order | Dragging a column, or reordering in the panel, puts QBE rows in the same order as the grid. Reset restores both together. | pass 2026-08-31 |
| B-16 | Columns panel defers to OK | Ticking and reordering in the panel change nothing until OK; Cancel leaves the grid untouched. | untested |
| B-17 | Delete button soft-deletes | Confirmation names the user; the record leaves the grid and DeletedFlag, DeletedBy and DeletedOn are set. | pass 2026-08-31 |
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
| GEN-01 | Generated pages compile | The emitted `_B` and `_U` build with no errors. | pass 2026-08-31 — EntityX_B/_U generated to the workspace, project builds 0 errors |
| GEN-02 | Browse SQL without `AS PK` | Generation is refused with a message naming the expected form. | untested |
| GEN-03 | Malformed lookup | An entry not matching `<Field> -> <Table>.<Value> displayed as <Display>`, optionally followed by `filtered by registration` or `not filtered by registration`, is reported. | untested — the picker composes the format now, so producing a malformed one means editing the stored value by hand |
| GEN-04 | Valid lookup | The field becomes a combo listing the display column and saving the value column. | pass 2026-09-01 — `GenderID -> FW_GENDER.ID displayed as GenderDescription`, generated and opened. **Superseded by GEN-21:** this exact spec went stale when the key was renamed. |
| GEN-05 | Cancel warns | Editing a generated page then cancelling prompts before discarding. | untested |
| GEN-06 | Save conflict | A conflict offers overwrite rather than a dead-end failure. | untested |
| GEN-07 | `FW_RoleTables` | A row is created, or an existing row's SQL updated when the table matches. | untested |
| GEN-08 | Dashboard icon | Added when `MenuCaller` is `Dashboard_Application`. `ICON_CATALOG.md` is **not** updated — still manual. | pass 2026-08-31 — GeneratedPageActionKey_EntityX_B present |
| GEN-09 | Preview Code writes nothing | `Preview Code` shows both sources and the Summary, and no page file, `FW_RoleTables` row or dashboard icon changes. | untested |
| GEN-10 | Compile Check passes | Compile Check on a valid request reports the generated source builds against the application assembly. | pass 2026-08-31 — scratch project produced PageGenPreview.dll |
| GEN-11 | Compile Check reports errors | A template fault is reported with file, line, column and `BC` code rather than a bare failure. | untested |
| GEN-12 | Preview Code needs a saved request | With unsaved edits it offers to save first, and returns without writing when the offer is declined. | untested |
| GEN-13 | Unticking Admin Required keeps the field | In Select Fields, unticking Admin Required or Lookup leaves Use in _U ticked and the row in place; unticking Use in _U still clears both. | pass 2026-08-31 |
| GEN-14 | Menu Caller lists the dashboards | The drop-down holds Make a Selection, Main Menu, and every `Dashboard_*` class found by reflection - four entries today. Window controls and browse pages are absent. | pass 2026-08-31 |
| GEN-15 | Menu Caller required clears on selection | Choosing a caller clears the red required border. The combo is unbound, so `IsEmptyComboSelection` judges it on the selected item rather than `SelectedValue`. | pass 2026-08-31 |
| GEN-16 | Save && Generate validates first | With a required field blank, validation reports before any overwrite prompt, so no overwrite is authorised for a request that cannot generate. | untested |
| GEN-18 | Ticking Lookup produces a usable lookup | Ticking Lookup asks for the table, the column it saves and the column it shows, and composes the stored form. Cancelling unticks the box rather than storing a lookup with no target. **Was a defect:** ticking used to store only the field name, which the generator refuses, and the Lookup Fields box is read-only so the form could not be typed either — a lookup could be ticked but never generated. | pass 2026-09-01 — picker used on `GenderID`, spec stored, page generated and opened |
| GEN-19 | Lookup picker appears in front | The picker is owned by the Select Fields window, not the page beneath it. **Was a defect:** owned by the page, Windows put it behind the active modal window, so ticking Lookup appeared to do nothing at all. | pass 2026-09-01 |
| GEN-20 | Lookup list is scoped to the registration | A lookup on a table with a `RegistrationID` lists only the session's rows plus rows with none; a table without that column lists everything. The Filter by Registration tick is enabled only when the column exists and ticked only when rows populate it. **Was a defect:** `FW_GENDER` holds Male and Female for two registrations and the combo showed each twice. | pass 2026-09-01 |
| GEN-17 | Required lookup uses the framework helper | A lookup marked Admin Required emits one `AddComboField` call, so it gets the App Admin blue, the `*`, the required border and the naming convention. Built by hand it got the `*` but never the blue, and `ShouldSkipBrRequiredStyling` decides App Admin ownership by that blue - so the field silently lost its precedence. | pass 2026-09-01 — Preview Code, Compile Check and the opened `_U` page all correct |
| GEN-21 | A saved lookup survives a key rename | Rename a lookup table's key, then open the request and save it. The stored spec is rebuilt from the declared foreign key rather than written back as loaded, and regenerating emits the new key. **Was a defect:** `FW_Gender.ID` became `GenderID` and every saved spec still said `ID`. Re-saving could not correct it — `JoinLookupFields` replayed the sentence it loaded, and the schema was consulted only when the Lookup box was ticked. The generated `_U` page then threw `Invalid column name 'ID'` on load. Note the browse SQL was correct throughout, because it reads `LookupTarget` instead. | pass 2026-09-02 — untick/re-tick produced `FW_Gender.GenderID`; the rebuild now makes the tick unnecessary |
| GEN-22 | A lookup needs a declared foreign key | With no foreign key on the column, the `_B` grid's Displays cell is a read-only grey box and the `_U` Lookup tick is disabled — the generator reads declared relationships only. Declare the key and both become live. **Was a defect:** `FW_ENTITY.GenderID -> FW_GENDER` was never created because `sql/049` named `FW_GENDER.ID`, a column that has never existed; the statement failed and the `GO` after it let the second key succeed, so one of the two was silently missing. | pass 2026-09-02 — `sql/056` declared it against `FW_Gender.GenderID`, trusted; the Displays dropdown appeared |
| GEN-23 | A page that cannot load says why | Force a generated page to throw on load. A dialog names the error and the log path instead of the window silently never opening. **Was a defect:** `Program.OnThreadException` logged to `startup.log` and showed nothing, so double-click and Modify appeared to do nothing at all — the failure was invisible for an entire session despite being logged 48 times. | untested |

---

## Automated coverage

These run without a database and are not duplicated above.

| What | Command |
|---|---|
| Unit tests — permissions, display names, keyed hash, credential precedence, empty combo | `dotnet test .\tests\HelloWorld.Tests\HelloWorld.Tests.vbproj` |
| Browse contract checks | `powershell -File .\scripts\validate-browse-regression.ps1 -SkipBuild` |
| Maintenance contract checks | `powershell -File .\scripts\validate-maintenance-regression.ps1 -SkipBuild` |
