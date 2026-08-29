# Icon Catalog

The repository icon catalog referenced by the Action Icon Guardrail. When an actionable icon is
added, changed or removed, update this file in the same change.

Track per icon: ActionKey, placement, ActionType, target, caption source, icon file, visibility
rule, click behavior.

Registration happens in two places:

- `MainMenu.vb` builds the ribbon and owns `UpsertActionTile`, `ConfigureActionVisibility`,
  `SetActionIconFromFile`.
- `MenuFormInitializer.Configure` decides which actions are visible for the session and adds
  page-specific tiles such as `user-admin`.

Icon files live in `assets/images/`.

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
- target: `RoleSelectionForm` plus a `SessionState` role switch
- caption source: current session role name, formatted
- icon file: `users.png`
- visibility rule: `MenuFormInitializer.vb:125`, always visible (pinned); the selector only opens
  when the user has more than one role
- click behavior: switches session role, reconfigures menu access, updates the caption

---

## Gaps

- `assets/images/helpdesk.png` exists but no catalogued action uses it. Either the Help Desk icon
  is uncatalogued or the asset is orphaned.
- The `dashboard` target needs confirming (see above).
