-- 152_error_log_resolved_source.sql
--
-- FW_ErrorLog.ResolvedSource - who decided a fault was finished with.
--
-- sql/148 added Resolved, ResolvedBy, ResolvedOn and Resolution, and they cover a person sitting
-- at the health page pressing Fixed. They do not cover the other way a fault gets resolved: the
-- code being changed. ResolvedBy is a FW_Users id, and the developer working from a script is not
-- signed in - there is no id to write, and writing somebody else's would be a lie in a column
-- somebody will one day trust.
--
-- The distinction is not bookkeeping. "The administrator decided this is acceptable" and "the
-- code was changed" are different claims about the same row, and only the second one predicts
-- that the fault will stop happening. A fault resolved by a person and one resolved by an edit
-- deserve different amounts of scepticism when they come back, and RecurredAfterResolved is far
-- more damning against the second.
--
-- Null on every existing row, deliberately. Those were resolved before this column existed and
-- nothing records which kind they were; defaulting them to 'User' would invent an answer. Null
-- reads as "not recorded", which is what it is.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_ErrorLog') AND name = 'ResolvedSource')
BEGIN
    ALTER TABLE dbo.FW_ErrorLog ADD ResolvedSource varchar(20) NULL;
    PRINT 'ADDED: FW_ErrorLog.ResolvedSource';
END
ELSE
    PRINT 'SKIPPED: FW_ErrorLog.ResolvedSource already exists';
GO

SELECT ResolvedSource, COUNT(*) AS Rows
FROM dbo.FW_ErrorLog
GROUP BY ResolvedSource;
