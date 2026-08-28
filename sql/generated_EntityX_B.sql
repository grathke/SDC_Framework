-- GENERATED PAGE REQUEST ARTIFACT
-- Browse page: EntityX_B
-- Maintenance page: EntityX_U
-- Existing table: dbo.FW_ENTITY
-- Table alias: ENTITY
-- This artifact records the validated browse SQL used by the generated page.

IF NOT EXISTS (SELECT 1 FROM dbo.FW_RoleTables WHERE WindowOrPage = 'EntityX_B')
BEGIN
    INSERT INTO dbo.FW_RoleTables (RegistrationID, WindowOrPage, DB_Table, Table_Alias, Table_SQL, CreatedBy)
    VALUES (NULL, 'EntityX_B', 'FW_ENTITY', 'ENTITY', N'SELECT
    [ID] AS PK,
    [Address1],
    [FirstLast]
FROM dbo.[FW_ENTITY]
WHERE [RegistrationID] = @RegistrationID
ORDER BY [ID] ASC', 0);
END;

SELECT
    [ID] AS PK,
    [Address1],
    [FirstLast]
FROM dbo.[FW_ENTITY]
WHERE [RegistrationID] = @RegistrationID
ORDER BY [ID] ASC
