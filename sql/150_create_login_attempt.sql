-- 150_create_login_attempt.sql
--
-- FW_LoginAttempt - one row per failed sign-in.
--
-- HEALTH_DASHBOARD_SPEC.md section 1. Nothing recorded a login before this: LoginForm held an
-- in-memory failedAttempts counter that drove the screen, reset when the form closed and persisted
-- nowhere. "Nobody can log in" is the worst outage this system has and it was entirely invisible.
--
-- ONE ROW PER ATTEMPT, not an hourly bucket. The pattern is the whole point - one user repeatedly,
-- or several accounts at once, or a run of names that do not exist - and a count per hour cannot
-- tell those apart. Successful sign-ins are the opposite case and are NOT stored here: they belong
-- to the session row of section 6, which is not built yet.
--
-- THE REASON IS A COLUMN, and that is what turns "I cannot log in" from a guessing game into a
-- diagnosis:
--
--   UnknownUser       no such user name
--   WrongPassword     the account exists and the password did not match
--   Inactive          the account exists and is switched off
--   DatabaseDown      the attempt never got as far as checking
--
-- IT MUST NEVER REACH THE LOGIN SCREEN. Telling somebody "unknown user" rather than "wrong
-- password" tells an attacker which names are real. The distinction lives here, where only an App
-- Admin reads it, and the screen keeps saying the one thing it says today.
--
-- REGISTRATIONID IS NULLABLE BECAUSE IT HAS TO BE. FW_Users carries its own RegistrationID, so a
-- wrong password against a real account is attributable before the password is even checked. An
-- unknown user name belongs to nobody - there is no user, so there is no registration - and those
-- alerts can only go to App Admin. A null here is a fact, not a gap.
--
-- ATTEMPTEDUSERNAME HOLDS WHAT WAS TYPED, which is not always a user name: people type their
-- password into the box above. That is why it stays in a table behind a login and never travels in
-- mail to anybody but the tenant it belongs to.
--
-- NO LOCKOUT. Recording is safe; locking people out is a policy decision with a support cost, and
-- it should be argued on its own. Nothing here counts toward one.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.FW_LoginAttempt', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_LoginAttempt
    (
        LoginAttemptID      int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_LoginAttempt PRIMARY KEY,

        AttemptedOn         datetime2(0) NOT NULL
            CONSTRAINT DF_FW_LoginAttempt_AttemptedOn DEFAULT (SYSUTCDATETIME()),

        -- What was typed. Not necessarily a user name - see the header.
        AttemptedUserName   varchar(100) NOT NULL,

        -- UnknownUser | WrongPassword | Inactive | DatabaseDown
        Reason              varchar(20) NOT NULL,

        -- Known only when the user exists. Null is a fact: nobody to attribute it to.
        RegistrationID      int NULL,
        UserID              int NULL,

        -- Where from, for telling one person's caps lock apart from a run of attempts.
        MachineName         varchar(100) NULL,
        SessionKind         varchar(20) NULL,
        AppVersion          varchar(40) NULL,

        DeletedFlag         bit NOT NULL CONSTRAINT DF_FW_LoginAttempt_DeletedFlag DEFAULT (0),
        DeletedBy           int NULL,
        DeletedOn           datetime2(0) NULL,

        RowVersion          rowversion NOT NULL
    );

    -- Reading it is always "lately", and usually by who or why.
    CREATE INDEX IX_FW_LoginAttempt_AttemptedOn
        ON dbo.FW_LoginAttempt (AttemptedOn DESC)
        INCLUDE (AttemptedUserName, Reason, RegistrationID);

    -- Counting attempts per name inside a window is what the three-strikes rule asks.
    CREATE INDEX IX_FW_LoginAttempt_UserName
        ON dbo.FW_LoginAttempt (AttemptedUserName, AttemptedOn DESC);

    PRINT 'CREATED: dbo.FW_LoginAttempt';
END
ELSE
    PRINT 'SKIPPED: dbo.FW_LoginAttempt already exists';
GO

SELECT COUNT(*) AS ExistingRows FROM dbo.FW_LoginAttempt;
