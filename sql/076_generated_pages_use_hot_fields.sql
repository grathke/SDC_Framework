/*
    The generation request remembers whether the page it produces should show Hot Fields.

    FW_Pages.UseHotFields (075) is what the running page reads. This is the request's copy of the
    same answer, so reopening a request shows the tick as it was left, and regenerating writes it
    to FW_Pages again.

    See BASE_BHF_SPEC.md section 1.1.
*/

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.FW_GeneratedPages')
      AND name = 'UseHotFields'
)
BEGIN
    ALTER TABLE dbo.FW_GeneratedPages
        ADD UseHotFields bit NOT NULL CONSTRAINT DF_FW_GeneratedPages_UseHotFields DEFAULT (0);

    PRINT 'FW_GeneratedPages.UseHotFields added, defaulting to 0.';
END
ELSE
BEGIN
    PRINT 'FW_GeneratedPages.UseHotFields already exists.';
END
GO
