/*
    081_remove_user_access_diagnostic_page.sql

    Removes what FW_UserAccessDiagnostic_B leaves behind, the page having been deleted from the
    source on 2026-09-08.

    It had no icon, no menu tile and no caller anywhere in the application. FW_UserAccessExplanation_B
    does the same job and is reachable from the App Admin dashboard, which is what made the two easy
    to confuse: until 079 the Explanation page was captioned 'User Access Diag.', so from a dashboard
    they were indistinguishable.

    usp_FW_AccessDiagnostic goes with it. This is the procedure 080 repaired hours earlier - it had
    read a table renamed away on 2026-09-03 and would have failed on execution. Fixing it first was
    not wasted: a procedure that cannot run is not evidence that nothing needs it, and the fix is
    what established that the only thing needing it was a page with no way in. 080 stays in the
    history as the record of that.

    The three DataAccess methods that went with the page - GetAccessDiagnostic,
    GetAccessDiagnosticRoleFields and UpdateDiagnosticRoleField - are gone from the source. The
    latter two already had no callers at all. Everything else named GetAccessDiagnostic* stays:
    those belong to the Explanation page.

    SAFE TO RE-RUN.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

    -- 1. The page row. Its alias was corrected from the stale 'Entity' by 079 this afternoon,
    --    which is how the page came to be looked at at all.
    DELETE FROM dbo.FW_Pages
    WHERE WindowOrPage = 'FW_UserAccessDiagnostic_B';
    PRINT 'FW_Pages: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 2. Saved grid column layouts, per user.
    DELETE FROM dbo.FW_TableLayouts
    WHERE PageName = 'FW_UserAccessDiagnostic_B';
    PRINT 'FW_TableLayouts: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 3. Saved searches, keyed by the caption rather than the page name.
    DELETE FROM dbo.FW_SavedQbe
    WHERE TableContext IN ('User Access Diagnostic', 'Entity');
    PRINT 'FW_SavedQbe: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 4. Dashboard tile position and picture. Expected to be none: the page never had an icon,
    --    which is the whole reason it is being removed.
    DELETE FROM dbo.FW_DashboardLayouts
    WHERE ActionKey IN ('ActionKey_FW_UserAccessDiagnostic_B', 'generated-fw_useraccessdiagnostic_b');
    PRINT 'FW_DashboardLayouts: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- 5. The generation request is SOFT deleted, not removed. It holds every field choice the page
    --    was generated from, and the framework's own policy for this table is a DeletedFlag. A hard
    --    delete here would be the one irreversible act in an otherwise recoverable change.
    UPDATE dbo.FW_GeneratedPages
    SET DeletedFlag = 1
    WHERE PageBaseName = 'UserAccessDiagnostic'
      AND ISNULL(DeletedFlag, 0) = 0;
    PRINT 'FW_GeneratedPages soft deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

-- 6. The procedure, outside the transaction because DROP cannot be rolled back usefully here.
IF OBJECT_ID('dbo.usp_FW_AccessDiagnostic', 'P') IS NOT NULL
BEGIN
    DROP PROCEDURE dbo.usp_FW_AccessDiagnostic;
    PRINT 'usp_FW_AccessDiagnostic dropped';
END
ELSE
    PRINT 'usp_FW_AccessDiagnostic already absent';

/*
    The two sweeps. Neither should return rows.
*/
SELECT 'FW_RoleDetails' AS Source, DB_Table AS MissingTable
FROM dbo.FW_RoleDetails WHERE OBJECT_ID('dbo.' + DB_Table) IS NULL
UNION ALL
SELECT 'FW_RoleSchema', DB_Table
FROM dbo.FW_RoleSchema WHERE OBJECT_ID('dbo.' + DB_Table) IS NULL;

SELECT OBJECT_NAME(referencing_id) AS ReferencingObject,
       referenced_entity_name       AS MissingReference
FROM sys.sql_expression_dependencies
WHERE referenced_id IS NULL;
