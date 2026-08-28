USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.FW_HD_Issues', N'IssueNumber') IS NULL
BEGIN
    ALTER TABLE dbo.FW_HD_Issues ADD IssueNumber VARCHAR(30) NULL;
END;

EXEC sys.sp_executesql N'
UPDATE dbo.FW_HD_Issues
SET IssueNumber = ''HD-'' + RIGHT(''000000'' + CONVERT(VARCHAR(20), IssueID), 6)
WHERE IssueNumber IS NULL;

ALTER TABLE dbo.FW_HD_Issues ALTER COLUMN IssueNumber VARCHAR(30) NOT NULL;';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_FW_HD_Issues_IssueNumber' AND object_id = OBJECT_ID(N'dbo.FW_HD_Issues'))
BEGIN
    CREATE UNIQUE INDEX UX_FW_HD_Issues_IssueNumber
        ON dbo.FW_HD_Issues(RegistrationID, IssueNumber)
        WHERE DeletedFlag = 0;
END;

EXEC sys.sp_executesql N'
SELECT IssueID, RegistrationID, IssueNumber, Subject, CreatedOn, UpdatedOn
FROM dbo.FW_HD_Issues
ORDER BY IssueID;';
