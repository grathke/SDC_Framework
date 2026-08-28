USE WX_Framework;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.FW_HD_GetAdminDashboard
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: KPI values.
    SELECT
        COUNT(*) AS TotalTickets,
        SUM(CASE WHEN Status NOT IN ('Resolved', 'Closed') THEN 1 ELSE 0 END) AS OpenTickets,
        SUM(CASE WHEN Status IN ('New', 'Reopened') THEN 1 ELSE 0 END) AS AwaitingSupport,
        CAST(NULL AS DECIMAL(10, 2)) AS AvgFirstResponseHours,
        AVG(CASE WHEN ClosedOn IS NOT NULL
                 THEN CONVERT(DECIMAL(10, 2), DATEDIFF(MINUTE, CreatedOn, ClosedOn)) / 60.0
            END) AS AvgResolutionHours
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0;

    -- Result set 2: status counts.
    SELECT Status AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0
    GROUP BY Status
    ORDER BY CASE Status
        WHEN 'New' THEN 1 WHEN 'Assigned' THEN 2 WHEN 'In Progress' THEN 3
        WHEN 'Waiting for User' THEN 4 WHEN 'Reopened' THEN 5
        WHEN 'Resolved' THEN 6 WHEN 'Closed' THEN 7 ELSE 8 END;

    -- Result set 3: priority counts.
    SELECT Priority AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues
    WHERE ISNULL(DeletedFlag, 0) = 0
    GROUP BY Priority
    ORDER BY CASE Priority
        WHEN 'Critical' THEN 1 WHEN 'High' THEN 2 WHEN 'Normal' THEN 3
        WHEN 'Low' THEN 4 ELSE 5 END;

    -- Result set 4: category counts.
    SELECT ISNULL(category.CategoryName, 'Uncategorized') AS Bucket, COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_HD_IssueCategories AS category ON category.CategoryID = issue.CategoryID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
    GROUP BY ISNULL(category.CategoryName, 'Uncategorized')
    ORDER BY TicketCount DESC, Bucket;

    -- Result set 5: account counts.
    SELECT issue.RegistrationID,
           ISNULL(registration.RegName, 'Unknown account') AS Bucket,
           COUNT(*) AS TicketCount
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_Registration AS registration ON registration.ID = issue.RegistrationID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
    GROUP BY issue.RegistrationID, ISNULL(registration.RegName, 'Unknown account')
    ORDER BY TicketCount DESC, Bucket;

    -- Result set 6: oldest tickets awaiting support.
    SELECT TOP 10
           issue.IssueID,
           issue.IssueNumber,
           issue.Subject,
           ISNULL(registration.RegName, 'Unknown account') AS AccountName,
           issue.Status,
           issue.Priority,
           issue.CreatedOn
    FROM dbo.FW_HD_Issues AS issue
    LEFT JOIN dbo.FW_Registration AS registration ON registration.ID = issue.RegistrationID
    WHERE ISNULL(issue.DeletedFlag, 0) = 0
      AND issue.Status IN ('New', 'Reopened')
    ORDER BY issue.CreatedOn;
END;
GO
