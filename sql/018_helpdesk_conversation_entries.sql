USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.FW_HD_Issues', N'ConversationEntryCount') IS NULL
    ALTER TABLE dbo.FW_HD_Issues ADD ConversationEntryCount INT NOT NULL CONSTRAINT DF_FW_HD_Issues_ConversationEntryCount DEFAULT (1);

IF COL_LENGTH(N'dbo.FW_HD_IssueAttachments', N'ConversationEntryID') IS NULL
    ALTER TABLE dbo.FW_HD_IssueAttachments ADD ConversationEntryID INT NULL;

EXEC sys.sp_executesql N'
UPDATE dbo.FW_HD_Issues
SET ConversationEntryCount = CASE WHEN ConversationEntryCount < 1 THEN 1 ELSE ConversationEntryCount END;

UPDATE dbo.FW_HD_IssueAttachments
SET ConversationEntryID = 1
WHERE ConversationEntryID IS NULL;';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FW_HD_IssueAttachments_ConversationEntry' AND object_id = OBJECT_ID(N'dbo.FW_HD_IssueAttachments'))
    CREATE INDEX IX_FW_HD_IssueAttachments_ConversationEntry
        ON dbo.FW_HD_IssueAttachments(RegistrationID, IssueID, ConversationEntryID, DeletedFlag);

EXEC sys.sp_executesql N'
SELECT IssueID, IssueNumber, ConversationEntryCount
FROM dbo.FW_HD_Issues
ORDER BY IssueID;

SELECT AttachmentID, IssueID, ConversationEntryID, FileName
FROM dbo.FW_HD_IssueAttachments
WHERE DeletedFlag = 0
ORDER BY IssueID, ConversationEntryID, AttachmentID;';
