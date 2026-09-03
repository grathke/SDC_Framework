/*
    060_drop_legacy_broken_objects.sql
    ==============================================================================================
    Drops eleven views and stored procedures that are broken and unreachable.

    Every one of them references tables that do not exist and have not for a long time: Entity,
    Users, Roles, Note, Attachment, Gender, Registration, ImportantDatesExpanded, PhoneType,
    Country, Tags, EntityTagsMM, AuditTrail, AuditTables, AuditColumns, AuditStatus. Note the
    names - all unprefixed. These predate the FW_ convention and were left behind when the tables
    they read were renamed or replaced.

    They were already broken before 059 dropped FW_Entity. Not one of them named FW_Entity; they
    name the older, unprefixed Entity. Confirmed through sys.sql_expression_dependencies, which
    lists a NULL referenced_id for each - SQL Server itself cannot resolve what they point at.

    None is called from the application. Every name was searched across the VB source.

    ----------------------------------------------------------------------------------------------
    WHY DROP RATHER THAN LEAVE
    ----------------------------------------------------------------------------------------------
    A broken object is worse than a missing one. sp_CreateRegistration is the clearest case: the
    name promises exactly the thing somebody would reach for when adding a registration by hand,
    and running it fails on dbo.Roles. Leaving it is leaving a trap with an inviting label.

    ----------------------------------------------------------------------------------------------
    RECOVERY
    ----------------------------------------------------------------------------------------------
    Definitions were scripted out before this ran, and the database was backed up:
        WX_Framework_before_purge_20260903_174332.bak

    ----------------------------------------------------------------------------------------------
    DELIBERATELY NOT DROPPED
    ----------------------------------------------------------------------------------------------
    Alive and called from the application - leave them alone:
        UserAccess, vw_FW_CurrentUser, vw_FW_UserRoles,
        FW_HD_GetAdminDashboard, usp_FW_AccessDiagnostic, usp_FW_ApplyAccessDiagnosticChanges

    fn_GenerateRandomCode and vw_RandomGuid resolve correctly and are not broken. They are merely
    orphaned: the function's only consumer was FW_Entity.RecordLocator's default, dropped in 059.
    Harmless where they sit, and a separate decision.

    Safe to run more than once.
*/

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- Views
------------------------------------------------------------------------------------------------
DROP VIEW IF EXISTS dbo.VWActiveUsers;              -- reads dbo.users
DROP VIEW IF EXISTS dbo.vwAttachment;               -- reads dbo.Attachment, dbo.Users
DROP VIEW IF EXISTS dbo.vwEntities;                 -- reads dbo.Entity, Country, Gender, PhoneType, Tags, EntityTagsMM, Users
DROP VIEW IF EXISTS dbo.vwImportantDatesExpanded;   -- reads dbo.Entity, dbo.ImportantDatesExpanded
DROP VIEW IF EXISTS dbo.vwNote;                     -- reads dbo.Note, dbo.Users
DROP VIEW IF EXISTS dbo.vwPhoneType;                -- reads dbo.PhoneType

PRINT 'Views dropped.';

------------------------------------------------------------------------------------------------
-- Stored procedures
------------------------------------------------------------------------------------------------
DROP PROCEDURE IF EXISTS dbo.LoadAuditTables;               -- AuditColumns, AuditStatus, AuditTables
DROP PROCEDURE IF EXISTS dbo.sp_CreateRegistration;         -- Gender, Registration, RoleDetail, Roles, UserRoles, Users
DROP PROCEDURE IF EXISTS dbo.sp_LoadAuditTrailExpanded;     -- AuditTrail and friends, RoleFields, Roles
DROP PROCEDURE IF EXISTS dbo.sp_LoadImportantDatesExpanded; -- Entity, Note, Registration, RoleFields, Roles
DROP PROCEDURE IF EXISTS dbo.sp_SaveEntityTagsMM;           -- EntityTagsMM

PRINT 'Procedures dropped.';

COMMIT TRANSACTION;

------------------------------------------------------------------------------------------------
-- What is left, and whether any of it still fails to resolve
------------------------------------------------------------------------------------------------
PRINT '';
PRINT '=== REMAINING PROGRAMMABLE OBJECTS ===';

SELECT o.type_desc AS ObjType, o.name AS ObjName
FROM   sys.objects AS o
WHERE  o.type IN ('V', 'P', 'FN', 'IF', 'TF', 'TR')
ORDER  BY o.type_desc, o.name;

PRINT '';
PRINT '=== ANY REFERENCE THAT STILL DOES NOT RESOLVE (empty is the goal) ===';

SELECT OBJECT_NAME(d.referencing_id) AS ReferencingObject,
       d.referenced_entity_name      AS MissingObject
FROM   sys.sql_expression_dependencies AS d
WHERE  d.referenced_id IS NULL
  AND  d.referenced_entity_name IS NOT NULL
  AND  d.is_ambiguous = 0
ORDER  BY ReferencingObject, MissingObject;
