-- FW_Registration.DateFormat and TimeFormat go; the ID columns are the answer.
--
-- The text columns held MM/DD/YYYY, HH:mm AP and MM-JJ-AAAA - descriptions in some other
-- convention, not .NET patterns, so nothing could have formatted a date with them. Whatever
-- they were for, FW_DateFormat and FW_TimeFormat now hold patterns that a picker and a grid can
-- both use, reached through DateFormatID and TimeFormatID.
--
-- Keeping both is the drift this replaces: registration 1 already had DateFormatID pointing at
-- dd/MM/yyyy while its DateFormat text said MM/DD/YYYY. Two statements of one fact, disagreeing,
-- with nothing to say which was meant.
--
-- No values are carried across. None of the three map to a pattern without guessing, and a
-- guessed date format is worse than the default, which is at least explicable.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'DateFormat')
BEGIN
    ALTER TABLE dbo.FW_Registration DROP COLUMN DateFormat;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'TimeFormat')
BEGIN
    ALTER TABLE dbo.FW_Registration DROP COLUMN TimeFormat;
END
GO

-- The field permissions for columns that no longer exist. Left behind they sit in the sweep
-- that reports role rows pointing at nothing, forever, until somebody learns to ignore it.
-- The ID rows stay - those are the fields the page will actually carry.
DELETE FROM dbo.FW_RoleFields
 WHERE FileLink IN ('FW_Registration.DateFormat', 'FW_Registration.TimeFormat');
GO
