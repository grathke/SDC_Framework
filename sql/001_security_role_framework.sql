SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*
    Framework security schema for role/profile-driven UI access.
    This matches the role maintenance concept in your screenshots:
    - Role master
    - Available tables master
    - Role x table access matrix
    - User x role assignment
    - Optional role x table x field access (for future _U page behavior)
*/

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID('dbo.FrameworkRole', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FrameworkRole
        (
            RoleId INT IDENTITY(1,1) NOT NULL,
            RoleLevel INT NOT NULL,
            RoleName NVARCHAR(120) NOT NULL,
            RoleCategory NVARCHAR(120) NULL,
            DisplayOrder INT NOT NULL CONSTRAINT DF_FrameworkRole_DisplayOrder DEFAULT (0),
            RegistrationName NVARCHAR(160) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_FrameworkRole_IsActive DEFAULT (1),
            DeletedFlag BIT NOT NULL CONSTRAINT DF_FrameworkRole_DeletedFlag DEFAULT (0),
            DeletedBy INT NULL,
            DeletedOn DATETIME2(0) NULL,
            CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FrameworkRole_CreatedOn DEFAULT (SYSUTCDATETIME()),
            CreatedBy INT NULL,
            UpdatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FrameworkRole_UpdatedOn DEFAULT (SYSUTCDATETIME()),
            UpdatedBy INT NULL,
            CONSTRAINT PK_FrameworkRole PRIMARY KEY CLUSTERED (RoleId),
            CONSTRAINT UQ_FrameworkRole_RoleLevel UNIQUE (RoleLevel),
            CONSTRAINT UQ_FrameworkRole_RoleName UNIQUE (RoleName)
        );
    END;

    IF OBJECT_ID('dbo.FrameworkTable', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FrameworkTable
        (
            TableId INT IDENTITY(1,1) NOT NULL,
            TableKey NVARCHAR(160) NOT NULL,
            PhysicalTableName NVARCHAR(256) NULL,
            DisplayName NVARCHAR(160) NOT NULL,
            ModuleName NVARCHAR(120) NULL,
            IsActive BIT NOT NULL CONSTRAINT DF_FrameworkTable_IsActive DEFAULT (1),
            DeletedFlag BIT NOT NULL CONSTRAINT DF_FrameworkTable_DeletedFlag DEFAULT (0),
            DeletedBy INT NULL,
            DeletedOn DATETIME2(0) NULL,
            SortOrder INT NOT NULL CONSTRAINT DF_FrameworkTable_SortOrder DEFAULT (0),
            CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FrameworkTable_CreatedOn DEFAULT (SYSUTCDATETIME()),
            UpdatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FrameworkTable_UpdatedOn DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_FrameworkTable PRIMARY KEY CLUSTERED (TableId),
            CONSTRAINT UQ_FrameworkTable_TableKey UNIQUE (TableKey)
        );
    END;

    IF OBJECT_ID('dbo.RoleTableAccess', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.RoleTableAccess
        (
            RoleId INT NOT NULL,
            TableId INT NOT NULL,
            CanCreate BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanCreate DEFAULT (0),
            CanReadOnly BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanReadOnly DEFAULT (0),
            CanUpdate BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanUpdate DEFAULT (0),
            CanDelete BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanDelete DEFAULT (0),
            CanImport BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanImport DEFAULT (0),
            CanViewAllRecords BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanViewAllRecords DEFAULT (0),
            CanViewOnlyMy BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanViewOnlyMy DEFAULT (0),
            CanUseQbe BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanUseQbe DEFAULT (0),
            CanExpandQbe BIT NOT NULL CONSTRAINT DF_RoleTableAccess_CanExpandQbe DEFAULT (0),
            IsActive BIT NOT NULL CONSTRAINT DF_RoleTableAccess_IsActive DEFAULT (1),
            DeletedFlag BIT NOT NULL CONSTRAINT DF_RoleTableAccess_DeletedFlag DEFAULT (0),
            DeletedBy INT NULL,
            DeletedOn DATETIME2(0) NULL,
            UpdatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_RoleTableAccess_UpdatedOn DEFAULT (SYSUTCDATETIME()),
            UpdatedBy INT NULL,
            CONSTRAINT PK_RoleTableAccess PRIMARY KEY CLUSTERED (RoleId, TableId),
            CONSTRAINT FK_RoleTableAccess_FrameworkRole FOREIGN KEY (RoleId) REFERENCES dbo.FrameworkRole(RoleId),
            CONSTRAINT FK_RoleTableAccess_FrameworkTable FOREIGN KEY (TableId) REFERENCES dbo.FrameworkTable(TableId)
        );
    END;

    IF OBJECT_ID('dbo.UserRoleAssignment', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.UserRoleAssignment
        (
            UserRoleAssignmentId INT IDENTITY(1,1) NOT NULL,
            UserId INT NOT NULL,
            RoleId INT NOT NULL,
            IsPrimaryRole BIT NOT NULL CONSTRAINT DF_UserRoleAssignment_IsPrimaryRole DEFAULT (0),
            IsActive BIT NOT NULL CONSTRAINT DF_UserRoleAssignment_IsActive DEFAULT (1),
            DeletedFlag BIT NOT NULL CONSTRAINT DF_UserRoleAssignment_DeletedFlag DEFAULT (0),
            DeletedBy INT NULL,
            DeletedOn DATETIME2(0) NULL,
            EffectiveFrom DATETIME2(0) NULL,
            EffectiveTo DATETIME2(0) NULL,
            CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_UserRoleAssignment_CreatedOn DEFAULT (SYSUTCDATETIME()),
            UpdatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_UserRoleAssignment_UpdatedOn DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_UserRoleAssignment PRIMARY KEY CLUSTERED (UserRoleAssignmentId),
            CONSTRAINT FK_UserRoleAssignment_FrameworkRole FOREIGN KEY (RoleId) REFERENCES dbo.FrameworkRole(RoleId)
        );

        CREATE UNIQUE NONCLUSTERED INDEX UX_UserRoleAssignment_UserRole
            ON dbo.UserRoleAssignment(UserId, RoleId)
            WHERE IsActive = 1;

        CREATE NONCLUSTERED INDEX IX_UserRoleAssignment_UserPrimary
            ON dbo.UserRoleAssignment(UserId, IsPrimaryRole, IsActive);
    END;

    IF OBJECT_ID('dbo.RoleFieldAccess', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.RoleFieldAccess
        (
            RoleFieldAccessId INT IDENTITY(1,1) NOT NULL,
            RoleId INT NOT NULL,
            TableId INT NOT NULL,
            FieldName NVARCHAR(160) NOT NULL,
            CanCreate BIT NOT NULL CONSTRAINT DF_RoleFieldAccess_CanCreate DEFAULT (0),
            CanUpdate BIT NOT NULL CONSTRAINT DF_RoleFieldAccess_CanUpdate DEFAULT (0),
            CanReadOnly BIT NOT NULL CONSTRAINT DF_RoleFieldAccess_CanReadOnly DEFAULT (0),
            HiddenFlag BIT NOT NULL CONSTRAINT DF_RoleFieldAccess_HiddenFlag DEFAULT (0),
            BrActive BIT NOT NULL CONSTRAINT DF_RoleFieldAccess_BrActive DEFAULT (0),
            WhenDisplayed BIT NOT NULL CONSTRAINT DF_RoleFieldAccess_WhenDisplayed DEFAULT (0),
            WhenSaved BIT NOT NULL CONSTRAINT DF_RoleFieldAccess_WhenSaved DEFAULT (0),
            Ord INT NULL,
            DeletedFlag BIT NOT NULL CONSTRAINT DF_RoleFieldAccess_DeletedFlag DEFAULT (0),
            DeletedBy INT NULL,
            DeletedOn DATETIME2(0) NULL,
            UpdatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_RoleFieldAccess_UpdatedOn DEFAULT (SYSUTCDATETIME()),
            UpdatedBy INT NULL,
            CONSTRAINT PK_RoleFieldAccess PRIMARY KEY CLUSTERED (RoleFieldAccessId),
            CONSTRAINT FK_RoleFieldAccess_FrameworkRole FOREIGN KEY (RoleId) REFERENCES dbo.FrameworkRole(RoleId),
            CONSTRAINT FK_RoleFieldAccess_FrameworkTable FOREIGN KEY (TableId) REFERENCES dbo.FrameworkTable(TableId),
            CONSTRAINT UQ_RoleFieldAccess UNIQUE (RoleId, TableId, FieldName)
        );
    END;

    IF OBJECT_ID('dbo.FW_RoleTables', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FW_RoleTables
        (
            ID INT IDENTITY(1,1) NOT NULL,
            RegistrationID INT NOT NULL,
            WindowOrPage NVARCHAR(160) NOT NULL,
            DB_Table NVARCHAR(256) NOT NULL,
            Table_Alias NVARCHAR(256) NOT NULL,
            Table_SQL NVARCHAR(MAX) NULL,
            CreatedBy INT NULL,
            CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_RoleTables_CreatedOn DEFAULT (GETDATE()),
            DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_RoleTables_DeletedFlag DEFAULT (0),
            DeletedBy INT NULL,
            DeletedOn DATETIME2(0) NULL,
            CONSTRAINT PK_FW_RoleTables PRIMARY KEY CLUSTERED (ID),
            CONSTRAINT UQ_FW_RoleTables_RegWinPage UNIQUE (RegistrationID, WindowOrPage),
            INDEX IX_FW_RoleTables_RegTable ON RegistrationID, DB_Table
        );
    END;

    IF OBJECT_ID('dbo.FW_TableAliases', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FW_TableAliases
        (
            ID INT IDENTITY(1,1) NOT NULL,
            TableName NVARCHAR(256) NOT NULL,
            DisplayAlias NVARCHAR(256) NOT NULL,
            CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_TableAliases_CreatedOn DEFAULT (GETDATE()),
            DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_TableAliases_DeletedFlag DEFAULT (0),
            DeletedBy INT NULL,
            DeletedOn DATETIME2(0) NULL,
            CONSTRAINT PK_FW_TableAliases PRIMARY KEY CLUSTERED (ID),
            CONSTRAINT UQ_FW_TableAliases_TableName UNIQUE (TableName)
        );
    END;

    IF OBJECT_ID('dbo.vRoleTableAccess', 'V') IS NULL
    BEGIN
        EXEC('CREATE VIEW dbo.vRoleTableAccess AS SELECT 1 AS Placeholder;');
    END;

    EXEC('ALTER VIEW dbo.vRoleTableAccess AS
        SELECT
            r.RoleId,
            r.RoleLevel,
            r.RoleName,
            t.TableId,
            t.TableKey,
            t.PhysicalTableName,
            t.DisplayName,
            a.CanCreate,
            a.CanReadOnly,
            a.CanUpdate,
            a.CanDelete,
            a.CanImport,
            a.CanViewAllRecords,
            a.CanViewOnlyMy,
            a.CanUseQbe,
            a.CanExpandQbe,
            a.IsActive,
            a.UpdatedOn
        FROM dbo.RoleTableAccess a
        INNER JOIN dbo.FrameworkRole r ON r.RoleId = a.RoleId
        INNER JOIN dbo.FrameworkTable t ON t.TableId = a.TableId;
    ');

    /* Seed core roles from current convention (safe, idempotent) */
    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkRole WHERE RoleName = N'Application Admin')
        INSERT INTO dbo.FrameworkRole (RoleLevel, RoleName, RoleCategory, DisplayOrder, RegistrationName, IsActive)
        VALUES (1, N'Application Admin', N'Application Admin', 1, N'DEVELOPMENT TEAM', 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkRole WHERE RoleName = N'Account Admin')
        INSERT INTO dbo.FrameworkRole (RoleLevel, RoleName, RoleCategory, DisplayOrder, RegistrationName, IsActive)
        VALUES (2, N'Account Admin', N'Account Admin', 2, N'DEVELOPMENT TEAM', 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkRole WHERE RoleName = N'User RW')
        INSERT INTO dbo.FrameworkRole (RoleLevel, RoleName, RoleCategory, DisplayOrder, RegistrationName, IsActive)
        VALUES (3, N'User RW', N'User', 3, N'DEVELOPMENT TEAM', 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkRole WHERE RoleName = N'User Owner Only')
        INSERT INTO dbo.FrameworkRole (RoleLevel, RoleName, RoleCategory, DisplayOrder, RegistrationName, IsActive)
        VALUES (4, N'User Owner Only', N'User', 4, N'DEVELOPMENT TEAM', 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkRole WHERE RoleName = N'User RO')
        INSERT INTO dbo.FrameworkRole (RoleLevel, RoleName, RoleCategory, DisplayOrder, RegistrationName, IsActive)
        VALUES (5, N'User RO', N'User', 5, N'DEVELOPMENT TEAM', 1);

    /* Seed key framework tables used by the current menu/access mapping */
    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkTable WHERE TableKey = N'MESSAGING')
        INSERT INTO dbo.FrameworkTable (TableKey, PhysicalTableName, DisplayName, ModuleName, SortOrder)
        VALUES (N'MESSAGING', N'dbo.Messages', N'Messaging', N'Framework', 10);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkTable WHERE TableKey = N'ENTITY')
        INSERT INTO dbo.FrameworkTable (TableKey, PhysicalTableName, DisplayName, ModuleName, SortOrder)
        VALUES (N'ENTITY', N'dbo.Entity', N'Entity', N'Framework', 20);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkTable WHERE TableKey = N'ROLES')
        INSERT INTO dbo.FrameworkTable (TableKey, PhysicalTableName, DisplayName, ModuleName, SortOrder)
        VALUES (N'ROLES', N'dbo.FrameworkRole', N'Roles', N'Security', 30);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkTable WHERE TableKey = N'FRAMEWORK DASHBOARD')
        INSERT INTO dbo.FrameworkTable (TableKey, PhysicalTableName, DisplayName, ModuleName, SortOrder)
        VALUES (N'FRAMEWORK DASHBOARD', NULL, N'Framework Dashboard', N'Dashboard', 40);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkTable WHERE TableKey = N'REGISTRATION DASHBOARD')
        INSERT INTO dbo.FrameworkTable (TableKey, PhysicalTableName, DisplayName, ModuleName, SortOrder)
        VALUES (N'REGISTRATION DASHBOARD', NULL, N'Registration Dashboard', N'Dashboard', 50);

    IF NOT EXISTS (SELECT 1 FROM dbo.FrameworkTable WHERE TableKey = N'APPLICATION SETTINGS DASHBOARD')
        INSERT INTO dbo.FrameworkTable (TableKey, PhysicalTableName, DisplayName, ModuleName, SortOrder)
        VALUES (N'APPLICATION SETTINGS DASHBOARD', NULL, N'Application Settings Dashboard', N'Settings', 60);

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRAN;

    THROW;
END CATCH;
GO
