SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER VIEW dbo.vw_FW_UserRoles AS
SELECT
    ur.UserRoleID,
    ur.RegistrationID,
    ur.UserID,
    ur.RoleID,
    u.FirstLast,
    r.RoleName,
    ur.DisplayOrder,
    ur.IsActive,
    ur.CreatedBy,
    ur.CreatedOn,
    ur.UpdatedBy,
    ur.UpdatedOn,
FROM dbo.FW_UserRoles AS ur
INNER JOIN dbo.FW_Users AS u
    ON u.UserId = ur.UserID
INNER JOIN dbo.FW_Roles AS r
    ON r.ID = ur.RoleID;
GO