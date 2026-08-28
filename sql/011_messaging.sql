USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FW_MessageThreads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_MessageThreads
    (
        ThreadID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_MessageThreads PRIMARY KEY,
        RegistrationID INT NOT NULL,
        Subject VARCHAR(250) NOT NULL,
        CreatedBy INT NOT NULL,
        CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_MessageThreads_CreatedOn DEFAULT SYSUTCDATETIME(),
        UpdatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_MessageThreads_UpdatedOn DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_MessageThreads_DeletedFlag DEFAULT (0),
        DeletedBy INT NULL,
        DeletedOn DATETIME2(0) NULL
    );
END;

IF OBJECT_ID(N'dbo.FW_Messages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_Messages
    (
        MessageID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_Messages PRIMARY KEY,
        ThreadID INT NOT NULL,
        RegistrationID INT NOT NULL,
        FromUserID INT NOT NULL,
        ToUserID INT NOT NULL,
        FromUserName VARCHAR(250) NULL,
        ToUserName VARCHAR(250) NULL,
        Body VARCHAR(MAX) NOT NULL,
        SentOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_Messages_SentOn DEFAULT SYSUTCDATETIME(),
        CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_Messages_CreatedOn DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_FW_Messages_Thread FOREIGN KEY (ThreadID) REFERENCES dbo.FW_MessageThreads(ThreadID)
    );
END;

IF OBJECT_ID(N'dbo.FW_Messages', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.FW_Messages', N'FromUserID') IS NULL
        ALTER TABLE dbo.FW_Messages ADD FromUserID INT NULL;

    IF COL_LENGTH(N'dbo.FW_Messages', N'ToUserID') IS NULL
        ALTER TABLE dbo.FW_Messages ADD ToUserID INT NULL;

    IF COL_LENGTH(N'dbo.FW_Messages', N'FromUserName') IS NULL
        ALTER TABLE dbo.FW_Messages ADD FromUserName VARCHAR(250) NULL;

    IF COL_LENGTH(N'dbo.FW_Messages', N'ToUserName') IS NULL
        ALTER TABLE dbo.FW_Messages ADD ToUserName VARCHAR(250) NULL;

    IF COL_LENGTH(N'dbo.FW_Messages', N'SenderUserID') IS NOT NULL
        EXEC(N'UPDATE dbo.FW_Messages SET FromUserID = SenderUserID WHERE FromUserID IS NULL');

END;

IF OBJECT_ID(N'dbo.FW_MessageRecipients', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_MessageRecipients
    (
        MessageRecipientID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_MessageRecipients PRIMARY KEY,
        MessageID INT NOT NULL,
        ThreadID INT NOT NULL,
        RegistrationID INT NOT NULL,
        UserID INT NOT NULL,
        RecipientType CHAR(2) NOT NULL CONSTRAINT CK_FW_MessageRecipients_Type CHECK (RecipientType IN ('To', 'Cc', 'Se')),
        FolderName VARCHAR(12) NOT NULL CONSTRAINT CK_FW_MessageRecipients_Folder CHECK (FolderName IN ('Inbox', 'Sent', 'Archive', 'Trash')),
        PreviousFolderName VARCHAR(12) NULL,
        IsRead BIT NOT NULL CONSTRAINT DF_FW_MessageRecipients_IsRead DEFAULT (0),
        ReadOn DATETIME2(0) NULL,
        CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_MessageRecipients_CreatedOn DEFAULT SYSUTCDATETIME(),
        UpdatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_MessageRecipients_UpdatedOn DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_FW_MessageRecipients_Message FOREIGN KEY (MessageID) REFERENCES dbo.FW_Messages(MessageID),
        CONSTRAINT FK_FW_MessageRecipients_Thread FOREIGN KEY (ThreadID) REFERENCES dbo.FW_MessageThreads(ThreadID)
    );
END;

IF OBJECT_ID(N'dbo.FW_MessageRecipients', N'U') IS NOT NULL
BEGIN
    EXEC(N'UPDATE m SET m.ToUserID = r.UserID FROM dbo.FW_Messages m INNER JOIN dbo.FW_MessageRecipients r ON r.MessageID = m.MessageID AND r.RecipientType = ''To'' WHERE m.ToUserID IS NULL');
END;

EXEC sys.sp_executesql N'
UPDATE m
SET FromUserName = COALESCE(NULLIF(LTRIM(RTRIM(fromUser.FirstLast)), ''''), NULLIF(LTRIM(RTRIM(ISNULL(fromUser.FirstName, '''') + '' '' + ISNULL(fromUser.LastName, ''''))), ''''), fromUser.Email),
    ToUserName = COALESCE(NULLIF(LTRIM(RTRIM(toUser.FirstLast)), ''''), NULLIF(LTRIM(RTRIM(ISNULL(toUser.FirstName, '''') + '' '' + ISNULL(toUser.LastName, ''''))), ''''), toUser.Email)
FROM dbo.FW_Messages AS m
LEFT JOIN dbo.FW_Users AS fromUser ON fromUser.UserID = m.FromUserID
LEFT JOIN dbo.FW_Users AS toUser ON toUser.UserID = m.ToUserID
WHERE m.FromUserName IS NULL OR m.ToUserName IS NULL;';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FW_MessageRecipients_UserFolder' AND object_id = OBJECT_ID(N'dbo.FW_MessageRecipients'))
    CREATE INDEX IX_FW_MessageRecipients_UserFolder ON dbo.FW_MessageRecipients(RegistrationID, UserID, FolderName, IsRead);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_FW_Messages_Thread' AND object_id = OBJECT_ID(N'dbo.FW_Messages'))
    CREATE INDEX IX_FW_Messages_Thread ON dbo.FW_Messages(RegistrationID, ThreadID, SentOn);
