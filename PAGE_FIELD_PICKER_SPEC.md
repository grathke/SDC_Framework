# Page Fields — what a browse page selects, and what Hot Fields shows

**Status: proposed, not built.** Designed 2026-09-09, and rewritten the same day after the first
version was argued down to a quarter of its size. Read it before changing `Table_SQL` handling, the
Hot Fields panel, saved layouts, or the page generator's `UpsertPageRecord` call.

Two problems.

**Adding a column to a browse grid means regenerating the page.** `FW_Pages.Table_SQL` decides what a
`_B` page selects, and only the generator writes it.

**Hot Fields shows raw ids.** The panel re-reads the record with `SELECT *` and resolves a lookup only
where the *grid* happens to show that column, by reading the resolved text off the selected row
(`BuildResolvedValuesFromGrid`). A lookup column that is not in the grid displays `2` rather than
`Female` — and those are exactly the fields the panel exists to reveal.

---

## 1. What decided the shape of this

**A `_B` page does not name its columns.** It reads `Table_SQL` at runtime and builds the grid from
what comes back, which is why every generated `_B` looks the same and why
`PAGE_GENERATION_SIMPLIFICATION.md` argues one need not be a compiled class at all.

So **changing a column needs only the `FW_Pages` row to change** — no source rewrite, no rebuild, no
deployment. That single fact removes most of what a first draft of this document proposed.

**The generation request is the page's definition.** `BrowseFields` and `MaintenanceFields` live on
`FW_GeneratedPages`; opening a request re-ticks the grids and `OrderSelectionGrid` restores the order
they were left in. Regeneration does not discard the ticks — it *reads* them. That is why the
generator can rebuild a page's SQL from scratch safely: it wrote it in the first place, and never has
to parse it back.

An earlier draft had a picker on the page rewriting SQL it had not written, which meant parsing a
statement that might carry a `CASE`, a `WHERE` or a sort nobody wants destroyed. None of that is
needed and none of it is here.

## 2. Who owns what

| | Owner | Changed by |
|---|---|---|
| Which columns the grid selects, and their order | the generation request | ticking fields, then **Save** |
| Whether the page has Hot Fields at all | the generation request | the existing `Display Hotfields` checkbox |
| Which fields Hot Fields shows, **at birth** | the generation request | HF ticks, applied when the page is **generated** |
| Which fields Hot Fields shows, **thereafter** | **the `_B` page** | an App Admin ticking on the panel |
| Which columns are visible, their width and order | saved layouts | the columns manager, as today |
| Which fields a role may see at all | `FW_RoleFields` | Roles, as today |

**One line for the whole model:** *the request defines the page at birth; the page owns its Hot Fields
thereafter; a confirmed regeneration takes it back to birth.*

## 3. Save writes data. Generate writes files and resets.

**Save** — writes `FW_Pages.Table_SQL` from the ticked browse fields, in their ticked order. No file is
written, no hash changes, nothing downstream is cleared. The running application picks the columns up
the next time the page opens. This is the ordinary way to add or remove a grid column.

**Generate** — everything Save does, plus the source files, plus the reset in section 5.

**One consequence to design for rather than discover:** the request's HF ticks are applied on
**Generate only**. Ticking HF in the request and pressing Save appears to do nothing, because the page
owns that list once it exists. The request's HF column must say so on screen — *applied when the page
is generated* — or it reads as a bug.

**Ticking a field turns the panel on.** Built 2026-09-09 after generating a page with no ticks and no
`Display Hotfields`, and finding no button and nothing to say why. Two switches that can disagree is
how somebody picks their fields, forgets the checkbox, and concludes the feature is broken. The
checkbox keeps its own meaning — show the panel with every field — and a tick simply implies it.

## 4. Hot Fields

**Selection only.** An HF tick decides whether a field appears in the panel. It does not affect the
grid, its columns or their order, and it never touches SQL — the panel does its own read of the
record.

**The panel stays alphabetical by caption** (`HotFieldsPanel.ShowRecord`), decided 2026-09-09. The
existing reasoning holds: a table's column order is a history of when columns were added, which is no
help to somebody looking for one field.

**Ticking on the page is App Admin only**, and it is a page setting rather than a personal one — one
list per page, in `FW_Pages`, the same shape as the background colour.

**Where a lookup spec exists, the panel shows the value rather than the id.** The specs already exist
in the format `GenderID -> FW_Gender.GenderID displayed as GenderName filtered by registration`, and
resolving them by joining keeps the panel at **one round trip per selection**, which is what it costs
today.

**No list means every field**, exactly as now. `Roles_B`, `FW_AuditTrail_B`, `FW_HD_Admin_B` and the
Help Desk pages have no generation request and no ticks, and must not change behaviour.

## 5. Regeneration is a reset, and says so loudly

**Decided 2026-09-09: generating a page returns it to the state it would have been in if generated for
the first time.** Not Save — **Generate**, the deliberate act, behind a dialog that states what it
destroys.

**Cleared:**

- every `FW_TableLayouts` row for the page: each user's `Last Used`, the `* Default`, and named layouts
- `FW_SavedQbe` for the page
- the Hot Fields list, back to the request's ticks

**Kept, and not negotiable:**

- **`FW_RoleFields` and `FW_RoleDetails`.** Permissions are security configuration. Generating a page
  to add a column must never grant or revoke access to a field, and must never quietly return a
  permission an administrator removed.
- **`FW_AuditTrail`.** It records what happened, which stays true.
- **A caption that differs from the generated default.** `UpsertPageRecord` overwrites `Table_Alias`
  today, pre-filled with the table name, which is how a page ends up captioned `USERS` after somebody
  deliberately named it something better.

**Why layouts go and the request's ticks do not.** A layout names a display index and a width for a
column *the page selects*; generation changes what the page selects, so the layout's referent has
moved. The request's ticks are the input to generation, not a casualty of it.

**`Last Used` is the row that decides whether the reset means anything.** It is per user, written
automatically as somebody uses a page, and `GetPreferredTableLayout` (`DataAccess.vb:8610`) prefers it
over the `* Default`. Checked against `WX_Framework` on 2026-09-09: every page anyone has opened has
both, ten rows across five pages. Leave `Last Used` behind and a regenerated page looks unchanged to
everyone who has ever opened it — which is precisely the people who would notice. A reset that skips
it is not a reset.

**The dialog states the count, not a warning in the abstract:**

```
Regenerating FW_HD_Issues_B starts the page over, as if it had never existed.

This will permanently remove:
  1 default layout, shared by everyone in the registration
  1 personal layout
  0 saved searches
  the Hot Fields selection, back to what this request specifies

Permissions, field access and audit history are not affected.
This cannot be undone.
```

Those counts are the real shape of this database on 2026-09-09, not an illustration: two layout rows
per opened page, and `FW_SavedQbe` empty. A large number means something unexpected is being thrown
away, which is exactly when somebody should stop and read.

## 6. Round trips

**No extra reads on page load.** `HotFields` is one more column on the `FW_Pages` row that
`EnsurePageAliasCache` already reads in a single query per session — the same row that gained
`DB_Table`, `Table_SQL` and `Background` in commit `b1dd9bc`.

**Hot Fields stays at one query per selection**, and only while the panel is open. Today
`SELECT TOP 1 * FROM table WHERE pk = @key`; with a list and specs, the ticked columns with their
lookups resolved. Resolving by joining rather than one query per lookup is what keeps it at one.

**The cache rule this inherits:** anything writing `HotFields` must update the cached entry, or a tick
appears to do nothing until the application restarts. `SavePageBackgroundColor` already carries that
obligation.

## 7. Open questions

1. **`FW_Pages` rows are global.** `UpsertPageRecord` writes `RegistrationID = NULL` and the rows are
   shared, so one App Admin's HF selection changes what **every registration** sees. Acceptable, or
   does the list need to be per registration? **This is the one question that could still change the
   storage**, and it is unanswered.
2. **A tick for a field a role cannot see.** `FW_RoleFields` must win, and the panel must not become a
   way to read a field a role was denied. Stated here because it belongs in the design, not only in
   the test matrix.
3. **A `Displays` spec whose foreign key is later dropped** falls back to showing the id. Probably
   right; should the panel say so, or fail quietly?

## 8. Before this is called done

It crosses page generation, shared browse behaviour, saved layouts and the database, so the
Application-Wide Change Gate applies. The behaviour matrix must cover:

- a page with HF ticks, and one without — the second must behave exactly as today
- Save with a column added, and with one removed, on a page that has been opened before
- Generate with layouts present, and with the dialog refused — nothing cleared either way if refused
- an on-page HF tick, then a Save in the request: the tick must survive
- an on-page HF tick, then a Generate: the tick must be gone
- a lookup with a spec, one without, and a spec whose foreign key has been dropped
- a role denied a ticked field: the role wins
- QBE after a column is added and after one is removed, since QBE derives from visible grid columns

Manual verification on the real pages, not compilation. Both regression scripts, and the QBE
guardrail's check that hiding a browse column also removes it from QBE.
