/*
    071_add_user_assigned_manager.sql
    ==============================================================================================
    Adds dbo.FW_Users.AssignedManagerID, a self-reference to dbo.FW_Users.UserId.

    A user's manager is another user, so the column points back at the table it lives on.

    ----------------------------------------------------------------------------------------------
    THE NAME IS A DELIBERATE EXCEPTION
    ----------------------------------------------------------------------------------------------
    CLAUDE.md's convention says a second reference to a target takes a role prefix in front of the
    target's key - <Role><TargetPK> - and gives this very case as its example: AssignedManagerUserID.
    The reasoning is that stripping the role leaves UserID, which names what the column points at,
    whereas AssignedManagerID reads as pointing at a Manager table that does not exist.

    AssignedManagerID was chosen anyway, on 2026-09-03, deliberately and with the convention in
    front of us. Recorded here so it reads as a decision rather than an oversight, and so the next
    person does not "fix" it.

    ----------------------------------------------------------------------------------------------
    NULLABLE, AND NO CASCADE
    ----------------------------------------------------------------------------------------------
    Nullable because most people have no manager recorded, and because the alternative - a sentinel
    like 0 or -1 - is exactly what sql/048 had to clean out of FW_ENTITY and sql/070 out of
    FW_Users.GenderID. A key cannot point at nothing, so it points at NULL.

    No ON DELETE action. SQL Server will not allow a cascade on a self-reference, and it should not
    be wanted here: deleting a manager must not delete the people who report to them, and silently
    nulling their manager on delete would hide a change worth noticing. Users are soft deleted in
    any case, so the row stays and the reference stays valid.

    Nothing stops a user being their own manager, or two users managing each other. A foreign key
    cannot express either. Self-selection is prevented in the maintenance page's lookup; longer
    cycles are a business rule nobody has asked for yet.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- 1. The column.
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.FW_Users', 'AssignedManagerID') IS NULL
BEGIN
    ALTER TABLE dbo.FW_Users ADD AssignedManagerID int NULL;
    PRINT 'Added FW_Users.AssignedManagerID';
END
ELSE
BEGIN
    PRINT 'FW_Users.AssignedManagerID already exists.';
END

COMMIT TRANSACTION;
GO

------------------------------------------------------------------------------------------------
-- 2. The key, in its own batch: the column must exist before it can be referenced.
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Users_AssignedManagerID_FW_Users')
BEGIN
    ALTER TABLE dbo.FW_Users WITH CHECK
        ADD CONSTRAINT FK_FW_Users_AssignedManagerID_FW_Users
        FOREIGN KEY (AssignedManagerID) REFERENCES dbo.FW_Users (UserId);
    PRINT 'Declared FK_FW_Users_AssignedManagerID_FW_Users';
END
GO

PRINT '';
PRINT '=== Foreign keys on FW_Users ===';
SELECT fk.name                                              AS ConstraintName,
       COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
       OBJECT_NAME(fk.referenced_object_id)                 AS ReferencesTable,
       CASE WHEN fk.is_not_trusted = 0 THEN 'trusted' ELSE 'NOT TRUSTED' END AS Trust
FROM   sys.foreign_keys AS fk
       JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
WHERE  fk.parent_object_id = OBJECT_ID('dbo.FW_Users')
ORDER  BY fk.name;
