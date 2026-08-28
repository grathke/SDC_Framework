-- Saved QBE filters per user per table context
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'FW_SavedQbe')
BEGIN
    CREATE TABLE dbo.FW_SavedQbe (
        SavedQbeID      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_SavedQbe PRIMARY KEY,
        RegistrationID  INT NOT NULL,
        UserID          INT NOT NULL,
        QbeName         NVARCHAR(100) NOT NULL,
        IsCompanyWide   BIT NOT NULL CONSTRAINT DF_FW_SavedQbe_IsCompanyWide DEFAULT (0),
        TableContext    NVARCHAR(200) NOT NULL,
        QbeData         NVARCHAR(MAX) NOT NULL,
        CreatedOn       DATETIME2 NOT NULL CONSTRAINT DF_FW_SavedQbe_CreatedOn DEFAULT (GETDATE()),
        UpdatedOn       DATETIME2 NOT NULL CONSTRAINT DF_FW_SavedQbe_UpdatedOn DEFAULT (GETDATE()),
        DeletedFlag     BIT NOT NULL CONSTRAINT DF_FW_SavedQbe_DeletedFlag DEFAULT (0),
        DeletedBy       INT NULL,
        DeletedOn       DATETIME2 NULL
    )

    CREATE INDEX IX_FW_SavedQbe_Lookup
        ON dbo.FW_SavedQbe (RegistrationID, UserID, TableContext)
END
