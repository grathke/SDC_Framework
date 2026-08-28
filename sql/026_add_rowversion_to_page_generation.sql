/* Add the concurrency token required by PageGeneration_U. */

IF OBJECT_ID(N'dbo.FW_PageGeneration_B_U', N'U') IS NULL
    THROW 52101, 'dbo.FW_PageGeneration_B_U does not exist.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns AS c
    INNER JOIN sys.tables AS t ON t.object_id = c.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    WHERE s.name = N'dbo'
    AND t.name = N'FW_PageGeneration_B_U'
      AND c.system_type_id = 189
)
BEGIN
    ALTER TABLE dbo.FW_PageGeneration_B_U
        ADD RowVersion ROWVERSION NOT NULL;
END;
GO
