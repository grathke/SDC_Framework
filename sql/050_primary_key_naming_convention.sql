/*
    050_primary_key_naming_convention.sql
    ----------------------------------------------------------------------------------------------
    Renames three primary keys to the convention now recorded in CLAUDE.md: <Stem>ID.

        FW_Entity.ID        -> EntityID
        FW_Registration.ID  -> RegistrationID
        FW_ZipCodes.PK      -> ZipCodeID

    Why
    ---
    A bare ID is the one key name that cannot survive a join: two of them collide in a single
    result set, which is why browse SQL has always had to alias the key AS PK. A named key also
    identifies its own target, which is what lets a relationship be suggested where none has been
    declared - the page generator reads declared foreign keys today and goes dark without one.

    FW_ZipCodes is the sharper case: its key is literally named PK, the same name the browse
    framework synthesises for every page's key. A real column and a synthesised one sharing a name
    in one framework is a collision waiting to happen.

    Safe to do because
    ------------------
    No foreign key references any of the three. Nothing points at them, so nothing can be left
    dangling. None of the 209 FW_RoleFields rows for these tables are keyed on the primary key
    either, so field-level permissions are untouched.

    What sp_rename does not do
    --------------------------
    It does not update views, procedures or stored SQL that name the column - those are left
    stale and fail at runtime with an invalid column name. Every one of them is re-scripted below,
    which is the whole reason this is a script rather than three renames:

        vw_FW_CurrentUser           joins ON r.ID = u.RegistrationID
        FW_HD_GetAdminDashboard     the same join, twice
        FW_RoleTables.Table_SQL     the Entity_B browse query
        FW_PageGeneration_B_U       the Entity_B generation request

    The view keeps every output column it has. Only its join changes.

    Rollback
    --------
    At the bottom of this file, and complete: renames reverse, and the view and procedure are
    re-scripted back to the ID they used before.
*/

EXEC sp_rename 'dbo.FW_Entity.ID',       'EntityID',       'COLUMN';
GO

EXEC sp_rename 'dbo.FW_Registration.ID', 'RegistrationID', 'COLUMN';
GO

EXEC sp_rename 'dbo.FW_ZipCodes.PK',     'ZipCodeID',      'COLUMN';
GO

/* ---------------------------------------------------------------------------------------------
   The view. Its column list is untouched; the join is the only line that changes.
   --------------------------------------------------------------------------------------------- */
ALTER VIEW dbo.vw_FW_CurrentUser AS
SELECT
    u.UserId AS UserId,
    u.RegistrationID,
    r.RegName,
    u.FirstName,
    u.LastName,
    u.FirstLast,
    u.LastFirst,
    u.Email,
    u.Phone,
    u.PasswordHash,
    u.IsActive,
    u.SuperAdmin
FROM dbo.FW_Users AS u
LEFT JOIN dbo.FW_Registration AS r
    ON r.RegistrationID = u.RegistrationID;
GO

/* ---------------------------------------------------------------------------------------------
   The stored procedure. Re-scripted from its live definition with the two joins repointed and
   nothing else altered.
   --------------------------------------------------------------------------------------------- */
ALTER PROCEDURE dbo.FW_HD_GetAdminDashboard
    @FilterType VARCHAR(30) = NULL,
    @FilterValue VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        COUNT(*) AS TotalTickets,
        SUM(CASE WHEN Status NOT IN ('Resolved', 'Closed') THEN 1 ELSE 0 END) AS OpenTickets,
        SUM(CASE WHEN Status IN ('New', 'Reopened') THEN 1 ELSE 0 END) AS AwaitingSupport,
        AVG(CASE WHEN FirstResponseOn IS NOT NULL
                 THEN CONVERT(DECIMAL(10, 2), DATEDIFF(MINUTE, CreatedOn, FirstResponseOn)) / 60.0
            END) AS AvgFirstResponseHours,
        AVG(CASE WHEN ClosedOn IS NOT NULL
                 THEN CONVERT(DECIMAL(10, 2), DATEDIFF(MINUTE, CreatedOn, ClosedOn)) / 60.0
            END) AS AvgResolutionHours
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0;

    SELECT Status AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0
    GROUP BY Status
    ORDER BY CASE Status
        WHEN 'New' THEN 1 WHEN 'Assigned' THEN 2 WHEN 'In Progress' THEN 3
        WHEN 'Waiting for User' THEN 4 WHEN 'Reopened' THEN 5
        WHEN 'Resolved' THEN 6 WHEN 'Closed' THEN 7 ELSE 8 END;

    SELECT Priority AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0
    GROUP BY Priority
    ORDER BY CASE Priority
        WHEN 'Critical' THEN 1 WHEN 'High' THEN 2 WHEN 'Normal' THEN 3
        WHEN 'Low' THEN 4 ELSE 5 END;

    SELECT ISNULL(category.CategoryName, 'Uncategorized') AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_HD_IssueCategories AS category ON category.CategoryID = issue.CategoryID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
    GROUP BY ISNULL(category.CategoryName, 'Uncategorized')
    ORDER BY TicketCount DESC, Bucket;

    SELECT issue.RegistrationID,
           ISNULL(registration.RegName, 'Unknown account') AS Bucket,
           COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_Registration AS registration ON registration.RegistrationID = issue.RegistrationID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
    GROUP BY issue.RegistrationID, ISNULL(registration.RegName, 'Unknown account')
    ORDER BY TicketCount DESC, Bucket;

    SELECT TOP 10
           issue.IssueID,
           issue.IssueNumber,
           issue.Subject,
           ISNULL(registration.RegName, 'Unknown account') AS AccountName,
           issue.Status,
           issue.Priority,
           issue.CreatedOn,
           issue.UpdatedOn
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_Registration AS registration ON registration.RegistrationID = issue.RegistrationID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
            AND (
                        @FilterType IS NULL
                        OR (@FilterType = 'All')
                        OR (@FilterType = 'Open' AND issue.Status NOT IN ('Resolved', 'Closed'))
                        OR (@FilterType = 'Awaiting' AND issue.Status IN ('New', 'Reopened'))
                        OR (@FilterType = 'Status' AND issue.Status = @FilterValue)
                        OR (@FilterType = 'Priority' AND issue.Priority = @FilterValue)
                        OR (@FilterType = 'Category' AND EXISTS (
                                SELECT 1 FROM dbo.FW_HD_IssueCategories AS filterCategory
                                WHERE filterCategory.CategoryID = issue.CategoryID
                                    AND filterCategory.CategoryName = @FilterValue))
                        OR (@FilterType = 'Registration' AND CONVERT(VARCHAR(30), issue.RegistrationID) = @FilterValue)
                    )
    ORDER BY issue.CreatedOn;
END;
GO

/* ---------------------------------------------------------------------------------------------
   The stored SQL rows. Both name the Entity key, and both are read back into a page at runtime.
   --------------------------------------------------------------------------------------------- */
UPDATE dbo.FW_RoleTables
SET Table_SQL = REPLACE(Table_SQL, 'E.ID AS PK', 'E.EntityID AS PK')
WHERE Table_SQL LIKE '%E.ID AS PK%';
GO

UPDATE dbo.FW_PageGeneration_B_U
SET BrowseSql = REPLACE(BrowseSql, 'E.[ID] AS PK', 'E.[EntityID] AS PK')
WHERE BrowseSql LIKE '%E.[ID] AS PK%';
GO

UPDATE dbo.FW_PageGeneration_B_U
SET BrowseSql = REPLACE(BrowseSql, 'E.ID AS PK', 'E.EntityID AS PK')
WHERE BrowseSql LIKE '%E.ID AS PK%';
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK
    ----------------------------------------------------------------------------------------------

EXEC sp_rename 'dbo.FW_Entity.EntityID',             'ID', 'COLUMN';
EXEC sp_rename 'dbo.FW_Registration.RegistrationID', 'ID', 'COLUMN';
EXEC sp_rename 'dbo.FW_ZipCodes.ZipCodeID',          'PK', 'COLUMN';
GO

ALTER VIEW dbo.vw_FW_CurrentUser AS
SELECT
    u.UserId AS UserId,
    u.RegistrationID,
    r.RegName,
    u.FirstName,
    u.LastName,
    u.FirstLast,
    u.Email,
    u.Phone,
    u.PasswordHash,
    u.IsActive,
    u.SuperAdmin
FROM dbo.FW_Users AS u
LEFT JOIN dbo.FW_Registration AS r
    ON r.ID = u.RegistrationID;
GO

UPDATE dbo.FW_RoleTables         SET Table_SQL = REPLACE(Table_SQL, 'E.EntityID AS PK', 'E.ID AS PK')     WHERE Table_SQL LIKE '%E.EntityID AS PK%';
UPDATE dbo.FW_PageGeneration_B_U SET BrowseSql = REPLACE(BrowseSql, 'E.[EntityID] AS PK', 'E.[ID] AS PK') WHERE BrowseSql LIKE '%E.[EntityID] AS PK%';
UPDATE dbo.FW_PageGeneration_B_U SET BrowseSql = REPLACE(BrowseSql, 'E.EntityID AS PK', 'E.ID AS PK')     WHERE BrowseSql LIKE '%E.EntityID AS PK%';
GO

    The stored procedure's rollback is the same edit in reverse: registration.RegistrationID back
    to registration.ID in both joins. Script it from source control rather than by hand.
*/
