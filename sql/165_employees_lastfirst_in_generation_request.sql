/*
    165_employees_lastfirst_in_generation_request.sql

    FW_Employees_B shows LastFirst and orders by it - again, and this time where a Generate
    cannot take it away.

    Migration 154 made this change on 2026-09-21, but only on the FW_Pages row. The generation
    request (FW_GeneratedPages 7) still said FirstLast, ordered by EmployeeID, and generating the
    page on 2026-09-25 wrote that back over the row. A page's SQL has two homes, the request and
    the row, and a change made to one of them lasts only until the other is used.

    The generator will only order by a field the page shows, so LastFirst replaces FirstLast in the
    select list as well as leading the ORDER BY. EmployeeID follows it because LastFirst is not
    unique, and the row cap is a TOP inside the query - see 154 for the pair of Adams, Brian rows
    that showed why.

    HireDate and CreatedOn, which 154 also selected, are left out: the 2026-09-25 generation dropped
    them, and nothing says that was a mistake.

    Re-runnable: each REPLACE finds nothing to change the second time.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

UPDATE dbo.FW_GeneratedPages
   SET BrowseSql    = REPLACE(REPLACE(BrowseSql,
                          '    E.[FirstLast],', '    E.[LastFirst],'),
                          'ORDER BY E.[EmployeeID] ASC', 'ORDER BY E.[LastFirst] ASC, E.[EmployeeID] ASC'),
       BrowseFields = CASE WHEN BrowseFields LIKE 'FirstLast,%' THEN 'LastFirst' + SUBSTRING(BrowseFields, 10, 4000) ELSE BrowseFields END
 WHERE GeneratedPageID = 7
   AND BrowsePageName = 'FW_Employees_B';

PRINT 'Generation request updated: ' + CAST(@@ROWCOUNT AS varchar(10));

UPDATE dbo.FW_Pages
   SET Table_SQL  = REPLACE(REPLACE(Table_SQL,
                        '    E.[FirstLast],', '    E.[LastFirst],'),
                        'ORDER BY E.[EmployeeID] ASC', 'ORDER BY E.[LastFirst] ASC, E.[EmployeeID] ASC'),
       ModifiedBy = 2,
       ModifiedOn = SYSUTCDATETIME()
 WHERE WindowOrPage = 'FW_Employees_B';

PRINT 'Page row updated: ' + CAST(@@ROWCOUNT AS varchar(10));
GO

-- CHARINDEX rather than LIKE: every pattern holds a square bracket, which LIKE reads as a class.
SELECT 'request' AS Source,
       CASE WHEN CHARINDEX('E.[LastFirst],', BrowseSql) > 0 THEN 'yes' ELSE '*** NO ***' END AS LastFirstShown,
       CASE WHEN CHARINDEX('    E.[FirstLast],', BrowseSql) = 0 THEN 'yes' ELSE '*** NO ***' END AS FirstLastGone,
       CASE WHEN CHARINDEX('ORDER BY E.[LastFirst] ASC, E.[EmployeeID] ASC', BrowseSql) > 0 THEN 'yes' ELSE '*** NO ***' END AS Ordered,
       CASE WHEN BrowseFields LIKE 'LastFirst,%' THEN 'yes' ELSE '*** NO ***' END AS Fields
  FROM dbo.FW_GeneratedPages WHERE GeneratedPageID = 7
UNION ALL
SELECT 'page',
       CASE WHEN CHARINDEX('E.[LastFirst],', Table_SQL) > 0 THEN 'yes' ELSE '*** NO ***' END,
       CASE WHEN CHARINDEX('    E.[FirstLast],', Table_SQL) = 0 THEN 'yes' ELSE '*** NO ***' END,
       CASE WHEN CHARINDEX('ORDER BY E.[LastFirst] ASC, E.[EmployeeID] ASC', Table_SQL) > 0 THEN 'yes' ELSE '*** NO ***' END,
       'n/a'
  FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_Employees_B';
GO
