# Runtime Page Layout

**Proposed, not built.** Agreed 2026-09-14. Read this before changing how a `_U` page is laid
out, before adding to `FW_Base_U`'s field helpers, and before starting the designer.

A generated `_U` page gets its layout from code the generator writes. This proposes moving the
layout into data the page reads at runtime, and then building a visual designer that edits that
data. Three steps, each useful on its own and in this order.

---

## 1. Why a row is the unit

Everything on a `_U` page is a row. The field helpers all take a `y`, and the framework decides
what a row *is* by comparing `Top`:

- `IsRowLayoutControl` and `IsCollapsibleRow` group controls by `Top` and collapse a row when
  permissions have hidden every field on it. That is how a hidden field closes up the page.
- `HideFieldAndCloseGap` measures the distance to the next row in the same column.
- `GeneratedFieldsBottom` is `20 + rows * 42`.

**This is the constraint that shapes the whole feature.** A designer that allowed free pixel
positions would stop two controls being "the same row", and permission-driven hiding would break
without saying so.

Three separate faults on 2026-09-14 were all this one mistake:

- `AddCheckField` placed its box at `y + 3` to look centred. It was therefore not on its own
  row: hiding the field left the box behind, and the column closed up by three pixels instead of
  forty-two. Fixed by giving the box the full row height and centring the glyph inside it.
- `HideFieldAndCloseGap` shifted rows up *before* hiding, then hid whatever was at the old row
  position - by then the row that had just moved into it.
- A nullable date recorded only its `Value` in the unsaved-changes snapshot, so ticking or
  unticking read as no change at all.

A designer must snap to the row pitch. The dotted grid is a picture of the layout model, not
decoration.

---

## 2. The geometry, as the generator emits it

| | |
|---|---|
| First row | `y = 20` |
| Row pitch | 42 |
| Label | at `(fieldLeft, y)`, `120 x 26` |
| Control | at `(fieldLeft + 130, y)`, usually `320` wide |
| Column width | 450 - label 120, gap 10, control 320 |
| Column 1 left | 20 |
| Column 2 left | `20 + 450 + gap`, where gap is 110 when the Zip Coder button needs room in column 1, otherwise 40 |
| Form width | 600 for one column; `column2Left + 450 + margin` for two |
| Form height | `max(120, 55 + rows * 42)` plus room a companion asked for |

`GeneratedFieldsBottom` is emitted as a `Protected ReadOnly` field so the hand-written half can
place things below the fields without hard-coding a number that a new field would invalidate.

---

## 3. Which control a column gets

Decided from the schema, never from the column's name:

| Column type | Control | Read from |
|---|---|---|
| declared foreign key with a lookup spec | `ComboBox` | the request's `LookupFields` |
| `date`, `datetime`, `datetime2`, `smalldatetime`, `time` | `DateTimePicker` | `DataAccess.GetDateColumnKinds` |
| `bit` | `CheckBox` | `DataAccess.GetBitColumnNames` |
| anything else | `TextBox` | - |

A lookup wins over a date. A computed column is never a date field - the database fills it.

Supporting reads: `GetNullableColumnNames` decides whether a date gets its check box;
`GetTextColumnMaxLengths` sets `MaxLength` and narrows the box towards what the column holds;
`GetComputedColumnNames` marks the ones that are read-only and never required.

---

## 4. The helpers a layout loader would call

All on `FW_Base_U`. Each creates `Label_<Field>` plus its control, which is the naming the
permission system, the tab order manager and the unmapped-field report all read.

```vb
AddField(caption, y, readOnly, Optional required, Optional fieldLeft,
         Optional multiline, Optional fieldWidth, Optional fieldHeight, Optional labelText) As TextBox

AddComboField(caption, y, Optional required, Optional fieldLeft,
              Optional fieldWidth, Optional labelText) As ComboBox

AddDateField(caption, y, Optional required, Optional fieldLeft,
             Optional nullable, Optional showTime, Optional labelText) As DateTimePicker

AddCheckField(caption, y, Optional fieldLeft, Optional labelText) As CheckBox
```

Reading and writing values:

```vb
SetDateField(picker, value)        DateFieldValue(picker) As Object    ' Nothing = null
SetCheckField(box, value)          CheckFieldValue(box) As Object
ConfigureLookupCombo(combo, source, valueMember, displayMember, selectedId, ...)
SetManualTabOrder(ParamArray controls)
DeclareUnboundField(controlName, reason)   ' a control that names no column, on purpose
HideFieldAndCloseGap(fieldName)            ' hides a field and pulls its column up
```

`DeclareUnboundField` matters: a control named for a column the table does not have stops the
page saving, with a report naming the control. That guard caught the role combo twice on
2026-09-14 and is the reason losing compile-time checking costs less than it appears to.

---

## 5. What changes, step by step

**Step 1 - store the layout as data.** `(field -> row, column)` on `FW_GeneratedPages`, replacing
`Column2Fields` and the order-of-the-list model. The current two-column map and Suggest Columns
become one case of it.

**Step 2 - build from it at runtime.** `FW_Base_U` reads the layout and calls the helpers above.
`BuildGeneratedFields` in the generated half is already a hard-coded version of that loop, which
is the tell that the loop is the real thing. Changing a design would then need no regenerate and
no rebuild - which is how `_B` pages already work, reading their columns from `FW_Pages.Table_SQL`
at runtime. `PAGE_GENERATION_SIMPLIFICATION.md` argues the same for browse pages.

**Step 3 - the designer.** A canvas with a dotted grid, a palette of the table's fields, drag to
place, snap to the row pitch. It edits the data from step 1 and owns `<Page>.Generated.vb`
outright.

### What the palette holds

Two orders are possible and they fail differently.

**Field-first** - pick the table, the palette lists its fields, drag one on. The control type
comes from the schema, as section 3 describes. You cannot place a control bound to nothing, and
you cannot choose the wrong control for a column.

**Toolbox-first** - drag a TextBox, then assign a field to it. More familiar, and it is what a
designer usually feels like. But it allows a control bound to nothing, which this framework
already refuses to save, and it allows a `bit` in a text box - the "type True and it accepts
Ture" problem that `AddCheckField` was added on 2026-09-14 to remove.

**Do both, with one palette holding two kinds of item.** Toolbox-first buys the thing field-first
cannot express: everything that is not a field - a section heading, a group box, a static label,
a button. Those have no column and never will.

| Palette item | Placed as | Named | Bound |
|---|---|---|---|
| a field of the table | the control its column type dictates | `TextBox_<Field>` and so on | to that column |
| heading, label, separator, button | the control chosen | free | not a column - declare it |

Anything unbound goes through `DeclareUnboundField(controlName, reason)`, or the page will not
save and the report will name the control. That is the guard working, not a problem to route
around.

**Overriding a field's control type** - a `bit` shown as a Yes/No combo, say - is worth allowing
eventually, but only against a list of types that can actually carry the column. Offered freely
it undoes the type decisions in section 3 one page at a time.

### The first cut

**The first cut of step 3 is a sandbox page**, for testing and investigating rather than for
anybody to use: an empty `_U`, pick a table, list its fields, drag them on. It exists to prove
the model - that a layout can be held as data, snapped to rows, and turned back into real
controls - before any of it is wired to the generator or to a page somebody depends on.

Judge it accordingly. It does not need the New Page Regression Guardrail, an ActionKey, an icon
or a permission until it stops being an experiment. Say plainly which of the two it currently is.

One trap: the removed `900_SANDBOX` (gone 2026-09-18) is **excluded from compilation** in the project file, alongside `tests`,
`project-backup` and `restore-points`. A page that has to run cannot live there. Put it somewhere
that builds and keep it obviously named, or it will be mistaken for a finished page later.

---

## 6. Why this is possible now

A generated `_U` was split on 2026-09-14 into two files:

```
<Page>.Generated.vb   the generator's - rewritten whole, every time
<Page>.vb             yours - written once, never read or rewritten again
```

Four hooks connect them: `OnFieldsBuilt`, `OnRecordBound`, `OnValidating(ByRef allowSave)` and
`OnBeforeSave(values)`. A hook nobody implements compiles away to nothing.

A designer can therefore rewrite the generated half freely, because hand-written code is safe in
the other file. Without the split, a designer and a developer would be fighting over one file.

---

## 7. Two things that are not blockers

**Thinfinity.** Drag is continuous pointer sampling and degrades over HTML5, which is why the
delivery rules say prefer click over hover. It does not apply here: the page generator is a local
development tool that never runs in a VirtualUI session, and a designer is the same tool.

**Losing compile-time checking.** A field that no longer exists becomes a runtime problem rather
than a build error. The unmapped-field report already catches exactly that at page open, names
the control and the missing column, and refuses to save.

---

## 8. The open question

Per-user layouts. `FW_TableLayouts` already stores browse-grid arrangements per page and per
user, so somebody will ask for the same on a maintenance page. That needs a policy decided up
front: when a developer moves a field and a user has their own arrangement, whose wins?
`PAGE_FIELD_PICKER_SPEC.md` section 5 records how the browse pages answer it - "Last Used" beats
the `* Default`, which is why a reset that leaves it behind changes nothing anyone can see.

Not part of steps 1 to 3. Decide it before building it.
