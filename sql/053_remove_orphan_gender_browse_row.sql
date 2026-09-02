/*
    053_remove_orphan_gender_browse_row.sql
    ----------------------------------------------------------------------------------------------
    Deletes the orphan FW_RoleTables row for FW_Gender_B.

    Why
    ---
    The row registers a browse page that does not exist - there is no FW_Gender_B.vb - and its
    query is half written: "SELECT ID as PK, GenderDescription", naming columns but no table. It
    fails outright if anything runs it. Nothing does, because the page it belongs to was never
    built. It surfaced during the primary key rename, when every registered query was executed to
    prove it still worked; this one never worked.

    Deleted rather than flagged, on request, and the reason matters
    --------------------------------------------------------------
    FW_RoleTables carries DeletedFlag and the shared policy is to use it. Not here.
    UpsertRoleTableRecord matches on WindowOrPage alone:

        IF EXISTS (SELECT 1 FROM dbo.FW_RoleTables WHERE WindowOrPage = @WindowOrPage) UPDATE ...

    with no DeletedFlag in the test and none in the SET. A soft-deleted row would therefore be
    found and overwritten by the generator the day FW_Gender_B is generated, keeping DeletedFlag = 1
    - a page registered and invisible at the same time, which is a worse state than either. The row
    is going because it will be recreated correctly by generation, so leaving a tombstone in its
    place defeats the purpose.

    Rollback
    --------
    At the bottom: the row exactly as it was, minus its identity value, which the table assigns.
*/

DELETE FROM dbo.FW_RoleTables
WHERE WindowOrPage = 'FW_Gender_B'
  AND Table_SQL = 'SELECT ID as PK, GenderDescription';
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - restores the row as it stood: ID 46, no RegistrationID, not deleted.
    ----------------------------------------------------------------------------------------------

INSERT INTO dbo.FW_RoleTables (RegistrationID, DB_Table, Table_Alias, WindowOrPage, Table_SQL, ExposedToUser, DeletedFlag)
VALUES (NULL, 'FW_GENDER', 'Gender', 'FW_Gender_B', 'SELECT ID as PK, GenderDescription', 1, 0);
GO
*/
