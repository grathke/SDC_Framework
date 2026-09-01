/*
    043_registration_address1.sql
    ----------------------------------------------------------------------------------------------
    Renames FW_Registration.Address to Address1.

    Why
    ---
    Address lines are numbered from one across the framework: Address1, Address2, Address3.
    FW_Users was renamed to that convention last week; FW_Registration still called its first line
    Address, so the two tables disagreed.

    The convention matters because names are derived, not looked up. A generated page or a loop over
    Address1..N handles every line the same way; an unnumbered first line is a special case in every
    piece of code that touches the set. Captions follow too - DisplayNameFormatter gives
    "Address 1" and "Address 2" rather than "Address" and "Address 2".

    Permission rows are keyed on the column, so the one FW_RoleFields row naming the old column is
    renamed with it. Leaving it would give a permission that can never match a control.

    Checked before writing this: neither vw_FW_CurrentUser nor FW_HD_GetAdminDashboard references
    the column by name, and no FW_Enumerations_U row points at it.

    Rollback
    --------
    At the bottom of this file.
*/

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_Registration')
             AND name = 'Address')
   AND NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID('dbo.FW_Registration')
                     AND name = 'Address1')
BEGIN
    EXEC sp_rename 'dbo.FW_Registration.Address', 'Address1', 'COLUMN';
END
GO

UPDATE dbo.FW_RoleFields
SET    FieldName = 'Address1',
       FileLink  = 'FW_Registration.Address1'
WHERE  TableName = 'FW_Registration'
  AND  FieldName = 'Address';
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration, and revert the matching code change.
    ----------------------------------------------------------------------------------------------

UPDATE dbo.FW_RoleFields
SET    FieldName = 'Address',
       FileLink  = 'FW_Registration.Address'
WHERE  TableName = 'FW_Registration'
  AND  FieldName = 'Address1';
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_Registration')
             AND name = 'Address1')
BEGIN
    EXEC sp_rename 'dbo.FW_Registration.Address1', 'Address', 'COLUMN';
END
GO
*/
