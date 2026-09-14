-- vw_FW_EmployeeRoles: read FirstLast from the computed column, not from the dropped FullName.
--
-- FW_Employees.FullName was removed on 2026-09-14, after sql/090 added the computed FirstLast and
-- LastFirst. The view selected "e.FullName AS FirstLast" - it was already presenting the stored
-- copy under the computed column's name - so the drop left it unbindable: "Invalid column name
-- 'FullName'", and anything reading the view failed outright.
--
-- The contract does not change. Same output columns, same names, same types, same rows. The only
-- difference is where FirstLast comes from, and the new source is the correct one: FullName had
-- drifted from the fields it was copied from - it read "Sandyy Litaker" for "Sandy Litaker" -
-- because nothing kept it in step.
GO
ALTER VIEW dbo.vw_FW_EmployeeRoles AS
SELECT
    er.UserRoleID,
    er.RegistrationID,
    er.EmployeeID,
    e.UserId,
    er.RoleID,
    e.FirstLast,
    r.RoleName,
    er.DisplayOrder,
    er.IsActive,
    er.CreatedBy,
    er.CreatedOn,
    er.UpdatedBy,
    er.UpdatedOn
FROM dbo.FW_EmployeeRoles AS er
INNER JOIN dbo.FW_Employees AS e
    ON e.EmployeeID = er.EmployeeID
INNER JOIN dbo.FW_Roles AS r
    ON r.ID = er.RoleID;
GO
