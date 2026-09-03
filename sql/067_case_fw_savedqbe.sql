/*
    067_case_fw_savedqbe.sql
    ==============================================================================================
    Renames dbo.FW_SavedQbe to dbo.FW_SavedQBE - a change of case only, so that QBE is spelled as
    the acronym it is.

    Nothing functional changes. The database collation is SQL_Latin1_General_CP1_CI_AS, so SQL
    Server already treated the two spellings as the same name, and no VB comparison of a table name
    is case sensitive - the only two StringComparison.Ordinal uses in the codebase compare a
    template placeholder and a block of SQL text, neither of them a table name.

    The code is updated in step regardless. A case-only mismatch between the database and the source
    costs nothing at runtime and quietly breaks searching: a case-sensitive grep for one spelling
    will not find the other, which is how a reference survives the next rename.

    ----------------------------------------------------------------------------------------------
    A CASE-ONLY sp_rename
    ----------------------------------------------------------------------------------------------
    Renaming through an intermediate name. Under a case-insensitive catalog, renaming straight from
    FW_SavedQbe to FW_SavedQBE can be read as renaming an object to the name it already has, which
    some versions refuse. Two hops always work and cost nothing.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF OBJECT_ID('dbo.FW_SavedQbe', 'U') IS NOT NULL
BEGIN
    DECLARE @currentName sysname =
        (SELECT name FROM sys.tables WHERE object_id = OBJECT_ID('dbo.FW_SavedQbe'));

    -- Compare under a case-SENSITIVE collation, or the catalog will report the rename as done
    -- when only the spelling still differs.
    IF @currentName COLLATE Latin1_General_CS_AS <> N'FW_SavedQBE' COLLATE Latin1_General_CS_AS
    BEGIN
        EXEC sp_rename 'dbo.FW_SavedQbe', 'FW_SavedQBE_tmp';
        EXEC sp_rename 'dbo.FW_SavedQBE_tmp', 'FW_SavedQBE';
        PRINT 'Renamed to FW_SavedQBE';
    END
    ELSE
    BEGIN
        PRINT 'Already spelled FW_SavedQBE.';
    END
END

UPDATE dbo.FW_RoleSchema SET DB_Table = 'FW_SavedQBE' WHERE DB_Table = 'FW_SavedQbe';
PRINT CONCAT('FW_RoleSchema updated: ', @@ROWCOUNT);

COMMIT TRANSACTION;

PRINT '';
PRINT '=== The table, as the catalog now spells it ===';
SELECT name FROM sys.tables WHERE name LIKE 'FW[_]SavedQ%';
