-- 148_error_log_resolution.sql
--
-- Saying a fault has been fixed, and noticing when it comes back anyway.
--
-- FW_ErrorLog already carried Acknowledged, which means "I have seen this, stop counting it
-- against the health score". That covers a known problem somebody is living with. Resolved is a
-- different claim - the cause is gone - and the two are worth keeping apart: acknowledging a fault
-- you cannot fix today is sensible, and calling it fixed when it is not is how a fault gets
-- forgotten.
--
-- THE REASON THIS IS WORTH MORE THAN A CHECKBOX. A fault marked resolved that recurs is a far
-- louder signal than a new one: it means a fix did not work, or worked and was undone. The
-- fingerprint makes that detectable for free - a repeat of the same fault finds the same row and
-- increments OccurrenceCount - so Telemetry clears Resolved on a repeat while KEEPING the
-- Resolution text. The page can then say "resolved on the 20th, recurred", which is the most
-- useful line that panel will ever show. Dropping the text on a repeat would throw away the very
-- thing that makes the recurrence interesting: what somebody thought the fix was.
--
-- Resolution holds what was done. A commit hash beats prose - "Fixed in 32617a9 - gauge needed
-- SupportsTransparentBackColor" tells the next person exactly where to look, and prose rarely does.
--
-- Nullable throughout, and ResolvedOn deliberately separate from Resolved: a bit alone cannot say
-- when, and "when" is what turns a list of fixes into a history.
--
-- Safe to re-run.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_ErrorLog') AND name = 'Resolved')
BEGIN
    ALTER TABLE dbo.FW_ErrorLog ADD Resolved bit NOT NULL
        CONSTRAINT DF_FW_ErrorLog_Resolved DEFAULT (0);
    PRINT 'ADDED: FW_ErrorLog.Resolved';
END
ELSE
    PRINT 'SKIPPED: FW_ErrorLog.Resolved already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_ErrorLog') AND name = 'ResolvedBy')
BEGIN
    ALTER TABLE dbo.FW_ErrorLog ADD ResolvedBy int NULL;
    PRINT 'ADDED: FW_ErrorLog.ResolvedBy';
END
ELSE
    PRINT 'SKIPPED: FW_ErrorLog.ResolvedBy already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_ErrorLog') AND name = 'ResolvedOn')
BEGIN
    ALTER TABLE dbo.FW_ErrorLog ADD ResolvedOn datetime2(0) NULL;
    PRINT 'ADDED: FW_ErrorLog.ResolvedOn';
END
ELSE
    PRINT 'SKIPPED: FW_ErrorLog.ResolvedOn already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_ErrorLog') AND name = 'Resolution')
BEGIN
    ALTER TABLE dbo.FW_ErrorLog ADD Resolution nvarchar(1000) NULL;
    PRINT 'ADDED: FW_ErrorLog.Resolution';
END
ELSE
    PRINT 'SKIPPED: FW_ErrorLog.Resolution already exists';
GO

-- Set when a repeat arrives for a fault somebody had called fixed. Kept as its own column rather
-- than inferred from dates, because the interesting question is "has this ever come back after a
-- fix", and a row that is currently unresolved cannot answer it from LastSeen alone.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_ErrorLog') AND name = 'RecurredAfterResolved')
BEGIN
    ALTER TABLE dbo.FW_ErrorLog ADD RecurredAfterResolved bit NOT NULL
        CONSTRAINT DF_FW_ErrorLog_Recurred DEFAULT (0);
    PRINT 'ADDED: FW_ErrorLog.RecurredAfterResolved';
END
ELSE
    PRINT 'SKIPPED: FW_ErrorLog.RecurredAfterResolved already exists';
GO

SELECT name, is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.FW_ErrorLog')
  AND name IN ('Acknowledged', 'Resolved', 'ResolvedBy', 'ResolvedOn', 'Resolution', 'RecurredAfterResolved')
ORDER BY name;
