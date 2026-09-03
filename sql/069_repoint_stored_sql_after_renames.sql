/*
    069_repoint_stored_sql_after_renames.sql
    ==============================================================================================
    Rewrites stored SQL that still names objects renamed earlier today.

    ----------------------------------------------------------------------------------------------
    THE MISS THIS FIXES
    ----------------------------------------------------------------------------------------------
    Migrations 065 to 068 renamed tables and columns and updated every place the framework records
    a NAME - DB_Table, FileLink, TableName, FieldName. None of them touched the places the framework
    stores SQL TEXT, and a browse page's query lives in FW_Pages.Table_SQL as a string.

    So FW_PageGeneration_B kept running:

        SELECT PageRequestID AS PK, ... FROM FW_PageGeneration_B_U

    against a table renamed to FW_GeneratedPages with a key renamed to GeneratedPageID. It failed at
    the point of use with "Invalid object name", which is the right error at the wrong time - the
    rename should have carried it.

    A name lives in three kinds of place, not two:
        1. the catalog          - sys.tables, sys.columns
        2. metadata that names  - DB_Table, FileLink, TableName, FieldName
        3. text that queries    - FW_Pages.Table_SQL, FW_GeneratedPages.BrowseSql

    The third is the one that is easy to forget, because nothing about it looks like a reference
    until it runs.

    ----------------------------------------------------------------------------------------------
    ALSO HERE
    ----------------------------------------------------------------------------------------------
    PageGeneration_B - the unprefixed page name 063 deleted - is back, recreated by the application
    since. It is left in place and its SQL repointed with the rest: something still asks for that
    page key, so deleting it again would only repeat the cycle. Worth finding what recreates it, as
    its own piece of work.

    Written as replacements rather than as literal new SQL, so a page whose query has been edited
    since keeps its edits.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

PRINT '=== BEFORE ===';
SELECT WindowOrPage
FROM   dbo.FW_Pages
WHERE  Table_SQL LIKE '%FW[_]PageGeneration[_]B[_]U%'
   OR  Table_SQL LIKE '%PageRequestID%'
   OR  Table_SQL LIKE '%FW[_]RoleTables%'
   OR  Table_SQL LIKE '%FW[_]Entity%'
   OR  Table_SQL LIKE '%RegTypeId%'
   OR  Table_SQL LIKE '%Use2FA%';

------------------------------------------------------------------------------------------------
-- 1. Page browse SQL. Table first, then key: the key rename is meaningless without the table.
------------------------------------------------------------------------------------------------
UPDATE dbo.FW_Pages
SET    Table_SQL = REPLACE(Table_SQL, 'FW_PageGeneration_B_U', 'FW_GeneratedPages')
WHERE  Table_SQL LIKE '%FW[_]PageGeneration[_]B[_]U%';
PRINT CONCAT('Table_SQL repointed to FW_GeneratedPages: ', @@ROWCOUNT);

UPDATE dbo.FW_Pages
SET    Table_SQL = REPLACE(Table_SQL, 'PageRequestID', 'GeneratedPageID')
WHERE  Table_SQL LIKE '%PageRequestID%';
PRINT CONCAT('Table_SQL repointed to GeneratedPageID: ', @@ROWCOUNT);

UPDATE dbo.FW_Pages
SET    Table_SQL = REPLACE(Table_SQL, 'FW_RoleTables', 'FW_Pages')
WHERE  Table_SQL LIKE '%FW[_]RoleTables%';
PRINT CONCAT('Table_SQL repointed to FW_Pages: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 2. The column renames from 068. Anchored to the owning table's spelling: FW_RegistrationType
--    has its own RegTypeID and must not be caught.
------------------------------------------------------------------------------------------------
UPDATE dbo.FW_Pages
SET    Table_SQL = REPLACE(Table_SQL, 'r.RegTypeId', 'r.RegistrationTypeID')
WHERE  Table_SQL LIKE '%r.RegTypeId%';
PRINT CONCAT('Table_SQL repointed to RegistrationTypeID: ', @@ROWCOUNT);

UPDATE dbo.FW_Pages
SET    Table_SQL = REPLACE(Table_SQL, 'Use2FA', 'TwoFactorAuthentication')
WHERE  Table_SQL LIKE '%Use2FA%';
PRINT CONCAT('Table_SQL repointed to TwoFactorAuthentication: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 3. The generator's own stored browse SQL, which becomes a page's SQL when regenerated.
------------------------------------------------------------------------------------------------
UPDATE dbo.FW_GeneratedPages
SET    BrowseSql = REPLACE(REPLACE(BrowseSql, 'FW_PageGeneration_B_U', 'FW_GeneratedPages'),
                           'PageRequestID', 'GeneratedPageID')
WHERE  BrowseSql LIKE '%FW[_]PageGeneration[_]B[_]U%'
   OR  BrowseSql LIKE '%PageRequestID%';
PRINT CONCAT('BrowseSql repointed: ', @@ROWCOUNT);

COMMIT TRANSACTION;

------------------------------------------------------------------------------------------------
-- 4. Nothing stored should still name a renamed object.
------------------------------------------------------------------------------------------------
PRINT '';
PRINT '=== ANY STORED SQL STILL NAMING A RENAMED OBJECT (empty is the goal) ===';

SELECT 'FW_Pages.Table_SQL' AS Loc, WindowOrPage AS Which
FROM   dbo.FW_Pages
WHERE  Table_SQL LIKE '%FW[_]PageGeneration[_]B[_]U%'
   OR  Table_SQL LIKE '%PageRequestID%'
   OR  Table_SQL LIKE '%FW[_]RoleTables%'
   OR  Table_SQL LIKE '%FW[_]Entity%'
   OR  Table_SQL LIKE '%Use2FA%'
UNION ALL
SELECT 'FW_GeneratedPages.BrowseSql', RequestName
FROM   dbo.FW_GeneratedPages
WHERE  BrowseSql LIKE '%FW[_]PageGeneration[_]B[_]U%'
   OR  BrowseSql LIKE '%PageRequestID%'
   OR  BrowseSql LIKE '%FW[_]Entity%';

PRINT '';
PRINT '=== The page generation SQL now ===';
SELECT WindowOrPage, Table_SQL FROM dbo.FW_Pages WHERE WindowOrPage LIKE '%PageGeneration%';
