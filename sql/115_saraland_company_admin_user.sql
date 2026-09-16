/*
    115_saraland_company_admin_user.sql

    The Company Admin counterpart to the role users in 114:

        Company Admin   CompanyAdmin   -> Company Admin

    Same convention as User 1, User 2 and User 3: named for the role it holds, holding that role
    and no other.

    Registration 2 only.

    PasswordHash is set in the step after this one - it is keyed on the UserId, which does not
    exist until the row does.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @dst INT = 2;
DECLARE @by  INT = 2;
DECLARE @roleId INT = (SELECT ID FROM dbo.FW_Roles WHERE RegistrationID = @dst AND RoleName = 'Company Admin');

IF @roleId IS NULL
BEGIN
    PRINT 'REFUSED: registration 2 has no Company Admin role.';
    RETURN;
END

BEGIN TRANSACTION;

INSERT INTO dbo.FW_Users (RegistrationID, FirstName, LastName, UserName, IsActive,
                          CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, 'Company', 'Admin', 'CompanyAdmin', 1, @by, GETDATE(), @by, GETDATE()
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Users u WHERE u.UserName = 'CompanyAdmin');

PRINT 'Logins created: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

INSERT INTO dbo.FW_Employees (RegistrationID, UserId, FirstName, LastName, UserName, IsActive, DeletedFlag,
                              CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, u.UserId, 'Company', 'Admin', 'CompanyAdmin', 1, 0, @by, GETDATE(), @by, GETDATE()
  FROM dbo.FW_Users u
 WHERE u.UserName = 'CompanyAdmin' AND u.RegistrationID = @dst
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserName = 'CompanyAdmin' AND e.RegistrationID = @dst);

PRINT 'Employees created: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

INSERT INTO dbo.FW_EmployeeRoles (RegistrationID, EmployeeID, RoleID, DisplayOrder, IsActive, DeletedFlag,
                                  CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, e.EmployeeID, @roleId, r.DisplayOrder, 1, 0, @by, GETDATE(), @by, GETDATE()
  FROM dbo.FW_Employees e
  JOIN dbo.FW_Roles r ON r.ID = @roleId
 WHERE e.UserName = 'CompanyAdmin' AND e.RegistrationID = @dst
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles d WHERE d.EmployeeID = e.EmployeeID AND d.RoleID = @roleId);

PRINT 'Roles assigned: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

SELECT 'SARALAND EMPLOYEES' AS Area, e.EmployeeID, e.FirstLast AS Employee, e.UserName AS Login,
       ISNULL(STUFF((SELECT ', ' + r.RoleName
                       FROM dbo.FW_EmployeeRoles er
                       JOIN dbo.FW_Roles r ON r.ID = er.RoleID
                      WHERE er.EmployeeID = e.EmployeeID AND ISNULL(er.DeletedFlag, 0) = 0
                      ORDER BY r.RoleName
                        FOR XML PATH('')), 1, 2, ''), '(none)') AS Roles
  FROM dbo.FW_Employees e
 WHERE e.RegistrationID = @dst AND ISNULL(e.DeletedFlag, 0) = 0
 ORDER BY e.EmployeeID;

SELECT 'NEEDS A PASSWORD' AS Area, u.UserId, u.UserName
  FROM dbo.FW_Users u
 WHERE u.RegistrationID = @dst AND ISNULL(u.PasswordHash, '') = ''
 ORDER BY u.UserId;
