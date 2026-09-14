-- FW_Registration.RegistrationTypeID gets its foreign key to FW_RegistrationType.
--
-- The key is renamed first. FW_RegistrationType keyed on RegTypeID, while the column pointing at
-- it is RegistrationTypeID - so the relationship could not be read off the names, which is the
-- one thing the convention buys: a foreign key spelled exactly like the key it points at means a
-- column identifies its own target. The page generator relies on it too, to suggest a lookup.
--
-- Cheap to fix: RegTypeID appears in one query in the whole application, and it is aliased there
-- anyway. No view, no procedure, no FW_RoleFields row. The table qualifies for a rename under
-- the convention's own exception, since it is being reworked right now to take this key.
--
-- RegTypeName is left as it is. The convention speaks to keys, and a display column carries no
-- relationship for a name to express.
EXEC sp_rename 'dbo.FW_RegistrationType.RegTypeID', 'RegistrationTypeID', 'COLUMN';
GO

-- WITH CHECK, deliberately: both registrations point at type 1, which exists, so there is
-- nothing to grandfather. A constraint added WITH NOCHECK is trusted by nothing and quietly
-- permits the rows that were already wrong.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Registration_RegistrationType')
BEGIN
    ALTER TABLE dbo.FW_Registration WITH CHECK
        ADD CONSTRAINT FK_FW_Registration_RegistrationType
        FOREIGN KEY (RegistrationTypeID) REFERENCES dbo.FW_RegistrationType (RegistrationTypeID);
END
GO

-- The primary key constraint kept the table's old-style name. Renamed so a key violation names
-- the table it is actually on - PK_RegistrationType reads as a constraint on a table called
-- RegistrationType, which does not exist.
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_RegistrationType')
BEGIN
    EXEC sp_rename 'PK_RegistrationType', 'PK_FW_RegistrationType';
END
GO
