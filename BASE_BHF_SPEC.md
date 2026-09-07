# Hot Fields — every field of the selected record, beside the grid

**Partly built, 2026-09-07.** The widget, the `FW_Pages.UseHotFields` flag and the `FW_Base_B`
wiring are in. The generation request's "Display Hotfields" checkbox is not.

Read this before changing `Base_B`'s layout row, and before adding anything to its constructor:
section 9 records why the widget is built ahead of the `buildDefaultBrowseShell` early return.

A browse grid shows the few columns its page SQL selects. Hot Fields shows **every** field of the
selected record in a tall, thin, read-only grid beside it — so you can see the whole record without
opening the maintenance page, while still looking at the row it came from.

---

## 1. It is a widget on every browse page, not a new base class

The first draft of this proposed a `FW_Base_BHF` class that pages inherited, with a `_BHF` page
suffix and a generator option. **That is not the design.** It carried a new page suffix, a new
column on `FW_GeneratedPages`, generator changes, a naming-convention amendment, and the standing
risk that some future script globbing `*_B.vb` would silently skip every Hot Fields page.

Instead: **a widget owned by `FW_Base_B`, the same shape as `PageBackgroundColorPicker`.** One
class, one button on the layout row, one panel. Every browse page has it. Nothing is generated,
nothing is named differently, and no page has to opt in by being a different kind of page.

The colour picker is the working precedent for all of it — a self-contained widget that attaches
itself to a host form, owns its own button and panel, and is positioned by the page's layout.

## 1.1 Not every page wants it — `FW_Pages.UseHotFields`

The widget lives in `FW_Base_B`, so **every** page inheriting it can have the button, generated or
hand-written. Whether it *shows* one is a per-page setting.

**A new `bit` column on `FW_Pages`, `UseHotFields`, defaulting to 0.** The button appears only where
it is set. That is one flag on the row the page already has, rather than a new page kind, a new
suffix or a new base class.

**It costs no extra read.** The page's `FW_Pages` row is already fetched at startup for its table
name, SQL and caption — `UseHotFields` rides along in the same row. Note that
`EnsurePageAliasCache` currently loads every `FW_Pages` row in one query but keeps only
`Table_Alias`; the column belongs in the same fold as `DB_Table`, `Table_SQL` and `Background`,
which is already queued work.

**Setting it:**

- **Generated pages**: a checkbox on the generation request, alongside `Generate` and
  `Use QBE only`, written to `FW_Pages` when the page is registered.
- **Existing pages**: settable directly, because the flag lives on the page's row rather than in
  its source. A page that was never generated is no different from one that was.

This replaces the earlier open question about opting out. It is opt **in**: a page shows the button
because someone asked for it, not because nobody objected.

## 2. The button

**On the layout row above the grid, not on the action row.** It belongs to the grid area — the
strip it opens sits beside the grid, so the control that opens it sits above the grid.

Hot Fields is a view concern — it changes how you are looking at the data, like the layout
drop-down, the layout buttons and the columns manager. The action row is for things that act on a
record: New, Read, Modify, Delete, Close. Putting it with the layout controls says what it is.

**It is the rightmost control on that row** — last when the row is read left to right, after Save
My Layout.

That row places its buttons right to left through a cursor, so the rightmost button is the one
placed **first**:

```vb
' The Hot Fields button takes the right-hand end, and everything already on the row
' shifts left by its width. Two lines, and the existing arithmetic follows.
hotFieldsButton.Left = layoutRightCursor - hotFieldsButton.Width
layoutRightCursor = hotFieldsButton.Left - 8

saveMyLayoutButton.Left = layoutRightCursor - saveMyLayoutButton.Width
layoutRightCursor = saveMyLayoutButton.Left - 8
```

Nothing needs repositioning by hand and nothing needs shuffling across: the cursor moves everything
else along on its own. The left of the row holds only `recordCountLabel` at x=8, so there is room
for the shift.

**One line of caption.** `Hot Fields` fits at the width those buttons already use. A two-line
caption is possible but would be the only tall button on that row and would break its baseline; if
it is ever tight, `Fields` or `HF` is a better answer than a second line.

**Click to open. Never hover, and never to close.** Hover and pointer polling degrade under Thinfinity VirtualUI —
mouse-move events are coalesced or arrive stale, so a hover-opened panel opens late or not at all.
`CLAUDE.md` records this, and the tile drop-downs learned it the hard way.

## 3. Opening it makes the window wider

**Opening widens the form by the strip's width. Nothing inside moves** — the grid keeps its size
and position, the action row keeps its spacing, and the new space is exactly where the strip goes.
Closing narrows it back and the strip disappears.

The grid is why you are on the page. Hot Fields is an addition, so the addition brings its own room.

### 3.1 When there is not enough room

**Grow by as much of the strip's width as the working area allows. Any shortfall comes out of the
grid.**

Measured with `Screen.FromControl(Me).WorkingArea`, which under VirtualUI reflects the session's
virtual screen — the browser viewport as it was when the session started.

**Growing past the viewport is not an option.** Confirmed 2026-09-07: the viewport is fixed once
set, and the browser does not add scroll bars for a window wider than it. Anything past the edge is
simply unreachable, so a form that grew past it would put controls where nobody can click them.

On a wide screen the shortfall is zero and the grid is never touched. On a laptop it is partial
growth plus a slightly narrower grid. Same feature, no cliff, nothing off-screen.

### 3.2 Position

If the form sits near the right edge of the working area, it shifts left as it grows, and shifts
back on close — unless the user moved it in between, in which case leave it where they put it.

### 3.3 Closing

**Subtract the strip's width from the current width, not a remembered number.** If the user resized
the window while the strip was open, restoring a stored width would undo their resize, and that
reads as the page fighting them.

### 3.4 It stays open

Once opened it stays open — across record changes, searches, everything. Following the selection is
the entire point, so a panel that dismisses when you click a row is worse than no panel at all.
**This is the one thing not to copy from the colour picker**, whose palette dismisses on click-away
because a palette is used once and finished with.

Open or closed persists for the session and across pages: open it on one browse page and the next
one opens with it showing.

## 3.5 The panel's own header

Three controls across the top of the panel:

```
  [ ◀ ]            [ Close ]            [ ▶ ]
```

- **`◀` docks the panel to the left of the page. `▶` docks it to the right.** The arrow for the
  side it is already on is disabled, so the pair always shows where it can go rather than where it
  is.
- **Close sits centred in the panel's width**, between them. **It is the only way to close the
  panel.** The layout row button opens it and does nothing else, which is how every other panel on
  a page behaves, and it puts the way out on the thing being closed rather than across the page.

Close lives here and nowhere else. People look for the way out at the thing they want gone, and a
close that is also across the page is a close that can end up behind something.

## 3.6 Docking, and why by click rather than by drag

**The panel docks left or right. It is never dragged, floated or placed freely.**

- **Dragging is continuous pointer sampling**, which is what degrades under VirtualUI: mouse-move
  events are coalesced or arrive stale, so a dragged panel lags the cursor or drops mid-drag.
  `CLAUDE.md` records this and the tile drop-downs already hit it. Two arrows are two clicks, and
  clicks always arrive.
- **Free placement would break the reason the window grew.** The form widens to make room for the
  strip at one edge. A panel dropped in the middle leaves a gap at that edge and covers the grid —
  the arrangement the growth existed to avoid.
- **The viewport is fixed and does not scroll**, so a panel dragged past the edge could not be
  retrieved. A panel that can only be at one edge or the other cannot get lost.

**Docking left grows the window leftwards.** The form's `Left` decreases by the strip's width so
the grid stays exactly where it is on screen and the new space appears on its left — the mirror of
the right-hand case, and the same rule in §3.1 applies when there is no room: the shortfall comes
out of the grid.

Which side, like open or closed, persists for the session and across pages.

**Width is not adjustable.** If it ever needs to be, cycle two or three preset widths on a click
rather than adding a splitter that has to be dragged.

## 4. The grid

Three columns. Every cell read only. No sorting, no editing, no selection that acts on anything.

| # | Column | Content |
|---|---|---|
| 1 | `#` | the row's position, 1, 2, 3 — numbered as drawn |
| 2 | Field | the field's caption |
| 3 | Value | the field's value for the selected record |

Column 1 is numbered at draw time from the row index, not stored and not bound — the same approach
`Roles_B` uses for its order column, and for the same reason: a position cannot disagree with what
is on screen.

The grid refills when the browse selection changes, and empties when nothing is selected.

**It scrolls vertically**, because a table with sixty columns produces sixty rows and the strip is
tall but not that tall. A `DataGridView` does this on its own once its rows exceed its height — the
only requirement is that it fills the panel rather than sizing itself to its content. Stated here
so nobody later "fixes" it into an auto-sizing grid.

**It does not scroll horizontally.** Three columns: the number and the caption take what they need,
and Value fills the rest. A value too long for the space is the Value column's problem to solve —
by wrapping or eliding — not a reason to make the user scroll sideways to read it.

## 5. Which fields

**Every column of the table named in the page SQL's `FROM` clause**, in table order, regardless of
what the `SELECT` names.

`Base_B` already resolves a result's schema through `GetSchemaFromSelectSql`, and `DataAccess`
already reads a table's columns. Neither needs a new path.

## 6. Where the values come from

The browse grid's `DataTable` holds only the columns the page's `SELECT` names, so the values are
not already in memory.

**Fetch the record when the selection changes — but only while the panel is open.**

- **Closed**, which is most pages most of the time, it costs nothing at all.
- **Open**, it costs one query per row clicked, and the panel was opened precisely to see those
  fields.

The first draft recommended the opposite — widening every page's `SELECT` to carry all columns, so
the values were already in hand. That was right when only `_BHF` pages paid for it. Now that the
feature is on every browse page, a permanently wider `SELECT` taxes every page whether or not
anyone ever opens the panel, and clicking down a grid is the most repeated action there is. Paying
only while the feature is in use is the better trade.

## 6.1 Passwords, never

**No field whose name contains "password" appears in the panel**, whatever it is called —
`Password`, `PasswordHash`, `PasswordSalt`, or one added next year. Matched on the name rather than
against a list, so nobody has to remember to extend the list. `FW_Base_U` redacts its audit
snapshots by the same test.

**A naming convention for other fields to keep out is proposed, not built.** The idea is a prefix
on the column name — `NHF_Whatever` — so a table can say for itself which of its fields never
belong on screen, rather than the decision living in code. It is worth doing when there is a second
case; one case is a rule, not a convention. Until then the two live rules are the password test
above and the role-invisible test in section 8.

## 6.2 Lookups show what the page shows

A page's SQL resolves its own lookups: it joins and aliases the description back to the field's own
name, so a generated browse SELECT reads `... AS [AssignedManagerID] FROM dbo.[FW_USERS] U LEFT
JOIN ...`. The selected browse row therefore already holds the manager's *name* where the table
holds the manager's *id*.

**So the value shown is the grid's, where the grid has one, and the table's otherwise.** The row is
already in memory, which makes this free — and it means the strip cannot disagree with the grid
beside it about what a field says.

The alternative was resolving lookups from metadata, which would mean a query per lookup per click
and a second implementation of something the page SQL already does.

## 7. Captions

Column 2 shows, in this order:

1. `FW_RoleFields.OverrideCaption` for that role and field, where one is set
2. the `FW_Pages` field caption, where one is set
3. `DisplayNameFormatter.ToDisplayName` on the column name

**This is not a new resolver.** `GetPageInitMetadata` already returns `FieldCaptions` and
`InvisibleFields` in the batch every page runs at startup, so the captions arrive with no extra
query. Use them. Do not add a second lookup.

## 8. Permissions

The panel shows data, so it is bound by the same rules as the page.

- **A field hidden by `FW_RoleFields` does not appear here. Decided 2026-09-07.** A permission that
  hides a field on one surface and not another is not a permission, and a panel that shows
  everything would quietly become the way round the field permissions configured in `Roles_U`.
  "Every field" therefore means every field this role is allowed to see, which is the honest
  reading of it.
  `GetPageInitMetadata` already returns `InvisibleFields` in the batch the page runs at startup, so
  this costs nothing to enforce.
- **The button follows the page's Read permission.** A role that cannot read the table does not
  have the page open in the first place, so this costs nothing, but it should be stated rather than
  assumed.
- Unlike the colour picker, the button is **not** restricted to Application Admins. Seeing a
  record's own fields is not an administrative act.

## 9. What this needs from `Base_B`

Small, and one restore point covers it. **`Base_B` is behind the QBE Visibility Guardrail: run the
`Create Base_B Restore Point` task before the first edit.**

1. Construct and attach the widget, beside where the colour picker is constructed — before the
   `buildDefaultBrowseShell` early return, so a page building its own shell gets one too. The
   colour picker had exactly this bug: it was created after the early return, so `Roles_B` never
   had one.
2. Position its button at the right-hand end of the layout row, which is laid out right to left
   through a cursor - so it is placed first and everything already there shifts left by its width.
3. Tell the widget when the browse selection changes.

Nothing else. Anything the widget needs from the page comes through a `Protected Overridable`
member, never a copy.

## 10. Order of work

1. The widget class, with the grid, the captions and the record fetch.
2. `Base_B`: construct, attach, position, notify. Restore point first.
3. The window growth and the working-area fallback.
4. Prove it on one real page at two window sizes, including a narrow one where the shortfall path
   runs.
5. A browse-regression check that the widget is constructed before the early return, so it cannot
   regress the way the colour picker did.

Steps 1 and 2 stand on their own — a strip that takes its width from the grid is useful before the
window growth exists.

## 11. Decisions still open

1. **Does the open/closed state persist beyond the session?** Recommendation: no. Storing it per
   user and per page is possible — `FW_TableLayouts` is that shape — but it is a write on every
   toggle, and a preference that outlives the reason for it, a narrow window on one machine, is
   worse than one that does not.
2. **A page whose SQL joins more than one table** — whose columns are "all the fields"? The `FROM`
   clause names one table first; the recommendation is that one, ignoring joined tables, but it
   needs confirming against a real page.
