-- 126_license_term_180_days.sql
--
-- Adds a 180 day term, and puts SARALAND on it.
--
-- The expiry is not moved. 125 reconstructed both registrations as 1 Year by anchoring the start
-- so that start + offset landed on the expiry already stored; this does the same with 180 days,
-- for the same reason - the expiry is what the login licence check reads, and it is not a label
-- to be adjusted so a term looks tidy.

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.FW_LicenseTerms WHERE OffsetDays = 180)
BEGIN
    -- Between 90 Days and 1 Year, so the list still reads shortest first.
    UPDATE dbo.FW_LicenseTerms SET DisplayOrder = DisplayOrder + 1 WHERE DisplayOrder >= 5;

    INSERT INTO dbo.FW_LicenseTerms (TermName, OffsetDays, DisplayOrder)
    VALUES ('180 Days', 180, 5);
END
GO

DECLARE @term int = (SELECT LicenseTermID FROM dbo.FW_LicenseTerms WHERE OffsetDays = 180);

IF @term IS NOT NULL
    UPDATE dbo.FW_Registration
       SET LicenseTermID     = @term,
           LicenseStart_Date = DATEADD(DAY, -180, LicenseExpiration_Date),
           UpdatedOn         = GETDATE()
     WHERE RegistrationID = 2
       AND LicenseExpiration_Date IS NOT NULL;
GO
