-- FW_Employees gets the two name columns FW_Users has: FirstLast and LastFirst, computed.
--
-- Computed rather than stored, for the reason the generated-page save path already documents -
-- a name held as data is a name that can disagree with the two columns it came from, and nothing
-- notices until somebody searches on one and reads the other.
--
-- The definitions are FW_Users' definitions verbatim, not a fresh pair that happens to produce
-- the same string. Two tables answering "what is this person called" in two spellings is exactly
-- how a join starts returning nothing for the rows where one of them put the comma somewhere
-- else. FirstName and LastName are NOT NULL here, so the CASE never fires - it is carried anyway
-- so the two definitions stay comparable, and so the column is still correct if either ever
-- becomes nullable.
--
-- PERSISTED, again matching FW_Users: these are what a browse grid sorts and filters on, and a
-- non-persisted computed column cannot be indexed.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'FirstLast')
BEGIN
    ALTER TABLE dbo.FW_Employees ADD FirstLast AS
        (ISNULL([FirstName], '')
         + CASE WHEN [FirstName] IS NOT NULL AND [LastName] IS NOT NULL THEN ' ' ELSE '' END
         + ISNULL([LastName], '')) PERSISTED;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'LastFirst')
BEGIN
    ALTER TABLE dbo.FW_Employees ADD LastFirst AS
        (ISNULL([LastName], '')
         + CASE WHEN [LastName] IS NOT NULL AND [FirstName] IS NOT NULL THEN ', ' ELSE '' END
         + ISNULL([FirstName], '')) PERSISTED;
END
GO
