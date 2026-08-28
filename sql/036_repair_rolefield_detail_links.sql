-- Repair legacy FW_RoleFields rows whose RoleDetailID was not populated.

UPDATE rf
SET rf.RoleDetailID = rd.ID
FROM dbo.FW_RoleFields rf
INNER JOIN dbo.FW_RoleDetails rd
    ON rd.RoleID = rf.RoleID
   AND rd.RegistrationID = rf.RegistrationID
   AND rd.SchemaID = rf.SchemaID
   AND rd.DB_Table = rf.TableName
WHERE rf.RoleDetailID IS NULL;
GO

PRINT 'Legacy FW_RoleFields RoleDetailID links repaired.';