-- 144_health_page_permission.sql
--
-- The permission that gates the System Health page.
--
-- FW_Health_B browses no table, so there is nothing for FW_RoleSchema to point at and nothing an
-- administrator could grant. CLAUDE.md's answer to that is a table whose only job is to carry a
-- permission: prefix FW_Perm_, one column, no data, and no _B/_U pair. FW_Perm_Dashboard is the
-- worked example and this follows it exactly.
--
-- It is a real table and not a name invented at a call site, deliberately. Removing a page
-- requires a sweep reporting every FW_RoleDetails row whose OBJECT_ID('dbo.' + DB_Table) is null,
-- and a permission keyed to nothing would sit in that report for ever until somebody learned to
-- ignore it. TableMessaging was that mistake - it read "MESSAGING", matched no table and no
-- FW_RoleSchema row, and the permission it resolved against could never be granted by anyone.
--
-- The empty table is also a warning to whoever finds it: a table with no rows and no marker looks
-- like one whose data has gone missing, and reads as a candidate for deletion. The prefix says it
-- is meant to be empty.
--
-- Granted to the Application Admin role in registration 1 only. The page reports across every
-- registration by design (HEALTH_DASHBOARD_SPEC.md section 5), and that role exists in our own
-- registration and nowhere else - which is what stops the capability being handed to a customer
-- by accident.
--
-- Safe to re-run: every step checks first.

SET NOCOUNT ON;
GO

-- 1. The table. One column, the primary key the convention asks for, and nothing else.
IF OBJECT_ID('dbo.FW_Perm_SystemHealth', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_Perm_SystemHealth
    (
        SystemHealthID int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_Perm_SystemHealth PRIMARY KEY
    );
    PRINT 'CREATED: dbo.FW_Perm_SystemHealth';
END
ELSE
    PRINT 'SKIPPED: dbo.FW_Perm_SystemHealth already exists';
GO

-- 2. The FW_RoleSchema row, or Roles_U will not offer it.
--    Table_Alias is what an administrator should read - "System Health", not the table name.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_RoleSchema WHERE DB_Table = 'FW_Perm_SystemHealth')
BEGIN
    INSERT INTO dbo.FW_RoleSchema (DB_Table, Table_Alias, IsActive, CreatedBy, CreatedOn, DeletedFlag)
    VALUES ('FW_Perm_SystemHealth', 'System Health', 1, 2, SYSUTCDATETIME(), 0);
    PRINT 'ADDED: FW_RoleSchema row for FW_Perm_SystemHealth';
END
ELSE
    PRINT 'SKIPPED: FW_RoleSchema row already exists';
GO

-- 3. Read for the Application Admin role, and nothing else.
--    Read is the only capability that means anything here - there is no record to create, update
--    or delete. Acknowledging a fault is a write, but it is a write to FW_ErrorLog, gated by
--    reaching this page at all.
DECLARE @SchemaID int =
    (SELECT TOP 1 ID FROM dbo.FW_RoleSchema WHERE DB_Table = 'FW_Perm_SystemHealth');

DECLARE @RoleID int =
    (SELECT TOP 1 ID FROM dbo.FW_Roles
     WHERE RegistrationID = 1 AND ISNULL(Typ_AppAdmin, 0) = 1
       AND ISNULL(IsActive, 1) = 1 AND ISNULL(DeletedFlag, 0) = 0);

IF @SchemaID IS NULL
    PRINT 'ABORTED: no FW_RoleSchema row - step 2 did not run';
ELSE IF @RoleID IS NULL
    PRINT 'ABORTED: no Application Admin role found in registration 1';
ELSE IF EXISTS (SELECT 1 FROM dbo.FW_RoleDetails
                WHERE RegistrationID = 1 AND RoleID = @RoleID AND DB_Table = 'FW_Perm_SystemHealth')
    PRINT 'SKIPPED: FW_RoleDetails row already exists';
ELSE
BEGIN
    INSERT INTO dbo.FW_RoleDetails
        (RegistrationID, RoleID, SchemaID, DB_Table, Table_Alias, OverrideCaption,
         Can_Create, Can_Read, Can_Update, Can_Delete,
         Can_UseQBE, Can_ViewAllRecords, Can_ViewOnlyMyRecords,
         IsActive, CreatedBy, CreatedOn, DeletedFlag)
    VALUES
        (1, @RoleID, @SchemaID, 'FW_Perm_SystemHealth', 'System Health', 'System Health',
         0, 1, 0, 0,
         0, 0, 0,
         1, 2, SYSUTCDATETIME(), 0);

    PRINT 'GRANTED: Read on System Health to the Application Admin role';
END
GO

-- What this looks like afterwards.
SELECT rs.ID AS SchemaID, rs.DB_Table, rs.Table_Alias,
       rd.RoleID, r.RoleName, rd.Can_Read
FROM dbo.FW_RoleSchema rs
LEFT JOIN dbo.FW_RoleDetails rd ON rd.DB_Table = rs.DB_Table
LEFT JOIN dbo.FW_Roles r ON r.ID = rd.RoleID
WHERE rs.DB_Table = 'FW_Perm_SystemHealth';
