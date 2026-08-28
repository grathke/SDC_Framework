IF OBJECT_ID(N'dbo.FW_PageGeneration_B_U', N'U') IS NULL
    THROW 52103, 'dbo.FW_PageGeneration_B_U does not exist.', 1;
GO

IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'GeneratedMaintenanceSource') IS NULL
    ALTER TABLE dbo.FW_PageGeneration_B_U ADD GeneratedMaintenanceSource VARCHAR(MAX) NULL;
GO

IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'GeneratedMaintenanceHash') IS NULL
    ALTER TABLE dbo.FW_PageGeneration_B_U ADD GeneratedMaintenanceHash VARCHAR(64) NULL;
GO
