# QBE in SQL, not in memory

**Proposed, nothing built.** Written 2026-09-21 from measurements taken the same morning.

Read this before changing `DataAccess.GetBrowseRowsByRegistration`, the QBE filter path, or the
row cap. Section 4 is the part that makes this harder than it looks, and section 6 is why it must
not be done in one pass.

## Raised again and dropped again — 2026-09-21

Re-measured on `WX_Framework` and left unbuilt on purpose. Opening the Employees browse fetches
**10,014 rows to display 11**, filters them in memory, and trims last; the database answered in
5 ms with 0 ms of CPU. No `TOP` is ever sent — `LimitBrowseRows` is a client-side copy of the
first N rows of a `DataTable` that has already been built in full.

**The reason it was dropped is the deployment, not the code.** The database and the application
run on the same server, so those 10,014 rows never cross a network. The waste is materialisation
and garbage collection, measured in tens of milliseconds, against a table that is already the
largest in the database.

> **That premise was false, and the paragraph above is left standing as the record of it.**
> Corrected 2026-09-21: the database is on `BEELINK`, the application runs on `NucBox_EVO-T1`,
> and every one of those 10,014 rows has been crossing a LAN the whole time. The server name was
> printed at the top of every run and was never compared against the hostname. See section 11.

That reason expires the day the database moves to a machine of its own, or a table reaches a
size where materialising all of it is the cost rather than moving it. Nothing in the code will
say when that happens. **Raise it again on either of those, not before.**

### Raised again the same afternoon, and taken up — see section 9

It did not survive the day. Two things changed it: the whole refresh was instrumented rather than
a part of it, which showed the fetch to be 94-97% of everything; and Glenn said the deployment is
same-machine **for now**, with AWS a live possibility, so the target is the fewest rows that
answer the question rather than "fast enough on this box".

Section 9 holds the staged plan, what each stage is worth, and the one design decision that has
to be settled before any of it is built. The paragraphs above stay because the reasoning was
sound on the facts it had - and because the fact that changed was a deployment question, not a
measurement.

---

## 1. What happens today

A browse page with SQL in `FW_Pages` — which is every generated page — filters **in memory**:

1. Run the page SQL **without the criteria**. Every row of the table crosses the wire.
2. Build a `DataView.RowFilter` expression from the QBE grid.
3. `view.ToTable()` copies the matching rows into a new `DataTable`.
4. `LimitBrowseRows` caps that copy.

The cap is applied **last**, after all the work. That is why raising it from 11 to 200 on
2026-09-21 changed the timings not at all.

`ENGLISH_SEARCH_SPEC.md` section 8 records the three filter paths and which pages use each. This
document is about the first and largest of them.

## 2. What it costs, measured

`FW_Employees_B`, against 10,014 rows, 20 searches on 2026-09-21:

| | Database | Whole find | Difference |
|---|---|---|---|
| Before the cap rose | 118 ms | 242 ms | 124 ms |
| After the cap rose to 200 | 235 ms | 414 ms | 179 ms |

**Both halves are functions of the same ten thousand rows.** The database time is fetching them;
the difference is filtering and copying them. Neither is affected by how many rows the user ends
up seeing, which is why the cap could not help.

For contrast, `FW_Registration_B` — a table with a handful of rows — runs its SQL in 9 ms and
completes a Find in 146 ms. The fixed cost of a Find is real, but it is not this.

**The scaling is the argument, not the current numbers.** 400 ms against ten thousand rows is
tolerable. The same page against 100,000 permits fetches 100,000 rows to show somebody eleven, and
no index can help, because the `WHERE` never reaches SQL Server.

## 3. What to change

Wrap the page's own SQL and filter outside it:

```sql
SELECT TOP 200 * FROM ( <the page SQL> ) AS q
WHERE q.LastName LIKE @p0 AND q.DepartmentID = @p1
ORDER BY ...
```

**A wrapper, not an injected `WHERE`.** The page SQL selects aliases and expressions —
`SELECT e.FirstName + ' ' + e.LastName AS FullName` — and SQL Server will not accept an alias in
the `WHERE` of the statement that defines it. Wrapping makes every selected column, alias or not,
addressable by the name the QBE grid already knows it by. It also means the page SQL is never
parsed or rewritten, only quoted, which removes a whole class of failure.

Three things fall out for free:

- **The cap becomes a `TOP` in SQL**, so only the rows shown cross the wire.
- **Mid-string wildcards start working.** `DataView.RowFilter` permits a wildcard only at the start
  or end of a pattern and throws on `%Gl%nn%`; SQL Server does not care.
- **Indexes become usable** for `=` and `StartsWith`. They are not usable now at all.

`AddBrowseScopePredicate` already does something of this shape for the registration predicate,
including the `ORDER BY` split. It is worth reading before writing anything new, and it is the
precedent for how carefully this has to be done.

## 4. Why this is harder than it looks

**`ORDER BY` inside a subquery is illegal without `TOP`.** The page SQL frequently ends with one.
It must be lifted out of the wrapped statement and reapplied outside, or the wrap fails on most
pages. This alone is most of the work.

**Filter semantics are not the same in the two engines.** Everything in this list is a behaviour
change a user could notice:

| | `DataView.RowFilter` | SQL Server |
|---|---|---|
| Case | Depends on the `DataTable`'s locale, usually insensitive | Depends on the column's collation |
| Trailing spaces | Significant | Ignored in `=` comparisons |
| Nulls | `IS NULL` only | `IS NULL` only, but `<>` excludes nulls, which surprises people |
| Mid-string wildcard | Throws | Works |
| Date text | Parsed by .NET | Parsed by SQL Server, and differently |

**Parameters, never concatenation.** Filters go into a `DataView` expression today, where the
worst outcome is a throw. In SQL the same string concatenated into a `WHERE` is an injection.
Every value must be a parameter, and the column name must be validated against the result schema
rather than trusted.

**The other two paths must not be forgotten.** `FW_Entity` and `FW_Users` already filter in SQL by
their own code. Three implementations of one idea is what the Consolidation Guardrail exists to
prevent, so this should end with one filter builder, not a third.

**Client-side filtering cannot be removed entirely.** A page whose SQL cannot be wrapped — one
already using `TOP`, or a stored procedure — still needs the old path. It stays as a documented
fallback and writes a `FW_FallbackUsageLog` row saying so, which is exactly what that log is for.

## 4a. The three that would have failed silently, and how they are handled

Written 2026-09-21 after measuring rather than assuming.

### Collation — checked, and there is nothing to do

```
Server:    SQL_Latin1_General_CP1_CI_AS
Database:  SQL_Latin1_General_CP1_CI_AS
Text columns not matching the database default:  0
Case-sensitive text columns:                     0
```

`CI` is case-insensitive, every text column inherits it, and none overrides it. **So no `COLLATE`
clause is needed, and none should be added.** That matters beyond tidiness: `COLLATE` on a column
makes the comparison non-sargable, which would have thrown away the index use this whole change
exists to gain.

**Do not alter a collation to make search convenient.** `ALTER DATABASE … COLLATE` changes only
the default for new objects; fixing existing columns means `ALTER TABLE … ALTER COLUMN` per
column, which rebuilds every index on it and fails outright when the column is in a primary key,
a foreign key, a unique constraint, a computed column or a schema-bound view. Collation also
decides uniqueness, so a table holding `Smith` and `SMITH` refuses to rebuild its unique index
under a case-insensitive collation - a failure that arrives during an upgrade, on a customer's
data, at the worst moment.

What to build instead is a **check**: read `DATABASEPROPERTYEX(DB_NAME(), 'Collation')` at startup
and, only when it is case-sensitive, say so on the health page the way Query Store's state is
said - *"searches on this installation are case-sensitive"*. A customer's server may legitimately
be `CS`, and searches quietly matching nothing with no error is precisely the silent failure this
document is trying to avoid.

The one place to set a collation is `CREATE DATABASE`, if the framework ever creates its own
rather than being pointed at one.

### Dates and numbers — typed parameters, never text

Today the filter value is a string and SQL Server parses it, which is where format and locale
disagree. Instead: read the column's type from the result schema, parse the typed value in .NET,
and pass a typed `SqlParameter`. Nothing is parsed server-side, so nothing can disagree.

When .NET cannot parse what was typed, **refuse up front and name the field** - exactly as a
mid-string wildcard is refused today - rather than sending something and hoping. That converts the
worst failure mode in section 5 from "wrong rows, no error" into a message.

### Knowing there are more - fetch one more than you show

`SELECT TOP 201` when the cap is 200. If 201 rows come back there are more; show 200 and say so.

Exact, one extra row, no second query. Guessing from "exactly 200 arrived" is wrong precisely when
the total is exactly 200, and this is cheaper than being careful about that.

It answers the yes/no question and not "how many". A real total needs `COUNT(*)` over the same
predicate - a second round trip that roughly doubles the cost of every capped search, on exactly
the large tables this change is for. Not worth it unless somebody asks for the number.

## 5. What could break, and how it would show

| Path | Risk | How it shows |
|---|---|---|
| Page SQL ending in `ORDER BY` | Wrap produces invalid SQL | Every Find on that page throws |
| Page SQL with `TOP` | Wrap conflicts with the cap | Wrong row count, silently |
| Alias colliding with a base column | Ambiguous column | Throws, naming the column |
| Case-sensitive collation | Searches stop matching | Nothing found, no error — **the dangerous one** |
| Date criteria | Parsed differently | Wrong rows, no error — **also dangerous** |
| Computed or unmapped QBE column | No such column in the wrap | Throws |

The two marked dangerous fail **silently and plausibly**. A page that returns nothing looks like a
page with nothing to return.

## 6. How to do it without breaking the application

**Not in one pass, and not on every page at once.**

1. **Build the SQL filter builder beside the existing one**, used by nothing. Unit-test it against
   the operator list — equals, not equals, contains, starts with, ends with, typed `%`, null.
2. **Add a comparison harness**: run both paths for the same page and criteria and assert the same
   rows come back. This is the only honest way to find the semantic differences in section 4 —
   they will not be found by reading.
3. **Turn it on for one page**, behind a setting, and exercise it against real data.
4. **Widen it**, page by page, with the old path still there.
5. **Only then** consider removing the client-side path, and keep it for the pages that cannot
   wrap.

A restore point on `FW_Base_B` and `DataAccess` before step 3, not before step 1.

## 7. What to test

Beyond the harness in step 2:

- every QBE operator, against a column that is text, one that is a number, one that is a date and
  one that is nullable
- a criterion matching nothing, one matching everything, one matching exactly the cap
- two criteria together, which is where an `AND` built wrong stops being visible
- a page whose SQL has `ORDER BY`, one with a `JOIN`, one with an alias over an expression
- the cap message, both wordings, since the cap moves from after the filter to inside the query
- `FW_UsageCounter` before and after, on the same page and data, as the measurement that says
  whether any of this was worth doing

## 8. What this is not

**Not English search.** `ENGLISH_SEARCH_SPEC.md` proposes parsing a sentence into a predicate.
This proposes putting the predicate the QBE grid already builds into the right place. The two are
independent, and this one should come first: English search that filters in memory would inherit
every problem described here.

**Not a performance project.** The current numbers are tolerable. This is about what happens at
ten times the data, and about a `WHERE` clause being in the place where a database can act on it.

---

## 9. The staged plan, and what each stage is worth — 2026-09-21

Written after instrumenting the whole refresh rather than a part of it, and after Glenn said the
target is the fewest rows that answer the question, **because AWS is a possibility even though
the database and application share a machine today**. That reverses the reason section 4a gave
for leaving this alone.

### What was measured

`FW_Base_B` now times the whole of `RefreshGrid` and reports what its named steps do not account
for. One session, eight refreshes of `FW_Employees_B` (10,014 rows, cap 11):

```
833ms  fetch=779 strip=5 bind=6 fit=3 buttons=4 layout=32 other=4   <- first load
237ms  fetch=232 bind=4 other=1
177ms  fetch=174 bind=2 other=1
160ms  fetch=157 bind=2 other=1
166ms  fetch=160 strip=1 bind=2 fit=2 other=1
188ms  fetch=162 strip=1 bind=3 fit=20 other=2   <- a date search
151ms  fetch=145 bind=4 other=2
```

**`fetch` is 94-97% of every refresh.** Binding, headers, hiding, fitting, buttons, layout, the
QBE rebuild and the view-state restore together come to 5-15ms. `other` is 1-4ms, so the line
accounts for the whole refresh.

Splitting `fetch` further, using the data layer's own `BrowseQueryMilliseconds` against the same
session's counters: about **125ms is the `Fill`** - transferring and materialising the rows - and
**20-35ms is what happens after it**: the deleted-flag hydration, the full `DataTable` copy that
hydration makes, the QBE filter and the row trim.

Two corrections to what was said earlier the same day, recorded so they are not repeated:

- The `layout` step is not the problem. It was the largest of the steps *being measured*, which
  is not the same as being large: 32ms on a first load and zero afterwards.
- A Find does not average 481ms. That was a mean over 49 searches across four days, carried by
  two searches at 1,908ms on 2026-09-20 - from before the row cap became the rule. The current
  hourly figure is **252ms average, 835ms worst**, and the worst is the first load of a session.

### Stage 1 - the deleted state decided in SQL, not after the fetch

**What it buys.** A browse result with no `DeletedFlag` column is hydrated: one extra query for
every deleted key in the table, a `source.Copy()` of the entire result - a second full
materialisation - a per-row loop, then a filter. Five of the eight browse pages take that path
(`FW_Employees_B`, `FW_Registration_B`, `FW_SwitchUser_B`, `FW_UserAccessDiagnostic_B`,
`Roles_B`), and every future page will too, because it comes from the generator's SQL template.
Worth roughly 20-35ms and one round trip per refresh.

**Why it comes first.** While rows are removed after the fetch, no `TOP` can be correct. This is
the prerequisite, not an optimisation standing on its own.

**Verdict: worth doing, and do it first.** Its own saving is modest; it unlocks the stage that
matters.

### Stage 2 - `TOP cap + 1` when nothing filters afterwards

**What it buys.** The ~125ms `Fill`, and on AWS ten thousand rows not crossing a network. The
page shows the same eleven rows and the same message; only the fetch changes.

**Why `cap + 1`.** The framework knows the list is longer than the cap because more rows came
back than it asked for. Ask for exactly eleven and that signal is gone - eleven rows, and no way
to say whether there are twelve or twelve thousand. Twelve preserves "showing the first 11 of a
longer list" exactly as truthfully as today.

**When it is safe.** All three: no QBE criteria, the registration scoped in the SQL rather than
in memory, and no deleted-flag filtering after the fetch. `FW_Employees_B` already meets the
middle one - `WHERE E.[RegistrationID] = @RegistrationID` - and has `ORDER BY E.[EmployeeID]`,
its own clustered key, so the `TOP` is both deterministic and cheap for the server.

**Verdict: the one to do.** Largest measured saving for the least risk, and it is the shape the
whole chain should have had.

### Stage 3 - the criteria in the `WHERE`

**What it buys.** The filtered case, which stage 2 cannot touch: with criteria the cap is 200 and
all 10,014 rows are still fetched, filtered in memory and trimmed. This is what a user does when
they are actually searching.

**What it costs.** Section 4 is why wrapping arbitrary page SQL is harder than it looks, and
section 6 is why it must not be done in one pass. It also has to reach the Users browse, whose
inline SQL names its parameter after the field.

**What it does not buy.** `Contains` stays a scan. `LIKE '%x%'` cannot seek an index at any
scale, and `FW_Employees` has no index on a name column anyway - only `PK_Employees` on
`EmployeeID` and `IX_FW_Employees_UserId`. Today every text operator scans equally, so the
operator costs nothing.

**Verdict: worth it, last, and measure again first.** After stages 1 and 2 the numbers will be
different, and this is the stage whose risk is real.

### What not to do

- **Do not remove `Contains` for performance.** It buys nothing while no name column is indexed,
  and it is what makes the search usable. If name search ever needs to be fast the order is:
  index the column, and only then does the operator matter. `FirstLast` is a persisted computed
  column and can be indexed like any other.
- **Do not turn the grid into a pager.** Fewest rows per *round trip*, not fewest rows: latency
  does not shrink with row count, and on AWS eight fetches of 25 will lose to one fetch of 200.
- **Do not require a criterion before showing data.** Decided 2026-09-21: an empty grid on open
  teaches nobody what the page holds. The cap plus its message is the answer, and the cap is
  `FW_Registration.MaxRecordsNoQBE` - per registration, not a constant, and free to raise to
  whatever fills the grid. Note that after stage 2 the cap **is** the fetch, so it becomes a real
  lever rather than a number that changes nothing.

### The one unresolved design decision

The framework cannot append `ISNULL(DeletedFlag, 0) = 0` to a page's SQL, because it does not
know the alias. `FW_Employees_B` joins `FW_Employees` to itself for the manager, so an unqualified
`DeletedFlag` is ambiguous and SQL Server rejects the batch - a page that will not open. The
generator knows the alias when it writes the SQL; the framework does not when it reads it back.

Three candidates, none chosen: the generator emits the predicate into the SQL and the framework
rewrites it for Show Deleted; the base alias is recorded in `FW_Pages` beside the SQL; or the
alias is parsed out of the `FROM` clause. **This has to be settled before stage 1 is built**, and
it is the decision most likely to be got wrong quietly.

### Decision — 2026-09-21, later the same day

**Build the wrapper. Do not build stages 1 and 2 separately.**

Glenn: AWS is a strong possibility and coming. The criterion set out above was "go straight to the
wrapper if the move is near-term or stage 3 is happening regardless", and both now hold.

What that settles:

- **The `FW_Pages` alias column and the `-- base: E` comment marker are both dead.** Inside a
  wrapper every column is `q.<name>`, so the ambiguity that made stage 1 awkward does not arise.
  Neither idea needs building, and neither leaves anything behind.
- **Stages 1 and 2 are not skipped, they are subsumed.** The wrapper supplies the `TOP`, the
  deleted predicate and the criteria in one construct.
- **It is not an AWS-only optimisation.** Every part of it is faster on this machine too: the
  ~125ms `Fill` goes, the hydration query and its full `DataTable` copy go, and SQL Server scans
  its own rows instead of shipping them here to be scanned. AWS raises the value, not the shape.

**The safety valve is what makes going straight at it acceptable.** Any page whose SQL cannot be
wrapped with confidence falls back to exactly today's path and writes a `FW_FallbackUsageLog` row
naming the page and the reason. The transformation is therefore opt-in per page by construction,
and the log says which pages declined instead of leaving somebody to discover it.

**Known risk to check rather than assume:** a filter on a computed alias — `WHERE q.GenderID = 3`
where that is really `G.[GenderDescription]` — can stop SQL Server pushing the predicate into the
join, so it computes the join and then filters. Still far cheaper than shipping every row, but it
belongs in an execution plan check on the pages that alias a joined column.

**Not a risk:** the `ORDER BY` cost is unchanged. The server already sorts the whole result today,
`TOP` or no `TOP`.

---

## 10. The wiring contract — settled 2026-09-21, not yet built

`BrowseSqlWrapper` is committed in `ef1eb29` and **called by nothing**. This section is what the
next session needs, because all of it was settled in conversation and none of it is in the code.

### Where the SQL comes from, which is not `FW_Pages`

The SQL that runs is whatever `GetActiveBaseSql()` returns - the page's SQL box. It arrives there
four ways: from `FW_Pages`; copied from another page for the same table when this page has no row;
**typed by a user and applied at runtime**; or overridden in code, which `FW_SwitchUser_B`,
`Roles_B` and `FW_UserAccessDiagnostic_B` all do.

**So the wrapper must decide from the string it is about to execute, never from `FW_Pages`.** An
audit of `FW_Pages` is for planning only. This is what closes the hole the SQL box would otherwise
leave open.

### The deleted state - three cases, no schema change

Decided at runtime from facts already cached (`TableHasColumn`, `GetPrimaryKeyFieldName`):

1. **The result selects `DeletedFlag`** - `FW_HD_Issues_B`, `FW_HD_Issues_Support_B`,
   `FW_PageGeneration_B`. Predicate `ISNULL(q.[DeletedFlag], 0) = 0`, or `= 1` for Show Deleted.
2. **It does not, but the base table has the column** - the other five, and almost every page yet
   to be written. Join back on the key:
   `LEFT JOIN dbo.[<table>] AS d ON d.[<pk>] = q.[PK]`, then `ISNULL(d.[DeletedFlag], 0) = 0`.
   **`LEFT JOIN`, never `INNER`**: a row whose key finds no match must survive as not-deleted,
   because that is what the hydration does today. An inner join would silently drop it.
   The result key is `PK` first, then the table's own key name - the same resolution
   `ResolveResultKeyColumn` already uses.
3. **The base table has no `DeletedFlag`** - the four reference tables, the two `FW_Perm_` tables,
   messaging, `FW_SwitchUser`. No predicate, and Show Deleted is already unavailable there.

**Requiring `DeletedFlag` on every table was considered and rejected**: it contradicts the
`FW_Perm_` convention in `CLAUDE.md` ("and no other column"), it would light up a working Show
Deleted button on pages with no delete or restore behind it, because
`DeletedViewGuard.TableSupportsDeletedView` keys purely on the column's presence - and it would
not remove case 2 anyway, since the question is what the *result* exposes, not what the table has.

### The rest of the contract

- **`TOP` is the cap plus one.** The framework knows a list is longer than the cap only because it
  received one more row than it asked for. Ask for exactly the cap and that signal is gone, and
  "showing the first 11 of a longer list" can no longer be said truthfully.
- **`ORDER BY q.[PK]` when the page has none.** `FW_HD_Issues_B` and `FW_Registration_B` have no
  `ORDER BY` today, and every page aliases its key as `PK`. Supplied by the wrapper, so no page SQL
  changes. See the open question below.
- **Decline if in-memory registration scoping would apply** - a page whose SQL carries no
  registration predicate of its own. That also removes rows after the fetch.
- **When wrapped, skip the in-memory deleted step and the in-memory QBE filter.** This is the part
  most likely to be got wrong: leaving the deleted step in re-runs the hydration on the returned
  rows, handing back the query this change exists to remove, and in Show Deleted mode it filters
  twice. **When declined, skip nothing** - the old path runs exactly as today.
- **`LimitBrowseRows` stays unchanged.** It turns the cap+1 rows into the cap and sets
  `BrowseRowsLimited`, which is what the two status messages read.
- **Every decline writes a `FW_FallbackUsageLog` row** naming the page and the reason, so the
  pages that opted out are visible rather than discovered.

### What to verify, beyond a green build

Per page, and on at least one page that declines: open with no criteria; a text criterion; a date
criterion and a Between; Show Deleted and back; a saved search retrieved; the row-cap message in
both wordings; and the deleted employee. **`FW_Employees` registration 1 holds exactly one deleted
row, `EmployeeID` 14, and it is sixth in key order** - so a first page that shows it is the
regression test for the whole deleted branch, visible without counting anything.

### Open question, not a blocker

`FW_HD_Issues_B` has no `ORDER BY`, so the wrapper would order it by `PK` - oldest issue first,
which is almost certainly not what a support queue wants. `CreatedOn DESC` is the likely answer.
That belongs in the page's own SQL where it is visible, and it is a page decision rather than a
framework default.

---

## 11. Measured through a real Thinfinity session — 2026-09-21

The first run through the real server rather than `--tf-dev`, on a 1678x807 viewport:

```
Browse refresh FW_Employees_B: 3373ms  fetch=3297 strip=8 bind=5 hide=2 fit=11 buttons=5 layout=40 other=5
```

**3.4 seconds to show eleven rows, 98% of it the fetch.** The same page on the desktop the same
afternoon: 151-237ms. Everything that is not the fetch comes to 71ms and is irrelevant.

**Repeated, and it settles at about 900ms:**

```
13:29:38  3373ms  fetch=3297     <- first open of the session, cold
13:37:04   917ms  fetch=827
13:39:41   857ms  fetch=763
13:39:51    64ms  fetch=34       <- FW_Registration_B, a small table
```

So 3.4 seconds was cold start and should not be quoted. **The steady figure is ~900ms to show
eleven rows, 90% of it the fetch** - four to five times the desktop, through the path the
application is delivered by.

`FW_Registration_B` at 64ms is the control case: the same code, the same session, a small table,
no problem. Nothing here is about the page, the grid or the session overhead. It is the row
count, and only the row count.

### Two premises corrected by this measurement

**The database is not local.** It runs on `BEELINK`; the application runs on `NucBox_EVO-T1`.
Section 4a's "those rows never cross a network" was wrong, and every timing in this document
already included a LAN hop. `hostname` against `SDC_DB_SERVER` settles it in seconds and was
never checked. When deployed they will probably be co-located; AWS remains possible.

**So the pushdown is worth doing today, not at ten times the data.** Section 2 said "not a
performance project - the current numbers are tolerable". 3.4 seconds to display eleven rows is
not tolerable, and it is happening now, in the delivery path the application actually uses.

### And the instrumentation is what found it

Before this afternoon the breakdown began after the query returned, so this line would have read
`66ms post-query` and said nothing at all. A measurement that excludes the dominant term is worse
than none, because it looks like an answer.
