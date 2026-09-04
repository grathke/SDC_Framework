/*
    072_current_user_view_excludes_deleted.sql
    ==============================================================================================
    dbo.vw_FW_CurrentUser stops returning soft-deleted users.

    ----------------------------------------------------------------------------------------------
    WHY
    ----------------------------------------------------------------------------------------------
    TryAuthenticate reads this view to find the account behind an email, and checked neither
    DeletedFlag nor IsActive. It found the record, confirmed a hash existed, validated it, and
    returned success.

    So deleting a user did not stop them signing in, and removing somebody's login privilege did
    not remove it. Both were live on 2026-09-04: sandy@gowisenow.com and asawyer@asboinc.com each
    had IsActive = 0 and a valid hash. LOGIN-03 in TEST_CASES.md recorded that an inactive user
    should be refused, and sat untested since it was written.

    The column was not merely unchecked - the view does not expose it, so the login code could not
    have checked it without this change.

    ----------------------------------------------------------------------------------------------
    WHY FILTER HERE AND CHECK IsActive IN CODE
    ----------------------------------------------------------------------------------------------
    They need different answers, so they are enforced in different places.

    A deleted account should not confirm it ever existed. Filtering it in the view means login
    reports the same "no user found" as an address that was never used - which is what it should
    say, because saying "that account was deleted" tells an attacker something and tells an honest
    user nothing they can act on.

    An inactive account is the opposite. That person exists, their password is correct, and the only
    useful thing to tell them is that their access was removed. So IsActive stays in the view and is
    checked in TryAuthenticate, which now answers "This account is not active. Ask an administrator
    to restore access." Filtering it here instead would have made login claim they do not exist and
    send them to reset a password that already works.

    ----------------------------------------------------------------------------------------------
    SCOPE
    ----------------------------------------------------------------------------------------------
    Three callers read this view, all in DataAccess: the login lookup, a registration name lookup,
    and one join. None of them wants a deleted user.

    The column list is unchanged. The WHERE clause is the only addition, so nothing that selects
    from it needs to change.

    Safe to run more than once - CREATE OR ALTER.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
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
    ON r.RegistrationID = u.RegistrationID
WHERE ISNULL(u.DeletedFlag, 0) = 0;
GO

PRINT 'vw_FW_CurrentUser now excludes soft-deleted users.';

SELECT UserId, Email, IsActive
FROM   dbo.vw_FW_CurrentUser
ORDER  BY UserId;
