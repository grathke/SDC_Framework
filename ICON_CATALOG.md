# Icon Catalog

The repository icon catalog referenced by the Action Icon Guardrail. When an actionable icon is
added, changed or removed, update this file in the same change.

Track per icon: ActionKey, placement, ActionType, target, caption source, icon file, visibility
rule, click behavior.

Registration happens in two places:

- `000_FRAMEWORK\010_MAINMENU\MainMenu.vb` builds the ribbon and owns `UpsertActionTile`, `ConfigureActionVisibility`,
  `SetActionIconFromFile`.
- `MenuFormInitializer.Configure` decides which actions are visible for the session and adds
  page-specific tiles such as `user-admin`.

Icon files live in `assets/images/`.

## Icon pictures can be changed at runtime

Neither the dashboards' icons nor the main menu's ribbon tiles are settled by their source alone.
An App Admin can right-click any of them and choose a different graphic; the choice is stored in
`FW_DashboardLayouts.IconFileName`, keyed on a surface name and the icon's `ActionKey`, and applied
on the next load. `IconImageController` owns this for all three surfaces.

The surface names are `Dashboard_Application`, `Dashboard_Company` and `SDC.Framework.MainMenu`. The
last is supplied by `MenuFormInitializer`, not by the menu form, because one menu form can serve
more than one application; a second application's initializer passes its own name and keeps its own
pictures.

- The filename written in `000_FRAMEWORK\020_DASHBOARDS\Dashboard_Application.vb`, `000_FRAMEWORK\020_DASHBOARDS\Dashboard_Company.vb` or the
  `AddActionTile` calls in `000_FRAMEWORK\010_MAINMENU\MainMenu.vb` is the **default**, not necessarily what is on
  screen. "Reset to Default" clears the override and returns to it.
- `UpdateRoleSelectionTile` rebuilds the role tile from source on every role change, picture
  included, and puts the chosen one back afterwards. Anything else that rewrites a tile's `Image`
  must call `ReapplyChosenIcons` after it, or the choice reverts silently.
- `LayoutPinnedActions` used to do the same on every ribbon resize. That block is gone: it re-set
  the caption, padding and picture `AddActionTile` had already set, so it did no work except wipe a
  chosen Help Desk picture whenever the window was resized.
- The choice is global. `sql/055` removed `RegistrationID` from that table because neither
  dashboard is registration aware, so there is no scope to disagree about.
- The picker is `IconPicker`, the same dialog the page generator uses, so a name means the same
  thing in both places: a file in `assets/images/`, or a built-in glyph marked `system:`.
- Only an App Admin sees the menu, and `DataAccess.SaveDashboardIconOverride` refuses anybody else
  regardless — a hidden menu item is not authorization.

## Ribbon tiles can be rearranged

An App Admin can drag the tiles in the main menu's left flow panel into a different order. The panel
shows its border only while a tile is actually being dragged — an App Admin who is just using the
menu sees the ribbon everybody else sees. Which tiles can move is said in a tooltip instead:
"Moveable" or "Fixed position", and only for an App Admin, since nobody else can drag anything.
`RibbonTileArrangementController` owns this.

The pinned row on the right (`my-profile`, `select-role`, `help-desk` — `login-as-substitute` is
registered but hidden since 2026-09-04) is a flow panel too, but no drag is wired to it: they keep
the order `LayoutPinnedActions` sets and can only have their pictures changed. It is a flow panel
so that a pinned tile hidden by a permission lets the rest close up behind it.

Both panels use one tile size and one margin — `TileWidth`, `TileHeight`, `TileMargin` in
`000_FRAMEWORK\010_MAINMENU\MainMenu.vb` — so spacing is identical across the ribbon and the pinned panel is sized to
exactly the tiles it holds. When a tile is hidden the remaining ones pack together and the spare
room collects at the end of the panel; no hole is left where the hidden one was.

The flow panel holds `close`, `dashboard`, `application-settings`, `users`, and
`user-admin` — the last added at runtime by `MenuFormInitializer`.

- `close`, `dashboard` and `application-settings` are **anchored**, in that order, at the head of
  the row: none can be dragged, and nothing can be dropped in front of them. The anchor list is
  `MenuFormInitializer.AnchoredMenuKeys`, passed in like the surface name, because which tiles lead
  a ribbon is the application's decision. An anchor naming a key with no tile matches nothing and
  costs nothing, so the list can run ahead of the ribbon.
- Anchoring is not visibility. Any anchored tile can still be hidden by a permission and the row
  closes up around it — hide `dashboard` and `application-settings` moves left into its place.
  `application-settings` is hidden from a role that is neither App Admin nor Company Admin.
- An anchored tile gets no rank in the table and no drag handlers at all. Letting it be dragged and
  then snapping it back would read as a fault rather than as a tile that stays put.

- What is stored is a **rank**, not a position: `GridRow` 1 and `GridColumn` the place along the
  row. The flow panel closes the gap when a permission hides a tile, so a saved coordinate would
  leave a hole where a hidden tile used to be.
- The rank is sparse. A tile with no saved rank keeps its source order, after the ranked ones, so a
  role that cannot see a tile — or a menu that does not have it at all — still gets a sensible row.
- Every tile's rank is rewritten on each drop, hidden ones included, because an insertion shifts
  everything after it.
- Global, like the pictures. `DataAccess.SaveRibbonTileOrder` requires App Admin; the shared write
  behind it does not, so dashboard dragging stays open to any user as it always has.

---

## close

- placement: main ribbon (left)
- ActionType: Command
- target: logout / close menu
- caption source: fixed (`Close`)
- icon file: `close.png`
- visibility rule: always visible
- click behavior: closes the menu session

## dashboard — REMOVED 2026-09-15

Gone from the ribbon. It was a placeholder throughout: its handler showed a message saying it was
not wired up, and the region-loading action it was meant to become was never decided. The Home
layout now occupies the idea of an arrangement the ribbon switches to, which is what this tile was
a stand-in for.

`FW_Perm_Dashboard` gated it and now gates nothing. The table and its `FW_RoleSchema` row survive,
so Roles still offers the permission — one an administrator can grant that changes nothing.
Removing it is the eight-table procedure in `CLAUDE.md`.

History, so the gap is not rediscovered twice: the tile was absent for some time before this. It
survived only in `project-backup/MenuForm.vb`, where it opened `HelloWorldPageForm` — a form with no
source left anywhere in the repository. The `ConfigureActionVisibility` call outlived it and did
nothing, because that method returns early for a key with no tile. Re-added 2026-09-02 as a
placeholder, removed 2026-09-15 without ever having been wired up.

## application-settings

- placement: main ribbon (left), anchored third
- ActionType: role-routed drop-down menu (App Admin) / role-routed page (Company Admin)
- target: AppAdmin -> drop-down over the regions below; CompanyAdmin -> `Dashboard_Company`
- caption source: fixed (`Application Settings`, on two lines)
- icon file: `gear.png`
- visibility rule: `MenuFormInitializer.ApplyActionAccess`, visible and enabled when
  `SessionState.Current.IsApplicationAdminRole` or `IsCompanyAdminRole`
- click behavior: an App Admin gets a menu; anyone else gets the old behaviour, which opens the
  dashboard for their role. The role is read on click, not when the ribbon is configured, so there
  is no handler to keep in step with a role change.
- menu items (App Admin only), built by `MenuFormInitializer.BuildApplicationSettingsItems`:
    - `Admin Dashboard` -> `FW_MainMenu.OpenApplicationSettings` — first, because it is what the
      button did before it grew a menu
    - separator
    - `Switch User` -> `FW_MainMenu.OpenSubstituteUser` — still a placeholder message
- note: every item invokes the tile handler that owns the action rather than repeating it, so the
  Application-versus-Company dashboard decision stays in one place. Opening and closing the menu
  belongs to `TileDropDownController` and is written nowhere here.
- note: inside either settings form, the Roles icon stays enabled at all times. The User Admin icon
  that sat beside it was removed on 2026-09-08 with the `FW_Users` pages.
## users

- placement: main ribbon (left)
- ActionType: Table/Page
- target: `Roles_B` (`ROLES`)
- caption source: fixed (`Users`)
- icon file: `users.png`
- visibility rule: `MenuFormInitializer.vb:120`, always visible and enabled
- click behavior: opens the `Roles_B` dialog

## user-admin — REMOVED 2026-09-15

Gone from the ribbon. It opened `Dashboard_Application` for an App Admin and `Dashboard_Company` for
everybody else, reading the role when clicked — which is exactly what `application-settings` already
does. Two tiles, one destination, and the other one is the tile that carries the drop-down menu.

The dashboards are unaffected: Application Settings remains the way in, so everything on them —
Roles, Help Desk admin, the audit trail, the Employees icon added the same day — is still reachable.

`users.png` is now unused by any catalogued action.

- note: this entry said `Users_AppAdmin_B` until 2026-09-08, which had not been true since the tile
  was pointed at the dashboards. That page has now been deleted with the rest of the `FW_Users`
  pages.

## ActionKey_FW_Employees_B

- placement: `Dashboard_Application` and `Dashboard_Company`, both at grid cell (1, 2)
- ActionType: Page
- target: `FW_Employees_B`
- caption source: fixed (`FW_Employees`) - the generation request's alias, worth renaming
- icon file: `Color_OK.png`
- visibility rule: none of its own. Placement is the gate: both dashboards are reachable only
  through `user-admin`, which opens the application one for an App Admin and the company one for
  everybody else. The page still enforces its own permissions through the `AccessProfile` it is
  handed - the placement is convenience, not the control.
- click behavior: opens `FW_Employees_B` modally with the dashboard's user and profile
- note: written by the page generator on 2026-09-15, once per dashboard, by setting the request's
  Menu Caller to each in turn. It had a ribbon tile (`generated-fw_employees_b`) until the same
  day; that was removed by hand, which is what the generator's own report asks for - it adds an
  icon but never removes the one somewhere else.

## menu-test

- placement: main ribbon, flow panel, after `user-admin` — movable
- ActionType: Menu (**demonstration**)
- target: none. Each item reports what was chosen.
- caption source: fixed (`Menu Test`, on two lines)
- icon file: `Fluent_Open.png`
- visibility rule: added by `MenuFormInitializer.AddMenuTestTile`; always visible
- click behavior: drops a `ContextMenuStrip` below the tile, over the regions. It closes when the
  pointer moves off both the tile and the menu, when an item is chosen, or on a click elsewhere.

Kept on purpose as the working example to copy when a real tile needs a menu. What it demonstrates
is how little a tile has to say: `AddMenuTestTile` supplies the items and nothing else. Every
question of behavior is answered once, for all tiles, by `TileDropDownController`:

- A `ContextMenuStrip` shown explicitly — not assigned to `Control.ContextMenuStrip`, which every
  tile already uses for the App Admin icon picker, and which answers the right button rather than
  the left.
- It is its own top-level window, so it drops **over** the regions below. A child panel would be
  clipped at the ribbon's edge.
- It closes when the pointer is over neither the menu nor its tile, polled rather than driven by
  `MouseLeave`: the tile and the menu are separate top-level windows, so moving from one to the
  other raises a leave on the first and a leave-driven close would shut the menu on the way to it.
- Clicking the tile a second time does **not** close the menu, which is a decision rather than an
  omission. An open drop-down holds the mouse, so that click is a dismissal before it is ever a
  button click: the menu has already gone by the time the tile's `Click` runs, which reopens it in
  the same frame. Suppressing that reopen from the `Closed` event was tried on 2026-09-03 and did
  not work — see MENU-22 in `TEST_CASES.md` before attempting it again.

A tile wanting a menu calls `tileDropDowns.Open(tile, itemFactory)` from its own click and inherits
all of it. There is deliberately no way to ask for different behavior.

- placement: main ribbon (right, pinned)
- ActionType: Command/Page placeholder
- target: not yet implemented
- caption source: fixed (`My Profile`)
- icon file: `my-profile.png`
- visibility rule: always visible (pinned)
- click behavior: placeholder message

## login-as-substitute

- placement: registered but **not on the ribbon**. It was a pinned tile on the right until
  2026-09-04, when the action moved into the `application-settings` drop-down.
- ActionType: Command/Page placeholder
- target: not yet implemented
- caption source: fixed (`Login as Different User`, on two lines) — unused while hidden; the menu
  row that replaced it reads `Switch User`
- icon file: `substitute-user.png` — unused while hidden
- visibility rule: `MenuFormInitializer.ApplyActionAccess` hides it unconditionally
- click behavior: placeholder message, reached through `FW_MainMenu.OpenSubstituteUser` from the
  Application Settings menu
- note: hidden rather than unregistered, because the menu item invokes this tile's own handler.
  Unregistering it would mean writing the workflow somewhere else and moving it back when the tile
  returns. The pinned row closes up on its own — `LayoutPinnedActions` skips a tile that is not
  there and `LayoutRibbonPanels` resizes the panel to what remains.
## select-role

- placement: main ribbon (right, pinned)
- ActionType: Command / role switcher
- target: `FW_RoleSelection` plus a `SessionState` role switch
- caption source: current session role name, formatted
- icon file: `users.png`
- visibility rule: `MenuFormInitializer.vb:125`, always visible (pinned); the selector only opens
  when the user has more than one role
- click behavior: switches session role, reconfigures menu access, updates the caption

## database-config

- placement: Application dashboard (`000_FRAMEWORK\020_DASHBOARDS\Dashboard_Application.vb`), grid cell 2,4
- ActionType: Command / configuration dialog
- target: `FW_DatabaseConfig`, behind `DeveloperAccessGate.Prompt`
- caption source: fixed (`Database Config`)
- icon file: none — `SystemIcons.WinLogo`, matching the other dashboard tiles
- visibility rule: the dashboard itself is Application Admin only; the tile is always shown there
- click behavior: prompts for a developer password, and only on success opens the database
  configuration dialog. The prompt is a guard against a stray click, not a security control — the
  accepted values are compiled in and a .NET assembly can be decompiled. The dashboard being
  Application Admin only is the actual protection.

## dashboard-company

- placement: Application dashboard (`000_FRAMEWORK\020_DASHBOARDS\Dashboard_Application.vb`), grid cell 3,2
- ActionType: Navigation / dialog
- target: `Dashboard_Company`, opened with `ShowDialog`
- caption source: fixed (`Company` / `Dashboard`, on two lines)
- icon file: `Fluent_Home.png`, falling back to `SystemIcons.Application`
- visibility rule: the Application dashboard is Application Admin only; the icon is always shown
  there
- click behavior: passes the owning dashboard's `currentUser` and `accessProfile` through to the
  company dashboard, so it opens knowing who is looking at it

Note on placement: cell 3,2 is where this icon sits in the source. Icons on both dashboards can be
dragged to other cells and their positions are stored in `FW_DashboardLayouts`, so a cell recorded
here is the default rather than necessarily where it will be found.

---

## FW_UserAccessDiagnostic_B

- placement: **both dashboards** — Application (`Dashboard_Application.vb`) and Company
  (`Dashboard_Company.vb`), grid cell 1,4 in each
- ActionType: Navigation / dialog
- target: `FW_UserAccessDiagnostic_B`, opened with `ShowDialog`
- caption source: **the caption chain**, because the button carries
  `.PageName = "FW_UserAccessDiagnostic_B"`. The coded caption is `User Access Diagnostic`; what
  shows is the role override if one is recorded, otherwise the page's `FW_Pages.Table_Alias`.
  That alias is now `User Access Diagnostic`, set by `sql/082`, so all three agree. It has read
  `USERS` and then `User Access Diag.` in the past, neither of which named this page.
- icon file: `Color_Information.png`, falling back to `SystemIcons.Question`
- visibility rule: always shown on either dashboard; the dashboards themselves are the access gate
- click behavior: passes the owning dashboard's `currentUser` and `accessProfile` to the page

Not to be confused with `FW_UserAccessDiagnostic_B`, which is a different page with no button
anywhere and no caller — see Gaps.

---

## Gaps

- `assets/images/helpdesk.png` exists but no catalogued action uses it. Either the Help Desk icon
  is uncatalogued or the asset is orphaned.
- The `dashboard` target needs confirming (see above).
- **`FW_AuditTrail_B` is protected by where it is placed, not by anything in the page.** Its
  constructor is `Public Sub New()` - no `UserContext`, no `AccessProfile` - and its Delete and
  Restore buttons, which soft-delete and restore audit rows, are gated only by whether the deleted
  view is showing. It is reachable from one place, the App Admin dashboard, so today only an
  Application Admin gets there.

  **Before adding a second way in** - a tile, an icon, a button on another page - gate the two
  commands first. `SessionState.IsApplicationAdmin` on both the buttons and their click handlers is
  enough; the page does not need the full access profile, because deleting audit history is an
  administrator act rather than something a role should be able to be granted. Reviewed and left as
  it is on 2026-09-07, deliberately, on the strength of the single entry point.
- `FW_UserAccessDiagnostic_B` has no icon, no menu tile and no caller anywhere in the source. It is
  unreachable code with a stale `FW_Pages` row (alias `Entity`). Removing it is the eight-table
  procedure in `CLAUDE.md`, not a row delete.
- `Dashboard_Company` and `Dashboard_Application` draw Roles identically - `Color_Shield.png` at the
  shared `DashboardIconSize`. Until 2026-09-07 the Company dashboard used `SystemIcons` glyphs, so
  the same action looked different depending on which dashboard it was opened from. The User Admin
  icon that shared this treatment (`Color_Favorites.png`) went from both dashboards on 2026-09-08,
  along with the `UserX` icon on the Application dashboard, which this catalog never recorded.

## ActionKey_UpdateSchema

- placement: `Dashboard_Application` only, grid cell (3, 3)
- ActionType: Command - the one catalogued icon that opens no page
- target: `DataAccess.SyncAllRoleFieldsWithSchema`
- caption source: fixed (`Update Schema`)
- icon file: `Color_Refresh.png`
- visibility rule: none of its own. The admin dashboard is the gate, reached through
  `application-settings` and shown only to an App Admin.
- click behavior: confirms first, naming the three things it does and saying it cannot be undone,
  then sweeps every role and table in every registration and reports the counts it actually got
  back. Deliberately not on `Dashboard_Company`: it writes across every registration, which is not
  a company administrator's to do.
- note: the rules it applies live in `DataAccess.SyncRoleFieldsWithSchema`, the same function the
  Add button in `Roles_U` calls for one role and one table. A stored procedure of that name was
  written in `sql/004` and never installed; `sql/123` retires it, because a second copy of these
  rules in T-SQL is one that can disagree with the first.
