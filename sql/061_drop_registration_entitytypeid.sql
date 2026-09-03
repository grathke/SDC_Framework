/*
    061_drop_registration_entitytypeid.sql
    ==============================================================================================
    Drops dbo.FW_Registration.EntityTypeID and the field-permission row that describes it.

    059 removed FW_EntityType. This column held its key, so it became a number pointing at a table
    that no longer exists - NOT NULL, carrying 4 and 3 for the two registrations, and read by
    nothing. It was deliberately left out of 059 because dropping a column from a live table is its
    own decision rather than something to fold into a table drop.

    Nothing stands in the way, all confirmed rather than assumed: no foreign key, no default, no
    index, no check constraint, and no line of VB names EntityType anywhere. It appears on no page
    as a control - only in the Roles field-permission list, by virtue of the FW_RoleFields row this
    script also removes.

    The two go together. Drop the column and leave the row, and the field list offers a permission
    for a column that does not exist; drop the row and leave the column, and the column becomes
    invisible to the permission system rather than gone.

    IRREVERSIBLE, though the values are recorded here and the pre-removal backup still holds them:
        RegistrationID 1 -> EntityTypeID 4
        RegistrationID 2 -> EntityTypeID 3
        WX_Framework_before_purge_20260903_174332.bak

    Safe to run more than once.
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- 1. The field-permission row. Anchored, so nothing else named Entity is caught.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_RoleFields
WHERE  FileLink = 'FW_Registration.EntityTypeID';

PRINT CONCAT('FW_RoleFields rows deleted: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 2. Refuse if anything has come to depend on the column since this was written.
------------------------------------------------------------------------------------------------
IF EXISTS (
    SELECT 1
    FROM   sys.foreign_key_columns AS fkc
           JOIN sys.columns AS c
             ON c.object_id = fkc.parent_object_id
            AND c.column_id = fkc.parent_column_id
    WHERE  fkc.parent_object_id = OBJECT_ID('dbo.FW_Registration')
      AND  c.name = 'EntityTypeID'
)
BEGIN
    PRINT '*** STOPPED - a foreign key now uses FW_Registration.EntityTypeID. ***';
    ROLLBACK TRANSACTION;
    RETURN;
END

------------------------------------------------------------------------------------------------
-- 3. The column.
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.FW_Registration', 'EntityTypeID') IS NOT NULL
BEGIN
    ALTER TABLE dbo.FW_Registration DROP COLUMN EntityTypeID;
    PRINT 'Dropped FW_Registration.EntityTypeID';
END
ELSE
BEGIN
    PRINT 'FW_Registration.EntityTypeID already gone.';
END

COMMIT TRANSACTION;

PRINT '';
PRINT '=== Any FW_RoleFields row whose column no longer exists (empty is the goal) ===';

SELECT FileLink
FROM   dbo.FW_RoleFields
WHERE  CHARINDEX('.', FileLink) > 1
  AND  OBJECT_ID('dbo.' + LEFT(FileLink, CHARINDEX('.', FileLink) - 1)) IS NOT NULL
  AND  COL_LENGTH('dbo.' + LEFT(FileLink, CHARINDEX('.', FileLink) - 1),
                  SUBSTRING(FileLink, CHARINDEX('.', FileLink) + 1, 200)) IS NULL
ORDER  BY FileLink;
