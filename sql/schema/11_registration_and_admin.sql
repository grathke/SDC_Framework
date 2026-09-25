-- Registration and bootstrap administrator. Run after 10.
-- Generated from WX_Framework on 2026-09-23 by scripts\dump-seed.ps1
-- Do not edit by hand. Regenerate instead.
--
-- The Smarty_* address-validation keys are deliberately absent. So are the columns
-- pointing at a company administrator and a help desk contact, which reference rows
-- that do not exist in a new database. CompanyAdmin_UserID is set at the end.

-- FW_Registration  (1 rows)
SET IDENTITY_INSERT dbo.[FW_Registration] ON;
GO
INSERT INTO dbo.[FW_Registration] ([RegistrationID], [RegistrationTypeID], [Address1], [Address2], [AllowMessaging], [AllowMultipleRoles], [AllowPasswordChangeAtLogin], [AllowUpdateMyProfile], [AllowUpdateMyProfileEmail], [City], [DisplayDashboardOnStartUp], [EndOfDay], [ExternalLogin], [LicenseExpiration_Date], [LogoutAtClose], [MainEmail], [MainFax], [MainPhone], [MaxRecordsNoQBE], [MaxUsers], [MessageRetrievalFrequency], [PasswordHash_Performed], [RegName], [State], [TwoFactorAuthentication], [VersionStartedOn], [WindowsLogin], [WebLandingPage], [Zip], [CreatedBy], [CreatedOn], [UpdatedBy], [UpdatedOn], [IsActive], [FormatDateID], [FormatTimeID], [TimeZoneID], [BTN_Create_Caption], [BTN_Read_Caption], [BTN_Update_Caption], [BTN_Delete_Caption], [DeletedFlag], [DeletedBy], [DeletedOn], [Smarty_UseEmbeddedKey], [HomeGraphic], [LicenseTermID], [LicenseStart_Date], [MaxRecordsWithQBE]) VALUES
    (1, 1, N'10941 sw dARDANELLE dR', NULL, 1, 1, 1, 1, 0, N'Port Saint Lucie', 1, NULL, NULL, '2026-12-31T00:00:00.000', 0, NULL, NULL, NULL, 11, 10, 5, 1, N'DEVELOPMENT TEAM', N'FL', 0, NULL, 0, N'https://www.gowisenow.com/city-permitting', N'34952', NULL, NULL, 2, '2026-09-22T19:20:47.833', 1, 1, 1, 2, N'Add', N'View', N'Edit', N'Delete', 0, NULL, NULL, 0, N'Go Wise Now.png', 6, '2025-12-31T00:00:00.000', NULL);
GO
SET IDENTITY_INSERT dbo.[FW_Registration] OFF;
GO

-- Bootstrap administrator: user name 'admin', password 'ChangeMe1'.
-- CHANGE THIS PASSWORD AS THE FIRST THING YOU DO. It is published in a repository.
SET IDENTITY_INSERT dbo.[FW_Users] ON;
GO
INSERT INTO dbo.[FW_Users] ([UserId], [RegistrationID], [UserName], [FirstName], [LastName],
       [IsActive], [SuperAdmin], [DeletedFlag], [Password], [PasswordHash], [CreatedBy], [CreatedOn])
VALUES (1, 1, N'admin', N'System', N'Administrator',
       1, 1, 0, N'#####', CONVERT(nvarchar(255), 0x4C34E49420079DFFF0F783353FE5547826EDDE2A32B44EBB044904F3954A561435468AE66567055C6FC4A931E4916060A8C968D4842CA5279476D0E3FDFFA657), 1, SYSUTCDATETIME());
GO
SET IDENTITY_INSERT dbo.[FW_Users] OFF;
GO

UPDATE dbo.FW_Registration SET CompanyAdmin_UserID = 1 WHERE RegistrationID = 1;
GO

