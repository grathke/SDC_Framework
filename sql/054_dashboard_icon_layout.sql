/*
    054_dashboard_icon_layout.sql
    ----------------------------------------------------------------------------------------------
    Adds FW_DashboardLayouts: where a dashboard's icons have been dragged to.

    Why a table
    -----------
    The alternative was writing positions back into 02 FW Dashboard_Application.vb, which is what
    the page generator already does when it places a new icon. Not for a drag: a move would need a
    rebuild before it took effect, it could not differ by registration, and it would put a user's
    arrangement into a source file the generator also edits.

    Absence is the default
    ----------------------
    An icon with no row here stays where the source put it. Nothing needs seeding, a newly
    generated icon appears at the cell the generator chose, and resetting a dashboard is a delete
    rather than a recalculation.

    Keyed on ActionKey
    ------------------
    Every dashboard icon now carries one - ActionKey_Roles, ActionKey_EntityX_B - derived from what
    it opens rather than from its caption. A caption can be reworded and a position survives it;
    regenerating a page reproduces the same key, so a generated icon keeps its place too.

    DashboardName is recorded although only the admin dashboard can be rearranged today. It costs
    one column now and saves a migration if the company dashboard ever wants the same.

    Rollback at the bottom.
*/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FW_DashboardLayouts')
BEGIN
    CREATE TABLE dbo.FW_DashboardLayouts
    (
        DashboardLayoutID INT IDENTITY(1, 1) NOT NULL,
        RegistrationID    INT NOT NULL,
        DashboardName     NVARCHAR(128) NOT NULL,
        ActionKey         NVARCHAR(128) NOT NULL,
        GridRow           INT NOT NULL,
        GridColumn        INT NOT NULL,
        CreatedBy         INT NULL,
        CreatedOn         DATETIME NULL,
        UpdatedBy         INT NULL,
        UpdatedOn         DATETIME NULL,
        DeletedFlag       BIT NOT NULL CONSTRAINT DF_FW_DashboardLayouts_DeletedFlag DEFAULT (0),
        DeletedBy         INT NULL,
        DeletedOn         DATETIME2 NULL,
        RowVersion        TIMESTAMP NOT NULL,
        CONSTRAINT PK_FW_DashboardLayouts PRIMARY KEY CLUSTERED (DashboardLayoutID)
    );
END
GO

/*
    One position per icon per dashboard per registration. The unique index is what lets a drop be a
    single upsert rather than a read followed by a decision.
*/
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FW_DashboardLayouts_Icon')
BEGIN
    CREATE UNIQUE INDEX UX_FW_DashboardLayouts_Icon
        ON dbo.FW_DashboardLayouts (RegistrationID, DashboardName, ActionKey);
END
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - the table holds only arrangements, so dropping it returns every dashboard to the
    positions in its source.
    ----------------------------------------------------------------------------------------------

DROP TABLE dbo.FW_DashboardLayouts;
GO
*/
