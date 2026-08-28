USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @RegistrationID INT = 1;
DECLARE @User1 INT = 1;
DECLARE @User2 INT = 2;
DECLARE @User3 INT = 3;
DECLARE @AdminUserID INT = @User2;
DECLARE @User1Name VARCHAR(200) = (SELECT FirstLast FROM dbo.FW_Users WHERE UserID = @User1 AND RegistrationID = @RegistrationID);
DECLARE @User2Name VARCHAR(200) = (SELECT FirstLast FROM dbo.FW_Users WHERE UserID = @User2 AND RegistrationID = @RegistrationID);
DECLARE @User3Name VARCHAR(200) = (SELECT FirstLast FROM dbo.FW_Users WHERE UserID = @User3 AND RegistrationID = @RegistrationID);
DECLARE @ToSupport VARCHAR(250) = 'TO:   SUPPORT' + CHAR(13) + CHAR(10);
DECLARE @FromUser1 VARCHAR(250) = 'FROM: ' + @User1Name + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);
DECLARE @FromUser2 VARCHAR(250) = 'FROM: ' + @User2Name + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);
DECLARE @FromSupport VARCHAR(250) = 'FROM: SUPPORT' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);
DECLARE @FromUser3 VARCHAR(250) = 'FROM: ' + @User3Name + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);
DECLARE @ToUser1 VARCHAR(250) = 'TO:   ' + @User1Name + CHAR(13) + CHAR(10);
DECLARE @ToUser3 VARCHAR(250) = 'TO:   ' + @User3Name + CHAR(13) + CHAR(10);
DECLARE @ConversationSeparator VARCHAR(100) = CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + '----------------------------------------' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10);
DECLARE @ProblemCategoryID INT = (SELECT TOP 1 CategoryID FROM dbo.FW_HD_IssueCategories WHERE RegistrationID IS NULL AND CategoryName = 'Problem' AND DeletedFlag = 0);
DECLARE @QuestionCategoryID INT = (SELECT TOP 1 CategoryID FROM dbo.FW_HD_IssueCategories WHERE RegistrationID IS NULL AND CategoryName = 'Question / How To' AND DeletedFlag = 0);
DECLARE @AccessCategoryID INT = (SELECT TOP 1 CategoryID FROM dbo.FW_HD_IssueCategories WHERE RegistrationID IS NULL AND CategoryName = 'Access / Permissions' AND DeletedFlag = 0);
DECLARE @BugCategoryID INT = (SELECT TOP 1 CategoryID FROM dbo.FW_HD_IssueCategories WHERE RegistrationID IS NULL AND CategoryName = 'Bug Report' AND DeletedFlag = 0);

IF NOT EXISTS (SELECT 1 FROM dbo.FW_Users WHERE UserID = @User1 AND RegistrationID = @RegistrationID)
    THROW 52001, 'UserID 1 was not found in RegistrationID 1.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_Users WHERE UserID = @User2 AND RegistrationID = @RegistrationID)
    THROW 52002, 'UserID 2 was not found in RegistrationID 1.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_Users WHERE UserID = @User3 AND RegistrationID = @RegistrationID)
    THROW 52003, 'UserID 3 was not found in RegistrationID 1.', 1;

IF NULLIF(LTRIM(RTRIM(@User1Name)), '') IS NULL OR NULLIF(LTRIM(RTRIM(@User2Name)), '') IS NULL OR NULLIF(LTRIM(RTRIM(@User3Name)), '') IS NULL
    THROW 52005, 'FirstLast is required for Help Desk seed users.', 1;

IF @ProblemCategoryID IS NULL OR @QuestionCategoryID IS NULL OR @AccessCategoryID IS NULL OR @BugCategoryID IS NULL
    THROW 52004, 'Help Desk categories are missing. Run 013_issue_categories.sql first.', 1;

BEGIN TRANSACTION;

-- ConversationText follows the application convention: newest entry first, original request last.
DELETE attachment
FROM dbo.FW_HD_IssueAttachments AS attachment
INNER JOIN dbo.FW_HD_Issues AS issue ON issue.IssueID = attachment.IssueID
WHERE issue.RegistrationID = @RegistrationID
    AND issue.IssueNumber LIKE 'HD-SEED-%';

DELETE FROM dbo.FW_HD_Issues
WHERE RegistrationID = @RegistrationID
    AND IssueNumber LIKE 'HD-SEED-%';

IF NOT EXISTS (SELECT 1 FROM dbo.FW_HD_Issues WHERE RegistrationID = @RegistrationID AND IssueNumber = 'HD-SEED-001' AND DeletedFlag = 0)
BEGIN
    INSERT INTO dbo.FW_HD_Issues
        (RegistrationID, IssueNumber, CategoryID, Subject, Description, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
    VALUES
        (@RegistrationID, 'HD-SEED-001', @ProblemCategoryID, 'Unable to open monthly report',
         'The monthly report opens to a blank screen.',
         '[2026-08-14 14:40 UTC]' + CHAR(13) + CHAR(10) + @ToUser1 + @FromSupport + 'The report permission was refreshed. Please try again.' + @ConversationSeparator +
         '[2026-08-14 14:05 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser1 + 'I still receive a blank screen after signing out and back in.' + @ConversationSeparator +
         '[2026-08-14 13:30 UTC]' + CHAR(13) + CHAR(10) + @ToUser1 + @FromSupport + 'We are checking your report access.' + @ConversationSeparator +
         '[2026-08-14 13:20 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser1 + 'This affects both the current month and the prior month.' + @ConversationSeparator +
         '[2026-08-14 13:10 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser1 + 'The monthly report opens to a blank screen.',
         5, 'Waiting for User', 'High', @User1, @AdminUserID, @User1, '2026-08-14T13:10:00', @AdminUserID, '2026-08-14T14:40:00');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_HD_Issues WHERE RegistrationID = @RegistrationID AND IssueNumber = 'HD-SEED-002' AND DeletedFlag = 0)
BEGIN
    INSERT INTO dbo.FW_HD_Issues
        (RegistrationID, IssueNumber, CategoryID, Subject, Description, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
    VALUES
        (@RegistrationID, 'HD-SEED-002', @QuestionCategoryID, 'How do I export a listing?',
         'Please provide the steps for exporting a browse listing to Excel.',
         '[2026-08-13 10:20 UTC]' + CHAR(13) + CHAR(10) + @ToUser1 + @FromSupport + 'Open the listing, choose the export command, then select Excel.' + @ConversationSeparator +
         '[2026-08-13 09:55 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser1 + 'Please provide the steps for exporting a browse listing to Excel.',
         2, 'Resolved', 'Low', @User1, @AdminUserID, @User1, '2026-08-13T09:55:00', @AdminUserID, '2026-08-13T10:20:00');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_HD_Issues WHERE RegistrationID = @RegistrationID AND IssueNumber = 'HD-SEED-003' AND DeletedFlag = 0)
BEGIN
    INSERT INTO dbo.FW_HD_Issues
        (RegistrationID, IssueNumber, CategoryID, Subject, Description, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
    VALUES
        (@RegistrationID, 'HD-SEED-003', @AccessCategoryID, 'Request access to application dashboard',
         'I need access to the application dashboard for daily support work.',
         '[2026-08-12 16:15 UTC]' + CHAR(13) + CHAR(10) + @ToUser3 + @FromSupport + 'The request is assigned for approval.' + @ConversationSeparator +
         '[2026-08-12 15:40 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser3 + 'I need access to the application dashboard for daily support work.',
         2, 'Assigned', 'Normal', @User3, @AdminUserID, @User3, '2026-08-12T15:40:00', @AdminUserID, '2026-08-12T16:15:00');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_HD_Issues WHERE RegistrationID = @RegistrationID AND IssueNumber = 'HD-SEED-004' AND DeletedFlag = 0)
BEGIN
    INSERT INTO dbo.FW_HD_Issues
        (RegistrationID, IssueNumber, CategoryID, Subject, Description, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
    VALUES
        (@RegistrationID, 'HD-SEED-004', @BugCategoryID, 'Search filter clears after refresh',
         'A saved search filter clears after I refresh the listing.',
         '[2026-08-11 11:25 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser3 + 'I can reproduce this after reopening the page.' + @ConversationSeparator +
         '[2026-08-11 10:50 UTC]' + CHAR(13) + CHAR(10) + @ToUser3 + @FromSupport + 'Can you confirm whether this happens after reopening the page?' + @ConversationSeparator +
         '[2026-08-11 10:30 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser3 + 'The same filter clears after I change to another registration.' + @ConversationSeparator +
         '[2026-08-11 10:15 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser3 + 'A saved search filter clears after I refresh the listing.',
         4, 'In Progress', 'Normal', @User3, @AdminUserID, @User3, '2026-08-11T10:15:00', @User3, '2026-08-11T11:25:00');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_HD_Issues WHERE RegistrationID = @RegistrationID AND IssueNumber = 'HD-SEED-005' AND DeletedFlag = 0)
BEGIN
    INSERT INTO dbo.FW_HD_Issues
        (RegistrationID, IssueNumber, CategoryID, Subject, Description, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
    VALUES
        (@RegistrationID, 'HD-SEED-005', @ProblemCategoryID, 'Correct duplicate customer address',
         'Two customer records have the same address and need review.',
         '[2026-08-10 08:30 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser1 + 'Two customer records have the same address and need review.',
         1, 'New', 'Normal', @User1, NULL, @User1, '2026-08-10T08:30:00', NULL, NULL);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_HD_Issues WHERE RegistrationID = @RegistrationID AND IssueNumber = 'HD-SEED-006' AND DeletedFlag = 0)
BEGIN
    INSERT INTO dbo.FW_HD_Issues
        (RegistrationID, IssueNumber, CategoryID, Subject, Description, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
    VALUES
        (@RegistrationID, 'HD-SEED-006', @QuestionCategoryID, 'Add a shortcut to recent messages',
         'A shortcut to recent messages would reduce navigation time.',
         '[2026-08-09 14:10 UTC]' + CHAR(13) + CHAR(10) + @ToUser3 + @FromSupport + 'The request was recorded for product review.' + @ConversationSeparator +
         '[2026-08-09 13:45 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser3 + 'A shortcut to recent messages would reduce navigation time.',
         2, 'Closed', 'Low', @User3, @AdminUserID, @User3, '2026-08-09T13:45:00', @AdminUserID, '2026-08-09T14:10:00');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_HD_Issues WHERE RegistrationID = @RegistrationID AND IssueNumber = 'HD-SEED-007' AND DeletedFlag = 0)
BEGIN
    INSERT INTO dbo.FW_HD_Issues
        (RegistrationID, IssueNumber, CategoryID, Subject, Description, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
    VALUES
        (@RegistrationID, 'HD-SEED-007', @ProblemCategoryID, 'User 2 cannot print issue history',
         'The print command produces no output from the Help Desk history view.',
         '[2026-08-15 15:05 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser2 + 'The print command produces no output from the Help Desk history view.',
         1, 'New', 'Normal', @User2, NULL, @User2, '2026-08-15T15:05:00', NULL, NULL);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_HD_Issues WHERE RegistrationID = @RegistrationID AND IssueNumber = 'HD-SEED-008' AND DeletedFlag = 0)
BEGIN
    INSERT INTO dbo.FW_HD_Issues
        (RegistrationID, IssueNumber, CategoryID, Subject, Description, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn)
    VALUES
        (@RegistrationID, 'HD-SEED-008', @AccessCategoryID, 'User 2 requests dashboard export access',
         'Please enable dashboard export for the current registration.',
         '[2026-08-15 14:30 UTC]' + CHAR(13) + CHAR(10) + @ToSupport + @FromUser2 + 'Please enable dashboard export for the current registration.',
         1, 'New', 'Normal', @User2, NULL, @User2, '2026-08-15T14:30:00', NULL, NULL);
END;

COMMIT TRANSACTION;

-- Seed fixtures start without metric timestamps; tests create them through the application workflow.
UPDATE dbo.FW_HD_Issues
SET FirstResponseOn = NULL,
        ClosedBy = NULL,
        ClosedOn = NULL
WHERE RegistrationID = @RegistrationID
    AND IssueNumber LIKE 'HD-SEED-%';

SELECT IssueID, IssueNumber, Subject, Status, Priority, ReporterUserID, AssignedSupportUserID, ConversationEntryCount
FROM dbo.FW_HD_Issues
WHERE RegistrationID = @RegistrationID
    AND IssueNumber LIKE 'HD-SEED-%'
  AND DeletedFlag = 0
ORDER BY IssueID;