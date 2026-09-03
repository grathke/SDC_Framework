/*
    066_rename_roletables_to_pages.sql
    ==============================================================================================
    Renames dbo.FW_RoleTables to dbo.FW_Pages, its key ID to PageID, and converts RegistrationID
    from nchar(10) to int.

    ----------------------------------------------------------------------------------------------
    WHY THE NAME WAS WRONG
    ----------------------------------------------------------------------------------------------
    There is no RoleID column on this table and never was. Role permissions live in FW_RoleDetails
    and FW_RoleFields. What this holds is one row per page per registration: the page, the table it
    reads, the SELECT it runs, its caption, display order and colour. Base_B reads it on every page
    load to find out what to query.

    The name was confusing enough that the codebase itself uses "RoleTable" for two unrelated
    things - RoleTableAccessEntry and GetRoleTableAccessEntries read FW_RoleDetails, not this table.
    That collision is the clearest evidence the name was carrying the wrong idea.

    FW_Pages, keyed PageID. The key was a bare ID, which CLAUDE.md singles out as the one name that
    cannot survive a join, since two of them collide in a single result set.

    ----------------------------------------------------------------------------------------------
    REGISTRATIONID
    ----------------------------------------------------------------------------------------------
    nchar(10) - a fixed-width string holding a number, space-padded, compared under collation
    padding rules, and the only table in the database to store a registration that way.

    The conversion is free: all 8 rows hold NULL. NULL is meaningful here rather than missing - the
    lookups read WHERE (RegistrationID = @RegistrationID OR RegistrationID IS NULL), so a null row
    applies to every registration. Every row is currently global, which is the intended default,
    not a fault.

    ----------------------------------------------------------------------------------------------
    SCOPE ELSEWHERE
    ----------------------------------------------------------------------------------------------
    Only one row of framework metadata names this table - FW_RoleSchema. It carries no field
    permissions, no saved layouts and no audit history of its own.

    Not rewritten: the migration files under sql that mention the old name. They record what was true when they
    ran.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- 1. RegistrationID to int, while the table still has its old name.
--    Done before the rename so a failure here leaves everything recognisable.
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_NAME = 'FW_RoleTables' AND COLUMN_NAME = 'RegistrationID' AND DATA_TYPE = 'nchar')
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.FW_RoleTables
               WHERE RegistrationID IS NOT NULL
                 AND TRY_CONVERT(int, LTRIM(RTRIM(RegistrationID))) IS NULL)
    BEGIN
        PRINT '*** STOPPED - a RegistrationID value will not convert to int. ***';
        SELECT ID, '[' + RegistrationID + ']' AS RawValue
        FROM   dbo.FW_RoleTables
        WHERE  RegistrationID IS NOT NULL
          AND  TRY_CONVERT(int, LTRIM(RTRIM(RegistrationID))) IS NULL;
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- Trim first: nchar pads to width, and the padding would otherwise be the thing being converted.
    UPDATE dbo.FW_RoleTables
    SET    RegistrationID = LTRIM(RTRIM(RegistrationID))
    WHERE  RegistrationID IS NOT NULL;

    ALTER TABLE dbo.FW_RoleTables ALTER COLUMN RegistrationID int NULL;
    PRINT 'RegistrationID converted to int';
END

------------------------------------------------------------------------------------------------
-- 2. The table and its key.
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.FW_RoleTables', 'U') IS NOT NULL AND OBJECT_ID('dbo.FW_Pages', 'U') IS NULL
BEGIN
    EXEC sp_rename 'dbo.FW_RoleTables', 'FW_Pages';
    PRINT 'Renamed table to FW_Pages';
END

IF COL_LENGTH('dbo.FW_Pages', 'ID') IS NOT NULL AND COL_LENGTH('dbo.FW_Pages', 'PageID') IS NULL
BEGIN
    EXEC sp_rename 'dbo.FW_Pages.ID', 'PageID', 'COLUMN';
    PRINT 'Renamed key to PageID';
END

------------------------------------------------------------------------------------------------
-- 3. Constraints, which sp_rename leaves announcing the old table.
------------------------------------------------------------------------------------------------
DECLARE @old sysname, @new sysname;
DECLARE renames CURSOR LOCAL FAST_FORWARD FOR
    SELECT name, REPLACE(name, 'FW_RoleTables', 'FW_Pages')
    FROM   sys.objects
    WHERE  parent_object_id = OBJECT_ID('dbo.FW_Pages')
      AND  name LIKE '%FW[_]RoleTables%';

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
-- 4. The one metadata row that names it.
------------------------------------------------------------------------------------------------
UPDATE dbo.FW_RoleSchema SET DB_Table = 'FW_Pages' WHERE DB_Table = 'FW_RoleTables';
PRINT CONCAT('FW_RoleSchema updated: ', @@ROWCOUNT);

COMMIT TRANSACTION;

PRINT '';
PRINT '=== ANY REMAINING REFERENCE TO THE OLD NAME (empty is the goal) ===';
SELECT 'FW_RoleSchema' AS Loc, DB_Table AS Value FROM dbo.FW_RoleSchema WHERE DB_Table LIKE '%RoleTables%'
UNION ALL SELECT 'FW_RoleDetails', DB_Table FROM dbo.FW_RoleDetails WHERE DB_Table LIKE '%RoleTables%'
UNION ALL SELECT 'FW_TableLayouts', TableName FROM dbo.FW_TableLayouts WHERE TableName LIKE '%RoleTables%'
UNION ALL SELECT 'FW_AuditTrail', TableName FROM dbo.FW_AuditTrail WHERE TableName LIKE '%RoleTables%'
UNION ALL SELECT 'FW_RoleFields', FileLink FROM dbo.FW_RoleFields WHERE FileLink LIKE 'FW[_]RoleTables%'
UNION ALL SELECT 'constraint', name FROM sys.objects WHERE name LIKE '%FW[_]RoleTables%';

PRINT '';
PRINT '=== FW_Pages ===';
SELECT PageID, RegistrationID, WindowOrPage, DB_Table FROM dbo.FW_Pages ORDER BY PageID;
