/*
    119_helpdesk_eastern_to_utc.sql

    Shifts the help desk timestamps from US Eastern to UTC: +4 hours (EDT, which is what was in
    force when these were written in August and September 2026).

    ** RUN ONCE. ** There is no marker in the data saying it has happened, so a second run shifts
    everything another four hours. The report at the end is the check.

    Only the FW_HD_ tables. Everything else written before the server moved to UTC is left alone -
    say so if it should follow.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

SELECT 'BEFORE' AS Stage, IssueNumber, CreatedOn, UpdatedOn, ClosedOn, FirstResponseOn
  FROM dbo.FW_HD_Issues ORDER BY IssueID;

BEGIN TRANSACTION;

UPDATE dbo.FW_HD_Issues
   SET CreatedOn       = DATEADD(HOUR, 4, CreatedOn),
       UpdatedOn       = DATEADD(HOUR, 4, UpdatedOn),
       ClosedOn        = DATEADD(HOUR, 4, ClosedOn),
       FirstResponseOn = DATEADD(HOUR, 4, FirstResponseOn),
       DeletedOn       = DATEADD(HOUR, 4, DeletedOn);
PRINT 'FW_HD_Issues shifted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

UPDATE dbo.FW_HD_IssueAttachments
   SET CreatedOn = DATEADD(HOUR, 4, CreatedOn),
       UpdatedOn = DATEADD(HOUR, 4, UpdatedOn),
       DeletedOn = DATEADD(HOUR, 4, DeletedOn);
PRINT 'FW_HD_IssueAttachments shifted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

UPDATE dbo.FW_HD_IssueCategories
   SET CreatedOn = DATEADD(HOUR, 4, CreatedOn),
       UpdatedOn = DATEADD(HOUR, 4, UpdatedOn),
       DeletedOn = DATEADD(HOUR, 4, DeletedOn);
PRINT 'FW_HD_IssueCategories shifted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

COMMIT TRANSACTION;

SELECT 'AFTER' AS Stage, IssueNumber, CreatedOn, UpdatedOn, ClosedOn, FirstResponseOn
  FROM dbo.FW_HD_Issues ORDER BY IssueID;
