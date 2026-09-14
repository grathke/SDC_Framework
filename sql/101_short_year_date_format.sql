-- A two-digit year, as a choice rather than the default.
--
-- MM/dd/yyyy stays first and stays what a new registration gets: a two-digit year is shorter,
-- not clearer, and "03/15/26" has to be read twice on anything that spans centuries - a birth
-- date most of all.
--
-- Placed at 15 so it sits directly under the four-digit form it is a variant of, rather than at
-- the end where it reads as unrelated.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_Format_Date WHERE FormatPattern = 'MM/dd/yy')
BEGIN
    INSERT INTO dbo.FW_Format_Date (FormatPattern, Description, DisplayOrder)
    VALUES ('MM/dd/yy', 'Month first, short year', 15);
END
GO
