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
            ISNULL(rt.ExposedToUser, 0) AS ExposedToUser
        FROM dbo.FW_RoleTables AS rt
        WHERE rt.DB_Table = @DB_Table
        ORDER BY CASE WHEN rt.RegistrationID = @RegistrationID THEN 0 ELSE 1 END, rt.ID DESC
    )
    SELECT
        @UserID AS UserID,
        @RegistrationID AS RegistrationID,
        tt.DB_Table,
        tt.Table_Alias,
        tt.WindowOrPage,
        ISNULL(tt.ExposedToUser, 0) AS ExposedToUser,
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
