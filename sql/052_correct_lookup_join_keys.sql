/*
    052_correct_lookup_join_keys.sql
    ----------------------------------------------------------------------------------------------
    Corrects an over-broad replace in 051.

    What went wrong
    ---------------
    051 rewrote every bracketed [ID] in a browse query that mentions FW_Entity, on the assumption
    that the only key such a query names is the entity's own. EntityX_B disproves it: the query
    joins two lookup tables, and its gender join reads

        LEFT JOIN dbo.[FW_Gender] G ON E.[GenderID] = G.[ID]

    where G.[ID] is FW_Gender's key, which this wave does not rename. It became G.[EntityID] and
    the query then failed outright with "Invalid column name 'EntityID'" - loudly, which is the one
    mercy: the page could not have shown wrong data, only no data.

    Two rows carry it, both for EntityX_B: the registered browse SQL and the generation request it
    came from.

    The lesson is in the guard, not the replace: a qualified name has to be matched with its alias,
    because the same bare key name belongs to a different table on either side of a join.

    Rollback at the bottom, though restoring a query that cannot run has little to recommend it.

    CORRECTION (see 056)
    --------------------
    The premise above is false. G.[ID] is not FW_Gender's key: that table's key is GenderID, an
    identity carrying PK_Gender, and it has never had a column named ID. 051 turned G.[ID] into
    G.[EntityID] and this script turned it back, so one invalid column name was swapped for
    another - the join could not run either way. GenderID was correct throughout.

    The wrong name originates in 049, which tried to declare the foreign key against FW_GENDER.ID
    and silently failed. Nothing carries either spelling now: the affected pages have since been
    regenerated, and every stored browse query binds. 056 declares the relationship correctly.
*/

UPDATE dbo.FW_RoleTables
SET Table_SQL = REPLACE(Table_SQL, 'G.[EntityID]', 'G.[ID]')
WHERE Table_SQL LIKE '%G.[[]EntityID]%';
GO

UPDATE dbo.FW_PageGeneration_B_U
SET BrowseSql = REPLACE(BrowseSql, 'G.[EntityID]', 'G.[ID]')
WHERE BrowseSql LIKE '%G.[[]EntityID]%';
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK
    ----------------------------------------------------------------------------------------------

UPDATE dbo.FW_RoleTables         SET Table_SQL = REPLACE(Table_SQL, 'G.[ID]', 'G.[EntityID]') WHERE Table_SQL LIKE '%G.[[]ID]%';
UPDATE dbo.FW_PageGeneration_B_U SET BrowseSql = REPLACE(BrowseSql, 'G.[ID]', 'G.[EntityID]') WHERE BrowseSql LIKE '%G.[[]ID]%';
GO
*/
