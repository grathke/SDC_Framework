-- 125_backfill_license_terms.sql
--
-- The registrations that already had an expiry predate the term and the start date, so they read
-- as Custom - correctly, since nothing recorded how their expiry was arrived at.
--
-- Reconstructing it: 1 Year, anchored so that start + 365 lands exactly on the expiry already
-- stored. The expiry itself is not touched. That matters - the expiry is what the login licence
-- check reads, and a backfill that moved it would change when a registration stops working in
-- order to tidy up a label.
--
-- Only rows that have an expiry and no term. A registration that has since been given a term is
-- left alone, and running this twice changes nothing.

SET QUOTED_IDENTIFIER ON;
GO

DECLARE @oneYear int = (SELECT LicenseTermID FROM dbo.FW_LicenseTerms WHERE OffsetDays = 365);

IF @oneYear IS NOT NULL
    UPDATE dbo.FW_Registration
       SET LicenseTermID     = @oneYear,
           LicenseStart_Date = DATEADD(DAY, -365, LicenseExpiration_Date),
           UpdatedOn         = GETDATE()
     WHERE LicenseExpiration_Date IS NOT NULL
       AND LicenseTermID IS NULL;
GO
