-- 158_rename_import_templates_to_saved_imports.sql
--
-- FW_ImportTemplates becomes FW_SavedImports, the name the page has used since Glenn renamed
-- templates to Saved Imports (2026-09-24). Renamed the same day it was made, while it held one
-- row, so the table says what the screen says.
--
-- Columns follow the naming rule for new tables: the key is <Stem>ID - SavedImportID - and the
-- name column is ImportName. Constraints and the index are renamed with it, so nothing in the
-- catalog still reads ImportTemplate.
--
-- Rows naming the old table, per CLAUDE.md "Removing A Page Or Table": FW_RoleSchema held one
-- (updated here); FW_RoleDetails, FW_RoleFields, FW_Pages, FW_TableLayouts, FW_SavedQbe,
-- FW_DashboardLayouts and FW_GeneratedPages held none. FW_AuditTrail's rows keep the old name -
-- they record what happened, under the name it happened under.
--
-- Guarded: on a database built fresh from 156 and 157, which now create FW_SavedImports
-- directly, this does nothing.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.FW_ImportTemplates', 'U') IS NOT NULL AND OBJECT_ID('dbo.FW_SavedImports', 'U') IS NULL
BEGIN
    BEGIN TRANSACTION;

    EXEC sp_rename 'dbo.FW_ImportTemplates.ImportTemplateID', 'SavedImportID', 'COLUMN';
    EXEC sp_rename 'dbo.FW_ImportTemplates.TemplateName', 'ImportName', 'COLUMN';

    EXEC sp_rename 'dbo.PK_FW_ImportTemplates', 'PK_FW_SavedImports', 'OBJECT';
    EXEC sp_rename 'dbo.DF_FW_ImportTemplates_UseCount', 'DF_FW_SavedImports_UseCount', 'OBJECT';
    EXEC sp_rename 'dbo.DF_FW_ImportTemplates_CreatedOn', 'DF_FW_SavedImports_CreatedOn', 'OBJECT';
    EXEC sp_rename 'dbo.DF_FW_ImportTemplates_DeletedFlag', 'DF_FW_SavedImports_DeletedFlag', 'OBJECT';
    EXEC sp_rename 'dbo.FW_ImportTemplates.UX_FW_ImportTemplates_Name', 'UX_FW_SavedImports_Name', 'INDEX';

    EXEC sp_rename 'dbo.FW_ImportTemplates', 'FW_SavedImports';

    UPDATE dbo.FW_RoleSchema
    SET DB_Table = 'FW_SavedImports', Table_Alias = 'Saved Imports'
    WHERE DB_Table = 'FW_ImportTemplates';

    COMMIT TRANSACTION;
END
GO
