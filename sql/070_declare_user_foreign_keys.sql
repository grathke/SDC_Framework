/*
    070_declare_user_foreign_keys.sql
    ==============================================================================================
    Declares the two foreign keys FW_Users has always implied but never stated:

        FW_Users.RegistrationID  ->  FW_Registration.RegistrationID
        FW_Users.GenderID        ->  FW_Gender.GenderID

    FW_Users had no foreign keys at all before this.

    Declaring them is not decoration. The page generator reads declared relationships to decide
    whether a column can be offered as a lookup - an undeclared one leaves the Lookup tick disabled
    and the browse grid's Displays cell a read-only grey box. That was the whole subject of sql/049
    and sql/056, and TEST_CASES GEN-22 records it.

    ----------------------------------------------------------------------------------------------
    ONE ORPHAN, CLEARED FIRST
    ----------------------------------------------------------------------------------------------
    UserId 2 carried GenderID = 2, and no gender 2 exists - the table holds 1, 3, 4 and 8.

    That is the same bad value sql/048 cleared out of FW_ENTITY when it wrote

        UPDATE dbo.FW_ENTITY SET GenderID = NULL WHERE GenderID IN (0, -1)

    and dealt with gender 2 alongside. FW_Users was not part of that pass, so its copy of the
    problem survived and only surfaced now, when a key was finally declared against the column.

    Set to NULL rather than guessed at. NULL is the honest value: the row records no gender, which
    is true. Assigning one would be inventing personal data to satisfy a constraint.

    RegistrationID needed no cleaning - every value already matches a registration.

    ----------------------------------------------------------------------------------------------
    WITH CHECK, DELIBERATELY
    ----------------------------------------------------------------------------------------------
    Both keys are created WITH CHECK so existing rows are validated now rather than trusted. An
    untrusted key is worse than none: it satisfies a search for "is there a relationship" while
    guaranteeing nothing about the data. sql/049 makes the same point.

    ----------------------------------------------------------------------------------------------
    KNOWN AND NOT ADDRESSED
    ----------------------------------------------------------------------------------------------
    FW_Gender rows are registration scoped - 1 and 4 belong to registration 1, 3 and 8 to
    registration 2 - but a single-column key cannot express "and the same registration". A user in
    registration 1 can still point at registration 2's Male. Enforcing that needs a composite key on
    (GenderID, RegistrationID), which needs a matching unique constraint on FW_Gender. Worth doing;
    not smuggled in here.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- 1. Clear gender values that point at nothing.
------------------------------------------------------------------------------------------------
UPDATE dbo.FW_Users
SET    GenderID = NULL
WHERE  GenderID IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.FW_Gender g WHERE g.GenderID = dbo.FW_Users.GenderID);

PRINT CONCAT('Orphan GenderID values cleared: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 2. Refuse to declare a key the data cannot satisfy, rather than creating it untrusted.
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM dbo.FW_Users u
           WHERE u.RegistrationID IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM dbo.FW_Registration r WHERE r.RegistrationID = u.RegistrationID))
BEGIN
    PRINT '*** STOPPED - a RegistrationID on FW_Users matches no registration. ***';
    SELECT UserId, RegistrationID FROM dbo.FW_Users u
    WHERE  u.RegistrationID IS NOT NULL
      AND  NOT EXISTS (SELECT 1 FROM dbo.FW_Registration r WHERE r.RegistrationID = u.RegistrationID);
    ROLLBACK TRANSACTION;
    RETURN;
END

------------------------------------------------------------------------------------------------
-- 3. The keys.
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Users_RegistrationID_FW_Registration')
BEGIN
    ALTER TABLE dbo.FW_Users WITH CHECK
        ADD CONSTRAINT FK_FW_Users_RegistrationID_FW_Registration
        FOREIGN KEY (RegistrationID) REFERENCES dbo.FW_Registration (RegistrationID);
    PRINT 'Declared FK_FW_Users_RegistrationID_FW_Registration';
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_FW_Users_GenderID_FW_Gender')
BEGIN
    ALTER TABLE dbo.FW_Users WITH CHECK
        ADD CONSTRAINT FK_FW_Users_GenderID_FW_Gender
        FOREIGN KEY (GenderID) REFERENCES dbo.FW_Gender (GenderID);
    PRINT 'Declared FK_FW_Users_GenderID_FW_Gender';
END

COMMIT TRANSACTION;

PRINT '';
PRINT '=== Foreign keys on FW_Users, and whether they are trusted ===';
SELECT fk.name                                        AS ConstraintName,
       COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName,
       OBJECT_NAME(fk.referenced_object_id)           AS ReferencesTable,
       CASE WHEN fk.is_not_trusted = 0 THEN 'trusted' ELSE 'NOT TRUSTED' END AS Trust
FROM   sys.foreign_keys AS fk
       JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
WHERE  fk.parent_object_id = OBJECT_ID('dbo.FW_Users')
ORDER  BY fk.name;

PRINT '';
PRINT '=== The row that was cleared ===';
SELECT UserId, FirstLast, GenderID FROM dbo.FW_Users WHERE UserId = 2;
