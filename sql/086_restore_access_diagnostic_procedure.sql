/*
    086_restore_access_diagnostic_procedure.sql

    Recreates dbo.usp_FW_AccessDiagnostic, which 081 dropped on 2026-09-09 together with
    FW_UserAccessDiagnostic_B. Both are restored today.

    The removal was argued from reachability: the page had no icon, no menu tile and no caller in
    the source, and the procedure behind it had been broken for five days without anyone noticing.
    Both facts were true. Neither was the right test. What was never checked is what the page could
    do that the page kept in its place could not - and the answer was: set an individual permission.
    The survivor writes a fixed set of defaults and nothing else, so deleting this one removed the
    only way to grant Read, or revoke Delete, without going to Roles_U.

    The body below is exactly the definition 080 repaired - dbo.FW_RoleTables -> dbo.FW_Pages and
    rt.ID -> rt.PageID, forced by the 2026-09-03 rename - and not the broken one that preceded it.
    Nothing else is changed.

    The misspelled output column ExposededToUser stays misspelled, for the same reason 080 gave: the
    page reads its columns by name, and correcting the spelling here would break the one caller this
    has.

    Idempotent: CREATE OR ALTER, so running it twice is harmless.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_FW_AccessDiagnostic
    @UserID INT,
    @RegistrationID INT,
    @DB_Table NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH AssignedRoles AS
    (
        SELECT DISTINCT
            ur.RoleID,
            r.RoleName
        FROM dbo.FW_UserRoles AS ur
        INNER JOIN dbo.FW_Roles AS r
            ON r.ID = ur.RoleID
        WHERE ur.UserID = @UserID
          AND ur.RegistrationID = @RegistrationID
          AND ISNULL(ur.IsActive, 1) = 1
          AND ISNULL(r.IsActive, 1) = 1
    ),
    TargetTable AS
    (
        SELECT TOP (1)
            rt.DB_Table,
            ISNULL(rt.Table_Alias, rt.DB_Table) AS Table_Alias,
            rt.WindowOrPage,
            ISNULL(rt.ExposedToUser, 0) AS ExposededToUser
        FROM dbo.FW_Pages AS rt
        WHERE rt.DB_Table = @DB_Table
        ORDER BY CASE WHEN rt.RegistrationID = @RegistrationID THEN 0 ELSE 1 END, rt.PageID DESC
    )
    SELECT
        @UserID AS UserID,
        @RegistrationID AS RegistrationID,
        tt.DB_Table,
        tt.Table_Alias,
        tt.WindowOrPage,
        ISNULL(tt.ExposededToUser, 0) AS ExposededToUser,
        ar.RoleID,
        ar.RoleName,
        CASE WHEN ar.RoleID IS NULL THEN 0 ELSE 1 END AS HasActiveRole,
        ISNULL(rd.Can_Create, 0) AS Can_Create,
        ISNULL(rd.Can_Read, 0) AS Can_Read,
        ISNULL(rd.Can_Update, 0) AS Can_Update,
        ISNULL(rd.Can_Delete, 0) AS Can_Delete,
        ISNULL(rd.Can_UseQBE, 0) AS Can_UseQBE,
        ISNULL(rd.Can_ViewAllRecords, 0) AS Can_ViewAllRecords,
        ISNULL(rd.Can_ViewOnlyMyRecords, 0) AS Can_ViewOnlyMyRecords
    FROM TargetTable AS tt
    LEFT JOIN AssignedRoles AS ar
        ON 1 = 1
    LEFT JOIN dbo.FW_RoleDetails AS rd
        ON rd.RoleID = ar.RoleID
       AND rd.RegistrationID = @RegistrationID
       AND rd.DB_Table = tt.DB_Table
    ORDER BY ar.RoleName, ar.RoleID;
END;
GO

PRINT 'usp_FW_AccessDiagnostic restored';
GO
