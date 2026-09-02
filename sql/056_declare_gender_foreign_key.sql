/*
    056_declare_gender_foreign_key.sql
    ----------------------------------------------------------------------------------------------
    Declares FW_Entity.GenderID -> FW_Gender.GenderID, which 049 intended and never achieved.

    Why 049 did not do it
    ---------------------
    049 wrote REFERENCES dbo.FW_GENDER (ID). FW_Gender has no column named ID and never has: its
    key is GenderID, an identity column carrying PK_Gender. The 050 rename wave did not touch this
    table, so nothing moved out from under 049 - the name was wrong when it was written.

    That statement failed. The GO after it let the next batch run, which is why 049 left exactly
    one of its two foreign keys behind: AssignedManagerID -> FW_Users.UserId is present and
    trusted, and the gender one was simply absent, with no error surviving to say so.

    The same wrong name spread. 051 rewrote every bracketed [ID] in a browse query mentioning
    FW_Entity and turned the gender join's G.[ID] into G.[EntityID]; 052 changed it back to G.[ID]
    on the stated premise that "G.[ID] is FW_Gender's key". It is not. Both spellings were invalid;
    GenderID was correct throughout. Nothing carries either spelling now - the affected pages have
    since been regenerated - so this script has no stored SQL left to repair.

    Why it matters beyond tidiness
    ------------------------------
    The page generator reads declared foreign keys and nothing else. With no relationship on
    GenderID, PageGeneration_U offers no lookup for it on the _B grid: BuildLookupJoins skips any
    row with an empty LookupTarget, so no join and no alias is written, and reopening the request
    finds nothing to read back. The _U grid appeared to disagree only because it replays the saved
    LookupFields text, which still records the non-existent FW_GENDER.ID.

    Safe to do because
    ------------------
    No orphans: every non-null FW_Entity.GenderID matches a row in FW_Gender. 048 cleared the 494
    rows holding the renumbered-away value 2. WITH CHECK therefore validates rather than merely
    trusting, and the constraint lands trusted.

    What changes behaviour
    ----------------------
    Deleting a gender still referenced by an entity now fails with a constraint error instead of
    succeeding and leaving a dangling reference. NULL is unaffected - a foreign key reads it as no
    reference, which is what the null rows are.

    Guarded, so running this twice changes nothing.

    Rollback at the bottom.
*/

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys AS fk
    JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
    JOIN sys.columns AS pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
    WHERE fk.parent_object_id = OBJECT_ID('dbo.FW_Entity') AND pc.name = 'GenderID'
)
    ALTER TABLE dbo.FW_Entity WITH CHECK
        ADD CONSTRAINT FK_FW_Entity_GenderID_FW_Gender
        FOREIGN KEY (GenderID) REFERENCES dbo.FW_Gender (GenderID);
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK
    ----------------------------------------------------------------------------------------------

ALTER TABLE dbo.FW_Entity DROP CONSTRAINT FK_FW_Entity_GenderID_FW_Gender;
GO
*/
