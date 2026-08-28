-- FW_RoleSchema: Auto-discovered list of FW_* and AS_* tables for role-based permissions
-- This table is maintained dynamically and linked to FW_RoleDetails for permission management

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FW_RoleSchema' AND TABLE_SCHEMA = 'dbo')
BEGIN
    CREATE TABLE dbo.FW_RoleSchema
    (
        ID INT PRIMARY KEY IDENTITY(1,1),
        DB_Table NVARCHAR(128),
        Table_Alias NVARCHAR(128),
        IsActive BIT DEFAULT 1,
        CreatedBy INT,
        CreatedOn DATETIME DEFAULT GETDATE(),
        DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_RoleSchema_DeletedFlag DEFAULT (0),
        DeletedBy INT NULL,
        DeletedOn DATETIME2(0) NULL
    )
    
    CREATE INDEX IX_FW_RoleSchema_DB_Table ON dbo.FW_RoleSchema(DB_Table)
    CREATE INDEX IX_FW_RoleSchema_IsActive ON dbo.FW_RoleSchema(IsActive)
END

-- Update FW_RoleDetails to reference FW_RoleSchema
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FW_RoleDetails' AND COLUMN_NAME = 'FW_RoleSchemaID')
BEGIN
    ALTER TABLE dbo.FW_RoleDetails ADD FW_RoleSchemaID INT NULL
    
    ALTER TABLE dbo.FW_RoleDetails ADD CONSTRAINT FK_FW_RoleDetails_FW_RoleSchema 
        FOREIGN KEY (FW_RoleSchemaID) REFERENCES dbo.FW_RoleSchema(ID) ON DELETE NO ACTION
END

-- Ensure RegistrationID exists in FW_RoleDetails
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'FW_RoleDetails' AND COLUMN_NAME = 'RegistrationID')
BEGIN
    ALTER TABLE dbo.FW_RoleDetails ADD RegistrationID INT NOT NULL DEFAULT 1
END

-- Clear existing FW_RoleSchema records
DELETE FROM dbo.FW_RoleSchema

-- Populate FW_RoleSchema with existing FW_* and AS_* tables from schema
INSERT INTO dbo.FW_RoleSchema (DB_Table, Table_Alias, IsActive, CreatedBy, CreatedOn)
SELECT DISTINCT t.TABLE_NAME,
    CASE 
        WHEN t.TABLE_NAME LIKE 'FW_%' THEN SUBSTRING(t.TABLE_NAME, 4, 8000)
        WHEN t.TABLE_NAME LIKE 'AS_%' THEN SUBSTRING(t.TABLE_NAME, 4, 8000)
        ELSE t.TABLE_NAME
    END AS Table_Alias,
    1 AS IsActive,
    0 AS CreatedBy,
    GETDATE() AS CreatedOn
FROM INFORMATION_SCHEMA.TABLES t
WHERE t.TABLE_SCHEMA = 'dbo' 
  AND (t.TABLE_NAME LIKE 'FW_%' OR t.TABLE_NAME LIKE 'AS_%')
ORDER BY t.TABLE_NAME
