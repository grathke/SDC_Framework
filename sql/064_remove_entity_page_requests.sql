/*
    064_remove_entity_page_requests.sql
    ==============================================================================================
    Removes the three page-generation requests that build pages against FW_ENTITY.

    FW_PageGeneration_B_U is the page generator's request log. Each row can be reopened and
    regenerated, which is what makes a stale one worth removing rather than ignoring: regenerating
    EntityX today would emit a page whose every SELECT names a table that was dropped in 059.

        1  Entity Test  EntityX  -> EntityX_B / EntityX_U  on FW_ENTITY
        2  Entity Y     EntityY  -> EntityY_B / EntityY_U  on FW_ENTITY
        3  EntityA      EntityA  -> EntityA_B / EntityA_U  on FW_ENTITY

    None of those six page files exists. EntityA had already lost its files before today - it is
    also what left the stray EntityA_B row that 062 cleared out of FW_TableLayouts.

    ----------------------------------------------------------------------------------------------
    ROW 4 STAYS
    ----------------------------------------------------------------------------------------------
    UserAccessDiagnostic, on FW_USERS. Its browse page FW_UserAccessDiagnostic_B is live. It also
    names FW_UserAccessDiagnostic_U, which does not exist - but that is by design rather than by
    accident: CLAUDE.md lists that page among the browse-only pages with no _U partner. The request
    records what was asked for, and what was asked for is still running.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- Keyed on the table they generate against, not on their names. A request is stale because its
-- table is gone, and that is the condition worth expressing.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_PageGeneration_B_U
WHERE  UnderlyingTableName IS NOT NULL
  AND  LTRIM(RTRIM(UnderlyingTableName)) <> ''
  AND  OBJECT_ID('dbo.' + LTRIM(RTRIM(UnderlyingTableName))) IS NULL;

PRINT CONCAT('Page requests deleted: ', @@ROWCOUNT);

COMMIT TRANSACTION;

PRINT '';
PRINT '=== REMAINING PAGE REQUESTS ===';
SELECT PageRequestID, RequestName, BrowsePageName, MaintenancePageName, UnderlyingTableName
FROM   dbo.FW_PageGeneration_B_U
ORDER  BY PageRequestID;
