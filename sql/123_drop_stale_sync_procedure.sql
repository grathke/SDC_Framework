-- 123_drop_stale_sync_procedure.sql
--
-- Removes dbo.SyncRoleFieldsWithSchema if it is present anywhere.
--
-- The procedure was written in 004 and never installed on WX_Framework. It could not have run if
-- it had been: it inserts a TableField column that FW_RoleFields no longer has - the column is
-- FileLink now - and it never sets RoleDetailID, so every row it wrote would be orphaned from its
-- FW_RoleDetails parent.
--
-- The rules live in DataAccess.SyncRoleFieldsWithSchema, which the Add button in Roles_U and the
-- Update Schema tile on the admin dashboard both call. A second copy in T-SQL is one that can
-- disagree with it, which is what happened here.

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.SyncRoleFieldsWithSchema', 'P') IS NOT NULL
    DROP PROCEDURE dbo.SyncRoleFieldsWithSchema;
GO
