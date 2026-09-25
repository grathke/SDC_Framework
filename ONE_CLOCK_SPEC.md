# One clock: every stored time is UTC

**Proposed, not built. Written 2026-09-25.** Nothing here has been started beyond the stopgap in
section 2. Read section 3 before proposing a server setting instead, section 5 before touching any
data, and section 7 before calling any part of it done.

Read this before changing how a time is written, shown, searched or saved - `GETDATE()`,
`SYSUTCDATETIME()`, `DateTime.Now`, `Date.Today`, `SessionTime`, `FW_Base_B.UtcColumns`, a
date-time field on a `_U` page, or a date criterion in QBE.

---

## 1. The problem

Past Imports showed an import made at 19:04 as **11:04 PM**. The column was right; the display was
not. `FW_ImportBatches.ImportedOn` is written by `SYSUTCDATETIME()` and the grid shows what is
stored.

Looking further, **the database keeps two clocks**:

| Measured 2026-09-25 on `WX_Framework` | Count |
|---|---|
| Tables holding a date-time column | 37 |
| Date-time columns (`datetime` 44, `datetime2` 62) | 106 |
| Date-only columns (`date`) | 8 |
| Column defaults of `SYSUTCDATETIME()` | 18 |
| Column defaults of `GETDATE()` - server local | 13 |
| Code lines using `GETDATE()`, `DateTime.Now`, `Date.Today` and the like | 94 |
| Code lines already using `SYSUTCDATETIME()` or `UtcNow` | 58 |

One table, `FW_ImportBatches`, has one of each: `ImportedOn` UTC, `CreatedOn` local. Nothing on a
row says which clock a value came from, so **no display rule can be applied to all of them** - any
rule that converts every date-time puts the other half four or five hours wrong.

The question this answers is Glenn's: *can every date-time shown on a `_B` or `_U` page honour the
viewer's time zone?* Yes - once every stored time is UTC. Not before.

## 2. What is already built - the stopgap

`FW_Base_B.UtcColumns` (2026-09-25): a browse page names the columns its SQL returns in UTC, and the
grid shows them through `SessionTime.ToSessionZone` after the fetch. Past Imports names
`ImportedOn` and `UndoneOn`; nothing else uses it. Display only - a QBE criterion on those columns
still compares against the stored value.

It exists because the page was wrong that day. It is retired in phase E, when every date-time
converts without being named.

## 3. Why the code, not the server's time zone

Setting `BEELINK` to UTC was considered and set aside (2026-09-25):

- It fixes `GETDATE()` only. `DateTime.Now` runs on whichever machine runs the application -
  `NucBox_EVO-T1`, or the Thinfinity server - and stays local.
- It does nothing for the rows already written in Eastern time.
- It shifts everything else on that machine that reads the clock: SQL Agent, backups, logs.
- **Correctness would depend on a machine setting.** Move the database to a client's server or to
  `wd-html5` and nobody has to forget anything for it to break - they only have to not know.

`SYSUTCDATETIME()` and `DateTime.UtcNow` return UTC whatever the machine is set to. With the code
fixed, the server's zone stops mattering, and there is nothing to remember.

## 4. The rule, once built

**There are three kinds of time, not two** (Glenn, 2026-09-25 - an accident report saying 2 PM):

| Kind | Example | Stored | Shown |
|---|---|---|---|
| **Instant** - when the system did something | `CreatedOn`, `ImportedOn`, `LoggedOn` | UTC | the viewer's zone |
| **Date only** | `BirthDate`, `HireDate` | as typed | as typed |
| **Wall-clock** - what a clock said where something happened | an accident at 2 PM, a shift start, an appointment at a site | as typed | as typed |

A wall-clock time is a fact about a place. The accident happened at 2 PM there; an insurer or a
regulator wants 2:00 PM, not 12:00 PM because the manager reading it sits in Denver. Converting
it would change the fact.

**The naming rule, for new tables:** a date-time column meant as wall-clock time **ends in
`Local`** - `IncidentAtLocal`, `ShiftStartLocal`. It is never converted. Where knowing the zone
matters, the table carries it beside the time - `IncidentTimeZoneID`, the reporter's or the site's,
set once when the row is written - and the page shows `2:00 PM EDT`. Every other date-time column
is an instant. A column's type cannot say which it is - both are `datetime2` - so the name does,
as the naming conventions in `CLAUDE.md` already let a name carry a column's meaning. No column in
the database is wall-clock today (2026-09-25), so the rule costs nothing existing.

- **An instant column holds UTC.** Every date-time column not named `...Local`, in every table.
- **A date-only column holds a calendar date** - `BirthDate`, `HireDate`, `TerminationDate`,
  `StartDate`, `EndDate`, the licence dates. It is **never converted**. A birthday does not move
  because the viewer is in another zone.
- **A wall-clock column holds what was typed**, and is never converted - not on write, not on
  display, not in a search.
- **Shown in the viewer's zone:** the employee's own, else the registration's - what
  `SessionTime` already resolves. Everywhere: `_B` grids, `_U` fields, Hot Fields, reports.
- **Typed in the viewer's zone:** an instant entered on a `_U` page is read as the viewer's local
  time and converted to UTC before it is written. Rare - most instants are stamped, not typed; a
  time somebody types is usually wall-clock, and then it is stored as typed.
- **"Today" is the viewer's today**, not the server's. `ImportRules` already does this; the date
  pickers and QBE defaults do not.
- **Logs stay local.** `startup.log` is read by a developer on that machine, against that machine's
  clock. It is not data.

## 5. The work, in order

Each phase is shippable alone and leaves the application correct for what it covers. **Phase B
cannot come before phase A** - migrating data while code is still writing local time just makes
new mixed rows.

### Phase A - write UTC everywhere

- The 13 `GETDATE()` defaults become `SYSUTCDATETIME()` - **except `FW_Employees.HireDate`**, which is
  a date-only column and should default to the session's today in code, or not at all.
- The code sites, by file:

  | File | Sites |
  |---|---:|
  | `500_INFRASTRUCTURE/Data/DataAccess.vb` | 65 |
  | `500_INFRASTRUCTURE/Data/SavedImportDataAccess.vb` | 4 |
  | `500_INFRASTRUCTURE/Data/ImportBatchDataAccess.vb` | 3 |
  | `030_EMPLOYEES/FW_EmployeeImport.vb` | 3 |
  | `050_REGISTRATION/FW_Registration_U.vb`, `DisplayFormats.vb`, `QbeDateRangeDialog.vb`, `EmployeeImportReport.vb` | 2 each |
  | `BrowserDocument.vb`, `FW_Base_U.vb`, `QbeDateCell.vb`, `ImportRules.vb`, `EmployeeImportPlan.vb`, `SessionStarter.vb`, `Program.vb`, `FW_Health_B.vb`, `FW_HD_Issues_U.vb`, `PageGeneration_U.vb` | 1 each |

  Each site is one of three things, and has to be read to know which: a **stored timestamp**
  (becomes UTC), a **"today" for the viewer** (becomes the session's today), or **local by
  design** - a log line, a file name, a sample of a date format (stays). Not a search and replace.
- The stored procedure `usp_FW_ApplyAccessDiagnosticChanges` uses `GETDATE()`.
- One test: nothing written after phase A differs from `SYSUTCDATETIME()` at the moment of writing
  by more than a few seconds. Checkable from `FW_AuditTrail.LoggedOn` (already UTC) against the
  row's own `UpdatedOn` for the same save.

### Phase B - convert the rows already written

- **First, per column, which clock wrote it** - not per default. A column can have a UTC default and
  a code path that writes `GETDATE()`, or the other way round, and then it holds both. For each of
  the 106 columns: every writer (default, code, procedure) is listed, and the column is one of
  *all UTC*, *all local*, or *mixed since a known date*.
- **Local rows convert from Eastern**, the zone `BEELINK` was set to when they were written - not the
  registration's zone, because the server wrote them in its own.
  `value AT TIME ZONE 'Eastern Standard Time' AT TIME ZONE 'UTC'` handles daylight saving per row.
- The hour repeated when the clocks go back is ambiguous: 01:30 happened twice. SQL Server picks
  one. Accepted - an hour's doubt on a timestamp from that night, recorded here rather than
  discovered.
- **A backup before, one transaction per table, and a count of rows changed per column** reported
  before commit. `FW_AuditTrail` before/after images hold times as text inside JSON and are **not**
  rewritten - they record what the screen said at the time.

### Phase C - show every date-time in the viewer's zone

- `FW_Base_B`: after the fetch, every `DateTime` column in the result converts - the step
  `UtcColumns` does today, without the page naming anything - **except a column named `...Local`**,
  and a date-only column, which a `date` type already marks. A page whose SQL computes a date-time
  from a date-only column (rare) declares it as an exception instead.
- `FW_Base_U`: on load, a date-time field shows the converted value; on save, it converts back to
  UTC. **The concurrency token is the `RowVersion`, not a date**, so a conversion cannot cause a
  false conflict - checked in phase C, not assumed.
- Hot Fields, the people report, Help Desk (`FW_HD_Issues_U` already converts by hand - it moves to
  the shared path) and `FW_Health_B`.

### Phase D - search in the viewer's zone

- A QBE criterion on a date-time column is typed in the viewer's zone. **"Equals 25 September" is a
  local day**, which is a UTC range: local midnight to the next local midnight, each converted.
  Between, Greater Than and Less Than convert their bounds the same way.
- This has to reach all three filter paths `ENGLISH_SEARCH_SPEC.md` section 8 names - the SQL
  wrapper, the in-memory fallback and the Start Empty path - or a search gives different answers
  depending on which path the page took.
- Date-only and `...Local` columns are compared as typed, unconverted.

### Phase E - retire the stopgap

`UtcColumns` and its one override go. Past Imports converts like every other page.

## 6. Behaviour matrix

| Path | Before | After |
|---|---|---|
| Create - system stamp (`CreatedOn`) | UTC or local, by table | UTC |
| Update - system stamp (`UpdatedOn`) | UTC or local, by code path | UTC |
| Date-time typed on a `_U` | stored as typed | converted from the viewer's zone, stored UTC |
| Date-only typed on a `_U` | stored as typed | unchanged |
| Wall-clock (`...Local`) typed, shown or searched | - | stored, shown and searched as typed |
| Browse grid | shows stored value | viewer's zone |
| QBE on a date-time | compares stored value | viewer's day, as a UTC range |
| Save conflict | RowVersion | RowVersion - unchanged |
| Audit before/after | text of what was shown | unchanged; new rows show viewer-zone text |
| Import `Today` / `Now` | session's today / UTC now | unchanged - already right |
| Viewer with no zone set | stored value | UTC, labelled UTC where a label is shown |
| `startup.log` | local | local |

## 7. Done means

- Phase A: the per-site review is written down, every stored write is UTC, and the phase A test
  finds nothing.
- Phase B: the per-column table exists, the counts matched before commit, and a sample of rows
  checked by hand against a known event - the Past Imports batches, whose times are in their notes.
- Phase C and D: the browse and maintenance regression scripts pass, and in a Thinfinity session a
  time created on screen reads back as the same wall-clock time on a `_B`, a `_U` and in a QBE
  search - in two registrations set to different zones.
- One falsifiable check for the whole: **a record created at a known local time shows that time on
  every surface, for a viewer in that zone, and that time plus the zone difference for a viewer in
  another.**

## 8. Not doing

- Setting a server to UTC (section 3).
- `datetimeoffset` columns. Storing the offset beside the instant answers "what did the writer's
  clock say", which nothing here asks.
- Converting date-only columns, ever.
