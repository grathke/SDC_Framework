-- Generated from WX_Framework on 2026-09-23 by scripts\dump-schema.ps1
-- Do not edit by hand. Regenerate instead.

-- SQL_STORED_PROCEDURE: FW_HD_GetAdminDashboard

/* ---------------------------------------------------------------------------------------------
   The stored procedure. Re-scripted from its live definition with the two joins repointed and
   nothing else altered.
   --------------------------------------------------------------------------------------------- */
CREATE PROCEDURE dbo.FW_HD_GetAdminDashboard
    @FilterType VARCHAR(30) = NULL,
    @FilterValue VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        COUNT(*) AS TotalTickets,
        SUM(CASE WHEN Status NOT IN ('Resolved', 'Closed') THEN 1 ELSE 0 END) AS OpenTickets,
        SUM(CASE WHEN Status IN ('New', 'Reopened') THEN 1 ELSE 0 END) AS AwaitingSupport,
        AVG(CASE WHEN FirstResponseOn IS NOT NULL
                 THEN CONVERT(DECIMAL(10, 2), DATEDIFF(MINUTE, CreatedOn, FirstResponseOn)) / 60.0
            END) AS AvgFirstResponseHours,
        AVG(CASE WHEN ClosedOn IS NOT NULL
                 THEN CONVERT(DECIMAL(10, 2), DATEDIFF(MINUTE, CreatedOn, ClosedOn)) / 60.0
            END) AS AvgResolutionHours
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0;

    SELECT Status AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0
    GROUP BY Status
    ORDER BY CASE Status
        WHEN 'New' THEN 1 WHEN 'Assigned' THEN 2 WHEN 'In Progress' THEN 3
        WHEN 'Waiting for User' THEN 4 WHEN 'Reopened' THEN 5
        WHEN 'Resolved' THEN 6 WHEN 'Closed' THEN 7 ELSE 8 END;

    SELECT Priority AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0
    GROUP BY Priority
    ORDER BY CASE Priority
        WHEN 'Critical' THEN 1 WHEN 'High' THEN 2 WHEN 'Normal' THEN 3
        WHEN 'Low' THEN 4 ELSE 5 END;

    SELECT ISNULL(category.CategoryName, 'Uncategorized') AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_HD_IssueCategories AS category ON category.CategoryID = issue.CategoryID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
    GROUP BY ISNULL(category.CategoryName, 'Uncategorized')
    ORDER BY TicketCount DESC, Bucket;

    SELECT issue.RegistrationID,
           ISNULL(registration.RegName, 'Unknown account') AS Bucket,
           COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_Registration AS registration ON registration.RegistrationID = issue.RegistrationID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
    GROUP BY issue.RegistrationID, ISNULL(registration.RegName, 'Unknown account')
    ORDER BY TicketCount DESC, Bucket;

    SELECT TOP 10
           issue.IssueID,
           issue.IssueNumber,
           issue.Subject,
           ISNULL(registration.RegName, 'Unknown account') AS AccountName,
           issue.Status,
           issue.Priority,
           issue.CreatedOn,
           issue.UpdatedOn
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_Registration AS registration ON registration.RegistrationID = issue.RegistrationID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
            AND (
                        @FilterType IS NULL
                        OR (@FilterType = 'All')
                        OR (@FilterType = 'Open' AND issue.Status NOT IN ('Resolved', 'Closed'))
                        OR (@FilterType = 'Awaiting' AND issue.Status IN ('New', 'Reopened'))
                        OR (@FilterType = 'Status' AND issue.Status = @FilterValue)
                        OR (@FilterType = 'Priority' AND issue.Priority = @FilterValue)
                        OR (@FilterType = 'Category' AND EXISTS (
                                SELECT 1 FROM dbo.FW_HD_IssueCategories AS filterCategory
                                WHERE filterCategory.CategoryID = issue.CategoryID
                                    AND filterCategory.CategoryName = @FilterValue))
                        OR (@FilterType = 'Registration' AND CONVERT(VARCHAR(30), issue.RegistrationID) = @FilterValue)
                    )
    ORDER BY issue.CreatedOn;
END;
GO

-- VIEW: UserAccess
CREATE VIEW dbo.UserAccess
AS
SELECT        UserId, FirstName, LastName, FirstLast, Email
FROM            dbo.FW_Users
GO

-- SQL_STORED_PROCEDURE: usp_FW_AccessDiagnostic

CREATE   PROCEDURE dbo.usp_FW_AccessDiagnostic
    @UserID INT,
    @RegistrationID INT,
    @DB_Table NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH AssignedRoles AS
    (
        SELECT DISTINCT
            ur.RoleID,
            r.RoleName
        FROM dbo.FW_EmployeeRoles AS ur
        INNER JOIN dbo.FW_Employees AS emp
            ON emp.EmployeeID = ur.EmployeeID
        INNER JOIN dbo.FW_Roles AS r
            ON r.ID = ur.RoleID
        WHERE emp.UserId = @UserID
          AND ur.RegistrationID = @RegistrationID
          AND ISNULL(ur.IsActive, 1) = 1
          AND ISNULL(r.IsActive, 1) = 1
    ),
    TargetTable AS
    (
        SELECT TOP (1)
            rt.DB_Table,
            ISNULL(rt.Table_Alias, rt.DB_Table) AS Table_Alias,
            rt.WindowOrPage,
            ISNULL(rt.ExposedToUser, 0) AS ExposededToUser
        FROM dbo.FW_Pages AS rt
        WHERE rt.DB_Table = @DB_Table
        ORDER BY CASE WHEN rt.RegistrationID = @RegistrationID THEN 0 ELSE 1 END, rt.PageID DESC
    )
    SELECT
        @UserID AS UserID,
        @RegistrationID AS RegistrationID,
        tt.DB_Table,
        tt.Table_Alias,
        tt.WindowOrPage,
        ISNULL(tt.ExposededToUser, 0) AS ExposededToUser,
        ar.RoleID,
        ar.RoleName,
        CASE WHEN ar.RoleID IS NULL THEN 0 ELSE 1 END AS HasActiveRole,
        ISNULL(rd.Can_Create, 0) AS Can_Create,
        ISNULL(rd.Can_Read, 0) AS Can_Read,
        ISNULL(rd.Can_Update, 0) AS Can_Update,
        ISNULL(rd.Can_Delete, 0) AS Can_Delete,
        ISNULL(rd.Can_UseQBE, 0) AS Can_UseQBE,
        ISNULL(rd.Can_ViewAllRecords, 0) AS Can_ViewAllRecords,
        ISNULL(rd.Can_ViewOnlyMyRecords, 0) AS Can_ViewOnlyMyRecords
    FROM TargetTable AS tt
    LEFT JOIN AssignedRoles AS ar
        ON 1 = 1
    LEFT JOIN dbo.FW_RoleDetails AS rd
        ON rd.RoleID = ar.RoleID
       AND rd.RegistrationID = @RegistrationID
       AND rd.DB_Table = tt.DB_Table
    ORDER BY ar.RoleName, ar.RoleID;
END;
GO

-- SQL_STORED_PROCEDURE: usp_FW_ApplyAccessDiagnosticChanges
CREATE   PROCEDURE dbo.usp_FW_ApplyAccessDiagnosticChanges
    @UserID INT,
    @RegistrationID INT,
    @RoleID INT,
    @SchemaID INT,
    @DB_Table NVARCHAR(128),
    @Can_Create BIT,
    @Can_Read BIT,
    @Can_Update BIT,
    @Can_Delete BIT,
    @Can_UseQBE BIT,
    @Can_ViewAllRecords BIT,
    @Can_ViewOnlyMyRecords BIT,
    @AssignRoleIfMissing BIT,
    @ActorUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Operation NVARCHAR(10);
    DECLARE @RowsAffected INT = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @UserID <= 0 OR @RegistrationID <= 0 OR @RoleID <= 0 OR @SchemaID <= 0 OR NULLIF(LTRIM(RTRIM(@DB_Table)), '') IS NULL
            THROW 52200, 'Required access-maintenance values are missing.', 1;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.FW_EmployeeRoles AS er
            INNER JOIN dbo.FW_Employees AS emp
                ON emp.EmployeeID = er.EmployeeID
            WHERE emp.UserId = @UserID
              AND er.RegistrationID = @RegistrationID
              AND er.RoleID = @RoleID
              AND ISNULL(er.IsActive, 1) = 1
        )
        BEGIN
            IF @AssignRoleIfMissing = 1
            BEGIN
                INSERT INTO dbo.FW_EmployeeRoles
                (RegistrationID, EmployeeID, RoleID, DisplayOrder, IsActive, CreatedBy, CreatedOn)
                SELECT @RegistrationID, emp.EmployeeID, @RoleID, 0, 1, @ActorUserID, GETDATE()
                  FROM dbo.FW_Employees AS emp
                 WHERE emp.UserId = @UserID;
            END
            ELSE
                THROW 52201, 'The selected user does not have the selected active role for this registration.', 1;
        END

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.FW_RoleSchema
            WHERE ID = @SchemaID
              AND DB_Table = @DB_Table
              AND ISNULL(IsActive, 1) = 1
        )
            THROW 52202, 'The selected schema/table does not exist or is inactive.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.FW_RoleDetails
            WHERE RoleID = @RoleID
              AND RegistrationID = @RegistrationID
              AND DB_Table = @DB_Table
        )
        BEGIN
            UPDATE dbo.FW_RoleDetails
            SET Can_Create = @Can_Create,
                Can_Read = @Can_Read,
                Can_Update = @Can_Update,
                Can_Delete = @Can_Delete,
                Can_UseQBE = @Can_UseQBE,
                Can_ViewAllRecords = @Can_ViewAllRecords,
                Can_ViewOnlyMyRecords = @Can_ViewOnlyMyRecords,
                UpdatedBy = @ActorUserID,
                UpdatedOn = GETDATE()
            WHERE RoleID = @RoleID
              AND RegistrationID = @RegistrationID
              AND DB_Table = @DB_Table;
                        SET @Operation = N'UPDATE';
                        SET @RowsAffected = @@ROWCOUNT;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.FW_RoleDetails
            (
                RoleID, RegistrationID, SchemaID, DB_Table, Table_Alias, OverrideCaption,
                Can_Create, Can_Read, Can_Update, Can_Delete, Can_UseQBE,
                Can_ViewAllRecords, Can_ViewOnlyMyRecords, IsActive,
                CreatedBy, CreatedOn
            )
            SELECT
                @RoleID, @RegistrationID, @SchemaID, @DB_Table,
                                ISNULL(Table_Alias, @DB_Table),
                                COALESCE((SELECT TOP 1 NULLIF(LTRIM(RTRIM(OverrideCaption)), '')
                                                    FROM dbo.FW_RoleDetails
                                                    WHERE RegistrationID = @RegistrationID
                                                        AND SchemaID = @SchemaID
                                                        AND DB_Table = @DB_Table
                                                        AND RoleID <> @RoleID
                                                    ORDER BY RoleID), ISNULL(Table_Alias, @DB_Table)),
                @Can_Create, @Can_Read, @Can_Update, @Can_Delete, @Can_UseQBE,
                @Can_ViewAllRecords, @Can_ViewOnlyMyRecords, 1,
                @ActorUserID, GETDATE()
            FROM dbo.FW_RoleSchema
            WHERE ID = @SchemaID;
            SET @Operation = N'INSERT';
            SET @RowsAffected = @@ROWCOUNT;

                        DECLARE @RoleDetailID INT = CONVERT(INT, SCOPE_IDENTITY());
                        INSERT INTO dbo.FW_RoleFields
                        (RoleDetailID, RegistrationID, RoleID, SchemaID, TableName, FieldName, FileLink,
                         FriendlyFieldName, OverrideCaption, Can_Create, Can_Read, Can_Update, IsActive,
                         CreatedBy, CreatedOn)
                        SELECT @RoleDetailID, @RegistrationID, @RoleID, @SchemaID, @DB_Table,
                                     c.COLUMN_NAME, @DB_Table + '.' + c.COLUMN_NAME,
                                     COALESCE((SELECT TOP 1 rf.FriendlyFieldName
                                                FROM dbo.FW_RoleFields rf
                                                WHERE rf.RegistrationID = @RegistrationID
                                                    AND rf.SchemaID = @SchemaID
                                                    AND rf.TableName = @DB_Table
                                                    AND rf.FieldName = c.COLUMN_NAME
                                                    AND rf.RoleID <> @RoleID
                                                    AND NULLIF(LTRIM(RTRIM(rf.FriendlyFieldName)), '') IS NOT NULL
                                                ORDER BY rf.RoleID), c.COLUMN_NAME),
                                     (SELECT TOP 1 rf.OverrideCaption
                                        FROM dbo.FW_RoleFields rf
                                        WHERE rf.RegistrationID = @RegistrationID
                                            AND rf.SchemaID = @SchemaID
                                            AND rf.TableName = @DB_Table
                                            AND rf.FieldName = c.COLUMN_NAME
                                            AND rf.RoleID <> @RoleID
                                        ORDER BY rf.RoleID),
                                     1, 1, 1, 0, @ActorUserID, GETDATE()
                        FROM INFORMATION_SCHEMA.COLUMNS c
                        WHERE c.TABLE_NAME = @DB_Table
                            AND c.COLUMN_NAME NOT IN ('CreatedBy', 'CreatedOn', 'UpdatedBy', 'UpdatedOn')
                            AND NOT EXISTS
                            (
                                    SELECT 1 FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                                    WHERE kcu.TABLE_NAME = @DB_Table
                                        AND kcu.COLUMN_NAME = c.COLUMN_NAME
                                        AND kcu.CONSTRAINT_NAME LIKE 'PK%'
                            );
        END;

        COMMIT TRANSACTION;
        SELECT @Operation AS Operation,
               @RowsAffected AS RowsAffected,
               @UserID AS UserID,
               @RegistrationID AS RegistrationID,
               @RoleID AS RoleID,
               @SchemaID AS SchemaID,
               @DB_Table AS DB_Table,
               CAST(1 AS BIT) AS Committed;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- VIEW: vw_FW_CurrentUser

CREATE   VIEW dbo.vw_FW_CurrentUser AS
SELECT
    u.UserId AS UserId,
    u.RegistrationID,
    r.RegName,
    u.FirstName,
    u.LastName,
    u.FirstLast,
    u.LastFirst,
    u.Email,
    u.Phone,
    u.PasswordHash,
    u.IsActive,
    u.SuperAdmin
FROM dbo.FW_Users AS u
LEFT JOIN dbo.FW_Registration AS r
    ON r.RegistrationID = u.RegistrationID
WHERE ISNULL(u.DeletedFlag, 0) = 0;
GO

-- VIEW: vw_FW_EmployeeRoles
CREATE VIEW dbo.vw_FW_EmployeeRoles AS
SELECT
    er.UserRoleID,
    er.RegistrationID,
    er.EmployeeID,
    e.UserId,
    er.RoleID,
    e.FirstLast,
    r.RoleName,
    er.DisplayOrder,
    er.IsActive,
    er.CreatedBy,
    er.CreatedOn,
    er.UpdatedBy,
    er.UpdatedOn
FROM dbo.FW_EmployeeRoles AS er
INNER JOIN dbo.FW_Employees AS e
    ON e.EmployeeID = er.EmployeeID
INNER JOIN dbo.FW_Roles AS r
    ON r.ID = er.RoleID;
GO

-- VIEW: vw_RandomGuid
CREATE   VIEW dbo.vw_RandomGuid
AS
    SELECT NEWID() AS RandomGuid;
GO

-- SQL_SCALAR_FUNCTION: fn_GenerateRandomCode
CREATE   FUNCTION dbo.fn_GenerateRandomCode()
RETURNS varchar(14)
AS
BEGIN
    DECLARE @Characters varchar(36) =
        'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';

    DECLARE @Guid uniqueidentifier;
    DECLARE @GuidBytes varbinary(16);
    DECLARE @Code varchar(12) = '';
    DECLARE @Position int = 1;
    DECLARE @CharacterPosition int;

    SELECT @Guid = RandomGuid
    FROM dbo.vw_RandomGuid;

    SET @GuidBytes = CONVERT(varbinary(16), @Guid);

    WHILE @Position <= 12
    BEGIN
        SET @CharacterPosition =
            (CONVERT(int, SUBSTRING(@GuidBytes, @Position, 1)) % 36) + 1;

        SET @Code =
            @Code + SUBSTRING(@Characters, @CharacterPosition, 1);

        SET @Position += 1;
    END;

    RETURN
        STUFF(
            STUFF(@Code, 5, 0, '-'),
            10, 0, '-'
        );
END;
GO

-- VIEW: FW_UserPeople

CREATE   VIEW dbo.FW_UserPeople
AS
SELECT
    v.UserId,
    v.RegistrationID,
    CAST('Employee' AS varchar(20))                         AS PersonType,
    e.EmployeeID                                            AS PersonID,
    e.FirstName,
    e.LastName,
    e.FirstLast,
    u.UserName,
    COALESCE(NULLIF(v.Email, ''), e.Email)                  AS Email,
    CAST(NULL AS varchar(75))                               AS CompanyName,
    CAST(CASE WHEN ISNULL(v.IsActive, 0) = 1
               AND ISNULL(e.IsActive, 1) = 1 THEN 1 ELSE 0 END AS bit) AS IsActive,
    CAST(ISNULL(v.IsActive, 0) AS bit)                      AS LoginActive,
    CAST(ISNULL(e.IsActive, 1) AS bit)                      AS PersonActive
  FROM dbo.vw_FW_CurrentUser v
 INNER JOIN dbo.FW_Users u
    ON u.UserId = v.UserId
 INNER JOIN dbo.FW_Employees e
    ON e.UserId = v.UserId
   AND ISNULL(e.DeletedFlag, 0) = 0

UNION ALL

/*  Contractors, when the table exists. Nothing else changes.

SELECT
    v.UserId,
    v.RegistrationID,
    CAST('Contractor' AS varchar(20)),
    c.ContractorID,
    c.FirstName,
    c.LastName,
    c.FirstLast,
    u.UserName,
    COALESCE(NULLIF(v.Email, ''), c.Email),
    c.CompanyName,
    CAST(CASE WHEN ISNULL(v.IsActive, 0) = 1
               AND ISNULL(c.IsActive, 1) = 1 THEN 1 ELSE 0 END AS bit),
    CAST(ISNULL(v.IsActive, 0) AS bit),
    CAST(ISNULL(c.IsActive, 1) AS bit)
  FROM dbo.vw_FW_CurrentUser v
 INNER JOIN dbo.FW_Users u ON u.UserId = v.UserId
 INNER JOIN dbo.FW_Contractors c ON c.UserId = v.UserId AND ISNULL(c.DeletedFlag, 0) = 0

UNION ALL
*/

SELECT
    v.UserId,
    v.RegistrationID,
    CAST('Login only' AS varchar(20))                       AS PersonType,
    CAST(NULL AS int)                                       AS PersonID,
    v.FirstName,
    v.LastName,
    v.FirstLast,
    u.UserName,
    v.Email,
    CAST(NULL AS varchar(75))                               AS CompanyName,
    CAST(ISNULL(v.IsActive, 0) AS bit)                      AS IsActive,
    CAST(ISNULL(v.IsActive, 0) AS bit)                      AS LoginActive,
    CAST(1 AS bit)                                          AS PersonActive
  FROM dbo.vw_FW_CurrentUser v
 INNER JOIN dbo.FW_Users u
    ON u.UserId = v.UserId
 WHERE NOT EXISTS (SELECT 1
                     FROM dbo.FW_Employees e
                    WHERE e.UserId = v.UserId
                      AND ISNULL(e.DeletedFlag, 0) = 0);
GO

-- SQL_STORED_PROCEDURE: usp_FW_RefreshSwitchUser

CREATE   PROCEDURE dbo.usp_FW_RefreshSwitchUser
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

