/*
    136_switch_user_hide_registration.sql

    Takes RegistrationID out of what the Switch User page shows, and puts it back where it belongs
    - in the WHERE clause.

    It was in the select list only to satisfy a mechanism: the framework decides whether a page is
    registration-scoped by reading its SQL for the word, so removing it entirely would have meant
    the dropdown filtered nothing. Showing it put an internal number in the grid and a field in QBE
    that nobody would ever search on - the dropdown above the grid already says which registration
    is being looked at.

    FW_SwitchUser_B now answers the predicate itself in GetActiveBaseSql: one registration becomes
    that id, and All Registrations becomes the column compared with itself, which is every row.
    That is why the parameter can stay in the text without the page failing when no registration is
    chosen.

    The saved layouts are cleared with it, or the stored arrangement would put the column back over
    the new one.

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
       ModifiedOn = GETDATE()
 WHERE WindowOrPage = 'FW_SwitchUser_B';

PRINT 'Page SQL updated on ' + CAST(@@ROWCOUNT AS varchar(10)) + ' row(s)';

DELETE FROM dbo.FW_TableLayouts
 WHERE PageName = 'FW_SwitchUser_B';

PRINT 'Saved layouts cleared: ' + CAST(@@ROWCOUNT AS varchar(10));

SELECT Table_SQL FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_SwitchUser_B';
