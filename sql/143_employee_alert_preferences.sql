-- 143_employee_alert_preferences.sql
--
-- Who gets told when something goes wrong.
--
-- Two independent flags rather than one exclusive choice, because nothing stops a role carrying
-- both Typ_AppAdmin and Typ_CompanyAdmin, so a person can legitimately be both and want both
-- kinds of mail. See HEALTH_DASHBOARD_SPEC.md section 7.
--
--   ReceivesSecurityAlerts  failed sign-ins for their own tenant - the customer's business
--   ReceivesFaultAlerts     crashes and fault fingerprints - ours
--
-- NULL rather than a default of 0. A bit that defaults to false cannot be told apart from one
-- somebody deliberately set to false, and the page requires an answer only while the person holds
-- an admin role - so "not applicable" and "asked and declined" are different states and the column
-- has to be able to say which. FW_Base_U's combo placeholder is what reads a NULL back.
--
-- NOT RUN as at 2026-09-20. Adding columns to FW_Employees is a schema change and needs its own
-- go-ahead. Running it is safe and reversible - ALTER TABLE ADD of a nullable column writes no
-- rows and holds no long lock - but the page work that reads these columns does not exist yet, so
-- there is nothing to gain by running it early.

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_Employees')
                 AND name = 'ReceivesSecurityAlerts')
BEGIN
    ALTER TABLE dbo.FW_Employees ADD ReceivesSecurityAlerts BIT NULL;
    PRINT 'ADDED: FW_Employees.ReceivesSecurityAlerts';
END
ELSE
    PRINT 'SKIPPED: FW_Employees.ReceivesSecurityAlerts already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_Employees')
                 AND name = 'ReceivesFaultAlerts')
BEGIN
    ALTER TABLE dbo.FW_Employees ADD ReceivesFaultAlerts BIT NULL;
    PRINT 'ADDED: FW_Employees.ReceivesFaultAlerts';
END
ELSE
    PRINT 'SKIPPED: FW_Employees.ReceivesFaultAlerts already exists';
GO

-- After running, still to do by hand - neither belongs in a migration:
--
--   1. Add both columns to the FW_Employees generation request so they appear on FW_Employees_U,
--      immediately after Email in MaintenanceFields. Do NOT put them in AdminRequiredFields: that
--      makes them required for every employee, and they are required only for administrators.
--      The conditional rule belongs beside ApplyAdminEmailRule in the hand-written half.
--
--   2. Roles will offer the new fields once FW_RoleSchema catches up, which a desktop (--no-tf)
--      run does at startup. A Thinfinity run will not: opening Roles is what syncs it there.
