SET QUOTED_IDENTIFIER ON;
GO

-- The typed password on the employee page, and the uniqueness login depends on.
--
-- FW_Employees.Password never holds a password. The save path takes the typed value out before
-- the row is written, hashes it against the new UserId, and writes '#####' here - so the column
-- is a placeholder the page can bind to, not a store. FW_Users does the same, except that it
-- writes the raw value first and masks it a statement later, which puts a plain-text password on
-- disk for the length of a transaction. This one never does.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'Password')
BEGIN
    ALTER TABLE dbo.FW_Employees ADD [Password] varchar(50) NULL;
END
GO

UPDATE dbo.FW_Employees SET [Password] = '#####' WHERE [Password] IS NULL;
GO

-- Login matches on UserName with SELECT TOP 1. Two rows sharing one name means it picks by row
-- order - the same fault the Email index was added to prevent, on the column that actually
-- authenticates people now.
--
-- Filtered, because UserName is nullable and a plain unique index would allow only one NULL
-- across the whole table. Deleted rows are deliberately included: a soft-deleted account keeps
-- its name, so nobody is handed a name that a restore would collide with.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FW_Users_UserName' AND object_id = OBJECT_ID('dbo.FW_Users'))
BEGIN
    CREATE UNIQUE INDEX UX_FW_Users_UserName
        ON dbo.FW_Users (UserName)
        WHERE UserName IS NOT NULL;
END
GO
