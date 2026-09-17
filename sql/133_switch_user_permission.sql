/*
    133_switch_user_permission.sql

    Corrects FW_RoleDetails rows for FW_SwitchUser to Read and Use QBE only.

    The rows were created before FW_SwitchUser was known to be a browse-only table, so they arrived
    with the long-standing defaults - Create, Update, Delete and QBE on, Read off. Roles_U now greys
    those three out for this table, which means the page that created them can no longer correct
    them. Hence a script.

    Not needed if the row is removed from the role and added again: it is then created as Read plus
    Use QBE by AddRoleTableWithFields, which is the path this exists to make unnecessary.

    Leaves View All and View Mine alone. View Mine in particular must stay off - the page's SQL
    carries a UserId column, and that capability would scope the page to the administrator's own
    row, which is the one person a switch cannot use.

    Re-runnable, and touches nothing but this table's rows.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

SELECT 'BEFORE' AS Stage, RoleID, RegistrationID, DB_Table, Table_Alias,
       Can_Create, Can_Read, Can_Update, Can_Delete, Can_UseQBE,
       Can_ViewAllRecords, Can_ViewOnlyMyRecords
  FROM dbo.FW_RoleDetails
 WHERE DB_Table = 'FW_SwitchUser'
 ORDER BY RegistrationID, RoleID;

UPDATE dbo.FW_RoleDetails
   SET Can_Create = 0,
       Can_Read   = 1,
       Can_Update = 0,
       Can_Delete = 0,
       Can_UseQBE = 1,
       UpdatedBy  = 2,
       UpdatedOn  = GETDATE()
 WHERE DB_Table = 'FW_SwitchUser'
   AND (Can_Create <> 0 OR Can_Read <> 1 OR Can_Update <> 0 OR Can_Delete <> 0 OR ISNULL(Can_UseQBE, 0) <> 1);

PRINT 'Rows corrected: ' + CAST(@@ROWCOUNT AS varchar(10));

SELECT 'AFTER' AS Stage, RoleID, RegistrationID, DB_Table, Table_Alias,
       Can_Create, Can_Read, Can_Update, Can_Delete, Can_UseQBE,
       Can_ViewAllRecords, Can_ViewOnlyMyRecords
  FROM dbo.FW_RoleDetails
 WHERE DB_Table = 'FW_SwitchUser'
 ORDER BY RegistrationID, RoleID;
