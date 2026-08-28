SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF OBJECT_ID('dbo.FW_TableLayouts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_TableLayouts
    (
        ID BIGINT IDENTITY(1,1) NOT NULL,
        RegistrationID INT NOT NULL,
        UserID INT NULL,
        PageName VARCHAR(100) NOT NULL,
        TableName VARCHAR(100) NULL,
        LayoutType VARCHAR(20) NOT NULL,
        LayoutName VARCHAR(100) NULL,
        JsonState NVARCHAR(MAX) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_FW_TableLayouts_IsActive DEFAULT (1),
        DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_TableLayouts_DeletedFlag DEFAULT (0),
        DeletedBy INT NULL,
        DeletedOn DATETIME2 NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME NOT NULL CONSTRAINT DF_FW_TableLayouts_CreatedOn DEFAULT (GETDATE()),
        UpdatedBy INT NULL,
        UpdatedOn DATETIME NULL,
        CONSTRAINT PK_FW_TableLayouts PRIMARY KEY CLUSTERED (ID),
        CONSTRAINT CK_FW_TableLayouts_LayoutType CHECK (LayoutType IN ('Default', 'UserNamed', 'LastUsed'))
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_FW_TableLayouts_Default'
      AND object_id = OBJECT_ID('dbo.FW_TableLayouts')
)
BEGIN
    CREATE UNIQUE INDEX UX_FW_TableLayouts_Default
        ON dbo.FW_TableLayouts(RegistrationID, PageName, TableName, LayoutType)
        WHERE LayoutType = 'Default' AND IsActive = 1;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_FW_TableLayouts_LastUsed'
      AND object_id = OBJECT_ID('dbo.FW_TableLayouts')
)
BEGIN
    CREATE UNIQUE INDEX UX_FW_TableLayouts_LastUsed
        ON dbo.FW_TableLayouts(RegistrationID, UserID, PageName, TableName, LayoutType)
        WHERE LayoutType = 'LastUsed' AND IsActive = 1;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_FW_TableLayouts_UserNamed'
      AND object_id = OBJECT_ID('dbo.FW_TableLayouts')
)
BEGIN
    CREATE UNIQUE INDEX UX_FW_TableLayouts_UserNamed
        ON dbo.FW_TableLayouts(RegistrationID, UserID, PageName, TableName, LayoutType, LayoutName)
        WHERE LayoutType = 'UserNamed' AND IsActive = 1;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_FW_TableLayouts_Lookup'
      AND object_id = OBJECT_ID('dbo.FW_TableLayouts')
)
BEGIN
    CREATE INDEX IX_FW_TableLayouts_Lookup
        ON dbo.FW_TableLayouts(RegistrationID, UserID, PageName, TableName, LayoutType, IsActive)
        INCLUDE (LayoutName, UpdatedOn, CreatedOn);
END;
GO
