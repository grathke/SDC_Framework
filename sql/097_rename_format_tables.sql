-- FW_DateFormat -> FW_Format_Date, FW_TimeFormat -> FW_Format_Time.
--
-- Named for how they are found rather than how they are read. A table list sorts Format_Date
-- and Format_Time next to each other, and next to whatever format table comes after them; the
-- old names put them at opposite ends of the D-to-T stretch with everything else between.
--
-- The primary keys follow. The convention derives a key from its table - the name past FW_,
-- singular - so FW_Format_Date keys on FormatDateID. Leaving DateFormatID behind would give the
-- table and its own key opposite word orders, and the foreign key rule is that a column is
-- spelled exactly like the key it points at, so FW_Registration moves with it.
--
-- Done now because these are one day old. A key name reaches every query, every model property,
-- every TextBox_<Field> and every FW_RoleFields row keyed Table.Field; today that is two
-- permission rows and one query.
EXEC sp_rename 'dbo.FW_DateFormat', 'FW_Format_Date';
EXEC sp_rename 'dbo.FW_TimeFormat', 'FW_Format_Time';
GO

EXEC sp_rename 'dbo.FW_Format_Date.DateFormatID', 'FormatDateID', 'COLUMN';
EXEC sp_rename 'dbo.FW_Format_Time.TimeFormatID', 'FormatTimeID', 'COLUMN';
EXEC sp_rename 'dbo.FW_Registration.DateFormatID', 'FormatDateID', 'COLUMN';
EXEC sp_rename 'dbo.FW_Registration.TimeFormatID', 'FormatTimeID', 'COLUMN';
GO

-- Constraints carry the old names in their own. Renamed so the next person reading a constraint
-- error is told which table it is actually about.
EXEC sp_rename 'PK_FW_DateFormat', 'PK_FW_Format_Date';
EXEC sp_rename 'PK_FW_TimeFormat', 'PK_FW_Format_Time';
EXEC sp_rename 'DF_FW_DateFormat_DisplayOrder', 'DF_FW_Format_Date_DisplayOrder';
EXEC sp_rename 'DF_FW_DateFormat_IsActive', 'DF_FW_Format_Date_IsActive';
EXEC sp_rename 'DF_FW_TimeFormat_DisplayOrder', 'DF_FW_Format_Time_DisplayOrder';
EXEC sp_rename 'DF_FW_TimeFormat_IsActive', 'DF_FW_Format_Time_IsActive';
EXEC sp_rename 'FK_FW_Registration_DateFormat', 'FK_FW_Registration_Format_Date';
EXEC sp_rename 'FK_FW_Registration_TimeFormat', 'FK_FW_Registration_Format_Time';
GO

-- The metadata rows. FW_RoleSchema first, or Roles_U offers a table that no longer exists and
-- not the one that does.
UPDATE dbo.FW_RoleSchema SET DB_Table = 'FW_Format_Date' WHERE DB_Table = 'FW_DateFormat';
UPDATE dbo.FW_RoleSchema SET DB_Table = 'FW_Format_Time' WHERE DB_Table = 'FW_TimeFormat';

UPDATE dbo.FW_RoleFields SET FieldName = 'FormatDateID', FileLink = 'FW_Registration.FormatDateID'
 WHERE FileLink = 'FW_Registration.DateFormatID';
UPDATE dbo.FW_RoleFields SET FieldName = 'FormatTimeID', FileLink = 'FW_Registration.FormatTimeID'
 WHERE FileLink = 'FW_Registration.TimeFormatID';
GO
