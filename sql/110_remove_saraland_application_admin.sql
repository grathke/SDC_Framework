/*
    110_remove_saraland_application_admin.sql

    Removes Saraland's own Application Admin role. Saraland is administered from registration 1
    instead, by choosing it in the registration combo.

    Physical delete, asked for explicitly on 2026-09-15: the role and its permissions are not
    wanted back, and a soft-deleted role still appears in the sweeps that look for rows pointing
    at nothing.

    Deleted, for RoleID 17 only:
        FW_RoleFields      212
        FW_RoleDetails      10
        FW_EmployeeRoles     3   Glenn, Sandy, Alan
        FW_Roles             1

    Alan Sawyer held no other role, so he is given User - a login with no role at all can sign in
    and do nothing, which reads as a broken account rather than a deliberate one.

    Registration 1 is untouched. FW_AuditTrail is untouched: it records what happened, which stays
    true.

    Re-runnable: everything is keyed on a role that will not be found the second time.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @roleId INT = (SELECT ID FROM dbo.FW_Roles WHERE RegistrationID = 2 AND RoleName = 'Application Admin');
DECLARE @userRoleId INT = (SELECT ID FROM dbo.FW_Roles WHERE RegistrationID = 2 AND RoleName = 'User');

IF @roleId IS NULL
BEGIN
    PRINT 'Saraland has no Application Admin role - nothing to do';
    RETURN;
END

IF @userRoleId IS NULL
BEGIN
    PRINT 'REFUSED: Saraland has no User role to move Alan to.';
    RETURN;
END

BEGIN TRANSACTION;

-- Anyone left with nothing gets User, before the role goes. After the delete there would be no
-- way to tell who had been affected.
INSERT INTO dbo.FW_EmployeeRoles (RegistrationID, EmployeeID, RoleID, DisplayOrder, IsActive, DeletedFlag,
                                  CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT 2, e.EmployeeID, @userRoleId, 1, 1, 0, 2, GETDATE(), 2, GETDATE()
  FROM dbo.FW_Employees e
 WHERE e.RegistrationID = 2
   AND EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles h WHERE h.EmployeeID = e.EmployeeID AND h.RoleID = @roleId)
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles k
                    WHERE k.EmployeeID = e.EmployeeID AND k.RoleID <> @roleId AND ISNULL(k.DeletedFlag, 0) = 0);

PRINT 'Given the User role: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' employee(s)';

DELETE FROM dbo.FW_EmployeeRoles WHERE RoleID = @roleId;
PRINT 'FW_EmployeeRoles deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

DELETE FROM dbo.FW_RoleFields WHERE RoleID = @roleId;
PRINT 'FW_RoleFields deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

DELETE FROM dbo.FW_RoleDetails WHERE RoleID = @roleId;
PRINT 'FW_RoleDetails deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

DELETE FROM dbo.FW_Roles WHERE ID = @roleId;
PRINT 'FW_Roles deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

SELECT 'SARALAND ROLES' AS Area, RoleName, ID FROM dbo.FW_Roles WHERE RegistrationID = 2 ORDER BY DisplayOrder, RoleName;

SELECT 'SARALAND EMPLOYEE ROLES' AS Area, e.FirstLast, r.RoleName
  FROM dbo.FW_EmployeeRoles er
  JOIN dbo.FW_Employees e ON e.EmployeeID = er.EmployeeID
  JOIN dbo.FW_Roles r ON r.ID = er.RoleID
 WHERE er.RegistrationID = 2 AND ISNULL(er.DeletedFlag, 0) = 0
 ORDER BY e.FirstLast, r.RoleName;

SELECT 'ORPHAN CHECK - should be empty' AS Area, 'FW_RoleFields' AS T, COUNT(*) AS Rows
  FROM dbo.FW_RoleFields f WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Roles r WHERE r.ID = f.RoleID)
UNION ALL
SELECT 'ORPHAN CHECK - should be empty', 'FW_RoleDetails', COUNT(*)
  FROM dbo.FW_RoleDetails d WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Roles r WHERE r.ID = d.RoleID)
UNION ALL
SELECT 'ORPHAN CHECK - should be empty', 'FW_EmployeeRoles', COUNT(*)
  FROM dbo.FW_EmployeeRoles er WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Roles r WHERE r.ID = er.RoleID);
