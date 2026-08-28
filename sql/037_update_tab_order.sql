IF OBJECT_ID('dbo.FW_UpdateTabOrder', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_UpdateTabOrder
    (
        ID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_UpdateTabOrder PRIMARY KEY,
        PageName varchar(128) NOT NULL,
        ControlName varchar(128) NOT NULL,
        TabOrder int NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_FW_UpdateTabOrder_IsActive DEFAULT (1),
        DeletedFlag bit NOT NULL CONSTRAINT DF_FW_UpdateTabOrder_DeletedFlag DEFAULT (0),
        DeletedBy int NULL,
        DeletedOn datetime NULL,
        CreatedBy int NULL,
        CreatedOn datetime NOT NULL CONSTRAINT DF_FW_UpdateTabOrder_CreatedOn DEFAULT (GETDATE()),
        UpdatedBy int NULL,
        UpdatedOn datetime NULL,
        CONSTRAINT UQ_FW_UpdateTabOrder_Page_Control UNIQUE (PageName, ControlName)
    );
END
GO
