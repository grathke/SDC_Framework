-- FW_Registration.DefaultRoleID goes, the same day it arrived.
--
-- It was the wrong place for the answer. The role a new employee gets is now chosen on the
-- employee create screen, where the person filling the form knows what that employee does. A
-- stored default guesses instead, and gets applied without anybody reading it.
--
-- Nothing was ever set: the column was added, wired up and taken out again before any
-- registration named a role.
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Registration_DefaultRole')
BEGIN
    ALTER TABLE dbo.FW_Registration DROP CONSTRAINT FK_FW_Registration_DefaultRole;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'DefaultRoleID')
BEGIN
    ALTER TABLE dbo.FW_Registration DROP COLUMN DefaultRoleID;
END
GO

-- Any field permission the page picked up for it, or the orphan sweep reports it for good.
DELETE FROM dbo.FW_RoleFields WHERE FileLink = 'FW_Registration.DefaultRoleID';
GO
