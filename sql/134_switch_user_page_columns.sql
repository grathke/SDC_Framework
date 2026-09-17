/*
    134_switch_user_page_columns.sql

    What the Switch User page shows, and therefore what QBE offers to search on.

    The page began with FirstLast, the combined name, which is fine to read and poor to search:
    "sawyer" has to be found inside it, and a first name and a last name cannot be asked for
    separately. First Name and Last Name are on the snapshot already, so the page takes those and
    drops the combined one - three name fields in the search list would be noise.

    What is searchable now, and why each earns its place:

      Last Name, First Name   the reason this page exists. The dialog it replaced could not match
                              a name at all.
      User Name               what somebody knows when they say "I sign in as jsmith".
      Email                   what a support request usually arrives from.
      Person Type             Employee, later Contractor. Worth nothing with one type and useful
                              the moment there are two.
      Company Name            empty until contractors exist, then the field that separates two
                              people with the same name.
      Active                  "only active people" is a real search when there are thousands.

    UserId stays in the select list: the page needs the login behind the row, and it is kept
    deliberately rather than resolved a second way.

    A _B page reads this SQL at run time, so this changes what the page shows on its next open.
    No rebuild.

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
WHERE s.[RegistrationID] = @RegistrationID
ORDER BY s.[LastName] ASC, s.[FirstName] ASC';

UPDATE dbo.FW_Pages
   SET Table_SQL  = @Sql,
       ModifiedBy = 2,
       ModifiedOn = GETDATE()
 WHERE WindowOrPage = 'FW_SwitchUser_B';

PRINT 'Page SQL updated on ' + CAST(@@ROWCOUNT AS varchar(10)) + ' row(s)';

SELECT Table_SQL FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_SwitchUser_B';

/*  The saved column layout, if one was kept while the page still showed FirstLast, would put the
    old arrangement back over the new columns. Removed for every user, so the page lays itself out
    from the SQL on the next open. */
DELETE FROM dbo.FW_TableLayouts
 WHERE PageName = 'FW_SwitchUser_B';

PRINT 'Saved layouts cleared: ' + CAST(@@ROWCOUNT AS varchar(10));
