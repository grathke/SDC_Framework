-- FW_Employees.TermminateDate -> TerminationDate.
--
-- The column was misspelled when the table was seeded from FW_Users on 2026-09-14. Renamed now
-- rather than left, because the convention only protects names that other work already depends
-- on, and this one is a day old: one permission row, one saved tab-order row and one generation
-- request carry it, against the dozens a settled column would have.
--
-- TerminationDate rather than a bare TerminateDate fix: the rename is happening either way, and
-- "Hire Date / Termination Date" is how the pair reads. DisplayNameFormatter needs no entry.
--
-- The name is spelled out as text in four places that sp_rename does not reach. Found by sweeping
-- every varchar column in the database, not by recalling the list.
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'TermminateDate')
BEGIN
    EXEC sp_rename 'dbo.FW_Employees.TermminateDate', 'TerminationDate', 'COLUMN';
END
GO

-- The field permission, keyed both ways.
UPDATE dbo.FW_RoleFields
   SET FieldName = 'TerminationDate'
 WHERE FieldName = 'TermminateDate';

UPDATE dbo.FW_RoleFields
   SET FileLink = REPLACE(FileLink, 'TermminateDate', 'TerminationDate')
 WHERE FileLink LIKE '%TermminateDate%';

-- The saved tab-order row, keyed on the control name the page builds from the column.
UPDATE dbo.FW_UpdateTabOrder
   SET ControlName = REPLACE(ControlName, 'TermminateDate', 'TerminationDate')
 WHERE ControlName LIKE '%TermminateDate%';

-- The generation request: the field list and the column map.
UPDATE dbo.FW_GeneratedPages
   SET MaintenanceFields = REPLACE(MaintenanceFields, 'TermminateDate', 'TerminationDate')
 WHERE MaintenanceFields LIKE '%TermminateDate%';

UPDATE dbo.FW_GeneratedPages
   SET Column2Fields = REPLACE(Column2Fields, 'TermminateDate', 'TerminationDate')
 WHERE Column2Fields LIKE '%TermminateDate%';
GO

-- GeneratedMaintenanceSource is deliberately left alone. It is the snapshot the manual-change
-- check compares the file on disk against, paired with GeneratedMaintenanceHash; rewriting the
-- text without the hash would report a hand-edited page that nobody edited. The next Generate
-- rewrites both together.
