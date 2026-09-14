-- Everything that still names FW_Employees.FullName, now that the column is gone.
--
-- FirstLast is the like-for-like replacement: FullName held "First Last", and the computed column
-- produces the same string from the fields it is derived from. Swapping to LastFirst anywhere -
-- the Assigned Manager picker is the obvious candidate, since a dropdown of people reads better
-- sorted by surname - is a choice to make in the field picker, not a repair to make here.
--
-- Table_SQL is what a _B page reads at runtime, so this one takes effect without a rebuild.
UPDATE dbo.FW_Pages
   SET Table_SQL = REPLACE(Table_SQL, 'FullName', 'FirstLast')
 WHERE Table_SQL LIKE '%FullName%';

-- The generation request: the browse column list, the SQL and the lookup's display column.
UPDATE dbo.FW_GeneratedPages
   SET BrowseFields = REPLACE(BrowseFields, 'FullName', 'FirstLast'),
       BrowseSql    = REPLACE(BrowseSql, 'FullName', 'FirstLast'),
       LookupFields = REPLACE(LookupFields, 'FullName', 'FirstLast')
 WHERE ISNULL(BrowseFields, '') + ISNULL(BrowseSql, '') + ISNULL(LookupFields, '') LIKE '%FullName%';

-- The field permission, keyed both ways.
UPDATE dbo.FW_RoleFields
   SET FieldName = 'FirstLast'
 WHERE FieldName = 'FullName';

UPDATE dbo.FW_RoleFields
   SET FileLink = REPLACE(FileLink, 'FullName', 'FirstLast')
 WHERE FileLink LIKE '%FullName%';

-- Saved grid layouts name their columns. A layout naming a column that no longer exists does not
-- fail, it silently drops the column from the grid - which reads as the page having lost a field.
UPDATE dbo.FW_TableLayouts
   SET JsonState = REPLACE(JsonState, 'FullName', 'FirstLast')
 WHERE JsonState LIKE '%FullName%';
GO
