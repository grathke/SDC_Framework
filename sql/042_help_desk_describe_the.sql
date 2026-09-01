/*
    042_help_desk_describe_the.sql
    ----------------------------------------------------------------------------------------------
    Names the thing the first description box is asking about.

    Why
    ---
    The box is titled "Describe The ..." and the word that follows should match what was chosen:
    Suggestion, Request, Problem. Taking the last word of the category name gets most of them right
    and one badly wrong - "Question / How To" gives "Describe The To" - because the meaning is not
    always in the last word.

    DescribeThe holds that word. Null falls back to the last word of the category name, so a
    category added without one still produces a sensible caption.

    Rollback
    --------
    At the bottom of this file.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_HD_IssueCategories')
                 AND name = 'DescribeThe')
BEGIN
    ALTER TABLE dbo.FW_HD_IssueCategories ADD DescribeThe varchar(50) NULL;
END
GO

UPDATE dbo.FW_HD_IssueCategories SET DescribeThe = 'Question'    WHERE CategoryName = 'Question / How To';
UPDATE dbo.FW_HD_IssueCategories SET DescribeThe = 'Suggestion'  WHERE CategoryName = 'Suggestion';
UPDATE dbo.FW_HD_IssueCategories SET DescribeThe = 'Request'     WHERE CategoryName = 'Feature Request';
UPDATE dbo.FW_HD_IssueCategories SET DescribeThe = 'Problem'     WHERE CategoryName = 'Bug Report';
UPDATE dbo.FW_HD_IssueCategories SET DescribeThe = 'Correction'  WHERE CategoryName = 'Data Correction';
UPDATE dbo.FW_HD_IssueCategories SET DescribeThe = 'Permissions' WHERE CategoryName = 'Access / Permissions';
UPDATE dbo.FW_HD_IssueCategories SET DescribeThe = 'Issue'       WHERE CategoryName = 'Other';
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration.
    ----------------------------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_HD_IssueCategories')
             AND name = 'DescribeThe')
BEGIN
    ALTER TABLE dbo.FW_HD_IssueCategories DROP COLUMN DescribeThe;
END
GO
*/
