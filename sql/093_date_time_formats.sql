-- Company display preferences: how this registration writes a date, and how it writes a time.
--
-- Two tables rather than one with a kind column. A single table needs every combo filtered to
-- the right half, and GetLookupTable filters only by registration - so one table would mean new
-- lookup machinery, or a registration whose date format is "hh:mm tt". Two tables of six rows
-- work with the combo path exactly as it is, and make the wrong choice unrepresentable.
--
-- FormatPattern is .NET custom format syntax because the same string has to drive all three
-- places a date appears: DateTimePicker.CustomFormat, a grid column's DefaultCellStyle.Format,
-- and ToString in a report. One column, or they drift.
--
-- No sample text column. The combo renders today's date through the pattern, so what the user
-- picks from is what they will see, and the label cannot disagree with the thing it labels.
IF OBJECT_ID('dbo.FW_DateFormat', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_DateFormat (
        DateFormatID  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_DateFormat PRIMARY KEY,
        FormatPattern varchar(40)  NOT NULL,
        Description   varchar(60)  NOT NULL,
        DisplayOrder  int          NOT NULL CONSTRAINT DF_FW_DateFormat_DisplayOrder DEFAULT (10),
        IsActive      bit          NOT NULL CONSTRAINT DF_FW_DateFormat_IsActive DEFAULT (1)
    );
END
GO

IF OBJECT_ID('dbo.FW_TimeFormat', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_TimeFormat (
        TimeFormatID  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_TimeFormat PRIMARY KEY,
        FormatPattern varchar(40)  NOT NULL,
        Description   varchar(60)  NOT NULL,
        DisplayOrder  int          NOT NULL CONSTRAINT DF_FW_TimeFormat_DisplayOrder DEFAULT (10),
        IsActive      bit          NOT NULL CONSTRAINT DF_FW_TimeFormat_IsActive DEFAULT (1)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.FW_DateFormat)
BEGIN
    INSERT INTO dbo.FW_DateFormat (FormatPattern, Description, DisplayOrder) VALUES
        ('MM/dd/yyyy',  'Month first, slashes',      10),
        ('dd/MM/yyyy',  'Day first, slashes',        20),
        ('yyyy-MM-dd',  'Year first, ISO',           30),
        ('MMM d, yyyy', 'Month name, short',         40),
        ('d MMM yyyy',  'Day first, month name',     50),
        ('MMMM d, yyyy','Month name, full',          60);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.FW_TimeFormat)
BEGIN
    INSERT INTO dbo.FW_TimeFormat (FormatPattern, Description, DisplayOrder) VALUES
        ('hh:mm tt',    '12 hour',                   10),
        ('hh:mm:ss tt', '12 hour with seconds',      20),
        ('HH:mm',       '24 hour',                   30),
        ('HH:mm:ss',    '24 hour with seconds',      40);
END
GO

-- NULL means the framework default. Nothing to backfill, and a registration that never answers
-- keeps the behaviour it has today.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'DateFormatID')
BEGIN
    ALTER TABLE dbo.FW_Registration ADD DateFormatID int NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.FW_Registration') AND name = 'TimeFormatID')
BEGIN
    ALTER TABLE dbo.FW_Registration ADD TimeFormatID int NULL;
END
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

-- Without an FW_RoleSchema row, Roles_U will not offer the table at all, and the sweep that
-- reports role rows pointing at nothing would never account for it. Table_Alias is what an
-- administrator should read, not the table name.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_RoleSchema WHERE DB_Table = 'FW_DateFormat')
BEGIN
    INSERT INTO dbo.FW_RoleSchema (DB_Table, Table_Alias, IsActive, CreatedBy, CreatedOn, DeletedFlag)
    VALUES ('FW_DateFormat', 'Date Format', 1, 0, GETDATE(), 0);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.FW_RoleSchema WHERE DB_Table = 'FW_TimeFormat')
BEGIN
    INSERT INTO dbo.FW_RoleSchema (DB_Table, Table_Alias, IsActive, CreatedBy, CreatedOn, DeletedFlag)
    VALUES ('FW_TimeFormat', 'Time Format', 1, 0, GETDATE(), 0);
END
GO
