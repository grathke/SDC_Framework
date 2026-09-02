/*
    051_primary_key_naming_stored_sql.sql
    ----------------------------------------------------------------------------------------------
    Finishes 050: the stored SQL rows it did not reach.

    Why a second script
    -------------------
    050's updates were guarded by LIKE '%E.[ID] AS PK%', and in T-SQL a LIKE pattern reads [ID] as
    a character class - one character, either I or D - not as a bracketed identifier. The guard
    therefore matched nothing and reported "0 rows affected" rather than failing, which is the worst
    way for a migration to be wrong. Escaped as [[]ID] it matches, and REPLACE itself was never the
    problem: it takes no pattern.

    Six rows carry a browse query that names the old key and is read back into a page at runtime:

        FW_RoleTables          EntityX_B, EntityY_B, EntityA_B   E.[ID] AS PK
        FW_PageGeneration_B_U  EntityX_B, EntityY_B, EntityA_B   E.[ID] AS PK
        FW_RoleTables          FW_Registration_B                 SELECT ID as PK

    Rollback at the bottom.
*/

UPDATE dbo.FW_RoleTables
SET Table_SQL = REPLACE(Table_SQL, 'E.[ID]', 'E.[EntityID]')
WHERE Table_SQL LIKE '%FW_Entity%' AND Table_SQL LIKE '%E.[[]ID]%';
GO

UPDATE dbo.FW_PageGeneration_B_U
SET BrowseSql = REPLACE(BrowseSql, 'E.[ID]', 'E.[EntityID]')
WHERE BrowseSql LIKE '%FW_Entity%' AND BrowseSql LIKE '%E.[[]ID]%';
GO

/*
    Pages generated before the base table took an alias select the key unqualified, and name it
    twice - once in the SELECT and once in ORDER BY. Replacing the bracketed name alone is exact:
    "[ID]" does not occur inside "[GenderID]" or "[RegistrationID]", which end in ID but do not
    contain the opening bracket.

    Every statement here is guarded and uses REPLACE, so running this script again changes nothing.
*/
UPDATE dbo.FW_RoleTables
SET Table_SQL = REPLACE(Table_SQL, '[ID]', '[EntityID]')
WHERE Table_SQL LIKE '%FW[_]Entity%' AND Table_SQL LIKE '%[[]ID]%';
GO

UPDATE dbo.FW_PageGeneration_B_U
SET BrowseSql = REPLACE(BrowseSql, '[ID]', '[EntityID]')
WHERE BrowseSql LIKE '%FW[_]Entity%' AND BrowseSql LIKE '%[[]ID]%';
GO

/*
    The hand-written registration browse, which predates the aliasing convention and selects the
    key unqualified.
*/
UPDATE dbo.FW_RoleTables
SET Table_SQL = REPLACE(Table_SQL, 'SELECT ID as PK', 'SELECT RegistrationID as PK')
WHERE WindowOrPage = 'FW_Registration_B' AND Table_SQL LIKE '%SELECT ID as PK%';
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK
    ----------------------------------------------------------------------------------------------

UPDATE dbo.FW_RoleTables         SET Table_SQL = REPLACE(Table_SQL, 'E.[EntityID]', 'E.[ID]') WHERE Table_SQL LIKE '%E.[[]EntityID]%';
UPDATE dbo.FW_PageGeneration_B_U SET BrowseSql = REPLACE(BrowseSql, 'E.[EntityID]', 'E.[ID]') WHERE BrowseSql LIKE '%E.[[]EntityID]%';
UPDATE dbo.FW_RoleTables         SET Table_SQL = REPLACE(Table_SQL, 'SELECT RegistrationID as PK', 'SELECT ID as PK') WHERE WindowOrPage = 'FW_Registration_B';
GO
*/
