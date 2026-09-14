-- Two-column _U pages: which fields sit in the second column.
--
-- Column 1 is absence. A field is in column 2 only if it is named here, so every request written
-- before this column existed reads as a one-column page and generates exactly what it generated
-- before - no backfill, and no page-level "how many columns" setting that could disagree with the
-- fields themselves.
--
-- Order is not stored here. MaintenanceFields already carries the order the user arranged with
-- Move Up and Move Down, and the generated page is that one list read twice: the fields not named
-- here, in order, down the left; the fields named here, in the same order, down the right.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_GeneratedPages') AND name = 'Column2Fields')
BEGIN
    ALTER TABLE dbo.FW_GeneratedPages ADD Column2Fields VARCHAR(MAX) NULL;
END
GO
