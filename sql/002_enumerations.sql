SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Enumerations table for UI control metadata extraction
    Stores the enumeration of controls, columns, and their database bindings per page
*/

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID('dbo.FW_Enumerations', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FW_Enumerations
        (
            ID INT IDENTITY(1,1) NOT NULL,
            PageName NVARCHAR(160) NOT NULL,
            ControlName NVARCHAR(256) NOT NULL,
            ControlCaption NVARCHAR(256) NULL,
            FileLink NVARCHAR(256) NULL,
            JsAlias NVARCHAR(256) NULL,
            CreatedBy INT NOT NULL,
            CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_Enumerations_CreatedOn DEFAULT (GETDATE()),
            DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_Enumerations_DeletedFlag DEFAULT (0),
            DeletedBy INT NULL,
            DeletedOn DATETIME2(0) NULL,
            CONSTRAINT PK_FW_Enumerations PRIMARY KEY CLUSTERED (ID),
            CONSTRAINT UQ_FW_Enumerations_PageControl UNIQUE (PageName, ControlName)
        );

        CREATE INDEX IX_FW_Enumerations_PageName ON dbo.FW_Enumerations(PageName);
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRAN;
    THROW;
END CATCH;
