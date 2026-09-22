/*
    155_qbe_field_layout.sql
    ----------------------------------------------------------------------------------------------
    Lets the QBE search panel keep its own field arrangement, separately from the browse grid.

    Until now QBE was derived from the visible grid columns, so hiding a column to save grid width
    also stopped the field being searchable. They are two questions - what a page shows, and what
    it can be searched on - and the second one now has a document of its own.

    Why this table rather than a new one
    -----------------------------------
    FW_TableLayouts already stores an arrangement scoped by registration, user, page, table and
    type, and already holds a shared row shape: a Default layout carries no UserID and is read by
    everybody. A QBE arrangement is the same shape with a different LayoutType, so it needs no new
    table, no new data-access method and no new cleanup path when a page is removed - the existing
    FW_TableLayouts entry in the page-removal list already covers it.

    Why it is per registration and not per user
    ------------------------------------------
    The grid layout saves itself silently when a page closes, because it belongs to the user who
    arranged it. A shared arrangement cannot work that way: whoever closed the page last would have
    rearranged the search panel for everyone. So this row is written only by an explicit Save, and
    only an App Admin sees the button that saves it.

    UserID is left NULL, which is what UpsertTableLayout writes for a shared row, and every lookup
    matches on ISNULL(UserID, 0) = 0.

    The index
    ---------
    The three existing unique indexes are each filtered to their own LayoutType, so none of them
    covers this one. Without its own index a second row could be inserted for the same page and the
    reader would take whichever was updated most recently - which would look like a save that
    sometimes did not stick.
*/

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.FW_TableLayouts', 'U') IS NULL
BEGIN
    RAISERROR('dbo.FW_TableLayouts does not exist. Run sql/006_table_layouts.sql first.', 16, 1);
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_FW_TableLayouts_LayoutType'
      AND parent_object_id = OBJECT_ID('dbo.FW_TableLayouts')
)
BEGIN
    ALTER TABLE dbo.FW_TableLayouts
        DROP CONSTRAINT CK_FW_TableLayouts_LayoutType;

    PRINT N'Dropped CK_FW_TableLayouts_LayoutType.';
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_FW_TableLayouts_LayoutType'
      AND parent_object_id = OBJECT_ID('dbo.FW_TableLayouts')
)
BEGIN
    ALTER TABLE dbo.FW_TableLayouts
        ADD CONSTRAINT CK_FW_TableLayouts_LayoutType
            CHECK (LayoutType IN ('Default', 'UserNamed', 'LastUsed', 'QbeDefault'));

    PRINT N'Recreated CK_FW_TableLayouts_LayoutType including QbeDefault.';
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_FW_TableLayouts_QbeDefault'
      AND object_id = OBJECT_ID('dbo.FW_TableLayouts')
)
BEGIN
    CREATE UNIQUE INDEX UX_FW_TableLayouts_QbeDefault
        ON dbo.FW_TableLayouts(RegistrationID, PageName, TableName, LayoutType)
        WHERE LayoutType = 'QbeDefault' AND IsActive = 1;

    PRINT N'Created UX_FW_TableLayouts_QbeDefault.';
END;
GO

SELECT
    LayoutType,
    COUNT(*) AS Rows
FROM dbo.FW_TableLayouts
GROUP BY LayoutType
ORDER BY LayoutType;
GO
