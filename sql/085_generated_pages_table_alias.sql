/*
    085_generated_pages_table_alias.sql

    FW_GeneratedPages.TableAlias - what a generated page is called.

    The generator derived the caption from the table name on every run: FW_Users became Users, and
    UpsertPageRecord wrote it over FW_Pages.Table_Alias each time. So a caption chosen by hand
    survived exactly until the next generation, silently, and the page went back to being named
    after its table.

    The request already defines the page - its fields, their order, its Hot Fields - so its name
    belongs there too. Empty means derive it as before, which is what every existing request does.

    Nothing is backfilled. A request with no alias keeps deriving one, so no page's caption changes
    because this column arrived.

    SAFE TO RE-RUN.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_GeneratedPages')
                 AND name = 'TableAlias')
BEGIN
    ALTER TABLE dbo.FW_GeneratedPages ADD TableAlias VARCHAR(100) NULL;
    PRINT 'FW_GeneratedPages.TableAlias added';
END
ELSE
    PRINT 'FW_GeneratedPages.TableAlias already present';
GO

SET NOCOUNT ON;

SELECT PageBaseName, BrowsePageName, TableAlias
FROM dbo.FW_GeneratedPages
WHERE ISNULL(DeletedFlag, 0) = 0
ORDER BY PageBaseName;
