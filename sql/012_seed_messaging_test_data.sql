USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @RegistrationID INT = 1;
DECLARE @User1 INT = 1;
DECLARE @User2 INT = 2;
DECLARE @User3 INT = 3;
DECLARE @CreatedBy INT = 2;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_Users WHERE UserID = @User1 AND RegistrationID = @RegistrationID)
    THROW 50999, 'UserID 1 was not found in RegistrationID 1.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_Users WHERE UserID = @User2 AND RegistrationID = @RegistrationID)
    THROW 51000, 'UserID 2 was not found in RegistrationID 1.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.FW_Users WHERE UserID = @User3 AND RegistrationID = @RegistrationID)
    THROW 51001, 'UserID 3 was not found in RegistrationID 1.', 1;

BEGIN TRANSACTION;

DECLARE @ThreadID INT;
DECLARE @MessageID INT;

-- User 2 sends to User 3. User 3 receives an unread Inbox copy.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_MessageThreads WHERE RegistrationID = @RegistrationID AND Subject = 'TEST - User 2 to User 3')
BEGIN
    INSERT INTO dbo.FW_MessageThreads (RegistrationID, Subject, CreatedBy)
    VALUES (@RegistrationID, 'TEST - User 2 to User 3', @User2);
    SET @ThreadID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_Messages (ThreadID, RegistrationID, FromUserID, ToUserID, Body)
    VALUES (@ThreadID, @RegistrationID, @User2, @User3, 'Test message from User 2 to User 3. This should appear unread in User 3 Inbox.');
    SET @MessageID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_MessageRecipients (MessageID, ThreadID, RegistrationID, UserID, RecipientType, FolderName, IsRead)
    VALUES
        (@MessageID, @ThreadID, @RegistrationID, @User2, 'Se', 'Sent', 1),
        (@MessageID, @ThreadID, @RegistrationID, @User3, 'To', 'Inbox', 0);
END;

-- User 2 sends to User 1. User 1 receives an unread Inbox copy.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_MessageThreads WHERE RegistrationID = @RegistrationID AND Subject = 'TEST - User 2 to User 1')
BEGIN
    INSERT INTO dbo.FW_MessageThreads (RegistrationID, Subject, CreatedBy)
    VALUES (@RegistrationID, 'TEST - User 2 to User 1', @User2);
    SET @ThreadID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_Messages (ThreadID, RegistrationID, FromUserID, ToUserID, Body)
    VALUES (@ThreadID, @RegistrationID, @User2, @User1, 'Test message from User 2 to User 1. This should appear unread in User 1 Inbox.');
    SET @MessageID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_MessageRecipients (MessageID, ThreadID, RegistrationID, UserID, RecipientType, FolderName, IsRead)
    VALUES
        (@MessageID, @ThreadID, @RegistrationID, @User2, 'Se', 'Sent', 1),
        (@MessageID, @ThreadID, @RegistrationID, @User1, 'To', 'Inbox', 0);
END;

-- User 3 sends to User 2. User 2 receives a read Inbox copy.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_MessageThreads WHERE RegistrationID = @RegistrationID AND Subject = 'TEST - User 3 to User 2')
BEGIN
    INSERT INTO dbo.FW_MessageThreads (RegistrationID, Subject, CreatedBy)
    VALUES (@RegistrationID, 'TEST - User 3 to User 2', @User3);
    SET @ThreadID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_Messages (ThreadID, RegistrationID, FromUserID, ToUserID, Body)
    VALUES (@ThreadID, @RegistrationID, @User3, @User2, 'Test message from User 3 to User 2. This should appear read in User 2 Inbox.');
    SET @MessageID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_MessageRecipients (MessageID, ThreadID, RegistrationID, UserID, RecipientType, FolderName, IsRead, ReadOn)
    VALUES
        (@MessageID, @ThreadID, @RegistrationID, @User3, 'Se', 'Sent', 1, SYSUTCDATETIME()),
        (@MessageID, @ThreadID, @RegistrationID, @User2, 'To', 'Inbox', 1, SYSUTCDATETIME());
END;

-- User 2 has archived their sent copy. User 3 still has the Inbox copy.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_MessageThreads WHERE RegistrationID = @RegistrationID AND Subject = 'TEST - Archived by User 2')
BEGIN
    INSERT INTO dbo.FW_MessageThreads (RegistrationID, Subject, CreatedBy)
    VALUES (@RegistrationID, 'TEST - Archived by User 2', @User2);
    SET @ThreadID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_Messages (ThreadID, RegistrationID, FromUserID, ToUserID, Body)
    VALUES (@ThreadID, @RegistrationID, @User2, @User3, 'Test archived copy. User 2 should see this in Archive and can move it back to Inbox.');
    SET @MessageID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_MessageRecipients (MessageID, ThreadID, RegistrationID, UserID, RecipientType, FolderName, PreviousFolderName, IsRead)
    VALUES
        (@MessageID, @ThreadID, @RegistrationID, @User2, 'Se', 'Archive', 'Sent', 1),
        (@MessageID, @ThreadID, @RegistrationID, @User3, 'To', 'Inbox', NULL, 0);
END;

-- User 3 has a message in Trash that originated in Archive and should restore there.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_MessageThreads WHERE RegistrationID = @RegistrationID AND Subject = 'TEST - Trash from Archive')
BEGIN
    INSERT INTO dbo.FW_MessageThreads (RegistrationID, Subject, CreatedBy)
    VALUES (@RegistrationID, 'TEST - Trash from Archive', @User3);
    SET @ThreadID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_Messages (ThreadID, RegistrationID, FromUserID, ToUserID, Body)
    VALUES (@ThreadID, @RegistrationID, @User2, @User3, 'Test Trash item. Restore should return User 3''s copy to Archive.');
    SET @MessageID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_MessageRecipients (MessageID, ThreadID, RegistrationID, UserID, RecipientType, FolderName, PreviousFolderName, IsRead)
    VALUES
        (@MessageID, @ThreadID, @RegistrationID, @User2, 'Se', 'Sent', NULL, 1),
        (@MessageID, @ThreadID, @RegistrationID, @User3, 'To', 'Trash', 'Archive', 1);
END;

-- User 2 sends a message to themselves. It should be visible in both Inbox and Sent.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_MessageThreads WHERE RegistrationID = @RegistrationID AND Subject = 'TEST - User 2 Self Send')
BEGIN
    INSERT INTO dbo.FW_MessageThreads (RegistrationID, Subject, CreatedBy)
    VALUES (@RegistrationID, 'TEST - User 2 Self Send', @User2);
    SET @ThreadID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_Messages (ThreadID, RegistrationID, FromUserID, ToUserID, Body)
    VALUES (@ThreadID, @RegistrationID, @User2, @User2, 'Test self-send. This verifies separate Inbox and Sent placements for the same MessageID.');
    SET @MessageID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_MessageRecipients (MessageID, ThreadID, RegistrationID, UserID, RecipientType, FolderName, IsRead)
    VALUES
        (@MessageID, @ThreadID, @RegistrationID, @User2, 'Se', 'Sent', 1),
        (@MessageID, @ThreadID, @RegistrationID, @User2, 'To', 'Inbox', 0);
END;

-- User 3 sends to User 1 with User 2 copied. User 2 should not see this in Inbox because it is Cc.
IF NOT EXISTS (SELECT 1 FROM dbo.FW_MessageThreads WHERE RegistrationID = @RegistrationID AND Subject = 'TEST - Cc User 2')
BEGIN
    INSERT INTO dbo.FW_MessageThreads (RegistrationID, Subject, CreatedBy)
    VALUES (@RegistrationID, 'TEST - Cc User 2', @User3);
    SET @ThreadID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_Messages (ThreadID, RegistrationID, FromUserID, ToUserID, Body)
    VALUES (@ThreadID, @RegistrationID, @User3, @User1, 'Test Cc message. User 2 is copied and should not see this in Inbox as a Cc recipient.');
    SET @MessageID = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO dbo.FW_MessageRecipients (MessageID, ThreadID, RegistrationID, UserID, RecipientType, FolderName, IsRead)
    VALUES
        (@MessageID, @ThreadID, @RegistrationID, @User3, 'Se', 'Sent', 1),
        (@MessageID, @ThreadID, @RegistrationID, @User1, 'To', 'Inbox', 0),
        (@MessageID, @ThreadID, @RegistrationID, @User2, 'Cc', 'Inbox', 0);
END;

COMMIT TRANSACTION;

UPDATE m
SET FromUserName = COALESCE(NULLIF(LTRIM(RTRIM(fromUser.FirstLast)), ''), NULLIF(LTRIM(RTRIM(ISNULL(fromUser.FirstName, '') + ' ' + ISNULL(fromUser.LastName, ''))), ''), fromUser.Email),
    ToUserName = COALESCE(NULLIF(LTRIM(RTRIM(toUser.FirstLast)), ''), NULLIF(LTRIM(RTRIM(ISNULL(toUser.FirstName, '') + ' ' + ISNULL(toUser.LastName, ''))), ''), toUser.Email)
FROM dbo.FW_Messages m
LEFT JOIN dbo.FW_Users fromUser ON fromUser.UserID = m.FromUserID
LEFT JOIN dbo.FW_Users toUser ON toUser.UserID = m.ToUserID
WHERE m.RegistrationID = @RegistrationID
    AND (m.FromUserName IS NULL OR m.ToUserName IS NULL);

SELECT
    r.UserID,
    r.FolderName,
    r.PreviousFolderName,
    r.RecipientType,
    t.Subject,
    r.IsRead,
    m.FromUserID,
    m.ToUserID
FROM dbo.FW_MessageRecipients AS r
INNER JOIN dbo.FW_Messages AS m ON m.MessageID = r.MessageID
INNER JOIN dbo.FW_MessageThreads AS t ON t.ThreadID = r.ThreadID
WHERE r.RegistrationID = @RegistrationID
    AND r.UserID IN (@User1, @User2, @User3)
  AND t.Subject LIKE 'TEST - %'
ORDER BY r.UserID, r.FolderName, t.Subject;
