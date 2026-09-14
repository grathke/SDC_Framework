-- dbo.TimeFormat goes, and registration 1 gets a month-first date.
--
-- TimeFormat was the first attempt at this, from 2026-07-07: four rows, superseded by
-- FW_Format_Time. Nothing points at it - no foreign key, no FW_RoleSchema row, no code - and it
-- is where the unusable "HH:mm AP" in FW_Registration.TimeFormat came from.
--
-- Its patterns could not have worked: SS and AP are not .NET specifiers, and row 4 reads
-- "HH;mm:SS AP" with a semicolon. Its Description column held the rendered sample, and row 4 is
-- the argument against storing one - the sample said 01:15:30 PM while the pattern beside it
-- would have produced something else. FW_Format_Time renders the sample from the pattern, so
-- the two cannot disagree.
--
-- No FW_ prefix, so it was never a framework table by the naming convention either.
IF OBJECT_ID('dbo.TimeFormat', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.TimeFormat;
END
GO

-- Registration 1 was pointing at dd/MM/yyyy, inherited from an ID set before there was a table
-- to point at. Month-first is what it wants.
UPDATE dbo.FW_Registration
   SET FormatDateID = (SELECT FormatDateID FROM dbo.FW_Format_Date WHERE FormatPattern = 'MM/dd/yyyy')
 WHERE RegistrationID = 1;
GO
