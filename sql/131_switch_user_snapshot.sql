/*
    131_switch_user_snapshot.sql

    dbo.FW_SwitchUser - who an administrator can switch to, as a real table.

    The Switch User search page browses this. It could have browsed dbo.FW_UserPeople directly,
    and a view is cheaper to maintain, but a view has no primary key and no RowVersion, and the
    browse framework expects both: a page on a view needs the RowVersion warning suppressed and a
    documented exception to the shared contract. A table needs neither, and behaves like every
    other page in the application.

    The view is still the single place that says who a login belongs to. This table is a copy of
    it, kept fresh by usp_FW_RefreshSwitchUser.

    **The refresh writes only what changed.** A delete-and-reload of several thousand rows on
    every page open is work nobody is waiting for, and with hundreds or thousands of people it
    would be felt. The MERGE compares the view against the table and touches a row only when a
    value actually differs, so a refresh that finds nothing new writes nothing at all. Rows for
    logins that have gone are removed, or they would stay switchable forever.

    The page reads the table either way. If a refresh cannot run - the database busy, the view
    changed, anything - the page still lists the last snapshot, and RefreshedOn says when that
    was.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.FW_SwitchUser', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_SwitchUser
    (
        SwitchUserID   int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_SwitchUser PRIMARY KEY CLUSTERED,
        UserId         int          NOT NULL,
        RegistrationID int          NULL,
        PersonType     varchar(20)  NOT NULL,
        PersonID       int          NULL,
        FirstName      varchar(50)  NULL,
        LastName        varchar(50) NULL,
        FirstLast      varchar(101) NULL,
        UserName       varchar(50)  NULL,
        Email          varchar(128) NULL,
        CompanyName    varchar(75)  NULL,
        IsActive       bit          NOT NULL CONSTRAINT DF_FW_SwitchUser_IsActive DEFAULT (0),
        LoginActive    bit          NOT NULL CONSTRAINT DF_FW_SwitchUser_LoginActive DEFAULT (0),
        PersonActive   bit          NOT NULL CONSTRAINT DF_FW_SwitchUser_PersonActive DEFAULT (0),
        RefreshedOn    datetime2(3) NOT NULL CONSTRAINT DF_FW_SwitchUser_RefreshedOn DEFAULT (SYSUTCDATETIME()),
        RowVersion     rowversion   NOT NULL
    );

    PRINT 'dbo.FW_SwitchUser created';
END
ELSE
BEGIN
    PRINT 'dbo.FW_SwitchUser already exists';
END
GO

/*  One row per login. The MERGE matches on it, and the browse page's PK alias identifies a
    selected row by it, so two rows for one login would be two people to switch to. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FW_SwitchUser_UserId' AND object_id = OBJECT_ID('dbo.FW_SwitchUser'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_FW_SwitchUser_UserId ON dbo.FW_SwitchUser (UserId);
    PRINT 'UX_FW_SwitchUser_UserId created';
END
GO

/*  The scope every browse page applies, and the column the search sorts on. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FW_SwitchUser_Registration' AND object_id = OBJECT_ID('dbo.FW_SwitchUser'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_FW_SwitchUser_Registration ON dbo.FW_SwitchUser (RegistrationID, LastName, FirstName);
    PRINT 'IX_FW_SwitchUser_Registration created';
END
GO

/*  The two indexes the view itself needs, and neither existed: FW_Employees had only its primary
    key, so every employee row was found by scanning the whole table, and FW_Users had only its
    key and a unique index on UserName. At eleven rows nothing noticed. At thousands it would. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FW_Employees_UserId' AND object_id = OBJECT_ID('dbo.FW_Employees'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_FW_Employees_UserId ON dbo.FW_Employees (UserId) WHERE DeletedFlag = 0;
    PRINT 'IX_FW_Employees_UserId created';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FW_Users_Registration' AND object_id = OBJECT_ID('dbo.FW_Users'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_FW_Users_Registration ON dbo.FW_Users (RegistrationID) INCLUDE (UserName, Email, FirstName, LastName);
    PRINT 'IX_FW_Users_Registration created';
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_FW_RefreshSwitchUser
    @RowsInserted int = NULL OUTPUT,
    @RowsUpdated  int = NULL OUTPUT,
    @RowsDeleted  int = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @changes TABLE (Action nvarchar(10));

    BEGIN TRANSACTION;

    MERGE dbo.FW_SwitchUser WITH (HOLDLOCK) AS target
    USING dbo.FW_UserPeople AS source
       ON target.UserId = source.UserId

    WHEN MATCHED AND (
            ISNULL(target.RegistrationID, -1) <> ISNULL(source.RegistrationID, -1)
         OR target.PersonType                 <> source.PersonType
         OR ISNULL(target.PersonID, -1)       <> ISNULL(source.PersonID, -1)
         OR ISNULL(target.FirstName, '')      <> ISNULL(source.FirstName, '')
         OR ISNULL(target.LastName, '')       <> ISNULL(source.LastName, '')
         OR ISNULL(target.FirstLast, '')      <> ISNULL(source.FirstLast, '')
         OR ISNULL(target.UserName, '')       <> ISNULL(source.UserName, '')
         OR ISNULL(target.Email, '')          <> ISNULL(source.Email, '')
         OR ISNULL(target.CompanyName, '')    <> ISNULL(source.CompanyName, '')
         OR target.IsActive                   <> source.IsActive
         OR target.LoginActive                <> source.LoginActive
         OR target.PersonActive               <> source.PersonActive)
    THEN UPDATE SET
            RegistrationID = source.RegistrationID,
            PersonType     = source.PersonType,
            PersonID       = source.PersonID,
            FirstName      = source.FirstName,
            LastName       = source.LastName,
            FirstLast      = source.FirstLast,
            UserName       = source.UserName,
            Email          = source.Email,
            CompanyName    = source.CompanyName,
            IsActive       = source.IsActive,
            LoginActive    = source.LoginActive,
            PersonActive   = source.PersonActive,
            RefreshedOn    = SYSUTCDATETIME()

    WHEN NOT MATCHED BY TARGET
    THEN INSERT (UserId, RegistrationID, PersonType, PersonID, FirstName, LastName, FirstLast,
                 UserName, Email, CompanyName, IsActive, LoginActive, PersonActive, RefreshedOn)
         VALUES (source.UserId, source.RegistrationID, source.PersonType, source.PersonID,
                 source.FirstName, source.LastName, source.FirstLast, source.UserName,
                 source.Email, source.CompanyName, source.IsActive, source.LoginActive,
                 source.PersonActive, SYSUTCDATETIME())

    /*  A login the view no longer returns has been deleted, or has lost the person row that made
        it findable. Leaving it here would leave somebody switchable who no longer exists. Only
        those rows are touched, not the whole table. */
    WHEN NOT MATCHED BY SOURCE
    THEN DELETE

    OUTPUT $action INTO @changes;

    COMMIT TRANSACTION;

    SELECT @RowsInserted = SUM(CASE WHEN Action = 'INSERT' THEN 1 ELSE 0 END),
           @RowsUpdated  = SUM(CASE WHEN Action = 'UPDATE' THEN 1 ELSE 0 END),
           @RowsDeleted  = SUM(CASE WHEN Action = 'DELETE' THEN 1 ELSE 0 END)
      FROM @changes;

    SELECT @RowsInserted = ISNULL(@RowsInserted, 0),
           @RowsUpdated  = ISNULL(@RowsUpdated, 0),
           @RowsDeleted  = ISNULL(@RowsDeleted, 0);
END
GO

SET NOCOUNT ON;

DECLARE @ins int, @upd int, @del int;
EXEC dbo.usp_FW_RefreshSwitchUser @RowsInserted = @ins OUTPUT, @RowsUpdated = @upd OUTPUT, @RowsDeleted = @del OUTPUT;
PRINT 'First refresh - inserted: ' + CAST(@ins AS varchar(10)) + ', updated: ' + CAST(@upd AS varchar(10)) + ', deleted: ' + CAST(@del AS varchar(10));

EXEC dbo.usp_FW_RefreshSwitchUser @RowsInserted = @ins OUTPUT, @RowsUpdated = @upd OUTPUT, @RowsDeleted = @del OUTPUT;
PRINT 'Second refresh, nothing should have changed - inserted: ' + CAST(@ins AS varchar(10)) + ', updated: ' + CAST(@upd AS varchar(10)) + ', deleted: ' + CAST(@del AS varchar(10));

SELECT SwitchUserID, UserId, RegistrationID, PersonType, PersonID, FirstLast, UserName, Email, IsActive, LoginActive, PersonActive,
       CONVERT(varchar(19), RefreshedOn, 120) AS RefreshedOn
  FROM dbo.FW_SwitchUser
 ORDER BY RegistrationID, LastName, FirstName;
