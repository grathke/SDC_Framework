/*
    137_generated_pages_owner.sql

    FW_GeneratedPages gains an Owner, and loses CreateAsFrameworkPages.

    The old flag answered half a question: ticked meant the FW_ prefix, and nothing at all decided
    where files were written - a single constant did. That allowed a page named FW_Widget_B to be
    written into an application's folder, a contradiction nothing would catch.

    Owner answers both. It holds the owner's ROOT FOLDER - '000_FRAMEWORK', '100_CTY' - because the
    folder is what the generator reads to offer owners, and a folder that is renamed or removed can
    then be reported against a request rather than silently meaning something else. The prefix is
    derived from it: 000_FRAMEWORK is FW_, everything else is its own name past the number.

    All three existing requests are framework pages - FW_Employees, FW_UserAccessDiagnostic, and
    UsersY, a test page that no longer exists - so every row is backfilled to 000_FRAMEWORK. The old
    flag is unreliable evidence of that: FW_UserAccessDiagnostic_B has it set to 0 while carrying
    the FW_ prefix in its name.

    The flag is dropped rather than kept beside the new column. Two fields answering one question is
    how they come to disagree, and the code no longer reads it.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('dbo.FW_GeneratedPages') AND name = 'Owner')
BEGIN
    ALTER TABLE dbo.FW_GeneratedPages ADD Owner varchar(60) NULL;
    PRINT 'Owner column added';
END
ELSE
BEGIN
    PRINT 'Owner column already exists';
END
GO

SET NOCOUNT ON;

SELECT 'BEFORE' AS Stage, GeneratedPageID, BrowsePageName, MaintenancePageName,
       CASE WHEN COL_LENGTH('dbo.FW_GeneratedPages', 'CreateAsFrameworkPages') IS NULL
            THEN NULL ELSE 1 END AS OldFlagStillPresent,
       Owner
  FROM dbo.FW_GeneratedPages
 ORDER BY GeneratedPageID;

UPDATE dbo.FW_GeneratedPages
   SET Owner = '000_FRAMEWORK'
 WHERE Owner IS NULL OR LTRIM(RTRIM(Owner)) = '';

PRINT 'Requests given an owner: ' + CAST(@@ROWCOUNT AS varchar(10));
GO

/*  The flag the Owner column replaces. Dropped only once every row has an owner, so a half-applied
    run cannot leave the table answering neither question. */
IF EXISTS (SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.FW_GeneratedPages') AND name = 'CreateAsFrameworkPages')
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_GeneratedPages WHERE Owner IS NULL)
BEGIN
    DECLARE @constraint sysname =
        (SELECT TOP 1 dc.name
           FROM sys.default_constraints dc
           JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
          WHERE dc.parent_object_id = OBJECT_ID('dbo.FW_GeneratedPages')
            AND c.name = 'CreateAsFrameworkPages');

    IF @constraint IS NOT NULL
        EXEC('ALTER TABLE dbo.FW_GeneratedPages DROP CONSTRAINT [' + @constraint + ']');

    ALTER TABLE dbo.FW_GeneratedPages DROP COLUMN CreateAsFrameworkPages;
    PRINT 'CreateAsFrameworkPages dropped';
END
ELSE
BEGIN
    PRINT 'CreateAsFrameworkPages already gone, or a row still has no owner';
END
GO

SET NOCOUNT ON;

SELECT 'AFTER' AS Stage, GeneratedPageID, BrowsePageName, MaintenancePageName, Owner
  FROM dbo.FW_GeneratedPages
 ORDER BY GeneratedPageID;
