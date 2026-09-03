# English Search — Specification

**Status: proposed, not built.** Written and revised 2026-09-03. Nothing in the application
implements this yet.

A text box and a button on a browse page. Type a filter in English, press the button, and the grid
shows the matching rows. It runs entirely on the machine — a parser over a closed vocabulary, no
model, no network, nothing sent anywhere.

This document is meant to be picked up cold. It records the decisions **and the reasons**, because
several of them were argued the other way first and the reasons are what stop that happening again.

---

## 1. The controls

A pair, added to `01 FW_Base_B.vb` **once**, so every browse page inherits them and there is no
second copy to drift:

- a single-line text box for the English statement
- a button beside it

Enter in the text box does the same thing as the button. An empty box plus the button clears the
English filter and returns the unfiltered view.

### What the button does

| Outcome | Behaviour |
|---|---|
| Doesn't parse | Message naming what was not understood. Nothing runs, grid unchanged. |
| Parses | Predicate appended to the page's own SQL, query runs, grid rebinds. |
| Parses, query errors | The error is shown, grid unchanged. Rare, since the predicate's shape is controlled — but it must not fail silently. |

Nothing is validated by *trying* the SQL. Validation happens at parse time against the field list
and the column types, so a bad request is refused before the database is touched.

---

## 2. Route: separate from the QBE

The English box **does not touch the QBE grid**. It does not fill it in, does not read it, and does
not change how any existing search behaves. The parser produces a SQL `WHERE` fragment, which is
appended to the page's own query and executed.

This was chosen over the alternative — parsing into the QBE grid the way a stored QBE is loaded —
and it is the better route for reasons that only became clear once the filter paths were traced:

- **Nothing existing can regress.** The QBE grid, its operators, its saved searches and its
  client-side filtering are untouched. No shared code changes behaviour.
- **Full SQL semantics.** Because SQL Server evaluates the predicate, `%` works anywhere in a
  pattern — including mid-string — as do `_`, `IN` and `NOT LIKE`. The QBE route could not offer
  this; see §8.
- **`OR` becomes possible.** The QBE's filter model is a `field|operator → value` dictionary joined
  with `AND`, so `OR` was not representable at all, and same-field `OR` needed an invented `InList`
  operator to squeeze into one grid cell. A `WHERE` fragment removes the restriction outright. Both
  `FirstName = Glenn or FirstName = Richard` and `FirstName = Glenn or City = Stuart` are ordinary
  SQL.
- **Smaller.** No new operator on `QbeComparisonOperator`, no change to `Models.vb`, no change to
  the operator dropdown, no change to the saved-QBE format.

---

## 3. Where it runs — the rule that must not be broken

The predicate is executed **through `GetBrowseRowsByRegistration`**, appended to the page's existing
SQL. It must not become a second query path with its own `SELECT`.

That function is what applies `RegistrationID` scoping, the soft-delete view, the view-only-mine
scope predicate and the row limit. A standalone query that forgets any one of them shows another
registration's rows, or deleted rows, and does so silently. UI visibility is not authorization, and
neither is a `WHERE` clause the parser happened to write.

`AddBrowseScopePredicate` at `DataAccess.vb:778` already solves the mechanical half — inserting a
predicate before any `ORDER BY`, choosing `WHERE` or `AND` correctly. Its guard is a deliberately
narrow regex accepting only the view-only-mine shape, and it is **not** to be loosened. The English
predicate gets a sibling function with its own guard, reusing the insertion logic.

### Parameters, not quotes

**There is no quoting anywhere in this design.** The parser emits

```
FirstLast LIKE @eng0
```

and binds `Glenn%` to `@eng0`. SQL Server never sees a quote character, so there is nothing to
escape and nothing to get wrong — a value containing `'`, `--` or a whole `DROP TABLE` is just text
in a parameter. Field names are resolved against the known column list and never interpolated from
typed text. Those two rules together are what make an arbitrary typed string safe to run.

---

## 4. Vocabulary and field verification

Fields come from the same source the QBE uses — `qbeFieldDefinitions`, a `List(Of
QbeFieldDefinition)` holding `FieldName`, `DisplayName` and `FieldKind` (`Models.vb:439`). It is
already limited to **visible grid columns** by the QBE Visibility Guardrail, so the English box can
never reach a hidden column, or the internal `PK` alias, any more than the QBE can.

Both spellings are accepted, case- and space-insensitive: `FirstLast`, `First Last`, `first last`.

An unknown field is reported, never guessed:

> **Surname** isn't a field on this page. Available: First Name, Last Name, First Last, City,
> State, Zip.

Note the wording. It is *not* "use only columns in the table" — the vocabulary is the columns
**visible on this grid**, which can be fewer than the table's. Telling someone to use table columns
when they just used a hidden one would be wrong advice.

A closest-match suggestion — *"did you mean Last Name?"* — is cheap and optional.

### What `FieldKind` is for

Knowing the column's type is what lets the parser be strict without being asked:

- **Type validation before anything runs.** `Age = abc` is rejected with "Age must be a whole
  number" rather than sent to the database to fail.
- **Binding the parameter as the right type** — Integer, DateTime, Boolean — not as a string, so
  comparisons are correct and indexes remain usable.
- **Rejecting operators that do not fit.** `contains` on a numeric column, or `%` on a date.
- **Normalizing values** — `yes` / `no` / `1` / `0` / `true` / `false` to a Boolean, reusing
  `TryParseBooleanFilter` in `DataAccess.vb` rather than writing a second one.

---

## 5. Lexicon

Symbols and words map to the same SQL, so nobody has to remember which dialect the box speaks.

| Input | SQL |
|---|---|
| `=`, `is`, `equals` | `=` |
| `<>`, `!=`, `is not`, `not` | `<>` |
| `>`, `more than`, `after` | `>` |
| `>=`, `at least`, `on or after` | `>=` |
| `<`, `less than`, `before` | `<` |
| `<=`, `at most`, `on or before` | `<=` |
| `contains`, `has` | `LIKE '%value%'` |
| `starts with` | `LIKE 'value%'` |
| `ends with` | `LIKE '%value'` |
| `between X and Y` | `>= X AND <= Y` |
| `or` | `OR`, grouped in parentheses |
| `and`, `,` | `AND` |

### Wildcards

**A value containing `%` is a `LIKE` pattern, used exactly as typed.** This is the behaviour the
whole design was asked for: it should do what the SQL you would have written by hand does.

| Typed | Emitted |
|---|---|
| `FirstLast = Glenn Rathke` | `FirstLast = @eng0` — no wildcard, exact |
| `FirstLast = Glenn%` | `FirstLast LIKE @eng0` |
| `FirstLast = %Rathke` | `FirstLast LIKE @eng0` |
| `FirstLast = %Rathk%` | `FirstLast LIKE @eng0` |
| `FirstLast = Gl%nn` | `FirstLast LIKE @eng0` — mid-string, anchored both ends |
| `FirstLast <> Glenn%` | `FirstLast NOT LIKE @eng0` |

The pattern is never wrapped. When the user supplies the wildcards, the operator does not add its
own — otherwise `%Rathk%` becomes `%%Rathk%%`, which still matches but is no longer the pattern that
was typed.

`_` is not a switch. Without a `%`, `= FW_Entity` stays an exact match and keeps working. With one,
`FW_%` is a `LIKE` and `_` behaves as a wildcard inside it, exactly as SQL treats it. One switch,
full SQL semantics past it.

`contains` / `starts with` / `ends with` remain the shorthand for when you would rather not type
wildcards at all.

---

## 6. Parsing rules

- Field names matched **longest first**, so `First Name` wins over `Name`.
- Leading noise skipped: `show me`, `find`, `list`, `all`, `where`, `with`.
- A value runs until the next recognised field name, `and`, `or`, or a comma.
- Quotes take a value whole — the escape hatch for a value containing `and`, `or`, a comma, or a
  literal `%`.
- `A or B` on the same field with the same operator collapses to `IN (@eng0, @eng1)`.

### Failure behaviour

Nothing is ever guessed, and a filter that cannot be built is never silently dropped.

- Anything unparsed is named back, with the fields this page knows.
- A value that does not suit its column's type is reported before anything runs.
- If the predicate cannot be applied, the user is told — never a quiet fall back to unfiltered rows.

---

## 7. Permissions and visibility

**v1 is gated on the existing `canUseQbe`** — someone allowed to query is allowed to query. No
schema change, nothing new to administer.

If independent control is wanted later — showing a role the English box but not the QBE, or the
reverse — that shape already exists in the permissions model rather than needing to be invented.
`AccessCapability` (`AccessSecurity.vb:9`) is a flags enum, and QBE access is already two of its
members:

```
UseQbe    = 16
ExpandQbe = 512
```

Both are granted from role rows — `Convert.ToBoolean(reader("Expand_QBE"))` at `DataAccess.vb:199`.
`UseEnglishSearch = 1024` would sit alongside them, making "QBE, English, both or neither" a
per-role setting changed in the role matrix without a rebuild.

**Its cost**, following the path `Expand_QBE` took: a migration adding a column to `FW_RoleTables`,
a property on `RoleTableAccessEntry`, the mapping in `AccessSecurity.vb`, two readers in
`DataAccess.vb` (lines 199 and 5240), and a tick box in the role matrix UI. Well-trodden, but it is
a permissions change with a schema migration, so it is deliberately **not** in v1 — it should not
block the parser, and the capability should not be added before it is known to be wanted.

---

## 8. Why not the grid — what the path trace found

Recorded because it is the evidence the route in §2 rests on.

There are three filter paths, not one:

| Path | Where | Used by |
|---|---|---|
| Client-side `DataView` | `DataAccess.vb:649` and `:4199`, via `BuildSingleColumnFilterExpr` at `:7672` | **most browse pages** — any page with SQL in `FW_RoleTables` |
| Inline SQL, `FW_Entity` | `DataAccess.vb:718` and `:750` | fallback when no page SQL is supplied |
| Inline SQL, `FW_Users` | `DataAccess.vb:4261` and `:4287` | the Users browse |

The first is the main one, and it filters in memory with `DataView.RowFilter`, whose expression
syntax permits a wildcard only at the **start or end** of a pattern. Mid-string `LIKE '%Gl%nn%'`
does not merely fail to match — it throws.

It **was** caught and discarded:

```vb
Catch
    ' Return unfiltered if expression is invalid
End Try
```

So an invalid filter expression returned **every row, unfiltered**, with nothing said — which reads
as "everything matched", the opposite of what happened.

**Fixed 2026-09-03**, in the same pass that made the QBE honour a typed `%`. An unusable filter now
throws with the filter text in the message, and a mid-string wildcard is refused up front naming the
field, rather than being discovered by a throw. Both are covered by B-21 and B-22 in
`TEST_CASES.md`. The path table above still stands, and is still what this route rests on.

Also observed while tracing, unverified as to intent: the filtered `DataView` branch returns
`view.ToTable()` directly, bypassing `LimitBrowseRows`, while the unfiltered branch applies it.

---

## 9. Files

| File | Change | Notes |
|---|---|---|
| new parser file | English → predicate + parameters | pure function; no database, no message loop |
| `tests\SDC.Framework.Tests` | parser tests | every phrasing becomes a test case |
| `DataAccess.vb` | sibling of `AddBrowseScopePredicate`; optional predicate + parameters on `GetBrowseRowsByRegistration` | additive — existing callers unaffected |
| `01 FW_Base_B.vb` | the text box, the button, the wire-up | **`Create Base_B Restore Point` before the first substantive edit** — QBE Visibility Guardrail |

Build order: **parser and its tests first, Base_B last.** The parser is a pure function, so nearly
all the risk is retired before the protected file is opened at all.

Then `scripts\validate-browse-regression.ps1` and the manual checklist it prints, plus a case set in
`TEST_CASES.md`.

---

## 10. Still open

- **English versus QBE when both have values.** Do they combine with `AND`, or does running one
  clear the other? Recommendation: **mutually exclusive**, with the active-filter label saying which
  is in force. Combining them produces a compound filter that is only half visible on screen.
- **An echo of what was parsed.** Populating the grid would have shown the user what was understood;
  this route does not, so a misparse is invisible. Recommendation: a read-only line — *"Filtering:
  First Last starts with Glenn, City = Stuart"* — beside the existing active-filter label that
  `UpdateActiveFilterLabel` maintains (`01 FW_Base_B.vb:4103`).
- **Dates.** Whether relative phrases — `last 30 days`, `this year` — earn their place, or v1 takes
  explicit dates only. The one part of the lexicon where scope can quietly grow.
~~The silent-unfiltered defect in §8~~ — fixed 2026-09-03, along with the QBE honouring a typed
`%`. One consequence for this specification: §5's wildcard rules are now **already true of the
QBE**, so the parser inherits them rather than introducing them.
