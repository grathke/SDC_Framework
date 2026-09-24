-- 159_import_batches.sql
--
-- Import batches: one row per completed employee import, and one per person it brought in, so a
-- whole import can be found, read and undone together (Glenn, 2026-09-24).
--
-- FW_ImportBatches        one row per import that COMMITTED. It is the first write inside the
--                         import's own transaction, so an import that fails leaves no batch -
--                         there are no partial batches. BatchName and Note are required: the
--                         import's gateway will not start without them.
-- FW_ImportBatchPeople    one row per person, holding both halves - the employee and the login -
--                         and a copy of their name and user name taken at import time, so a batch
--                         that has been undone still says exactly who was in it.
--
-- NO COLUMN ON FW_Employees OR FW_Users. A column there would reach the generated page, the browse
-- SQL and the field permissions of both. The link table keeps the import's bookkeeping out of the
-- core tables and costs one join, only when a batch is looked at.
--
-- UNDO IS A HARD DELETE (Glenn's decision, recorded in PARKED_DECISIONS and the page's code): the
-- batch's employees, their logins, their own settings and the Help Desk issues they reported are
-- physically deleted; issues merely assigned to them are unassigned; messages and the audit trail
-- are kept. The batch row itself stays, marked with who undid it and when - that and the people
-- rows are the record that it happened.
--
-- NO DeletedFlag. A batch is never soft-deleted: it is undone, which UndoneOn records, and the
-- row stays as the record that the import happened. The columns would also have given the browse
-- page a Show Deleted button with nothing behind it.
--
-- FileHash is SHA-256 of the file's bytes, so the same file imported twice can be warned about.
--
-- The batch before batches: today's first import (EmployeeIDs 10024-10037) predates this table.
-- It is gathered below from the audit trail rows that import wrote, so it can be undone like any
-- other. Guarded, so running this twice does not make it twice.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.FW_ImportBatches', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_ImportBatches
    (
        ImportBatchID       int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_ImportBatches PRIMARY KEY,

        RegistrationID      int NOT NULL,
        SavedImportID       int NULL,
        BatchName           nvarchar(100) NOT NULL,
        Note                nvarchar(1000) NOT NULL,
        FileName            nvarchar(260) NULL,
        FileHash            varbinary(32) NULL,
        PeopleCount         int NOT NULL,

        ImportedBy          int NULL,
        ImportedOn          datetime2(0) NOT NULL
            CONSTRAINT DF_FW_ImportBatches_ImportedOn DEFAULT (sysutcdatetime()),
        UndoneBy            int NULL,
        UndoneOn            datetime2(0) NULL,
        UndoneCount         int NULL,

        CreatedBy           int NULL,
        CreatedOn           datetime NULL
            CONSTRAINT DF_FW_ImportBatches_CreatedOn DEFAULT (getdate()),
        UpdatedBy           int NULL,
        UpdatedOn           datetime NULL,
        RowVersion          rowversion
    );

    CREATE INDEX IX_FW_ImportBatches_Registration ON dbo.FW_ImportBatches (RegistrationID, ImportedOn);
    CREATE INDEX IX_FW_ImportBatches_FileHash ON dbo.FW_ImportBatches (RegistrationID, FileHash) WHERE UndoneOn IS NULL;
END
GO

IF OBJECT_ID('dbo.FW_ImportBatchPeople', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_ImportBatchPeople
    (
        ImportBatchPersonID int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_ImportBatchPeople PRIMARY KEY,

        ImportBatchID       int NOT NULL
            CONSTRAINT FK_FW_ImportBatchPeople_Batch REFERENCES dbo.FW_ImportBatches (ImportBatchID),
        EmployeeID          int NOT NULL,
        UserId              int NOT NULL,
        FirstName           nvarchar(45) NULL,
        LastName            nvarchar(45) NULL,
        UserName            nvarchar(50) NULL,
        SourceRow           int NULL,
        RemovedOn           datetime2(0) NULL
    );

    CREATE INDEX IX_FW_ImportBatchPeople_Batch ON dbo.FW_ImportBatchPeople (ImportBatchID);
    CREATE INDEX IX_FW_ImportBatchPeople_Employee ON dbo.FW_ImportBatchPeople (EmployeeID);
END
GO

-- The import made before batches existed, gathered from its audit rows.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_ImportBatches WHERE BatchName = N'Imported before batches existed')
BEGIN
    DECLARE @People TABLE (EmployeeID int, UserId int, RegistrationID int, FirstName nvarchar(45), LastName nvarchar(45),
                           UserName nvarchar(50), ImportedBy int, LoggedOn datetime2(0));

    INSERT INTO @People
    SELECT e.EmployeeID, e.UserId, ISNULL(e.RegistrationId, a.RegistrationID), e.FirstName, e.LastName, e.UserName, a.UserID, a.LoggedOn
    FROM dbo.FW_AuditTrail a
    JOIN dbo.FW_Employees e ON e.EmployeeID = TRY_CAST(a.RecordKey AS int)
    WHERE a.PageName = 'FW_EmployeeImport' AND a.TableName = 'FW_Employees'
      AND a.OperationType = 'Import' AND a.Phase = 'AfterSave' AND ISNULL(a.SaveSucceeded, 0) = 1
      AND NOT EXISTS (SELECT 1 FROM dbo.FW_ImportBatchPeople p WHERE p.EmployeeID = e.EmployeeID);

    IF EXISTS (SELECT 1 FROM @People)
    BEGIN
        DECLARE @Registration int = (SELECT TOP 1 RegistrationID FROM @People);
        DECLARE @ImportedBy int = (SELECT TOP 1 ImportedBy FROM @People);
        DECLARE @ImportedOn datetime2(0) = (SELECT MIN(LoggedOn) FROM @People);

        INSERT INTO dbo.FW_ImportBatches (RegistrationID, BatchName, Note, FileName, PeopleCount, ImportedBy, ImportedOn, CreatedBy)
        VALUES (@Registration, N'Imported before batches existed',
                N'Gathered from the audit trail when import batches were added (sql 159). The file was employees-clean.csv.',
                N'employees-clean.csv', (SELECT COUNT(*) FROM @People), @ImportedBy, @ImportedOn, @ImportedBy);

        DECLARE @Batch int = CAST(SCOPE_IDENTITY() AS int);

        INSERT INTO dbo.FW_ImportBatchPeople (ImportBatchID, EmployeeID, UserId, FirstName, LastName, UserName)
        SELECT @Batch, EmployeeID, UserId, FirstName, LastName, UserName FROM @People;
    END
END
GO

-- The FW_Pages row for FW_ImportBatches_B, the Past Imports page. Stated rather than left to the
-- page's first open, for the reason 132 gives: a fallback that guesses wrong locks every button.
--
--   ImportBatchID AS PK      the batch, the row's identity to the browse framework.
--   b.RegistrationID = @RegistrationID
--                            the scope, answered by the framework like any browse page: the
--                            registration combo's choice for an App Admin, the session's for a
--                            Company Admin, whose combo is hidden. One registration at a time.
--                            No registration column: the combo already says which, and a column
--                            holding the same value on every row is only a QBE field to skip.
--   Status                   Imported, or Undone with the date, so an undone batch reads as one.
--
-- Hot Fields off: the grid already shows the whole row.
-- FW_RoleSchema, FW_RoleFields: the schema sync adds them. No FW_RoleDetails grant is needed or
-- wanted - the page is gated by role (App Admin, Company Admin), as the import is.
DECLARE @PageSql varchar(max) =
'SELECT
    b.[ImportBatchID] AS PK,
    b.[ImportBatchID],
    b.[BatchName],
    b.[Note],
    b.[PeopleCount],
    b.[ImportedOn],
    u.[FirstLast] AS ImportedByName,
    CASE WHEN b.[UndoneOn] IS NULL THEN ''Imported''
         ELSE ''Undone '' + CONVERT(varchar(16), b.[UndoneOn], 120) END AS Status,
    b.[FileName]
FROM dbo.[FW_ImportBatches] b
LEFT JOIN dbo.[FW_Users] u ON u.[UserId] = b.[ImportedBy]
WHERE b.[RegistrationID] = @RegistrationID
ORDER BY b.[ImportedOn] DESC';

DECLARE @PageCreatedBy int = ISNULL((SELECT TOP 1 UserId FROM dbo.FW_Users WHERE UserName = 'grathke@sdcdev.net'), 2);

IF EXISTS (SELECT 1 FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_ImportBatches_B')
    UPDATE dbo.FW_Pages
       SET DB_Table = 'FW_ImportBatches', Table_Alias = 'Past Imports', Table_SQL = @PageSql, UseHotFields = 0
     WHERE WindowOrPage = 'FW_ImportBatches_B';
ELSE
    INSERT INTO dbo.FW_Pages (WindowOrPage, DB_Table, Table_Alias, Table_SQL, UseHotFields, CreatedBy, CreatedOn)
    VALUES ('FW_ImportBatches_B', 'FW_ImportBatches', 'Past Imports', @PageSql, 0, @PageCreatedBy, GETDATE());
GO
