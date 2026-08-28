CREATE OR ALTER PROCEDURE dbo.usp_FW_ApplyAccessDiagnosticChanges
    @UserID INT,
    @RegistrationID INT,
    @RoleID INT,
    @SchemaID INT,
    @DB_Table NVARCHAR(128),
    @Can_Create BIT,
    @Can_Read BIT,
    @Can_Update BIT,
    @Can_Delete BIT,
    @Can_UseQBE BIT,
    @Can_ViewAllRecords BIT,
    @Can_ViewOnlyMyRecords BIT,
    @AssignRoleIfMissing BIT,
    @ActorUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Operation NVARCHAR(10);
    DECLARE @RowsAffected INT = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @UserID <= 0 OR @RegistrationID <= 0 OR @RoleID <= 0 OR @SchemaID <= 0 OR NULLIF(LTRIM(RTRIM(@DB_Table)), '') IS NULL
            THROW 52200, 'Required access-maintenance values are missing.', 1;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.FW_UserRoles
            WHERE UserID = @UserID
              AND RegistrationID = @RegistrationID
              AND RoleID = @RoleID
              AND ISNULL(IsActive, 1) = 1
        )
        BEGIN
            IF @AssignRoleIfMissing = 1
            BEGIN
                INSERT INTO dbo.FW_UserRoles
                (RegistrationID, UserID, RoleID, DisplayOrder, IsActive, CreatedBy, CreatedOn)
                VALUES
                (@RegistrationID, @UserID, @RoleID, 0, 1, @ActorUserID, GETDATE());
            END
            ELSE
                THROW 52201, 'The selected user does not have the selected active role for this registration.', 1;
        END

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.FW_RoleSchema
            WHERE ID = @SchemaID
              AND DB_Table = @DB_Table
              AND ISNULL(IsActive, 1) = 1
        )
            THROW 52202, 'The selected schema/table does not exist or is inactive.', 1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.FW_RoleDetails
            WHERE RoleID = @RoleID
              AND RegistrationID = @RegistrationID
              AND DB_Table = @DB_Table
        )
        BEGIN
            UPDATE dbo.FW_RoleDetails
            SET Can_Create = @Can_Create,
                Can_Read = @Can_Read,
                Can_Update = @Can_Update,
                Can_Delete = @Can_Delete,
                Can_UseQBE = @Can_UseQBE,
                Can_ViewAllRecords = @Can_ViewAllRecords,
                Can_ViewOnlyMyRecords = @Can_ViewOnlyMyRecords,
                UpdatedBy = @ActorUserID,
                UpdatedOn = GETDATE()
            WHERE RoleID = @RoleID
              AND RegistrationID = @RegistrationID
              AND DB_Table = @DB_Table;
                        SET @Operation = N'UPDATE';
                        SET @RowsAffected = @@ROWCOUNT;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.FW_RoleDetails
            (
                RoleID, RegistrationID, SchemaID, DB_Table, Table_Alias, OverrideCaption,
                Can_Create, Can_Read, Can_Update, Can_Delete, Can_UseQBE,
                Can_ViewAllRecords, Can_ViewOnlyMyRecords, IsActive,
                CreatedBy, CreatedOn
            )
            SELECT
                @RoleID, @RegistrationID, @SchemaID, @DB_Table,
                                ISNULL(Table_Alias, @DB_Table),
                                COALESCE((SELECT TOP 1 NULLIF(LTRIM(RTRIM(OverrideCaption)), '')
                                                    FROM dbo.FW_RoleDetails
                                                    WHERE RegistrationID = @RegistrationID
                                                        AND SchemaID = @SchemaID
                                                        AND DB_Table = @DB_Table
                                                        AND RoleID <> @RoleID
                                                    ORDER BY RoleID), ISNULL(Table_Alias, @DB_Table)),
                @Can_Create, @Can_Read, @Can_Update, @Can_Delete, @Can_UseQBE,
                @Can_ViewAllRecords, @Can_ViewOnlyMyRecords, 1,
                @ActorUserID, GETDATE()
            FROM dbo.FW_RoleSchema
            WHERE ID = @SchemaID;
            SET @Operation = N'INSERT';
            SET @RowsAffected = @@ROWCOUNT;

                        DECLARE @RoleDetailID INT = CONVERT(INT, SCOPE_IDENTITY());
                        INSERT INTO dbo.FW_RoleFields
                        (RoleDetailID, RegistrationID, RoleID, SchemaID, TableName, FieldName, FileLink,
                         FriendlyFieldName, OverrideCaption, Can_Create, Can_Read, Can_Update, IsActive,
                         CreatedBy, CreatedOn)
                        SELECT @RoleDetailID, @RegistrationID, @RoleID, @SchemaID, @DB_Table,
                                     c.COLUMN_NAME, @DB_Table + '.' + c.COLUMN_NAME,
                                     COALESCE((SELECT TOP 1 rf.FriendlyFieldName
                                                FROM dbo.FW_RoleFields rf
                                                WHERE rf.RegistrationID = @RegistrationID
                                                    AND rf.SchemaID = @SchemaID
                                                    AND rf.TableName = @DB_Table
                                                    AND rf.FieldName = c.COLUMN_NAME
                                                    AND rf.RoleID <> @RoleID
                                                    AND NULLIF(LTRIM(RTRIM(rf.FriendlyFieldName)), '') IS NOT NULL
                                                ORDER BY rf.RoleID), c.COLUMN_NAME),
                                     (SELECT TOP 1 rf.OverrideCaption
                                        FROM dbo.FW_RoleFields rf
                                        WHERE rf.RegistrationID = @RegistrationID
                                            AND rf.SchemaID = @SchemaID
                                            AND rf.TableName = @DB_Table
                                            AND rf.FieldName = c.COLUMN_NAME
                                            AND rf.RoleID <> @RoleID
                                        ORDER BY rf.RoleID),
                                     1, 1, 1, 0, @ActorUserID, GETDATE()
                        FROM INFORMATION_SCHEMA.COLUMNS c
                        WHERE c.TABLE_NAME = @DB_Table
                            AND c.COLUMN_NAME NOT IN ('CreatedBy', 'CreatedOn', 'UpdatedBy', 'UpdatedOn')
                            AND NOT EXISTS
                            (
                                    SELECT 1 FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                                    WHERE kcu.TABLE_NAME = @DB_Table
                                        AND kcu.COLUMN_NAME = c.COLUMN_NAME
                                        AND kcu.CONSTRAINT_NAME LIKE 'PK%'
                            );
        END;

        COMMIT TRANSACTION;
        SELECT @Operation AS Operation,
               @RowsAffected AS RowsAffected,
               @UserID AS UserID,
               @RegistrationID AS RegistrationID,
               @RoleID AS RoleID,
               @SchemaID AS SchemaID,
               @DB_Table AS DB_Table,
               CAST(1 AS BIT) AS Committed;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
