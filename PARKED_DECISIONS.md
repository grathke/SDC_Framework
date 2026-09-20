# Parked Decisions

What has been decided but not built, and what is deliberately waiting. Written down because it
otherwise lives only in the memory of whoever was in the conversation.

**Nothing here is a task list.** Every item is parked on purpose, most of them waiting on the new
schema. Do not start any of it without asking â€” several were argued at length and stopped for
reasons that are not obvious from the code.

`CLAUDE.md` holds the rules. This holds the state.

---

## Waiting on the new schema

### The main menu's buttons come from a table

*Stated 2026-09-18. Not started.*

The framework always runs. A user logs in, what they are permitted decides which modules they get,
and those become the buttons. Today the ribbon is written in code â€” `MenuFormInitializer` calls
`UpsertActionTile` once per tile and permissions hide some â€” which is why the page generator has a
hook that inserts a tile for a newly generated page.

**The menu is data, the pages stay classes.** A row names a page; the framework resolves that name
to the class at run time. No hand-maintained registry â€” that was rejected â€” and no reflection
registry to keep in step, because a page class is named exactly what the row says.

Why classes rather than rows: the reuse already lives in `FW_Base_B` and `FW_Base_U`, so a page
class is thin and the generator writes it anyway. A class is checked by the compiler, gives
behaviour somewhere to live the moment one page needs it â€” `FW_SwitchUser_B` needed five overrides â€”
and appears in stack traces and IntelliSense. A row cannot hold behaviour.

**Its weakness, and the answer.** Resolving by name means a typo in a menu row is not caught until
somebody clicks. Walk the table at startup, or in the preflight script, and report any row naming a
page that does not exist. A dead button becomes a report rather than a surprise.

**What the initializer keeps:** only what an application alone can say â€” its surface name, its home
graphic, which tiles are anchored.

### Which control opens in the left-hand region

*Parked 2026-09-10.*

The rule already agreed: every region always has an occupant. So the startup question is not a set
of yes/no flags but *which control opens there*, falling back to the default when the role is not
permitted it.

Three registration fields circle this and two do nothing today: `AllowMessaging` and
`DisplayDashboardOnStartUp` are consumed by nothing, and a third â€” "show messages on load" â€” does
not exist. Settle them together rather than adding a third flag that means the same as the first two.
*Refined 2026-09-19.* Name the **tile**, not the region. A tile's click handler already checks the
permission, builds the control, puts it in the right region and sets the chrome â€” so a startup
setting holding an `ActionKey` invokes what already exists and the question "which region gets
what" disappears. It also collapses the three flags above into one value: `DisplayDashboardOnStartUp`
becomes *startup tile = Dashboard*, `AllowMessaging` and the absent "show messages on load" become
*startup tile = Messages*. One setting that cannot contradict itself, and it survives the move to
menu buttons in a table, where the `ActionKey` is already the key.

Three things still to decide before building it: not every tile is a valid target (some open a
drop-down rather than filling a region); what happens when the tile is not permitted, which the rule
above already answers as *fall back to the default*; and whether the registration sets it, the user
overrides it, or both.

### Who may change a maintenance page's layout

*Settled 2026-09-19. Not built.*

Three scopes, deliberately different, and the distinction is what a thing **is** rather than where
it is stored:

| | Scope | Who may change it |
|---|---|---|
| Browse column layouts | per user, per page | any user, their own |
| `_U` tab order | one per page | developer only |
| `_U` control positions | one per page | developer only |

A browse grid is somebody arranging their own working view. A maintenance page's field order and
positions are the page's *design*, which everybody then receives â€” so there is one set, and no
per-user variant. That closes the open question in `PAGE_LAYOUT_RUNTIME_SPEC.md` section 8: there is
no conflict between a developer's layout and a user's, because there is no user's.

**"Developer" is a session, not only a role.** App Admin is a role a customer's administrator can
hold over Thinfinity. The test is the role **and** a desktop session â€” the same line page generation
draws when it refuses to open in a browser. Applied to the tab order manager on 2026-09-19; the
positions would use the same gate.

**Reordering is the developer's, applying is everybody's.** Every page reads the saved order on open,
for every user, in every session. Only the writing is gated.

### Where repositioning happens

*Settled 2026-09-19. Not built.*

In the preview opened from page generation, and nowhere else â€” not on a normally opened page, even
for an App Admin.

- Page generation refuses to open in a browser session, so a preview can never be dragged over
  Thinfinity. On a normal page that needs a second rule, and a rule whose only job is to block a
  path is how the path gets unblocked by accident.
- The preview is already the arrangement surface: it computes the placement, re-arranges the real
  page to match it, and saves the tab order.
- A normal page is carrying a record. Dragging fields while somebody has half-typed an address mixes
  designing the page with using it, and the undo for each is different.
- Design happens once and use happens constantly. Putting a drag mode on every page open, for
  something done rarely, trades a permanent risk for an occasional convenience.


**Saving replaces the page's whole set**, in one transaction, as `SaveTabOrderSettings` already does:
delete the page's rows, write the current ones. It changes rarely, nothing is gained by merging row
by row, and a replace cannot leave a stale row behind. A saved row naming a control that is not on
the page is ignored rather than reported â€” unlike the unmapped-field report, which exists because
such a control stops a page saving. Here it is an expected state: a row saved for a field that is in
the request but not yet generated simply waits for it.
### How a field is repositioned

*Settled and built 2026-09-19.*

**Built 2026-09-19, and it needed nothing new.** The tool already existed: the maintenance grid in
the field picker has `Move Up`, `Move Down` and a `Column` cell per field, so the order and the
split were always editable â€” you change them there and press Preview to see the result.

What was missing was only that crossing the boundary did nothing. `Move Up` moved one place in the
whole included list, so when the neighbour belonged to the other column the button appeared dead,
which on a two-column page is most presses. Now the two fields **trade columns as well as places**:
within a column that is a no-op, across the boundary it is the crossing, and because the boundary is
an index that never moves, membership stays contiguous by construction.

Nothing goes on the preview. Buttons there would have added directness, not capability, and a third
surface that reorders is how the three drift apart. Everything below stands as the reasoning for
why, and is kept because the conclusion was reached twice from opposite ends.

**Two buttons, not dragging.** Select a field; `Up` and `Down` move it one row and displace what is
there.

Dragging was the obvious idea and is the wrong one here. The delivery rules prefer discrete events
over continuous pointer sampling â€” a button click always arrives, mouse movement is coalesced and
degrades by acting late rather than failing. Buttons also match what already exists: `Up` and `Down`
in the tab order manager, `Move Up` and `Move Down` in the field picker. A third surface that
reordered by mouse would make the same operation work two ways depending on where you were standing.

Snapping disappears with the drag. One press is one row slot, so the rule that a row is the unit â€”
`IsRowLayoutControl` and `CollapseHiddenFieldRows` decide what a row *is* by comparing `Top` â€” is
enforced by construction rather than by a snap that has to be correct. Nothing can land between rows
because nothing is ever placed by pixel.

**No long-move affordance**, deliberately. Neither the tab order manager nor the field picker has
one, and inventing a keyboard jump here would be a third way of doing what those two already do. If
long moves hurt, they hurt in all three places and get added to all three.

**Down past the end of a column wraps to the top of the next one**, and `Up` at the top of a column
goes back to the bottom of the previous. Moving a field across columns is not a separate command,
it is what happens when you keep pressing â€” which is also the order the page tabs in, down one
column and then the next.

The consequence is that **column membership is always contiguous**: everything before the wrap point
is in one column, everything after in the next. That is accepted. The model today allows an
interleaved arrangement â€” `Column2Fields` is any subset â€” but nothing produces one and nothing wants
one.

**Written as "the next column", not "the other column".** There may one day be a column 3, and the
interaction costs nothing to generalise. The layout does not generalise for free: `PlaceMaintenancePage`
holds `TwoColumns`, a single `ColumnTwoLeft` and a `leftFields`/`rightFields` pair,
`MaintenanceLayout.PageWidth` chooses between a one-column and a two-column width, and the request
stores `Column2Fields` rather than a column number per field. None of it is hard â€” placement is one
function now, so columns become a loop over an index â€” but it is a change, not a flag. And three
columns is roughly 1400 wide against a 1260 minimum window, so it is also a decision about the
window.

**A field's row is its ordinal within its column, not a coordinate.** So `Up` on the page moves it
past the previous field *in its own column*, which may be several steps in the underlying ordered
list when the fields between belong elsewhere. That is not what the field picker's `Move Up` does â€”
that moves one place in the whole included list, and can appear to do nothing when the neighbour is
in another column. The page moves by rows because rows are what you are looking at; the two are not
the same operation and should not be written as though they are.



### A conversation entry table for the Help Desk

*Agreed 2026-09-12.*

Today a whole thread lives in `FW_HD_Issues.ConversationText` as one formatted blob, with
`ConversationEntryCount` counting entries. `FW_HD_IssueAttachments` already carries a
`ConversationEntryID` column, always written NULL, because there is no table for it to point at.

What it buys beyond attachments per entry: authorship, timestamp and ordering become columns rather
than text formatted once and never queryable. A blob cannot answer "who replied last", "how long
until first response", or "show me entries by support only" without parsing prose.

Shape follows the naming conventions: `FW_HD_IssueConversation`, keyed `ConversationID`.

### Contractors are not employees

*Stated 2026-09-16.*

Every person who signs in is an employee today, and that will change: contractors will have a user
login and no employee row. There is no contractor table yet.

Three things built on 2026-09-16 assume every login has an employee row, and each will quietly miss
a contractor:

- **Switch User search** (`DataAccess.FindUsersForSwitch`) matches the user name, the login email
  and `FW_Employees.Email`. A contractor's email will not be searched.
- **The "found, but is inactive" message** says *employee*, and active state is read from both
  `FW_Users.IsActive` and `FW_Employees.IsActive`.
- **An employee's active flag carries to their login** through `DataAccess.SyncLoginActive`.

---

## Designed, specified, and deliberately not started

### Runtime page layout and the form designer

A `_U` page's layout moved out of generated code and into data the page reads at run time, plus the
drag-and-drop designer that would edit it. Fully specified in `PAGE_LAYOUT_RUNTIME_SPEC.md` and
`FORM_DESIGNER_SPEC.md`, parked 2026-09-15. **Not to be started unasked** â€” it is a large change to
how every maintenance page is built.

### A phone gets its own menu, login and pages

`ClientDevice.Probe` is built and nothing acts on it. The intent: the login screen always opens and
calls the main menu, unless the browser says the client is a phone, in which case a phone login and
a phone main menu open the same pages. That shared "open a page by name" is the same piece the
data-driven menu needs, which is why the two are one decision rather than two.

### Publishing to the wd-html5 server

Planned for the week of 2026-09-14 and not done. The database credentials are the thing to settle
first. See also the rule in `CLAUDE.md` about server work: the server itself is not configured
through Claude.

### The login database timeout

Login waits around ten seconds when the database server is down. The fix is to probe the SQL port
before connecting â€” TCP, not ICMP, since a machine can answer a ping with SQL Server stopped.

---

## Settled, with parts still open

### Switch User

Built, verified and committed 2026-09-16, and extended since: writes are refused while switched
(`SwitchedUserGuard`), and authorship follows the administrator through `SessionState.ActingUserID`.

The distinction that took the longest and must be preserved: `CreatedBy`, `UpdatedBy`,
`ModifiedBy`, `DeletedBy` and `FW_AuditTrail.UserID` are **authorship** and follow the
administrator. A `UserID` that is a **row key** stays the viewed user â€” table layouts, saved QBE,
page zooms, UI hints, message recipients, `FW_Messages.FromUserID`, `FW_HD_Issues.ReporterUserID`.

Still open: the search does not match first or last names, so "alan" matches every address at one
domain and nothing else; and what should happen when several accounts match.

### Query count reduction

110 round trips were measured on 2026-09-17 for logging in and opening a few pages. The page SQL
column list is now read once per browse open, and several caches exist because of it. The menu
rebuild after a dashboard closes is the next seven.

### Field captions: one rule, and a column left behind

Settled 2026-09-19. This started as "the two page types resolve captions differently" and ended as
a consolidation, so what is recorded here is the decision, not an open item.

**The rule now.** An override is purely an override:

    OverrideCaption present  ->  that is the caption
    OverrideCaption absent   ->  derived from the field by DisplayNameFormatter

`FW_RoleFields.FriendlyFieldName` used to sit between those two and no longer does. It was seeded
by `FormatFieldName`, which has no acronym handling, so what it mostly contributed was
`BTN_Create_Caption` as "B T N Create Caption" and `CA_CanChange` as "C A Can Change" - waiting on
columns no page happened to select. Measured the same day: 765 rows carried one, and exactly **one**
field in the database had a browse header that actually depended on it, `FW_Employees.FirstLast` as
"Full Name". That was promoted to a real `OverrideCaption` and propagated across the tenant's roles
by itself, which is what the column is for.

**QBE takes its caption from the grid column it filters**, rather than asking the caption map a
second time. The map had won over any header a page had set for itself, so a page that captioned a
column got a QBE row disagreeing with the column above it. No page did that yet. The Start Empty
path still resolves through the map, because it runs before a grid exists and has no header to read.

**The two routes remain, deliberately.** A browse page resolves by field name through
`GetPageInitMetadata`; a maintenance page resolves by control name through `ApplyControlUpdates`,
where the caption is a few lines inside the pass that also applies permissions, masking and required
borders. They are keyed differently because they are answering differently-shaped questions, and
merging the plumbing is not worth the blast radius. What was merged is the rule they both apply.

**Propagation is per tenant, not per role and not across tenants.** Saving a caption in Roles writes
it to every role row for that field in the same `RegistrationID`; a second registration keeps its
own wording. `usp_FW_ApplyAccessDiagnosticChanges` seeds the same way when it creates a role's rows.

**What is actually parked: removing `FriendlyFieldName` altogether.** It is inert - nothing names
anything from it - but it is still written by the inserts and by that stored procedure, and still
shown in Roles_U. Removing the writes means editing the procedure and the Roles_U grid, and dropping
the column is destructive. None of it buys anything a user sees, and the column is currently the
only record of hand-edited names from before the promotion above. Left alone on purpose.

Note for whoever greps: `ToFriendlyCaption` is **not** `FriendlyFieldName`. It is the deriver, and
it is load-bearing.
---

## Rejected, or dropped

- **Zoom flicker** â€” dropped 2026-09-18. Do not raise it again.
- **A sandbox folder for generated pages** â€” rejected 2026-09-18 as too complicated. Each owner has
  its own `999_GENERATED` waiting room instead.
- **A hand-maintained page registry** â€” rejected 2026-09-18 in favour of resolving a page class by
  the name the menu row holds.

---

## Backups

Commits protect against bad edits, not against losing the machine. As of 2026-09-18 there is **no
git remote**, so everything lives here only. `restore-points/` and `project-backup/` are excluded
from the repository â€” some restore points contain an old `run-local.ps1` with a real password, and
they stay out of history for that reason.
