/*
    045_page_generation_browse_hash.sql
    ----------------------------------------------------------------------------------------------
    Adds FW_PageGeneration_B_U.GeneratedBrowseHash so a hand-edited _B page is protected the same
    way a _U page is.

    Why
    ---
    Only the maintenance page has ever had a baseline hash. A _B page edited by hand was overwritten
    by the next generation with no warning of any kind - the protection everyone assumed covered
    "the generated pages" covered half of them.

    The column mirrors GeneratedMaintenanceHash: a SHA-256 hex string of the source as written, or
    NULL for a request that has not generated its browse page since this column existed. NULL means
    unprotected, which is the same as today, so applying this changes nothing until the next
    generation writes a baseline.

    There is no GeneratedBrowseSource to match GeneratedMaintenanceSource. The maintenance source is
    stored because the page keeps a baseline for comparison; the browse side only needs to answer
    whether the file still matches, and the hash alone answers that.

    Rollback
    --------
    At the bottom of this file.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_PageGeneration_B_U')
                 AND name = 'GeneratedBrowseHash')
BEGIN
    ALTER TABLE dbo.FW_PageGeneration_B_U ADD GeneratedBrowseHash VARCHAR(64) NULL;
END
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration, and revert the matching code change.
    ----------------------------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_PageGeneration_B_U')
             AND name = 'GeneratedBrowseHash')
BEGIN
    ALTER TABLE dbo.FW_PageGeneration_B_U DROP COLUMN GeneratedBrowseHash;
END
GO
*/
