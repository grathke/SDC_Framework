IF COL_LENGTH('dbo.FW_Registration', 'BusinessRuleType') IS NULL
BEGIN
    ALTER TABLE dbo.FW_Registration
        ADD BusinessRuleType varchar(30) NOT NULL
            CONSTRAINT DF_FW_Registration_BusinessRuleType DEFAULT ('BR_RegIDBased');
END
GO

IF COL_LENGTH('dbo.FW_Registration', 'BusinessRuleType') IS NOT NULL
BEGIN
    UPDATE dbo.FW_Registration
        SET BusinessRuleType = 'BR_RegIDBased'
        WHERE BusinessRuleType IS NULL OR LTRIM(RTRIM(BusinessRuleType)) = '';
END
