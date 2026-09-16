/*
    120_employee_registration_foreign_key.sql

    FW_Employees.RegistrationID -> FW_Registration.

    The column has always held a registration, but nothing declared it, so the page generator
    could not offer RegistrationID as a lookup - it would have generated a text box asking for a
    number.

    Re-runnable, and refuses rather than fails if any row points at nothing.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM dbo.FW_Employees e
            WHERE e.RegistrationID IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.FW_Registration r WHERE r.RegistrationID = e.RegistrationID))
BEGIN
    PRINT 'REFUSED: FW_Employees has rows whose RegistrationID matches no registration.';
    SELECT EmployeeID, RegistrationID FROM dbo.FW_Employees e
     WHERE e.RegistrationID IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.FW_Registration r WHERE r.RegistrationID = e.RegistrationID);
    RETURN;
END

IF OBJECT_ID('dbo.FK_FW_Employees_FW_Registration', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.FW_Employees
      ADD CONSTRAINT FK_FW_Employees_FW_Registration
      FOREIGN KEY (RegistrationID) REFERENCES dbo.FW_Registration (RegistrationID);
    PRINT 'Added FK_FW_Employees_FW_Registration';
END
ELSE
    PRINT 'FK_FW_Employees_FW_Registration already exists';
GO

SELECT fk.name AS Constraint_Name, OBJECT_NAME(fk.parent_object_id) AS FromTable, cp.name AS FromColumn
  FROM sys.foreign_keys fk
  JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
  JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
 WHERE fk.referenced_object_id = OBJECT_ID('dbo.FW_Registration')
 ORDER BY FromTable;
