/*
    135_switch_user_all_registrations.sql

    Takes the registration predicate out of the Switch User page's SQL, so the selector decides it.

    The page had `WHERE s.RegistrationID = @RegistrationID` written into it, which scoped it to one
    company however the selector was set - and an App Admin looking into somebody's problem knows
    the person, not which company they are in. The dialog this page replaced searched every
    registration, so a page fixed to one narrowed what an administrator could already do.

    The RegistrationID column stays in the select list. That is what the framework looks for: with
    a registration chosen it appends the predicate itself, and with All Registrations chosen it
    appends nothing. One column, both behaviours, no page-specific filtering code.

    Who can pick All Registrations, and it is three answers all of which must be yes:
      - the page says so       FW_SwitchUser_B overrides AllowAllRegistrations
      - the session is App Admin
      - the role has View All Records on FW_SwitchUser, which is what shows the selector at all

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
    s.[UserId],
    s.[RegistrationID]
FROM dbo.[FW_SwitchUser] s
ORDER BY s.[LastName] ASC, s.[FirstName] ASC';

UPDATE dbo.FW_Pages
   SET Table_SQL  = @Sql,
       ModifiedBy = 2,
       ModifiedOn = GETDATE()
 WHERE WindowOrPage = 'FW_SwitchUser_B';

PRINT 'Page SQL updated on ' + CAST(@@ROWCOUNT AS varchar(10)) + ' row(s)';

SELECT Table_SQL FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_SwitchUser_B';

SELECT 'Grant View All Records on FW_SwitchUser to see the selector' AS Reminder,
       RoleID, Can_Read, Can_UseQBE, Can_ViewAllRecords
  FROM dbo.FW_RoleDetails
 WHERE DB_Table = 'FW_SwitchUser';
