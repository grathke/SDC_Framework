-- 088_seed_employees_from_users.sql
--
-- Seeds FW_Employees from FW_Users, as the first step of separating a person from a login.
--
-- Additive only: FW_Users is not touched. If this is wrong, the fix is to empty FW_Employees and
-- run it again - nothing else depends on it yet.
--
-- Safe to run more than once: it inserts only users who have no employee row, matched on UserId.
--
-- Not mapped, because FW_Users has no source for them: RegionID, Country, Extension,
-- DoNotSendEmail, DoNotSendPhone, Inspector, Notes, Photo, PhotoPath, Title.
--
-- ReportsTo is left null deliberately. FW_Users.AssignedManagerID is a key and ReportsTo is text,
-- so filling it means choosing a name format and freezing it - a decision, not a migration.
--
-- Truncations, all of them widening columns that are narrower in FW_Employees:
--   FirstName  50  -> 45
--   LastName   50  -> 45
--   FullName  101  -> 75   (from FirstLast)
--   Email     128  -> 100

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

INSERT INTO dbo.FW_Employees
    (UserId, RegistrationId, IsActive,
     FirstName, LastName, FullName, UserName,
     Address1, Address2, City, State, Zip, Email, CellPhone,
     BirthDate, HireDate, TermminateDate,
     PrimaryJobId, PrimarySubJobId,
     CreatedBy, CreatedOn, UpdatedBy, UpdatedOn,
     DeletedFlag, DeletedBy, DeletedOn)
SELECT
    u.UserId,
    u.RegistrationID,
    u.IsActive,
    LEFT(ISNULL(u.FirstName, ''), 45),
    LEFT(ISNULL(u.LastName, ''), 45),
    LEFT(NULLIF(LTRIM(RTRIM(u.FirstLast)), ''), 75),
    u.UserName,
    u.Address1,
    u.Address2,
    u.City,
    u.State,
    u.Zip,
    LEFT(u.Email, 100),
    u.Phone,
    u.BirthDate,
    u.StartDate,
    u.EndDate,
    u.PrimaryJobId,
    u.PrimarySubJobId,
    u.CreatedBy,
    u.CreatedOn,
    u.UpdatedBy,
    u.UpdatedOn,
    u.DeletedFlag,
    u.DeletedBy,
    u.DeletedOn
FROM dbo.FW_Users u
WHERE u.UserName IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = u.UserId);

PRINT 'Employees seeded from users.';
GO

-- What went in, and anybody skipped for having no user name.
SELECT e.EmployeeID, e.UserId, e.UserName, e.FirstName, e.LastName, e.FullName, e.Email, e.IsActive
FROM dbo.FW_Employees e
ORDER BY e.UserId;

SELECT SkippedForNoUserName = COUNT(1)
FROM dbo.FW_Users u
WHERE u.UserName IS NULL;
GO

-- ReportsTo, from the manager's name.
--
-- Text where FW_Users.AssignedManagerID is a key, so this freezes a name format - FirstLast, the
-- same one the application displays. It is a copy rather than a link: renaming a manager in
-- FW_Users will not change what an employee row says, which is the cost of the column's type.
--
-- Only fills what is empty, so a value edited by hand survives a re-run.
UPDATE e
   SET e.ReportsTo = LEFT(NULLIF(LTRIM(RTRIM(m.FirstLast)), ''), 75)
  FROM dbo.FW_Employees e
  JOIN dbo.FW_Users u ON u.UserId = e.UserId
  JOIN dbo.FW_Users m ON m.UserId = u.AssignedManagerID
 WHERE e.ReportsTo IS NULL;

PRINT 'ReportsTo filled from the assigned manager.';
GO
