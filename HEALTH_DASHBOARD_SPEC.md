# Health Dashboard Spec

**Proposed 2026-09-19. Nothing built.** The telemetry it reads is built and running; this is the
page that shows it.

Read this before adding anything to `FW_ErrorLog`, before changing `Telemetry.vb`, and before
building any other page that reports across registrations — section 5 is the tenancy exception and
it is deliberate.

---

## 1. What it is

One page, App Admin only, answering "is this installation healthy right now" with a number and a
needle, backed by three tables that already exist.

`FW_Health_B` — the `_B` suffix for consistency with the other dashboard, not because it browses a
table. It inherits `Form` directly, as `FW_HD_AdminDashboard_B` does.

**Row-level read counting is out of scope. Deliberate searches are in.** The distinction was drawn
2026-09-19 and it is the whole difference between cheap and impossible.

Counting every read means instrumenting the hot path, adding a round trip to the thing being
measured, and producing a table larger than everything it reports on. Not worth it, and the number
it yields - how many `SELECT`s the framework fired - is not a question anybody asks.

Counting a **search** is different. Pressing Find in QBE is one discrete, user-initiated event, a
handful per person per day, the same order of volume as the audit trail already carries. It has a
single owner in `FW_Base_B`'s find handler, so it is one instrumentation point rather than a
scattering. And it answers a question worth asking: how much are people actually using this, and
which pages.

### Logins, which are not a usage counter

Added 2026-09-19. **Nothing records a login today** - no audit row, no telemetry. `LoginForm` holds
a `failedAttempts` field but it is in memory, drives the screen, resets when the form closes and
persists nowhere. There is no lockout.

Two holes follow. `FW_AuditTrail` can prove who changed a row but not when they signed in, so the
session that produced a change is unrecorded. And a run of failed attempts - somebody guessing at
one account - leaves no trace at all. "Nobody can log in" is the worst outage this system can have
and it is currently invisible.

**Corrected 2026-09-20.** This section used to offer "a whole tenant locked out the morning after a
password change" as the second example. There is no such thing: authentication is per user, and
`ComputePasswordHashForUser` keys its HMACSHA512 with **that user's own UserId**, so a tenant's
users share no password, no secret and not even a salt. Nothing can break them all at once.

What genuinely takes everybody out is worth naming instead, because it is what the alerting has to
catch:

- **`dbo.vw_FW_CurrentUser`.** Every sign-in in the installation joins through that view. Change it,
  break it, or drop a column beneath it and every login fails everywhere. It is a fair part of why
  views are a Protected Area in `CLAUDE.md`.
- **The database unreachable.** Everyone fails, though with a distinct reason rather than looking
  like a bad password.

Both are installation-wide rather than tenant-wide, which is the shape the alerting should follow.
Neither reaches the failed-password counter at all, because in both cases no password was ever
checked - so a mass outage never floods a rule that counts wrong passwords.

Successes and failures want different storage, so they are two things rather than one:

| | Shape | Why |
|---|---|---|
| Successful | Hourly bucket, as the counters below | Volume and trend, not a row per sign-in |
| Failed | One row per attempt | The pattern is the point - one user repeatedly, or everybody at once |

A failed row holds the attempted user name, the time, the registration where one resolves, and the
**reason kept separate**: unknown user, wrong password, inactive account, database unreachable.
That separation is what turns "I cannot log in" from a guessing game into a diagnosis.

**It must not leak back to the login screen.** Telling a user "unknown user" rather than "wrong
password" tells an attacker which names are real. The distinction belongs in the table, which only
an App Admin reads, and never in the message on screen. The current screen already separates
credential failures from other failures and should stay as it is.

A lockout after N failures is the obvious neighbour of this and is deliberately **not** part of it.
Recording is safe; locking people out is a policy decision with a support cost, and it should be
argued on its own.

### The three usage counters

Three kinds, all of them a person deciding to look at something:

| Kind | Raised when | Owner |
|---|---|---|
| `Search` | Find pressed on the QBE row | `FW_Base_B` find handler |
| `BrowseOpen` | A browse page loads its grid | `FW_Base_B` load path |
| `RecordOpen` | Modify or Read opens one record | `FW_Base_B` command handlers |

All three live in `FW_Base_B`, so this is a few call sites in one file rather than instrumentation
spread across the application - and every generated page gets it by inheriting, without knowing.

Counted as **one row per hour, per registration, per kind** - not a row per event. At that
granularity the table stays small enough to keep for ever, and it holds no record values, no record
keys and no search terms, which keeps it clear of the privacy question a per-user activity log would
raise. The question it answers is "how much is this being used, and which pages", not "what did
this person look at".

## 2. The layout

```
+------------------------------------------------------------------------+
|  SYSTEM HEALTH                              Last 7 days v    Refresh    |
+------------------------------------------------------------------------+
|                                                                        |
|            +-----------------------+      +----------------------+     |
|            |     60     80         |      | SAVES        1,024   |     |
|            |  40            100    |      | succeeded    99.2%   |     |
|            | 20       \            |      +----------------------+     |
|            |  0        \____       |      +----------------------+     |
|            |          94.6         |      | FAULTS          3    |     |
|            |        HEALTHY        |      | unacknowledged  1    |     |
|            +-----------------------+      +----------------------+     |
|                                            +----------------------+     |
|                                            | FALLBACKS      37    |     |
|                                            | per 100 ops   4.6    |     |
|                                            +----------------------+     |
+------------------------------------------------------------------------+
|  ACTIVITY                                                              |
|  Create ######## 64   Update ############## 126   Delete ### 24        |
|  Generate ##### 40    Sync #### 35          Restore ## 11              |
+------------------------------------------------------------------------+
|  NEEDS ATTENTION                                                       |
|  !  SqlException - FW_Employees_U.LoadRecord      x12   2h ago   [Ack] |
|  !  NullReference - Roles_B.RestoreGridViewState   x3   1d ago   [Ack] |
|  .  SQL_Fallback_UpsertException                   x9   3d ago         |
+------------------------------------------------------------------------+
```

Fixed size, no drag-resize, like every other page. The period selector drives every panel at once -
one window of time for the whole page, so the needle and the counts below it cannot disagree.

## 3. What the needle measures

Three inputs, all already recorded. Score is `100 - penalties`, floored at zero.

| Input | Source | Max penalty |
|---|---|---|
| Save success rate | `FW_AuditTrail.SaveSucceeded`, over rows where `Phase = 'AfterSave'` | 50 |
| Fault pressure | `FW_ErrorLog` - unacknowledged distinct faults, plus occurrence count, decayed by age | 30 |
| Fallback rate | `FW_FallbackUsageLog` per 100 audited operations | 20 |

Bands: **90-100 green, 75-89 amber, below 75 red.** The needle colour and the word under the number
both follow the band, so it reads at a glance and also reads correctly to somebody who cannot
distinguish the colours.

**Why these three.** Saves are the thing the application exists to do, and `SaveSucceeded` is a real
recorded outcome rather than an inference. Faults are what nobody has looked at yet. Fallbacks are
the silent-degradation class - the application still working, but not the way it was meant to, which
is the state that otherwise goes unnoticed for months.

**Why decay.** A fault from three weeks ago that has not recurred is not current ill health. Without
decay the needle only ever falls, which makes it decoration. Acknowledging a fault removes it from
the score outright - that is what `FW_ErrorLog.Acknowledged`, `AcknowledgedBy` and `AcknowledgedOn`
are for, and the `[Ack]` button in Needs Attention is the only write this page performs.

**Weights are a starting point, not a finding.** They were chosen before anybody had watched the
number move. Expect to change them once there is a month of real data, and change them in one place.

## 4. The gauge

A custom control with its own `OnPaint`. 180-degree arc, tick marks every 20, three coloured bands,
a needle, and the score centred beneath in a large face.

- No third-party dependency. GDI+ only.
- No animation loop. It draws on refresh and on resize, and nothing else. Over Thinfinity every
  frame is a round trip to the browser, so a sweeping needle would cost a great deal to say the
  same thing a static one says.
- No hover behaviour, for the same reason. Anything the gauge has to communicate is drawn on it.
- Scales with `PageZoom` like any other control, because it is measured from its own bounds rather
  than from constants.

## 5. Tenancy: a deliberate exception

**This page does not take the registration predicate.** It reports across every registration, and
that is the point - health is our view of the installation, not a customer's view of their slice.

It is therefore an exception to the rule that every page is scoped, and it is only safe because:

- It is App Admin only. That role exists in our own registration and nowhere else, so the capability
  cannot be granted to a customer by accident.
- It shows counts and fault signatures, never record values. A fault's `Message` may name a table
  and a column; it must never be joined to the rows themselves on this page.

If a customer-facing version of this is ever wanted, it is a different page with the predicate in
place - not a flag on this one.

## 6. What it needs before it can be built

1. A `FW_RoleSchema` row so Roles can offer it, a `FW_RoleDetails` row granting Read to the
   Application Admin role, and an entry in `ICON_CATALOG.md` for the tile that opens it.
2. A decision on where the tile lives. The Application dashboard is the obvious home.
3. Enough real data to be worth looking at. The table was created on 2026-09-19 with no rows, so the
   fault panel will be empty for a while - which is the correct reading, not a bug.

## 7. Deliberately not in version one

- Row-level read counting, as section 1. Search counts are in, and need a small table of hourly
  buckets plus one call in `FW_Base_B`'s find handler - neither exists yet, so the Activity panel
  shows writes only until they do.
- Per-registration breakdown. It invites using this page to look at a customer, which section 5 says
  it is not for.
- Trend lines and history. The needle is "now". A history panel is a second page over the same data
  and can wait until there is history worth plotting.
- Alerting. Help Desk tickets raised from a fault fingerprint were designed alongside the telemetry
  and parked - see `PARKED_DECISIONS.md`. Nothing on this page should raise one until the throttling
  question there is answered.
