/*
    039_help_desk_expected_behavior.sql
    ----------------------------------------------------------------------------------------------
    Splits a help desk report into what happened and what should have happened.

    Why
    ---
    A report that says only "the search filter clears after refresh" is a symptom. Acting on it
    means guessing the intent. Capturing the expected behaviour alongside the problem turns a
    ticket into something that can be worked - by a person or by an assistant it is pasted into.

    Which categories ask for it is data, not code: RequiresExpectedBehavior is set on the
    categories where the question makes sense, so adding a category is a row rather than an edit
    to FW_HD_Issues_U.

        Problem            - something is not working as expected
        Suggestion         - the expectation IS the suggestion
        Feature Request    - the expectation IS the request
        Bug Report         - a reproducible defect

    Off for Question / How To, Data Correction, Access / Permissions and Other, where there is no
    "should work" to describe.

    Rollback
    --------
    At the bottom of this file.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_HD_Issues')
                 AND name = 'ExpectedBehavior')
BEGIN
    ALTER TABLE dbo.FW_HD_Issues ADD ExpectedBehavior varchar(max) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_HD_IssueCategories')
                 AND name = 'RequiresExpectedBehavior')
BEGIN
    ALTER TABLE dbo.FW_HD_IssueCategories
        ADD RequiresExpectedBehavior bit NOT NULL CONSTRAINT DF_FW_HD_IssueCategories_RequiresExpectedBehavior DEFAULT (0);
END
GO

UPDATE dbo.FW_HD_IssueCategories
SET    RequiresExpectedBehavior = 1
WHERE  CategoryName IN ('Problem', 'Suggestion', 'Feature Request', 'Bug Report');
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration.
    ----------------------------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_HD_IssueCategories')
             AND name = 'RequiresExpectedBehavior')
BEGIN
    ALTER TABLE dbo.FW_HD_IssueCategories DROP CONSTRAINT DF_FW_HD_IssueCategories_RequiresExpectedBehavior;
    ALTER TABLE dbo.FW_HD_IssueCategories DROP COLUMN RequiresExpectedBehavior;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_HD_Issues')
             AND name = 'ExpectedBehavior')
BEGIN
    ALTER TABLE dbo.FW_HD_Issues DROP COLUMN ExpectedBehavior;
END
GO
*/
