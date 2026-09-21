-- DECIDED 2026-09-21: DO NOT RUN THIS YET. The rows stay.
--
-- They were inserted to exercise the Find timing and were going to be deleted afterwards. Glenn's
-- argument for keeping them is the better one: a city reaches 10,000 permits, inspections or
-- transactions soon enough, and a framework with no table that size has nowhere to notice what
-- happens at that size. They have already earned it twice - the row cap being off for filtered
-- searches, and QBE filtering in memory, were both found because this table is big enough to make
-- the difference visible.
--
-- The table is arbitrary. The row count is the point. Run this when something else reaches that
-- size, or when the timings need a small-table baseline to compare against.
--
-- 147_loadtest_employees_remove.sql
--
-- Takes out everything 146 put in.
--
-- Matched on '@loadtest.invalid' in UserName. The .invalid TLD is reserved by RFC 2606 and can
-- never be a real address, so this cannot match a record somebody actually uses. That is the
-- reason the marker is what it is rather than a name prefix or a range of ids.
--
-- A HARD DELETE, not a soft one, and this is the exception rather than a change of policy. The
-- soft-delete rule exists so a real record can be recovered and so the audit trail still resolves
-- against it. These rows were never real: nobody ever edited one, nothing points at one, and
-- leaving ten thousand DeletedFlag = 1 rows behind would mean every browse page carrying them in
-- its deleted view for ever, which is the opposite of cleaning up.
--
-- Employees go first, because their UserId points at the users.
--
-- Safe to run when there is nothing to remove, and safe to run twice.

SET NOCOUNT ON;
GO

DECLARE @Employees int, @Users int;

-- Anything pointing at one of these employees has to go first, or the delete will be refused.
-- There should be none of it: 146 creates no roles and these accounts cannot be signed into. It
-- is here because "should be none" is not the same as "is none", and a failed delete halfway
-- through is worse than a slow one.
DELETE er
FROM dbo.FW_EmployeeRoles er
JOIN dbo.FW_Employees e ON e.EmployeeID = er.EmployeeID
WHERE e.UserName LIKE '%@loadtest.invalid';

IF @@ROWCOUNT > 0
    PRINT 'REMOVED employee role rows: ' + CAST(@@ROWCOUNT AS varchar(10));

DELETE FROM dbo.FW_Employees
WHERE UserName LIKE '%@loadtest.invalid';

SET @Employees = @@ROWCOUNT;

-- Users only once no employee points at them, so a real account that happened to be caught by
-- the marker could not be taken out from under a live employee row.
DELETE FROM dbo.FW_Users
WHERE UserName LIKE '%@loadtest.invalid'
  AND NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = FW_Users.UserId);

SET @Users = @@ROWCOUNT;

PRINT 'REMOVED employees: ' + CAST(@Employees AS varchar(10));
PRINT 'REMOVED users: ' + CAST(@Users AS varchar(10));
GO

-- Both should be zero.
SELECT
    (SELECT COUNT(*) FROM dbo.FW_Employees WHERE UserName LIKE '%@loadtest.invalid') AS EmployeesLeft,
    (SELECT COUNT(*) FROM dbo.FW_Users WHERE UserName LIKE '%@loadtest.invalid') AS UsersLeft;

SELECT COUNT(*) AS TotalEmployees FROM dbo.FW_Employees;
