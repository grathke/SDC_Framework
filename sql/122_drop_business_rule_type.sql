-- 122_drop_business_rule_type.sql
--
-- Removes the BusinessRuleType feature from the schema. Nothing read the value any more: the
-- combo, the model property, the session field and BusinessRuleTypes.vb all came out of the
-- application on 2026-09-16, and no page branched on it before that.
--
-- The default constraint has to go first - a column cannot be dropped while one is bound to it.

SET QUOTED_IDENTIFIER ON;
GO

DECLARE @constraint sysname;

SELECT @constraint = d.name
FROM sys.default_constraints d
JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
WHERE d.parent_object_id = OBJECT_ID('dbo.FW_Registration')
  AND c.name = 'BusinessRuleType';

IF @constraint IS NOT NULL
    EXEC('ALTER TABLE dbo.FW_Registration DROP CONSTRAINT [' + @constraint + ']');
GO

IF COL_LENGTH('dbo.FW_Registration', 'BusinessRuleType') IS NOT NULL
    ALTER TABLE dbo.FW_Registration DROP COLUMN BusinessRuleType;
GO

-- Field permissions and role captions keyed to the column, which would otherwise point at a
-- name that no longer resolves.
DELETE FROM dbo.FW_RoleFields
WHERE TableName = 'FW_Registration' AND FieldName = 'BusinessRuleType';
GO
