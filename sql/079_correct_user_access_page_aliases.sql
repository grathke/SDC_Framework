/*
    079_correct_user_access_page_aliases.sql

    Two FW_Pages rows carry an alias that names something other than the page.

        FW_UserAccessDiagnostic_B    'Entity'              -> 'User Access Diagnostic'
        FW_UserAccessExplanation_B   'User Access Diag.'   -> 'User Access Explanation'

    The first names a table deleted on 2026-09-03. The second is the mismatch that caused the
    caption work on 2026-09-06 in the first place: a button reading "User Access Diag." opening a
    page titled "USER ACCESS EXPLANATION". That work gave the caption one resolver; it did not
    correct the data the resolver reads, so the page has been answering to the other page's name
    ever since.

    Table_Alias is the default in that chain - below a genuine role override, above the derived
    name - so this changes BOTH the page title and any button carrying .PageName for these pages.
    That is the intent: one source, one name.

    Found by the orphan sweep after removing the FW_Users pages on 2026-09-08.

    SAFE TO RE-RUN. Each update is conditional on the old value still being there, so running it
    twice changes nothing and a later rename by hand is not silently reverted.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

    UPDATE dbo.FW_Pages
    SET Table_Alias = 'User Access Diagnostic'
    WHERE WindowOrPage = 'FW_UserAccessDiagnostic_B'
      AND ISNULL(Table_Alias, '') = 'Entity';
    PRINT 'FW_UserAccessDiagnostic_B: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

    UPDATE dbo.FW_Pages
    SET Table_Alias = 'User Access Explanation'
    WHERE WindowOrPage = 'FW_UserAccessExplanation_B'
      AND ISNULL(Table_Alias, '') = 'User Access Diag.';
    PRINT 'FW_UserAccessExplanation_B: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

-- What the two rows say afterwards.
SELECT WindowOrPage, DB_Table, Table_Alias
FROM dbo.FW_Pages
WHERE WindowOrPage IN ('FW_UserAccessDiagnostic_B', 'FW_UserAccessExplanation_B');

/*
    Any FW_Pages row whose alias still names a table that does not exist. Should return nothing:
    'Entity' was the only one, and it is the reason this script exists.
*/
SELECT WindowOrPage, Table_Alias
FROM dbo.FW_Pages
WHERE Table_Alias IS NOT NULL
  AND OBJECT_ID('dbo.FW_' + REPLACE(Table_Alias, ' ', '')) IS NULL
  AND OBJECT_ID('dbo.' + REPLACE(Table_Alias, ' ', '')) IS NOT NULL;
