/*
    109_employees_browse_registration_filter.sql

    Scopes FW_Employees_B to a registration.

    Its SQL never mentioned RegistrationID, and the runtime only applies the registration the
    combo selects when the statement refers to that column - so the page listed every
    registration's employees to everyone, and the combo appeared to do nothing.

    The predicate is alias-qualified because FW_Employees is joined to itself for the manager
    name; an unqualified RegistrationID would be ambiguous.

    Two changes, and both are needed: the FW_Pages row is what the page reads, and the
    FW_GeneratedPages flag is what keeps it there the next time the page is saved or generated.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

UPDATE dbo.FW_GeneratedPages
   SET UseRegistrationID = 1
 WHERE BrowsePageName = 'FW_Employees_B'
   AND ISNULL(CAST(UseRegistrationID AS INT), 0) = 0;

PRINT 'UseRegistrationID set on ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' request(s)';

UPDATE dbo.FW_Pages
   SET Table_SQL = REPLACE(Table_SQL,
                           'LEFT JOIN dbo.[FW_Employees] E2 ON E.[AssignedManagerID] = E2.[EmployeeID]',
                           'LEFT JOIN dbo.[FW_Employees] E2 ON E.[AssignedManagerID] = E2.[EmployeeID]' + CHAR(13) + CHAR(10) +
                           'WHERE E.[RegistrationID] = @RegistrationID')
 WHERE WindowOrPage = 'FW_Employees_B'
   AND Table_SQL NOT LIKE '%RegistrationID%';

PRINT 'Table_SQL updated on ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' page row(s)';

SELECT Table_SQL FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_Employees_B';
