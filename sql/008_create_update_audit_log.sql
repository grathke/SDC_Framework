IF OBJECT_ID('dbo.FW_AuditTrail', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_AuditTrail
    (
        UpdateAuditLogID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_AuditTrail PRIMARY KEY,
        LoggedOn datetime2(0) NOT NULL CONSTRAINT DF_FW_AuditTrail_LoggedOn DEFAULT (SYSUTCDATETIME()),
        RegistrationID int NULL,
        UserID int NULL,
        PageName varchar(100) NOT NULL,
        TableName varchar(100) NULL,
        OperationType varchar(30) NOT NULL,
        Phase varchar(20) NOT NULL,
        RecordKey varchar(100) NULL,
        SaveSucceeded bit NULL,
        SnapshotJson varchar(max) NULL,
        DeletedFlag bit NOT NULL CONSTRAINT DF_FW_AuditTrail_DeletedFlag DEFAULT (0),
        DeletedBy int NULL,
        DeletedOn datetime2(0) NULL
    );
END
