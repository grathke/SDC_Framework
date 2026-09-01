/*
    044_users_rolefields_address1.sql
    ----------------------------------------------------------------------------------------------
    Repoints the four FW_RoleFields rows that still name FW_Users.Address at FW_Users.Address1.

    Why
    ---
    FW_Users.Address was renamed to Address1 last week and FW_Registration followed in 043, but the
    FW_Users permission rows were never repointed. Permission rows are keyed on table.field through
    FileLink, so those four rows name a column that no longer exists and can never match a control.

    The symptom was silent: Address1 on Users_AppAdmin_U was set required by permission for role 4
    and simply never turned yellow. Nothing reported an error - the page looked up
    FW_Users.Address1, found no row, and painted nothing. That is the cost of a link that is stored
    rather than derived, and it is the case the mismatch report should learn to catch.

    Row 1641 (role 4) carries IsRequired = 1 and IsActive = 1, set on 2026-09-01. Updating in place
    keeps those settings. Sync Table in Roles_U would also repair the link, but only by deleting the
    obsolete row and inserting a fresh one with IsActive = 0 and no required flag, per role.

    Checked before writing this: no table in the database still has a column named exactly Address,
    so the link is orphaned everywhere rather than ambiguous; no Address1 row already exists for
    these roles, so nothing collides; and FW_Enumerations, FW_Enumerations_U, Table_Rules and
    RoleFields hold no rows pointing at a bare .Address.

    Affects 4 rows: roles 4, 1, 2 and 8, all RegistrationID 1, SchemaID 11.

    Rollback
    --------
    At the bottom of this file.
*/

UPDATE dbo.FW_RoleFields
SET    FieldName         = 'Address1',
       FriendlyFieldName = CASE WHEN FriendlyFieldName = 'Address'
                                THEN 'Address1'
                                ELSE FriendlyFieldName
                           END,
       FileLink          = 'FW_Users.Address1'
WHERE  TableName = 'FW_Users'
  AND  FieldName = 'Address';
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration.
    ----------------------------------------------------------------------------------------------

UPDATE dbo.FW_RoleFields
SET    FieldName         = 'Address',
       FriendlyFieldName = CASE WHEN FriendlyFieldName = 'Address1'
                                THEN 'Address'
                                ELSE FriendlyFieldName
                           END,
       FileLink          = 'FW_Users.Address'
WHERE  TableName = 'FW_Users'
  AND  FieldName = 'Address1';
GO
*/
