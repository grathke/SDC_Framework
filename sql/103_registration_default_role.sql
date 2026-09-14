-- A default role for somebody created from the employee page.
--
-- Creating an employee gives them a person record and a login, and no way in: roles are keyed to
-- the employee and nobody had assigned one, so a brand-new account signs in correctly and is
-- turned away with "you do not have any roles assigned". Making the create screen a two-step job
-- that looks like one.
--
-- Per registration, because roles are. Each company names its own baseline, and company 2's
-- role ids mean nothing to company 1.
--
-- NULL keeps the old behaviour deliberately: a registration that has not named a default gets
-- no automatic role, and the administrator assigns one as before. This should not start granting
-- access to companies that never asked for it.
--
-- Named DefaultRoleID rather than the convention's "exactly the primary key it points at".
-- FW_Roles keys on a bare ID, which the convention itself calls out as the one name that cannot
-- survive a join - a column called ID on FW_Registration would say nothing at all.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'DefaultRoleID')
BEGIN
    ALTER TABLE dbo.FW_Registration ADD DefaultRoleID int NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Registration_DefaultRole')
BEGIN
    ALTER TABLE dbo.FW_Registration WITH CHECK
        ADD CONSTRAINT FK_FW_Registration_DefaultRole
        FOREIGN KEY (DefaultRoleID) REFERENCES dbo.FW_Roles (ID);
END
GO
