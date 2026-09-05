# Page Generation — A Simpler Shape

**Section 2 was built on 2026-09-05. Section 3 was decided against the same day. Section 4 is still a proposal.** Written 2026-09-04 at
the end of a session that built main-menu placement into the generator.

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

## 2. The cheap step, and the one to do first — BUILT 2026-09-05

The three handlers now live in `FW_Base_B`, reached through two hooks a page overrides:

| Hook | Default | Effect when supplied |
|---|---|---|
| `CreateMaintenancePage(recordId)` | `Nothing` | Create and Update open the returned `FW_Base_U` |
| `UsesStandardSoftDelete()` | `False` | Delete soft-deletes the selected row |

**Both default to off.** That was the part worth getting right: put working delete logic in the base
ungated and `FW_Registration_B`, `FW_AuditTrail_B` and `FW_HD_Admin_B` all silently gain a live
delete button they never had. Off by default, every page that opts into nothing behaves exactly as
it did.

The primary key is read from the database rather than passed in, so a renamed key cannot leave a
page deleting against a column that no longer exists.

`SavedRecordId` moved to `FW_Base_U` — the base has to ask *some* maintenance page which record it
saved, and cannot see a property on a class it does not know. `FW_Base_U` is already a `Form`, so
the hook returns one thing that can be both shown and read. An interface would have needed a cast
back to `Form` that compiles and fails at runtime.

**Result: a generated `_B` went from 66 lines to 20.** `UserX_B.vb` and `UsersY_B.vb` had been
byte-identical apart from the class name.

One consequence: `FW_GeneratedPages` stores a hash of the generated source to detect hand edits.
Changing the template invalidated it for the two existing requests, so regenerating `UserX` or
`UsersY` reports them as manually edited until re-baselined.

---

## 3. The step that follows from it — DECIDED AGAINST, 2026-09-05

**Keep generating the file.** The argument below is sound and still loses, for a reason that was
not written down when it was made:

> A `_B` may need to be changed. Another table, parent and child, some other kind of search.

A browse page is only boilerplate until the first one is not. Parent/child, a different search, SQL
spanning tables — each needs somewhere to put behaviour, and a row in `FW_Pages` has nowhere. The
file is that place, and it has to exist *before* anyone discovers they need it: a page that has to
be converted from a row back into a class the moment it grows a requirement is worse than one that
was always a class.

Section 2 had already taken the win this section was chasing. The duplication is gone, and every
override point on `FW_Base_B` is still reachable. Twenty-one lines is a cheap price for *this page
can become anything later*.

What follows is kept because the observation is still true, and because the same reasoning does
**not** apply to buttons in section 4 — a button has no behaviour of its own to protect.

---

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

Section 2, and it is done. What it leaves behind is the honest answer to section 3's question —
*how much of a page really needs to be code*:

```vb
Public Class UsersY_B
    Inherits FW_Base_B

    Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
        MyBase.New(user, profile, "FW_USERS")
    End Sub

    Protected Overrides Function CreateMaintenancePage(recordId As Integer) As FW_Base_U
        Return New UsersY_U(recordId, CurrentUserContext, CurrentAccessProfile)
    End Function

    Protected Overrides Function UsesStandardSoftDelete() As Boolean
        Return True
    End Function
End Class
```

Three facts: the class name, the table, and the partner page.

That was nearly the argument for doing away with the file altogether, and section 3 records why it
was not: a page this small is *still the place* a parent/child relationship or a different search
would go, and it has to exist before anyone finds they need it.

So section 2 is where this ends for `_B` pages. Section 4, on buttons, is untouched by that
reasoning and remains open.
