/*
    059_remove_fw_entity.sql
    ==============================================================================================
    Removes FW_Entity and everything the framework recorded about it. The pages that read it -
    Entity_B/_U, EntityX_B/_U, EntityY_B/_U - and their menu and dashboard buttons are removed in
    the same change.

    FW_Entity was the framework's demonstration table. Nothing in the application needs it, and
    keeping a table alive to be an example costs a foreign key on every user delete and a hardcoded
    fallback in the middle of the shared browse query.

    IRREVERSIBLE. A backup was taken before this was first run:
        WX_Framework_before_purge_20260903_174332.bak

    ----------------------------------------------------------------------------------------------
    FW_EntityType IS A DIFFERENT TABLE, AND ALSO GOES
    ----------------------------------------------------------------------------------------------
    A LIKE '%Entity%' matches both, which is why every predicate below is anchored rather than
    fuzzy. They are removed as two separate, explicit acts rather than one loose pattern - the
    difference matters if this script is ever adapted for something else.

    FW_EntityType holds five rows, nothing references it by foreign key, and no line of VB names
    it. It goes too, along with its six FW_RoleFields rows and its one FW_RoleDetails row.

    FW_Registration.EntityTypeID is deliberately LEFT IN PLACE. It is NOT NULL, two registrations
    carry a value, and no code reads it. Dropping a column from a live table is its own decision and
    is not smuggled in here. It is now a number pointing at a table that no longer exists, so it
    should be dropped - as its own change, on purpose.

    ----------------------------------------------------------------------------------------------
    WHAT IT TOUCHES  (counts as of 2026-09-03)
    ----------------------------------------------------------------------------------------------
      FW_RoleFields        155 rows  FileLink LIKE 'FW_Entity.%'
                           + 6 rows  FileLink LIKE 'FW_EntityType.%'
      FW_RoleTables          3 rows  Entity_B, EntityX_B, EntityY_B
      FW_RoleDetails         3 rows  DB_Table = 'FW_Entity'
                           + 1 row   DB_Table = 'FW_EntityType'
      FW_DashboardLayouts    2 rows  'entity' and 'ActionKey_EntityY_B'
      Foreign keys           2       both declared ON FW_Entity by sql/049 and sql/056
      Tables                 2       dbo.FW_Entity (1027 rows), dbo.FW_EntityType (5 rows)

    Safe to run more than once: every statement matches nothing on a second run.
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- 1. Field permissions. Anchored to 'FW_Entity.' so FW_EntityType is untouched.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_RoleFields
WHERE  FileLink LIKE 'FW[_]Entity.%';

PRINT CONCAT('FW_RoleFields rows deleted (FW_Entity): ', @@ROWCOUNT);

DELETE FROM dbo.FW_RoleFields
WHERE  FileLink LIKE 'FW[_]EntityType.%';

PRINT CONCAT('FW_RoleFields rows deleted (FW_EntityType): ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 2. Page rows. Named explicitly rather than matched, so a future EntityType page cannot be hit.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_RoleTables
WHERE  WindowOrPage IN ('Entity_B', 'Entity_U', 'EntityX_B', 'EntityX_U', 'EntityY_B', 'EntityY_U');

PRINT CONCAT('FW_RoleTables rows deleted: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 3. Table captions and role detail overrides.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_RoleDetails
WHERE  DB_Table IN ('FW_Entity', 'FW_EntityType');

PRINT CONCAT('FW_RoleDetails rows deleted: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 4. Tile positions and chosen pictures, on the ribbon and the application dashboard.
------------------------------------------------------------------------------------------------
DELETE FROM dbo.FW_DashboardLayouts
WHERE  ActionKey IN ('entity', 'ActionKey_EntityX_B', 'ActionKey_EntityY_B');

PRINT CONCAT('FW_DashboardLayouts rows deleted: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 5. The foreign keys declared ON FW_Entity, by name and only if present.
--
--    FK_FW_ENTITY_AssignedManagerID_FW_Users is the one that made deleting a user fail: it is why
--    the table had to go before FW_Users could be cleaned up.
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_ENTITY_AssignedManagerID_FW_Users')
BEGIN
    ALTER TABLE dbo.FW_Entity DROP CONSTRAINT FK_FW_ENTITY_AssignedManagerID_FW_Users;
    PRINT 'Dropped FK_FW_ENTITY_AssignedManagerID_FW_Users';
END

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_ENTITY_GenderID_FW_GENDER')
BEGIN
    ALTER TABLE dbo.FW_Entity DROP CONSTRAINT FK_FW_ENTITY_GenderID_FW_GENDER;
    PRINT 'Dropped FK_FW_ENTITY_GenderID_FW_GENDER';
END

------------------------------------------------------------------------------------------------
-- 6. Refuse to drop the table if anything still points at it.
--
--    Nothing did when this was written. The check is here because the cost of being wrong is a
--    dropped table, and the cost of asking is one query.
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('dbo.FW_Entity'))
BEGIN
    PRINT '*** STOPPED - something still references FW_Entity: ***';

    SELECT fk.name AS ConstraintName,
           OBJECT_NAME(fk.parent_object_id) AS ReferencingTable
    FROM   sys.foreign_keys AS fk
    WHERE  fk.referenced_object_id = OBJECT_ID('dbo.FW_Entity');

    ROLLBACK TRANSACTION;
    RETURN;
END

------------------------------------------------------------------------------------------------
-- 7. The table.
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.FW_Entity', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.FW_Entity;
    PRINT 'Dropped dbo.FW_Entity';
END

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('dbo.FW_EntityType'))
BEGIN
    PRINT '*** STOPPED - something still references FW_EntityType. ***';
    ROLLBACK TRANSACTION;
    RETURN;
END

IF OBJECT_ID('dbo.FW_EntityType', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.FW_EntityType;
    PRINT 'Dropped dbo.FW_EntityType';
END

COMMIT TRANSACTION;

PRINT '';
PRINT 'Left behind on purpose - FW_Registration.EntityTypeID, now pointing at nothing:';
SELECT RegistrationID, EntityTypeID FROM dbo.FW_Registration WHERE EntityTypeID IS NOT NULL;
