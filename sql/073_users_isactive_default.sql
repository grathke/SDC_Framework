/*
    073_users_isactive_default.sql
    ==============================================================================================
    Gives dbo.FW_Users.IsActive a default of 1, and settles the rows that have no value.

    ----------------------------------------------------------------------------------------------
    WHY
    ----------------------------------------------------------------------------------------------
    IsActive had no default while DeletedFlag beside it had one. Any insert that did not name the
    column left NULL, and 072 made login treat NULL as inactive - the safe direction to fail, but it
    meant a user created through a generated page could never sign in and nothing said why.

    Two rows were already in that state: users 5 and 6, both created through UserX_U on 2026-09-04,
    both with a correct password hash and no way in.

    Defaulted to 1 rather than 0. A record being created is a record somebody intends to use, and
    the alternative - every new account silently dormant until an administrator notices - is the
    behaviour that just caused this. Anyone wanting a dormant account can untick Is Active, which is
    a deliberate act rather than an omission.

    ----------------------------------------------------------------------------------------------
    EXISTING NULLS
    ----------------------------------------------------------------------------------------------
    Set to 1. They are accounts created on purpose whose IsActive was never populated, not accounts
    somebody chose to deactivate - a deliberate deactivation writes 0. Users 1 and 3 hold a real 0
    and are left exactly as they are.

    A default does not touch rows that already exist, so the UPDATE is not optional.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

PRINT '=== BEFORE ===';
SELECT ISNULL(CAST(IsActive AS varchar(5)), 'NULL') AS IsActive, COUNT(*) AS Rows
FROM   dbo.FW_Users
GROUP  BY IsActive;

------------------------------------------------------------------------------------------------
-- 1. The rows that never got a value.
------------------------------------------------------------------------------------------------
UPDATE dbo.FW_Users SET IsActive = 1 WHERE IsActive IS NULL;
PRINT CONCAT('Rows set active: ', @@ROWCOUNT);

------------------------------------------------------------------------------------------------
-- 2. The default, so no future insert can repeat it.
------------------------------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1
    FROM   sys.default_constraints dc
           JOIN sys.columns c ON c.object_id = dc.parent_object_id
                             AND c.column_id = dc.parent_column_id
    WHERE  dc.parent_object_id = OBJECT_ID('dbo.FW_Users')
      AND  c.name = 'IsActive'
)
BEGIN
    ALTER TABLE dbo.FW_Users
        ADD CONSTRAINT DF_FW_Users_IsActive DEFAULT (1) FOR IsActive;
    PRINT 'Added DF_FW_Users_IsActive';
END
ELSE
BEGIN
    PRINT 'IsActive already has a default.';
END

COMMIT TRANSACTION;

PRINT '';
PRINT '=== AFTER ===';
SELECT UserId, Email, IsActive,
       CASE WHEN PasswordHash IS NULL OR PasswordHash = '' THEN 'no hash' ELSE 'hashed' END AS HashState
FROM   dbo.FW_Users
ORDER  BY UserId;
