USE WX_Framework;
GO

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
FROM sys.tables AS t
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
    IF COL_LENGTH(N''dbo.' + REPLACE(t.name, '''', '''''') + N''', ''RowVersion'') IS NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.columns AS c
           WHERE c.object_id = OBJECT_ID(N''dbo.' + REPLACE(t.name, '''', '''''') + N''', ''U'')
             AND c.system_type_id = 189
       )
    BEGIN
        ALTER TABLE dbo.' + QUOTENAME(t.name) + N' ADD RowVersion rowversion NOT NULL;
        PRINT N''Added RowVersion to dbo.' + REPLACE(t.name, '''', '''''') + N''';
    END
    ELSE IF COL_LENGTH(N''dbo.' + REPLACE(t.name, '''', '''''') + N''', ''RowVersion'') IS NULL
    BEGIN
        PRINT N''Skipped dbo.' + REPLACE(t.name, '''', '''''') + N' because it already has a rowversion column under another name.'';
    END
END;
'
FROM sys.tables AS t
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name LIKE N'FW[_]%';

EXEC sys.sp_executesql @sql;

SELECT
    t.name AS TableName,
    rv.name AS RowVersionColumn,
    ty.name AS SqlType,
    CASE WHEN rv.name = N'RowVersion' THEN 0 ELSE 1 END AS UsesAlternateRowVersionName
FROM sys.tables AS t
OUTER APPLY
(
    SELECT TOP (1) c.name
    FROM sys.columns AS c
    WHERE c.object_id = t.object_id
      AND c.system_type_id = 189
) AS rv
LEFT JOIN sys.types AS ty
    ON ty.system_type_id = 189
   AND ty.user_type_id = 189
WHERE t.schema_id = SCHEMA_ID(N'dbo')
  AND t.name LIKE N'FW[_]%'
ORDER BY t.name;
