USE WX_Framework;
GO

UPDATE dbo.FW_RoleTables
SET Table_SQL = 'SELECT * FROM dbo.FW_HD_Issues'
WHERE WIndowOrPage = 'FW_HD_Issues_Support_B'
  AND Table_SQL = 'SELECT IssueID AS PK, * FROM dbo.FW_HD_Issues';
GO