# New Browse and Maintenance Page Request

**Pages will be inherited from `FW_Base_B` and `FW_Base_U`.**

**1. What name should both pages use?**
Answer: `<TableNameWithoutSpaces>`

Use this template when requesting a new paired `_B` browse page and `_U` maintenance page.

## Page Identity

**2. Use the same base name for the Browse and Maintenance pages:**
Answer: `Yes` (default)

**3. Browse page name:**
Answer: `<Name to use>_B` (append automatically)

**4. Maintenance page name:**
Answer: `<Name to use>_U` (append automatically)

If the answer above is `No`, provide separate Browse and Maintenance base names and explain why.

**5. Underlying table:**
Answer: `<dbo.FW_TableName>`

**6. RegistrationID:**
Answer: `<explicit numeric value, for example 1>`

## Browse SQL

**7. Browse SQL:**
Answer: `Paste the complete SQL statement below.`

The framework handles `DeletedFlag` and `RowVersion` automatically. Do not include them in the SQL. Include `RegistrationID` in the SQL only when the table requires registration filtering.

```sql
SELECT
    t.ID AS PK,
    t.ID,
    t.RegistrationID,
    t.FieldName,
    t.DeletedFlag,
    t.RowVersion
FROM dbo.FW_TableName AS t
WHERE t.RegistrationID = @RegistrationID
  AND ISNULL(t.DeletedFlag, 0) = 0
ORDER BY t.FieldName;
```

## Optional Details

For optional fields, leave the prompt in place and answer with `Not specified`, `None`, or `Not applicable` when needed. Do not treat the example placeholder text as an actual value.

**8. Fields that should be editable:**
Answer: `<list fields or say all standard fields>`

**9. Fields that should be read-only:**
Answer: `<list fields>`

**10. Lookup fields:**
Answer: `<field -> lookup table>`

**11. Admin Required fields:**
Answer: `<list fields or say database-driven rules>`

**12. Special Create behavior:**
Answer: `<Standard behavior, Create is not allowed, or describe special behavior>`

**13. Special Update behavior:**
Answer: `<Standard behavior, Update is not allowed, or describe special behavior>`

**14. Special Delete behavior:**
Answer: `<Standard soft delete behavior, Delete is not allowed, or describe special behavior>`

**15. Menu or caller to open the page:**
Answer: `<existing caller or new caller>`

## Required Rules

- Use `FW_Base_B` for the browse page.
- Use `FW_Base_U` for the maintenance page.
- The supplied SQL must expose the maintenance key explicitly as `PK`.
- Do not include `DeletedFlag` or `RowVersion` in the supplied SQL; the framework handles them automatically.
- Include `RegistrationID` and its filter in the supplied SQL only when the table is registration-scoped.
- The `_U` page must explicitly return the underlying table from `GetTableNameOverride()`.
- Controls must use database field names, for example `TextBox_FieldName`.
- Controls must have data bindings to the corresponding model fields.
- Preserve RowVersion through load, clone, form binding, and save.
- Preserve the selected row and scroll position after returning from `_U`.
- Use the active session user and access profile.
- Use the explicitly supplied `RegistrationID` value for the page context; never infer it from the active session.
- If the `FW_RoleTables` record is missing for the page and supplied RegistrationID, create it with the supplied SQL, table name, friendly alias, RegistrationID, and CreatedBy.
- If `RegistrationID` is missing, invalid, or ambiguous, stop and ask for it before creating files or database metadata.
- Verify the resulting `FW_RoleTables` record against the actual database.
- Derive the friendly name by removing `FW_`, splitting underscores, and formatting PascalCase words.

## Do Not Create Automatically

Do not create any of the following unless explicitly requested:

- Roles
- Role details
- Role fields
- Permission records
- Access profiles

Creating or updating a `FW_RoleTables` metadata record is allowed and is separate from creating permissions.

## SQL Table Inference

If the underlying table is omitted, infer it only when the SQL has one clear base table, such as:

```sql
FROM dbo.FW_Gender AS g
```

For joins, views, stored procedures, aggregates, or multiple possible update tables, stop and ask for the underlying maintenance table. Do not guess.

## Expected Completion Checks

- Create a restore point before editing shared framework files.
- Inspect all existing related pages and callers before editing.
- Build the project.
- Run the browse regression script.
- Verify the `FW_RoleTables` row against the actual database.
- Verify Create, Read, Update, Delete, Cancel, missing schema, missing metadata, conflict, and retry paths as applicable.
- Launch the application and manually verify the actual page workflow.
- Create a new good-baseline restore point only after the runtime workflow is confirmed successful.

## Request

Create the new paired pages using the information above.

Create the browse page as a new page inheriting from `FW_Base_B`.

Create the maintenance page as a new page inheriting from `FW_Base_U`.

Do not create roles, role details, role fields, permissions, or access profiles.
