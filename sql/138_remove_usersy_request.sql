/*
    138_remove_usersy_request.sql

    Removes the UsersY generation request.

    UsersY_B and UsersY_U were a test pair, generated to exercise the generator and removed from the
    codebase by sql/078 along with the FW_Users pages. The request outlived them: reopening it would
    offer to generate a page nobody wants, against a table that describes nothing.

    Physical delete rather than a soft one. FW_GeneratedPages is a request, not a record of work -
    what was generated is in the files and in FW_Pages - and a deleted request that still appears in
    the list is worse than no request at all.

    The pages themselves are already gone, so nothing else points at this row.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

SELECT 'BEFORE' AS Stage, GeneratedPageID, RequestName, BrowsePageName, MaintenancePageName, Owner
  FROM dbo.FW_GeneratedPages
 ORDER BY GeneratedPageID;

DELETE FROM dbo.FW_GeneratedPages
 WHERE BrowsePageName IN ('UsersY_B', 'UserX_B')
    OR MaintenancePageName IN ('UsersY_U', 'UserX_U')
    OR RequestName IN ('UsersY', 'UserX');

PRINT 'Requests removed: ' + CAST(@@ROWCOUNT AS varchar(10));

SELECT 'AFTER' AS Stage, GeneratedPageID, RequestName, BrowsePageName, MaintenancePageName, Owner
  FROM dbo.FW_GeneratedPages
 ORDER BY GeneratedPageID;

/*  Nothing else should name them. Each of these should return no rows. */
SELECT 'FW_Pages' AS Table_, WindowOrPage AS Value_ FROM dbo.FW_Pages
 WHERE WindowOrPage LIKE 'UsersY%' OR WindowOrPage LIKE 'UserX%'
UNION ALL
SELECT 'FW_RoleDetails', DB_Table FROM dbo.FW_RoleDetails
 WHERE DB_Table LIKE 'UsersY%' OR DB_Table LIKE 'UserX%'
UNION ALL
SELECT 'FW_RoleFields', TableName FROM dbo.FW_RoleFields
 WHERE TableName LIKE 'UsersY%' OR TableName LIKE 'UserX%'
UNION ALL
SELECT 'FW_RoleSchema', DB_Table FROM dbo.FW_RoleSchema
 WHERE DB_Table LIKE 'UsersY%' OR DB_Table LIKE 'UserX%'
UNION ALL
SELECT 'FW_TableLayouts', PageName FROM dbo.FW_TableLayouts
 WHERE PageName LIKE 'UsersY%' OR PageName LIKE 'UserX%'
UNION ALL
SELECT 'FW_DashboardLayouts', ActionKey FROM dbo.FW_DashboardLayouts
 WHERE ActionKey LIKE '%UsersY%' OR ActionKey LIKE '%UserX%'
UNION ALL
SELECT 'FW_SavedQBE', TableContext FROM dbo.FW_SavedQBE
 WHERE TableContext LIKE '%UsersY%' OR TableContext LIKE '%UserX%';
