IF OBJECT_ID(N'dbo.FW_PageGeneration_B_U', N'U') IS NULL
    THROW 52104, 'dbo.FW_PageGeneration_B_U does not exist.', 1;
GO

IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'UseRegistrationID') IS NULL
    ALTER TABLE dbo.FW_PageGeneration_B_U
        ADD UseRegistrationID BIT NOT NULL
            CONSTRAINT DF_FW_PageGeneration_B_U_UseRegistrationID DEFAULT (0);
GO