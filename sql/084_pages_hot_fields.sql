/*
    084_pages_hot_fields.sql

    FW_Pages.HotFields - which fields the Hot Fields panel shows, for the running page.

    sql/083 added the same list to FW_GeneratedPages, where a tick starts life. This is the copy the
    application reads. Two columns rather than one because they answer different questions: the
    request says what the page was born with, and this says what it shows now. An App Admin ticking
    on the panel changes this one; generating the page copies the request's list over it.

    A delimited field list. NULL or empty means every field, which is what the panel does today - so
    every existing page keeps its behaviour until somebody ticks something.

    It rides in the row EnsurePageAliasCache already reads, so it costs no round trip on page load.

    SAFE TO RE-RUN.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_Pages')
                 AND name = 'HotFields')
BEGIN
    ALTER TABLE dbo.FW_Pages ADD HotFields VARCHAR(MAX) NULL;
    PRINT 'FW_Pages.HotFields added';
END
ELSE
    PRINT 'FW_Pages.HotFields already present';

-- GO, because the batch below is compiled before the ALTER above has run. Without it the script
-- fails on a column it is in the middle of adding, which reads like the ALTER failed.
GO

SET NOCOUNT ON;

SELECT WindowOrPage, ISNULL(UseHotFields, 0) AS UseHotFields, HotFields
FROM dbo.FW_Pages
ORDER BY WindowOrPage;
