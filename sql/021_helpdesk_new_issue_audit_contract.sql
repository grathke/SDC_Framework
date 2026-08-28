USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @defaultConstraintName sysname;
DECLARE @dropDefaultSql nvarchar(400);

SELECT @defaultConstraintName = dc.name
FROM sys.default_constraints AS dc
INNER JOIN sys.columns AS c
    ON c.object_id = dc.parent_object_id
    AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID(N'dbo.FW_HD_Issues')
  AND c.name = N'UpdatedOn';

IF @defaultConstraintName IS NOT NULL
BEGIN
  SET @dropDefaultSql = N'ALTER TABLE dbo.FW_HD_Issues DROP CONSTRAINT ' + QUOTENAME(@defaultConstraintName) + N';';
  EXEC(@dropDefaultSql);
END;

ALTER TABLE dbo.FW_HD_Issues
ALTER COLUMN UpdatedOn DATETIME2(0) NULL;
GO