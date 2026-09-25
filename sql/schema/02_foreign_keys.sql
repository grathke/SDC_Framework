-- Generated from WX_Framework on 2026-09-23 by scripts\dump-schema.ps1
-- Do not edit by hand. Regenerate instead.

ALTER TABLE [dbo].[FW_EmployeeRoles] ADD CONSTRAINT [FK_FW_EmployeeRoles_Employee] FOREIGN KEY ([EmployeeID]) REFERENCES [dbo].[FW_Employees] ([EmployeeID]);
ALTER TABLE [dbo].[FW_EmployeeRoles] ADD CONSTRAINT [FK_FW_EmployeeRoles_Registration] FOREIGN KEY ([RegistrationID]) REFERENCES [dbo].[FW_Registration] ([RegistrationID]);
ALTER TABLE [dbo].[FW_EmployeeRoles] ADD CONSTRAINT [FK_FW_EmployeeRoles_Role] FOREIGN KEY ([RoleID]) REFERENCES [dbo].[FW_Roles] ([RoleID]);
ALTER TABLE [dbo].[FW_Employees] ADD CONSTRAINT [FK_FW_Employees_AssignedManager] FOREIGN KEY ([AssignedManagerID]) REFERENCES [dbo].[FW_Employees] ([EmployeeID]);
ALTER TABLE [dbo].[FW_Employees] ADD CONSTRAINT [FK_FW_Employees_FW_Registration] FOREIGN KEY ([RegistrationId]) REFERENCES [dbo].[FW_Registration] ([RegistrationID]);
ALTER TABLE [dbo].[FW_Employees] ADD CONSTRAINT [FK_FW_Employees_FW_TimeZones] FOREIGN KEY ([TimeZoneID]) REFERENCES [dbo].[FW_TimeZones] ([TimeZoneID]);
ALTER TABLE [dbo].[FW_Employees] ADD CONSTRAINT [FK_FW_Employees_Gender] FOREIGN KEY ([GenderID]) REFERENCES [dbo].[FW_Gender] ([GenderID]);
ALTER TABLE [dbo].[FW_Employees] ADD CONSTRAINT [FK_FW_Employees_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[FW_Users] ([UserId]);
ALTER TABLE [dbo].[FW_HD_IssueAttachments] ADD CONSTRAINT [FK_FW_HD_IssueAttachments_Issue] FOREIGN KEY ([IssueID]) REFERENCES [dbo].[FW_HD_Issues] ([IssueID]);
ALTER TABLE [dbo].[FW_HD_Issues] ADD CONSTRAINT [FK_FW_HD_Issues_Category] FOREIGN KEY ([CategoryID]) REFERENCES [dbo].[FW_HD_IssueCategories] ([CategoryID]);
ALTER TABLE [dbo].[FW_MessageRecipients] ADD CONSTRAINT [FK_FW_MessageRecipients_Message] FOREIGN KEY ([MessageID]) REFERENCES [dbo].[FW_Messages] ([MessageID]);
ALTER TABLE [dbo].[FW_MessageRecipients] ADD CONSTRAINT [FK_FW_MessageRecipients_Thread] FOREIGN KEY ([ThreadID]) REFERENCES [dbo].[FW_MessageThreads] ([ThreadID]);
ALTER TABLE [dbo].[FW_Messages] ADD CONSTRAINT [FK_FW_Messages_Thread] FOREIGN KEY ([ThreadID]) REFERENCES [dbo].[FW_MessageThreads] ([ThreadID]);
ALTER TABLE [dbo].[FW_Registration] ADD CONSTRAINT [FK_FW_Registration_Format_Date] FOREIGN KEY ([FormatDateID]) REFERENCES [dbo].[FW_Format_Date] ([FormatDateID]);
ALTER TABLE [dbo].[FW_Registration] ADD CONSTRAINT [FK_FW_Registration_Format_Time] FOREIGN KEY ([FormatTimeID]) REFERENCES [dbo].[FW_Format_Time] ([FormatTimeID]);
ALTER TABLE [dbo].[FW_Registration] ADD CONSTRAINT [FK_FW_Registration_FW_TimeZones] FOREIGN KEY ([TimeZoneID]) REFERENCES [dbo].[FW_TimeZones] ([TimeZoneID]);
ALTER TABLE [dbo].[FW_Registration] ADD CONSTRAINT [FK_FW_Registration_LicenseTerm] FOREIGN KEY ([LicenseTermID]) REFERENCES [dbo].[FW_LicenseTerms] ([LicenseTermID]);
ALTER TABLE [dbo].[FW_Registration] ADD CONSTRAINT [FK_FW_Registration_RegistrationType] FOREIGN KEY ([RegistrationTypeID]) REFERENCES [dbo].[FW_RegistrationType] ([RegistrationTypeID]);
ALTER TABLE [dbo].[FW_RoleDetails] ADD CONSTRAINT [FK_FW_RoleDetails_FW_RoleSchema] FOREIGN KEY ([FW_RoleSchemaID]) REFERENCES [dbo].[FW_RoleSchema] ([ID]);
ALTER TABLE [dbo].[FW_Roles] ADD CONSTRAINT [FK_FW_Roles_Registration] FOREIGN KEY ([RegistrationID]) REFERENCES [dbo].[FW_Registration] ([RegistrationID]);
ALTER TABLE [dbo].[FW_Users] ADD CONSTRAINT [FK_FW_Users_AssignedManagerID_FW_Users] FOREIGN KEY ([AssignedManagerID]) REFERENCES [dbo].[FW_Users] ([UserId]);
ALTER TABLE [dbo].[FW_Users] ADD CONSTRAINT [FK_FW_Users_FW_TimeZones] FOREIGN KEY ([TimeZoneID]) REFERENCES [dbo].[FW_TimeZones] ([TimeZoneID]);
ALTER TABLE [dbo].[FW_Users] ADD CONSTRAINT [FK_FW_Users_GenderID_FW_Gender] FOREIGN KEY ([GenderID]) REFERENCES [dbo].[FW_Gender] ([GenderID]);
ALTER TABLE [dbo].[FW_Users] ADD CONSTRAINT [FK_FW_Users_RegistrationID_FW_Registration] FOREIGN KEY ([RegistrationID]) REFERENCES [dbo].[FW_Registration] ([RegistrationID]);
GO

