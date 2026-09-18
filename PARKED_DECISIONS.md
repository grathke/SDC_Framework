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

---

## Settled, with parts still open

### Switch User

Built, verified and committed 2026-09-16, and extended since: writes are refused while switched
(`SwitchedUserGuard`), and authorship follows the administrator through `SessionState.ActingUserID`.

The distinction that took the longest and must be preserved: `CreatedBy`, `UpdatedBy`,
`ModifiedBy`, `DeletedBy` and `FW_AuditTrail.UserID` are **authorship** and follow the
administrator. A `UserID` that is a **row key** stays the viewed user — table layouts, saved QBE,
page zooms, UI hints, message recipients, `FW_Messages.FromUserID`, `FW_HD_Issues.ReporterUserID`.

Still open: the search does not match first or last names, so "alan" matches every address at one
domain and nothing else; and what should happen when several accounts match.

### Query count reduction

110 round trips were measured on 2026-09-17 for logging in and opening a few pages. The page SQL
column list is now read once per browse open, and several caches exist because of it. The menu
rebuild after a dashboard closes is the next seven.

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
