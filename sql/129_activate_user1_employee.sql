/*
    129_activate_user1_employee.sql

    Makes the "User 1" employee (EmployeeID 8, registration 2) active again.

    Its login, user1 (UserId 12), was active while the employee was not, which left an account that
    could sign in but that Switch User reported as "found, but is inactive". Both are meant to be
    active: decided 2026-09-17.

    Only employee 8, and only while it is still inactive. Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

SELECT 'BEFORE' AS Stage, e.EmployeeID, e.FirstName, e.LastName, e.IsActive AS EmployeeActive,
       u.UserId, u.UserName, u.IsActive AS LoginActive
  FROM dbo.FW_Employees e
  JOIN dbo.FW_Users u ON u.UserId = e.UserId
 WHERE e.EmployeeID = 8;

UPDATE dbo.FW_Employees
   SET IsActive = 1,
       UpdatedBy = 2,
       UpdatedOn = GETDATE()
 WHERE EmployeeID = 8
   AND UserId = 12
   AND ISNULL(IsActive, 0) = 0;

PRINT 'Employees made active: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

SELECT 'AFTER' AS Stage, e.EmployeeID, e.FirstName, e.LastName, e.IsActive AS EmployeeActive,
       u.UserId, u.UserName, u.IsActive AS LoginActive
  FROM dbo.FW_Employees e
  JOIN dbo.FW_Users u ON u.UserId = e.UserId
 WHERE e.EmployeeID = 8;
