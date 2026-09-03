/*
    062_remove_orphan_table_layouts.sql
    ==============================================================================================
    Removes saved grid layouts for pages that no longer exist.

    FW_TableLayouts holds a JsonState per page per layout type - Default, LastUsed, UserNamed. A
    row for a page that has been deleted is unreachable: nothing will ever ask for it, and it will
    never be cleaned up on its own, because the cleanup would have to be triggered by the page.

    Half the table is in this state. 059 missed it, which is worth saying plainly: the plan for
    removing FW_Entity covered FW_RoleTables, FW_RoleFields, FW_RoleDetails and
    FW_DashboardLayouts, and did not think of layouts. A page leaves traces in five framework
    tables, not four.

    ----------------------------------------------------------------------------------------------
    TWO GROUPS, BOTH DEAD, DIFFERENT VINTAGES
    ----------------------------------------------------------------------------------------------
    Entity pages - 11 rows. Removed from the framework on 2026-09-03. EntityA_B is among them and
    has no source file at all; it was already orphaned before that.

    Pre-prefix page names - 7 rows. PageGeneration_B, Registration_B and UserAccessDiagnostic_B
    were renamed to their FW_ forms, and each of those now carries its own layout rows in this same
    table. These are the old halves, stranded by the rename.

    Verified against the source tree rather than assumed: none of the seven page names above has a
    .vb file.

    Layouts are a convenience, not data. The worst case of being wrong is that a grid opens in its
    default column order.

    Safe to run more than once.
*/

/*
    QUOTED_IDENTIFIER must be ON to delete from this table: it carries an index that requires it,
    and sqlcmd defaults the option OFF where SSMS defaults it ON. Set here rather than passed as
    sqlcmd -I, so the script behaves the same whichever client runs it. Migrations 059 to 061 did
    not need this - they touch tables with no such index.
*/
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

PRINT '=== BEFORE ===';
SELECT PageName, COUNT(*) AS Rows
FROM   dbo.FW_TableLayouts
GROUP  BY PageName
ORDER  BY PageName;

------------------------------------------------------------------------------------------------
-- 1. Entity pages, removed 2026-09-03 along with the table they read.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_TableLayouts
WHERE  PageName IN ('Entity_B', 'Entity_U', 'EntityA_B', 'EntityX_B', 'EntityX_U', 'EntityY_B', 'EntityY_U');

PRINT CONCAT('Entity layout rows deleted: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 2. Page names stranded by the FW_ prefix rename. Named explicitly, because the prefixed
--    versions are live and must not be caught by anything looser.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_TableLayouts
WHERE  PageName IN ('PageGeneration_B', 'Registration_B', 'UserAccessDiagnostic_B');

PRINT CONCAT('Pre-prefix layout rows deleted: ', @@ROWCOUNT);

COMMIT TRANSACTION;

PRINT '';
PRINT '=== AFTER ===';
SELECT PageName, TableName, COUNT(*) AS Rows
FROM   dbo.FW_TableLayouts
GROUP  BY PageName, TableName
ORDER  BY PageName;

PRINT '';
PRINT '=== Any layout whose table no longer exists (empty is the goal) ===';
SELECT DISTINCT PageName, TableName
FROM   dbo.FW_TableLayouts
WHERE  TableName IS NOT NULL
  AND  LTRIM(RTRIM(TableName)) <> ''
  AND  OBJECT_ID('dbo.' + LTRIM(RTRIM(TableName))) IS NULL;
