-- Generated from WX_Framework on 2026-09-23 by scripts\dump-schema.ps1
-- Do not edit by hand. Regenerate instead.

CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_DashboardLayouts_Icon] ON [dbo].[FW_DashboardLayouts] ([DashboardName], [ActionKey]);
CREATE NONCLUSTERED INDEX [IX_FW_Employees_UserId] ON [dbo].[FW_Employees] ([UserId]) WHERE ([DeletedFlag]=(0));
CREATE NONCLUSTERED INDEX [IX_FW_ErrorLog_LastSeen] ON [dbo].[FW_ErrorLog] ([LastSeen] DESC) INCLUDE ([ExceptionType], [PageName], [OccurrenceCount], [Acknowledged]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_ErrorLog_Fingerprint] ON [dbo].[FW_ErrorLog] ([Fingerprint]);
CREATE NONCLUSTERED INDEX [IX_FW_PageRequests_RequestName] ON [dbo].[FW_GeneratedPages] ([RequestName], [DeletedFlag]);
CREATE NONCLUSTERED INDEX [IX_FW_HD_IssueAttachments_ConversationEntry] ON [dbo].[FW_HD_IssueAttachments] ([RegistrationID], [IssueID], [ConversationEntryID], [DeletedFlag]);
CREATE NONCLUSTERED INDEX [IX_FW_HD_IssueAttachments_Issue] ON [dbo].[FW_HD_IssueAttachments] ([RegistrationID], [IssueID], [DeletedFlag], [CreatedOn]);
CREATE NONCLUSTERED INDEX [IX_FW_IssueCategories_RegistrationActive] ON [dbo].[FW_HD_IssueCategories] ([RegistrationID], [IsActive], [DisplayOrder]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_IssueCategories_RegistrationName] ON [dbo].[FW_HD_IssueCategories] ([RegistrationID], [CategoryName]) WHERE ([DeletedFlag]=(0));
CREATE NONCLUSTERED INDEX [IX_FW_HD_Issues_Assigned] ON [dbo].[FW_HD_Issues] ([RegistrationID], [AssignedSupportUserID], [Status], [UpdatedOn]);
CREATE NONCLUSTERED INDEX [IX_FW_HD_Issues_RegistrationStatus] ON [dbo].[FW_HD_Issues] ([RegistrationID], [Status], [UpdatedOn]);
CREATE NONCLUSTERED INDEX [IX_FW_HD_Issues_Reporter] ON [dbo].[FW_HD_Issues] ([RegistrationID], [ReporterUserID], [UpdatedOn]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_HD_Issues_IssueNumber] ON [dbo].[FW_HD_Issues] ([RegistrationID], [IssueNumber]) WHERE ([DeletedFlag]=(0));
CREATE NONCLUSTERED INDEX [IX_FW_LoginAttempt_AttemptedOn] ON [dbo].[FW_LoginAttempt] ([AttemptedOn] DESC) INCLUDE ([AttemptedUserName], [Reason], [RegistrationID]);
CREATE NONCLUSTERED INDEX [IX_FW_LoginAttempt_UserName] ON [dbo].[FW_LoginAttempt] ([AttemptedUserName], [AttemptedOn] DESC);
CREATE NONCLUSTERED INDEX [IX_FW_MessageRecipients_Unread] ON [dbo].[FW_MessageRecipients] ([RegistrationID], [UserID]) WHERE ([RecipientType]='To' AND [FolderName]='Inbox' AND [IsRead]=(0));
CREATE NONCLUSTERED INDEX [IX_FW_MessageRecipients_UserFolder] ON [dbo].[FW_MessageRecipients] ([RegistrationID], [UserID], [FolderName], [IsRead]);
CREATE NONCLUSTERED INDEX [IX_FW_Messages_Thread] ON [dbo].[FW_Messages] ([RegistrationID], [ThreadID], [SentOn]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_PageZooms_User_Page] ON [dbo].[FW_PageZooms] ([UserID], [PageName]);
CREATE NONCLUSTERED INDEX [IX_FW_SavedQbe_Lookup] ON [dbo].[FW_SavedQBE] ([RegistrationID], [UserID], [TableContext]);
CREATE NONCLUSTERED INDEX [IX_FW_Session_Open] ON [dbo].[FW_Session] ([EndedOn], [StartedOn] DESC) INCLUDE ([UserID], [RegistrationID], [SessionKind], [LastActivityOn]);
CREATE NONCLUSTERED INDEX [IX_FW_Session_StartedOn] ON [dbo].[FW_Session] ([StartedOn] DESC) INCLUDE ([UserID], [RegistrationID], [EndedOn], [EndReason]);
CREATE NONCLUSTERED INDEX [IX_FW_SwitchUser_Registration] ON [dbo].[FW_SwitchUser] ([RegistrationID], [LastName], [FirstName]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_SwitchUser_UserId] ON [dbo].[FW_SwitchUser] ([UserId]);
CREATE NONCLUSTERED INDEX [IX_FW_TableLayouts_Lookup] ON [dbo].[FW_TableLayouts] ([RegistrationID], [UserID], [PageName], [TableName], [LayoutType], [IsActive]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_TableLayouts_Default] ON [dbo].[FW_TableLayouts] ([RegistrationID], [PageName], [TableName], [LayoutType]) WHERE ([LayoutType]='Default' AND [IsActive]=(1));
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_TableLayouts_LastUsed] ON [dbo].[FW_TableLayouts] ([RegistrationID], [UserID], [PageName], [TableName], [LayoutType]) WHERE ([LayoutType]='LastUsed' AND [IsActive]=(1));
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_TableLayouts_QbeDefault] ON [dbo].[FW_TableLayouts] ([RegistrationID], [PageName], [TableName], [LayoutType]) WHERE ([LayoutType]='QbeDefault' AND [IsActive]=(1));
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_TableLayouts_UserNamed] ON [dbo].[FW_TableLayouts] ([RegistrationID], [UserID], [PageName], [TableName], [LayoutType], [LayoutName]) WHERE ([LayoutType]='UserNamed' AND [IsActive]=(1));
CREATE NONCLUSTERED INDEX [IX_FW_UsageCounter_HourUtc] ON [dbo].[FW_UsageCounter] ([HourUtc] DESC, [Kind]) INCLUDE ([PageName], [EventCount], [DbMillisTotal], [PerceivedMillisTotal]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_UsageCounter_Bucket] ON [dbo].[FW_UsageCounter] ([HourUtc], [Kind], [PageName], [RegistrationID]);
CREATE NONCLUSTERED INDEX [IX_FW_Users_Registration] ON [dbo].[FW_Users] ([RegistrationID]) INCLUDE ([UserName], [Email], [FirstName], [LastName]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_Users_UserName] ON [dbo].[FW_Users] ([UserName]) WHERE ([UserName] IS NOT NULL);
CREATE UNIQUE NONCLUSTERED INDEX [UX_FW_UserUiHints_UserHint] ON [dbo].[FW_UserUiHints] ([RegistrationID], [UserID], [HintKey]);
GO

