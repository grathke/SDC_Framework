USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FW_HD_IssueAttachments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_HD_IssueAttachments
    (
        AttachmentID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_HD_IssueAttachments PRIMARY KEY,
        IssueID INT NOT NULL,
        ConversationEntryID INT NULL,
        RegistrationID INT NOT NULL,
        FileName VARCHAR(255) NOT NULL,
        ContentType VARCHAR(150) NULL,
        FileSize BIGINT NOT NULL,
        FileData VARBINARY(MAX) NOT NULL,
        CreatedBy INT NOT NULL,
        CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_HD_IssueAttachments_CreatedOn DEFAULT SYSUTCDATETIME(),
        UpdatedBy INT NULL,
        UpdatedOn DATETIME2(0) NULL,
        RowVersion ROWVERSION NOT NULL,
        DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_HD_IssueAttachments_DeletedFlag DEFAULT (0),
        DeletedBy INT NULL,
        DeletedOn DATETIME2(0) NULL,
        CONSTRAINT FK_FW_HD_IssueAttachments_Issue FOREIGN KEY (IssueID) REFERENCES dbo.FW_HD_Issues(IssueID),
        CONSTRAINT CK_FW_HD_IssueAttachments_Size CHECK (FileSize >= 0)
    );

    CREATE INDEX IX_FW_HD_IssueAttachments_Issue
        ON dbo.FW_HD_IssueAttachments(RegistrationID, IssueID, DeletedFlag, CreatedOn);

END;

SELECT AttachmentID, IssueID, RegistrationID, FileName, ContentType, FileSize, DeletedFlag
FROM dbo.FW_HD_IssueAttachments
WHERE DeletedFlag = 0
ORDER BY AttachmentID;
