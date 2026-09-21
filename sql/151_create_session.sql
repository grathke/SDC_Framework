-- 151_create_session.sql
--
-- FW_Session - one row per sign-in, from HEALTH_DASHBOARD_SPEC.md section 6.
--
-- Until now nothing recorded a successful login at all. FW_AuditTrail could prove who changed a
-- row but not when they signed in, so the session that produced a change was unrecorded, and
-- "who is using this right now" had no answer.
--
-- ONE ROW PER SESSION, and the hourly login count is a roll-up of these rather than a second
-- thing to write. Both were wanted; written independently they would be two records of one fact
-- that can disagree, and somebody would then have to work out which is lying.
--
-- THE START IS EXACT. THE END IS NOT ALWAYS, and EndReason says which kind it got:
--
--   Exit         the person left properly - exact
--   Disconnect   VirtualUI's OnClose - now near-exact, see below
--   Crash        a row whose process is gone - last activity only
--
-- DISCONNECT WAS ONCE LATE BY MINUTES AND NO LONGER IS. The grace was measured at 156 seconds and
-- at about 210 on 2026-09-11, and this file said never to subtract a constant for it because it
-- was not one. Remeasured on 2026-09-21: OnClose arrives as the tab closes, and the process is
-- gone about four and a half seconds later - matching the Reconnection timeout of 5 seconds on
-- the application profile, which is the setting nobody had read. Session 1041 then proved the end
-- is written inside that window rather than lost to the kill. Still subtract nothing: there is no
-- longer anything worth subtracting.
--
-- TWO DURATIONS, because the session end is the wrong measure of when somebody stopped:
--
--   Connected = StartedOn to EndedOn        what the server was carrying
--   Active    = StartedOn to LastActivityOn how long the person was actually there
--
-- On 2026-09-20 the health page sat open on this machine from 09:05 to 13:00 with nobody at the
-- desk. Connected would call that four hours; Active would not. Report the pair and never an
-- average of them - a figure between a generous measure and a strict one describes nothing.
--
-- LastActivityOn is derived rather than written per action. Its input is FW_UsageCounter, which
-- already records the searches and page opens; a write per click would be a round trip added to
-- every click.
--
-- NO HEARTBEAT. It is the classic round-trip multiplier - every user, every interval, for ever,
-- whether or not anything changed. Cybele support recommended one on 2026-09-21, for robustness
-- against a crashed process; it is still declined, because reconciling on ProcessID answers the
-- same question without the traffic. The original second reason - that a heartbeat would keep
-- beating through a three-minute grace with no browser attached - no longer holds now the grace
-- is five seconds, but the first reason is enough on its own.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.FW_Session', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_Session
    (
        SessionID           int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_Session PRIMARY KEY,

        UserID              int NOT NULL,
        RegistrationID      int NULL,
        RoleID              int NULL,

        StartedOn           datetime2(0) NOT NULL
            CONSTRAINT DF_FW_Session_StartedOn DEFAULT (SYSUTCDATETIME()),

        -- Null while the session is open. That is what "connected now" reads.
        EndedOn             datetime2(0) NULL,

        -- Exit | Disconnect | Crash. Null while open.
        EndReason           varchar(20) NULL,

        -- Set from FW_UsageCounter when the session ends, so Active can be worked out without a
        -- write per click. Null when the person did nothing at all.
        LastActivityOn      datetime2(0) NULL,

        -- Thinfinity or Desktop, and which machine the process ran on. Over Thinfinity the
        -- machine is the server, never the person's - which is exactly why the browser's own
        -- address is worth keeping separately when the SDK is wired in.
        SessionKind         varchar(20) NULL,
        MachineName         varchar(100) NULL,
        ClientAddress       varchar(64) NULL,
        AppVersion          varchar(40) NULL,

        -- A process that never wrote an end. The reconciler uses this to close it as a Crash.
        ProcessID           int NULL,

        DeletedFlag         bit NOT NULL CONSTRAINT DF_FW_Session_DeletedFlag DEFAULT (0),
        DeletedBy           int NULL,
        DeletedOn           datetime2(0) NULL,

        RowVersion          rowversion NOT NULL
    );

    -- "Who is connected now" is every row with no end, and it is the page's commonest read.
    CREATE INDEX IX_FW_Session_Open
        ON dbo.FW_Session (EndedOn, StartedOn DESC)
        INCLUDE (UserID, RegistrationID, SessionKind, LastActivityOn);

    -- The hourly sign-in count rolls up from here.
    CREATE INDEX IX_FW_Session_StartedOn
        ON dbo.FW_Session (StartedOn DESC)
        INCLUDE (UserID, RegistrationID, EndedOn, EndReason);

    PRINT 'CREATED: dbo.FW_Session';
END
ELSE
    PRINT 'SKIPPED: dbo.FW_Session already exists';
GO

SELECT COUNT(*) AS ExistingRows FROM dbo.FW_Session;
