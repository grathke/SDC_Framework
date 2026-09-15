/*
    106_drop_registration_dead_field_permissions.sql

    Removes the FW_RoleFields rows left behind when seven columns were dropped from
    dbo.FW_Registration on 2026-09-15:

        ImportantDOB, ImportantDaysFuture, ImportantDaysPast, ExternalUnique,
        Ribbonbar_InvisibleIcons, Ribbonbar_Main, Ribbonbar_Examples

    A field permission outlives its column. Nothing refuses the row, nothing reports it, and
    Roles_U goes on offering the field to every administrator who opens it - a permission that
    can be granted and withdrawn and governs nothing at all. That is the same failure the
    FW_Perm_ convention exists to prevent, arrived at from the other direction: there the table
    was missing, here the column is.

    Six rows on WX_Framework as of 2026-09-15 - one per column, with ExternalUnique having none.
    The count is reported rather than assumed, because a different database will have its own.

    Only FW_RoleFields is touched. Checked on 2026-09-15 and clean, but worth restating so the
    next person does not have to re-derive it:

      - FW_Pages.Table_SQL       - no page SQL named any of the seven
      - FW_GeneratedPages        - no generation request selected them
      - FW_SavedQbe              - a saved search naming a dropped column would simply find
                                   nothing; none exist
      - FW_AuditTrail            - stays. An audit row records what happened, and that a value
                                   was once changed remains true after the column is gone.

    Safe to run more than once: a second run deletes nothing and reports zero.
*/

SET NOCOUNT ON;

IF OBJECT_ID('dbo.FW_RoleFields', 'U') IS NULL
BEGIN
    PRINT 'dbo.FW_RoleFields does not exist - nothing to do';
    RETURN;
END

DECLARE @dropped TABLE (FieldName VARCHAR(100) NOT NULL);
INSERT INTO @dropped (FieldName)
VALUES ('ImportantDOB'),
       ('ImportantDaysFuture'),
       ('ImportantDaysPast'),
       ('ExternalUnique'),
       ('Ribbonbar_InvisibleIcons'),
       ('Ribbonbar_Main'),
       ('Ribbonbar_Examples');

-- Refuse to run while any of them is still a column. Dropping the permission for a field that
-- still exists would silently remove access to a live field, which is the one outcome this
-- script must never produce.
IF EXISTS (SELECT 1
             FROM sys.columns c
             JOIN @dropped d ON d.FieldName = c.name
            WHERE c.object_id = OBJECT_ID('dbo.FW_Registration'))
BEGIN
    PRINT 'REFUSED: one or more of these columns still exists on dbo.FW_Registration.';
    PRINT 'Drop the columns first, or remove the survivor from the list above.';

    SELECT c.name AS StillPresent
      FROM sys.columns c
      JOIN @dropped d ON d.FieldName = c.name
     WHERE c.object_id = OBJECT_ID('dbo.FW_Registration');

    RETURN;
END

-- FileLink is keyed Table.Field, which is the shape every other FW_RoleFields row uses.
DECLARE @links TABLE (FileLink VARCHAR(200) NOT NULL);
INSERT INTO @links (FileLink)
SELECT 'FW_Registration.' + FieldName FROM @dropped;

SELECT 'BEFORE' AS Stage, rf.FileLink, COUNT(*) AS Rows
  FROM dbo.FW_RoleFields rf
  JOIN @links l ON l.FileLink = rf.FileLink
 GROUP BY rf.FileLink;

DELETE rf
  FROM dbo.FW_RoleFields rf
  JOIN @links l ON l.FileLink = rf.FileLink;

PRINT 'Deleted ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' dead FW_RoleFields row(s).';

-- Should return nothing.
SELECT 'AFTER' AS Stage, rf.FileLink
  FROM dbo.FW_RoleFields rf
  JOIN @links l ON l.FileLink = rf.FileLink;
