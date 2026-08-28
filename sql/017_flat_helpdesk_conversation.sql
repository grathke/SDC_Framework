USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.FW_HD_Issues', N'ConversationText') IS NULL
    ALTER TABLE dbo.FW_HD_Issues ADD ConversationText VARCHAR(MAX) NULL;

EXEC sys.sp_executesql N'
UPDATE i
SET ConversationText = COALESCE(NULLIF(i.ConversationText, ''''), i.Description) +
    COALESCE(CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) +
        (SELECT STRING_AGG(CONVERT(VARCHAR(MAX), ''['' + CONVERT(VARCHAR(19), r.CreatedOn, 120) + ''] '' + COALESCE(u.FirstLast, ''User'') + '': '' + r.ResponseText), CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10))
         FROM dbo.FW_HD_IssueResponses r
         LEFT JOIN dbo.FW_Users u ON u.UserID = r.UserID
         WHERE r.IssueID = i.IssueID AND r.DeletedFlag = 0), '''')
FROM dbo.FW_HD_Issues i;';

IF OBJECT_ID(N'dbo.FW_HD_IssueAttachments', N'U') IS NOT NULL
BEGIN
    DECLARE @dropSql VARCHAR(MAX) = '';
    SELECT @dropSql = @dropSql + 'ALTER TABLE dbo.FW_HD_IssueAttachments DROP CONSTRAINT ' + QUOTENAME(fk.name) + ';'
    FROM sys.foreign_keys fk
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.FW_HD_IssueAttachments')
      AND fk.referenced_object_id = OBJECT_ID(N'dbo.FW_HD_IssueResponses');
    IF @dropSql <> '' EXEC(@dropSql);

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FW_HD_IssueAttachments_Response' AND object_id = OBJECT_ID(N'dbo.FW_HD_IssueAttachments'))
        DROP INDEX IX_FW_HD_IssueAttachments_Response ON dbo.FW_HD_IssueAttachments;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_FW_HD_IssueAttachments_Parent' AND parent_object_id = OBJECT_ID(N'dbo.FW_HD_IssueAttachments'))
        ALTER TABLE dbo.FW_HD_IssueAttachments DROP CONSTRAINT CK_FW_HD_IssueAttachments_Parent;

    IF COL_LENGTH(N'dbo.FW_HD_IssueAttachments', N'IssueResponseID') IS NOT NULL
        ALTER TABLE dbo.FW_HD_IssueAttachments DROP COLUMN IssueResponseID;
END;

IF OBJECT_ID(N'dbo.FW_HD_IssueResponses', N'U') IS NOT NULL
    DROP TABLE dbo.FW_HD_IssueResponses;

COMMIT TRANSACTION;

EXEC sys.sp_executesql N'
SELECT IssueID, IssueNumber, ConversationText
FROM dbo.FW_HD_Issues
ORDER BY IssueID;';