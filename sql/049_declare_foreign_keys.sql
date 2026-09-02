/*
    049_declare_foreign_keys.sql
    ----------------------------------------------------------------------------------------------
    Declares FW_ENTITY.GenderID -> FW_GENDER.ID.

    Why
    ---
    That column has always pointed at FW_GENDER; nothing said so. The page generator can read a
    declared relationship and work out a lookup for itself - the table and the key with certainty,
    leaving only the column to display as a choice - which is what lets a generated page show a
    name where it currently shows a number.

    The database had seven declared foreign keys before this and none were on the tables pages are
    generated from.

    Cleared first by 048: 494 rows held GenderID = 2, a value renumbered out of existence.

    What changes behaviour
    ----------------------
    A foreign key refuses a delete that would leave a reference dangling, so deleting a gender that
    is still in use now fails with a constraint error instead of succeeding. NULL is unaffected: a
    foreign key permits it and reads it as no reference, which is what the 500 null rows are.

    Deliberately not declared
    -------------------------
    UserID and OwnerID point at FW_Users, and the three RegistrationID columns point at
    FW_Registration. All five would pass - 048 cleared the sentinels that blocked one of them - but
    they are left for a later decision rather than added because they happen to be possible.
    Adding one is two lines, following the pattern above.

    Rollback
    --------
    At the bottom of this file. Dropping the constraint does not restore the values 048 cleared;
    run that script's rollback as well if those are wanted back.
*/

ALTER TABLE dbo.FW_ENTITY WITH CHECK
    ADD CONSTRAINT FK_FW_ENTITY_GenderID_FW_GENDER
    FOREIGN KEY (GenderID) REFERENCES dbo.FW_GENDER (ID);
GO

/*
    AssignedManagerID is the lookup no naming convention can derive - the column says "manager",
    the table says FW_Users - so declaring it is what makes that lookup discoverable at all.
    Went in cleanly because 048 had already cleared its six sentinel rows.
*/
ALTER TABLE dbo.FW_ENTITY WITH CHECK
    ADD CONSTRAINT FK_FW_ENTITY_AssignedManagerID_FW_Users
    FOREIGN KEY (AssignedManagerID) REFERENCES dbo.FW_Users (UserId);
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK
    ----------------------------------------------------------------------------------------------

ALTER TABLE dbo.FW_ENTITY DROP CONSTRAINT FK_FW_ENTITY_AssignedManagerID_FW_Users;
ALTER TABLE dbo.FW_ENTITY DROP CONSTRAINT FK_FW_ENTITY_GenderID_FW_GENDER;
GO
*/
