USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.FW_HD_Issues', N'FirstResponseOn') IS NULL
BEGIN
    ALTER TABLE dbo.FW_HD_Issues ADD FirstResponseOn DATETIME2(0) NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.FW_HD_GetAdminDashboard
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
    LEFT JOIN dbo.FW_Registration AS registration ON registration.ID = issue.RegistrationID
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
    LEFT JOIN dbo.FW_Registration AS registration ON registration.ID = issue.RegistrationID
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
