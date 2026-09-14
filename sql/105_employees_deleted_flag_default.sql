-- FW_Employees.DeletedFlag gets the default and the NOT NULL every other table gives it.
--
-- FW_Roles, FW_RoleSchema, FW_EmployeeRoles and FW_Registration all declare it NOT NULL with a
-- default of 0. FW_Employees was the only one with neither, so a row written without naming the
-- column got NULL - which every query then has to remember to read as "not deleted", and dozens
-- of them spell ISNULL(DeletedFlag, 0) = 0 to do it.
--
-- NULL means unknown, and there is no such thing as a maybe-deleted row.
UPDATE dbo.FW_Employees SET DeletedFlag = 0 WHERE DeletedFlag IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc
               JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
               WHERE dc.parent_object_id = OBJECT_ID('dbo.FW_Employees') AND c.name = 'DeletedFlag')
BEGIN
    ALTER TABLE dbo.FW_Employees
        ADD CONSTRAINT DF_FW_Employees_DeletedFlag DEFAULT ((0)) FOR DeletedFlag;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'DeletedFlag' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.FW_Employees ALTER COLUMN DeletedFlag bit NOT NULL;
END
GO
