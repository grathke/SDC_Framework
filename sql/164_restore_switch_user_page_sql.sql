/*
    164_restore_switch_user_page_sql.sql

    Puts the Switch User page's own SQL back on its FW_Pages row.

    Found 2026-09-25: switching user showed MISSING PK. Row 55 (FW_SwitchUser_B) held the
    Employees browse SQL - the version migration 154 writes - nested inside itself, the outer
    query's @RegistrationID replaced by a second whole copy. The page swaps @RegistrationID for
    s.[RegistrationID], the employee SQL has no alias s, the query failed, no PK came back.

    It went wrong between 2026-09-17 20:39 (the page's * Default layout was saved then with the
    Switch User columns) and 2026-09-23 (the schema snapshot already held the bad value).
    ModifiedOn was not moved and no audit row names it, so what wrote it is not known. Neither
    migration 154 nor the generator can have done it as written - 154 is keyed to
    FW_Employees_B, and no generation request is named FW_SwitchUser_B.

    The SQL is migration 136's. 136 is not simply re-run because it also deletes the page's saved
    layouts, and the * Default layout is still correct.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @Sql varchar(max) =
'SELECT
    s.[SwitchUserID] AS PK,
    s.[LastName],
    s.[FirstName],
    s.[UserName],
    s.[Email],
    s.[PersonType],
    s.[CompanyName],
    s.[IsActive],
    s.[UserId]
FROM dbo.[FW_SwitchUser] s
WHERE s.[RegistrationID] = @RegistrationID
ORDER BY s.[LastName] ASC, s.[FirstName] ASC';

UPDATE dbo.FW_Pages
   SET Table_SQL  = @Sql,
       ModifiedBy = 2,
       ModifiedOn = SYSUTCDATETIME()
 WHERE WindowOrPage = 'FW_SwitchUser_B'
   AND Table_SQL <> @Sql;

PRINT 'Switch User page SQL restored on ' + CAST(@@ROWCOUNT AS varchar(10)) + ' row(s)';

SELECT PageID,
       CASE WHEN CHARINDEX('s.[SwitchUserID] AS PK', Table_SQL) > 0 THEN 'yes' ELSE '*** NO ***' END AS SwitchUserKey,
       CASE WHEN CHARINDEX('FW_Employees', Table_SQL) = 0 THEN 'yes' ELSE '*** NO ***' END AS NoEmployeeSql
  FROM dbo.FW_Pages
 WHERE WindowOrPage = 'FW_SwitchUser_B';
