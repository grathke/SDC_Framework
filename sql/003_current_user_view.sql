SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER VIEW dbo.vw_FW_CurrentUser AS
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
    ON r.ID = u.RegistrationID;
GO
