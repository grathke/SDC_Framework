-- 127_renumber_license_terms.sql
--
-- Puts LicenseTermID back in day order.
--
-- 180 Days was added after the fact and took the next identity, so the key read 1,2,3,4,8,5,6,7
-- against a list that is meant to climb. The combo sorts on DisplayOrder and never cared, but a
-- key that disagrees with the order of its own rows is read by people, not only by queries.
--
-- Only safe because these keys are days old and referenced from exactly one column. It is not a
-- pattern: renumbering a key that has been in use is how rows end up pointing at the wrong parent.
-- Registrations are re-pointed by OffsetDays rather than by the old id, so the mapping survives
-- the renumber.

SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;
GO

-- What each registration is on, by the thing that does not change.
SELECT r.RegistrationID, t.OffsetDays
  INTO #held
  FROM dbo.FW_Registration r
  JOIN dbo.FW_LicenseTerms t ON t.LicenseTermID = r.LicenseTermID;
GO

ALTER TABLE dbo.FW_Registration DROP CONSTRAINT FK_FW_Registration_LicenseTerm;
UPDATE dbo.FW_Registration SET LicenseTermID = NULL;
GO

DELETE FROM dbo.FW_LicenseTerms;
DBCC CHECKIDENT ('dbo.FW_LicenseTerms', RESEED, 0) WITH NO_INFOMSGS;
GO

INSERT INTO dbo.FW_LicenseTerms (TermName, OffsetDays, DisplayOrder)
VALUES ('10 Days',       10,   1),
       ('30 Days',       30,   2),
       ('60 Days',       60,   3),
       ('90 Days',       90,   4),
       ('180 Days',      180,  5),
       ('1 Year',        365,  6),
       ('2 Years',       730,  7),
       ('Custom',        NULL, 8);
GO

UPDATE r
   SET r.LicenseTermID = t.LicenseTermID
  FROM dbo.FW_Registration r
  JOIN #held h ON h.RegistrationID = r.RegistrationID
  JOIN dbo.FW_LicenseTerms t ON t.OffsetDays = h.OffsetDays;
GO

ALTER TABLE dbo.FW_Registration
  ADD CONSTRAINT FK_FW_Registration_LicenseTerm
      FOREIGN KEY (LicenseTermID) REFERENCES dbo.FW_LicenseTerms (LicenseTermID);
GO

DROP TABLE #held;
GO

COMMIT TRANSACTION;
GO
