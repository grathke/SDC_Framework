/*
    114_saraland_role_users.sql

    One employee per Saraland role below Company Admin, named for its position in the list:

        User 1   User1   -> User RW    (the role just below Company Admin)
        User 2   User2   -> User
        User 3   User3   -> User RO

    User 1 already exists and held Company Admin, which was inherited from the copy of
    registration 1's "New User". It is moved to User RW rather than recreated, so its login and
    password survive.

    Registration 2 only. Nothing in registration 1 is touched.

    PasswordHash is not written here - it is keyed on the UserId, which does not exist until the
    row does. The step after this one sets it for any new login.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @dst INT = 2;
DECLARE @by  INT = 2;

DECLARE @plan TABLE (FirstName VARCHAR(100), LastName VARCHAR(100), Login VARCHAR(200), RoleName VARCHAR(100));
INSERT INTO @plan VALUES
 ('User', '1', 'User1', 'User RW'),
 ('User', '2', 'User2', 'User'),
 ('User', '3', 'User3', 'User RO');

-- Refuse rather than half-build if a role is missing.
IF EXISTS (SELECT 1 FROM @plan p
            WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Roles r
                               WHERE r.RegistrationID = @dst AND r.RoleName = p.RoleName))
BEGIN
    PRINT 'REFUSED: a named role does not exist in registration 2.';
    SELECT p.RoleName AS MissingRole FROM @plan p
     WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Roles r WHERE r.RegistrationID = @dst AND r.RoleName = p.RoleName);
    RETURN;
END

BEGIN TRANSACTION;

/* Logins. FirstLast and LastFirst are computed. */
INSERT INTO dbo.FW_Users (RegistrationID, FirstName, LastName, UserName, IsActive,
                          CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, p.FirstName, p.LastName, p.Login, 1, @by, GETDATE(), @by, GETDATE()
  FROM @plan p
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Users u WHERE u.UserName = p.Login);

PRINT 'Logins created: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

INSERT INTO dbo.FW_Employees (RegistrationID, UserId, FirstName, LastName, UserName, IsActive, DeletedFlag,
                              CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, u.UserId, p.FirstName, p.LastName, p.Login, 1, 0, @by, GETDATE(), @by, GETDATE()
  FROM @plan p
  JOIN dbo.FW_Users u ON u.UserName = p.Login AND u.RegistrationID = @dst
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserName = p.Login AND e.RegistrationID = @dst);

PRINT 'Employees created: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

/* Exactly one role each - anything they already hold that is not the planned role goes. */
DELETE er
  FROM dbo.FW_EmployeeRoles er
  JOIN dbo.FW_Employees e ON e.EmployeeID = er.EmployeeID
  JOIN @plan p ON p.Login = e.UserName
  JOIN dbo.FW_Roles r ON r.ID = er.RoleID
 WHERE e.RegistrationID = @dst AND r.RoleName <> p.RoleName;

PRINT 'Other roles removed: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

INSERT INTO dbo.FW_EmployeeRoles (RegistrationID, EmployeeID, RoleID, DisplayOrder, IsActive, DeletedFlag,
                                  CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, e.EmployeeID, r.ID, r.DisplayOrder, 1, 0, @by, GETDATE(), @by, GETDATE()
  FROM @plan p
  JOIN dbo.FW_Employees e ON e.UserName = p.Login AND e.RegistrationID = @dst
  JOIN dbo.FW_Roles r ON r.RegistrationID = @dst AND r.RoleName = p.RoleName
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles d
                    WHERE d.EmployeeID = e.EmployeeID AND d.RoleID = r.ID);

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
