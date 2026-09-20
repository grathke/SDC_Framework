# Test Cases

Manual and scripted test cases, each with a stable ID so they can be referenced in commits, notes
and conversation. Automated coverage lives elsewhere and is listed at the bottom.

Add a case here when a defect is found, so the same thing can be checked again deliberately.

Status: `pass` with a date, `fail` with what happened, or `untested`.

---

## DB — Database connection and configuration

Run with `.\scripts\test-db-connection-failures.ps1 -Case <name>`. Nothing on the server changes;
each case is simulated through `SDC_DB_CONNECTION` in the launched process.

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
| LOGIN-02 | Single-role user | No role selector — straight to the main menu. **Needs a new account:** it named `jessica.anderson0015@example.com`, one of the 506 users deleted on 2026-09-04. | untested |
| LOGIN-03 | Inactive user | `asawyer@asboinc.com` has `IsActive = 0` and a valid hash. Login is refused with "This account is not active." **Was a defect, and this case recorded it while it did not work:** `TryAuthenticate` found the record, confirmed a hash and validated it, checking neither `IsActive` nor `DeletedFlag`. Removing somebody's access did not remove it. Fixed 2026-09-04; the check runs before the password, because their password is correct and "invalid password" would send them to reset one that works. | pass 2026-09-04 |
| LOGIN-04 | Other registration | Logs in scoped to a second registration. **Needs a new account:** it named `1@1.COM` in registration 2, deleted on 2026-09-04. The surviving `1@1.com` is registration 1. | untested |
| LOGIN-05 | Role selector double-click | Double-clicking a row selects and continues, with no cell edit outline. | untested |
| LOGIN-06 | Role switch from the main menu | Changing role reconfigures menu access and the caption. | untested |
| LOGIN-07 | A deleted user cannot sign in | Soft-delete a user with a valid password, then try their email. Login answers "No user found for that email" - the same as an address that never existed, because confirming a deleted account was once real tells an attacker something and the user nothing. **Was a defect:** `vw_FW_CurrentUser` did not expose `DeletedFlag`, so login could not have checked it; a deleted user with a hash signed in normally. `sql/072` filters the view. | untested |
| LOGIN-08 | An email is unique across registrations | Creating or editing a user with an email another user holds is refused: "The email X already belongs to Y. An email is how a user signs in, it must be unique." A holder whose record is soft-deleted is reported separately and still refused - reusing it would create the duplicate the moment they were restored, and `RestoreUser` has no check. **Was a defect:** nothing checked at all - no unique index, `IsUnique` null on every `FW_RoleFields` row for it, and `ValidateUniqueFields` scoped by `RegistrationID` so it could not have expressed it. Enforced on the page, at the write boundary, and by `UX_FW_Users_Email`. | pass 2026-09-04 |
| LOGIN-09 | An email is stored in one form | Save a user with `TEST@Example.COM`; the column holds `test@example.com`. Login compares `LOWER(REPLACE(Email,' ',''))`, so normalising on the way in makes the stored value the form a lookup asks for - which is what lets a plain unique index mean what the application means. Without it `a@b.com` and `a @b.com` are two rows to the index and one address to login. | untested |

---

## MENU — Ribbon arrangement and icon pictures

App Admin only. Both are global: one arrangement and one set of pictures for every user, stored in
`FW_DashboardLayouts` under `SDC.Framework.MainMenu`.

| ID | Case | Expected | Status |
|---|---|---|---|
| MENU-01 | Drop-span bar | A blue bar appears in the strip below the tiles only while one is being dragged: absent when the menu opens for every role, App Admin included; drawn as a press passes the drag threshold; gone on release. A plain click never shows it. It spans the movable stretch only — from the left edge of the first movable tile to the end of the row, with the anchored head outside its left end. The row must not shift as it appears. **Was a defect twice:** the mark started as the panel's `BorderStyle`, which was tied to the role, so an App Admin merely using the menu saw a permanently outlined panel; and it outlined the whole panel including the anchored head, promising a drop zone three tiles will not accept. Toggling a `BorderStyle` also costs the client area a pixel each way, which nudged the row at drag start. | pass 2026-09-03 |
| MENU-02 | Reorder a tile | Drag `User Admin` ahead of `Users`. The row reflows as the pointer passes the neighbour's midpoint, and the order survives closing and reopening the menu. | pass 2026-09-03 |
| MENU-02a | The head of the row is anchored | `Close`, `Dashboard`, `Application Settings` lead the flow panel in that order and none can be dragged. No other tile can be dropped in front of them — a drag towards the head of the row stops when it reaches one. | pass 2026-09-03 |
| MENU-02b | An anchored tile can still be hidden | Anchoring does not force a tile visible. With Application Settings hidden by role, `Users` moves left into its place and the head of the row is Close and Dashboard. | pass 2026-09-03 |
| MENU-18 | Menu Test dropdown | Clicking `Menu Test` drops a menu below the tile that overlays the regions rather than being clipped at the ribbon's edge; items highlight on hover; choosing one reports it. The menu closes once the pointer is over neither the menu nor the tile, and does **not** close while moving between the two. Dragging the tile does not open it. | pass 2026-09-02 |
| MENU-22 | Clicking the tile again does not close the menu | Clicking `Menu Test` while its menu is open leaves the menu open. **Deliberate, and here so it is not re-attempted blindly.** An open drop-down holds the mouse, so that click is a dismissal before it is ever a button click: the menu has gone by the time the tile's `Click` runs, which reopens it in the same frame and looks like nothing happened. Suppressing the reopen from the `Closed` event was built on 2026-09-03 and **failed** — it assumed `Closed` runs before `Click`, and it evidently does not. Removed rather than left dead. Cancelling the close from `Closing` while the pointer is on the tile, then letting `Click` see a still-open menu, is the untried approach and does not depend on that ordering. Not pursued because pointer-away closing already covers the case. | n/a by design |
| MENU-23 | A click elsewhere closes the menu | Open the menu, then click a different ribbon tile. The menu closes and the other tile does its own job. **Covered by MENU-18, and not separately reachable by hand.** Getting to another tile means leaving both the tile and the menu, so the 200ms watcher has almost always closed the menu before the click lands — the outcome is right, but hover-away did the work and the drop-down's own outside-click dismissal never ran. Isolating it needs the poll interval temporarily lengthened in `TileDropDownController`, which is not worth a code change: both mechanisms produce the same correct result and one simply gets there first. | covered by MENU-18 |
| MENU-24 | Application Settings drops a menu for an App Admin | As App Admin, click `Application Settings`. A menu opens over the regions below with `Admin Dashboard`, a separator, and `Switch User`. Choosing the first opens the same dashboard the button opened before. | pass 2026-09-04 — menu opened with both items and the separator |
| MENU-25 | Company Admin keeps a plain button | As Company Admin (not App Admin), click `Application Settings`. The Company dashboard opens directly, with no menu. A one-item menu would be a worse button, so the tile only grows one where there is more than one action. | pass 2026-09-04 — App Admin gets the menu, Company Admin the plain button |
| MENU-26 | The role is read on click, not at configure time | As App Admin, open the menu and close it, switch to a Company Admin role, then click `Application Settings`. The dashboard opens directly. Switch back and the menu returns. The handler branches on the session at click time, so no stale handler survives a role change. | pass 2026-09-04 |
| MENU-27 | Login as Different User has left the pinned row | The right-hand row shows `My Profile`, `Select a Role` and `Help Desk` with no gap where the fourth tile was, for every role. The action is now the last item of the Application Settings menu, reached through `FW_MainMenu.OpenSubstituteUser`, so it is still the same placeholder handler and not a second copy. | pass 2026-09-04 |
| MENU-28 | A full ribbon is evenly spaced | Fill the movable row to capacity. The gap between the last movable tile and `My Profile` is the same 4px as every other gap in the ribbon, not the leftover slack it used to be. `LayoutRibbonPanels` gives the flow panel a whole number of tiles and starts the pinned panel where it ends, so the spare pixels collect to the right of the pinned row instead of in the middle. | pass 2026-09-04 — 9 tiles at the 1280 default, evenly spaced through to the pinned row |
| MENU-29 | The drop bar covers the free slots | Drag a movable tile. The bar in the gutter runs from the first movable tile to the end of the panel, including the empty room after the last tile. **Was a defect:** it stopped at the last tile, so three free slots showed as bare panel and the row looked full. | pass 2026-09-04 |
| MENU-30 | Resizing keeps the spacing | Drag the window between its 1180 minimum and full width. Tile spacing never changes, the row never jitters, and the pinned group steps in whole tiles. Neither panel is anchored Right any more — both are placed by one calculation, so an anchor cannot stretch the flow panel to a fractional tile between resizes. | pass 2026-09-04 |
| MENU-31 | Two more tiles fit at the minimum window | At the 1180 minimum, the movable row holds 8 tiles against the 6 in use. `PageGenerator.MainMenuMovableTileCapacity` holds that figure rather than the capacity at the current width, because a tile that fits only when the window is wide is clipped silently when it is not — the panel neither wraps nor scrolls. | pass 2026-09-04 |
| MENU-11 | Dashboard placeholder | `Dashboard` appears second with `dashboard.png`, and clicking it says it is not wired up yet. It is a placeholder holding its anchored place, not a working action. | pass 2026-09-03 |
| MENU-03 | The drop does not open a page | The mouse-up that ends a drag does not open the dragged tile's page. A **later** click on that same tile does open it. | pass 2026-09-03 |
| MENU-04 | Click without dragging | A plain click still opens its page, unchanged. | pass 2026-09-03 |
| MENU-05 | Hidden tile closes the gap | With the order from MENU-02 saved, log in as a role that is neither App Admin nor Company Admin, so Application Settings is hidden. The remaining tiles pack left with no hole and keep their saved relative order — anchoring does not exempt it from being hidden. **This is the discriminating check** — a gap means a coordinate was saved instead of a rank. | pass 2026-09-03 |
| MENU-06 | Non-admin cannot rearrange | As a non-App-Admin, dragging a tile does nothing and right-click offers no menu. | pass 2026-09-03 |
| MENU-07 | Change a tile picture | Right-click any ribbon tile, choose a graphic. It applies at once and is still there on the next launch. Reset to Default returns the source picture. | pass 2026-09-03 |
| MENU-08 | Pinned picture survives a resize | Change the Help Desk picture, then resize the window. It must not revert. **Was a defect:** `LayoutPinnedActions` re-set that tile's caption, padding and image on every resize — duplicating what `AddActionTile` already did — so a chosen picture was wiped the first time the window changed size. The block was removed rather than worked around. | pass 2026-09-03 |
| MENU-12 | Ribbon fits its tiles | All six left tiles are fully visible at the default window size and at the 1180 minimum. **Was a defect:** at 122px per tile six needed 756px in a 730px panel, and the panel neither wraps nor scrolls, so the last tile was silently clipped. | pass 2026-09-03 |
| MENU-14 | Switching into an App Admin role | Log in as a non-admin role, then switch to an App Admin role from the Select a Role tile. Dragging and right-click must both start working without restarting — dragging a tile now raises the panel border, which it did not before the switch. **Was a defect:** handlers were wired once at startup, so nothing could be dragged and no right-click menu was offered after a switch. | pass 2026-09-03 |
| MENU-16 | Every tile says which kind it is | As App Admin, hovering Close, Dashboard, Application Settings or any of the four pinned tiles on the right shows "Fixed position", and hovering any of the movable tiles shows "Moveable". No tooltip appears at all for a role that cannot rearrange anything. | pass 2026-09-03 |
| MENU-17 | Dragging holds mouse capture | Press a movable tile and drag across its neighbours: they must **not** light up with the hover highlight, and the tiles must trade places as the pointer crosses in. **Was a defect:** without an explicit `Capture = True` the pressed tile stopped receiving mouse movement the moment the pointer left it, so no swap could ever be computed and the ribbon appeared immovable — the hover highlight appearing on the tiles being crossed was the giveaway. | pass 2026-09-02 |
| MENU-15 | Switching out of an App Admin role | Switch from an App Admin role to one that is not. Dragging and right-click must both stop, so no drag can raise the panel border. The write is refused at the data boundary either way. | pass 2026-09-03 |
| MENU-13 | Even spacing, Help Desk flush right | Every tile in both panels is the same width with the same gap, and Help Desk finishes at the right-hand end of the ribbon rather than 22px short of it. | pass 2026-09-03 |
| MENU-09 | Role tile picture survives a role change | Change the Select a Role picture, then switch role. It must not revert — `UpdateRoleSelectionTile` rebuilds that tile and re-applies the choice afterwards. | pass 2026-09-03 |
| MENU-19 | The drop span follows a hidden anchored tile | The bar must start at the first tile that is *visible* and movable, not where that tile sits when everything is shown, and a leftward drag must stop after the last **visible** anchored tile rather than reaching the front. **Not reachable, and that is the design.** An App Admin and a Company Admin always see Application Settings — `canAccessApplicationSettings = isAppAdminSession OrElse isCompanyAdminSession` in `MenuFormInitializer` — and only an App Admin can drag, so no session both hides an anchored tile and can rearrange. `dashboard` and `close` are wired `True` unconditionally and never hide at all. The visible-child skips in `IndexUnderPointer` and `Panel_Paint` are defensive: they cost nothing and mean an anchored tile given a failable permission later would not need this found again. | n/a by design |
| MENU-20 | Anchored tiles are never ranked | No anchored key — `close`, `dashboard`, `application-settings` — has a `GridColumn` in `FW_DashboardLayouts` under `SDC.Framework.MainMenu`, however many arrangements have been saved. A **row** for one is expected and fine: changing a tile picture upserts a row carrying only `IconFileName`, leaving the grid columns null, and `GetDashboardIconPositions` filters `GridRow IS NOT NULL AND GridColumn IS NOT NULL` so a null-rank row is never read as a rank. It is the rank that must be absent, not the row. **Check it in the database, not the UI** — `RankOf` returns `AnchoredBase` before it ever reads storage, so a stray rank would be masked on screen and would surface only if the tile were later un-anchored. | pass 2026-09-03 — `close` has a picture row with null rank; no anchored key carries a rank |
| MENU-21 | Anchored tile returns across a role switch | As App Admin, arrange the movable tiles and note the order. Switch to a role that is neither App Admin nor Company Admin: Application Settings hides and the row packs left (MENU-05). Switch back: it resumes the head of the row ahead of the movable tiles, which keep the arranged order behind it. Weaker than MENU-20 — it proves the round trip, not that no rank was written. | pass 2026-09-03 |
| MENU-10 | Dashboards unchanged | Both dashboards still drag icons and re-picture them exactly as before, now through the shared `IconImageController`. Dashboard dragging stays open to any user, not just App Admin. | pass 2026-09-03 |

---

## U — Maintenance pages

Mostly verified on `Users_AppAdmin_U` during 2026-08-29/30. Re-run after changes to `000_FRAMEWORK\000_BASE CLASSES\FW_Base_U.vb`.

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
| U-30 | Copy Report is App Admin only | On a help desk issue, the `Copy Report` button is visible to an Application Admin and absent to everyone else, a Company Admin included. Deliberately narrower than `IsCurrentUserSupport`, which a Company Admin satisfies: they act as support here — setting priority and status — but the report names the source file behind the page, which is not for them. Tidiness rather than a control; the report carries nothing not already on the form, so nothing depends on the button being hidden. | pass 2026-09-03 |
| U-31 | A password is never stored as typed | Save a user with a password on any page, generated or hand-written. The column reads `#####` and `PasswordHash` is populated. Editing without retyping leaves both untouched - the sentinel means unchanged. **Was a defect:** the contract lived only in `Users_AppAdmin_U`, and `UpdateUserPasswordHash` had one caller, so a generated page wrote `1234` into `[Password]` and left the hash null - a readable password stored, and an account that could not sign in. Enforced at the write boundary now, so `Password` stays editable but cannot reach the column as typed. `PasswordHash` is refused outright. | pass 2026-09-04 |
| U-32 | A password is hashed after the insert, not before | Create a user in one save. The hash is keyed on the `UserId`, so it cannot exist until identity has issued one - the write path hashes after the row is written. A page that tried before would silently store nothing, which is what `PersistPasswordIfChanged` does when `UserID` is still 0. | pass 2026-09-04 |
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
| B-13 | No `FW_Pages` row | Opening creates the row with the right `WindowOrPage`, `DB_Table`, alias and `CreatedBy`. | untested |
| B-23 | Text QBE defaults to Equals | Opening the QBE on any page shows `EqualsTo` as the selected operator for every text field, not `Contains`. The dropdown order is unchanged — `GetAllowedOperators` already listed Equals first — and numeric, boolean and date fields are unaffected because they already defaulted to Equals. | pass 2026-09-03 |
| B-24 | A typed `%` becomes a pattern | With the Equals operator: `Glenn%` finds names starting Glenn, `%Rathke` names ending Rathke, `%Rathk%` names containing Rathk, and `Gl%nn` matches mid-string. **Was a defect:** `%` was a literal under Equals, so every one of these emitted `= 'Glenn%'` and returned nothing at all — no error, just an empty grid, which reads as "no such person". | pass 2026-09-03 |
| B-25 | A typed `%` under Not Equals | `<> Glenn%` emits `NOT LIKE` and excludes names starting Glenn. **Was a defect:** it emitted `<> 'Glenn%'`, which excludes nothing, so the filter returned every row — the same silence in the opposite direction. | pass 2026-09-03 |
| B-18 | The operator stops wrapping when the user supplies wildcards | `Contains` with `%Rathk%` searches for `%Rathk%`, not `%%Rathk%%`. Both match the same rows, so check it in the emitted SQL rather than the grid — the point is that the pattern executed is the pattern typed. | pass 2026-09-03 |
| B-19 | Underscore is not a switch | `= FW_Users` stays an exact match and does **not** also find `FW.Users` or `FWxUsers`. Only `%` puts a value into pattern matching; once it has, `FW_%` treats `_` as a wildcard exactly as SQL does. This is the case that stops the change breaking real lookups, since underscores are common in this data and literal percent signs are not. | pass 2026-09-03 |
| B-20 | No wildcard, no change | Every existing search behaves as it did: `Rathke` under Contains still finds Rathke, Rathkey and Brathke; under Equals it is exact. Saved QBEs store their operator, so one saved under Contains still runs as Contains after the default changed. | pass 2026-09-03 |
| B-21 | Mid-string `%` reports on a client-filtered page | On a page whose SQL comes from `FW_Pages` — most pages — `Gl%nn` reports that a wildcard in the middle cannot be used there, naming the field. **Was a defect, and the reason the message exists:** those pages filter through `DataView.RowFilter`, which allows `%` only at the ends and *throws* otherwise; the throw was caught by a bare `Catch` that returned the unfiltered table. Every row came back, silently, which reads as "everything matched". The same pattern works normally on a page filtered in SQL, so behaviour still differs by page — it now says so instead of lying. | pass 2026-09-03 |
| B-22 | An unusable filter never returns everything | Force any invalid filter expression on a client-filtered page. It must report, naming the filter. It must never fall back to the unfiltered table. | covered by B-21 — with mid-string refused up front, a genuinely invalid expression is no longer easily reachable by hand. The fall-back-to-unfiltered path is gone either way. |
| B-27 | A binary column never reaches the grid | Open a page selecting `*` from a table with a `RowVersion` - Users_AppAdmin_B does - and scroll to the far right. No error, and no `RowVersion` column in the grid, the columns manager or QBE. **Was a defect:** a DataGridView builds its column type from the data type, so a `Byte()` column becomes a `DataGridViewImageColumn`; painting eight bytes of timestamp as an image throws out of GdiPlus and the grid raises its own dialog. It only fired when somebody scrolled far enough right for the column to paint, and every `FW_` table has had a `RowVersion` since `sql/010`. Removed from the DataTable before binding, not hidden: the image column's own `ValueType` is `Image` so a `Byte()` check on the grid column matches nothing, and a hidden column can be restored by a saved layout. | pass 2026-09-04 |
| B-26 | Deleted state comes from the page's own table | **Recorded as B-14 in commit 237239f, renumbered 2026-09-03** - B-14 was already taken by the saved-layout case from fdd9b49. A browse result with no `DeletedFlag` column has one hydrated from the table the page reads, keyed on that table's own primary key. **Was a defect:** the hydration named `FW_Entity` unconditionally, for all 11 of the 12 registered browse queries that lack a `DeletedFlag` column, so each page's keys were looked up in a table it has nothing to do with. `FW_Entity` 8 was soft-deleted and `FW_Users` 8 was not, which silently dropped Alan Smith from `FW_UserAccessDiagnostic_B` and `FW_UserAccessExplanation_B`. Only `Users_AppAdmin_B` escaped, because its `SELECT UserID AS PK, *` happens to pull `DeletedFlag` in. `Roles_B` was never affected — it passes its own table and key to `ExecuteCustomQuery`. | pass 2026-09-02 — fixed; re-check by soft-deleting an entity and confirming the same-numbered row still shows on a non-entity browse page |

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
| GEN-07 | `FW_Pages` | A row is created, or an existing row's SQL updated when the table matches. | untested |
| GEN-08 | Dashboard icon | Added when `MenuCaller` is `Dashboard_Application`. `ICON_CATALOG.md` is **not** updated — still manual. | pass 2026-08-31 — GeneratedPageActionKey_EntityX_B present |
| GEN-09 | Preview Code writes nothing | `Preview Code` shows both sources and the Summary, and no page file, `FW_Pages` row or dashboard icon changes. | untested |
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
| GEN-24 | The dashboard is found wherever it lives | Generate a browse page with `MenuCaller` set to `Dashboard_Application`. The icon is added to `000_FRAMEWORK\020_DASHBOARDS\Dashboard_Application.vb`, and Preview Code names that file rather than one that does not exist. **Was a defect:** the path was derived as `"02_FW_" & menuCaller & ".vb"` at the workspace root, and the 2026-09-04 folder reorganisation invalidated both halves — the `##_FW_` prefix went and the file moved into a band folder. Generation reported "could not be found"; Preview Code silently claimed the button would be added to `02_FW_Dashboard_Application.vb`. Invisible to both the build and a normal run, since only the generator executes this path. Now searched for rather than derived. | untested |
| GEN-25 | Main Menu places a ribbon tile | Generate a browse page with `MenuCaller` set to `Main Menu`. A tile is written into `MenuFormInitializer`, the report names it and says how many of the 8 movable slots are now used, and the button opens the page after the next build. **Was a defect:** the generator only ever placed dashboard icons. `Main Menu` was offered in the drop-down, the request was accepted, the pages were written, and no button was placed — the only trace being a Preview Code line reading "NOT A DASHBOARD", which reads as information rather than refusal. Found on 2026-09-04 when `a generated browse page` generated with no way in. | pass 2026-09-04 — tile written, report read "7 of 8 tiles" |
| GEN-26 | A full ribbon falls back to the dashboard | With 8 movable tiles already on the ribbon, generate against `Main Menu`. The report says the ribbon is full, gives the count, and the button is placed on the App Admin dashboard instead. The page is never left reachable from nowhere. | untested |
| GEN-27 | Capacity is judged at the narrowest window | The count compares against 8 — the capacity at the 1180 minimum — not at the current window width. A tile placed against the default width would be clipped for anyone with a smaller window, silently, because the flow panel neither wraps nor scrolls. | untested |
| GEN-28 | Regenerating does not double the tile | Generate twice against `Main Menu`. The second run reports the tile is already on the ribbon and changes nothing, matched on the `generated-<page>` action key. | pass 2026-09-04 — second run left one tile, both pages rewritten in place with identical hashes |
| GEN-29 | A new page lands in 999_GENERATED | A pair generated for the first time is written to `999_GENERATED/` at the repository root, not scattered at the root itself, and compiles from there. `PageGeneration_U` built that path in four places of its own; all four now call `PageGenerator.GeneratedPagePath`, so the page's manual-edit checks cannot end up looking somewhere the generator no longer writes. | pass 2026-09-04 — both pages rewritten into 999_GENERATED |
| GEN-30 | Regeneration follows a filed page | File a generated page into a band or a project folder, then regenerate it. It is rewritten where it now lives, not duplicated into `999_GENERATED`. **Would have been a defect:** writing by name alone made filing a one-way door — a regenerated page put a second copy in `999_GENERATED`, two files declaring the same class in one namespace, which is `BC30179` rather than anything that reads as a duplicate. The page is searched for before it is placed, the same way a dashboard is, and copies under `bin`, `obj`, `tests`, `restore-points`, `project-backup` and `900_SANDBOX` are ignored so a stale archive cannot be rewritten instead. | pass 2026-09-04 — filed into 040_USERS, regenerated there, staging folder stayed empty |

---

## SESSION — Sign-in, sign-out and how a session ended

`FW_Session` takes one row per sign-in. The start is exact; the end is not always, and `EndReason`
says which kind it got. Read the rows with `.\scripts\sessions.ps1`.

| ID | What | How | Result |
|---|---|---|---|
| SESSION-1 | A clean exit is exact | Sign in, close the application properly. The row gets `EndReason = Exit` and a connected time matching the wall clock. | pass 2026-09-20 — 169s and 39s on two desktop runs |
| SESSION-2 | A closed browser tab writes `Disconnect` | Sign in over Thinfinity, close the browser tab, leave the application alone. After the disconnect grace the row gets `EndReason = Disconnect`, written at the moment `VirtualUISessionClosed` fires. **Was a defect:** the handler logged, started a forced-exit timer, asked the message loop to unwind and left the row to Main's `Finally`. VirtualUI takes the process down first, so the `Finally` never ran, no `Main end` was logged, and the session stayed open for ever. An earlier session the same evening unwound properly and wrote `Exit` — two paths, and the one that loses the row is the ordinary one. Found 2026-09-20 by running exactly this test. | pass 2026-09-20 — `Disconnect` after 187s, kind `Thinfinity` |
| SESSION-3 | The forced-exit timer survives long enough to fire | With a modal dialog holding the process open, a VirtualUI close still exits within about three seconds and logs `forcing exit`. **Was a defect:** the timer was a local kept only by `GC.KeepAlive` at the end of its own method, so it was collectable the moment the method returned and a collected `Timer` never fires. It is now a field. | untested — needs a stuck modal to reproduce |
| SESSION-4 | A killed process is closed by the next startup | Sign in, kill the process, start the application again. The startup sweep closes the row as `Crash`, logging how many it closed, and only for this machine. | pass 2026-09-20 — `Closed 1 abandoned session(s) from this machine` |
| SESSION-5 | A crash reports its length as unknown | `sessions.ps1` prints `unknown` rather than a number for a `Crash` row, and `-Summary` counts it apart from the arithmetic. The end fell back to the last activity, or to the start where there was none, which understates rather than invents. | pass 2026-09-20 |
| SESSION-6 | Activity is stamped as it happens | Sign in and run a search. `LastActivityOn` is set within the flush interval. **Was a defect:** it was derived at the end of a session from `MAX(FW_UsageCounter.HourUtc)` where the bucket was at or after `StartedOn`. A bucket carries the hour it opened, so a session starting at 20:40 never matched its own 20:00 bucket — null every time. The buckets are also keyed by registration rather than by user, which would have credited one person with another's searches. | pass 2026-09-20 — first non-null reading, 50s into an open session |
| SESSION-7 | Switch User reads as two sessions | Sign in, switch to another user. The first row is ended and a second opened, carrying the new user, registration and role. | untested |
| SESSION-8 | The disconnect grace is not a constant | Measured at 156s, about 210s, 197s and 187s on four occasions. Nothing may subtract a fixed figure from a `Disconnect` end to guess when the tab was really closed. | pass 2026-09-20 — four readings, no two alike |

---

## Automated coverage

These run without a database and are not duplicated above.

| What | Command |
|---|---|
| Unit tests — permissions, display names, keyed hash, credential precedence, empty combo | `dotnet test .\tests\SDC.Framework.Tests\SDC.Framework.Tests.vbproj` |
| Browse contract checks | `powershell -File .\scripts\validate-browse-regression.ps1 -SkipBuild` |
| Maintenance contract checks | `powershell -File .\scripts\validate-maintenance-regression.ps1 -SkipBuild` |
