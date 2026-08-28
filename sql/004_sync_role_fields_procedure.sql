-- Create procedure to sync FW_RoleFields with database schema
-- Deletes obsolete fields and inserts new fields

IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_NAME = 'SyncRoleFieldsWithSchema' AND ROUTINE_TYPE = 'PROCEDURE')
BEGIN
    DROP PROCEDURE dbo.SyncRoleFieldsWithSchema
END
GO

CREATE PROCEDURE dbo.SyncRoleFieldsWithSchema
    @RoleID INT,
    @SchemaID INT,
    @RegistrationID INT,
    @UpdatedBy INT,
    @DeletedCount INT OUTPUT,
    @InsertedCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON
    
    DECLARE @TableName NVARCHAR(255)
    DECLARE @DB_Table NVARCHAR(255)
    
    -- Get table name from SchemaID
    SELECT @TableName = DB_Table 
    FROM dbo.FW_RoleSchema 
    WHERE ID = @SchemaID
    
    IF @TableName IS NULL
    BEGIN
        SET @DeletedCount = 0
        SET @InsertedCount = 0
        RETURN
    END
    
    SET @DB_Table = @TableName
    
    -- ===== STEP 1: DELETE obsolete fields from FW_RoleFields =====
    -- Delete records where FieldName exists in FW_RoleFields but NOT in schema
    SET @DeletedCount = 0
    
    DELETE FROM dbo.FW_RoleFields
    WHERE RoleID = @RoleID 
        AND SchemaID = @SchemaID
        AND TableName = @TableName
        AND FieldName NOT IN (
            SELECT COLUMN_NAME 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @TableName
        )
    
    SET @DeletedCount = @@ROWCOUNT
    
    -- ===== STEP 2: INSERT new fields into FW_RoleFields =====
    -- Insert schema columns that don't exist in FW_RoleFields for this role
    SET @InsertedCount = 0
    
    -- Get list of primary key columns to exclude
    DECLARE @PKColumns TABLE (ColumnName NVARCHAR(255))
    INSERT INTO @PKColumns
    SELECT COLUMN_NAME 
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
    WHERE TABLE_NAME = @TableName 
        AND CONSTRAINT_NAME LIKE 'PK%'
    
    -- Insert new fields
    INSERT INTO dbo.FW_RoleFields 
        (RegistrationID, RoleID, SchemaID, TableName, FieldName, TableField, FriendlyFieldName, 
         CA_CanChange, Can_Create, Can_Read, Can_Update, IsActive, IsRequired, IsUnique, 
         Make_Invisible, OrderBy, OverrideCaption, CreatedBy, CreatedOn)
    SELECT DISTINCT
        @RegistrationID,
        @RoleID,
        @SchemaID,
        @TableName,
        c.COLUMN_NAME,
        @TableName + '.' + c.COLUMN_NAME,
        dbo.FormatFieldName(c.COLUMN_NAME),
        NULL,  -- CA_CanChange (3-state)
        1,     -- Can_Create
        1,     -- Can_Read
        1,     -- Can_Update
        1,     -- IsActive
        0,     -- IsRequired
        0,     -- IsUnique
        0,     -- Make_Invisible
        NULL,  -- OrderBy
        NULL,  -- OverrideCaption
        @UpdatedBy,
        GETDATE()
    FROM INFORMATION_SCHEMA.COLUMNS c
    WHERE c.TABLE_NAME = @TableName
        AND c.COLUMN_NAME NOT IN ('CreatedBy', 'CreatedOn', 'UpdatedBy', 'UpdatedOn')  -- Exclude audit fields
        AND c.COLUMN_NAME NOT IN (SELECT ColumnName FROM @PKColumns)  -- Exclude primary keys
        AND NOT EXISTS (
            SELECT 1 FROM dbo.FW_RoleFields rf 
            WHERE rf.RoleID = @RoleID 
                AND rf.SchemaID = @SchemaID
                AND rf.FieldName = c.COLUMN_NAME
        )
    
    SET @InsertedCount = @@ROWCOUNT
    
END
GO

PRINT 'Procedure dbo.SyncRoleFieldsWithSchema created successfully.'
