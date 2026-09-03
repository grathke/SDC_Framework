/*
    058_rename_menu_surface_to_sdc_framework.sql
    ----------------------------------------------------------------------------------------------
    Renames the main menu's surface from "HelloWorld.MainMenu" to "SDC.Framework.MainMenu",
    following the project's rename from HelloWorld to SDC.Framework.

    Why this needs a migration at all
    ---------------------------------
    The surface name is not a label. It is FW_DashboardLayouts.DashboardName - the key every saved
    ribbon arrangement and every chosen tile picture is stored under. MenuFormInitializer passes it
    to ConfigureArrangement, and DataAccess reads it back on every menu load. Change the constant in
    the code without moving the rows and nothing errors: the load simply finds no rows, the ribbon
    silently returns to its source order, every chosen picture reverts, and the old rows sit there
    unreachable. A rename that quietly discards saved work is worse than one that fails.

    Why an UPDATE rather than a delete and re-save
    ----------------------------------------------
    The rows carry more than position: IconFileName, the audit columns, and the DeletedFlag state -
    including the two spacing-test rows soft-deleted on 2026-09-03, which must stay deleted rather
    than reappear. An UPDATE moves all of it. It also keeps the DashboardLayoutID values, so
    anything that ever referenced one still resolves.

    Safe to run more than once. The second run matches nothing, because the first left no row under
    the old name.
*/

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FW_DashboardLayouts')
BEGIN
    UPDATE dbo.FW_DashboardLayouts
    SET DashboardName = 'SDC.Framework.MainMenu'
    WHERE DashboardName = 'HelloWorld.MainMenu';

    PRINT 'Rows moved to SDC.Framework.MainMenu: ' + CAST(@@ROWCOUNT AS VARCHAR(10));
END
ELSE
BEGIN
    -- A database without 054 applied has no such table, and so nothing to rename. Not an error:
    -- the application already treats a missing table as "no saved arrangement".
    PRINT 'FW_DashboardLayouts does not exist; nothing to rename.';
END

COMMIT TRANSACTION;
