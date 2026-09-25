/*
    167_usage_counter_rows.sql

    How many rows a search returned, beside how long it took.

    HEALTH_DASHBOARD_SPEC.md section 9.1. A page's search time is compared with its own history,
    and within one page the average moves when people search broadly one day and narrowly the
    next. The rows are recorded, not divided by: most of a small search is fixed cost, and a slow
    query often returns few rows, so milliseconds per row would make the wrong searches look bad.
    What the rows answer is "did the result grow too?" before a slowdown is called a regression.

    RowEventCount is its own column rather than EventCount, because rows before this migration -
    and an hour bucket that spans the upgrade - have events with no row count. The average is
    RowsTotal / RowEventCount; RowEventCount = 0 means not recorded, never "no rows".

    Additive and idempotent: an older build keeps writing the columns it knows, and these default.

    TO REVERSE:
      ALTER TABLE dbo.FW_UsageCounter DROP CONSTRAINT DF_FW_UsageCounter_RowEventCount,
                                           DF_FW_UsageCounter_RowsTotal;
      ALTER TABLE dbo.FW_UsageCounter DROP COLUMN RowEventCount, RowsTotal, RowsMax;
*/

SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.FW_UsageCounter', 'RowEventCount') IS NULL
BEGIN
    ALTER TABLE dbo.FW_UsageCounter ADD
        RowEventCount int    NOT NULL CONSTRAINT DF_FW_UsageCounter_RowEventCount DEFAULT (0),
        RowsTotal     bigint NOT NULL CONSTRAINT DF_FW_UsageCounter_RowsTotal DEFAULT (0),
        RowsMax       int    NULL;
    PRINT 'ADDED: FW_UsageCounter.RowEventCount, RowsTotal, RowsMax';
END
ELSE
    PRINT 'SKIPPED: FW_UsageCounter row columns already exist';
GO
