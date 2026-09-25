-- 161_one_clock_defaults.sql
--
-- ONE_CLOCK_SPEC.md phase A, the SQL half: every column default and the one procedure that
-- stamped the server's local clock now stamp UTC.
--
-- DO NOT APPLY ON ITS OWN. Phases A, B and C are released together (spec section 9): applied
-- alone, new rows in these columns are UTC beside old rows that are local, and every screen
-- showing them is four hours out for the new ones. It is applied in the same sitting as the
-- phase B data conversion, after the backup that precedes it.
--
-- FW_Employees.HireDate keeps GETDATE(): a date column, a calendar day, and while BEELINK is on
-- Eastern its date is the East Coast's. CK_Birthdate's BirthDate < GETDATE() is a date
-- comparison and stays too.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @defaults TABLE (TableName sysname, ColumnName sysname);
INSERT INTO @defaults VALUES
    ('FW_ImportBatches',  'CreatedOn'),
    ('FW_LicenseTerms',   'CreatedOn'),
    ('FW_PageZooms',      'CreatedOn'),
    ('FW_SavedImports',   'CreatedOn'),
    ('FW_SavedQBE',       'CreatedOn'),
    ('FW_SavedQBE',       'UpdatedOn'),
    ('FW_TableLayouts',   'CreatedOn'),
    ('FW_UpdateTabOrder', 'CreatedOn'),
    ('FW_Users',          'CreatedOn'),
    ('FW_UserUiHints',    'CreatedOn'),
    ('FW_UserUiHints',    'SeenOn'),
    ('FW_UserUiHints',    'UpdatedOn');

-- Replaced by name found at run time, not by the names in this file: FW_Users.CreatedOn's is a
-- system-generated DF__Users__CreatedOn__2BFE89A6, and another database will have another.
DECLARE @table sysname, @column sysname, @constraint sysname, @sql nvarchar(max);
DECLARE defaults_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, ColumnName FROM @defaults;
OPEN defaults_cursor;
FETCH NEXT FROM defaults_cursor INTO @table, @column;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @constraint = dc.name
      FROM sys.default_constraints dc
      JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
     WHERE dc.parent_object_id = OBJECT_ID('dbo.' + @table) AND c.name = @column
       AND dc.definition LIKE '%getdate%';

    IF @constraint IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE dbo.' + QUOTENAME(@table) + N' DROP CONSTRAINT ' + QUOTENAME(@constraint) + N'; ' +
                   N'ALTER TABLE dbo.' + QUOTENAME(@table) + N' ADD CONSTRAINT ' + QUOTENAME('DF_' + @table + '_' + @column) +
                   N' DEFAULT (sysutcdatetime()) FOR ' + QUOTENAME(@column) + N';';
        EXEC sys.sp_executesql @sql;
    END

    SET @constraint = NULL;
    FETCH NEXT FROM defaults_cursor INTO @table, @column;
END
CLOSE defaults_cursor;
DEALLOCATE defaults_cursor;

-- The procedure is rewritten from its live definition rather than from a copy here, so a
-- procedure that has drifted from sql/schema keeps everything else it does.
DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.usp_FW_ApplyAccessDiagnosticChanges'));
IF @definition IS NOT NULL AND CHARINDEX('GETDATE()', @definition) > 0
BEGIN
    SET @definition = REPLACE(@definition, 'GETDATE()', 'SYSUTCDATETIME()');
    SET @definition = STUFF(@definition, PATINDEX('%CREATE%PROCEDURE%', @definition), 6, 'ALTER');
    EXEC sys.sp_executesql @definition;
END

COMMIT TRANSACTION;

-- Check afterwards - both should return no rows:
--   SELECT t.name, c.name FROM sys.default_constraints dc
--     JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
--     JOIN sys.tables t ON t.object_id = c.object_id
--    WHERE dc.definition LIKE '%getdate%' AND NOT (t.name = 'FW_Employees' AND c.name = 'HireDate');
--   SELECT 1 WHERE OBJECT_DEFINITION(OBJECT_ID('dbo.usp_FW_ApplyAccessDiagnosticChanges')) LIKE '%GETDATE()%';
