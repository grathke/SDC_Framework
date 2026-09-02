/*
    048_entity_orphan_lookups_to_null.sql
    ----------------------------------------------------------------------------------------------
    Clears FW_ENTITY lookup values that point at records which do not exist.

    Why
    ---
    These columns are foreign keys in everything but name, and declaring them as such is blocked
    while they hold values with nothing behind them. Each is nullable, so NULL is available and
    means exactly what these rows are: no reference recorded.

        GenderID = 2            494 rows. FW_GENDER holds 1, 3, 4 and 8 - Male and Female for
                                registrations 1 and 2 - so 2 was renumbered or removed at some
                                point. All 494 are in registration 1. They display nothing today
                                and would join to nothing, so NULL is what they already are in
                                effect; it is simply now written down.

        AssignedManagerID       6 rows: 0 in three, -1 in three.
        OwnerID                 2 rows: -1 in both.

                                Sentinels rather than references. Zero and minus one match no user
                                and never did - this is the "empty combo writes 0" habit, recorded
                                as data.

    Nothing is deleted and no column changes type. Rows keep every other value they hold.

    Rollback
    --------
    At the bottom of this file. The original values are recoverable because each column held one
    of a very small set of wrong values, listed above - the rollback restores those, which is not
    the same as restoring per-row history. There was none to lose: the values were all identical.
*/

UPDATE dbo.FW_ENTITY SET GenderID = NULL WHERE GenderID = 2;
GO

UPDATE dbo.FW_ENTITY SET AssignedManagerID = NULL WHERE AssignedManagerID IN (0, -1);
GO

UPDATE dbo.FW_ENTITY SET OwnerID = NULL WHERE OwnerID = -1;
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - restores the values this script cleared. It cannot distinguish rows that were
    already NULL before it ran from rows it set to NULL, so run it only if nothing has edited
    FW_ENTITY since.
    ----------------------------------------------------------------------------------------------

UPDATE dbo.FW_ENTITY SET GenderID = 2 WHERE GenderID IS NULL AND RegistrationID = 1;
GO
*/
