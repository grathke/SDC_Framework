# QBE in SQL, not in memory

**Built and working, 2026-09-21.** Written the same day from measurements taken that morning, and
finished that afternoon. **Section 12 is what was actually built, what it measured, and what is
still unproven — read it first.** Everything above section 12 is the reasoning that led there and
is kept because it records why each decision went the way it did; where it has since been
disproved, it says so in place.

Read this before changing `DataAccess.GetBrowseRowsByRegistration`, the QBE filter path, or the
row cap. Section 4 is the part that makes this harder than it looks, and section 4a is why a
filter value must be a typed parameter rather than text.

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
both wordings; and the deleted employee.

**The fixture is `EmployeeID` 13, and it is the only deleted row in registration 1.** Corrected
2026-09-21: it was 14, that row was restored while proving Show Deleted works, and 13 was deleted in
its place. 13 is the better fixture, because the page now orders by `LastFirst` and 13 is `AAA, AAA` -
it would sort to the very top of the first page. **A predicate that fails puts a wrong row in the
first position of the grid**, which needs no counting to see. Note that restoring the fixture is not
the same as it existing: check it rather than assume, since anybody proving Show Deleted works ends
up restoring the one row that makes the test possible.

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

---

## 12. What was built, and what it measured — 2026-09-21

`BrowseSqlWrapper` was committed in `ef1eb29` and called by nothing. It is now called by
`DataAccess.GetBrowseRowsByRegistration`, which is the method every browse page goes through.

### Where the change lives

**`DataAccess.vb` carries all of it.** `FW_Base_B` was not touched for the wiring, because
everything section 10 asks for — skip the in-memory deleted step, skip the in-memory QBE filter,
`TOP cap + 1`, leave `LimitBrowseRows` alone — happens inside that one method. No page changed, no
page knows.

`BrowseSqlWrapper.TryWrap` gained one optional parameter, `defaultOrderByColumn`. A page with no
`ORDER BY` of its own cannot be capped deterministically, and every browse query aliases its key
`AS PK`, so the caller passes `"PK"`. It is an **output name, not SQL**: the wrapper matches it
against the select list and quotes it, and a name the query does not produce declines the wrap.
`Roles_B`'s code-level `DefaultSelectSql` aliases nothing `AS PK` and is exactly that case.

### Three deliberate departures from section 10

**`NOT EXISTS`, not `LEFT JOIN`.** Section 10 describes joining the base table back on the key.
The wrapper accepts predicates and has no join slot, and
`NOT EXISTS (SELECT 1 FROM dbo.[t] AS d WHERE d.[pk] = q.[PK] AND ISNULL(d.[DeletedFlag], 0) = 1)`
has identical semantics — a key with no match survives as not-deleted, which is what the hydration
does — with no way for a join to multiply rows. Verified row-for-row against the hydration set
before any code was wired: both said 10,006 of 10,007.

**A decline is logged once per table and reason per run, not once per refresh.** A row per refresh
would put a write round trip on the path this change exists to shorten, and bury the fact in
thousands of identical rows.

**A wrapped query that throws, throws.** It is not retried unwrapped. A decline is a decision made
before running anything, on evidence; a catch-and-retry would be a guess made afterwards, it would
hide the wrap that needs fixing, and a `SqlException` is not necessarily about the wrap — a timeout
or a dropped connection would be answered by running the expensive query a second time against a
database that had just failed.

### The filter builder

`TryBuildBrowseSqlPredicates` sits beside `BuildDataViewFilterExpression`. Column names come from
the wrapper's `OutputNames` and are re-spelled from them, so nothing a caller supplies reaches the
SQL as an identifier. Every value is a typed `SqlParameter` named `@qbe0` onwards.

**It declines rather than dropping a criterion.** A dropped criterion returns more rows than were
asked for, which reads as everything having matched. Declining runs the old path, which behaves
exactly as it does today — including showing its own message for a date it cannot read.

`ResolveTextComparison` and `QbeDateBounds` still own what an operator means. The new builder calls
the same two the in-memory path and the Users page call, so the three cannot drift apart on
semantics. What is duplicated is the string assembly and the parameter naming, which is why the
Users page was left alone: consolidating it meant changing a working page on the strength of a
builder nobody had exercised.

**Types cost no round trip in the normal case.** The builder needs to know whether a column is a
bit, a number, a date or text — guessing from the value's shape is the silent failure in section 5.
Types are remembered from every fill, and a page opens unfiltered long before anyone searches it,
so the answer is already there. Only a filtered search against SQL that has never been run pays one
`SchemaOnly` call.

### What each page does

Checked against the SQL `FW_Pages` actually held, not against examples:

| Page | Result | Deleted case |
|---|---|---|
| `FW_Employees_B` | wraps | joins back on the key |
| `FW_HD_Issues_B` | wraps, `ORDER BY q.[PK]` supplied | joins back on the key |
| `FW_HD_Issues_Support_B` | wraps | joins back on the key |
| `FW_PageGeneration_B` | wraps | selects `DeletedFlag` |
| `FW_Registration_B` | wraps, `ORDER BY q.[PK]` supplied | joins back on the key |
| `FW_SwitchUser_B` | wraps | no `DeletedFlag` on the table |
| `FW_UserAccessDiagnostic_B` | wraps | joins back on the key |

**`Roles_B` is not in this table, and section 9's plan was wrong about it.** It does not call
`GetBrowseRowsByRegistration` at all — it calls `DataAccess.ExecuteCustomQuery` on its own path, so
the change cannot reach it. The empty `FW_FallbackUsageLog` is what revealed that: had it gone
through the new path it would have declined and written a row.

### Measured through Thinfinity, same page, same data

`FW_Employees_B`, 10,007 rows, cap 11:

```
before   149-260ms   fetch=136-241    db min 93ms
after     16- 33ms   fetch= 11- 25    db min 11ms
```

A `Between` search fetches in **12ms**. Before, every filtered search fetched all 10,007 rows and
filtered them here.

Server-side, measured before wiring: 465ms elapsed and 10,007 rows on the wire became **12ms and 12
rows**. Logical reads are a wash — 1,389 wrapped against 922 plus the hydration query's 461 — and
that is the honest figure: the deleted scan did not appear, it moved inside the one statement, and
a round trip went with it.

**The cold cost is once per server, not once per user.** First-ever execution was 1,528ms; after a
full rebuild with the plan cached it was 575ms; warm it is 16-33ms. The ~950ms difference is SQL
Server compiling a plan, which lives in the server, is shared by every user and every process, and
survives the application closing. The remaining ~500ms is per-process and **its composition is not
known** — it is not the connection, which login already opens and pools, and no further explanation
should be offered here without measuring it. It is smaller than the 912ms the old cold fetch cost.

### Two premises from sections 9 and 11 that the measurements disproved

**Thinfinity is not four to five times the desktop.** Section 11 read ~900ms steady against
149-237ms on the desktop and concluded the delivery path was the cause. Two Thinfinity sessions
hours apart, confirmed as Thinfinity by `FW_Session`, gave 823-917ms and then 149-260ms on the same
page and data. The variance is in something else, and the old path ranged from **149ms to 3,373ms
for the same eleven rows**. No explanation is offered; three were offered during this work and two
were wrong.

**The runtime SQL box is no longer a reason for anything.** Section 10 justifies deciding from the
SQL about to run partly because a user can type SQL at runtime. `ApplyCrudAccess` has hidden that
row from everyone including App Admin since 2026-09-10. The rule is still right for the live
reason: `FW_SwitchUser_B` and `FW_UserAccessDiagnostic_B` both rewrite their SQL in code before it
runs, so `FW_Pages` is not what executes.

### Verified in the running application

- warm and cold refreshes, with the figures above
- Show Deleted, the deleted row shown alone, and **restored** — the whole soft-delete cycle through
  the new predicate rather than through the hydration
- a `Between` date range, and a text `Contains`
- a criterion matching nothing: an empty grid, no error
- `Roles_B` unaffected, as it must be

### Still unproven, and this is the gap - PROVEN 2026-09-25

**Proven 2026-09-25, without touching FW_Pages.** A throwaway console program called
`GetBrowseRowsByRegistration` directly with two versions of FW_Employees_B's SQL: the page's own,
which wraps, and the same with one unnamed column (`ISNULL(E.[City], '')`), which declines with
"the select list could not be read". On registration 1 (10,028 rows) the two returned identical key
sets for: no criteria; Show Deleted; `LastFirst` starts with AAA in both views - the criterion that
matches only the deleted fixture, `EmployeeID` 13, so the declined path returned nothing in the
normal view and exactly 13 under Show Deleted; `LastFirst` starts with Adams; a City equality; and
the row cap at 11 and at 200, where both set `BrowseRowsLimited`. The decline wrote one
`FW_FallbackUsageLog` row (38). So a declining page does run its in-memory deleted and QBE filters,
as it must. What remains untried is only a declining page in the UI, which reads the same table.

The original note follows.

**No page has ever declined.** All seven pages that use the method wrap, `Roles_B` does not use it,
the SQL box is invisible so nobody can type an unwrappable query, and the QBE date cell is a
`DateTimePicker` so an unparseable date cannot be entered. The decline decision itself has 26 unit
tests over all eight real page SQLs, and `LogFallbackUsage` has worked for months — but **one branch
has never run: that a declining page does *not* skip its in-memory deleted and QBE filters.** Get
that condition backwards and a declining page shows deleted rows and ignores its criteria, quietly.

It becomes reachable the first time somebody writes a page whose SQL the wrapper does not
understand. Whoever does should check the grid, not only that the page opens. Forcing it
deliberately means temporarily giving one page unwrappable SQL in `FW_Pages` — `FW_Registration_B`
at two rows is the safe candidate — opening it, and putting the SQL back.

### One thing found and left alone

`FW_HD_Issues_B` and `FW_HD_Issues_Support_B` both carry `AND ISNULL(i.DeletedFlag, 0) = 0` in their
stored SQL, typed into their generation requests. Nothing the framework writes does that:
`BuildDefaultSqlForBrowse` emits a key alias, `t.*`, and a registration predicate, and nothing else.

A baked-in `= 0` **breaks Show Deleted permanently and silently** — the framework asks for `= 1`,
the SQL has already excluded every deleted row, and an empty grid reads as "there are no deleted
records". That is the state of both pages today, it predates this change, and the wrap neither
fixes nor worsens it. Removing it from both requests would make Show Deleted start working on both
pages, and it is deliberately not part of this change because it alters what those pages return.

---

## 13. The classes and members, and what each is for

Every scope below was read out of the source rather than remembered. The pattern throughout: the
wrapper is public because it is a general-purpose rewriter worth testing on its own, and everything
we added to `DataAccess` is private because it is one method's internal decision-making and nothing
outside should be able to call half of it.

### `BrowseSqlWrapper` — `000_FRAMEWORK/500_INFRASTRUCTURE/Data/BrowseSqlWrapper.vb`

A `Public Module`, standing alone and owning no state. It knows nothing about browse pages,
registrations or the QBE grid — it is given SQL and predicate text and hands back SQL or a reason.
That is deliberate: it is the only part of this work we can test exhaustively without a database,
and it carries 26 unit tests including all eight real page queries.

| Member | Scope | What it does |
|---|---|---|
| `BrowseSqlWrapper` | **Public Module** | The rewriter. No instances, no state. |
| `InnerAlias` | **Public Const** | `"q"` — the alias the page's own query is given inside the wrapper. Public because callers build predicates against it. |
| `WrapResult` | **Public Class** | What a wrap attempt returns: the SQL, or the reason it declined. |
| `WrapResult.Wrapped` | Public Property | True when `Sql` can be run in place of the original. |
| `WrapResult.Sql` | Public Property | The wrapped statement. |
| `WrapResult.DeclineReason` | Public Property | Why it refused, for `FW_FallbackUsageLog`. Empty when it did not. |
| `WrapResult.OutputNames` | Public Property | The names the inner query produces, in order. Callers need these to know which predicates they may even ask for. |
| `TryReadOutputNames` | **Public Function** | Reads the select list without running anything, so a caller can decide what is available before building a thing. |
| `TryWrap` | **Public Function** | The whole job: wrap, or explain why not. Takes the inner SQL, the `TOP` count, the predicates, and optionally the output column to order by when the page has no `ORDER BY`. |
| `TryRewriteOrderBy` | Private | Turns `ORDER BY E.[EmployeeID] ASC` into `ORDER BY q.[PK] ASC`, or fails. `E` does not exist outside the derived table, so this has to succeed or the wrap is refused. |
| `BuildExpressionMap` | Private | Each select item's source expression against the name it produced, so an `ORDER BY` term written the way the select list writes it can be found. |
| `ExpressionOf` | Private | The expression part of a select item, with any `AS` alias removed. |
| `OutputNameOf` | Private | The name a select item produces, or empty when it cannot be determined. Empty is what declines `Roles_B`. |
| `StripBrackets` | Private | `[Name]` to `Name`. |
| `NormalizeExpression` | Private | Brackets and spacing removed, so `E.[EmployeeID]` and `E.EmployeeID` are one key. |
| `SelectListStart` | Private | Where the select list begins, past `SELECT` and any `DISTINCT`. |
| `IndexOfKeywordAtDepthZero` | Private | The first position of a keyword not inside parentheses, brackets or a string. A hand-written scanner, not a regular expression — `ORDER BY` appears inside subqueries and inside quoted text, and cutting at the wrong one produces SQL that still runs and returns different rows. |
| `StartsWithKeywordAt` | Private | Whether a keyword starts here as a whole word, allowing any run of whitespace inside a two-word keyword. |
| `SplitAtDepthZero` | Private | Splits on a separator not inside parentheses, brackets or a string. |

### What we added to `DataAccess` — `000_FRAMEWORK/500_INFRASTRUCTURE/Data/DataAccess.vb`

All private. None of it is callable from a page, and that is the point: a page asks for browse rows
and the decision about how to get them is not a page's business.

| Member | Scope | What it does |
|---|---|---|
| `WrappedBrowseQuery` | **Private Class** | A rewritten query and its parameters, or the reason there isn't one. `Declined(reason)` is its only factory. |
| `browseResultTypes` | Private Shared ReadOnly | The column types a browse result produces, keyed by the SQL that produces it. This is what lets a typed parameter be built without asking the server. |
| `browseResultTypesLock` | Private Shared ReadOnly | Guards both caches. |
| `browseDeclinesLogged` | Private Shared ReadOnly | Which table-and-reason pairs have already been logged this run, so a declining page writes one row rather than one per refresh. |
| `RememberBrowseResultTypes` | Private Shared Sub | Records a result's column types after every fill, wrapped or not. |
| `GetBrowseResultTypes` | Private Shared Function | The types for a query, from the cache, or one `SchemaOnly` call when it has never been run. |
| `LogBrowseWrapDecline` | Private Shared Sub | One `FW_FallbackUsageLog` row per table and reason per run. |
| `BuildBrowseDeletedPredicate` | Private Shared Function | The deleted state as SQL, in the three cases of section 10. Returns empty for the third, which is a real answer meaning "no predicate". |
| `TryBuildBrowseSqlPredicates` | Private Shared Function | The QBE criteria as predicates over `q.[name]`, with a typed `SqlParameter` per value. Returns False to decline the whole wrap rather than drop a criterion. |
| `IsNumericBrowseType` | Private Shared Function | Whether a CLR type is one we compare numerically. |
| `TryBuildWrappedBrowseQuery` | Private Shared Function | The decision. Every reason to decline is checked here, before anything runs. |
| `GetBrowseRowsByRegistration` | **Public Shared Function** | Unchanged signature, unchanged contract. Every browse page still calls exactly this. |

### What we reused rather than rebuilt

The Consolidation Guardrail is why this list exists. Each of these already owned its idea, and we
called it instead of writing a second one.

| Member | Scope | Why we did not duplicate it |
|---|---|---|
| `ResolveTextComparison` | Private Shared | **Owns what a text operator means**, and returns the operator and the value together because they have to agree. The in-memory path and the Users page already call it; we are the third. |
| `TextComparison` | Private Structure | The pair it returns. |
| `GetSqlOperator` | Private Shared | Owns the numeric and boolean operators. It deliberately throws for text, to force callers to `ResolveTextComparison`. |
| `QbeDateBounds` | **Public Module** | Owns what a chosen day means against a column carrying a time. We call `Resolve` and build parameters from the bounds; the in-memory path calls `ToDataViewExpression` on the same bounds. |
| `TryParseBooleanFilter` | Private Shared | Owns which strings mean yes and no. |
| `LimitBrowseRows` | Private Shared | Turns `cap + 1` rows into the cap and sets `BrowseRowsLimited`. **Unchanged** — the two row-cap messages read it. |
| `LogFallbackUsage` | **Public Shared** | Existing, with five other fallback types already using it. |
| `TableHasColumn` | **Public Shared** | Cached metadata: does the table have `DeletedFlag`. |
| `GetPrimaryKeyFieldName` | **Public Shared** | Cached metadata: the key to join back on. |
| `IsRegistrationScopedTable` | **Public Shared** | Whether `RegistrationID` is the table's own key, which decides whether scoping applies at all. |
| `NormalizeTableName` | Private Shared | Strips qualification before a name reaches SQL. |
| `IsSafeSqlIdentifier` | Private Shared | The gate every identifier we emit passes through. |
| `GetSchemaFromSelectSql` | **Public Shared** | The `SchemaOnly` read behind `GetBrowseResultTypes`. |
| `BuildDataViewFilterExpression` | Private Shared | **The old path, untouched.** It runs whenever we decline, and it still has to behave exactly as it did. |

---

## 14. The Find chain, traced end to end — and why AWS is the point

### We traced the whole chain, not a part of it

The earlier instrumentation began *after* the query returned. It reported 66-88ms of named steps
against a Find that took 656ms, and the missing 400ms was invisible precisely because the
measurement excluded the dominant term. **A breakdown that does not add up to the whole is worse
than none, because it looks like an answer.** So we timed every link:

```
FindButton_Click          stopwatch starts here - before validation, so a refused Find counts too
  RunFind
    RefreshGrid
      PrepareBrowseSource      a page that supplies its own source
      EnsureSqlOrClose
      CaptureGridViewState     what row was selected, where the grid was scrolled
      GetActiveBaseSql         the page's SQL, possibly rewritten in code
      GetBrowseUserScopePredicate / GetBrowseUserId
      ShouldApplyViewOnlyMyScope
                                          <- "pre" step, several of these reach the database
      GetBrowseRowsByRegistration         <- "fetch"
      RemoveInvisibleRoleFieldColumns     <- "strip"
      RemoveBinaryColumns
      browseGrid.DataSource = dt          <- "bind"
      ApplyFriendlyColumnHeaders          <- "headers"
      ApplyPkColumnHiding, HideRegistrationIdColumn, HideSoftDeleteColumns, ApplyColumnVisibilityMap
                                          <- "hide"
      EnsureAtLeastOneManageableVisibleColumn, UpdateMaintenanceKeyAvailability
                                          <- "keys"
      FitVisibleColumnsToAvailableWidth   <- "fit"
      UpdateLayoutUiAvailability, RefreshColumnsManagerFromGrid, UpdateShowDeletedButtonState
                                          <- "buttons"
      EnsureDefaultLayoutExists, ApplySavedLayoutIfAvailable
                                          <- "layout", first load only
      PopulateQbeFromGridColumns          <- "qbe", only when the SQL or columns changed
      RestoreGridViewState                <- "restore"
      BuildCurrentLayoutSnapshotJson      <- "snapshot", first load only
      ReportPostQuery                     whole - accounted = "other"
  RecordFind                              writes db and perceived to FW_UsageCounter
```

`other` is the whole minus the named steps, which is what makes the line trustworthy: it came to
1-4ms, so nothing was hiding.

**The finding was unambiguous. `fetch` was 94-97% of every refresh.** Binding, headers, hiding,
fitting, buttons, layout, the QBE rebuild and the view-state restore together came to 5-15ms. Two
things we had believed were wrong: `layout` was not the problem — it was merely the largest of the
steps *being measured*, 32ms on a first load and zero afterwards — and a Find did not average 481ms,
that being a mean carried by two outliers from before the row cap existed.

Splitting `fetch` again, against `BrowseQueryMilliseconds`: about 125ms was the `Fill` — moving and
materialising the rows — and 20-35ms was what happened after it: the deleted-flag hydration, the
full `DataTable` copy that hydration makes, the QBE filter and the row trim.

**So there was exactly one thing worth attacking, and everything else was noise.** We also lowered
the log threshold from 60ms to 15ms afterwards, because the change took a warm refresh to 60ms
exactly and had begun hiding its own successes. A threshold that rises to meet an improvement stops
measuring when there is finally something to measure.

### Why AWS changes the arithmetic, and why it is the whole argument

Section 2 originally said "not a performance project — the current numbers are tolerable", and the
first version of this document dropped the work on the grounds that the database and the application
shared a machine so the rows never crossed a network. **Both of those were wrong, and AWS is why it
matters that they were.**

The database is on `BEELINK`; the application runs on `NucBox_EVO-T1`. Every one of those ten
thousand rows has been crossing a LAN the whole time. A LAN hop is forgiving — sub-millisecond, no
charge, no shared pipe. **An AWS hop is none of those things**, and every property that makes it
unforgiving is one this change attacks directly:

**Latency does not shrink with row count, so round trips are the unit that matters.** A LAN
round trip is under a millisecond; same-availability-zone is a millisecond or two; cross-region is
tens. The old path made **two** round trips per browse refresh — the page query, then
`GetDeletedIdSetForSourceTable` asking which rows of the table are deleted. We removed the second
one by folding it into the first as a `NOT EXISTS`. That is one round trip saved on **every refresh
of five of the seven pages**, and of every page written from now on, because it comes from the
generator's SQL template. This is also why we did **not** turn the grid into a pager: eight fetches
of 25 rows will lose to one fetch of 200 the moment latency is real.

**Bytes on the wire become money, not just milliseconds.** The old path moved **10,007 rows to
display 11**. The new one moves **12**. On this LAN that was ~125ms of `Fill`; on AWS it is ~125ms
*plus* egress billed per gigabyte, on every refresh, for every user, forever. Nothing about the
page changed — the same eleven rows, the same message — only what crossed the link.

**The row cap became a real lever instead of a number that changed nothing.**
`FW_Registration.MaxRecordsNoQBE` used to be applied *after* the fetch, by `LimitBrowseRows`, to a
`DataTable` already built in full. Raising it from 11 to 200 on 2026-09-21 changed the timings not
at all, which is the clearest possible proof it controlled nothing that cost anything. It is now the
`TOP`, so it controls the bytes. It is per registration, which means a customer on a thin link and a
customer on a fat one can be tuned separately without a code change.

**We kept `cap + 1` rather than `cap` for one reason worth the extra row.** The framework knows a
list is longer than the cap only because it received one more row than it asked for. Ask for exactly
eleven and that signal is gone — eleven rows, and no way to say whether there are twelve or twelve
thousand. A real total would need `COUNT(*)` over the same predicate: a second round trip doubling
the cost of every capped search, on exactly the large tables this is for. One extra row answers the
yes/no question for free.

**The per-user cold cost is bounded, and the expensive half is shared.** First execution of the
wrapped statement cost 1,528ms; with the plan cached, 575ms; warm, 16-33ms. The ~950ms difference is
SQL Server compiling a plan — and a plan lives in the server, is shared by every user and every
process, and survives the application closing. **On AWS that means one user pays it once per server,
not each user on each login.** The remaining ~500ms is per-process; its composition is not known and
we are not guessing at it again, having been wrong twice.

**The work SQL Server does is unchanged, so this is not a trade.** Logical reads came to 1,389
wrapped against 922 plus the hydration query's 461 — a wash. The deleted scan did not appear from
nowhere; it moved inside the one statement, and a round trip left with it. The `ORDER BY` costs the
same either way, because the server already sorted the whole result before `TOP` existed.

### What AWS will not fix, and should not be expected to

**`Contains` is still a scan, and that is the right trade for now.** `LIKE '%x%'` cannot seek an
index at any scale, and `FW_Employees` has no index on a name column regardless — only
`PK_Employees` and `IX_FW_Employees_UserId`. Every text operator therefore scans equally today, so
the operator costs nothing and `Contains` is what makes the search usable. If name search ever needs
to be fast the order is: index the column, and only then does the operator matter. `FirstLast` is a
persisted computed column and can be indexed like any other. **Do not remove `Contains` for
performance** — it would buy nothing.

**Text parameters are `NVarChar`, which is not sargable against a `varchar` column.** SQL Server
converts the column rather than the parameter in that direction. It costs nothing today because no
text column is indexed, and it is recorded here so that whoever indexes one knows to check it first.

**One scan per refresh remains, and a filtered index would remove it.** The deleted predicate reads
the base table to find deleted rows — 461 pages on `FW_Employees` — because `ISNULL(DeletedFlag, 0)`
is not sargable and nothing indexes `DeletedFlag`. A filtered index would remove that from every
browse refresh on every page. It is a schema change, it needs its own approval, and it is
deliberately not part of this work.

**Connection pooling is per process.** If Thinfinity runs one process per session, each session pays
its own connect and authenticate to the database. On a LAN that is invisible; across an AWS link it
is a round trip per login. This work neither helps nor harms it, and it is the next thing worth
measuring if AWS happens.
