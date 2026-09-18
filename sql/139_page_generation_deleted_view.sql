/*
    FW_PageGeneration_B: let the framework own the deleted filter.

    The page's SQL carried "WHERE ISNULL(DeletedFlag, 0) = 0" written into it, and did not select
    DeletedFlag at all. Show Deleted works by swapping that predicate, so with one hardcoded in the
    page's own SQL the deleted view asked for rows that were both deleted and not deleted, and
    returned nothing - a soft-deleted request could be created and never seen again.

    Found on 2026-09-18 after soft-deleting the User Access Diagnostic request, which is deliberately
    hidden: its page has grown to 878 hand-written lines, and regenerating it would replace them.

    Selecting DeletedFlag is what lets the browse page tell the two views apart; dropping the WHERE
    is what lets it choose between them.
*/

UPDATE dbo.FW_Pages
SET Table_SQL = 'SELECT GeneratedPageID AS PK, RequestName, PageBaseName, BrowsePageName, MaintenancePageName, DeletedFlag FROM FW_GeneratedPages ORDER BY RequestName'
WHERE WindowOrPage = 'FW_PageGeneration_B';

SELECT WindowOrPage, Table_SQL
FROM dbo.FW_Pages
WHERE WindowOrPage = 'FW_PageGeneration_B';
