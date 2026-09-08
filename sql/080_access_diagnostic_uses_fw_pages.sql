/*
    080_access_diagnostic_uses_fw_pages.sql

    usp_FW_AccessDiagnostic still read dbo.FW_RoleTables, renamed to dbo.FW_Pages on 2026-09-03.
    The reference has not resolved since, so the procedure would have failed on execution. It was
    found by the sys.sql_expression_dependencies sweep after removing the FW_Users pages on
    2026-09-08, and it was the only object in the database still naming the old table.

    Two changes, both forced by that rename:

        dbo.FW_RoleTables  ->  dbo.FW_Pages
        rt.ID              ->  rt.PageID     (the key was renamed under the <Stem>ID convention)

    Every other column the procedure uses - DB_Table, Table_Alias, WindowOrPage, ExposedToUser,
    RegistrationID - exists on FW_Pages unchanged.

    Nothing was visibly broken because the only caller is FW_UserAccessDiagnostic_B, which has no
    icon, no menu tile and no caller anywhere in the source. A broken procedure behind an
    unreachable page is still a broken procedure, and it would have surfaced the moment that page
    was given a way in.

    The misspelled output column ExposededToUser is left exactly as it is: the page reads its
    columns by name, so correcting the spelling here would break the one caller this has.

    Body below is the live definition with only those two substitutions applied.
*/
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


