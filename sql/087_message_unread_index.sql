-- 087_message_unread_index.sql
--
-- The unread check runs once per signed-in user per interval, and asks exactly one question:
-- how many rows in this user's inbox are unread. A filtered index answers it from a single page
-- rather than scanning recipients the query can never return.
--
-- Filtered on the same three constants the query uses, so the index holds only unread inbox mail
-- addressed to someone - a small fraction of the table, and one that shrinks as people read.
--
-- Safe to run more than once.
--
-- The two SET options are required rather than decorative: a filtered index cannot be created
-- unless both are ON, and sqlcmd connects with QUOTED_IDENTIFIER OFF.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_FW_MessageRecipients_Unread'
                 AND object_id = OBJECT_ID('dbo.FW_MessageRecipients'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_FW_MessageRecipients_Unread
        ON dbo.FW_MessageRecipients (RegistrationID, UserID)
        WHERE RecipientType = 'To' AND FolderName = 'Inbox' AND IsRead = 0;

    PRINT 'Created IX_FW_MessageRecipients_Unread.';
END
ELSE
BEGIN
    PRINT 'IX_FW_MessageRecipients_Unread already exists.';
END
GO
