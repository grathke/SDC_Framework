/*
    117_timezone_foreign_keys.sql

    Declares the two foreign keys into dbo.FW_TimeZones:

        FW_Registration.TimeZoneID -> FW_TimeZones.TimeZoneID
        FW_Users.TimeZoneID        -> FW_TimeZones.TimeZoneID

    Both columns already spell their target's primary key exactly, which is the naming convention
    working - but a name is not a relationship. The page generator reads *declared* foreign keys
    to offer a lookup, so until these exist a generated page gives TimeZoneID a text box and greys
    the Lookup tick with nothing to point at.

    FW_Registration.TimeZoneID is int and FW_TimeZones.TimeZoneID is smallint; a foreign key
    cannot span the two, so the column is narrowed first. Both registrations hold null and there
    are no orphans in either table, checked before this was written.

    The primary key constraint is also renamed: it was created as PK_TimeZone, before the table
    became FW_TimeZones.

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

/* Refuse rather than truncate. Nothing should be outside smallint, but narrowing a column that
   holds a larger value would fail mid-statement and leave the reason to be guessed at. */
IF EXISTS (SELECT 1 FROM dbo.FW_Registration WHERE TimeZoneID IS NOT NULL AND (TimeZoneID > 32767 OR TimeZoneID < -32768))
BEGIN
    PRINT 'REFUSED: FW_Registration.TimeZoneID holds a value outside smallint.';
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.FW_Registration r
            WHERE r.TimeZoneID IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.FW_TimeZones z WHERE z.TimeZoneID = r.TimeZoneID))
BEGIN
    PRINT 'REFUSED: FW_Registration.TimeZoneID has values with no matching timezone.';
    SELECT RegistrationID, TimeZoneID FROM dbo.FW_Registration r
     WHERE r.TimeZoneID IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.FW_TimeZones z WHERE z.TimeZoneID = r.TimeZoneID);
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.FW_Users u
            WHERE u.TimeZoneID IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM dbo.FW_TimeZones z WHERE z.TimeZoneID = u.TimeZoneID))
BEGIN
    PRINT 'REFUSED: FW_Users.TimeZoneID has values with no matching timezone.';
    RETURN;
END

/* 1. Narrow the registration column to match its target. */
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID('dbo.FW_Registration') AND c.name = 'TimeZoneID' AND t.name = 'int')
BEGIN
    ALTER TABLE dbo.FW_Registration ALTER COLUMN TimeZoneID smallint NULL;
    PRINT 'FW_Registration.TimeZoneID narrowed to smallint';
END
ELSE
    PRINT 'FW_Registration.TimeZoneID is already smallint';
GO

/* 2. The two foreign keys. */
IF OBJECT_ID('dbo.FK_FW_Registration_FW_TimeZones', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.FW_Registration
      ADD CONSTRAINT FK_FW_Registration_FW_TimeZones
      FOREIGN KEY (TimeZoneID) REFERENCES dbo.FW_TimeZones (TimeZoneID);
    PRINT 'Added FK_FW_Registration_FW_TimeZones';
END
ELSE
    PRINT 'FK_FW_Registration_FW_TimeZones already exists';

IF OBJECT_ID('dbo.FK_FW_Users_FW_TimeZones', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.FW_Users
      ADD CONSTRAINT FK_FW_Users_FW_TimeZones
      FOREIGN KEY (TimeZoneID) REFERENCES dbo.FW_TimeZones (TimeZoneID);
    PRINT 'Added FK_FW_Users_FW_TimeZones';
END
ELSE
    PRINT 'FK_FW_Users_FW_TimeZones already exists';

/* 3. The primary key still carries the table's old name. */
IF EXISTS (SELECT 1 FROM sys.key_constraints
            WHERE name = 'PK_TimeZone' AND parent_object_id = OBJECT_ID('dbo.FW_TimeZones'))
BEGIN
    EXEC sp_rename 'dbo.PK_TimeZone', 'PK_FW_TimeZones', 'OBJECT';
    PRINT 'Renamed PK_TimeZone to PK_FW_TimeZones';
END
GO

SELECT 'FOREIGN KEYS INTO FW_TimeZones' AS Area,
       fk.name AS Constraint_Name, OBJECT_NAME(fk.parent_object_id) AS FromTable, cp.name AS FromColumn
  FROM sys.foreign_keys fk
  JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
  JOIN sys.columns cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
 WHERE fk.referenced_object_id = OBJECT_ID('dbo.FW_TimeZones');

SELECT 'PRIMARY KEY' AS Area, name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.FW_TimeZones');
