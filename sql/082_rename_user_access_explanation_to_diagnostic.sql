/*
    082_rename_user_access_explanation_to_diagnostic.sql

    FW_UserAccessExplanation_B becomes FW_UserAccessDiagnostic_B, and is captioned
    'User Access Diagnostic'.

    The two pages were always confusable. The one that could be opened was named Explanation and
    captioned 'User Access Diag.'; the one named Diagnostic could not be opened at all and was
    captioned 'Entity'. 079 corrected both captions, 081 removed the unreachable page, and this
    gives the survivor the name that was being asked for all along - which is only free to take
    because 081 vacated it an hour earlier.

    A rename reaches the same tables a removal does, because every one of them keys on the page
    name or on something derived from it:

        FW_Pages.WindowOrPage           the page's identity
        FW_Pages.Table_Alias            the caption every button and title resolves through
        FW_TableLayouts.PageName        saved grid columns, per user
        FW_DashboardLayouts.ActionKey   tile position and chosen picture, on both dashboards
        FW_SavedQbe.TableContext        saved searches, keyed by CAPTION rather than page name

    FW_RoleDetails and FW_RoleFields are not touched: they key on DB_Table, which is still FW_Users,
    and the page's permissions should survive its rename.

    SAFE TO RE-RUN. Every update is conditional on the old value still being present.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

    UPDATE dbo.FW_Pages
    SET WindowOrPage = 'FW_UserAccessDiagnostic_B',
        Table_Alias  = 'User Access Diagnostic'
    WHERE WindowOrPage = 'FW_UserAccessExplanation_B';
    PRINT 'FW_Pages renamed: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    UPDATE dbo.FW_TableLayouts
    SET PageName = 'FW_UserAccessDiagnostic_B'
    WHERE PageName = 'FW_UserAccessExplanation_B';
    PRINT 'FW_TableLayouts: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    UPDATE dbo.FW_DashboardLayouts
    SET ActionKey = 'ActionKey_FW_UserAccessDiagnostic_B'
    WHERE ActionKey = 'ActionKey_FW_UserAccessExplanation_B';
    PRINT 'FW_DashboardLayouts: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    -- Saved searches follow the caption, not the page. Empty today, which is the only reason this
    -- rename costs nothing here: a later rename would orphan real saved searches.
    UPDATE dbo.FW_SavedQbe
    SET TableContext = 'User Access Diagnostic'
    WHERE TableContext IN ('User Access Explanation', 'User Access Diag.');
    PRINT 'FW_SavedQbe: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

-- What the page row says afterwards, and that nothing still carries the old name.
SELECT WindowOrPage, DB_Table, Table_Alias
FROM dbo.FW_Pages
WHERE WindowOrPage LIKE 'FW_UserAccess%';

SELECT 'FW_Pages' AS StillOldName, COUNT(*) AS Rows_ FROM dbo.FW_Pages WHERE WindowOrPage = 'FW_UserAccessExplanation_B'
UNION ALL SELECT 'FW_TableLayouts', COUNT(*) FROM dbo.FW_TableLayouts WHERE PageName = 'FW_UserAccessExplanation_B'
UNION ALL SELECT 'FW_DashboardLayouts', COUNT(*) FROM dbo.FW_DashboardLayouts WHERE ActionKey = 'ActionKey_FW_UserAccessExplanation_B';
