# Import test files

Files for exercising the employee import. Not used by the running application — they exist so the
import can be tested against something with known faults in it, rather than against whatever
happens to be lying around.

`employees-test.csv` and `employees-test.json` hold the same fourteen people. **Eleven should
import and three should come back**, which is the point: a file where everything works proves
almost nothing. `ImportRulesTests` reads both and fails if either stops holding fourteen rows.

| Row | What it tests |
|---|---|
| Amelia Hartley | A quoted address containing a comma — `"14 Mill Lane, Apt 3"`. Splitting on commas shifts every later column one place left, silently |
| Marcus Obi, Tom Becker | Ordinary rows, no user name supplied, so one is generated |
| Priya Raman | Supplies its own user name, which should be used rather than generated |
| **(no first name)** Whitfield | **Returned** — `FirstName` is `NOT NULL` with no default |
| Dana **(no last name)** | **Returned** — same for `LastName` |
| Owen Fletcher | Claims `grathke@sdcdev.net`, a user name that already exists — in another registration, because user names are unique across the whole system. **Imported as `grathke2@sdcdev.net`**: a taken name is numbered, not refused, and the number goes before the @ |
| Grace / Gareth Nolan | Both claim `gnolan`. Grace gets it, **Gareth is imported as `gnolan2`** — the clash is inside the file, so the database could not have caught it |
| Ines Delacroix-Moreau | Accented characters, an apostrophe and a hyphenated surname. The generated user name has to survive all three — `idelacroixmoreau` |
| Bob O'Hare | An apostrophe in the surname — `bohare`, not `bo'hare` |
| Sam Nguyen | **A line break inside a quoted field.** A naive parser turns this one row into two, and the second one looks like a row with no name |
| Ruth Alvarez | **Returned** — `not a date` in the hire date |
| Karl Vance | No email and no phone. Both optional, so it should import. Its note contains a comma and is quoted — until 2026-09-24 it was not, the row had thirteen fields, and the reader test is what caught it |

No row supplies a password, so every imported person gets a six-digit PIN, and the results file is
the only place those PINs appear.

Two people share a surname on purpose. **Duplicates are decided on user name, never on names** — two
people called Nolan are two people, and flagging them would be a warning that is usually wrong,
which teaches everybody to click past the one time it is right.

The files deliberately carry no photo, audit column or row version. Those are never offered as
mapping targets: some are computed, some are stamped by the save path, and one is an `image`
column a CSV cell cannot hold - nor the photo path beside it. Nor are `SuperAdmin`, `TOTPKey` or `Use2FA` on the login — a file
is not how anybody should be given super-administrator rights.

## `employees-full.csv` and `employees-full.json`

Twelve people and **31 columns**, reaching nearly every field an import can write — employee and
login both. **Seven should import and five should come back.** Import them into **DEVELOPMENT TEAM**:
the Gender IDs (1 Male, 4 Female) are that registration's, and Saraland's are different numbers.

The JSON is the source; the CSV was generated from it, so the two cannot drift. The JSON adds what
a CSV cannot hold: numbers and booleans as JSON types, a `null`, an `exportedBy` field outside the
list of records, and a nested `emergencyContact` that flattens to `emergencyContact.name` and
`emergencyContact.phone` - columns offered for mapping that nothing should be mapped to.

Nothing is mapped for you - every pair is picked by hand on Map Fields. The headings are
deliberately not the column names, which is the realistic case. The mapping that imports this file:

| Heading | Map to |
|---|---|
| Given Name, Surname, Login | First Name, Last Name, User Name |
| E-mail Address, Address Line 1, Town, State, Postal Code | Email, Address 1, City, State, Zip - **on Employee, and again on Login** (a column can be used twice) |
| Mobile, Home Phone, Work Phone, Ext | Cell Phone, Home Phone, Work Phone, Extension |
| DOB, Start Date, Termination Date, Job Title | Birth Date, Hire Date, Termination Date, Title |
| Gender ID, Time Zone ID, Active, Inspector | Gender ID, Time Zone ID, Is Active, Inspector |
| Do Not Send Email, Do Not Send Phone, Receives Health Alerts, Password | the fields of those names |
| Photo Path | nothing - photos are not importable, and this column is left unmapped |
| Windows Login, Type User, Display Dashboard On Startup | on **Login**: Windows User, Type User, Display Dashboard On Start Up |

That is some thirty pairs - the reason templates exist. Map it once, Save, and Load it for the JSON
file: its keys are the same headings written in camelCase (`givenName`, `emailAddress`), so a
template saved from the CSV will not match them, and the page lists every heading it could not find.
A template per file shape is the intended use.

| Row | What it tests |
|---|---|
| Hannah Whitcombe | Every field filled, a comma inside a quoted address |
| Diego Paredes | Supplies a login and a password, so no PIN; US-style dates `3/22/1991` |
| Mei Tanaka | Dates written out - `July 9, 1985`; Pacific (Time Zone ID 5) where the registration is Eastern; Contractor |
| Kwame Asante-Boateng | **Active = No** and a termination date. With Active mapped, the login is switched off as well |
| Siobhán Ó Briain | Accents and a space in the surname - user name `sobriain`; Country Ireland |
| Luca Ferrari | An email address as the login |
| Priscilla Oyelaran | `"Cilla"` in quotes inside a quoted note |
| Nathan Greer | **Returned** - date of birth in 2031 (the table's CHECK constraint, reported before the write) |
| Olivia Brandt | **Returned** - Receives Health Alerts is `maybe` |
| Rafael Mendes | **Returned** - Gender ID is `M`. Lookups by their text are parked, not built |
| Yusuf Kaya | **Returned** - email with no @ |
| Ingrid Solberg | **Returned** - State `Alabama` fits the employee's 25 characters and not the login's 2. Unmap Login State, or fix the cell, and it imports |

**To prove the import is all-or-nothing**, change Hannah's Gender ID to `99` in a copy. The check
passes - it does not look up foreign keys - and the database refuses the row at the write, so the
whole import rolls back and names her row.

## `employees-clean.csv`

`employees-test.csv` with its three faults fixed - Whitfield has a first name (Wendy), Dana a last name
(Moore), Ruth a real hire date - so **all fourteen import**. For trying a real import end to end.
`ImportRulesTests.CleanFile_EveryRowPassesTheCheckRules` fails if a row stops passing.

It still exercises everything that imports rather than fails: the quoted comma, the line break in an
address, the accented and apostrophed names, Owen's taken user name (`grathke2@sdcdev.net`) and the
two Nolans sharing `gnolan` (the second becomes `gnolan2`). Importing it twice makes a second set of
people, numbered again - which is what import batches, once built, will let you undo.
