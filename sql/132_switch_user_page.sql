/*
    132_switch_user_page.sql

    The FW_Pages row for FW_SwitchUser_B, the Switch User search page.

    Written here rather than left to the page's first open. A standard _B page with no row builds
    itself a fallback row and fallback SQL, and a fallback that guesses wrong locks every button
    but Close. The SQL, the alias and the Hot Fields flag are decisions, so they are stated.

    What the SQL says, and why:

      SwitchUserID AS PK   PK is the row's identity to the browse framework - selection,
                           reselecting after a refresh, the maintenance key - so it is the table's
                           own primary key. UserId is carried as a column of its own, because the
                           login is what a switch needs, and the page reads it from the chosen row.

      RegistrationID       The scope. Base_B appends this predicate itself when the page does not
                           name it, but naming it keeps the page readable on its own.

      No RowVersion, no DeletedFlag, no CreatedBy/UpdatedBy. Nothing edits these rows: the
      snapshot is rewritten by usp_FW_RefreshSwitchUser, and the page is a picker.

    Hot Fields off. The panel lists every field of the selected record, which for a snapshot of a
    view is the same handful of columns the grid already shows.

    Not written here, deliberately:

      FW_RoleSchema   the schema sync picks FW_SwitchUser up by itself, because it is a real table,
                      and derives the alias "Switch User" from the name.
      FW_RoleFields   the schema sweep writes one row per column per role.
      FW_RoleDetails  granted in Roles_U, which owns permissions and writes the before-and-after
                      audit pair when a role changes. A script would skip both.
                      Grant Read and Update only. Leave View Only My Records OFF - the page SQL
                      carries a UserId column, and that capability would scope the page to the
                      administrator's own row, which is the one person a switch cannot use.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @Sql varchar(max) =
'SELECT
    s.[SwitchUserID] AS PK,
    s.[PersonType],
    s.[FirstLast],
    s.[UserName],
    s.[Email],
    s.[CompanyName],
    s.[IsActive],
    s.[UserId],
    s.[RegistrationID]
FROM dbo.[FW_SwitchUser] s
WHERE s.[RegistrationID] = @RegistrationID
ORDER BY s.[LastName] ASC, s.[FirstName] ASC';

DECLARE @CreatedBy int = ISNULL((SELECT TOP 1 UserId FROM dbo.FW_Users WHERE UserName = 'grathke@sdcdev.net'), 2);

IF EXISTS (SELECT 1 FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_SwitchUser_B')
BEGIN
    UPDATE dbo.FW_Pages
       SET DB_Table      = 'FW_SwitchUser',
           Table_Alias   = 'Switch User',
           Table_SQL     = @Sql,
           UseHotFields = 0
     WHERE WindowOrPage = 'FW_SwitchUser_B';

    PRINT 'FW_Pages row updated';
END
ELSE
BEGIN
    INSERT INTO dbo.FW_Pages (WindowOrPage, DB_Table, Table_Alias, Table_SQL, UseHotFields, CreatedBy, CreatedOn)
    VALUES ('FW_SwitchUser_B', 'FW_SwitchUser', 'Switch User', @Sql, 0, @CreatedBy, GETDATE());

    PRINT 'FW_Pages row inserted';
END

SELECT PageID, WindowOrPage, DB_Table, Table_Alias, UseHotFields, CreatedBy,
       CONVERT(varchar(19), CreatedOn, 120) AS CreatedOn
  FROM dbo.FW_Pages
 WHERE WindowOrPage = 'FW_SwitchUser_B';

SELECT Table_SQL FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_SwitchUser_B';

SELECT 'FW_RoleSchema' AS Check_,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.FW_RoleSchema WHERE DB_Table = 'FW_SwitchUser')
            THEN 'row exists' ELSE 'not yet - the schema sync will add it' END AS State;
