IF OBJECT_ID(N'dbo.FW_PageGeneration_B_U', N'U') IS NULL
    THROW 52102, 'dbo.FW_PageGeneration_B_U does not exist.', 1;
GO

IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'BrowseFields') IS NULL
    ALTER TABLE dbo.FW_PageGeneration_B_U ADD BrowseFields VARCHAR(MAX) NULL;
GO

IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'MaintenanceFields') IS NULL
    ALTER TABLE dbo.FW_PageGeneration_B_U ADD MaintenanceFields VARCHAR(MAX) NULL;
GO