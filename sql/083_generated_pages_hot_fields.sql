/*
    083_generated_pages_hot_fields.sql

    FW_GeneratedPages.HotFields - which fields the Hot Fields panel shows, chosen on the generation
    request beside the browse fields it sits next to.

    A delimited field list, the same shape as BrowseFields and MaintenanceFields, and stored the same
    way: the request holds it, opening a request re-ticks the grid from it, and generating writes it
    on to the page. Nothing here is derived from Table_SQL, because Hot Fields does not read the
    page's SQL - the panel re-reads the record from the table.

    Empty means every field, which is what the panel does today. That is what keeps this additive:
    Roles_B, FW_AuditTrail_B, FW_HD_Admin_B and the Help Desk pages have no generation request at
    all and must not change behaviour.

    See PAGE_FIELD_PICKER_SPEC.md. FW_Pages gains its own HotFields column when the runtime half is
    built; this is only the request's copy, which is where a tick starts life.

    SAFE TO RE-RUN.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_GeneratedPages')
                 AND name = 'HotFields')
BEGIN
    ALTER TABLE dbo.FW_GeneratedPages ADD HotFields VARCHAR(MAX) NULL;
    PRINT 'FW_GeneratedPages.HotFields added';
END
ELSE
    PRINT 'FW_GeneratedPages.HotFields already present';

SELECT name, TYPE_NAME(user_type_id) AS Type, is_nullable
FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.FW_GeneratedPages')
  AND name IN ('BrowseFields', 'MaintenanceFields', 'HotFields');
