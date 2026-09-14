-- The unique email goes; the unique user name stays.
--
-- UX_FW_Users_Email was there because an email was how somebody signed in: two accounts sharing
-- one left login resolving by row order. Login moved to UserName on 2026-09-14, so an email is
-- now only a way to reach a person - two people can legitimately share one, and across
-- registrations they routinely do, which this index forbade outright.
--
-- Nothing replaces it. UX_FW_Users_UserName already carries the invariant, on the column that
-- actually authenticates, and the save path refuses a duplicate by name before the index has to.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FW_Users_Email' AND object_id = OBJECT_ID('dbo.FW_Users'))
BEGIN
    DROP INDEX UX_FW_Users_Email ON dbo.FW_Users;
END
GO
