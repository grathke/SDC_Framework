# Parked Decisions

What has been decided but not built, and what is deliberately waiting. Written down because it
otherwise lives only in the memory of whoever was in the conversation.

**Nothing here is a task list.** Every item is parked on purpose, most of them waiting on the new
schema. Do not start any of it without asking — several were argued at length and stopped for
reasons that are not obvious from the code.

`CLAUDE.md` holds the rules. This holds the state.

---

## Waiting on the new schema

### The main menu's buttons come from a table

*Stated 2026-09-18. Not started.*

The framework always runs. A user logs in, what they are permitted decides which modules they get,
and those become the buttons. Today the ribbon is written in code — `MenuFormInitializer` calls
`UpsertActionTile` once per tile and permissions hide some — which is why the page generator has a
hook that inserts a tile for a newly generated page.

**The menu is data, the pages stay classes.** A row names a page; the framework resolves that name
to the class at run time. No hand-maintained registry — that was rejected — and no reflection
registry to keep in step, because a page class is named exactly what the row says.

Why classes rather than rows: the reuse already lives in `FW_Base_B` and `FW_Base_U`, so a page
class is thin and the generator writes it anyway. A class is checked by the compiler, gives
behaviour somewhere to live the moment one page needs it — `FW_SwitchUser_B` needed five overrides —
and appears in stack traces and IntelliSense. A row cannot hold behaviour.

**Its weakness, and the answer.** Resolving by name means a typo in a menu row is not caught until
somebody clicks. Walk the table at startup, or in the preflight script, and report any row naming a
page that does not exist. A dead button becomes a report rather than a surprise.

**What the initializer keeps:** only what an application alone can say — its surface name, its home
graphic, which tiles are anchored.

### Which control opens in the left-hand region

*Parked 2026-09-10.*

The rule already agreed: every region always has an occupant. So the startup question is not a set
of yes/no flags but *which control opens there*, falling back to the default when the role is not
permitted it.

Three registration fields circle this and two do nothing today: `AllowMessaging` and
`DisplayDashboardOnStartUp` are consumed by nothing, and a third — "show messages on load" — does
not exist. Settle them together rather than adding a third flag that means the same as the first two.
*Refined 2026-09-19.* Name the **tile**, not the region. A tile's click handler already checks the
permission, builds the control, puts it in the right region and sets the chrome — so a startup
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
positions are the page's *design*, which everybody then receives — so there is one set, and no
per-user variant. That closes the open question in `PAGE_LAYOUT_RUNTIME_SPEC.md` section 8: there is
no conflict between a developer's layout and a user's, because there is no user's.

**"Developer" is a session, not only a role.** App Admin is a role a customer's administrator can
hold over Thinfinity. The test is the role **and** a desktop session — the same line page generation
draws when it refuses to open in a browser. Applied to the tab order manager on 2026-09-19; the
positions would use the same gate.

**Reordering is the developer's, applying is everybody's.** Every page reads the saved order on open,
for every user, in every session. Only the writing is gated.

### Where repositioning happens

*Settled 2026-09-19. Not built.*

In the preview opened from page generation, and nowhere else — not on a normally opened page, even
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
the page is ignored rather than reported — unlike the unmapped-field report, which exists because
such a control stops a page saving. Here it is an expected state: a row saved for a field that is in
the request but not yet generated simply waits for it.
### How a field is repositioned

*Settled and built 2026-09-19.*

**Built 2026-09-19, and it needed nothing new.** The tool already existed: the maintenance grid in
the field picker has `Move Up`, `Move Down` and a `Column` cell per field, so the order and the
split were always editable — you change them there and press Preview to see the result.

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
over continuous pointer sampling — a button click always arrives, mouse movement is coalesced and
degrades by acting late rather than failing. Buttons also match what already exists: `Up` and `Down`
in the tab order manager, `Move Up` and `Move Down` in the field picker. A third surface that
reordered by mouse would make the same operation work two ways depending on where you were standing.

Snapping disappears with the drag. One press is one row slot, so the rule that a row is the unit —
`IsRowLayoutControl` and `CollapseHiddenFieldRows` decide what a row *is* by comparing `Top` — is
enforced by construction rather than by a snap that has to be correct. Nothing can land between rows
because nothing is ever placed by pixel.

**No long-move affordance**, deliberately. Neither the tab order manager nor the field picker has
one, and inventing a keyboard jump here would be a third way of doing what those two already do. If
long moves hurt, they hurt in all three places and get added to all three.

**Down past the end of a column wraps to the top of the next one**, and `Up` at the top of a column
goes back to the bottom of the previous. Moving a field across columns is not a separate command,
it is what happens when you keep pressing — which is also the order the page tabs in, down one
column and then the next.

The consequence is that **column membership is always contiguous**: everything before the wrap point
is in one column, everything after in the next. That is accepted. The model today allows an
interleaved arrangement — `Column2Fields` is any subset — but nothing produces one and nothing wants
one.

**Written as "the next column", not "the other column".** There may one day be a column 3, and the
interaction costs nothing to generalise. The layout does not generalise for free: `PlaceMaintenancePage`
holds `TwoColumns`, a single `ColumnTwoLeft` and a `leftFields`/`rightFields` pair,
`MaintenanceLayout.PageWidth` chooses between a one-column and a two-column width, and the request
stores `Column2Fields` rather than a column number per field. None of it is hard — placement is one
function now, so columns become a loop over an index — but it is a change, not a flag. And three
columns is roughly 1400 wide against a 1260 minimum window, so it is also a decision about the
window.

**A field's row is its ordinal within its column, not a coordinate.** So `Up` on the page moves it
past the previous field *in its own column*, which may be several steps in the underlying ordered
list when the fields between belong elsewhere. That is not what the field picker's `Move Up` does —
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

### Live record updates across sessions, the way FileMaker does it

*Parked 2026-09-21. Asked for as a want, not a requirement, and answered in enough detail that the
next conversation starts from here rather than from the beginning.*

**The want.** Two people have the same browse page open. One changes a record. If that record is
in the other person's grid, their row updates with the new values, without them doing anything.
The explicit condition attached to it: **the page must not appear to slow down.**

**It is possible. Push notifications are not the way.** SQL Server's `SqlDependency`, over Service
Broker, is the obvious-looking answer and is the wrong tool here. Its query restrictions are
severe - no `*`, no aggregates, no `TOP`, no subqueries or derived tables, two-part names only -
and browse-page SQL is authored by whoever made the page and stored in `FW_Pages`, so most of it
would be rejected. Rejection is silent, and the failure modes are the two worst available: fire
immediately and for ever, or never fire at all. Each registration is also single-shot and has to be
renewed after every fire.

**Poll a change token, never the data.** The cost is decided entirely by what the tick asks. A tick
that re-runs the page's own query is what would make the page feel slow; a tick that asks "has
anything I care about changed since id N?" returns nothing on almost every tick. That suggests a
narrow `FW_ChangeFeed` - change id, table, record key, when, by whom - written inside the save's
existing transaction, which the Transaction Guardrail already requires. A page remembers the last
id it saw and re-fetches only on a hit.

**What it would cost, separating what is measured from what is not:**

- an idle tick is one narrow indexed query, single-digit milliseconds - **estimated, not measured**
- a tick that finds something is the page's ordinary refresh, **measured at 16-33ms warm** on
  `FW_Employees_B` after the QBE pushdown work
- multiply by users, because over Thinfinity every session is its own process on the server: 50
  users on a 10-second tick is 5 queries a second across the whole system, which is nothing. A
  one-second tick is 50 a second and mostly waste

**The grid is the harder problem than the database.** Rebinding it would throw away the selection,
the scroll position and the sort - so a row changing under somebody would also lose their place,
which is the fault `RestoreGridViewState` and the QBE Find work both exist to avoid. The rule has
to be: merge values into the existing rows, never rebind, never move the selection, never touch a
row being edited.

**Browse pages only. Never a maintenance page.** A `_U` page holding a record has a `RowVersion`
and a save-conflict path the guardrails require. Refreshing its fields underneath the user would
silently destroy the conflict detection that exists to stop one person overwriting another's work.
FileMaker's behaviour is right for a list and wrong for an open form, and the distinction is not a
detail to be decided later.

**What is unanswered.** Whether the feed is written by the data layer or by each save; how a page
decides which changes are "its" without re-running its own query; what a deleted or newly inserted
row does to a grid somebody is reading; and whether a changed row should be marked for a moment
rather than silently swapping its values.



### Telemetry: tickets from faults, and sending it off-machine

*Parked 2026-09-19. The recording half is built; these two are not.*

`FW_ErrorLog` and `Telemetry.vb` are live, and `HEALTH_DASHBOARD_SPEC.md` covers the page that
reads them. Two further phases were designed at the same time and stopped on purpose.

**A Help Desk ticket raised from a fault fingerprint.** Attractive - you learn about a fault before
the customer rings - and dangerous in exactly one way: a crash loop on a page fifty people have open
is fifty thousand tickets, and the thing built to protect the system takes it out instead. It needs
a throttle and a deduplication rule before a line of it is written. The fingerprint already exists
and is the right key: a fault already seen is not news. Open questions: which registration owns the
ticket, since it is our ticket and not the customer's; and whether the trigger is first occurrence
or crossing a threshold.

**Sending telemetry somewhere we own.** The client side is easy. The two real questions are not
technical. An exception message can carry record values, a user name, occasionally a connection
string - so what leaves the building needs a deliberate allow-list, never `ex.ToString()`, and this
is a multi-tenant system where that means one customer's data in a payload about another's bug. And
the receiving end is server work, which is not done through Claude: stand up the endpoint, then the
client is a small piece against it.

Neither is blocked by anything. Both are waiting on a decision rather than on code.



### Runtime page layout and the form designer

A `_U` page's layout moved out of generated code and into data the page reads at run time, plus the
drag-and-drop designer that would edit it. Fully specified in `PAGE_LAYOUT_RUNTIME_SPEC.md` and
`FORM_DESIGNER_SPEC.md`, parked 2026-09-15. **Not to be started unasked** — it is a large change to
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
before connecting — TCP, not ICMP, since a machine can answer a ping with SQL Server stopped.

### Permits: searching a range of permit numbers

Raised 2026-09-25, for when a permits table exists. Glenn expects a permit number to be **free
text** - people "can enter what they like" - and asked whether QBE can find permits between a
starting and ending number.

**Not by the text.** A range over free text compares letter by letter: `P-9` sorts after `P-10`,
case and punctuation vary, and a permit typed as `2026/452` falls in nobody's range. Results would
look right and silently miss permits.

What serves the question instead:

- **Starts With** on the permit number - `P-2026-` finds every 2026 permit. Text fields have it.
- **A date range** - issued, opened or closed between two dates. Built and tested (B-44); usually the
  real question.
- **If a numeric range is genuinely wanted:** the permits table carries a plain number the system
  assigns, beside the free-text number people type, and Between runs on that.

**Numbers have no Between yet** - `FW_Base_B.GetAllowedOperators` explains why: a range is two
filters on one field, and a search path that names its SQL parameter after the field would declare
it twice. Give each filter its own numbered parameter first, then turn Between on for numbers, with
the same two-box dialog dates use. Build it with the permits table, only if the sequence number is
wanted.

### The database keeps two clocks - BUILT 2026-09-25

Found 2026-09-25, when Past Imports showed a 19:04 import as 11:04 PM: 18 date defaults were
`SYSUTCDATETIME()` and 13 were `GETDATE()`. Resolved the same day as one clock, UTC, written by the
code (`ONE_CLOCK_SPEC.md`, merged as 9c9071c).

- Writes use `SYSUTCDATETIME()`; `sql/161_one_clock_defaults.sql` is applied. Only
  `FW_Employees.HireDate` keeps `GETDATE()`, on purpose - it is a calendar date.
- Every screen shows an instant in the viewer's zone through `SessionTime`. The naming rule decides
  which columns are instants: ends in `Date` is a calendar date, ends in `Local` is wall-clock,
  anything else is an instant (`CLAUDE.md`, Naming Conventions).
- No data conversion was needed. BEELINK was already on UTC+0 with daylight saving off, so the
  `GETDATE()` rows were UTC all along, and it is now set to plain UTC.

### The Fixed button cannot say a fault was fixed in code

*Parked 2026-09-25.* `FW_ErrorLog.ResolvedSource` knows two values - `Claude` renders as "Code
change" and `User` as the person's name - but the health page's Fixed button always writes `User`,
so a fault closed by a code change reads as if somebody had decided it did not matter. Letting the
dialog ask which it was was offered and never asked for. **Reopens** when the history is being read
to learn what was actually fixed, and the answer matters.

### The architecture document: contents and a PDF

*Parked 2026-09-25.* The document has no contents section linking its headings, and no PDF. A PDF
exports at letter or A4, but a page-numbered index cannot be written into the document, because a
doc has no pages until it is rendered. **Reopens** when it is about to be sent to somebody who will
read it on paper or needs to find a section quickly.

---

## Waiting on an event, not a decision

Built, and not yet seen working on the one path that cannot be forced. Nothing to do until the event
happens; when it does, check it and record the result in `TEST_CASES.md`.

### Fault mail from a Thinfinity session

*Since 2026-09-24.* The `SDC_MAIL_*` settings are in the Windows user environment, so a process the
VirtualUI server launches should see them - unless the server passes only the environment it
started with. Proven on the desktop; unproven in a browser session. **Reopens** at the first real
fault raised in a Thinfinity session: did the mail arrive? Raising a fake one would prove the fake.

### A browse page that declines the SQL wrapper, seen in the application

*Since 2026-09-25.* The decline path is proven by a direct call (`QBE_SQL_PUSHDOWN_SPEC.md` section
12), but no current page declines, so it has never been seen from the UI. **Reopens** when a page
writes a `FW_FallbackUsageLog` row: open it, search, and confirm the results match.

---

## Settled, with parts still open

### Tab order follows the layout, with fine-tuning on top - when generated pages are next worked on

*Planned and parked 2026-09-24.* Glenn asked why moving, adding or deleting a control on a generated
page means reordering the tabs by hand. Because a page with a saved order (`FW_UpdateTabOrder`) uses
that list, keyed by control name, for ever: a control added later is already slotted in by its
position (`ApplySavedTabOrder`, `ComesAfterOnPage`), but a **moved** one keeps its old place. A page
with no saved order uses the generator's `SetManualTabOrder` list, right when generated and blind to
anything moved since. Three pages had a saved order on 2026-09-24: FW_Employees_U (24 controls),
FW_Registration_U (23), Users_AppAdmin_U (14).

The plan, two layers:

1. **Layout order, worked out on every open** from where the controls sit - down each column in
   turn, left column first, as the generator orders a two-column page today. Moves, additions and
   deletions are followed with nothing to redo.
2. **Fine-tuning saved as exceptions, not a whole list** - "Cell Phone comes straight after Home
   Phone". A rule survives its field being moved; one whose field is deleted is dropped. The
   no-tab-stop tick stays per field. The manager gains **Reset to Layout Order**. Glenn: the
   reorder panel stays, for exactly this fine-tuning.

Touches the tab order code in `FW_Base_U` (restore point and FRAMEWORK_NOTES first), a "comes after"
column on `FW_UpdateTabOrder`, and the three saved pages, whose lists become rules on first use so
nobody's order changes the day it ships. Tests: move, add, delete a field a rule points at, Reset,
and the three pages tabbing exactly as before.

### FW_Roles.ID becomes RoleID - BUILT 2026-09-25

**Built 2026-09-25 as migration 166**, when Glenn asked for it directly rather than waiting for a
Roles rework. About 50 lines changed, not 100: most of the `ID`s counted below belong to other
tables (`FW_RoleDetails`, `FW_RoleSchema`, the registration combo). The view was not
schema-bound. `validate-browse-regression.ps1` now fails on a single-line `FW_Roles` query
written against `ID`. The test list below is what still has to be walked in the running app.

*Parked 2026-09-24.* Glenn asked what it would cost; the answer was "do it when Roles is reworked
anyway", which is the rule CLAUDE.md sets for existing keys. Measured that day:

- Database: the column (one `sp_rename`; the foreign key from `FW_EmployeeRoles.RoleID` follows),
  **`vw_FW_EmployeeRoles`** - a view, so a protected area needing Glenn's explicit go-ahead - and
  `usp_FW_AccessDiagnostic`; the Roles_B row in `FW_Pages` and any saved layouts naming `ID`.
- Code: about 100 places in six files - up to 72 in `DataAccess.vb`, 18 in `Roles_U`, 5 in
  `Roles_B`, one each in `FW_MainMenu` and `HealthMail`, two in the employee import's role list.
- Risk: those queries decide who signs in with which role and what it may see. A missed one fails
  at sign-in, not at build. Restore point first; test login, role selection, both Roles pages, the
  access diagnostic, health mail and the import.
- Order: rename the column, then alter the view and procedure, in one transaction - they cannot be
  altered to `RoleID` before it exists - shipped with the new build, the app closed. If the view is
  `WITH SCHEMABINDING`, the rename is refused until it is unbound: check that first.

The gain: `e.RoleID = r.RoleID`, matching `FW_EmployeeRoles`, `FW_RoleDetails`, `FW_RoleFields` and
`FW_Session`, which already say `RoleID`.

### Employee import

Built 2026-09-24: `FW_EmployeeImport`, CSV or JSON into FW_Employees and their logins, with a
field mapping, defaults, Saved Imports (`FW_SavedImports`, each keeping a copy of its file), a row-by-row check and an
all-or-nothing write through the same save path as FW_Employees_U. Decided with Glenn:

- one role for the whole import, not per row
- a user name already taken is numbered (`gnolan2`, `grathke2@sdcdev.net`), never refused
- a blank password becomes a six-digit PIN, unique within the import; the results file is the only
  place it is ever shown. `ChangeMeX` was offered as the alternative and not chosen
- a default value fills a field whether or not anything is mapped to it

Deliberately left out, each worth doing only when a real file needs it:

- **Other kinds of file.** The dashboard tile says Import Files (Glenn, 2026-09-24) because more
  than employees is meant to come in through it. Which files, and whether the tile then opens a
  chooser or the import grows a "what are you importing" step, is still to be worked out. Today
  it opens the employee import directly.
- **Updating people who already exist.** New people only. Matching an existing employee needs a
  key the file and the table agree on, and user name is the only one - which the numbering rule
  above would then have to stop applying.
- **Lookup fields by their text.** GenderID, TimeZoneID, AssignedManagerID and the rest take the
  number, not "Female" or a manager's name. The foreign keys are declared, so the lookup table is
  known; what is not is which of its columns a file would name.
- **A role or password rule per row.**
- **Date-and-time defaults** - parked 2026-09-24 until a date-and-time field is importable (every
  one today is date-only). Wanted then: `Today 2:00 PM`, `Today 14:00`, `2026-10-01 2:00 PM`
  besides `Now` and `Today`, all read in the session's time zone and **converted to UTC**, the way
  moments are stored. Today a typed or file date-and-time is stored as written, unconverted - wrong
  by the zone offset once displayed - which is the gap to close with it.

### Switch User

Built, verified and committed 2026-09-16, and extended since: writes are refused while switched
(`SwitchedUserGuard`), and authorship follows the administrator through `SessionState.ActingUserID`.

The distinction that took the longest and must be preserved: `CreatedBy`, `UpdatedBy`,
`ModifiedBy`, `DeletedBy` and `FW_AuditTrail.UserID` are **authorship** and follow the
administrator. A `UserID` that is a **row key** stays the viewed user — table layouts, saved QBE,
page zooms, UI hints, message recipients, `FW_Messages.FromUserID`, `FW_HD_Issues.ReporterUserID`.

Still open, and parked 2026-09-25 until Switch User is next worked on:

- **A save while switched.** Settled 2026-09-17 but not built: instead of a flat refusal, ask
  whether to save the record under the administrator's own id. Yes saves it that way; No cancels.
- **Names in the search.** It does not match first or last names, so "alan" matches every address
  at one domain and nothing else. `FW_Users` already carries FirstName and LastName (sql/112).
- **Several matches.** Picking one from a list that works like QBE, reading `dbo.FW_UserPeople`,
  which already stamps each login's PersonType - settle it with contractors.

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
### Sharing the framework with the second developer

The repository went to `github.com/grathke/SDC_Framework` on 2026-09-23 — private, personal
account, both branches, 468 files. That closed the off-machine backup gap for source code, which
had been open since the project began.

**The venture is 50/50, and the end state is a shared organisation where both partners are
owners** — not one partner's personal account. Personal was chosen only because the organisation is
a joint decision and the backup gap was not. Transfer costs the same later as now: one click, in
Settings → General → Danger Zone, keeping history, issues and pull requests, redirecting the old
URL. Expected within five days of 2026-09-23.

Four things left, parked on 2026-09-23:

- **Commit `7c7088f` is unpushed**, and `master` is one behind again. Nothing breaks; a clone
  simply lands one commit short until both are pushed.
- **The other developer's GitHub username** was never needed. Adding him as a collaborator was
  dropped deliberately rather than forgotten: once the repository is in an organisation he owns
  a share of, his access comes from membership, and a per-repo entry would only need tidying away.
- **His SQL Server version is unchecked.** The database goes to him as a `.bak`, not built from
  `sql/schema/`, and a backup restores forward only. If his server is older than 2025 the restore
  fails outright, after several hundred MB have moved.
- **`FW_Users.Password`**, the legacy `varchar(50)` column, holds a plaintext value for 15 of the
  10,014 rows — alongside `PasswordHash` for the same 15, and a TOTP key for 3. `WritePasswordHash`
  writes `#####` into it on every save, so the 15 predate that path and nothing removes them. They
  are other people's passwords, and a `.bak` carries them. Not looked at; not decided.

---

## Rejected, or dropped

- **Zoom flicker** — dropped 2026-09-18. Do not raise it again.
- **A sandbox folder for generated pages** — rejected 2026-09-18 as too complicated. Each owner has
  its own `999_GENERATED` waiting room instead.
- **A hand-maintained page registry** — rejected 2026-09-18 in favour of resolving a page class by
  the name the menu row holds.

---

## Backups

Commits protect against bad edits, not against losing the machine. As of 2026-09-18 there is **no
git remote**, so everything lives here only. `restore-points/` and `project-backup/` are excluded
from the repository — some restore points contain an old `run-local.ps1` with a real password, and
they stay out of history for that reason.
