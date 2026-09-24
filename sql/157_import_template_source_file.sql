-- 157_import_template_source_file.sql
--
-- A copy of the file a template was saved from, so choosing the template shows that file on the
-- Source tab at once, with no upload (Glenn, 2026-09-24).
--
-- WHY A COPY AND NOT A PATH. In a browser session the file was uploaded from the user's own
-- computer and the server kept nothing; no browser lets a page read a file off that computer
-- again without the user picking it. The only way back to the file is a copy.
--
-- WHAT IT COSTS, SAID PLAINLY. The copy is the file as it was when the template was saved - a
-- staff list, names and addresses and whatever else it held - kept for as long as the template
-- is. A newer file is still brought in with Choose..., and the next Save stores that one instead.
-- Deleting the template wipes the copy there and then, rather than leaving it under a soft
-- delete: the row stays for the audit trail, the file does not.
--
-- Nullable, because a template saved before this carries no file, and one saved while editing a
-- mapping with no file loaded keeps whatever copy it already had.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.FW_SavedImports', 'SourceFileName') IS NULL
    ALTER TABLE dbo.FW_SavedImports ADD SourceFileName nvarchar(260) NULL;
GO

IF COL_LENGTH('dbo.FW_SavedImports', 'SourceFileData') IS NULL
    ALTER TABLE dbo.FW_SavedImports ADD SourceFileData varbinary(max) NULL;
GO
