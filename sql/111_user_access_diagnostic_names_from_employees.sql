/*
    111_user_access_diagnostic_names_from_employees.sql

    FW_UserAccessDiagnostic_B took its names straight from FW_Users, which is why an account
    created through the Employees page showed as a blank row: the names live on the employee now,
    and the login carries only credentials.

    The key stays UserId. The page hands it to GetAccessDiagnostic, GetAccessDiagnosticRegistrationRoles
    and ApplyAccessDiagnosticChanges, all of which are about the login rather than the person.
    Only the display columns move to FW_Employees.

    Also scoped to a registration, which it never was - it listed every registration's logins.

    INNER JOIN, not LEFT: a login with no employee is not a person whose access can be diagnosed.
    Two such rows exist in registration 1 (1@1.com and 3@3.com) and will no longer be listed.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

UPDATE dbo.FW_Pages
   SET Table_SQL =
'SELECT
    U.[UserId] AS PK,
    E.[FirstName],
    E.[LastName],
    E.[FirstLast],
    U.[UserName]
FROM dbo.[FW_Users] U
INNER JOIN dbo.[FW_Employees] E ON E.[UserId] = U.[UserId]
WHERE U.[RegistrationID] = @RegistrationID
ORDER BY E.[FirstLast] ASC'
 WHERE WindowOrPage = 'FW_UserAccessDiagnostic_B';

PRINT 'Table_SQL updated on ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' page row(s)';

SELECT Table_SQL FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_UserAccessDiagnostic_B';

SELECT 'LOGINS WITH NO EMPLOYEE - these drop off the page' AS Note,
       u.RegistrationID, u.UserId, u.UserName
  FROM dbo.FW_Users u
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = u.UserId)
 ORDER BY u.RegistrationID, u.UserId;
