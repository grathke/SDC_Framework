IF OBJECT_ID('dbo.FW_FallbackUsageLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_FallbackUsageLog
    (
        FallbackUsageLogID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_FallbackUsageLog PRIMARY KEY,
        LoggedOn datetime2(0) NOT NULL CONSTRAINT DF_FW_FallbackUsageLog_LoggedOn DEFAULT (SYSUTCDATETIME()),
        RegistrationID int NULL,
        UserID int NULL,
        PageName varchar(100) NULL,
        FallbackType varchar(100) NOT NULL,
        Details varchar(1000) NULL,
        DeletedFlag bit NOT NULL CONSTRAINT DF_FW_FallbackUsageLog_DeletedFlag DEFAULT (0),
        DeletedBy int NULL,
        DeletedOn datetime2(0) NULL
    );
END
