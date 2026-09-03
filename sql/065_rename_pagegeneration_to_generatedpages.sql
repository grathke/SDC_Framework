/*
    065_rename_pagegeneration_to_generatedpages.sql
    ==============================================================================================
    Renames dbo.FW_PageGeneration_B_U to dbo.FW_GeneratedPages, and its key PageRequestID to
    GeneratedPageID.

    The old name was a page name wearing a table's clothes. FW_PageGeneration_B_U reads as the pair
    of pages that maintain the rows rather than as the thing the rows are - and the pages have since
    been renamed to FW_PageGeneration_B anyway, so the table was named after something that no
    longer exists in that form. FW_GeneratedPages says what a row is: a generated page.

    The key follows the convention in CLAUDE.md - the table name past FW_, singular, no underscore,
    ID uppercase - which makes GeneratedPageID. The convention is for new tables, and says to rename
    an existing key only when the table is being reworked anyway. It is being reworked now, and this
    is the last cheap moment: nothing declares a foreign key against this key, so the rename reaches
    code and metadata but no relationships.

    ----------------------------------------------------------------------------------------------
    A NAME LIVES IN MORE PLACES THAN THE TABLE
    ----------------------------------------------------------------------------------------------
    This is the same list that 059 to 064 worked through, in reverse. A rename that changes only
    sys.tables leaves every one of these pointing at a name that no longer resolves:

        FW_RoleTables.DB_Table          1 row
        FW_RoleFields.FileLink         17 rows
        FW_RoleDetails.DB_Table         1 row
        FW_RoleSchema.DB_Table          1 row
        FW_TableLayouts.TableName       2 rows
        FW_AuditTrail.TableName        92 rows

    The audit rows are rewritten rather than left. They describe the same table under a former name,
    and leaving them keyed to a string that resolves to nothing recreates the orphan class that
    059-064 just cleared.

    Default constraints are renamed too. sp_rename does not touch them, so they would otherwise keep
    announcing a table that no longer exists.

    Not rewritten: the comments in migrations 059 to 064. Those record what was true when they ran.

    ----------------------------------------------------------------------------------------------
    KNOWN, LEFT ALONE
    ----------------------------------------------------------------------------------------------
    One FW_RoleFields row reads FW_PageGeneration_B_U.RegistrationID. There is no such column - the
    table spells it UseRegistrationID - so the row governs nothing. It becomes
    FW_GeneratedPages.RegistrationID here, still governing nothing. Pre-existing, harmless, and its
    intent is unknown, so it is renamed rather than deleted or guessed at.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- 1. The table and its key.
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.FW_PageGeneration_B_U', 'U') IS NOT NULL
   AND OBJECT_ID('dbo.FW_GeneratedPages', 'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.FW_PageGeneration_B_U', 'FW_GeneratedPages';
    PRINT 'Renamed table to FW_GeneratedPages';
END

IF COL_LENGTH('dbo.FW_GeneratedPages', 'PageRequestID') IS NOT NULL
   AND COL_LENGTH('dbo.FW_GeneratedPages', 'GeneratedPageID') IS NULL
BEGIN
    EXEC sp_rename 'dbo.FW_GeneratedPages.PageRequestID', 'GeneratedPageID', 'COLUMN';
    PRINT 'Renamed key to GeneratedPageID';
END

------------------------------------------------------------------------------------------------
-- 2. Default constraints, which sp_rename leaves behind.
------------------------------------------------------------------------------------------------
DECLARE @old sysname, @new sysname;
DECLARE renames CURSOR LOCAL FAST_FORWARD FOR
    SELECT name, REPLACE(name, 'FW_PageGeneration_B_U', 'FW_GeneratedPages')
    FROM   sys.objects
    WHERE  parent_object_id = OBJECT_ID('dbo.FW_GeneratedPages')
      AND  name LIKE '%FW[_]PageGeneration[_]B[_]U%';

OPEN renames;
FETCH NEXT FROM renames INTO @old, @new;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC sp_rename @old, @new;
    PRINT CONCAT('Renamed constraint ', @old, ' -> ', @new);
    FETCH NEXT FROM renames INTO @old, @new;
END
CLOSE renames;
DEALLOCATE renames;

------------------------------------------------------------------------------------------------
-- 3. Everywhere the framework recorded the old name.
------------------------------------------------------------------------------------------------
UPDATE dbo.FW_RoleTables  SET DB_Table   = 'FW_GeneratedPages' WHERE DB_Table   = 'FW_PageGeneration_B_U';
PRINT CONCAT('FW_RoleTables updated: ', @@ROWCOUNT);

UPDATE dbo.FW_RoleDetails SET DB_Table   = 'FW_GeneratedPages' WHERE DB_Table   = 'FW_PageGeneration_B_U';
PRINT CONCAT('FW_RoleDetails updated: ', @@ROWCOUNT);

UPDATE dbo.FW_RoleSchema  SET DB_Table   = 'FW_GeneratedPages' WHERE DB_Table   = 'FW_PageGeneration_B_U';
PRINT CONCAT('FW_RoleSchema updated: ', @@ROWCOUNT);

UPDATE dbo.FW_TableLayouts SET TableName = 'FW_GeneratedPages' WHERE TableName  = 'FW_PageGeneration_B_U';
PRINT CONCAT('FW_TableLayouts updated: ', @@ROWCOUNT);

UPDATE dbo.FW_AuditTrail  SET TableName  = 'FW_GeneratedPages' WHERE TableName  = 'FW_PageGeneration_B_U';
PRINT CONCAT('FW_AuditTrail updated: ', @@ROWCOUNT);

UPDATE dbo.FW_RoleFields
SET    FileLink = 'FW_GeneratedPages' + SUBSTRING(FileLink, LEN('FW_PageGeneration_B_U') + 1, 200)
WHERE  FileLink LIKE 'FW[_]PageGeneration[_]B[_]U.%';
PRINT CONCAT('FW_RoleFields updated: ', @@ROWCOUNT);

COMMIT TRANSACTION;

------------------------------------------------------------------------------------------------
-- 4. Nothing should still name the old table.
------------------------------------------------------------------------------------------------
PRINT '';
PRINT '=== ANY REMAINING REFERENCE TO THE OLD NAME (empty is the goal) ===';

SELECT 'FW_RoleTables'   AS Loc, DB_Table  AS Value FROM dbo.FW_RoleTables   WHERE DB_Table  LIKE '%PageGeneration%'
UNION ALL SELECT 'FW_RoleDetails',  DB_Table  FROM dbo.FW_RoleDetails  WHERE DB_Table  LIKE '%PageGeneration%'
UNION ALL SELECT 'FW_RoleSchema',   DB_Table  FROM dbo.FW_RoleSchema   WHERE DB_Table  LIKE '%PageGeneration%'
UNION ALL SELECT 'FW_TableLayouts', TableName FROM dbo.FW_TableLayouts WHERE TableName LIKE '%PageGeneration%'
UNION ALL SELECT 'FW_AuditTrail',   TableName FROM dbo.FW_AuditTrail   WHERE TableName LIKE '%PageGeneration%'
UNION ALL SELECT 'FW_RoleFields',   FileLink  FROM dbo.FW_RoleFields   WHERE FileLink  LIKE 'FW[_]PageGeneration%'
UNION ALL SELECT 'constraint',      name      FROM sys.objects         WHERE name      LIKE '%FW[_]PageGeneration[_]B[_]U%';

PRINT '';
PRINT '=== The renamed table ===';
SELECT TOP 3 GeneratedPageID, RequestName, BrowsePageName, UnderlyingTableName
FROM   dbo.FW_GeneratedPages
ORDER  BY GeneratedPageID;
