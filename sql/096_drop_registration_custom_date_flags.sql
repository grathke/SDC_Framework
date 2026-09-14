-- FW_Registration.IsCustomDate1Important, 2 and 3 go.
--
-- Three bit flags marking three custom date fields as important - and there are no custom date
-- fields anywhere in the database for them to mark. Nothing reads them: no page, no view, no
-- procedure, no query in the application. Registration 1 has all three set, which means nothing
-- at all, because there is nothing for it to mean.
--
-- The default constraints go first. SQL Server refuses to drop a column another object depends
-- on, and a default is such an object.
DECLARE @drop NVARCHAR(MAX) = N'';
SELECT @drop = @drop + N'ALTER TABLE dbo.FW_Registration DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';' + CHAR(10)
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.FW_Registration')
  AND c.name IN ('IsCustomDate1Important', 'IsCustomDate2Important', 'IsCustomDate3Important');
IF @drop <> N'' EXEC sp_executesql @drop;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'IsCustomDate1Important')
    ALTER TABLE dbo.FW_Registration DROP COLUMN IsCustomDate1Important;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'IsCustomDate2Important')
    ALTER TABLE dbo.FW_Registration DROP COLUMN IsCustomDate2Important;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'IsCustomDate3Important')
    ALTER TABLE dbo.FW_Registration DROP COLUMN IsCustomDate3Important;
GO

-- The field permissions for columns that no longer exist, or they sit in the orphan sweep for
-- good.
DELETE FROM dbo.FW_RoleFields
 WHERE FieldName IN ('IsCustomDate1Important', 'IsCustomDate2Important', 'IsCustomDate3Important');
GO
