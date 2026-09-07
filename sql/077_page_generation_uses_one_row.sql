/*
    FW_PageGeneration_B used to look itself up under two names at once.

    Its SQL, alias and caption came from a PageGeneration_B row - the name the page had before it
    took the FW_ prefix - because the class overrode ResolveBrowsePageName to return it. Its
    background colour and its Hot Fields flag came from the FW_PageGeneration_B row, because those
    are keyed on the class name. Two rows in FW_Pages for one page, each half used, and nothing on
    screen to say which was which.

    That is why the leftover row kept coming back. sql/063 deleted it and the application recreated
    it; sql/069 found it back, left it in place, and asked someone to find what recreated it. The
    override was the answer, and it has been removed - so this delete is the first one that holds.

    Run the application's build with that change in before running this. Deleting the row while the
    override is still there would only repeat the cycle a third time.

    What is lost, once: saved grid layouts, saved searches and any stored colour keyed to the old
    page name. Per-user conveniences, not data.

    Safe to run more than once.
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

PRINT '=== BEFORE ===';
SELECT WindowOrPage, Table_Alias, Background
FROM   dbo.FW_Pages
WHERE  DB_Table = 'FW_GeneratedPages'
ORDER  BY WindowOrPage;

/* The page's own title, and what the menu calls it. The surviving row read
   'Page Generation _B _U'; the deleted one read 'GeneratedPages'. Neither was the page's name. */
UPDATE dbo.FW_Pages
SET    Table_Alias = 'Page Generation'
WHERE  WindowOrPage = 'FW_PageGeneration_B'
  AND  ISNULL(Table_Alias, '') <> 'Page Generation';

/* Only ever the unprefixed leftover. The prefixed row is the page. */
DELETE FROM dbo.FW_Pages
WHERE  WindowOrPage = 'PageGeneration_B';

PRINT '=== AFTER ===';
SELECT WindowOrPage, Table_Alias, Background
FROM   dbo.FW_Pages
WHERE  DB_Table = 'FW_GeneratedPages'
ORDER  BY WindowOrPage;

COMMIT;
GO
