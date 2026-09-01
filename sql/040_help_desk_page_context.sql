/*
    040_help_desk_page_context.sql
    ----------------------------------------------------------------------------------------------
    Records which page a help desk report came from, and which categories require one.

    Why
    ---
    A defect cannot be acted on without knowing where it happened, and by the time a user opens the
    help desk they have left the page. So the page is captured rather than asked for: the Help Desk
    button on every _B and _U page passes its own name in.

    RequiresPage is separate from RequiresExpectedBehavior because they answer different questions.
    A Suggestion needs "how it should work" - that is the suggestion - but it does not belong to any
    one page. Only a Problem or a Bug Report does.

        RequiresExpectedBehavior  Problem, Suggestion, Feature Request, Bug Report
        RequiresPage              Problem, Bug Report

    Opened from a page, every category is offered. Opened from the main menu, the ones needing a
    page are not, because there is nothing truthful to record.

    Rollback
    --------
    At the bottom of this file.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_HD_Issues')
                 AND name = 'ReportedFromPage')
BEGIN
    ALTER TABLE dbo.FW_HD_Issues ADD ReportedFromPage varchar(100) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_HD_IssueCategories')
                 AND name = 'RequiresPage')
BEGIN
    ALTER TABLE dbo.FW_HD_IssueCategories
        ADD RequiresPage bit NOT NULL CONSTRAINT DF_FW_HD_IssueCategories_RequiresPage DEFAULT (0);
END
GO

UPDATE dbo.FW_HD_IssueCategories
SET    RequiresPage = 1
WHERE  CategoryName IN ('Problem', 'Bug Report');
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration.
    ----------------------------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_HD_IssueCategories')
             AND name = 'RequiresPage')
BEGIN
    ALTER TABLE dbo.FW_HD_IssueCategories DROP CONSTRAINT DF_FW_HD_IssueCategories_RequiresPage;
    ALTER TABLE dbo.FW_HD_IssueCategories DROP COLUMN RequiresPage;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_HD_Issues')
             AND name = 'ReportedFromPage')
BEGIN
    ALTER TABLE dbo.FW_HD_Issues DROP COLUMN ReportedFromPage;
END
GO
*/
