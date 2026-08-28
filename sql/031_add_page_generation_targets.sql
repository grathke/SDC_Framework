IF OBJECT_ID(N'dbo.FW_PageGeneration_B_U', N'U') IS NULL
    THROW 52105, 'dbo.FW_PageGeneration_B_U does not exist.', 1;

IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'GenerateBrowsePage') IS NULL
    ALTER TABLE dbo.FW_PageGeneration_B_U
        ADD GenerateBrowsePage BIT NOT NULL
            CONSTRAINT DF_FW_PageGeneration_B_U_GenerateBrowsePage DEFAULT (1);

IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'GenerateMaintenancePage') IS NULL
    ALTER TABLE dbo.FW_PageGeneration_B_U
        ADD GenerateMaintenancePage BIT NOT NULL
            CONSTRAINT DF_FW_PageGeneration_B_U_GenerateMaintenancePage DEFAULT (1);
GO
