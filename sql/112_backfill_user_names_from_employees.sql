/*
    112_backfill_user_names_from_employees.sql

    Copies FirstName and LastName from the employee onto its login, where the login has none.

    Accounts created through the Employees page got a FW_Users row carrying only a user name -
    CreateLoginForEmployee never wrote the person's name - which left the table unreadable to
    anyone looking at it, and showed as a blank row on the User Access Diagnostic.

    The create path was fixed the same day. This is the rows already written.

    Only where the login has no name of its own. A login whose name differs from the employee's is
    left alone: that is a disagreement to look at, not one to overwrite silently.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

SELECT 'BEFORE' AS Stage, u.UserId, u.UserName,
       ISNULL(u.FirstName, '(null)') AS UserFirst, ISNULL(u.LastName, '(null)') AS UserLast,
       ISNULL(e.FirstName, '(null)') AS EmpFirst, ISNULL(e.LastName, '(null)') AS EmpLast
  FROM dbo.FW_Users u
  JOIN dbo.FW_Employees e ON e.UserId = u.UserId
 WHERE (NULLIF(LTRIM(RTRIM(ISNULL(u.FirstName, ''))), '') IS NULL
     OR NULLIF(LTRIM(RTRIM(ISNULL(u.LastName, ''))), '') IS NULL)
 ORDER BY u.UserId;

UPDATE u
   SET u.FirstName = COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(u.FirstName, ''))), ''), e.FirstName),
       u.LastName  = COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(u.LastName, ''))), ''), e.LastName),
       u.UpdatedBy = 2,
       u.UpdatedOn = GETDATE()
  FROM dbo.FW_Users u
  JOIN dbo.FW_Employees e ON e.UserId = u.UserId
 WHERE NULLIF(LTRIM(RTRIM(ISNULL(u.FirstName, ''))), '') IS NULL
    OR NULLIF(LTRIM(RTRIM(ISNULL(u.LastName, ''))), '') IS NULL;

PRINT 'Logins given a name: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

SELECT 'AFTER - logins still without a name' AS Stage, u.RegistrationID, u.UserId, u.UserName,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = u.UserId)
            THEN 'has an employee' ELSE 'no employee row' END AS Why
  FROM dbo.FW_Users u
 WHERE NULLIF(LTRIM(RTRIM(ISNULL(u.FirstName, ''))), '') IS NULL
    OR NULLIF(LTRIM(RTRIM(ISNULL(u.LastName, ''))), '') IS NULL
 ORDER BY u.RegistrationID, u.UserId;
