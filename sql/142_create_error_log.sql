/*
    FW_ErrorLog - one row per distinct fault, not per occurrence.

    The two global handlers in Program.vb already log an unhandled exception to startup.log, which
    is a file on the server nobody reads until something has gone wrong. This is the queryable
    half, and it also holds the faults that never reach a handler at all: the deliberate silent
    catches, where a nicety fails quietly on purpose and used to leave no trace anywhere.

    Keyed by Fingerprint rather than by occurrence. A crash loop on a page fifty people have open
    is fifty thousand faults and one problem, so a repeat increments OccurrenceCount and moves
    LastSeen instead of writing another row. That keeps the table small enough to read, and it is
    also what makes a Help Desk ticket per fault safe to build later - a fingerprint that already
    exists is not news.

    Fingerprint is a hash of exception type, page and the top frames of the stack with line numbers
    removed, so the same fault recognises itself after an edit that moves it down a file.

    Deliberately holds no record values. An exception message can carry a row's contents, a user
    name, a connection string, and this table is read across registrations - so Message is the
    exception's own text and nothing is interpolated into it here.

    Scoped by registration where one is known, and nullable where it is not: a fault during login
    or startup happens before anybody has a registration.
*/

IF OBJECT_ID('dbo.FW_ErrorLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_ErrorLog
    (
        ErrorLogID          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_ErrorLog PRIMARY KEY,

        -- Identity of the fault, not of the occurrence.
        Fingerprint         char(32) NOT NULL,
        ExceptionType       varchar(200) NOT NULL,
        PageName            varchar(100) NULL,
        Context             varchar(200) NULL,

        -- Most recent occurrence wins for these; they describe the fault, not its history.
        Message             nvarchar(1000) NULL,
        StackTrace          nvarchar(max) NULL,
        Origin              varchar(30) NOT NULL,          -- ThreadException | UnhandledException | Swallowed
        SessionKind         varchar(20) NULL,              -- Thinfinity | Desktop

        FirstSeen           datetime2(0) NOT NULL CONSTRAINT DF_FW_ErrorLog_FirstSeen DEFAULT (SYSUTCDATETIME()),
        LastSeen            datetime2(0) NOT NULL CONSTRAINT DF_FW_ErrorLog_LastSeen DEFAULT (SYSUTCDATETIME()),
        OccurrenceCount     int NOT NULL CONSTRAINT DF_FW_ErrorLog_OccurrenceCount DEFAULT (1),

        -- Where it was last seen. Not a history - the count above is the history.
        RegistrationID      int NULL,
        UserID              int NULL,
        MachineName         varchar(100) NULL,
        AppVersion          varchar(40) NULL,

        -- Set when somebody has looked at it, so a known fault stops shouting.
        Acknowledged        bit NOT NULL CONSTRAINT DF_FW_ErrorLog_Acknowledged DEFAULT (0),
        AcknowledgedBy      int NULL,
        AcknowledgedOn      datetime2(0) NULL,

        DeletedFlag         bit NOT NULL CONSTRAINT DF_FW_ErrorLog_DeletedFlag DEFAULT (0),
        DeletedBy           int NULL,
        DeletedOn           datetime2(0) NULL,

        RowVersion          rowversion NOT NULL
    );

    -- The upsert looks a fault up by fingerprint on every occurrence, so this is the hot path.
    CREATE UNIQUE INDEX UX_FW_ErrorLog_Fingerprint
        ON dbo.FW_ErrorLog (Fingerprint);

    -- Reading the log is "what has gone wrong lately", newest first.
    CREATE INDEX IX_FW_ErrorLog_LastSeen
        ON dbo.FW_ErrorLog (LastSeen DESC)
        INCLUDE (ExceptionType, PageName, OccurrenceCount, Acknowledged);
END

SELECT COUNT(*) AS ExistingRows FROM dbo.FW_ErrorLog;
