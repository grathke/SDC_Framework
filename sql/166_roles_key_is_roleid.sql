/*
    166_roles_key_is_roleid.sql

    FW_Roles' primary key is RoleID, not ID.

    The naming rule (CLAUDE.md, Naming Conventions) is <Stem>ID, because a bare ID is the one
    name that cannot survive a join. Every column that points at a role was already called
    RoleID - FW_EmployeeRoles, FW_RoleDetails, FW_RoleFields, the session - so the joins read
    r.ID = er.RoleID, the one place the rule broke. Now they read r.RoleID = er.RoleID.

    WHAT MOVES WITH IT, all in this one transaction:
      - the column, by sp_rename; the foreign key from FW_EmployeeRoles follows it by itself
      - vw_FW_EmployeeRoles - a view, changed with Glenn's approval on 2026-09-25 for this
        rename. Its output columns are unchanged; only its join is.
      - usp_FW_AccessDiagnostic, the same one-line join
      - the Roles_B row in FW_Pages, whose SQL selected dbo.FW_Roles.ID AS PK

    The application must change with it: DataAccess, Roles_B, Roles_U, the employee import, the
    roles selector and HealthMail all name the column in SQL strings, which no compiler checks.
    An older build against this database fails at login.

    TO REVERSE, in one transaction:
      EXEC sp_rename 'dbo.FW_Roles.RoleID', 'ID', 'COLUMN';
      and put back "r.ID = er.RoleID" / "r.ID = ur.RoleID" in the view and the procedure, and
      "dbo.FW_Roles.ID AS PK" in FW_Pages - the reverse of each REPLACE below.

    Not re-runnable as a whole, by design: the rename is guarded, and each REPLACE finds nothing
    the second time.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.FW_Roles', 'ID') IS NOT NULL AND COL_LENGTH('dbo.FW_Roles', 'RoleID') IS NULL
BEGIN
    EXEC sp_rename 'dbo.FW_Roles.ID', 'RoleID', 'COLUMN';
    PRINT 'FW_Roles.ID renamed to RoleID';
END
ELSE
    PRINT 'FW_Roles already has RoleID - rename skipped';

DECLARE @def nvarchar(max);

-- The view and the procedure are rebuilt from their own current text, so nothing else in them
-- can drift from what is deployed. Each must contain the old join exactly once, or it stops.
SET @def = OBJECT_DEFINITION(OBJECT_ID('dbo.vw_FW_EmployeeRoles'));
IF CHARINDEX('ON r.ID = er.RoleID', @def) > 0
BEGIN
    SET @def = REPLACE(@def, 'ON r.ID = er.RoleID', 'ON r.RoleID = er.RoleID');
    SET @def = STUFF(@def, CHARINDEX('CREATE VIEW', @def), LEN('CREATE VIEW'), 'ALTER VIEW');
    EXEC (@def);
    PRINT 'vw_FW_EmployeeRoles joins on RoleID';
END

SET @def = OBJECT_DEFINITION(OBJECT_ID('dbo.usp_FW_AccessDiagnostic'));
IF CHARINDEX('ON r.ID = ur.RoleID', @def) > 0
BEGIN
    SET @def = REPLACE(@def, 'ON r.ID = ur.RoleID', 'ON r.RoleID = ur.RoleID');
    SET @def = STUFF(@def, CHARINDEX('CREATE', @def), LEN('CREATE'), 'ALTER');
    EXEC (@def);
    PRINT 'usp_FW_AccessDiagnostic joins on RoleID';
END

UPDATE dbo.FW_Pages
   SET Table_SQL  = REPLACE(Table_SQL, 'dbo.FW_Roles.ID AS PK', 'dbo.FW_Roles.RoleID AS PK'),
       ModifiedBy = 2,
       ModifiedOn = SYSUTCDATETIME()
 WHERE WindowOrPage = 'Roles_B'
   AND CHARINDEX('dbo.FW_Roles.ID AS PK', Table_SQL) > 0;

PRINT 'Roles_B page SQL updated: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRANSACTION;
GO

-- Proof. Every line should say yes, and the last query should return no rows: it is the sweep
-- CLAUDE.md asks for after a rename - any view or procedure left pointing at a name that no
-- longer resolves.
SELECT 'column renamed:        ' + CASE WHEN COL_LENGTH('dbo.FW_Roles', 'RoleID') IS NOT NULL AND COL_LENGTH('dbo.FW_Roles', 'ID') IS NULL THEN 'yes' ELSE '*** NO ***' END AS Check_
UNION ALL SELECT 'view joins on RoleID:  ' + CASE WHEN CHARINDEX('r.RoleID = er.RoleID', OBJECT_DEFINITION(OBJECT_ID('dbo.vw_FW_EmployeeRoles'))) > 0 THEN 'yes' ELSE '*** NO ***' END
UNION ALL SELECT 'proc joins on RoleID:  ' + CASE WHEN CHARINDEX('r.RoleID = ur.RoleID', OBJECT_DEFINITION(OBJECT_ID('dbo.usp_FW_AccessDiagnostic'))) > 0 THEN 'yes' ELSE '*** NO ***' END
UNION ALL SELECT 'Roles_B page SQL:      ' + CASE WHEN CHARINDEX('dbo.FW_Roles.RoleID AS PK', Table_SQL) > 0 THEN 'yes' ELSE '*** NO ***' END FROM dbo.FW_Pages WHERE WindowOrPage = 'Roles_B'
UNION ALL SELECT 'foreign key intact:    ' + CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc JOIN sys.columns c ON c.object_id = fkc.referenced_object_id AND c.column_id = fkc.referenced_column_id WHERE fkc.referenced_object_id = OBJECT_ID('dbo.FW_Roles') AND c.name = 'RoleID') THEN 'yes' ELSE '*** NO ***' END;

-- Every view and procedure that names FW_Roles, recompiled against the schema as it now is. A
-- reference to the old column fails here rather than on the first screen that runs it. (The
-- first draft asked sys.sql_expression_dependencies for referenced_minor_name, which that view
-- does not have - and a renamed column leaves no minor id to find anyway.)
DECLARE @name sysname, @failures int = 0;
DECLARE modules CURSOR LOCAL FAST_FORWARD FOR
    SELECT QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name)
      FROM sys.objects o
     WHERE o.type IN ('V', 'P', 'FN', 'IF', 'TF')
       AND OBJECT_DEFINITION(o.object_id) LIKE '%FW_Roles%';
OPEN modules;
FETCH NEXT FROM modules INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC sys.sp_refreshsqlmodule @name;
    END TRY
    BEGIN CATCH
        SET @failures += 1;
        PRINT '*** ' + @name + ' does not compile: ' + ERROR_MESSAGE();
    END CATCH
    FETCH NEXT FROM modules INTO @name;
END
CLOSE modules;
DEALLOCATE modules;
PRINT 'Modules naming FW_Roles that fail to compile: ' + CAST(@failures AS varchar(10));

SELECT o.name AS NeedsRefresh
  FROM sys.objects o
 WHERE o.type IN ('V', 'P', 'FN', 'IF', 'TF')
   AND OBJECT_DEFINITION(o.object_id) LIKE '%FW_Roles%'
   AND (OBJECT_DEFINITION(o.object_id) LIKE '%r.ID %' OR OBJECT_DEFINITION(o.object_id) LIKE '%FW_Roles.ID%');
GO
