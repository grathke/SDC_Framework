/*
    038_page_generation_icon.sql
    ----------------------------------------------------------------------------------------------
    Adds the dashboard icon to a page generation request.

    Why
    ---
    A generated page's dashboard button has always used a SystemIcons glyph, the same one for every
    generated page. The request can now name a file from assets\images, and the generated button
    loads it through the dashboard's existing LoadDashboardIcon helper.

    The column holds the file name only, never a path and never the image bytes. The images live in
    assets\images beside the exe, so one copy serves every application in the folder and a picture
    can be replaced without a rebuild.

    Null means no icon was chosen, and the generated button keeps the SystemIcons fallback.

    Rollback
    --------
    The rollback is at the bottom of this file.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_PageGeneration_B_U')
                 AND name = 'IconFileName')
BEGIN
    ALTER TABLE dbo.FW_PageGeneration_B_U ADD IconFileName varchar(100) NULL;
END
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration.
    ----------------------------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_PageGeneration_B_U')
             AND name = 'IconFileName')
BEGIN
    ALTER TABLE dbo.FW_PageGeneration_B_U DROP COLUMN IconFileName;
END
GO
*/
