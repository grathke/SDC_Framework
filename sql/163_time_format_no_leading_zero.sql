-- 163_time_format_no_leading_zero.sql
--
-- A 12-hour time without the leading zero - 9:39 AM rather than 09:39 AM - as a choice beside
-- the existing one, not instead of it (Glenn, 2026-09-25: "I'd like to be able to use either").
-- DisplayOrder 15 sits it straight after the 12-hour format it differs from.

IF NOT EXISTS (SELECT 1 FROM dbo.FW_Format_Time WHERE FormatPattern = 'h:mm tt')
    INSERT INTO dbo.FW_Format_Time (FormatPattern, Description, DisplayOrder, IsActive)
    VALUES ('h:mm tt', '12 hour, no leading zero', 15, 1);
GO

-- And the same with seconds (Glenn, 2026-09-25): 9:05:07 AM beside 09:05:07 AM.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_Format_Time WHERE FormatPattern = 'h:mm:ss tt')
    INSERT INTO dbo.FW_Format_Time (FormatPattern, Description, DisplayOrder, IsActive)
    VALUES ('h:mm:ss tt', '12 hour with seconds, no leading zero', 25, 1);
GO
