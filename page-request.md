# New Browse and Maintenance Page Request

**Pages will be inherited from `FW_Base_B` and `FW_Base_U`.**

Fill in each `Answer:` line, save the file, then tell me "build the page request".
Question numbers match the in-app generator document, so this form and a document
produced by `PageGeneration_U` are interchangeable.

Leave an optional answer as `Not specified`, `None`, or `Not applicable`.
I read the generation directions from `PageGeneration_U.DefaultGenerationDirections()`
and `.github/new-page-request-manual.md`, so they are not repeated here.

---

**1. Request Name:**
> Free text. Names the request, not the page.

Answer: ``

## Page Request

**2. Generate Browse Page (_B):**
> `True` or `False`. Defaults to `True`.

Answer: `True`

**2a. Use QBE Only on the Browse Page (_B):**
> `Yes` or `No`. Defaults to `No`. `Yes` overrides `OnlyUseQbe()` and hides the CRUD
> buttons while keeping QBE search.

Answer: `No`

**3. Generate Maintenance Page (_U):**
> `True` or `False`. Defaults to `True`. Requires question 2 to be `True` — a `_U` page
> is never generated on its own.

Answer: `True`

**4. Pages To Generate:**
> Base name, no spaces, no `_B`/`_U` suffix. Example: `Department`

Answer: ``

**5. Browse Page Name:**
> Normally leave as the base name plus `_B`.

Answer: `_B`

**6. Maintenance Page Name:**
> Normally leave as the base name plus `_U`.

Answer: `_U`

**7. Underlying Table:**
> The table the `_U` page updates. Example: `dbo.FW_Department`
> Must exist in `dbo` and have a primary key.

Answer: ``

**8. Lookup Fields:**
> Format: `ManagerID -> FW_Users.UserID displayed as FirstLast`
> One per line. `None` if there are no lookups.

Answer: ``

**9. Admin Required Fields:**
> Fields an administrator must complete. Rendered with a trailing `*` and a red border
> when empty during save validation. Or answer `Database-driven rules`.

Answer: ``

**10. Menu or Caller to Open the Page:**
> The specific existing caller or new caller. If this is generic ("Main Menu",
> "Dashboard"), I will stop and ask you to pick the exact caller.

Answer: ``

## Browse SQL

**11. Browse SQL:**
> Must expose the maintenance key explicitly as `PK`.
> Do **not** include `DeletedFlag` or `RowVersion` — the framework handles both.
> Include `RegistrationID` in SELECT only if you want it displayed as a grid column.

```sql

```

**12. Use RegistrationID:**
> `Yes` or `No`. Controls only the `WHERE [RegistrationID] = @RegistrationID` predicate,
> appended as the last WHERE condition. It does not add RegistrationID to the columns.

Answer: ``

**12a. RegistrationID value:**
> Explicit numeric value, e.g. `3`. Never "active session RegistrationID".
> Required when 12 is `Yes`. Controls the page context and the `FW_RoleTables` record.
> If this is missing or ambiguous I will stop and ask before creating anything.

Answer: ``

**13. Use QBE Only on the Browse Page (_B):**
> Repeats 2a in the app's document. Keep both consistent.

Answer: `No`

## Field Selection Metadata

**_B data grid fields:**
> Comma-separated database field names shown in the browse grid.
> Every field must exist in the underlying table.

Value: ``

**_U maintenance fields:**
> Comma-separated database field names appearing on the maintenance page.
> Selecting a field here does not make it Admin Required or a Lookup.

Value: ``

**Order By:**
> Field names in the order they should appear in ORDER BY, each `ASC` or `DESC`.
> May include fields not shown in the grid. Defaults to `PK ASC` if left blank.

Value: ``

## Behavior

**Special Create behavior:**
> `Standard behavior`, `Create is not allowed`, `Standard behavior with defaults: ...`,
> a generated-value rule, or a special validation rule.

Answer: `Standard behavior`

**Special Update behavior:**
> `Standard behavior`, `Update is not allowed`, or a special validation rule.
> Multi-table updates must list every table; related writes share one transaction.

Answer: `Standard behavior`

**Special Delete behavior:**
> `Standard soft delete behavior`, `Delete is not allowed`, or `Physical delete`.
> Physical delete requires reason, approval, audit behavior, and confirmation wording.

Answer: `Standard soft delete behavior`

## Request

Create the new paired pages using the information above.

Create the browse page as a new page inheriting from `FW_Base_B`.
Create the maintenance page as a new page inheriting from `FW_Base_U`.

Do not create roles, role details, role fields, permissions, or access profiles.
