-- 162_past_imports_imported_by_caption.sql
--
-- Past Imports: the person's name column read "Imported By Name" (Glenn, 2026-09-25: "drop the
-- name"). The alias becomes ImportedBy, which DisplayNameFormatter shows as "Imported By". The
-- table's own ImportedBy is the user id and is not selected, so the alias shadows nothing.
--
-- Only FW_Pages.Table_SQL changes; otherwise the SQL is 160's.

DECLARE @PageSql varchar(max) =
'SELECT
    b.[ImportBatchID] AS PK,
    b.[ImportBatchID],
    b.[BatchName],
    b.[Note],
    b.[PeopleCount],
    b.[ImportedOn],
    u.[FirstLast] AS ImportedBy,
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
