/*
    026_control_updates_role_scope.sql

    Scopes field-level control attributes to the logged-in user's role, and removes the
    hardcoded RegistrationID.

    Why
    ---
    vw_FW_ControlUpdates_U joined FW_Enumerations_U to FW_RoleFields on FileLink alone and
    exposed no RoleID. FW_RoleFields rows are per role, so a field configured for two roles
    returned two rows, and DataAccess.ApplyControlUpdates applied whichever arrived last -
    with no ORDER BY, an arbitrary role's attributes won.

    The view also filtered RegistrationID = 1, so no other registration received any field
    attributes at all: Make_Invisible, Can_Read, Can_Create, Can_Update and IsUnique were
    all inert outside registration 1.

    The FileLink-only join is deliberate and unchanged: the enumeration is the universal
    control list for a page. Only the FW_RoleFields side needs role scoping.

    Registration and role are both filtered one layer up, in DataAccess.GetControlUpdates,
    from the active session.

    Paired code change
    ------------------
    DataAccess.GetControlUpdates adds "AND RoleID = @RoleID" using
    SessionState.Current.Value.RoleID. Applying this script without that change is safe:
    the extra column is simply unused, but duplicate rows per role would return again.

    Rollback
    --------
    The original definition is at the bottom of this file. Run that block to restore it,
    and revert the GetControlUpdates change in the same step.
*/

CREATE OR ALTER VIEW dbo.vw_FW_ControlUpdates_U
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

/*
    ------------------------------------------------------------------------------
    ROLLBACK - original definition as it stood before this migration, 2026-08-29.
    Run this block to restore, and revert DataAccess.GetControlUpdates alongside it.
    ------------------------------------------------------------------------------

CREATE OR ALTER VIEW dbo.vw_FW_ControlUpdates_U
AS
SELECT        dbo.FW_RoleFields.RegistrationID, dbo.FW_Enumerations_U.PageName, dbo.FW_Enumerations_U.ControlName, dbo.FW_Enumerations_U.LinkedControl, dbo.FW_RoleFields.OverrideCaption,
                         dbo.FW_RoleFields.CA_CanChange, dbo.FW_RoleFields.Can_Create, dbo.FW_RoleFields.Can_Read, dbo.FW_RoleFields.Can_Update, dbo.FW_RoleFields.IsRequired, dbo.FW_RoleFields.IsUnique,
                         dbo.FW_RoleFields.Make_Invisible, dbo.FW_RoleFields.OrderBy
FROM            dbo.FW_Enumerations_U INNER JOIN
                         dbo.FW_RoleFields ON dbo.FW_Enumerations_U.FileLink = dbo.FW_RoleFields.FileLink
WHERE        (dbo.FW_RoleFields.IsActive = 1) AND (dbo.FW_RoleFields.RegistrationID = 1)
GO
*/
