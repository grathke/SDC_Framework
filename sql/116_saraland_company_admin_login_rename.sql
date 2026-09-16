/*
    116_saraland_company_admin_login_rename.sql

    Renames Saraland's Company Admin login from CompanyAdmin to CA.

    Both rows, together. FW_Users.UserName is what somebody types at the login screen and
    FW_Employees.UserName is the same fact on the employee - SyncLoginUserName keeps them in step
    when the page edits one, and a rename that touched only one half is an employee who cannot
    sign in.

    The password is set in the step after this: it is keyed on the UserId, not the name, so a
    rename does not disturb it - but this one is being reset to 1234 to match the others.

    Registration 2 only. Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM dbo.FW_Users WHERE UserName = 'CA')
BEGIN
    PRINT 'A login named CA already exists - nothing to do';
    SELECT UserId, RegistrationID, UserName FROM dbo.FW_Users WHERE UserName = 'CA';
    RETURN;
END

BEGIN TRANSACTION;

UPDATE dbo.FW_Users
   SET UserName = 'CA', UpdatedBy = 2, UpdatedOn = GETDATE()
 WHERE RegistrationID = 2 AND UserName = 'CompanyAdmin';
PRINT 'FW_Users renamed: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

UPDATE dbo.FW_Employees
   SET UserName = 'CA', UpdatedBy = 2, UpdatedOn = GETDATE()
 WHERE RegistrationID = 2 AND UserName = 'CompanyAdmin';
PRINT 'FW_Employees renamed: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

SELECT 'SARALAND LOGINS' AS Area, e.EmployeeID, e.FirstLast AS Employee, e.UserName AS Login, u.UserId,
       ISNULL(STUFF((SELECT ', ' + r.RoleName
                       FROM dbo.FW_EmployeeRoles er
                       JOIN dbo.FW_Roles r ON r.ID = er.RoleID
                      WHERE er.EmployeeID = e.EmployeeID AND ISNULL(er.DeletedFlag, 0) = 0
                      ORDER BY r.RoleName
                        FOR XML PATH('')), 1, 2, ''), '(none)') AS Roles
  FROM dbo.FW_Employees e
  JOIN dbo.FW_Users u ON u.UserId = e.UserId
 WHERE e.RegistrationID = 2 AND ISNULL(e.DeletedFlag, 0) = 0
 ORDER BY e.EmployeeID;
