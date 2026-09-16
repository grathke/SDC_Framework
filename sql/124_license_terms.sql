-- 124_license_terms.sql
--
-- A licence term is now a choice rather than a date typed from memory.
--
-- The date remains the truth: FW_Registration.LicenseExpiration_Date is what the login licence
-- check reads, and nothing consults the term. The term records what was intended, and the start
-- date records when that intent was applied - without it, a term measured from "today" stops
-- matching its own expiry date tomorrow, and every registration would read as Custom the day
-- after it was set.
--
-- Custom is the row with no offset at all. A date edited by hand lands on it, and so does a
-- date changed outside the application: the page re-applies the stored term to the stored start
-- and shows Custom when the two no longer agree, rather than a term that is no longer true.
--
-- Whole days, deliberately. A calendar month is 28 to 31 days, so "1 Month" applied on the 31st
-- means one thing to the person picking it and another to the arithmetic; a term of 30 days means
-- the same thing in February as in July and there is nothing left to disagree about.

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.FW_LicenseTerms', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_LicenseTerms (
        LicenseTermID  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_LicenseTerms PRIMARY KEY,
        TermName       varchar(50)  NOT NULL,
        OffsetDays     int          NULL,
        DisplayOrder   int          NOT NULL CONSTRAINT DF_FW_LicenseTerms_DisplayOrder DEFAULT (0),
        IsActive       bit          NOT NULL CONSTRAINT DF_FW_LicenseTerms_IsActive     DEFAULT (1),
        CreatedBy      int          NULL,
        CreatedOn      datetime     NULL CONSTRAINT DF_FW_LicenseTerms_CreatedOn DEFAULT (GETDATE()),
        UpdatedBy      int          NULL,
        UpdatedOn      datetime     NULL,
        DeletedFlag    bit          NOT NULL CONSTRAINT DF_FW_LicenseTerms_DeletedFlag DEFAULT (0),
        DeletedBy      int          NULL,
        DeletedOn      datetime     NULL,
        RowVersion     rowversion   NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.FW_LicenseTerms)
BEGIN
    INSERT INTO dbo.FW_LicenseTerms (TermName, OffsetDays, DisplayOrder)
    VALUES ('10 Days',       10,   1),
           ('30 Days',       30,   2),
           ('60 Days',       60,   3),
           ('90 Days',       90,   4),
           ('180 Days',      180,  5),
           ('1 Year',        365,  6),
           ('2 Years',       730,  7),
           ('Custom',        NULL, 8);
END
GO

IF COL_LENGTH('dbo.FW_Registration', 'LicenseTermID') IS NULL
    ALTER TABLE dbo.FW_Registration ADD LicenseTermID int NULL;
GO

IF COL_LENGTH('dbo.FW_Registration', 'LicenseStart_Date') IS NULL
    ALTER TABLE dbo.FW_Registration ADD LicenseStart_Date date NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Registration_LicenseTerm')
    ALTER TABLE dbo.FW_Registration
      ADD CONSTRAINT FK_FW_Registration_LicenseTerm
          FOREIGN KEY (LicenseTermID) REFERENCES dbo.FW_LicenseTerms (LicenseTermID);
GO

-- The registrations that already have an expiry predate the term, and no start date can be
-- invented for them. They stay Custom by having no term at all, which is what the page shows
-- when nothing is stored.
