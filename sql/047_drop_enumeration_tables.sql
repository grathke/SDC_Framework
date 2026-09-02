/*
    047_drop_enumeration_tables.sql
    ----------------------------------------------------------------------------------------------
    Drops vw_FW_ControlUpdates_U, FW_Enumerations and FW_Enumerations_U.

    Why
    ---
    Field-level permissions used to resolve through a stored snapshot of each page's controls,
    written by an Enum button someone had to remember to press and joined to FW_RoleFields through
    this view. Any control added after the snapshot was invisible to permissions - no caption, no
    required marker, no hiding - and nothing reported it. One page had five such fields; three
    pages had never been captured at all, so field permissions had never once applied to them.

    Permissions now derive the mapping from the live page: a control named TextBox_Address1 on a
    page whose table is FW_Users is FW_Users.Address1. Nothing has read these tables or this view
    since. The Enum buttons were removed, and the last writer - Apply SQL on a browse page - is
    removed in the same change as this script, so nothing writes them either.

    What is destroyed
    -----------------
    104 rows in FW_Enumerations and 26 in FW_Enumerations_U: a record of which controls each page
    carried when it was last enumerated, the most recent in July. Nothing reads it and the code
    that produced it is gone, so it cannot be regenerated. The rows are scripted out in
    047_enumeration_rows_backup.txt, which is the only copy after this runs.

    Order matters: the view depends on FW_Enumerations_U, so it goes first.

    Rollback
    --------
    At the bottom of this file: both tables, then the view, then the data from the backup file.
*/

IF EXISTS (SELECT 1 FROM sys.views WHERE object_id = OBJECT_ID('dbo.vw_FW_ControlUpdates_U'))
BEGIN
    DROP VIEW dbo.vw_FW_ControlUpdates_U;
END
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('dbo.FW_Enumerations_U'))
BEGIN
    DROP TABLE dbo.FW_Enumerations_U;
END
GO

IF EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('dbo.FW_Enumerations'))
BEGIN
    DROP TABLE dbo.FW_Enumerations;
END
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration, and revert the matching code change.

    The code change removes EnumeratePageControls, EnumeratePageControls_U, their private writers,
    EnumerateControlsRecursive, ExtractColumnMappingsFromSql, and the Apply SQL call in
    01 FW_Base_B.vb. Restoring these objects without restoring that code leaves empty tables that
    nothing fills.

    After running this block, load the rows from 047_enumeration_rows_backup.txt.
    ----------------------------------------------------------------------------------------------

CREATE TABLE dbo.FW_Enumerations (
    [ID] int IDENTITY(1,1) NOT NULL,
    [PageName] varchar(50) NULL,
    [ControlName] varchar(100) NULL,
    [ControlCaption] varchar(50) NULL,
    [FileLink] varchar(100) NULL,
    [JsAlias] varchar(20) NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL,
    [DeletedBy] int NULL,
    [DeletedOn] datetime2 NULL,
    [RowVersion] timestamp NOT NULL
);
GO

CREATE TABLE dbo.FW_Enumerations_U (
    [ID] int IDENTITY(1,1) NOT NULL,
    [PageName] varchar(50) NULL,
    [ControlName] varchar(100) NULL,
    [LinkedControl] varchar(50) NULL,
    [ControlCaption] varchar(50) NULL,
    [FileLink] varchar(100) NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL,
    [DeletedBy] int NULL,
    [DeletedOn] datetime2 NULL,
    [RowVersion] timestamp NOT NULL
);
GO

CREATE VIEW dbo.vw_FW_ControlUpdates_U
AS
SELECT      dbo.FW_RoleFields.RegistrationID,
            dbo.FW_RoleFields.RoleID,
            dbo.FW_Enumerations_U.PageName,
            dbo.FW_Enumerations_U.ControlName,
            dbo.FW_Enumerations_U.LinkedControl,
            dbo.FW_RoleFields.OverrideCaption,
            dbo.FW_RoleFields.CA_CanChange,
            dbo.FW_RoleFields.Can_Create,
            dbo.FW_RoleFields.Can_Read,
            dbo.FW_RoleFields.Can_Update,
            dbo.FW_RoleFields.IsRequired,
            dbo.FW_RoleFields.IsUnique,
            dbo.FW_RoleFields.Make_Invisible,
            dbo.FW_RoleFields.OrderBy
FROM        dbo.FW_Enumerations_U
INNER JOIN  dbo.FW_RoleFields
        ON  dbo.FW_Enumerations_U.FileLink = dbo.FW_RoleFields.FileLink
WHERE       dbo.FW_RoleFields.IsActive = 1;
GO
*/
