USE WX_Framework;
GO

ALTER TABLE dbo.FW_RoleTables
ALTER COLUMN WIndowOrPage varchar(100) NULL;
GO

ALTER TABLE dbo.FW_RoleTables
ALTER COLUMN DB_Table varchar(100) NULL;
GO

ALTER TABLE dbo.FW_RoleTables
ALTER COLUMN Table_Alias varchar(100) NULL;
GO