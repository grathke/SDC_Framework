IF OBJECT_ID('dbo.FW_UserUiHints', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_UserUiHints
    (
        ID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_UserUiHints PRIMARY KEY,
        RegistrationID INT NOT NULL,
        UserID INT NOT NULL,
        HintKey NVARCHAR(100) NOT NULL,
        Seen BIT NOT NULL CONSTRAINT DF_FW_UserUiHints_Seen DEFAULT (1),
        SeenOn DATETIME NOT NULL CONSTRAINT DF_FW_UserUiHints_SeenOn DEFAULT (GETDATE()),
        CreatedBy INT NULL,
        CreatedOn DATETIME NOT NULL CONSTRAINT DF_FW_UserUiHints_CreatedOn DEFAULT (GETDATE()),
        UpdatedBy INT NULL,
        UpdatedOn DATETIME NOT NULL CONSTRAINT DF_FW_UserUiHints_UpdatedOn DEFAULT (GETDATE()),
        DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_UserUiHints_DeletedFlag DEFAULT (0),
        DeletedBy INT NULL,
        DeletedOn DATETIME NULL
    );

    CREATE UNIQUE INDEX UX_FW_UserUiHints_UserHint
        ON dbo.FW_UserUiHints (RegistrationID, UserID, HintKey);
END;
