/*
    068_rename_registration_columns.sql
    ==============================================================================================
    Renames two columns on dbo.FW_Registration:

        RegTypeId  ->  RegistrationTypeID
        Use2FA     ->  TwoFactorAuthentication

    ----------------------------------------------------------------------------------------------
    WHY THE COLUMN AND NOT THE CONTROL
    ----------------------------------------------------------------------------------------------
    The framework maps a control to a column by name - ComboBox_<FieldName>, CheckBox_<FieldName> -
    so FW_Registration_U's controls and these columns had to be made to agree. They did not: the
    controls were called ComboBox_RegistrationType and CheckBox_TwoFactorAuthentication, so no
    FW_RoleFields row could ever reach them and no field permission or caption override applied.

    The first fix, earlier on 2026-09-03, renamed the controls to match the columns. That worked and
    was the wrong way round: in both pairs the control carried the better name and the column the
    worse one, so satisfying the convention made two good names bad.

    RegTypeId is abbreviated and spells Id in lower case where the rest of the database uses ID.
    There is an FW_RegistrationType table, and CLAUDE.md's convention - the table name past FW_,
    singular, ID uppercase - gives RegistrationTypeID exactly.

    Use2FA begins with a verb, which reads as an instruction rather than a setting. Every other
    boolean on the table is named for what it is.

    ----------------------------------------------------------------------------------------------
    LEFT ALONE
    ----------------------------------------------------------------------------------------------
    AllowUpdateMyProfile and AllowUpdateMyProfileEmail. Verbose, but accurate and unambiguous. Their
    controls were renamed to match and that is the right way round for those two.

    ----------------------------------------------------------------------------------------------
    ELSEWHERE
    ----------------------------------------------------------------------------------------------
    FW_RoleFields holds a row per field, keyed by FieldName with FileLink as Table.Field. Both are
    rewritten below, or the rows would go on naming columns that no longer exist - which is the
    exact fault this migration exists to fix, reintroduced one table along.

    No foreign key declares RegTypeId against FW_RegistrationType, so nothing is dropped or
    recreated. That missing key is worth declaring one day; it is not this change.

    Safe to run more than once.
*/

SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

------------------------------------------------------------------------------------------------
-- 1. The columns.
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.FW_Registration', 'RegTypeId') IS NOT NULL
   AND COL_LENGTH('dbo.FW_Registration', 'RegistrationTypeID') IS NULL
BEGIN
    EXEC sp_rename 'dbo.FW_Registration.RegTypeId', 'RegistrationTypeID', 'COLUMN';
    PRINT 'RegTypeId -> RegistrationTypeID';
END

IF COL_LENGTH('dbo.FW_Registration', 'Use2FA') IS NOT NULL
   AND COL_LENGTH('dbo.FW_Registration', 'TwoFactorAuthentication') IS NULL
BEGIN
    EXEC sp_rename 'dbo.FW_Registration.Use2FA', 'TwoFactorAuthentication', 'COLUMN';
    PRINT 'Use2FA -> TwoFactorAuthentication';
END

------------------------------------------------------------------------------------------------
-- 2. The field-permission rows that name them.
------------------------------------------------------------------------------------------------
UPDATE dbo.FW_RoleFields
SET    FieldName = 'RegistrationTypeID',
       FileLink  = 'FW_Registration.RegistrationTypeID'
WHERE  FileLink = 'FW_Registration.RegTypeId';

PRINT CONCAT('FW_RoleFields rows updated for RegistrationTypeID: ', @@ROWCOUNT);

UPDATE dbo.FW_RoleFields
SET    FieldName = 'TwoFactorAuthentication',
       FileLink  = 'FW_Registration.TwoFactorAuthentication'
WHERE  FileLink = 'FW_Registration.Use2FA';

PRINT CONCAT('FW_RoleFields rows updated for TwoFactorAuthentication: ', @@ROWCOUNT);

COMMIT TRANSACTION;

PRINT '';
PRINT '=== Any FW_Registration field row naming a column that does not exist (empty is the goal) ===';
SELECT FileLink
FROM   dbo.FW_RoleFields
WHERE  FileLink LIKE 'FW[_]Registration.%'
  AND  COL_LENGTH('dbo.FW_Registration', SUBSTRING(FileLink, CHARINDEX('.', FileLink) + 1, 200)) IS NULL;

PRINT '';
PRINT '=== The renamed columns ===';
SELECT COLUMN_NAME, DATA_TYPE
FROM   INFORMATION_SCHEMA.COLUMNS
WHERE  TABLE_NAME = 'FW_Registration'
  AND  COLUMN_NAME IN ('RegistrationTypeID', 'TwoFactorAuthentication', 'AllowUpdateMyProfile', 'AllowUpdateMyProfileEmail')
ORDER  BY COLUMN_NAME;
