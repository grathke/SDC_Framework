-- 154_employees_browse_order_by_lastfirst.sql
--
-- FW_Employees_B lists people surname-first, and in surname order.
--
-- TWO CHANGES, ONE INTENT. The select list showed FirstLast ("Glenn Rathke") and the page was
-- ordered by EmployeeID, which is insertion order and means nothing to anybody looking for a
-- person. It now shows LastFirst ("Rathke, Glenn") and orders by it, so the grid reads the way a
-- staff list reads and scrolling to a surname works.
--
-- THE TIE-BREAKER IS NOT DECORATION, AND THIS IS THE PART WORTH READING. LastFirst is not unique -
-- two employees can share a name, and the test data has many such pairs. Until 2026-09-21 that was
-- survivable: the framework fetched every row and took the first N in memory, so an ambiguous order
-- was at least stable within one fetch.
--
-- It is no longer survivable, because the row cap is now a TOP inside the query (see
-- QBE_SQL_PUSHDOWN_SPEC.md section 12). With ORDER BY LastFirst alone, rows 11 and 12 were both
-- "Adams, Brian" - EmployeeID 8104 and 2104 - and which of them appeared on the first page was
-- undefined. SQL Server is free to return either, and may answer differently between runs or
-- between plans. A cap over a non-unique order is a page that quietly shows a different person
-- each time.
--
-- EmployeeID is the primary key, so appending it makes the order total. It costs nothing: the
-- server is already sorting, and a second sort key on an integer it has in hand is free.
--
-- NO INDEX. LastFirst is a persisted computed column and therefore indexable, and an index on
-- (RegistrationID, LastFirst, EmployeeID) was built, measured and dropped on 2026-09-21. The
-- optimizer did not use it - 491 logical reads and 16ms of CPU with and without, and the plan kept
-- its Clustered Index Scan and Sort even under OPTION (RECOMPILE). The reason is the select list:
-- Address1, City, State, Zip, UserName, HireDate and CreatedOn are not in the index, so streaming
-- from it would need a key lookup per row and the plain scan-and-sort costed cheaper.
--
-- A covering index - the same keys with those nine columns INCLUDEd - would remove the sort. It
-- would also duplicate most of the table's width and be maintained on every write. At 10,007 rows
-- sorting in 13ms that trade is not worth making. Revisit it only when the sort shows up in the
-- fetch figure, and measure before and after rather than assuming.
--
-- THIS ROW IS DATA, NOT CODE. FW_Pages.Table_SQL is read at every browse refresh, so the change
-- takes effect on the next open with no rebuild. The same statement runs against any installation
-- that has an FW_Employees_B row; one that does not is left alone rather than given a row here,
-- because a page's SQL is written by hand or by the generator and a migration inventing one would
-- be claiming an answer nobody gave.

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_Employees_B')
BEGIN
    PRINT 'No FW_Employees_B row in FW_Pages. Nothing to change.';
END
ELSE
BEGIN
    UPDATE dbo.FW_Pages
    SET Table_SQL =
        'SELECT' + CHAR(13) + CHAR(10) +
        '    E.[EmployeeID] AS PK,' + CHAR(13) + CHAR(10) +
        '    E.[LastFirst],' + CHAR(13) + CHAR(10) +
        '    E.[Address1],' + CHAR(13) + CHAR(10) +
        '    E.[City],' + CHAR(13) + CHAR(10) +
        '    E.[State],' + CHAR(13) + CHAR(10) +
        '    E.[Zip],' + CHAR(13) + CHAR(10) +
        '    G.[GenderDescription] AS [GenderID],' + CHAR(13) + CHAR(10) +
        '    E2.[FirstLast] AS [AssignedManagerID],' + CHAR(13) + CHAR(10) +
        '    E.[UserName],' + CHAR(13) + CHAR(10) +
        '    E.[HireDate],' + CHAR(13) + CHAR(10) +
        '    E.[CreatedOn]' + CHAR(13) + CHAR(10) +
        'FROM dbo.[FW_Employees] E' + CHAR(13) + CHAR(10) +
        'LEFT JOIN dbo.[FW_Gender] G ON E.[GenderID] = G.[GenderID]' + CHAR(13) + CHAR(10) +
        'LEFT JOIN dbo.[FW_Employees] E2 ON E.[AssignedManagerID] = E2.[EmployeeID]' + CHAR(13) + CHAR(10) +
        'WHERE E.[RegistrationID] = @RegistrationID' + CHAR(13) + CHAR(10) +
        'ORDER BY E.[LastFirst] ASC, E.[EmployeeID] ASC'
    WHERE WindowOrPage = 'FW_Employees_B';

    PRINT 'FW_Employees_B SQL updated. Rows: ' + CAST(@@ROWCOUNT AS varchar(10));
END
GO

-- Proof, rather than a claim. The key is still aliased AS PK so Modify, Read and Delete can act on
-- a row; the order names LastFirst and EmployeeID; and the registration predicate is still explicit,
-- which is what keeps the framework from filtering the tenant in memory afterwards.
DECLARE @sql nvarchar(max) = (SELECT Table_SQL FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_Employees_B');

-- CHARINDEX, not LIKE. Every pattern here contains a square bracket, and in a LIKE pattern a
-- bracket opens a character class - so '%E.[LastFirst],%' asks for one character out of that set
-- and matches nothing. The first version of this check reported four failures against SQL that was
-- perfectly correct, which is the more dangerous way for a check to be wrong.
SELECT 'AS PK present:            ' + CASE WHEN CHARINDEX('AS PK', @sql) > 0 THEN 'yes' ELSE '*** NO ***' END AS Check_
UNION ALL SELECT 'LastFirst selected:       ' + CASE WHEN CHARINDEX('E.[LastFirst],', @sql) > 0 THEN 'yes' ELSE '*** NO ***' END
UNION ALL SELECT 'ordered by LastFirst:     ' + CASE WHEN CHARINDEX('ORDER BY E.[LastFirst] ASC', @sql) > 0 THEN 'yes' ELSE '*** NO ***' END
UNION ALL SELECT 'tie-breaker on the key:   ' + CASE WHEN CHARINDEX(', E.[EmployeeID] ASC', @sql) > 0 THEN 'yes' ELSE '*** NO ***' END
UNION ALL SELECT 'registration predicate:   ' + CASE WHEN CHARINDEX('WHERE E.[RegistrationID] = @RegistrationID', @sql) > 0 THEN 'yes' ELSE '*** NO ***' END
UNION ALL SELECT 'FirstLast gone from list: ' + CASE WHEN CHARINDEX('E.[FirstLast],', @sql) = 0 THEN 'yes' ELSE '*** NO ***' END
UNION ALL SELECT 'single statement:         ' + CASE WHEN LEN(@sql) - LEN(REPLACE(UPPER(@sql), 'ORDER BY', '')) = 8 THEN 'yes' ELSE '*** NO - more than one ORDER BY ***' END;
GO
