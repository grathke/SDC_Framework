-- 160_past_imports_undone_on_column.sql
--
-- Past Imports shows its times in the viewer's time zone (Glenn, 2026-09-25: a 19:04 import
-- showed as 11:04 PM). ImportedOn and UndoneOn are stored UTC; FW_ImportBatches_B names them
-- through FW_Base_B.UtcColumns and the page converts them after the fetch.
--
-- The undo time was part of the Status text - 'Undone 2026-09-25 13:40' - built in SQL, where
-- nothing can convert it. Status now says Imported or Undone, and UndoneOn is a column of its own.
--
-- Only FW_Pages.Table_SQL changes. Nothing else in 159 is repeated.

DECLARE @PageSql varchar(max) =
'SELECT
    b.[ImportBatchID] AS PK,
    b.[ImportBatchID],
    b.[BatchName],
    b.[Note],
    b.[PeopleCount],
    b.[ImportedOn],
    u.[FirstLast] AS ImportedByName,
    CASE WHEN b.[UndoneOn] IS NULL THEN ''Imported'' ELSE ''Undone'' END AS Status,
    b.[UndoneOn],
    b.[FileName]
FROM dbo.[FW_ImportBatches] b
LEFT JOIN dbo.[FW_Users] u ON u.[UserId] = b.[ImportedBy]
WHERE b.[RegistrationID] = @RegistrationID
ORDER BY b.[ImportedOn] DESC';

UPDATE dbo.FW_Pages
   SET Table_SQL = @PageSql
 WHERE WindowOrPage = 'FW_ImportBatches_B';
GO
