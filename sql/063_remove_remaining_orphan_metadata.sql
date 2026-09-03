/*
    063_remove_remaining_orphan_metadata.sql
    ==============================================================================================
    The last of the orphaned framework metadata, found by sweeping every FW_ table that keys on a
    page name or a table name rather than by chasing one table at a time.

    Removing a page turns out to leave traces in seven places, not the four that 059 planned for:

        FW_RoleTables        the page's row and its SQL          (059)
        FW_RoleFields        one row per field permission        (059)
        FW_RoleDetails       table captions and role overrides   (059)
        FW_DashboardLayouts  tile position and chosen picture    (059)
        FW_TableLayouts      saved grid column layouts           (062)
        FW_RoleSchema        one row per known table             (this script)
        FW_SavedQbe          saved searches, by table context    (this script)

    Worth having written down. Each was found only because someone asked whether the last one had
    been missed.

    ----------------------------------------------------------------------------------------------
    WHAT GOES
    ----------------------------------------------------------------------------------------------
    FW_RoleSchema, 4 rows. FW_Entity and FW_EntityType, dropped 2026-09-03. FW_Enumerations and
    FW_Enumerations_U, dropped earlier in commit e20839b - orphaned before today and missed then.

    FW_SavedQbe, 2 rows. Saved searches against "Entity Listing" and "Clients Listing", the latter
    being the caption the Entity page carried before it was renamed. Both belong to UserID 2 and
    neither has a page to open in.

    FW_RoleTables, 1 row. PageGeneration_B, stranded by the FW_ prefix rename - FW_PageGeneration_B
    is live and carries its own row.

    FW_AuditTrail, 76 rows. Entity_B, Entity_U, EntityX_U and EntityY_U, all keyed to FW_Entity.

    ----------------------------------------------------------------------------------------------
    A NOTE ON DELETING AUDIT HISTORY
    ----------------------------------------------------------------------------------------------
    This one was argued the other way first, and the reasoning is kept because the argument holds
    everywhere except here.

    An audit row records that something happened - who, when, before, after - and that does not
    stop being true because the page was later deleted. Config for a page that no longer exists is
    unreachable clutter; history for a page that no longer exists can be the only evidence it ever
    ran. The default should be to keep it.

    Removed here because this database has never held real work. Every one of these 76 rows is a
    test edit against a demonstration table, so there is no history to lose. On a database carrying
    real activity, this section should not be copied.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- 1. Schema registry rows for tables that no longer exist.
--    Computed rather than listed, so it also catches anything dropped and forgotten before today.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_RoleSchema
WHERE  DB_Table IS NOT NULL
  AND  LTRIM(RTRIM(DB_Table)) <> ''
  AND  OBJECT_ID('dbo.' + LTRIM(RTRIM(DB_Table))) IS NULL;

PRINT CONCAT('FW_RoleSchema rows deleted: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 2. Saved searches for the removed Entity page, under both captions it ever had.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_SavedQbe
WHERE  TableContext IN ('Entity Listing', 'Clients Listing');

PRINT CONCAT('FW_SavedQbe rows deleted: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 3. The page row stranded by the FW_ rename. Named explicitly: FW_PageGeneration_B is live.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_RoleTables
WHERE  WindowOrPage = 'PageGeneration_B';

PRINT CONCAT('FW_RoleTables rows deleted: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 4. Audit history for the removed table. Keyed on TableName rather than PageName: that is what
--    the rows are actually about, and it catches anything logged from a shared page such as
--    FW_Base_B rather than from Entity_U itself.
--
--    See the note above before copying this to a database that has held real work.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_AuditTrail
WHERE  TableName IN ('FW_Entity', 'FW_EntityType');

PRINT CONCAT('FW_AuditTrail rows deleted: ', @@ROWCOUNT);

COMMIT TRANSACTION;

PRINT '';
PRINT '=== FW_RoleSchema entries whose table is missing (empty is the goal) ===';
SELECT DB_Table FROM dbo.FW_RoleSchema
WHERE  OBJECT_ID('dbo.' + LTRIM(RTRIM(DB_Table))) IS NULL;

PRINT '';
PRINT '=== Remaining saved searches ===';
SELECT SavedQbeID, QbeName, TableContext FROM dbo.FW_SavedQbe ORDER BY SavedQbeID;

PRINT '';
PRINT '=== Audit history that remains, by table ===';
SELECT TableName, COUNT(*) AS Rows
FROM   dbo.FW_AuditTrail
GROUP  BY TableName
ORDER  BY TableName;
