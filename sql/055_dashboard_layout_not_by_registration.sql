/*
    055_dashboard_layout_not_by_registration.sql
    ----------------------------------------------------------------------------------------------
    Takes RegistrationID off FW_DashboardLayouts. A dashboard arrangement is one arrangement.

    Why
    ---
    054 scoped positions by registration on the assumption that the dashboards were registration
    aware. Neither of them is - the admin dashboard makes no reference to a registration at all,
    and the company one shows the same three icons to everybody. Scoping by a value the screen
    never consults would have meant an arrangement that silently reset when a session's
    registration changed, for no benefit anyone could see.

    Nothing is lost. The four saved positions keep their icons and cells; only the column they were
    filed under goes.

    Rollback at the bottom, which restores the column and its index but not the values that were in
    it - there is nothing to restore them from, and no reading of the data that needs them.
*/

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FW_DashboardLayouts_Icon')
    DROP INDEX UX_FW_DashboardLayouts_Icon ON dbo.FW_DashboardLayouts;
GO

IF COL_LENGTH('dbo.FW_DashboardLayouts', 'RegistrationID') IS NOT NULL
    ALTER TABLE dbo.FW_DashboardLayouts DROP COLUMN RegistrationID;
GO

/*
    One position per icon per dashboard, which is what makes a drop a single upsert.
*/
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FW_DashboardLayouts_Icon')
    CREATE UNIQUE INDEX UX_FW_DashboardLayouts_Icon
        ON dbo.FW_DashboardLayouts (DashboardName, ActionKey);
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK
    ----------------------------------------------------------------------------------------------

DROP INDEX UX_FW_DashboardLayouts_Icon ON dbo.FW_DashboardLayouts;
ALTER TABLE dbo.FW_DashboardLayouts ADD RegistrationID INT NOT NULL CONSTRAINT DF_FW_DashboardLayouts_RegistrationID DEFAULT (0);
CREATE UNIQUE INDEX UX_FW_DashboardLayouts_Icon ON dbo.FW_DashboardLayouts (RegistrationID, DashboardName, ActionKey);
GO
*/
