-- 153_max_records_with_qbe.sql
--
-- FW_Registration.MaxRecordsWithQBE - the row cap once somebody has entered search criteria.
--
-- THERE WERE ALWAYS TWO CAPS AND ONLY ONE SETTING. MaxRecordsNoQBE governs a Find with no
-- criteria, where the cap means "this is the top of a longer list" and a handful is enough to
-- show the shape of the data. A Find WITH criteria means something else entirely - "your search
-- was not narrow enough" - and a handful there is infuriating: somebody who has just filtered by
-- department expects to see the department.
--
-- Until 2026-09-21 the filtered case had no cap at all. The limit was taken only when the filter
-- list was empty, so typing one character into a QBE cell switched it off: a contains search on
-- 10,000 employees bound every one of the 1,050 rows that matched. The grid was unreadable, and
-- the binding was most of what the Find cost - about 130ms against 9ms of SQL on one page.
--
-- 200 RATHER THAN 100. A hundred is easy to reach legitimately - permits on one street, staff in
-- one department - and being capped on a search you thought was narrow is the annoying case.
-- The cost is small: a thousand rows measured at about 130ms of binding, so 200 is roughly 25.
--
-- Nullable, defaulting to 200 on read, exactly as MaxRecordsNoQBE defaults to 10. A registration
-- that has never been asked has not decided anything, and null says that where a written-in
-- default would pretend otherwise.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'MaxRecordsWithQBE')
BEGIN
    ALTER TABLE dbo.FW_Registration ADD MaxRecordsWithQBE int NULL;
    PRINT 'ADDED: FW_Registration.MaxRecordsWithQBE';
END
ELSE
    PRINT 'SKIPPED: FW_Registration.MaxRecordsWithQBE already exists';
GO

SELECT RegistrationID, RegName, MaxRecordsNoQBE, MaxRecordsWithQBE FROM dbo.FW_Registration;
