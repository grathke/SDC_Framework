-- 149_health_alert_recipient.sql
--
-- One flag, replacing the two added by sql/143.
--
-- 143 added ReceivesSecurityAlerts and ReceivesFaultAlerts on the reasoning that the two audiences
-- get different mail: a Company Admin hears about failed sign-ins at their own company, an App
-- Admin hears about faults. That reasoning still holds - what changed is who decides.
--
-- The ROLE decides which mail somebody gets. The checkbox decides only whether they get any. So
-- one column says everything two were saying, and somebody holding both admin roles gets both
-- kinds - which is what they would want, and what two independent flags made them ask for twice.
--
-- It is also what makes this a plain checkbox. Two flags with an "asked and declined" state
-- distinct from "never asked" needed a three-state control, which the page generator cannot emit -
-- it renders every bit column as a CheckBox. That pushed the design toward either a generator
-- change or hand-written controls in the companion half. One optional checkbox needs neither: the
-- generator already does exactly this, so the field can be added to the generation request and the
-- page regenerated without a line of framework code.
--
-- LEFT NULLABLE, and NULL means unticked. The three-state reasoning in 143 no longer applies, so
-- NOT NULL DEFAULT 0 would now be the tidier declaration - but nothing reads this column yet and
-- tidiness is not worth a second migration. Every read uses ISNULL(ReceivesHealthAlerts, 0) = 1.
--
-- SAFE TO RUN. Nothing reads the two columns being dropped: they were added on 2026-09-20 and no
-- page, query or report references them. Dropping them is not data loss, it is removing something
-- that was never filled in. Re-running does nothing.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'ReceivesHealthAlerts')
BEGIN
    ALTER TABLE dbo.FW_Employees ADD ReceivesHealthAlerts BIT NULL;
    PRINT 'ADDED: FW_Employees.ReceivesHealthAlerts';
END
ELSE
    PRINT 'SKIPPED: FW_Employees.ReceivesHealthAlerts already exists';
GO

-- Carry anything already ticked across, in case either column was filled in between 143 and now.
-- Expected to move nothing; it costs one statement and means the migration is not relying on that
-- expectation being true.
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'ReceivesSecurityAlerts')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'ReceivesHealthAlerts')
BEGIN
    EXEC sp_executesql N'
        UPDATE dbo.FW_Employees
        SET ReceivesHealthAlerts = 1
        WHERE ISNULL(ReceivesHealthAlerts, 0) = 0
          AND (ISNULL(ReceivesSecurityAlerts, 0) = 1 OR ISNULL(ReceivesFaultAlerts, 0) = 1);';

    PRINT 'CARRIED FORWARD: ' + CAST(@@ROWCOUNT AS varchar(10)) + ' row(s)';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'ReceivesSecurityAlerts')
BEGIN
    ALTER TABLE dbo.FW_Employees DROP COLUMN ReceivesSecurityAlerts;
    PRINT 'DROPPED: FW_Employees.ReceivesSecurityAlerts';
END
ELSE
    PRINT 'SKIPPED: FW_Employees.ReceivesSecurityAlerts already gone';
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name = 'ReceivesFaultAlerts')
BEGIN
    ALTER TABLE dbo.FW_Employees DROP COLUMN ReceivesFaultAlerts;
    PRINT 'DROPPED: FW_Employees.ReceivesFaultAlerts';
END
ELSE
    PRINT 'SKIPPED: FW_Employees.ReceivesFaultAlerts already gone';
GO

SELECT name, is_nullable FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.FW_Employees') AND name LIKE 'Receives%';

-- AFTERWARDS, by hand and by you - none of it belongs in a migration:
--
--   1. Add ReceivesHealthAlerts to the FW_Employees generation request (id 7), in
--      MaintenanceFields, after Email. Do NOT add it to AdminRequiredFields - it is optional, and
--      requiring it would force an answer from every employee who will never receive anything.
--
--   2. Regenerate the page. That needs a desktop run (--no-tf), because generation compiles what
--      it writes and a compile cannot run inside a browser session.
--
--   3. FW_RoleFields gains a row for the new field on the next startup schema sweep, which also
--      only happens on a desktop run.
--
-- The generator renders a bit column as a CheckBox with no configuration, so there is nothing to
-- choose and nothing to add to the framework.
