# Icon Catalog

The repository icon catalog referenced by the Action Icon Guardrail. When an actionable icon is
added, changed or removed, update this file in the same change.

Track per icon: ActionKey, placement, ActionType, target, caption source, icon file, visibility
rule, click behavior.

Registration happens in two places:

- `03 FW MainMenu.vb` builds the ribbon and owns `UpsertActionTile`, `ConfigureActionVisibility`,
  `SetActionIconFromFile`.
- `MenuFormInitializer.Configure` decides which actions are visible for the session and adds
  page-specific tiles such as `user-admin`.

Icon files live in `assets/images/`.

## Dashboard icon pictures can be changed at runtime

The two dashboards are not catalogued icon by icon here, and their pictures are no longer settled
by their source alone. An App Admin can right-click any dashboard icon and choose a different
graphic; the choice is stored in `FW_DashboardLayouts.IconFileName`, keyed on `DashboardName` and
the icon's `ActionKey`, and applied on the next load.

- The filename written in `02 FW Dashboard_Application.vb` or `02 FW Dashboard_Company.vb` is the
  **default**, not necessarily what is on screen. "Reset to Default" clears the override and
  returns to it.
- The choice is global. `sql/055` removed `RegistrationID` from that table because neither
  dashboard is registration aware, so there is no scope to disagree about.
- The picker is `IconPicker`, the same dialog the page generator uses, so a name means the same
  thing in both places: a file in `assets/images/`, or a built-in glyph marked `system:`.
- Only an App Admin sees the menu, and `DataAccess.SaveDashboardIconOverride` refuses anybody else
  regardless — a hidden menu item is not authorization.

---

## close

- placement: main ribbon (left)
- ActionType: Command
- target: logout / close menu
- caption source: fixed (`Close`)
- icon file: `close.png`
- visibility rule: always visible
- click behavior: closes the menu session

## dashboard

- placement: main ribbon (left)
- ActionType: Page
- target: **unresolved** — the note this came from named `HelloWorldPageForm`, which no longer
  exists outside `project-backup/`. Confirm the current target before relying on this entry.
- caption source: fixed (`Dashboard`)
- icon file: `dashboard.png`
- visibility rule: `ConfigureActionVisibility("dashboard", True, True)` in `MenuFormInitializer.vb:123`
- click behavior: opens the dashboard page dialog

## application-settings

- placement: main ribbon (left)
- ActionType: role-routed page/command
- target: AppAdmin -> `AppAdminSettingsForm`; CompanyAdmin -> `CompanyAdminSettingsForm`
- caption source: role-driven (App Admin Settings / Company Admin Settings)
- icon file: `gear.png`
- visibility rule: `MenuFormInitializer.vb:118`, visible and enabled when
  `SessionState.Current.IsApplicationAdminRole` or `IsCompanyAdminRole`
- click behavior: opens the settings form for the active admin role
- note: inside either settings form, the Roles and User Admin icons stay enabled at all times

## users

- placement: main ribbon (left)
- ActionType: Table/Page
- target: `Roles_B` (`ROLES`)
- caption source: fixed (`Users`)
- icon file: `users.png`
- visibility rule: `MenuFormInitializer.vb:120`, always visible and enabled
- click behavior: opens the `Roles_B` dialog

## entity

- placement: main ribbon (left)
- ActionType: Table
- target: `FW_Entity` via `Entity_B`
- caption source: `FW_RoleDetails.OverrideCaption` for `DB_Table = FW_Entity`, fallback `Entity`
- icon file: `clients.png`
- visibility rule: `MenuFormInitializer.vb:121`, always visible and enabled
- click behavior: opens `Entity_B` with access key `FW_Entity`

## user-admin

- placement: main ribbon (left)
- ActionType: Page
- target: `Users_AppAdmin_B`
- caption source: fixed (`User Administration`)
- icon file: `users.png`
- visibility rule: added by `MenuFormInitializer.vb:128`; currently always visible
- click behavior: opens the `Users_AppAdmin_B` dialog

## my-profile

- placement: main ribbon (right, pinned)
- ActionType: Command/Page placeholder
- target: not yet implemented
- caption source: fixed (`My Profile`)
- icon file: `my-profile.png`
- visibility rule: always visible (pinned)
- click behavior: placeholder message

## login-as-substitute

- placement: main ribbon (right, pinned)
- ActionType: Command/Page placeholder
- target: not yet implemented
- caption source: fixed (`LOGIN AS SUBSTITUE USER`)
- icon file: `substitute-user.png`
- visibility rule: always visible (pinned)
- click behavior: placeholder message

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

- placement: Application dashboard (`02 FW Dashboard_Application.vb`), grid cell 2,4
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

- placement: Application dashboard (`02 FW Dashboard_Application.vb`), grid cell 3,2
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

## Gaps

- `assets/images/helpdesk.png` exists but no catalogued action uses it. Either the Help Desk icon
  is uncatalogued or the asset is orphaned.
- The `dashboard` target needs confirming (see above).
