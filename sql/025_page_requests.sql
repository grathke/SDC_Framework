/*
    Draft schema for database-backed New Browse and Maintenance Page requests.
    This script is intentionally not executed by the application startup script.
    Adjust the columns before generating the replacement form from this table.
*/

IF OBJECT_ID(N'dbo.FW_PageGeneration_B_U', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_PageGeneration_B_U
    (
        PageRequestID INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_PageGeneration_B_U PRIMARY KEY,
        RequestName VARCHAR(50) NOT NULL,
        PageBaseName VARCHAR(50) NULL,
        BrowsePageName VARCHAR(50) NULL,
        MaintenancePageName VARCHAR(50) NULL,
        UnderlyingTableName VARCHAR(50) NULL,
        UseRegistrationID BIT NOT NULL CONSTRAINT DF_FW_PageGeneration_B_U_UseRegistrationID DEFAULT (0),
        BrowseSql VARCHAR(MAX) NULL,
        EditableFields VARCHAR(50) NULL,
        ReadOnlyFields VARCHAR(50) NULL,
        LookupFields VARCHAR(50) NULL,
        AdminRequiredFields VARCHAR(50) NULL,
        CreateBehavior VARCHAR(50) NULL,
        UpdateBehavior VARCHAR(50) NULL,
        DeleteBehavior VARCHAR(50) NULL,
        MenuCaller VARCHAR(50) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2(0) NOT NULL
            CONSTRAINT DF_FW_PageGeneration_B_U_CreatedOn DEFAULT (GETDATE()),
        UpdatedBy INT NULL,
        UpdatedOn DATETIME2(0) NULL,
        DeletedFlag BIT NOT NULL
            CONSTRAINT DF_FW_PageGeneration_B_U_DeletedFlag DEFAULT (0),
        RowVersion TIMESTAMP NOT NULL
    );

    CREATE INDEX IX_FW_PageGeneration_B_U_RequestName
        ON dbo.FW_PageGeneration_B_U (RequestName, DeletedFlag);
END;
GO
