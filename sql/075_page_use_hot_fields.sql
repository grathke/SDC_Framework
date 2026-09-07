/*
    Hot Fields is opt in, per page.

    Every browse page inherits the widget from FW_Base_B, but the button only appears where this
    flag is set. One column on the row the page already has - no new page kind, no new suffix, no
    new base class, and nothing that has to be regenerated.

    It costs no extra read: EnsurePageAliasCache already loads every FW_Pages row in a single query
    for the session, and this column is read in the same pass.

    See BASE_BHF_SPEC.md section 1.1.
*/

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.FW_Pages')
      AND name = 'UseHotFields'
)
BEGIN
    ALTER TABLE dbo.FW_Pages
        ADD UseHotFields bit NOT NULL CONSTRAINT DF_FW_Pages_UseHotFields DEFAULT (0);

    PRINT 'FW_Pages.UseHotFields added, defaulting to 0.';
END
ELSE
BEGIN
    PRINT 'FW_Pages.UseHotFields already exists.';
END
GO
