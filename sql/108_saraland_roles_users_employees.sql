/*
    108_saraland_roles_users_employees.sql

    Gives registration 2 (SARALAND) the same shape as registration 1: the same five roles, their
    FW_RoleDetails and FW_RoleFields, and three people with the same role assignments.

    Roles are matched by NAME, never by ID - the two registrations number their roles differently.

    PasswordHash is NOT written here. It is HMACSHA512 keyed by the UserId, which does not exist
    until the row is inserted, and the stored form is the UTF-16 string of those bytes. A second
    step computes it. Until then these logins cannot be used, which is the safe way round.

    Re-runnable: every insert is guarded, so a second run adds nothing.
*/

-- Required: FW_Users and FW_Employees carry indexed computed columns, and an insert is refused
-- without it.
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @src INT = 1;   -- DEVELOPMENT TEAM
DECLARE @dst INT = 2;   -- SARALAND
DECLARE @by  INT = 2;   -- acting user (Glenn in registration 1)

BEGIN TRANSACTION;

/* ---------- 1. Roles ---------- */

-- 'ApplicationAdmin' in registration 2 is registration 1's 'Application Admin'.
UPDATE dbo.FW_Roles
   SET RoleName = 'Application Admin'
 WHERE RegistrationID = @dst AND RoleName = 'ApplicationAdmin';

-- Any role registration 1 has and registration 2 does not.
INSERT INTO dbo.FW_Roles (RegistrationID, RoleName, CA_CanChange,
                          Can_Create, Can_Read, Can_Update, Can_Delete, Can_Export, Can_Import,
                          Can_UseQBE, Can_ViewAllRecords, Can_ViewOnlyMyRecords,
                          DisplayOrder, IsActive,
                          Typ_AppAdmin, Typ_CompanyAdmin, Typ_RW, Typ_RO, Typ_User, Typ_OnlyMyRecords,
                          CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, s.RoleName, s.CA_CanChange,
       s.Can_Create, s.Can_Read, s.Can_Update, s.Can_Delete, s.Can_Export, s.Can_Import,
       s.Can_UseQBE, s.Can_ViewAllRecords, s.Can_ViewOnlyMyRecords,
       s.DisplayOrder, 1,
       s.Typ_AppAdmin, s.Typ_CompanyAdmin, s.Typ_RW, s.Typ_RO, s.Typ_User, s.Typ_OnlyMyRecords,
       @by, GETDATE(), @by, GETDATE()
  FROM dbo.FW_Roles s
 WHERE s.RegistrationID = @src
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_Roles d
                    WHERE d.RegistrationID = @dst AND d.RoleName = s.RoleName);

-- The ones already there were inactive and carried none of the type flags, which is what decides
-- whether a role is App Admin, Company Admin, read-write and so on.
UPDATE d
   SET d.CA_CanChange = s.CA_CanChange,
       d.Can_Create = s.Can_Create, d.Can_Read = s.Can_Read,
       d.Can_Update = s.Can_Update, d.Can_Delete = s.Can_Delete,
       d.Can_Export = s.Can_Export, d.Can_Import = s.Can_Import,
       d.Can_UseQBE = s.Can_UseQBE,
       d.Can_ViewAllRecords = s.Can_ViewAllRecords,
       d.Can_ViewOnlyMyRecords = s.Can_ViewOnlyMyRecords,
       d.DisplayOrder = s.DisplayOrder,
       d.IsActive = 1,
       d.Typ_AppAdmin = s.Typ_AppAdmin, d.Typ_CompanyAdmin = s.Typ_CompanyAdmin,
       d.Typ_RW = s.Typ_RW, d.Typ_RO = s.Typ_RO,
       d.Typ_User = s.Typ_User, d.Typ_OnlyMyRecords = s.Typ_OnlyMyRecords,
       d.UpdatedBy = @by, d.UpdatedOn = GETDATE()
  FROM dbo.FW_Roles d
  JOIN dbo.FW_Roles s ON s.RegistrationID = @src AND s.RoleName = d.RoleName
 WHERE d.RegistrationID = @dst;

/* Name -> id, both sides. Everything below joins through this. */
DECLARE @roleMap TABLE (RoleName VARCHAR(100) NOT NULL PRIMARY KEY, SrcID INT NOT NULL, DstID INT NOT NULL);
INSERT INTO @roleMap (RoleName, SrcID, DstID)
SELECT s.RoleName, s.ID, d.ID
  FROM dbo.FW_Roles s
  JOIN dbo.FW_Roles d ON d.RegistrationID = @dst AND d.RoleName = s.RoleName
 WHERE s.RegistrationID = @src;

/* ---------- 2. Role details and field permissions ---------- */

-- SchemaID points at FW_RoleSchema, which is one row per table and not per registration, so it
-- copies across unchanged. RoleDetailID and FW_RoleSchemaID are null throughout and are left so.
INSERT INTO dbo.FW_RoleDetails (RegistrationID, RoleID, SchemaID, CA_CanChange,
                                Can_Create, Can_Read, Can_Update, Can_Delete, Can_Export, Can_Import,
                                Can_UseQBE, Can_ViewAllRecords, Can_ViewOnlyMyRecords,
                                DB_Table, Table_Alias, OverrideCaption,
                                Expand_QBE, IsActive, MaxRecords, StartEmpty, DeletedFlag,
                                CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, m.DstID, s.SchemaID, s.CA_CanChange,
       s.Can_Create, s.Can_Read, s.Can_Update, s.Can_Delete, s.Can_Export, s.Can_Import,
       s.Can_UseQBE, s.Can_ViewAllRecords, s.Can_ViewOnlyMyRecords,
       s.DB_Table, s.Table_Alias, s.OverrideCaption,
       s.Expand_QBE, s.IsActive, s.MaxRecords, s.StartEmpty, 0,
       @by, GETDATE(), @by, GETDATE()
  FROM dbo.FW_RoleDetails s
  JOIN @roleMap m ON m.SrcID = s.RoleID
 WHERE s.RegistrationID = @src
   AND ISNULL(s.DeletedFlag, 0) = 0
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_RoleDetails d
                    WHERE d.RegistrationID = @dst AND d.RoleID = m.DstID AND d.DB_Table = s.DB_Table);

-- RoleDetailID is a real reference and must be remapped: joined source -> its table -> the
-- registration 2 detail row for the same role and table. Copied straight across it would point
-- every Saraland field permission at a Development Team row.
INSERT INTO dbo.FW_RoleFields (RegistrationID, RoleID, RoleDetailID, SchemaID, CA_CanChange,
                               Can_Create, Can_Read, Can_Update,
                               FieldName, FriendlyFieldName, TableName, FileLink,
                               IsActive, IsRequired, IsUnique, Make_Invisible, OrderBy,
                               OverrideCaption, DeletedFlag,
                               CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, m.DstID, dd.ID, s.SchemaID, s.CA_CanChange,
       s.Can_Create, s.Can_Read, s.Can_Update,
       s.FieldName, s.FriendlyFieldName, s.TableName, s.FileLink,
       s.IsActive, s.IsRequired, s.IsUnique, s.Make_Invisible, s.OrderBy,
       s.OverrideCaption, 0,
       @by, GETDATE(), @by, GETDATE()
  FROM dbo.FW_RoleFields s
  JOIN @roleMap m ON m.SrcID = s.RoleID
  JOIN dbo.FW_RoleDetails sd ON sd.ID = s.RoleDetailID
  JOIN dbo.FW_RoleDetails dd ON dd.RegistrationID = @dst
                            AND dd.RoleID = m.DstID
                            AND dd.DB_Table = sd.DB_Table
 WHERE s.RegistrationID = @src
   AND ISNULL(s.DeletedFlag, 0) = 0
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_RoleFields d
                    WHERE d.RegistrationID = @dst AND d.RoleID = m.DstID AND d.FileLink = s.FileLink);

/* ---------- 3. The three people ---------- */

DECLARE @people TABLE (FirstName VARCHAR(100), LastName VARCHAR(100), Login VARCHAR(200), SrcEmployeeID INT);
INSERT INTO @people VALUES
 ('Glenn', 'Rathke',  'glenn@saraland.org', 2),
 ('Sandy', 'Litaker', 'sandy@saraland.org', 1),
 ('Alan',  'Sawyer',  'alan@saraland.org',  3);

-- FirstLast and LastFirst are computed; the database derives them.
INSERT INTO dbo.FW_Users (RegistrationID, FirstName, LastName,
                          UserName, Email, [Password], IsActive,
                          CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, p.FirstName, p.LastName,
       p.Login, p.Login, '#####', 1,
       @by, GETDATE(), @by, GETDATE()
  FROM @people p
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Users u WHERE u.UserName = p.Login);

INSERT INTO dbo.FW_Employees (RegistrationID, UserId, FirstName, LastName,
                              UserName, Email, IsActive, DeletedFlag,
                              CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, u.UserId, p.FirstName, p.LastName,
       p.Login, p.Login, 1, 0,
       @by, GETDATE(), @by, GETDATE()
  FROM @people p
  JOIN dbo.FW_Users u ON u.UserName = p.Login AND u.RegistrationID = @dst
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserName = p.Login AND e.RegistrationID = @dst);

/* Same roles each person holds in registration 1, mapped by role name. */
INSERT INTO dbo.FW_EmployeeRoles (RegistrationID, EmployeeID, RoleID, DisplayOrder, IsActive, DeletedFlag,
                                  CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
SELECT @dst, e.EmployeeID, m.DstID, sr.DisplayOrder, 1, 0,
       @by, GETDATE(), @by, GETDATE()
  FROM @people p
  JOIN dbo.FW_Employees e ON e.UserName = p.Login AND e.RegistrationID = @dst
  JOIN dbo.FW_EmployeeRoles sr ON sr.RegistrationID = @src AND sr.EmployeeID = p.SrcEmployeeID
                              AND ISNULL(sr.DeletedFlag, 0) = 0
  JOIN dbo.FW_Roles srole ON srole.ID = sr.RoleID
  JOIN @roleMap m ON m.RoleName = srole.RoleName
 WHERE NOT EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles d
                    WHERE d.RegistrationID = @dst AND d.EmployeeID = e.EmployeeID AND d.RoleID = m.DstID);

COMMIT TRANSACTION;

/* ---------- What was built ---------- */

SELECT 'ROLES' AS Area, RoleName, ID, ISNULL(IsActive,0) AS Act
  FROM dbo.FW_Roles WHERE RegistrationID = 2 ORDER BY DisplayOrder, RoleName;

SELECT 'COUNTS' AS Area,
       (SELECT COUNT(*) FROM dbo.FW_RoleDetails WHERE RegistrationID = 2) AS RoleDetails,
       (SELECT COUNT(*) FROM dbo.FW_RoleFields  WHERE RegistrationID = 2) AS RoleFields,
       (SELECT COUNT(*) FROM dbo.FW_Users       WHERE RegistrationID = 2) AS Users,
       (SELECT COUNT(*) FROM dbo.FW_Employees   WHERE RegistrationID = 2) AS Employees,
       (SELECT COUNT(*) FROM dbo.FW_EmployeeRoles WHERE RegistrationID = 2) AS EmployeeRoles;

SELECT 'NEEDS HASH' AS Area, u.UserId, u.UserName
  FROM dbo.FW_Users u
 WHERE u.RegistrationID = 2 AND ISNULL(u.PasswordHash, '') = ''
 ORDER BY u.UserId;
