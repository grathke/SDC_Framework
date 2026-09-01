/*
    046_role_tables_background.sql
    ----------------------------------------------------------------------------------------------
    Adds FW_RoleTables.Background so a browse page remembers its background colour.

    Why
    ---
    Every _B page looks alike, which makes it easy to act on the wrong one. A faint page colour per
    page is the cheapest way to tell them apart, and FW_RoleTables is where a page's other display
    settings already live - it is keyed on WindowOrPage and is read when the page loads its SQL, so
    the colour arrives with everything else rather than costing another round trip.

    Stored as an INT holding an ARGB value, which is what Color.ToArgb returns and
    Color.FromArgb takes. NULL means no colour chosen, and the page stays white - the same as
    today, so applying this changes nothing until a colour is picked.

    Scope note
    ----------
    FW_RoleTables has a RegistrationID column but every current row leaves it NULL, so a colour set
    here applies to that page for everyone. Making it per registration means populating
    RegistrationID on these rows, which is a separate decision.

    Rollback
    --------
    At the bottom of this file.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_RoleTables')
                 AND name = 'Background')
BEGIN
    ALTER TABLE dbo.FW_RoleTables ADD Background INT NULL;
END
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration, and revert the matching code change.
    ----------------------------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_RoleTables')
             AND name = 'Background')
BEGIN
    ALTER TABLE dbo.FW_RoleTables DROP COLUMN Background;
END
GO
*/
