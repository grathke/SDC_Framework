-- 156_import_templates.sql
--
-- FW_SavedImports - a saved mapping for the employee import: which heading in a file goes to
-- which column, and what default fills a column the file does not carry.
--
-- ONE ROW PER TEMPLATE, THE MAPPING AS JSON. A row per mapped column was the alternative, and
-- would make a template a parent and children to be saved in one transaction for a thing that is
-- only ever read and written whole. The JSON is small - a few dozen pairs - and nothing queries
-- inside it.
--
-- PER REGISTRATION, NOT PER USER. The people who import for a company share its files and its
-- column names; a mapping one of them saved is the one the next should find.
--
-- LastUsedOn AND UseCount EXIST TO MAKE DELETING EASY. Templates accumulate - one per file
-- somebody tried once - and the page lists them oldest-used last so the ones nobody uses are the
-- ones at the bottom with a date that says so. Deleting is soft, like everything else.
--
-- TargetTable is FW_Employees today. It is a column rather than an assumption so a second
-- importable table does not share, or collide with, this one's names.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.FW_SavedImports', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_SavedImports
    (
        SavedImportID    int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_SavedImports PRIMARY KEY,

        RegistrationID      int NOT NULL,
        TargetTable         nvarchar(128) NOT NULL,
        ImportName        nvarchar(100) NOT NULL,
        MappingData         nvarchar(max) NOT NULL,

        LastUsedOn          datetime NULL,
        UseCount            int NOT NULL
            CONSTRAINT DF_FW_SavedImports_UseCount DEFAULT (0),

        CreatedBy           int NULL,
        CreatedOn           datetime NULL
            CONSTRAINT DF_FW_SavedImports_CreatedOn DEFAULT (getdate()),
        UpdatedBy           int NULL,
        UpdatedOn           datetime NULL,
        DeletedFlag         bit NOT NULL
            CONSTRAINT DF_FW_SavedImports_DeletedFlag DEFAULT (0),
        DeletedBy           int NULL,
        DeletedOn           datetime2(0) NULL,
        RowVersion          rowversion
    );
END
GO

-- A live name is unique within its registration and target. A deleted one is not counted, so a
-- name can be reused after the template that had it is gone.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_FW_SavedImports_Name'
                                           AND object_id = OBJECT_ID('dbo.FW_SavedImports'))
    CREATE UNIQUE INDEX UX_FW_SavedImports_Name
        ON dbo.FW_SavedImports (RegistrationID, TargetTable, ImportName)
        WHERE DeletedFlag = 0;
GO
