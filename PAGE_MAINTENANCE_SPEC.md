# Maintaining a generated page without starting it over

**Status: proposed, not built.** Designed 2026-09-09. One part of it exists: saving a generation
request already applies the page settings that are data (commit `0f9cb73`). Read this before
changing the page generator's file writing, the maintenance baseline hash, or anything that decides
whether a page counts as manually edited.

Two problems, one shape.

**A hand-edited `_U` page is frozen.** The generator hashes the whole file and stores it. A file
whose hash no longer matches is reported as manually edited and skipped, so a page that grew a
dual-list for assigning roles, or a validation rule somebody asked for, can never gain a field
again - not without losing the edit.

**Generate means two different things.** Creating a page and adding a field to one are the same
button, so the act that should be routine carries the weight of the act that should be rare.

---

## 1. Three actions, three blast radiuses

| | Writes | Destroys | Needs a rebuild |
|---|---|---|---|
| **Save** | the `FW_Pages` row: grid columns and order, caption, Hot Fields on/off | nothing | no |
| **Update** | the generated half of the page's code: its fields | nothing | yes |
| **Generate** | both halves, plus the menu or dashboard button | layouts, saved searches, the Hot Fields list | yes |

**Save is built.** It applies on saving the request, updates a page that exists and never creates
one, and says what it applied and what still needs generating.

**Update is the one people would use most.** Adding a field to a `_U` page is ordinary maintenance.

**Generate becomes rare, and can afford to be loud** - which is what makes the reset in
`PAGE_FIELD_PICKER_SPEC.md` section 5 acceptable. Today Generate carries both meanings, which is
why it feels heavy for adding a column.

## 2. The file split, which is what makes Update safe

A generated `_U` page becomes two files:

```
UserX_U.Generated.vb    the fields, their controls, their lookups   - the generator owns this
UserX_U.vb              everything anybody added by hand            - the generator never reads it
```

VB partial classes, so both halves are one class at compile time. The generated file offers a method
the custom file calls - `BuildGeneratedFields()` or similar - so the custom half decides where the
generated controls sit rather than being interleaved with them.

**Update then rewrites one file wholesale.** No merge, no hash comparison, no judgement about
whether somebody touched it. The question "was this file edited" stops being asked, because the
answer stops mattering.

**Two alternatives considered and not chosen:**

- **An anchored region** in the single file, the generator replacing what sits between markers.
  There is precedent - that is how ribbon tiles are written into `MenuFormInitializer.vb` and icons
  into the dashboards, both hand-maintained files. It is cheaper, and it still loses an edit made
  inside the region. A file boundary cannot be edited into by accident; a comment marker can.
- **A three-way merge.** `SavePageGenerationMaintenanceBaseline` stores the full previous source,
  not only its hash, so baseline plus current plus newly generated is a genuine three-way merge -
  what git does. It is the most powerful and much the most work, and it earns its place only if the
  split proves insufficient. Recorded because the data is already being kept for it.

**Removing a field a custom edit depends on breaks the build**, and that is the right failure: an
error naming the line, at once. Today the same mistake is silent - the page is skipped as manually
edited, so you get neither the change nor the error.

## 3. A separate page, not an Update button

`FW_PageSettings_U`: one page, offering only what can change after generation.

**Opened by Modify on `FW_PageGeneration_B`**, which passes the page name. See section 6.

**On it:**

- which page is being maintained
- the caption
- the browse grid's fields and their order
- Hot Fields on or off, and which fields
- the `_U` page's fields - the only thing here that writes code
- the lookup display for either

**Not on it, because these describe what the page *is*:** the request name, the page names, the
underlying table, the menu caller, the icon, the create/update/delete behaviour, whether it is a
framework page. Change one of those and you are not maintaining a page, you are describing a
different one - which is the generation request's job.

**Why a page rather than a button.** A button on the request form would leave two thirds of that
form inapplicable, and the alternative - disabling and hiding the parts that no longer apply - is
how somebody ends up unable to find a field they know is there. A separate page offers only what it
can honour.

**App Admin only**, consistent with the panel's own ticking.

## 4. Hot Fields ticks live in three places. One rule.

They can be set on the generation request, on `FW_PageSettings_U`, and on the browse page's own
panel. That is fine, and it is not the two-owners problem, because of what each one writes:

| Surface | Writes | When it takes effect |
|---|---|---|
| the generation request | the page's **birth** value | on Generate only |
| `FW_PageSettings_U` | the **live** value | on save |
| the `_B` panel | the **live** value | at once |

**Two surfaces edit the live value and one restores it.** Editing a record from two screens is
ordinary; what would be wrong is a save from the request quietly overwriting an App Admin's
curation, which is why the request's ticks are applied on Generate and nowhere else.

## 5. What happens to the pages that already exist

A one-time split: each generated `_U` moves its field-building into a new `.Generated.vb`, and
whatever remains stays where it is. It has to be done page by page and read by somebody, because
telling generated code from hand-written code in a file that has been edited is exactly the judgement
the hash was standing in for.

**Not automatic, and not urgent.** A page that is never split keeps today's behaviour, hash and all.
The split is what buys it Update.

**The baseline hash afterwards** covers only the generated file. A page that has been split can no
longer be "manually edited" in the sense the generator means, so that report disappears for it - and
should say so rather than silently stopping.

## 6. Decided, and still open

**Reached from `FW_PageGeneration_B`, by Modify.** Decided 2026-09-09. The browse page's Modify
button opens the settings page directly, passing the **page name** rather than the request id - so
the same page can later be opened from anywhere else, including from a page that has no generation
request at all, which is most of the framework's own pages. It is also how everything around it is
keyed: `FW_Pages`, the caption chain, the Hot Fields list and the saved layouts all key on
`WindowOrPage`, and a request id would need translating at every use.

**Modify routes by whether the page exists.** A request that has been generated opens the settings
page; one that has not opens the request form, because there is nothing yet to maintain. No second
button, and nothing to explain.

**The menu caller, the icon and the page name stay off it.** Decided 2026-09-09, each for its own
reason, and the reasons are worth keeping because they are what makes the page safe to use:

- **The icon already has an owner.** An App Admin right-clicks a tile or dashboard icon and chooses
  a picture; it is stored in `FW_DashboardLayouts.IconFileName` and applied on the next load. It is
  already changeable without regenerating - putting it here too would be a second way to set one
  value.
- **The menu caller is not an update, it is a move.** It means adding a button on one surface and
  removing it from another, and the generator never removes: it only reports that a page already has
  a button elsewhere. That is not a theory - moving `UserX` to the ribbon on 2026-09-09 needed the
  dashboard icon taken off by hand. Offering the choice here would quietly leave a second way in.
- **The page name is a rename**, which is the eight-table procedure in `CLAUDE.md` plus the class,
  the file and the tile. That belongs to the request and a deliberate regeneration.

So everything on the settings page is either data or the generated field list, and nothing on it can
leave the application half-moved.

**Still open:**

1. **Naming.** `FW_PageSettings_U` or `FW_PageFields_U`, and is the action called **Update**,
   **Apply Fields** or **Rebuild Fields**? Update reads well beside Save and Generate but collides
   with the CRUD Update on every other page.
2. **`_B` pages.** They are already almost entirely data - `Table_SQL` decides their columns - so a
   split may buy them nothing. Worth confirming before doing it to both halves out of symmetry.
3. **What Update does when the `.Generated.vb` is missing** - a page generated before the split.
   Probably: create it, and say the page has been split.

## 7. Before this is called done

It crosses page generation, the browse and maintenance base classes, and the database, so the
Application-Wide Change Gate applies. The behaviour matrix must cover:

- a split page and an unsplit one, through all three actions
- Update adding a field, and removing one that custom code references - the second must fail the
  build, not silently
- Generate on a split page: both halves rewritten, custom half replaced, the reset applied
- Save on a page that does not exist yet - it must not create a row
- a page whose `.Generated.vb` was deleted by hand
- the Hot Fields ticks set from each of the three surfaces in turn, and the request's ticks not
  overwriting the live value on Save

Manual verification on real pages, not compilation. Both regression scripts, and a page that has
been hand-edited must survive an Update with its edits intact - which is the whole point.
