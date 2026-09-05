# Page Generation — A Simpler Shape

**Proposed, not built.** Written 2026-09-04 at the end of a session that built main-menu
placement into the generator. Nothing here has been implemented, and none of it is urgent.

The question that prompted it: *is there a better, simpler, more elegant way to generate pages?*

---

## 1. The finding

Here is the whole of a generated browse page, `UsersY_B.vb`, with the body of each handler
elided:

```vb
Public Class UsersY_B
    Inherits FW_Base_B

    Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
        MyBase.New(user, profile, "FW_USERS")
        ...
    End Sub

    Protected Overrides Function HandleDefaultCreateAction() As Boolean   ' ~6 lines
    Protected Overrides Function HandleDefaultUpdateAction(...) As Boolean ' ~6 lines
    Protected Overrides Function HandleDefaultDeleteAction() As Boolean    ' ~35 lines
End Class
```

**Four facts in it are page-specific**: the class name, the table `FW_USERS`, the primary key
`UserId`, and the name of the partner `_U` class. Everything else — roughly fifty lines — is
identical in every page the generator will ever produce.

The delete override carries its own explanation:

> *Soft-deletes the selected record. Without this the Delete button falls through to the base
> placeholder and silently does nothing.*

That is the smell. **A base class with a placeholder that does nothing forces every child to carry
the same forty lines to fix it.** A bug in that delete logic has to be fixed in every page ever
generated. The generator has quietly become a way of *duplicating* shared logic rather than
sharing it — which the Consolidation Guardrail would refuse outright if a person had written it
three times by hand.

---

## 2. The cheap step, and the one to do first

Move `HandleDefaultCreateAction`, `HandleDefaultUpdateAction` and `HandleDefaultDeleteAction` into
`FW_Base_B`, parameterised by table and primary key. The base already knows the table; the key can
be passed or read from the schema the same way the rest of the framework reads it.

The generated `_B` then collapses to a class declaration and a constructor — about twelve lines.

Nothing else in the system changes. No database work, no change to how pages are generated or
filed. The next delete defect is fixed once instead of once per page.

**This is worth doing on its own merits, whatever is decided about the rest.**

---

## 3. The step that follows from it

Once `_B` is only a constructor, it does not need to be a *class*.

The data is already in the database:

| Table | Already holds |
|---|---|
| `FW_Pages` | the table, the SQL, the alias |
| `FW_RoleFields` | field permissions |
| `FW_RoleDetails` | captions and role overrides |
| `FW_TableLayouts` | the saved grid layout |

A browse page could be an **instance** of `FW_Base_B` configured from its `FW_Pages` row rather
than a subclass compiled from a file. Generating one becomes inserting a row, and everything
downstream of the file disappears:

- no source file, so no compile step before the page exists
- no filing a page out of `999_GENERATED PAGES` into a band or a project
- no duplicate-class hazard when a filed page is regenerated
- no rebuild before a new page can be opened

---

## 4. The same argument, sharper, for buttons

Every awkward part of the 2026-09-04 main-menu work exists **only because a button is code**:

- patching `MenuFormInitializer.vb` by inserting text at a literal anchor
- `MainMenuMovableTileCapacity`, a constant that must be kept in step with the ribbon's geometry
- scanning two source files to count the tiles already placed
- checking the source for an existing `generated-<page>` key so a second run does not double it
- rebuilding before a generated button appears at all

`FW_DashboardLayouts` already stores a tile's **position and picture** as data, keyed by ActionKey.
Its **existence** is source that has to be patched in and compiled.

**That split is the problem.** The same button is half row and half code, and the two halves can
disagree. Make the existence a row as well and the whole apparatus above is unnecessary — and
capacity becomes a runtime fact the ribbon can answer for itself, rather than a number written down
in two places.

---

## 5. Where the argument stops

`_U` is different. `UsersY_U.vb` is 170 lines with real content: control layout, `AddComboField`
calls, `BindToFormInternal`, `TryBuildRecord`, `SaveRecord`. That earns being code. Much of it is
still template and the page-specific part is largely the field list, but it is not the same
open-and-shut case as `_B`.

Generated source can also be **hand-edited**, and the system deliberately supports that — the
generator checks whether a page has manual changes before overwriting it. A purely data-driven page
has nowhere to put custom behaviour.

So the shape to aim for is **data-driven by default, with a file generated only when a page needs
behaviour of its own** — which is what filing a page out of `999_GENERATED PAGES` already signals.

---

## 6. If only one thing is done

Section 2. The three duplicated handlers belong in the base class. It is small, safe, independent
of every other decision here, and it makes the question in section 3 much easier to answer — because
once the boilerplate is gone, what remains in a generated `_B` is the honest answer to *how much of
a page really needs to be code*.
