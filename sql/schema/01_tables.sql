-- Generated from WX_Framework on 2026-09-23 by scripts\dump-schema.ps1
-- Do not edit by hand. Regenerate instead.

IF OBJECT_ID('dbo.[FW_AuditTrail]', 'U') IS NULL
CREATE TABLE dbo.[FW_AuditTrail] (
    [UpdateAuditLogID] int IDENTITY(1,1) NOT NULL,
    [LoggedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_UpdateAuditLog_LoggedOn] DEFAULT (sysutcdatetime()),
    [RegistrationID] int NULL,
    [UserID] int NULL,
    [PageName] varchar(100) NOT NULL,
    [TableName] varchar(100) NULL,
    [OperationType] varchar(30) NOT NULL,
    [Phase] varchar(20) NOT NULL,
    [RecordKey] varchar(100) NULL,
    [SaveSucceeded] bit NULL,
    [SnapshotJson] varchar(MAX) NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_AuditLog_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_UpdateAuditLog] PRIMARY KEY CLUSTERED ([UpdateAuditLogID])
);
GO

IF OBJECT_ID('dbo.[FW_DashboardLayouts]', 'U') IS NULL
CREATE TABLE dbo.[FW_DashboardLayouts] (
    [DashboardLayoutID] int IDENTITY(1,1) NOT NULL,
    [DashboardName] nvarchar(128) NOT NULL,
    [ActionKey] nvarchar(128) NOT NULL,
    [GridRow] int NULL,
    [GridColumn] int NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_DashboardLayouts_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2 NULL,
    [RowVersion] rowversion,
    [IconFileName] nvarchar(260) NULL,
    CONSTRAINT [PK_FW_DashboardLayouts] PRIMARY KEY CLUSTERED ([DashboardLayoutID])
);
GO

IF OBJECT_ID('dbo.[FW_DashboardLayouts_Backup_20260906]', 'U') IS NULL
CREATE TABLE dbo.[FW_DashboardLayouts_Backup_20260906] (
    [DashboardLayoutID] int IDENTITY(1,1) NOT NULL,
    [DashboardName] nvarchar(128) NOT NULL,
    [ActionKey] nvarchar(128) NOT NULL,
    [GridRow] int NULL,
    [GridColumn] int NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL,
    [DeletedBy] int NULL,
    [DeletedOn] datetime2 NULL,
    [RowVersion] rowversion,
    [IconFileName] nvarchar(260) NULL
);
GO

IF OBJECT_ID('dbo.[FW_EmployeeRoles]', 'U') IS NULL
CREATE TABLE dbo.[FW_EmployeeRoles] (
    [UserRoleID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [RoleID] int NOT NULL,
    [DisplayOrder] int NULL,
    [IsActive] bit NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_UserRoles_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [EmployeeID] int NOT NULL,
    CONSTRAINT [PK_UserRoles] PRIMARY KEY CLUSTERED ([UserRoleID])
);
GO

IF OBJECT_ID('dbo.[FW_Employees]', 'U') IS NULL
CREATE TABLE dbo.[FW_Employees] (
    [EmployeeID] int IDENTITY(1,1) NOT NULL,
    [UserId] int NOT NULL,
    [RegistrationId] int NULL,
    [RegionID] int NULL,
    [IsActive] bit NULL CONSTRAINT [DF_Employees_ActiveStatus] DEFAULT ((1)),
    [FirstName] varchar(45) NOT NULL,
    [LastName] varchar(45) NOT NULL,
    [FirstLast] AS ((isnull([FirstName],'')+case when [FirstName] IS NOT NULL AND [LastName] IS NOT NULL then ' ' else '' end)+isnull([LastName],'')) PERSISTED,
    [LastFirst] AS ((isnull([LastName],'')+case when [LastName] IS NOT NULL AND [FirstName] IS NOT NULL then ', ' else '' end)+isnull([FirstName],'')) PERSISTED,
    [UserName] varchar(50) NOT NULL,
    [Address1] varchar(60) NULL,
    [Address2] varchar(60) NULL,
    [City] varchar(60) NULL,
    [State] nchar(25) NULL,
    [Zip] varchar(50) NULL,
    [Country] varchar(75) NULL,
    [GenderID] int NULL,
    [Email] varchar(100) NULL,
    [CellPhone] varchar(25) NULL,
    [HomePhone] varchar(25) NULL,
    [WorkPhone] varchar(25) NULL,
    [Extension] varchar(10) NULL,
    [BirthDate] date NULL,
    [HireDate] date NULL CONSTRAINT [DF_Employees_HireDate] DEFAULT (getdate()),
    [TerminationDate] date NULL,
    [DoNotSendEmail] int NULL CONSTRAINT [DF_Employees_DoNotSendEmail] DEFAULT ((0)),
    [DoNotSendPhone] int NULL CONSTRAINT [DF_Employees_DoNotSendPhone] DEFAULT ((0)),
    [Inspector] bit NULL CONSTRAINT [DF_Employees_Inspector] DEFAULT ((0)),
    [Notes] ntext NULL,
    [Photo] image NULL,
    [PhotoPath] varchar(255) NULL,
    [PrimaryJobId] int NULL,
    [PrimarySubJobId] int NULL,
    [Title] varchar(50) NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_Employees_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [AssignedManagerID] int NULL,
    [Password] varchar(50) NULL,
    [TimeZoneID] int NULL,
    [ReceivesHealthAlerts] bit NULL,
    CONSTRAINT [PK_Employees] PRIMARY KEY CLUSTERED ([EmployeeID]),
    CONSTRAINT [CK_Birthdate] CHECK ([BirthDate]<getdate())
);
GO

IF OBJECT_ID('dbo.[FW_ErrorLog]', 'U') IS NULL
CREATE TABLE dbo.[FW_ErrorLog] (
    [ErrorLogID] int IDENTITY(1,1) NOT NULL,
    [Fingerprint] char(32) NOT NULL,
    [ExceptionType] varchar(200) NOT NULL,
    [PageName] varchar(100) NULL,
    [Context] varchar(200) NULL,
    [Message] nvarchar(1000) NULL,
    [StackTrace] nvarchar(MAX) NULL,
    [Origin] varchar(30) NOT NULL,
    [SessionKind] varchar(20) NULL,
    [FirstSeen] datetime2(0) NOT NULL CONSTRAINT [DF_FW_ErrorLog_FirstSeen] DEFAULT (sysutcdatetime()),
    [LastSeen] datetime2(0) NOT NULL CONSTRAINT [DF_FW_ErrorLog_LastSeen] DEFAULT (sysutcdatetime()),
    [OccurrenceCount] int NOT NULL CONSTRAINT [DF_FW_ErrorLog_OccurrenceCount] DEFAULT ((1)),
    [RegistrationID] int NULL,
    [UserID] int NULL,
    [MachineName] varchar(100) NULL,
    [AppVersion] varchar(40) NULL,
    [Acknowledged] bit NOT NULL CONSTRAINT [DF_FW_ErrorLog_Acknowledged] DEFAULT ((0)),
    [AcknowledgedBy] int NULL,
    [AcknowledgedOn] datetime2(0) NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_ErrorLog_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [Resolved] bit NOT NULL CONSTRAINT [DF_FW_ErrorLog_Resolved] DEFAULT ((0)),
    [ResolvedBy] int NULL,
    [ResolvedOn] datetime2(0) NULL,
    [Resolution] nvarchar(1000) NULL,
    [RecurredAfterResolved] bit NOT NULL CONSTRAINT [DF_FW_ErrorLog_Recurred] DEFAULT ((0)),
    [ResolvedSource] varchar(20) NULL,
    CONSTRAINT [PK_FW_ErrorLog] PRIMARY KEY CLUSTERED ([ErrorLogID])
);
GO

IF OBJECT_ID('dbo.[FW_FallbackUsageLog]', 'U') IS NULL
CREATE TABLE dbo.[FW_FallbackUsageLog] (
    [FallbackUsageLogID] int IDENTITY(1,1) NOT NULL,
    [LoggedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_FallbackUsageLog_LoggedOn] DEFAULT (sysutcdatetime()),
    [RegistrationID] int NULL,
    [UserID] int NULL,
    [PageName] varchar(100) NULL,
    [FallbackType] varchar(100) NOT NULL,
    [Details] varchar(1000) NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_FallbackUsageLog_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_FallbackUsageLog] PRIMARY KEY CLUSTERED ([FallbackUsageLogID])
);
GO

IF OBJECT_ID('dbo.[FW_Format_Date]', 'U') IS NULL
CREATE TABLE dbo.[FW_Format_Date] (
    [FormatDateID] int IDENTITY(1,1) NOT NULL,
    [FormatPattern] varchar(40) NOT NULL,
    [Description] varchar(60) NOT NULL,
    [DisplayOrder] int NOT NULL CONSTRAINT [DF_FW_Format_Date_DisplayOrder] DEFAULT ((10)),
    [IsActive] bit NOT NULL CONSTRAINT [DF_FW_Format_Date_IsActive] DEFAULT ((1)),
    CONSTRAINT [PK_FW_Format_Date] PRIMARY KEY CLUSTERED ([FormatDateID])
);
GO

IF OBJECT_ID('dbo.[FW_Format_Time]', 'U') IS NULL
CREATE TABLE dbo.[FW_Format_Time] (
    [FormatTimeID] int IDENTITY(1,1) NOT NULL,
    [FormatPattern] varchar(40) NOT NULL,
    [Description] varchar(60) NOT NULL,
    [DisplayOrder] int NOT NULL CONSTRAINT [DF_FW_Format_Time_DisplayOrder] DEFAULT ((10)),
    [IsActive] bit NOT NULL CONSTRAINT [DF_FW_Format_Time_IsActive] DEFAULT ((1)),
    CONSTRAINT [PK_FW_Format_Time] PRIMARY KEY CLUSTERED ([FormatTimeID])
);
GO

IF OBJECT_ID('dbo.[FW_Gender]', 'U') IS NULL
CREATE TABLE dbo.[FW_Gender] (
    [GenderID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [GenderDescription] varchar(30) NOT NULL,
    [IsActive] bit NOT NULL CONSTRAINT [DF_Gender_Active] DEFAULT ((1)),
    [CreatedBy] int NOT NULL,
    [CreatedOn] datetime NOT NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_Gender_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_Gender] PRIMARY KEY CLUSTERED ([GenderID])
);
GO

IF OBJECT_ID('dbo.[FW_GeneratedPages]', 'U') IS NULL
CREATE TABLE dbo.[FW_GeneratedPages] (
    [GeneratedPageID] int IDENTITY(1,1) NOT NULL,
    [RequestName] varchar(50) NOT NULL,
    [PageBaseName] varchar(50) NULL,
    [BrowsePageName] varchar(50) NULL,
    [MaintenancePageName] varchar(50) NULL,
    [UnderlyingTableName] varchar(50) NULL,
    [BrowseSql] varchar(MAX) NULL,
    [EditableFields] varchar(MAX) NULL,
    [ReadOnlyFields] varchar(MAX) NULL,
    [LookupFields] varchar(MAX) NULL,
    [AdminRequiredFields] varchar(MAX) NULL,
    [CreateBehavior] varchar(50) NULL,
    [UpdateBehavior] varchar(50) NULL,
    [DeleteBehavior] varchar(50) NULL,
    [MenuCaller] varchar(50) NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_PageRequests_CreatedOn] DEFAULT (sysutcdatetime()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime2(0) NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_PageRequests_DeletedFlag] DEFAULT ((0)),
    [RowVersion] rowversion,
    [BrowseFields] varchar(MAX) NULL,
    [MaintenanceFields] varchar(MAX) NULL,
    [UseRegistrationID] bit NOT NULL CONSTRAINT [DF_FW_GeneratedPages_UseRegistrationID] DEFAULT ((0)),
    [GeneratedMaintenanceSource] varchar(MAX) NULL,
    [GeneratedMaintenanceHash] varchar(64) NULL,
    [GenerateBrowsePage] bit NOT NULL CONSTRAINT [DF_FW_GeneratedPages_GenerateBrowsePage] DEFAULT ((1)),
    [GenerateMaintenancePage] bit NOT NULL CONSTRAINT [DF_FW_GeneratedPages_GenerateMaintenancePage] DEFAULT ((1)),
    [UseQbeOnly] bit NOT NULL CONSTRAINT [DF_FW_GeneratedPages_UseQbeOnly] DEFAULT ((0)),
    [IconFileName] varchar(100) NULL,
    [GeneratedBrowseHash] varchar(64) NULL,
    [UseHotFields] bit NOT NULL CONSTRAINT [DF_FW_GeneratedPages_UseHotFields] DEFAULT ((0)),
    [HotFields] varchar(MAX) NULL,
    [TableAlias] varchar(100) NULL,
    [Column2Fields] varchar(MAX) NULL,
    [Owner] varchar(60) NULL,
    CONSTRAINT [PK_FW_PageRequests] PRIMARY KEY CLUSTERED ([GeneratedPageID])
);
GO

IF OBJECT_ID('dbo.[FW_HD_IssueAttachments]', 'U') IS NULL
CREATE TABLE dbo.[FW_HD_IssueAttachments] (
    [AttachmentID] int IDENTITY(1,1) NOT NULL,
    [IssueID] int NOT NULL,
    [RegistrationID] int NOT NULL,
    [FileName] varchar(255) NOT NULL,
    [ContentType] varchar(150) NULL,
    [FileSize] bigint NOT NULL,
    [FileData] varbinary(MAX) NOT NULL,
    [CreatedBy] int NOT NULL,
    [CreatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_HD_IssueAttachments_CreatedOn] DEFAULT (sysutcdatetime()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_HD_IssueAttachments_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [ConversationEntryID] int NULL,
    CONSTRAINT [PK_FW_HD_IssueAttachments] PRIMARY KEY CLUSTERED ([AttachmentID]),
    CONSTRAINT [CK_FW_HD_IssueAttachments_Size] CHECK ([FileSize]>=(0))
);
GO

IF OBJECT_ID('dbo.[FW_HD_IssueCategories]', 'U') IS NULL
CREATE TABLE dbo.[FW_HD_IssueCategories] (
    [CategoryID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NULL,
    [CategoryName] varchar(100) NOT NULL,
    [Description] varchar(500) NULL,
    [DisplayOrder] int NOT NULL CONSTRAINT [DF_FW_IssueCategories_DisplayOrder] DEFAULT ((0)),
    [IsActive] bit NOT NULL CONSTRAINT [DF_FW_IssueCategories_IsActive] DEFAULT ((1)),
    [CreatedBy] int NOT NULL CONSTRAINT [DF_FW_IssueCategories_CreatedBy] DEFAULT ((0)),
    [CreatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_IssueCategories_CreatedOn] DEFAULT (sysutcdatetime()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_IssueCategories_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RequiresExpectedBehavior] bit NOT NULL CONSTRAINT [DF_FW_HD_IssueCategories_RequiresExpectedBehavior] DEFAULT ((0)),
    [RequiresPage] bit NOT NULL CONSTRAINT [DF_FW_HD_IssueCategories_RequiresPage] DEFAULT ((0)),
    [DescribeThe] varchar(50) NULL,
    CONSTRAINT [PK_FW_IssueCategories] PRIMARY KEY CLUSTERED ([CategoryID])
);
GO

IF OBJECT_ID('dbo.[FW_HD_Issues]', 'U') IS NULL
CREATE TABLE dbo.[FW_HD_Issues] (
    [IssueID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [ApplicationID] int NULL,
    [CategoryID] int NOT NULL,
    [Subject] varchar(250) NOT NULL,
    [Description] varchar(MAX) NOT NULL,
    [Status] varchar(30) NOT NULL CONSTRAINT [DF_FW_HD_Issues_Status] DEFAULT ('New'),
    [Priority] varchar(20) NOT NULL CONSTRAINT [DF_FW_HD_Issues_Priority] DEFAULT ('Normal'),
    [ReporterUserID] int NOT NULL,
    [AssignedSupportUserID] int NULL,
    [CreatedBy] int NOT NULL,
    [CreatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_HD_Issues_CreatedOn] DEFAULT (sysutcdatetime()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime2(0) NULL,
    [ClosedBy] int NULL,
    [ClosedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_HD_Issues_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [IssueNumber] varchar(30) NOT NULL,
    [ConversationText] varchar(MAX) NULL,
    [ConversationEntryCount] int NOT NULL CONSTRAINT [DF_FW_HD_Issues_ConversationEntryCount] DEFAULT ((1)),
    [FirstResponseOn] datetime2(0) NULL,
    [ExpectedBehavior] varchar(MAX) NULL,
    [ReportedFromPage] varchar(100) NULL,
    [StepsToReproduce] varchar(MAX) NULL,
    CONSTRAINT [PK_FW_HD_Issues] PRIMARY KEY CLUSTERED ([IssueID]),
    CONSTRAINT [CK_FW_HD_Issues_Priority] CHECK ([Priority]='Critical' OR [Priority]='High' OR [Priority]='Normal' OR [Priority]='Low'),
    CONSTRAINT [CK_FW_HD_Issues_Status] CHECK ([Status]='Reopened' OR [Status]='Closed' OR [Status]='Resolved' OR [Status]='Waiting for User' OR [Status]='In Progress' OR [Status]='Assigned' OR [Status]='New')
);
GO

IF OBJECT_ID('dbo.[FW_LicenseTerms]', 'U') IS NULL
CREATE TABLE dbo.[FW_LicenseTerms] (
    [LicenseTermID] int IDENTITY(1,1) NOT NULL,
    [TermName] varchar(50) NOT NULL,
    [OffsetDays] int NULL,
    [DisplayOrder] int NOT NULL CONSTRAINT [DF_FW_LicenseTerms_DisplayOrder] DEFAULT ((0)),
    [IsActive] bit NOT NULL CONSTRAINT [DF_FW_LicenseTerms_IsActive] DEFAULT ((1)),
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL CONSTRAINT [DF_FW_LicenseTerms_CreatedOn] DEFAULT (getdate()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_LicenseTerms_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_LicenseTerms] PRIMARY KEY CLUSTERED ([LicenseTermID])
);
GO

IF OBJECT_ID('dbo.[FW_LoginAttempt]', 'U') IS NULL
CREATE TABLE dbo.[FW_LoginAttempt] (
    [LoginAttemptID] int IDENTITY(1,1) NOT NULL,
    [AttemptedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_LoginAttempt_AttemptedOn] DEFAULT (sysutcdatetime()),
    [AttemptedUserName] varchar(100) NOT NULL,
    [Reason] varchar(20) NOT NULL,
    [RegistrationID] int NULL,
    [UserID] int NULL,
    [MachineName] varchar(100) NULL,
    [SessionKind] varchar(20) NULL,
    [AppVersion] varchar(40) NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_LoginAttempt_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_LoginAttempt] PRIMARY KEY CLUSTERED ([LoginAttemptID])
);
GO

IF OBJECT_ID('dbo.[FW_MessageRecipients]', 'U') IS NULL
CREATE TABLE dbo.[FW_MessageRecipients] (
    [MessageRecipientID] int IDENTITY(1,1) NOT NULL,
    [MessageID] int NOT NULL,
    [ThreadID] int NOT NULL,
    [RegistrationID] int NOT NULL,
    [UserID] int NOT NULL,
    [RecipientType] char(2) NOT NULL,
    [FolderName] varchar(12) NOT NULL,
    [PreviousFolderName] varchar(12) NULL,
    [IsRead] bit NOT NULL CONSTRAINT [DF_FW_MessageRecipients_IsRead] DEFAULT ((0)),
    [ReadOn] datetime2(0) NULL,
    [CreatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_MessageRecipients_CreatedOn] DEFAULT (sysutcdatetime()),
    [UpdatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_MessageRecipients_UpdatedOn] DEFAULT (sysutcdatetime()),
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_MessageRecipients] PRIMARY KEY CLUSTERED ([MessageRecipientID]),
    CONSTRAINT [CK_FW_MessageRecipients_Folder] CHECK ([FolderName]='Trash' OR [FolderName]='Archive' OR [FolderName]='Sent' OR [FolderName]='Inbox'),
    CONSTRAINT [CK_FW_MessageRecipients_Type] CHECK ([RecipientType]='Se' OR [RecipientType]='Cc' OR [RecipientType]='To')
);
GO

IF OBJECT_ID('dbo.[FW_Messages]', 'U') IS NULL
CREATE TABLE dbo.[FW_Messages] (
    [MessageID] int IDENTITY(1,1) NOT NULL,
    [ThreadID] int NOT NULL,
    [RegistrationID] int NOT NULL,
    [FromUserID] int NOT NULL,
    [ToUserID] int NOT NULL,
    [Body] varchar(MAX) NOT NULL,
    [SentOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_Messages_SentOn] DEFAULT (sysutcdatetime()),
    [CreatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_Messages_CreatedOn] DEFAULT (sysutcdatetime()),
    [RowVersion] rowversion,
    [FromUserName] varchar(250) NULL,
    [ToUserName] varchar(250) NULL,
    CONSTRAINT [PK_FW_Messages] PRIMARY KEY CLUSTERED ([MessageID])
);
GO

IF OBJECT_ID('dbo.[FW_MessageThreads]', 'U') IS NULL
CREATE TABLE dbo.[FW_MessageThreads] (
    [ThreadID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [Subject] varchar(250) NOT NULL,
    [CreatedBy] int NOT NULL,
    [CreatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_MessageThreads_CreatedOn] DEFAULT (sysutcdatetime()),
    [UpdatedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_MessageThreads_UpdatedOn] DEFAULT (sysutcdatetime()),
    [RowVersion] rowversion,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_MessageThreads_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    CONSTRAINT [PK_FW_MessageThreads] PRIMARY KEY CLUSTERED ([ThreadID])
);
GO

IF OBJECT_ID('dbo.[FW_Pages]', 'U') IS NULL
CREATE TABLE dbo.[FW_Pages] (
    [PageID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NULL,
    [DB_Table] varchar(100) NULL,
    [ExposedToUser] bit NULL,
    [Table_Alias] varchar(100) NULL,
    [WindowOrPage] varchar(100) NULL,
    [Table_SQL] varchar(MAX) NULL,
    [DisplayOrder] tinyint NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [ModifiedBy] int NULL,
    [ModifiedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_Pages_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [Background] int NULL,
    [UseHotFields] bit NOT NULL CONSTRAINT [DF_FW_Pages_UseHotFields] DEFAULT ((0)),
    [HotFields] varchar(MAX) NULL,
    CONSTRAINT [PK_FW_Pages] PRIMARY KEY CLUSTERED ([PageID])
);
GO

IF OBJECT_ID('dbo.[FW_PageZooms]', 'U') IS NULL
CREATE TABLE dbo.[FW_PageZooms] (
    [PageZoomID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NULL,
    [UserID] int NOT NULL,
    [PageName] varchar(128) NOT NULL,
    [ZoomFactor] decimal(4,2) NOT NULL CONSTRAINT [DF_FW_PageZooms_ZoomFactor] DEFAULT ((1.00)),
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL CONSTRAINT [DF_FW_PageZooms_CreatedOn] DEFAULT (getdate()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_PageZooms_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_PageZooms] PRIMARY KEY CLUSTERED ([PageZoomID])
);
GO

IF OBJECT_ID('dbo.[FW_Perm_Dashboard]', 'U') IS NULL
CREATE TABLE dbo.[FW_Perm_Dashboard] (
    [DashboardID] int IDENTITY(1,1) NOT NULL,
    CONSTRAINT [PK_FW_Perm_Dashboard] PRIMARY KEY CLUSTERED ([DashboardID])
);
GO

IF OBJECT_ID('dbo.[FW_Perm_SystemHealth]', 'U') IS NULL
CREATE TABLE dbo.[FW_Perm_SystemHealth] (
    [SystemHealthID] int IDENTITY(1,1) NOT NULL,
    CONSTRAINT [PK_FW_Perm_SystemHealth] PRIMARY KEY CLUSTERED ([SystemHealthID])
);
GO

IF OBJECT_ID('dbo.[FW_Registration]', 'U') IS NULL
CREATE TABLE dbo.[FW_Registration] (
    [RegistrationID] int IDENTITY(1,1) NOT NULL,
    [RegistrationTypeID] int NULL,
    [CompanyAdminID] int NULL,
    [CompanyAdminRoleID] int NULL,
    [Address1] varchar(75) NULL,
    [Address2] varchar(75) NULL,
    [AllowMessaging] bit NULL,
    [AllowMultipleRoles] bit NULL,
    [AllowPasswordChangeAtLogin] bit NULL,
    [AllowUpdateMyProfile] bit NULL,
    [AllowUpdateMyProfileEmail] bit NULL,
    [City] varchar(75) NULL,
    [DisplayDashboardOnStartUp] bit NULL,
    [EndOfDay] varchar(5) NULL,
    [ExternalLogin] bit NULL,
    [LicenseExpiration_Date] date NULL,
    [LogoutAtClose] bit NULL,
    [MainEmail] varchar(50) NULL,
    [MainFax] varchar(25) NULL,
    [MainPhone] varchar(25) NULL,
    [MaxRecordsNoQBE] int NULL,
    [MaxUsers] smallint NOT NULL CONSTRAINT [DF_Registration_MaxUser] DEFAULT ((1)),
    [MessageRetrievalFrequency] int NULL,
    [PasswordHash_Performed] bit NULL,
    [RegName] varchar(125) NULL,
    [State] varchar(2) NULL,
    [TwoFactorAuthentication] bit NULL,
    [VersionStartedOn] varchar(50) NULL,
    [WindowsLogin] bit NULL,
    [WebLandingPage] varchar(50) NULL,
    [Zip] varchar(25) NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [CompanyAdmin_UserID] int NULL,
    [IsActive] bit NOT NULL,
    [FormatDateID] int NULL,
    [FormatTimeID] int NULL,
    [TimeZoneID] int NULL,
    [BTN_Create_Caption] varchar(10) NULL,
    [BTN_Read_Caption] varchar(10) NULL,
    [BTN_Update_Caption] varchar(10) NULL,
    [BTN_Delete_Caption] varchar(10) NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_Registration_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [HDUserSupport] int NULL,
    [HDApplicationSupport] int NULL,
    [Smarty_AuthID] varchar(50) NULL,
    [Smarty_AuthToken] varchar(50) NULL,
    [Smarty_EmbeddedKey] varchar(50) NULL,
    [Smarty_UseEmbeddedKey] bit NULL,
    [HomeGraphic] varchar(260) NULL,
    [LicenseTermID] int NULL,
    [LicenseStart_Date] date NULL,
    [MaxRecordsWithQBE] int NULL,
    CONSTRAINT [PK_Registration] PRIMARY KEY CLUSTERED ([RegistrationID])
);
GO

IF OBJECT_ID('dbo.[FW_RegistrationType]', 'U') IS NULL
CREATE TABLE dbo.[FW_RegistrationType] (
    [RegistrationTypeID] int IDENTITY(1,1) NOT NULL,
    [RegTypeName] varchar(50) NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_RegistrationType_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_RegistrationType] PRIMARY KEY CLUSTERED ([RegistrationTypeID])
);
GO

IF OBJECT_ID('dbo.[FW_RoleDetails]', 'U') IS NULL
CREATE TABLE dbo.[FW_RoleDetails] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NULL,
    [RoleID] int NULL,
    [RoleDetailID] int NULL,
    [SchemaID] int NULL,
    [CA_CanChange] bit NULL,
    [Can_Create] bit NULL,
    [Can_Delete] bit NULL,
    [Can_Export] bit NULL,
    [Can_Import] bit NULL,
    [Can_Read] bit NULL,
    [Can_Update] bit NULL,
    [Can_UseQBE] bit NULL,
    [Can_ViewAllRecords] bit NULL,
    [Can_ViewOnlyMyRecords] bit NULL,
    [DB_Table] varchar(50) NULL,
    [Table_Alias] varchar(50) NULL,
    [OverrideCaption] varchar(50) NULL,
    [Expand_QBE] bit NULL,
    [IsActive] bit NULL,
    [MaxRecords] int NULL,
    [StartEmpty] bit NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [FW_RoleSchemaID] int NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_RoleDetails_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_RoleDetails] PRIMARY KEY CLUSTERED ([ID])
);
GO

IF OBJECT_ID('dbo.[FW_RoleFields]', 'U') IS NULL
CREATE TABLE dbo.[FW_RoleFields] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NULL,
    [RoleID] int NULL,
    [RoleDetailID] int NULL,
    [SchemaID] int NULL,
    [CA_CanChange] bit NULL,
    [Can_Create] bit NULL,
    [Can_Read] bit NULL,
    [Can_Update] bit NULL,
    [FieldName] varchar(50) NULL,
    [FriendlyFieldName] varchar(60) NULL,
    [IsActive] bit NULL,
    [IsRequired] bit NULL,
    [IsUnique] bit NULL,
    [Make_Invisible] bit NULL,
    [OrderBy] int NULL,
    [OverrideCaption] varchar(20) NULL,
    [FileLink] varchar(101) NULL,
    [TableName] varchar(50) NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_RoleFields_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_RoleFields] PRIMARY KEY CLUSTERED ([ID])
);
GO

IF OBJECT_ID('dbo.[FW_Roles]', 'U') IS NULL
CREATE TABLE dbo.[FW_Roles] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [RoleName] varchar(20) NULL,
    [CA_CanChange] bit NULL,
    [Can_Create] bit NULL,
    [Can_Read] bit NULL,
    [Can_Update] bit NULL,
    [Can_Delete] bit NULL,
    [Can_Export] bit NULL,
    [Can_Import] bit NULL,
    [Can_UseQBE] bit NULL,
    [Can_ViewAllRecords] bit NULL,
    [Can_ViewOnlyMyRecords] bit NULL,
    [DisplayOrder] tinyint NULL,
    [IsActive] bit NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [Typ_AppAdmin] bit NULL,
    [Typ_CompanyAdmin] bit NULL,
    [Typ_RW] bit NULL,
    [Typ_RO] bit NULL,
    [Typ_User] bit NULL,
    [Typ_OnlyMyRecords] bit NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_Roles_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_Roles] PRIMARY KEY CLUSTERED ([ID])
);
GO

IF OBJECT_ID('dbo.[FW_RoleSchema]', 'U') IS NULL
CREATE TABLE dbo.[FW_RoleSchema] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [DB_Table] varchar(128) NULL,
    [Table_Alias] varchar(128) NULL,
    [IsActive] bit NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [xxx] nchar(10) NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_RoleSchema_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_RolesSchema] PRIMARY KEY CLUSTERED ([ID])
);
GO

IF OBJECT_ID('dbo.[FW_RoleTemplate]', 'U') IS NULL
CREATE TABLE dbo.[FW_RoleTemplate] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [RoleName] varchar(20) NULL,
    [CA_CanChange] bit NULL,
    [Can_Create] bit NULL,
    [Can_Read] bit NULL,
    [Can_Update] bit NULL,
    [Can_Delete] bit NULL,
    [Can_Export] bit NULL,
    [Can_Import] bit NULL,
    [Can_UseQBE] bit NULL,
    [Can_ViewAllRecords] bit NULL,
    [Can_ViewOnlyMyRecords] bit NULL,
    [DisplayOrder] tinyint NULL,
    [IsActive] bit NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL,
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [Typ_AppAdmin] bit NULL,
    [Typ_CompanyAdmin] bit NULL,
    [Typ_RW] bit NULL,
    [Typ_RO] bit NULL,
    [Typ_User] bit NULL,
    [Typ_OnlyMyRecords] bit NULL,
    [DeletedFlag] bit NOT NULL,
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion
);
GO

IF OBJECT_ID('dbo.[FW_SavedQBE]', 'U') IS NULL
CREATE TABLE dbo.[FW_SavedQBE] (
    [SavedQbeID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [UserID] int NOT NULL,
    [QbeName] nvarchar(100) NOT NULL,
    [IsCompanyWide] bit NOT NULL CONSTRAINT [DF_FW_SavedQbe_IsCompanyWide] DEFAULT ((0)),
    [TableContext] nvarchar(200) NOT NULL,
    [QbeData] nvarchar(MAX) NOT NULL,
    [CreatedOn] datetime2 NOT NULL CONSTRAINT [DF_FW_SavedQbe_CreatedOn] DEFAULT (getdate()),
    [UpdatedOn] datetime2 NOT NULL CONSTRAINT [DF_FW_SavedQbe_UpdatedOn] DEFAULT (getdate()),
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_SavedQbe_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_SavedQbe] PRIMARY KEY CLUSTERED ([SavedQbeID])
);
GO

IF OBJECT_ID('dbo.[FW_Session]', 'U') IS NULL
CREATE TABLE dbo.[FW_Session] (
    [SessionID] int IDENTITY(1,1) NOT NULL,
    [UserID] int NOT NULL,
    [RegistrationID] int NULL,
    [RoleID] int NULL,
    [StartedOn] datetime2(0) NOT NULL CONSTRAINT [DF_FW_Session_StartedOn] DEFAULT (sysutcdatetime()),
    [EndedOn] datetime2(0) NULL,
    [EndReason] varchar(20) NULL,
    [LastActivityOn] datetime2(0) NULL,
    [SessionKind] varchar(20) NULL,
    [MachineName] varchar(100) NULL,
    [ClientAddress] varchar(64) NULL,
    [AppVersion] varchar(40) NULL,
    [ProcessID] int NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_Session_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_Session] PRIMARY KEY CLUSTERED ([SessionID])
);
GO

IF OBJECT_ID('dbo.[FW_SwitchUser]', 'U') IS NULL
CREATE TABLE dbo.[FW_SwitchUser] (
    [SwitchUserID] int IDENTITY(1,1) NOT NULL,
    [UserId] int NOT NULL,
    [RegistrationID] int NULL,
    [PersonType] varchar(20) NOT NULL,
    [PersonID] int NULL,
    [FirstName] varchar(50) NULL,
    [LastName] varchar(50) NULL,
    [FirstLast] varchar(101) NULL,
    [UserName] varchar(50) NULL,
    [Email] varchar(128) NULL,
    [CompanyName] varchar(75) NULL,
    [IsActive] bit NOT NULL CONSTRAINT [DF_FW_SwitchUser_IsActive] DEFAULT ((0)),
    [LoginActive] bit NOT NULL CONSTRAINT [DF_FW_SwitchUser_LoginActive] DEFAULT ((0)),
    [PersonActive] bit NOT NULL CONSTRAINT [DF_FW_SwitchUser_PersonActive] DEFAULT ((0)),
    [RefreshedOn] datetime2(3) NOT NULL CONSTRAINT [DF_FW_SwitchUser_RefreshedOn] DEFAULT (sysutcdatetime()),
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_SwitchUser] PRIMARY KEY CLUSTERED ([SwitchUserID])
);
GO

IF OBJECT_ID('dbo.[FW_TableLayouts]', 'U') IS NULL
CREATE TABLE dbo.[FW_TableLayouts] (
    [ID] bigint IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [UserID] int NULL,
    [PageName] varchar(100) NOT NULL,
    [TableName] varchar(100) NULL,
    [LayoutType] varchar(20) NOT NULL,
    [LayoutName] varchar(100) NULL,
    [JsonState] nvarchar(MAX) NOT NULL,
    [IsActive] bit NOT NULL CONSTRAINT [DF_FW_TableLayouts_IsActive] DEFAULT ((1)),
    [CreatedBy] int NULL,
    [CreatedOn] datetime NOT NULL CONSTRAINT [DF_FW_TableLayouts_CreatedOn] DEFAULT (getdate()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_TableLayouts_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2 NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_TableLayouts] PRIMARY KEY CLUSTERED ([ID]),
    CONSTRAINT [CK_FW_TableLayouts_LayoutType] CHECK ([LayoutType]='QbeDefault' OR [LayoutType]='LastUsed' OR [LayoutType]='UserNamed' OR [LayoutType]='Default')
);
GO

IF OBJECT_ID('dbo.[FW_TimeZones]', 'U') IS NULL
CREATE TABLE dbo.[FW_TimeZones] (
    [TimeZoneID] int IDENTITY(1,1) NOT NULL,
    [WindowsTimeZoneName] sysname NOT NULL,
    [TimeZoneName] varchar(100) NULL,
    [DisplayName] varchar(500) NOT NULL,
    CONSTRAINT [PK_FW_TimeZones] PRIMARY KEY CLUSTERED ([TimeZoneID])
);
GO

IF OBJECT_ID('dbo.[FW_UpdateTabOrder]', 'U') IS NULL
CREATE TABLE dbo.[FW_UpdateTabOrder] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [PageName] varchar(128) NOT NULL,
    [ControlName] varchar(128) NOT NULL,
    [TabOrder] int NOT NULL,
    [IsActive] bit NOT NULL CONSTRAINT [DF_FW_UpdateTabOrder_IsActive] DEFAULT ((1)),
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_UpdateTabOrder_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NOT NULL CONSTRAINT [DF_FW_UpdateTabOrder_CreatedOn] DEFAULT (getdate()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    CONSTRAINT [UQ_FW_UpdateTabOrder_Page_Control] UNIQUE NONCLUSTERED ([PageName], [ControlName]),
    CONSTRAINT [PK_FW_UpdateTabOrder] PRIMARY KEY CLUSTERED ([ID])
);
GO

IF OBJECT_ID('dbo.[FW_UsageCounter]', 'U') IS NULL
CREATE TABLE dbo.[FW_UsageCounter] (
    [UsageCounterID] int IDENTITY(1,1) NOT NULL,
    [HourUtc] datetime2(0) NOT NULL,
    [Kind] varchar(20) NOT NULL,
    [PageName] varchar(100) NOT NULL,
    [RegistrationID] int NULL,
    [EventCount] int NOT NULL CONSTRAINT [DF_FW_UsageCounter_EventCount] DEFAULT ((0)),
    [DbMillisTotal] bigint NOT NULL CONSTRAINT [DF_FW_UsageCounter_DbTotal] DEFAULT ((0)),
    [DbMillisMin] int NULL,
    [DbMillisMax] int NULL,
    [PerceivedMillisTotal] bigint NOT NULL CONSTRAINT [DF_FW_UsageCounter_PerTotal] DEFAULT ((0)),
    [PerceivedMillisMin] int NULL,
    [PerceivedMillisMax] int NULL,
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_UsageCounter_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_UsageCounter] PRIMARY KEY CLUSTERED ([UsageCounterID])
);
GO

IF OBJECT_ID('dbo.[FW_Users]', 'U') IS NULL
CREATE TABLE dbo.[FW_Users] (
    [UserId] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NULL,
    [GenderID] int NULL,
    [Address1] varchar(75) NULL,
    [Address2] varchar(75) NULL,
    [BirthDate] date NULL,
    [City] varchar(75) NULL,
    [DisplayDashboardOnStartUp] bit NULL,
    [Email] varchar(128) NULL,
    [EndDate] date NULL,
    [FirstName] varchar(50) NULL,
    [FirstLast] AS ((isnull([FirstName],'')+case when [FirstName] IS NOT NULL AND [LastName] IS NOT NULL then ' ' else '' end)+isnull([LastName],'')) PERSISTED,
    [IsActive] bit NULL CONSTRAINT [DF_FW_Users_IsActive] DEFAULT ((1)),
    [LastFirst] AS ((isnull([LastName],'')+case when [LastName] IS NOT NULL AND [FirstName] IS NOT NULL then ', ' else '' end)+isnull([FirstName],'')) PERSISTED,
    [LastName] varchar(50) NULL,
    [LocationGroupId] int NULL,
    [Password] varchar(50) NULL,
    [PasswordHash] nvarchar(255) NULL,
    [Phone] varchar(25) NULL,
    [PrimaryJobId] int NULL,
    [PrimarySubJobId] int NULL,
    [StartDate] date NULL,
    [State] varchar(2) NULL,
    [SuperAdmin] bit NULL,
    [TOTPKey] varchar(16) NULL,
    [TypeUser] varchar(50) NULL,
    [UserName] varchar(50) NULL,
    [Use2FA] bit NULL,
    [WindowsUser] varchar(50) NULL,
    [Zip] varchar(10) NULL,
    [CreatedBy] int NULL,
    [CreatedOn] datetime NULL CONSTRAINT [DF__Users__CreatedOn__2BFE89A6] DEFAULT (getdate()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NULL,
    [WindowsTimeZoneName] sysname NULL,
    [TimeZoneID] int NULL,
    [TimeZoneName] varchar(100) NULL,
    [DeletedFlag] bit NULL CONSTRAINT [DF_FW_Users_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    [AssignedManagerID] int NULL,
    CONSTRAINT [Users_PK_Users] PRIMARY KEY CLUSTERED ([UserId])
);
GO

IF OBJECT_ID('dbo.[FW_UserUiHints]', 'U') IS NULL
CREATE TABLE dbo.[FW_UserUiHints] (
    [ID] int IDENTITY(1,1) NOT NULL,
    [RegistrationID] int NOT NULL,
    [UserID] int NOT NULL,
    [HintKey] nvarchar(100) NOT NULL,
    [Seen] bit NOT NULL CONSTRAINT [DF_FW_UserUiHints_Seen] DEFAULT ((1)),
    [SeenOn] datetime NOT NULL CONSTRAINT [DF_FW_UserUiHints_SeenOn] DEFAULT (getdate()),
    [CreatedBy] int NULL,
    [CreatedOn] datetime NOT NULL CONSTRAINT [DF_FW_UserUiHints_CreatedOn] DEFAULT (getdate()),
    [UpdatedBy] int NULL,
    [UpdatedOn] datetime NOT NULL CONSTRAINT [DF_FW_UserUiHints_UpdatedOn] DEFAULT (getdate()),
    [DeletedFlag] bit NOT NULL CONSTRAINT [DF_FW_UserUiHints_DeletedFlag] DEFAULT ((0)),
    [DeletedBy] int NULL,
    [DeletedOn] datetime2(0) NULL,
    [RowVersion] rowversion,
    CONSTRAINT [PK_FW_UserUiHints] PRIMARY KEY CLUSTERED ([ID])
);
GO

IF OBJECT_ID('dbo.[FW_ZipCodes]', 'U') IS NULL
CREATE TABLE dbo.[FW_ZipCodes] (
    [ZipCodeID] int IDENTITY(1,1) NOT NULL,
    [ZipCode] nvarchar(5) NULL,
    [City] nvarchar(35) NULL,
    [State] nvarchar(2) NULL,
    [County] nvarchar(45) NULL,
    [AreaCode] nvarchar(45) NULL,
    [CityType] nvarchar(1) NULL,
    [CityAliasAbbreviation] nvarchar(13) NULL,
    [CityAliasName] nvarchar(35) NULL,
    [Latitude] decimal(18,6) NULL,
    [Longitude] decimal(18,6) NULL,
    [TimeZone] nvarchar(2) NULL,
    [Elevation] int NULL,
    [CountyFIPS] nvarchar(5) NULL,
    [DayLightSaving] nvarchar(1) NULL,
    [PreferredLastLineKey] nvarchar(25) NULL,
    [ClassificationCode] nvarchar(2) NULL,
    [MultiCounty] nvarchar(1) NULL,
    [StateFIPS] nvarchar(2) NULL,
    [CityStateKey] nvarchar(6) NULL,
    [CityAliasCode] nvarchar(5) NULL,
    [PrimaryRecord] nvarchar(50) NULL,
    [CityMixedCase] nvarchar(50) NULL,
    [CityAliasMixedCase] nvarchar(50) NULL,
    [StateANSI] nvarchar(2) NULL,
    [CountyANSI] nvarchar(3) NULL,
    [FacilityCode] nvarchar(1) NULL,
    [UniqueZIPName] nvarchar(1) NULL,
    [CityDeliveryIndicator] nvarchar(1) NULL,
    [CarrierRouteRateSortation] nvarchar(1) NULL,
    [FinanceNumber] nvarchar(6) NULL,
    [CountyMixedCase] nvarchar(45) NULL,
    CONSTRAINT [ZipCodeDatabase_STANDARD_PK_ZipCodeDatabase_STANDARD] PRIMARY KEY CLUSTERED ([ZipCodeID])
);
GO

