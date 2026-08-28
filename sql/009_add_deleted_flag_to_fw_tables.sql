SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT N'Current database: ' + DB_NAME();

IF SCHEMA_ID(N'dbo') IS NULL
BEGIN
  RAISERROR('Schema dbo was not found in the current database.', 16, 1);
  RETURN;
END;

DECLARE @targetCount INT;
SELECT @targetCount = COUNT(1)
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name LIKE N'FW[_]%';

PRINT N'FW_ tables found in dbo: ' + CAST(ISNULL(@targetCount, 0) AS NVARCHAR(20));

IF ISNULL(@targetCount, 0) = 0
BEGIN
  RAISERROR('No dbo.FW_ tables found. Verify you are connected to the expected database.', 16, 1);
  RETURN;
END;

DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql += N'
IF OBJECT_ID(N''dbo.' + REPLACE(t.name, '''', '''''') + N''', ''U'') IS NOT NULL
BEGIN
    IF COL_LENGTH(N''dbo.' + REPLACE(t.name, '''', '''''') + N''', ''DeletedFlag'') IS NULL
    BEGIN
    ALTER TABLE dbo.' + QUOTENAME(t.name) + N' ADD DeletedFlag BIT NOT NULL CONSTRAINT ' + QUOTENAME('DF_' + t.name + '_DeletedFlag') + N' DEFAULT (0);
    END;

    IF COL_LENGTH(N''dbo.' + REPLACE(t.name, '''', '''''') + N''', ''DeletedBy'') IS NULL
    BEGIN
    ALTER TABLE dbo.' + QUOTENAME(t.name) + N' ADD DeletedBy INT NULL;
    END;

    IF COL_LENGTH(N''dbo.' + REPLACE(t.name, '''', '''''') + N''', ''DeletedOn'') IS NULL
    BEGIN
    ALTER TABLE dbo.' + QUOTENAME(t.name) + N' ADD DeletedOn DATETIME2(0) NULL;
    END;
END;
'
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name LIKE N'FW[_]%';

EXEC sp_executesql @sql;

SELECT
    t.name AS TableName,
    CASE WHEN COL_LENGTH(N'dbo.' + t.name, 'DeletedFlag') IS NULL THEN 1 ELSE 0 END AS MissingDeletedFlag,
    CASE WHEN COL_LENGTH(N'dbo.' + t.name, 'DeletedBy') IS NULL THEN 1 ELSE 0 END AS MissingDeletedBy,
    CASE WHEN COL_LENGTH(N'dbo.' + t.name, 'DeletedOn') IS NULL THEN 1 ELSE 0 END AS MissingDeletedOn
FROM sys.tables t
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name LIKE N'FW[_]%'
  AND (
      COL_LENGTH(N'dbo.' + t.name, 'DeletedFlag') IS NULL
      OR COL_LENGTH(N'dbo.' + t.name, 'DeletedBy') IS NULL
      OR COL_LENGTH(N'dbo.' + t.name, 'DeletedOn') IS NULL
  )
ORDER BY t.name;