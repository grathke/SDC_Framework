/*
    078_remove_fw_users_pages.sql

    Removes the metadata left behind by the four pages deleted on 2026-09-08:

        UserX_B, UserX_U, UsersY_B, UsersY_U, Users_AppAdmin_B, Users_AppAdmin_U

    FW_Users is being replaced by an Employee table, so every page that maintained it has gone
    from the source. Their rows have not, and CLAUDE.md records what that costs: removing
    FW_Entity was planned against four tables and the other four surfaced one at a time.

    WHAT IS DELIBERATELY KEPT
    -------------------------
    FW_GeneratedPages   The generation requests. They hold every field choice made for these
                        pages, and the Employee pages will be generated from something similar.
                        Re-entering them by hand is the expensive part; the rows are cheap.

    FW_AuditTrail       An audit row records that something happened, and that stays true after
                        the page is deleted.

    SAFE TO RE-RUN. Every statement is a delete against a name that no longer exists.

    Run inside the transaction below. Read the counts it prints before committing: an unexpected
    number is the signal that a page name here is wrong.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Pages TABLE (PageName SYSNAME PRIMARY KEY);
INSERT INTO @Pages (PageName)
VALUES ('UserX_B'), ('UserX_U'),
       ('UsersY_B'), ('UsersY_U'),
       ('Users_AppAdmin_B'), ('Users_AppAdmin_U');

BEGIN TRANSACTION;

    -- 1. The page rows themselves: SQL, alias, background colour, hot-fields flag.
    DELETE p
    FROM dbo.FW_Pages AS p
    INNER JOIN @Pages AS x ON x.PageName = p.WindowOrPage;
    PRINT 'FW_Pages: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 2. Field permissions. One row per role per field - the big one, often over a hundred.
    --    Keyed by FileLink as 'Table.Field', so this clears by table rather than by page.
    DELETE FROM dbo.FW_RoleFields
    WHERE TableName = 'FW_Users';
    PRINT 'FW_RoleFields: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 3. Table captions and per-role overrides.
    DELETE FROM dbo.FW_RoleDetails
    WHERE DB_Table = 'FW_Users';
    PRINT 'FW_RoleDetails: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 4. The known-tables list. FW_Users itself still exists as a table, so this row STAYS.
    --    Left here as a statement of intent rather than an omission.
    PRINT 'FW_RoleSchema: 0 (FW_Users still exists as a table)';

    -- 5. Dashboard tile positions and chosen pictures, keyed by ActionKey.
    DELETE FROM dbo.FW_DashboardLayouts
    WHERE ActionKey IN ('ActionKey_UserX_B', 'ActionKey_UserAdmin', 'generated-usersy_b');
    PRINT 'FW_DashboardLayouts: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 6. Saved grid column layouts, per page and per user.
    DELETE t
    FROM dbo.FW_TableLayouts AS t
    INNER JOIN @Pages AS x ON x.PageName = t.PageName;
    PRINT 'FW_TableLayouts: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 7. Saved searches. Keyed by table CONTEXT - the page's caption, not its name - which is why
    --    this matches on the captions those pages carried rather than on the page names.
    DELETE FROM dbo.FW_SavedQbe
    WHERE TableContext IN ('UserX', 'UsersY', 'User Admin', 'USERS', 'FW_Users');
    PRINT 'FW_SavedQbe: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

/*
    The two sweeps. Neither should return rows. Run them after committing.
*/

-- Role and layout rows whose table no longer exists.
SELECT 'FW_RoleDetails' AS Source, DB_Table AS MissingTable
FROM dbo.FW_RoleDetails
WHERE OBJECT_ID('dbo.' + DB_Table) IS NULL
UNION ALL
SELECT 'FW_RoleSchema', DB_Table
FROM dbo.FW_RoleSchema
WHERE OBJECT_ID('dbo.' + DB_Table) IS NULL;

-- Views and procedures with unresolved references.
SELECT OBJECT_NAME(referencing_id) AS ReferencingObject,
       referenced_entity_name       AS MissingReference
FROM sys.sql_expression_dependencies
WHERE referenced_id IS NULL;

/*
    NOT ADDRESSED HERE, and needing their own decision:

    vw_FW_CurrentUser and vw_FW_UserRoles still read FW_Users, which is correct today and will
    not be once the columns move to Employee. vw_FW_UserRoles selects u.FirstLast specifically.
    Both are database views, so changing them needs explicit approval.
*/
