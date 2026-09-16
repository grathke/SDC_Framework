/*
    118_employee_timezone.sql

    A time zone on the employee, overriding the registration's when set.

    Nullable, and null is the normal case: it means "use the registration's". A person in another
    zone gets their own.

    int, matching FW_TimeZones.TimeZoneID and every other TimeZoneID in the database.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

IF COL_LENGTH('dbo.FW_Employees', 'TimeZoneID') IS NULL
BEGIN
    ALTER TABLE dbo.FW_Employees ADD TimeZoneID int NULL;
    PRINT 'Added dbo.FW_Employees.TimeZoneID';
END
ELSE
    PRINT 'dbo.FW_Employees.TimeZoneID already exists';
GO

IF OBJECT_ID('dbo.FK_FW_Employees_FW_TimeZones', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.FW_Employees
      ADD CONSTRAINT FK_FW_Employees_FW_TimeZones
      FOREIGN KEY (TimeZoneID) REFERENCES dbo.FW_TimeZones (TimeZoneID);
    PRINT 'Added FK_FW_Employees_FW_TimeZones';
END
ELSE
    PRINT 'FK_FW_Employees_FW_TimeZones already exists';
GO

SELECT fk.name AS Constraint_Name, OBJECT_NAME(fk.parent_object_id) AS FromTable, cp.name AS FromColumn
  FROM sys.foreign_keys fk
  JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
  JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
 WHERE fk.referenced_object_id = OBJECT_ID('dbo.FW_TimeZones')
 ORDER BY FromTable;
