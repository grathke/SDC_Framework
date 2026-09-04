/*
    074_unique_user_email.sql
    ==============================================================================================
    A unique index on dbo.FW_Users.Email.

    ----------------------------------------------------------------------------------------------
    WHY AN INDEX AS WELL AS THE CODE
    ----------------------------------------------------------------------------------------------
    The application refuses a duplicate email in two places already - the page validation and the
    write boundary in TrySaveGeneratedPageRecord. Both are code, and today proved what code-only
    rules are worth: the password contract lived in one form and a generated page walked straight
    past it.

    An index is the version that holds for a path nobody has written yet, and for a restore, a
    script, or a hand-edit in SSMS. The code gives the readable message; the index gives the
    guarantee.

    ----------------------------------------------------------------------------------------------
    WHY A PLAIN INDEX IS ENOUGH NOW
    ----------------------------------------------------------------------------------------------
    It would not have been an hour ago. Login compares LOWER(REPLACE(Email, ' ', '')), so
    'a@b.com' and 'a @b.com' are one address to it and two rows to a plain index - a guarantee with
    a hole exactly where the check was needed.

    The fix was to normalise on the way in rather than on every read. NormalizeEmailForStorage now
    lowercases, trims and removes spaces before the value reaches the column, in both the FW_Users
    insert and update and in the generated-page write. So the stored value is already the form a
    lookup asks for, and a plain unique index means precisely what the application means.

    Case needs no help: the database collates SQL_Latin1_General_CP1_CI_AS.

    That also avoids a computed column, whose index would have imposed QUOTED_IDENTIFIER ON on
    every future write to FW_Users - the error this session hit twice against FW_TableLayouts.

    ----------------------------------------------------------------------------------------------
    SCOPE
    ----------------------------------------------------------------------------------------------
    Filtered on Email IS NOT NULL only. Deleted rows are deliberately included: the application
    refuses to reuse an address held by a soft-deleted user, because allowing it would create the
    duplicate the moment somebody restored them - and RestoreUser has no check of its own. An index
    that ignored deleted rows would leave that door open.

    All four rows were already canonical when this ran, so nothing needed converting.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

------------------------------------------------------------------------------------------------
-- 1. Refuse rather than fail halfway: name the duplicates instead of letting CREATE INDEX do it.
------------------------------------------------------------------------------------------------
IF EXISTS (
    SELECT 1
    FROM   dbo.FW_Users
    WHERE  Email IS NOT NULL
    GROUP  BY Email
    HAVING COUNT(*) > 1
)
BEGIN
    PRINT '*** STOPPED - these emails are held by more than one row: ***';
    SELECT Email, COUNT(*) AS Rows
    FROM   dbo.FW_Users
    WHERE  Email IS NOT NULL
    GROUP  BY Email
    HAVING COUNT(*) > 1;
    RETURN;
END

------------------------------------------------------------------------------------------------
-- 2. The index.
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FW_Users_Email' AND object_id = OBJECT_ID('dbo.FW_Users'))
BEGIN
    CREATE UNIQUE INDEX UX_FW_Users_Email
        ON dbo.FW_Users (Email)
        WHERE Email IS NOT NULL;
    PRINT 'Created UX_FW_Users_Email';
END
ELSE
BEGIN
    PRINT 'UX_FW_Users_Email already exists.';
END

PRINT '';
PRINT '=== Unique indexes on FW_Users ===';
SELECT i.name AS IndexName,
       i.is_unique,
       ISNULL(i.filter_definition, '(unfiltered)') AS Filter,
       COL_NAME(ic.object_id, ic.column_id) AS ColumnName
FROM   sys.indexes AS i
       JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
WHERE  i.object_id = OBJECT_ID('dbo.FW_Users')
  AND  i.is_unique = 1
ORDER  BY i.name;
