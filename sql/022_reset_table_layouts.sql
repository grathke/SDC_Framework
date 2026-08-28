USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DROP TABLE dbo.FW_TableLayouts;

CREATE TABLE dbo.FW_TableLayouts
(
    ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_TableLayouts PRIMARY KEY,
    RegistrationID INT NOT NULL,
    UserID INT NULL,
    PageName VARCHAR(100) NOT NULL,
    TableName VARCHAR(100) NULL,
    LayoutType VARCHAR(20) NOT NULL,
    LayoutName VARCHAR(100) NULL,
    JsonState NVARCHAR(MAX) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_FW_TableLayouts_IsActive DEFAULT (1),
    CreatedBy INT NULL,
    CreatedOn DATETIME NOT NULL CONSTRAINT DF_FW_TableLayouts_CreatedOn DEFAULT (GETDATE()),
    UpdatedBy INT NULL,
    UpdatedOn DATETIME NULL,
    DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_TableLayouts_DeletedFlag DEFAULT (0),
    DeletedBy INT NULL,
    DeletedOn DATETIME2 NULL,
    RowVersion ROWVERSION NOT NULL
);

CREATE INDEX IX_FW_TableLayouts_Lookup
    ON dbo.FW_TableLayouts (RegistrationID, UserID, PageName, TableName, LayoutType, IsActive);

CREATE UNIQUE INDEX UX_FW_TableLayouts_Default
    ON dbo.FW_TableLayouts (RegistrationID, PageName, TableName, LayoutType)
    WHERE LayoutType = 'Default' AND IsActive = 1;

CREATE UNIQUE INDEX UX_FW_TableLayouts_LastUsed
    ON dbo.FW_TableLayouts (RegistrationID, UserID, PageName, TableName, LayoutType)
    WHERE LayoutType = 'LastUsed' AND IsActive = 1;

CREATE UNIQUE INDEX UX_FW_TableLayouts_UserNamed
    ON dbo.FW_TableLayouts (RegistrationID, UserID, PageName, TableName, LayoutType, LayoutName)
    WHERE LayoutType = 'UserNamed' AND IsActive = 1;

COMMIT TRANSACTION;
GO