IF COL_LENGTH(N'dbo.FW_PageGeneration_B_U', N'UseQbeOnly') IS NULL
BEGIN
    ALTER TABLE dbo.FW_PageGeneration_B_U
        ADD UseQbeOnly BIT NOT NULL
            CONSTRAINT DF_FW_PageGeneration_B_U_UseQbeOnly DEFAULT (0);
END;