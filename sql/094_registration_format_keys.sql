-- Finish what sql/093 could not: the two foreign keys.
--
-- FW_Registration already carried DateFormatID and TimeFormatID when the format tables were
-- created, and registration 2 held -1 in both - a sentinel for "not set" from before there was
-- anything to point at. An FK cannot accept it, and it does not need to: NULL already means the
-- framework default, so the sentinel has a real spelling now and this is it.
--
-- The keys are not only integrity. The page generator reads declared foreign keys and offers
-- each one as a Lookup with a choice of display column, so these are what make Date Format and
-- Time Format appear in Registration's field picker rather than having to be added by hand.
UPDATE dbo.FW_Registration SET DateFormatID = NULL WHERE DateFormatID IS NOT NULL AND DateFormatID <= 0;
UPDATE dbo.FW_Registration SET TimeFormatID = NULL WHERE TimeFormatID IS NOT NULL AND TimeFormatID <= 0;

-- Anything left pointing at a row that does not exist goes the same way, rather than blocking
-- the constraint for a value nobody can explain.
UPDATE dbo.FW_Registration SET DateFormatID = NULL
 WHERE DateFormatID IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_DateFormat f WHERE f.DateFormatID = dbo.FW_Registration.DateFormatID);

UPDATE dbo.FW_Registration SET TimeFormatID = NULL
 WHERE TimeFormatID IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.FW_TimeFormat f WHERE f.TimeFormatID = dbo.FW_Registration.TimeFormatID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Registration_DateFormat')
BEGIN
    ALTER TABLE dbo.FW_Registration WITH CHECK
        ADD CONSTRAINT FK_FW_Registration_DateFormat
        FOREIGN KEY (DateFormatID) REFERENCES dbo.FW_DateFormat (DateFormatID);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Registration_TimeFormat')
BEGIN
    ALTER TABLE dbo.FW_Registration WITH CHECK
        ADD CONSTRAINT FK_FW_Registration_TimeFormat
        FOREIGN KEY (TimeFormatID) REFERENCES dbo.FW_TimeFormat (TimeFormatID);
END
GO
