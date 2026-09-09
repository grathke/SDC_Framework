# Page Field Picker — choosing a browse page's fields after it is generated

**Status: proposed, not built.** Designed 2026-09-09. Nothing in the application does any of this
yet. Read it before changing `Table_SQL` handling, the Hot Fields panel, saved layouts, or the page
generator's `UpsertPageRecord` call.

Two problems, one answer.

**A generated page's field list is frozen at generation.** `FW_Pages.Table_SQL` decides which columns
a `_B` page selects, and the only thing that writes it is the page generator. Adding a column to the
grid therefore means regenerating the page — rewriting its source, invalidating its hash — to change
a row in a table. The page reads that row at runtime and never reads the source for it.

**Hot Fields shows raw ids.** The panel re-reads the record with `SELECT *` and resolves a lookup
only where the *grid* happens to show that column, by reading the resolved text off the selected row
(`BuildResolvedValuesFromGrid`). A lookup column that is not in the grid displays `2` rather than
`Female` — and those are exactly the fields the panel exists to reveal.

---

## 1. One picker, on the page, owned by an App Admin

A **Fields…** button on the browse page's layout row, beside the Hot Fields button and the colour
picker, visible to an App Admin. It lists the underlying table's columns, each with:

| Column | Meaning |
|---|---|
| **Grid** | the field is selected by the page and can appear in the grid |
| **HF** | the field appears in the Hot Fields panel |
| **Displays** | for a column with a declared foreign key, which column of the target to show |

Saving writes the `FW_Pages` row. No file is written, no source is touched, no rebuild is needed,
and the page picks the change up the next time it opens.

**Why the page rather than the generator.** Three reasons, and the third is decisive:

1. It is where page-level settings already live. `PageBackgroundColorPicker` is precisely this shape —
   a widget that attaches to a page and writes one `FW_Pages` column.
2. You are looking at the grid you are changing.
3. **`Roles_B`, `FW_AuditTrail_B`, `FW_HD_Admin_B` and the Help Desk pages have no generation request
   at all.** Anything that lives in the generator can never reach them.

The generator **creates** a page. The picker **maintains** it. That split is what stops a later
regeneration silently reverting the picker's work — see section 6.

## 2. What it stores

| Where | What |
|---|---|
| `FW_Pages.Table_SQL` | rewritten from the ticked Grid fields, by the shared builder in section 5 |
| `FW_Pages.HotFields` | **new column** — the ticked HF fields, as a delimited list |
| `FW_Pages.LookupSpecs` | **new column** — the display answers, in the format already used by `FW_GeneratedPages.LookupFields` |

The lookup format is not invented here. It already exists:

```
GenderID -> FW_Gender.GenderID displayed as GenderName filtered by registration
```

**Cache rule.** If these ride in the `FW_Pages` cache — which is where the pending round-trip work is
heading — then saving **must** update the cached entry, or a tick appears to do nothing until the
application restarts. `SavePageBackgroundColor` already carries this obligation; this is the same
trap, not a new one.

## 3. Who owns what — three layers, and only one is missing

| Layer | Who sets it | Exists today |
|---|---|---|
| Which fields the page selects at all | page, App Admin, via this picker | **no — the gap** |
| Which are visible, their width and order, for everyone by default | `* Default` saved layout, Company Admin | yes |
| Which are visible, their width and order, for me | my saved layout | yes |
| **What I was last looking at** | **`Last Used`, written automatically, per user** | **yes** |
| Which are hidden from a role entirely | `FW_RoleFields` | yes |

**`Last Used` is the layer that changes everything, and it is easy to miss.** It is not something a
user chooses — it is written for them as they use the page, and `GetPreferredTableLayout`
(`DataAccess.vb:8610`) prefers it over the `* Default`. Checked against `WX_Framework` on 2026-09-09:
every page anyone has opened has **both** a `* Default` and a `Last Used`, ten rows across five pages.

So the population that sees a changed default order is not "users who have not customised the grid".
It is **users who have never opened the page**.

**The picker does not own column order.** It sets the order columns arrive in, because that falls out
of the SQL, but the place to say "this is the order everyone should see" is the Default layout, which
is already built, already gated and already wins. Adding a second owner of order is how a column
arrangement starts reverting for reasons nobody can explain.

## 4. Precedence, and why nothing here fights

Saved layouts are applied **last**, so they win. Verified in `TryApplyLayoutSnapshotJson`
(`Base_B.vb:2837`), which walks the *saved entries* rather than the grid's columns:

- **A field the picker adds** is not mentioned in an existing layout, so it is never visited and keeps
  its default state: it **shows**. An old layout cannot hide a new field.
- **A field the picker removes** is looked up, not found, and skipped. No error.
- **A layout matching nothing at all** is rejected before it is applied, by
  `LayoutJsonHasAnyMatchingGridColumn` (`Base_B.vb:1921`), so a layout from an older shape of the page
  cannot blank the grid.

Two consequences worth stating before anyone tests this:

- **A newly added field lands wherever its default index puts it** for a user with a saved layout,
  because the saved entries carry explicit `DisplayIndex` values and the new column does not. It
  appears; it may not appear where the picker's order intended.
- **Test the order on an account that has never opened that page.** Not "never dragged the grid" —
  `Last Used` is written automatically, so an account that has merely *visited* the page already has a
  layout that wins. On any other account the order setting will look broken when it is working
  correctly.

A **"use the page default"** action is worth adding alongside, so a user who has drifted can adopt a
new default without deleting their layout by hand. Without it, a field you add is least visible to the
people who use that grid most.

## 5. One SQL builder, one lookup resolver

**Both already exist, and both are in the wrong place.**

`BuildLookupSpecFromRelationship`, `BuildLookupSpecFromTarget` and the registration-scope test are
`Private Shared` inside `PageGeneration_U`. The picker needs the same answers, so they move to a
shared owner and the generator calls it. Two implementations of "what does this lookup display" will
disagree eventually, and the disagreement will present as one page showing a name and another showing
a number for the same column.

The browse SQL builder is the same story, and matters more, because `FW_Base_B` depends on invariants
that generated SQL guarantees today:

- the key aliased **`AS PK`**
- the `RegistrationID` predicate where the table is registration scoped
- soft-delete columns present, so the deleted view works
- lookups joined, rather than left as ids

A picker that writes SQL must produce all of those every time. Hand-rolling a second builder inside
the picker is the way this feature breaks browse pages.

## 6. Regeneration is a reset, and says so

Regeneration overwrites `Table_SQL` today — `UpsertPageRecord` does `IF EXISTS … UPDATE … Table_SQL =
@TableSQL` (`DataAccess.vb:1293`). It does **not** touch saved layouts. That combination is worse than
either extreme: the baseline changes, and the layouts that override it survive, so for anyone holding
a layout — including everyone covered by the `* Default` — the regenerated page looks unchanged.

**Decided 2026-09-09: regenerating a page returns it to the state it would have been in if generated
for the first time.** Regeneration is not a routine act; it means the page is being replaced.

**Reset — these describe the page's shape, which is what regeneration just changed:**

- `FW_TableLayouts` for the page: every user's named layout, the `* Default`, **and every `Last Used`
  row**
- `FW_SavedQbe` for the page: saved searches naming columns that may no longer be selected
- `Table_SQL`, `HotFields`, `LookupSpecs`, `UseHotFields`

**`Last Used` is the row that decides whether a reset means anything.** It is per user, written
automatically, and preferred over the `* Default` — so leaving it behind means a regenerated page
looks unchanged to everyone who has ever opened it, which is precisely the population that would
notice. A reset that skips it is not a reset.

**Keep — these are not page shape:**

- **`FW_RoleFields` and `FW_RoleDetails`.** Permissions are security configuration. Regenerating a
  page to add a column must never grant or revoke access to a field, and must never quietly return a
  permission an administrator removed. This is not negotiable.
- The dashboard icon's saved position and chosen picture. That is about the dashboard.
- `FW_AuditTrail`. It records what happened, which stays true.

**The caption is the awkward one.** `UpsertPageRecord` also overwrites `Table_Alias`, pre-filled with
the table name — which is how a page ends up captioned `USERS` after somebody deliberately named it
something better. **Preserve an alias that differs from the generated default** rather than stamping
over it. Otherwise regeneration keeps undoing caption work, and the caption is the one setting three
different surfaces read.

**And it must be loud.** Deleting layouts destroys work that may belong to several people, so
regeneration states its blast radius and requires a yes:

```
Regenerating FW_HD_Issues_B will remove:
  1 default layout, shared by the registration
  1 personal layout
  0 saved searches
Permissions, field access and audit history are not affected.
```

Those counts are the real shape of this database on 2026-09-09, not an illustration: two layout rows
per opened page — a `* Default` and one `Last Used` — and `FW_SavedQbe` empty, nobody having saved a
search yet. Worth knowing when this is built, because the dialog will usually be reporting small
numbers, and a large one means something unexpected is being thrown away.

Nobody expects "regenerate" to mean "delete other people's saved layouts". Saying so is what makes
the reset safe to have.

## 7. What Hot Fields becomes

**Ticked fields where a list exists; every field where it does not.**

- A page with HF ticks shows exactly those, in the order given, with lookups resolved from the specs.
- A page with no ticks behaves exactly as it does today: `SELECT *`, every column, exclusions applied
  (`PK`, `RegistrationID`, `RowVersion`, `DeletedFlag`, `DeletedBy`, `DeletedOn`, the primary key).

That fallback is what makes this additive. `Roles_B`, `FW_AuditTrail_B` and the Help Desk pages have
no picker input on day one and must not change behaviour.

**This changes what the feature is.** `BASE_BHF_SPEC.md` promises "every field of the selected
record". With a curated list that is no longer true, and the trade is deliberate: control and resolved
lookups, at the cost of a new column staying invisible until somebody ticks it. `BASE_BHF_SPEC.md`
needs amending in the same change, not afterwards.

## 8. Round trips

**Still one query per selection**, and only while the panel is open.

Today: `SELECT TOP 1 * FROM table WHERE pk = @key`. With a field list and specs: a single `SELECT`
of the ticked columns with `LEFT JOIN`s for the resolved lookups. Same one round trip, better answer —
and resolving a lookup by joining is what avoids the obvious wrong turn of one lookup query per field.

The picker's own reads are per open, not per selection, and `HotFields` and `LookupSpecs` ride in the
`FW_Pages` row that the pending cache fold is already loading.

## 9. Open questions

1. **Does the picker need to reach pages with no `FW_Pages` row yet?** A page creates its row on first
   open (`Base_B.vb:917`), so in practice the row exists by the time anyone can press the button.
   Worth confirming rather than assuming.
2. **Should a non-generated page be allowed a curated HF list?** Section 7 says yes and it comes free,
   but those pages' SQL is hand-written and the picker must not rewrite it. Likely rule: the picker
   edits HF and lookups for any page, and `Table_SQL` only for pages that own a generated `Table_SQL`.
3. **What happens to a `Displays` choice when the foreign key is dropped?** The spec goes stale and the
   column falls back to its id. Probably right; should it say so on screen?
4. **Who may use the picker — App Admin only, or Company Admin too?** Shared layouts are Company Admin
   (see section 3), which argues for consistency.

## 10. Before this is called done

It crosses page generation, shared browse behaviour, saved layouts, QBE and the database, so the
Application-Wide Change Gate applies. The behaviour matrix must cover:

- a page with picker input, and one without
- a field added, and a field removed, for: a user with no layout, a user with their own layout, and a
  registration with a `* Default`
- a lookup with a spec, a lookup without one, and a spec whose foreign key has since been dropped
- regeneration with and without saved layouts present, and the confirmation refused
- a role that cannot see a field the picker selected — `FW_RoleFields` still wins
- QBE after a field is added and after one is removed, since QBE derives from visible grid columns

Manual verification on the real pages, not compilation. Both regression scripts, and the QBE
guardrail's check that hiding a browse column also removes it from QBE.
