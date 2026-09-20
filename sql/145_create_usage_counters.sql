-- 145_create_usage_counters.sql
--
-- How much the application is used, and how long it takes.
--
-- HEALTH_DASHBOARD_SPEC.md sections 1 and 9. One row per hour, per registration, per kind, per
-- page - not a row per event. At that granularity the table stays small enough to keep for ever,
-- and it holds no record values, no record keys and no search terms, which keeps it clear of the
-- privacy question a per-user activity log would raise. It answers "how much is this being used,
-- which pages, and how fast", never "what did this person look at".
--
-- Row-level read counting is deliberately out of scope. Counting every SELECT means instrumenting
-- the hot path and producing a table larger than everything it reports on. A Search, a BrowseOpen
-- and a RecordOpen are discrete, user-initiated events, a handful per person per day.
--
-- TWO TIMINGS, NOT ONE, and section 9 records why they cannot be collapsed:
--
--   DbMillis         the SQL alone, measured around the fill
--   PerceivedMillis  the whole Find - SQL plus bind, role-field removal, column hiding,
--                    fit-to-width, saved layout and repaint
--
-- GetBrowseRowsByRegistration has a custom-SQL path that runs the page SQL as-is and then applies
-- the QBE filters CLIENT-SIDE. On such a page Find pulls the whole result set and filters it in
-- memory: database time stays flat while perceived time grows with the table. That is a completely
-- different fix from "the query is slow", and invisible if only one number is kept.
--
-- Min and max as well as total. The average is total over count; the max is what gets complained
-- about, and an average hides it.
--
-- Perceived time is a FLOOR on what the user experienced, never the thing itself. The stopwatch
-- stops when the grid paints server-side; over Thinfinity the pixels still have to reach the
-- browser. Do not label it "response time" anywhere.

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.FW_UsageCounter', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_UsageCounter
    (
        UsageCounterID      int IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_FW_UsageCounter PRIMARY KEY,

        -- The bucket. HourUtc is the hour floor, so every event in that hour lands on one row.
        HourUtc             datetime2(0) NOT NULL,
        Kind                varchar(20) NOT NULL,          -- Search | BrowseOpen | RecordOpen
        PageName            varchar(100) NOT NULL,

        -- Nullable for the same reason FW_ErrorLog's is: something can happen before anybody has
        -- a registration. Also the empty string for PageName is used rather than NULL, so the
        -- unique index below can do its job - NULLs do not compare equal to each other.
        RegistrationID      int NULL,

        EventCount          int NOT NULL CONSTRAINT DF_FW_UsageCounter_EventCount DEFAULT (0),

        DbMillisTotal       bigint NOT NULL CONSTRAINT DF_FW_UsageCounter_DbTotal DEFAULT (0),
        DbMillisMin         int NULL,
        DbMillisMax         int NULL,

        PerceivedMillisTotal bigint NOT NULL CONSTRAINT DF_FW_UsageCounter_PerTotal DEFAULT (0),
        PerceivedMillisMin  int NULL,
        PerceivedMillisMax  int NULL,

        DeletedFlag         bit NOT NULL CONSTRAINT DF_FW_UsageCounter_DeletedFlag DEFAULT (0),
        DeletedBy           int NULL,
        DeletedOn           datetime2(0) NULL,

        RowVersion          rowversion NOT NULL
    );

    -- The upsert looks a bucket up on every flush, so this is the path that matters.
    -- RegistrationID is coalesced to -1 in the key because NULL never equals NULL in an index,
    -- and two flushes for a registration-less event must land on the same row rather than
    -- creating one each.
    CREATE UNIQUE INDEX UX_FW_UsageCounter_Bucket
        ON dbo.FW_UsageCounter (HourUtc, Kind, PageName, RegistrationID);

    -- Reading it is always "lately", and usually by kind.
    CREATE INDEX IX_FW_UsageCounter_HourUtc
        ON dbo.FW_UsageCounter (HourUtc DESC, Kind)
        INCLUDE (PageName, EventCount, DbMillisTotal, PerceivedMillisTotal);

    PRINT 'CREATED: dbo.FW_UsageCounter';
END
ELSE
    PRINT 'SKIPPED: dbo.FW_UsageCounter already exists';
GO

SELECT COUNT(*) AS ExistingRows FROM dbo.FW_UsageCounter;
