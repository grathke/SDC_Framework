# The Form Designer

**Proposed, nothing built.** Designed 2026-09-14. This expands section 5 of
`PAGE_LAYOUT_RUNTIME_SPEC.md` — the step that document called "the designer" and left as one
paragraph. Read both before touching `FW_Base_U`'s field helpers, `BuildMaintenanceSource` in
`PageGenerator.vb`, or the maintenance panel in `PageGeneration_U.vb`.

Two things are being decided here, and they are separable:

1. **A second kind of `_U` page**, chosen in the generation request, whose field block is built from
   data at runtime rather than from emitted code.
2. **The designer that edits that data** — what its palette holds, how a control comes into
   existence, and what the screen looks like.

---

## 1. What the request already knows

By the time anybody opens a designer, the generation request holds the whole input:

| Held on `FW_GeneratedPages` | Used for |
|---|---|
| `TableName` | the schema reads below |
| `MaintenanceFields` | which columns the page has, **in the order they were arranged** |
| `Column2Fields` | which of them sit in the second column |
| `RequiredFields` | the App Admin asterisk and the blue label |
| `LookupFields` | the combo specs, `Field -> Table.Value displayed as Display` |

The schema supplies the rest, and it is read from the database rather than guessed from a name:
`GetDateColumnKinds`, `GetBitColumnNames`, `GetNullableColumnNames`, `GetComputedColumnNames`,
`GetTextColumnMaxLengths`.

**The designer therefore never opens empty.** It opens showing the page the generator would have
produced from that request — the same fields, the same order, the same two columns. Every gesture
after that is a refinement of a working page. This is the most important interaction decision in
the document, and section 5 explains why each of the surveyed products either got it right or paid
for getting it wrong.

---

## 2. Nine products, and the one idea to take from each

The survey is here because the shape of this kind of tool has been settled and re-settled for
thirty years. The places where every generation agrees are worth more than any novel idea.

### 2.1 VB6, and the Visual Studio WinForms designer

A toolbox of control types down the left, a form you draw on, a **property grid** on the right
listing every property of the selection. Snap to grid, align and distribute commands, a tab-order
dialog, lock controls.

**Take: the property grid.** Direct manipulation settles position; a property sheet settles
everything position cannot express — caption override, width, required, read-only, the column a
control binds to. Without one, every such setting needs its own gesture invented.

**Leave: free pixel placement.** It is the one thing this framework cannot accept — see section 3.

### 2.2 Microsoft Access, form design view

The same canvas, plus a **Field List** pane. Drag a field from it and Access creates a label and a
bound control together, choosing a check box for Yes/No and a combo for a lookup column.

**Take: the field-first palette.** A field dragged from a list cannot be bound to nothing and
cannot be given the wrong control for its type. The list also doubles as a checklist — a field
already placed is marked, which answers "what have I not put on yet" without counting.

### 2.3 PowerBuilder, the DataWindow painter

Bands rather than a free canvas: header, detail, summary. The detail band is one record's worth of
controls, repeated. The part worth stealing is not the painter at all — it is that a column's
**edit style** (edit mask, drop-down, check box, radio buttons) is defined **once, in the data
dictionary**, and every window using that column inherits it.

**Take: a control-type override that lives with the column, not with the page.** A `bit` shown as a
Yes/No combo is a reasonable wish. Granted per page it undoes the type decisions in
`PAGE_LAYOUT_RUNTIME_SPEC.md` section 3 one page at a time; granted per column, once, it is a
policy — and it stays true on the next page that uses the column.

### 2.4 Clarion

A data dictionary carrying each field's prompt, format and control type; templates that generate
whole windows from it; **embed points** where hand-written code goes, which survive regeneration.

**Take: nothing new — this is confirmation.** The embed points are the four hooks already built on
2026-09-14 (`OnFieldsBuilt`, `OnRecordBound`, `OnValidating`, `OnBeforeSave`), and the dictionary's
prompt is `DisplayNameFormatter.ToDisplayName`. A thirty-year-old tool arriving independently at
the same two-file split is the strongest evidence available that the split is right.

### 2.5 Delphi and C++Builder

Component palette, object inspector, and an **alignment palette** — a small floating toolbar of
align-left, same-width, distribute-vertically buttons.

**Take: the alignment toolbar, rewritten in this framework's terms.** Not "align left", which is
meaningless when position is snapped anyway, but *move up a row*, *send to the other column*,
*split this row in two*, *one column*, *two columns*.

### 2.6 WinDev and WebDev

The data model — the *analysis* — is first class, and windows are generated from it. Captions live
on the data item, not on each window. Controls declare **anchors** instead of being laid out by a
manager. Each control has a fixed, named list of events. And a window inherits from a **template**:
change the template, and every window built on it changes.

**Take: templates.** Every `_U` page here already shares a header, a caption, an OK and a Cancel
button, a background colour and a Help Desk launcher, because `FW_Base_U` hard-codes them. That is
a template expressed as a base class, and it works until two applications on this framework want
different ones. Worth knowing the direction; not worth building yet.

### 2.7 Oracle APEX, the Page Designer

Three panes. Left: a tree of the page's regions and items. Centre: a layout grid that items are
dragged into, snapping to a twelve-column grid rather than to pixels. Right: a property editor for
the selection. In front of all of it, a **Create Page wizard** asking what *kind* of page this is —
form, report, master-detail, wizard — before showing any of the three panes.

**Take: both.** The three-pane shape, and the page-kind question asked first. APEX is the closest
existing thing to what is wanted here, and its grid-snap is the same concession to a layout model
that section 3 forces.

### 2.8 Ironspeed Designer

Point it at a database, tick tables, and it generates the application: list pages, record pages,
filters. Per table you chose the **page type**. Per column, a row in a **settings grid** — the
control (text box, text area, drop-down, check box, date picker, upload), the format, the
validation, whether it appears on the list page, the record page, or both. A foreign key became a
drop-down with the display column chosen in that grid. Generated code sat in one file and yours in
another, which is what made regeneration survivable.

**Take: the settings grid is the authoritative model, and the canvas is a view of it.** This matters
more than it sounds. `PageGeneration_U` already *has* that grid — Include, Required, Lookup,
Displays, Column. A designer that replaces it starts an argument about which one is true. A
designer that edits the same underlying document leaves the grid as the fast way to do bulk work
and the canvas as the way to see the result. `Suggest Columns` becomes `Auto-arrange`, doing on the
canvas exactly what it does in the grid today.

### 2.9 The modern low-code tools — Power Apps, Lightning App Builder, Retool, Appsmith, Budibase

Different vendors, one converged interface: palette left, canvas centre snapping into a grid or a
vertical stack, inspector right, and **binding chosen from a picker rather than typed**.

**Take: the convergence itself as evidence.** Five independent products, three decades after VB6,
arrive at palette–canvas–inspector. Build that. Also take binding-by-picker: the field a control
binds to is chosen from the table's columns, never typed, which removes the class of fault where a
control is named for a column that does not exist.

FileMaker's layout mode earns a footnote for one detail: each field has a **control style**
dropdown, and that dropdown lists only styles the field's type can carry. It is the whitelist that
makes 2.3's override safe.

---

## 3. The constraint everything above has to fit

**A row is the unit, and rows are identified by `Top`.** `IsRowLayoutControl` and
`IsCollapsibleRow` in `Base_U.vb` group controls by their `Top` and collapse a row when permissions
have hidden every field on it; `HideFieldAndCloseGap` measures the distance to the next row in the
same column; `GeneratedFieldsBottom` is `20 + rows * 42`.

A designer offering free pixel positions would break permission-driven hiding without saying so —
two controls a pixel apart stop being the same row. Three separate faults on 2026-09-14 were this
one mistake, recorded in `PAGE_LAYOUT_RUNTIME_SPEC.md` section 1.

**The dotted grid on the canvas is therefore a picture of the layout model, not decoration.** Drop
targets are row slots. Nothing lands between rows.

---

## 4. The two kinds of `_U` page

Add `LayoutMode` to the generation request. One radio pair, two values:

| | `Coded` | `Designed` |
|---|---|---|
| Field block comes from | emitted positions in `<Page>.Generated.vb` | a layout document read at runtime |
| Changing the layout needs | regenerate, rebuild | neither |
| Compile-time check on field names | yes | no — the unmapped-field report catches it at page open |
| Existing pages | all of them | none yet |

Both build the *same controls* through the *same four helpers* — `AddField`, `AddComboField`,
`AddDateField`, `AddCheckField`. `Designed` emits a `BuildGeneratedFields` that is one call to
`BuildFromLayout()`. `BuildGeneratedFields` as it stands today is already a hard-coded version of
that loop, which is the tell that the loop is the real thing.

**Freeze to code.** One command on the designer emits the current layout as a `Coded` page. It makes
the choice reversible, costs very little, and it is the answer to anybody who does not want a page
they depend on reading its shape from a table.

---

## 5. How a control comes into existence

Four options, and the recommendation is the fourth because it is the first three in order.

**Option A — field list only.** Palette lists the table's columns; drag creates the control the
schema dictates. Nothing unbound can exist. Cannot express a section heading, a group box, a static
note or a button.

**Option B — toolbox only, as VB6.** Palette lists control types; the property sheet then assigns a
column. Familiar. Allows a control bound to nothing, and a `bit` in a text box — the "type True and
it accepts Ture" problem `AddCheckField` was added to remove.

**Option C — one palette, two sections.** Fields of the table above, furniture below. Field items
are type-dictated; furniture is free and goes through `DeclareUnboundField(controlName, reason)`.
This is the decision already recorded in `PAGE_LAYOUT_RUNTIME_SPEC.md` section 5, and it stands.

**Option D — C, opened on an auto-arranged page.** The designer starts from the request's
`MaintenanceFields` in their saved order, already laid out, already two columns where
`Column2Fields` says so. The palette's field section is then mostly ticked off, and it exists for
the fields you left out, the ones you remove and put back, and a column added to the table since.

Option D is what Ironspeed got right and what a pure canvas tool gets wrong: **the first pass is
free, and the tool is for the second pass.** A designer that opens blank asks somebody to place
forty fields by hand to reach the page they already had.

| Palette item | Placed as | Named | Bound |
|---|---|---|---|
| a column of the table | the control its type dictates | `TextBox_<Field>`, `ComboBox_<Field>`, … | to that column |
| Section heading | a `Label`, larger, own row | free | not a column — declared |
| Static note | a `Label` | free | declared |
| Separator | a one-pixel `Panel`, own row | free | declared |
| Button | a `Button` | free | declared |
| Spacer | an empty row | — | — |

---

## 6. The layout document

Rows and slots, at the row pitch. Stored as JSON.

```json
{
  "version": 1,
  "table": "FW_Employees",
  "columns": [ { "left": 20 }, { "left": 490 } ],
  "rows": [
    { "slots": [ { "col": 0, "field": "FirstName", "required": true },
                 { "col": 1, "field": "LastName", "required": true } ] },
    { "slots": [ { "col": 0, "kind": "heading", "text": "Employment" } ] },
    { "slots": [ { "col": 0, "field": "HireDate" },
                 { "col": 1, "field": "Active" } ] },
    { "slots": [ { "col": 0, "field": "Notes", "span": 2, "rowSpan": 2 } ] }
  ]
}
```

Why this shape rather than the `(field -> row, column)` pairs proposed as step 1:

- **A row is an object**, which is the thing `IsCollapsibleRow` reasons about. Making it an object
  in the data as well means the loader and the collapse logic agree by construction.
- **Furniture has somewhere to live.** A pair list keyed by field name cannot hold a heading.
- **`span` and `rowSpan` have somewhere to live**, which is what a memo box needs.
- Row order **is** the tab order, with an optional explicit override. `SetManualTabOrder` takes the
  slots in reading order and there is no second ordering to keep in step with the first.

`rowSpan` is the one entry here that changes `FW_Base_U`: `HideFieldAndCloseGap` and
`GeneratedFieldsBottom` both assume a pitch of 42. **Leave `rowSpan` out of the first cut** and keep
the pitch fixed — it is the only item on the list that can break a working page.

**Where it is stored.** `FW_GeneratedPages.MaintenanceLayout`, written by the request, read at page
open. One column on a row the request already reads, which is what `Column2Fields` and `HotFields`
did before it.

Reading it at runtime is one round trip **per session, not per page**: one query reads every layout
there is, keyed by page name, the same shape as `EnsurePageAliasCache`. The table holds one row per
generated page.

```sql
SELECT MaintenancePageName, MaintenanceLayout
FROM   dbo.FW_GeneratedPages
WHERE  MaintenanceLayout IS NOT NULL
```

The alternative — a row on `FW_Pages` keyed by the `_U` page name — is what would let a
hand-written `_U` have a designed layout too. It is a bigger change, `FW_Pages` rows are browse rows
today, and nothing yet needs it. **Decided for now, not closed.**

---

## 7. The screen

Three panes, because nine products agree on three panes.

```
┌─ command bar ─────────────────────────────────────────────────────────┐
│ Auto-arrange  1 col  2 col  + Heading  Preview  Freeze to code  Save  │
├────────────┬───────────────────────────────────────┬─────────────────┤
│ PALETTE    │ CANVAS                                │ PROPERTIES      │
│            │                                       │                 │
│ Fields     │  1 · First Name ▢▢▢▢  Last Name ▢▢▢▢  │ First Name      │
│ ✓ FirstName│  2 · ── Employment ─────────────────  │ Column     1    │
│ ✓ LastName │  3 · Hire Date  ▢▢▢   Active  ☑       │ Row        1    │
│   Notes    │  4 ·                                  │ Required   ☑    │
│            │                                       │ Width    320    │
│ Furniture  │                                       │ Caption    …    │
│  Heading   │                                       │                 │
│  Note      │                                       │                 │
│  Separator │                                       │                 │
└────────────┴───────────────────────────────────────┴─────────────────┘
```

**A row-number gutter down the left of the canvas.** The rows are the model; numbering them makes
the model visible, and makes "move it to row 6" a thing that can be said.

**Preview opens the real page.** The designer builds through the same four helpers the page uses,
which means a preview is not an approximation — it is the page, with a sample record. Cheap to
build, and it removes the whole class of "the designer showed me something else".

### The colour vocabulary

One colour per control type, used in three places at once: the palette chip, a marker on the placed
control's left edge, and the property pane's header. The page's shape then reads at a glance.

| | Colour | Why |
|---|---|---|
| Text | slate blue | the default, and the quietest |
| Memo | deeper slate | a text box that is taller, not a different thing |
| Lookup | violet | a choice from a list |
| Date | teal | |
| Yes / No | amber | |
| Computed | grey | never typed into; the page shows it read-only |
| Furniture | warm grey | not a field, and should not look like one |

**One colour is reserved and must not be borrowed.** `AppAdminRequiredBackColor` is a load-bearing
exact ARGB — `ShouldSkipBrRequiredStyling` reads it to decide that App Admin required beats the
`FW_RoleFields` yellow. Nothing in the designer may paint that colour for any other reason, and a
required field on the canvas shows the same blue label it will have on the page.

### What is achievable in WinForms

Flat, saturated, high-contrast — the VS Code palette rather than a glass one. Owner-drawn buttons
give flat fills, rounded corners through a `GraphicsPath`, and hover states. Double buffering keeps
the drag smooth. Shadows and blur cost more than they return.

The designer is a local development tool and never runs in a VirtualUI session, which is why drag
is allowed here at all — the delivery rule preferring click over hover does not reach it.

---

## 8. The plan

**Phase 1 — a page built from data.** `BuildFromLayout` on `FW_Base_U`, the `MaintenanceLayout`
column, the session cache, `LayoutMode` on the request, and the generator emitting the thin
`BuildGeneratedFields` when the mode is `Designed`. Freeze to code. No designer.

*Verified by:* regenerating an existing page in `Designed` mode and comparing it with the `Coded`
one field by field, position by position. Both regression scripts. The unmapped-field report on a
layout naming a dropped column.

**Phase 2 — the sandbox designer.** The three panes, field-first palette, drag to rows, save the
document, Preview. A page in `070_PAGEGENERATION`, obviously named as an experiment, not wired to
any request. `900_SANDBOX` is excluded from compilation and cannot host a page that has to run.

*Judged as:* an experiment. No ActionKey, no icon, no permission, no New Page Regression Guardrail
until it stops being one.

**Phase 3 — wire it to the request.** A `Design…` button on `PageGeneration_U`'s maintenance panel,
seeded from `MaintenanceFields` and `Column2Fields`, writing the layout back. `Suggest Columns`
becomes `Auto-arrange`, and both editors work on one document.

**Phase 4 — furniture, the property sheet, and the type override.** The whitelist of control types a
column can carry. `rowSpan`, and the change to `HideFieldAndCloseGap` it needs.

**Phase 5 — the questions this does not answer.** Per-user layouts, which
`PAGE_LAYOUT_RUNTIME_SPEC.md` section 8 leaves open. Tabs and sections. Master-detail, which is the
page *kind* question APEX and Ironspeed both ask and this document has deliberately narrowed to one
kind.

### Before any of it is called done

It crosses page generation, `FW_Base_U`, the database and the generated page contract, which puts it
under the Application-Wide Change Gate. The behaviour matrix must cover: a `Coded` page unchanged; a
`Designed` page created, updated, deleted and cancelled; a permission-hidden field collapsing its
row on a designed page; a required field's blue label and border; a layout naming a column that has
been dropped; a layout missing entirely; and a designed page whose companion file places controls
below `GeneratedFieldsBottom`.

---

## 9. The decisions needed before phase 1

1. **Layout storage** — `FW_GeneratedPages` with a session cache, as section 6 recommends, or a
   `FW_Pages` row per `_U` page so a hand-written page can be designed too.
2. **Default for new pages** — does the request open on `Coded` or `Designed`? Recommended:
   `Coded`, until phase 3 is verified on a real page.
3. **Freeze to code** — phase 1, or not at all. Recommended: phase 1, while the emitter is already
   open.
4. **`rowSpan`** — held back to phase 4 as recommended, or wanted in phase 1 with the `Base_U` row
   arithmetic change that implies.
