IF OBJECT_ID(N'dbo.FW_PageGeneration_B_U', N'U') IS NULL
    THROW 52106, 'dbo.FW_PageGeneration_B_U does not exist.', 1;

IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'CreateAsFrameworkPages') IS NULL
    ALTER TABLE dbo.FW_PageGeneration_B_U
        ADD CreateAsFrameworkPages BIT NOT NULL
            CONSTRAINT DF_FW_PageGeneration_B_U_CreateAsFrameworkPages DEFAULT (0);
GO
