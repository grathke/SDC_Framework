USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FW_HD_Issues', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_HD_Issues
    (
        IssueID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_HD_Issues PRIMARY KEY,
        RegistrationID INT NOT NULL,
        ApplicationID INT NULL,
        IssueNumber VARCHAR(30) NULL,
        CategoryID INT NOT NULL,
        Subject VARCHAR(250) NOT NULL,
        Description VARCHAR(MAX) NOT NULL,
        ConversationEntryCount INT NOT NULL CONSTRAINT DF_FW_HD_Issues_ConversationEntryCount DEFAULT (1),
        Status VARCHAR(30) NOT NULL CONSTRAINT DF_FW_HD_Issues_Status DEFAULT ('New'),
        Priority VARCHAR(20) NOT NULL CONSTRAINT DF_FW_HD_Issues_Priority DEFAULT ('Normal'),
        ReporterUserID INT NOT NULL,
        AssignedSupportUserID INT NULL,
        CreatedBy INT NOT NULL,
        CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_HD_Issues_CreatedOn DEFAULT SYSUTCDATETIME(),
        UpdatedBy INT NULL,
        UpdatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_HD_Issues_UpdatedOn DEFAULT SYSUTCDATETIME(),
        ClosedBy INT NULL,
        ClosedOn DATETIME2(0) NULL,
        RowVersion ROWVERSION NOT NULL,
        DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_HD_Issues_DeletedFlag DEFAULT (0),
        DeletedBy INT NULL,
        DeletedOn DATETIME2(0) NULL,
        CONSTRAINT FK_FW_HD_Issues_Category FOREIGN KEY (CategoryID) REFERENCES dbo.FW_HD_IssueCategories(CategoryID),
        CONSTRAINT CK_FW_HD_Issues_Status CHECK (Status IN ('New', 'Assigned', 'In Progress', 'Waiting for User', 'Resolved', 'Closed', 'Reopened')),
        CONSTRAINT CK_FW_HD_Issues_Priority CHECK (Priority IN ('Low', 'Normal', 'High', 'Critical'))
    );

    CREATE INDEX IX_FW_HD_Issues_RegistrationStatus
        ON dbo.FW_HD_Issues(RegistrationID, Status, UpdatedOn);

    CREATE INDEX IX_FW_HD_Issues_Reporter
        ON dbo.FW_HD_Issues(RegistrationID, ReporterUserID, UpdatedOn);

    CREATE INDEX IX_FW_HD_Issues_Assigned
        ON dbo.FW_HD_Issues(RegistrationID, AssignedSupportUserID, Status, UpdatedOn);
END;

SELECT IssueID, RegistrationID, CategoryID, Subject, Status, Priority, ReporterUserID, AssignedSupportUserID
FROM dbo.FW_HD_Issues
WHERE DeletedFlag = 0
ORDER BY IssueID;

