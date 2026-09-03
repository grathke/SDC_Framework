/*
    057_dashboard_icon_image.sql
    ----------------------------------------------------------------------------------------------
    Adds IconFileName to FW_DashboardLayouts, so an App Admin can re-picture a dashboard icon at
    runtime and have the choice survive the next launch. Makes GridRow and GridColumn nullable in
    the same breath, because a row can now exist for a reason that has nothing to do with position.

    Why this table
    --------------
    It already holds exactly one row per icon per dashboard - UX_FW_DashboardLayouts_Icon enforces
    that - so the picture rides along with the position rather than needing a table of its own. 055
    took RegistrationID off it because neither dashboard is registration aware, which also settles
    the scope of this column: there is no scope left to disagree about, so the choice is global.

    Why the columns become nullable
    -------------------------------
    054 wrote the table for one purpose, where every row was a drop and so always had a cell. An
    icon that is re-pictured but never dragged has no cell, and the obvious stand-in is the worst
    one available: inserting 0, 0 files it at the top-left corner, and the arrangement loader would
    faithfully move it there on the next launch. Choosing a picture would silently move the icon.

    NULL says what is true - this row records a picture and not a position - and the loader skips
    those rows rather than reading a position that was never chosen.

    What the value is
    -----------------
    The same string the page generator stores in FW_PageGeneration_B_U.IconFileName, and the same
    one IconPicker returns: either a file name in assets\images, or a built-in glyph marked with the
    system: prefix. One vocabulary, so a choice made in either place means the same thing.

    NULL IconFileName means the icon keeps the picture its dashboard was written with. That is the
    default and what Reset restores, so nothing needs seeding here.

    Guarded, so running this twice changes nothing. Rollback at the bottom - it cannot restore
    NOT NULL if any row has since recorded a picture without a position, which is stated rather
    than worked around.
*/

IF COL_LENGTH('dbo.FW_DashboardLayouts', 'IconFileName') IS NULL
    ALTER TABLE dbo.FW_DashboardLayouts ADD IconFileName NVARCHAR(260) NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_DashboardLayouts')
             AND name = 'GridRow' AND is_nullable = 0)
    ALTER TABLE dbo.FW_DashboardLayouts ALTER COLUMN GridRow INT NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_DashboardLayouts')
             AND name = 'GridColumn' AND is_nullable = 0)
    ALTER TABLE dbo.FW_DashboardLayouts ALTER COLUMN GridColumn INT NULL;
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK
    ----------------------------------------------------------------------------------------------

DELETE FROM dbo.FW_DashboardLayouts WHERE GridRow IS NULL OR GridColumn IS NULL;
ALTER TABLE dbo.FW_DashboardLayouts ALTER COLUMN GridRow INT NOT NULL;
ALTER TABLE dbo.FW_DashboardLayouts ALTER COLUMN GridColumn INT NOT NULL;
ALTER TABLE dbo.FW_DashboardLayouts DROP COLUMN IconFileName;
GO
*/
