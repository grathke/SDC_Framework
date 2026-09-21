# QBE in SQL, not in memory

**Proposed, nothing built.** Written 2026-09-21 from measurements taken the same morning.

Read this before changing `DataAccess.GetBrowseRowsByRegistration`, the QBE filter path, or the
row cap. Section 4 is the part that makes this harder than it looks, and section 6 is why it must
not be done in one pass.

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
