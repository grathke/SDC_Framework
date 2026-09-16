/*
    113_delete_orphan_logins.sql

    Deletes logins that have no employee.

    Two test accounts from early September, 1@1.com and 3@3.com, both still active in
    registration 1. Employees own the login now, so a FW_Users row with no employee behind it is
    an account nobody can be. Checked first: no UI hints, no saved layouts, and nothing created by
    either of them.

    Physical, asked for on 2026-09-15. A soft-deleted login still turns up in every sweep looking
    for rows that point at nothing, and these were never people.

    FW_AuditTrail is untouched: it records what happened, which stays true.

    Guarded rather than keyed on ids - it deletes only what still has no employee, so it cannot
    take a real account if one is added later.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

SELECT 'TO DELETE' AS Stage, u.UserId, u.UserName, u.RegistrationID
  FROM dbo.FW_Users u
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = u.UserId)
 ORDER BY u.UserId;

BEGIN TRANSACTION;

DELETE FROM dbo.FW_UserUiHints
 WHERE UserID IN (SELECT u.UserId FROM dbo.FW_Users u
                   WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = u.UserId));
PRINT 'FW_UserUiHints deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

DELETE FROM dbo.FW_Users
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = dbo.FW_Users.UserId);
PRINT 'FW_Users deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

SELECT 'REMAINING LOGINS' AS Stage, u.RegistrationID, u.UserId, u.UserName,
       ISNULL(u.FirstName, '(null)') AS FirstName, ISNULL(u.LastName, '(null)') AS LastName
  FROM dbo.FW_Users u
 ORDER BY u.RegistrationID, u.UserId;

SELECT 'ORPHAN CHECK - should be empty' AS Stage, COUNT(*) AS Rows
  FROM dbo.FW_Users u
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = u.UserId);
