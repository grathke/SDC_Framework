Option Strict On
Option Explicit On

Imports System.Data
Imports System.Globalization
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>
    ''' Saved import mappings - FW_SavedImports - read, saved, used and deleted.
    '''
    ''' Every call is authorised the way the import itself is, by
    ''' DataAccess.RequireEmployeeImportAccess: a template belongs to a registration, and reading
    ''' or deleting another company's would be the same breach as importing into it.
    '''
    ''' One round trip per call. Save is an update-or-insert in one statement, which is safe
    ''' against the filtered unique index because the update runs first and the insert only when
    ''' it matched nothing.
    ''' </summary>
    Friend NotInheritable Class SavedImportDataAccess

        Private Sub New()
        End Sub

        Private Shared Function OpenConnection() As SqlConnection
            Dim conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
            conn.Open()
            Return conn
        End Function

        ''' <summary>
        ''' Every Saved Import the session may use, across the registrations it may import into:
        ''' all of them for an Application Admin, the session's own for a Company Admin -
        ''' the same rule as the Import Into combo (DataAccess.MayImportIntoAnyRegistration). Each row carries its registration and that
        ''' registration's name, because the list now comes first on the page and choosing from it
        ''' sets Import Into (Glenn, 2026-09-24).
        '''
        ''' One round trip. Most recently used first, never-used last.
        ''' </summary>
        Friend Shared Function ListAccessible(targetTable As String, profile As AccessProfile) As DataTable
            If Not DataAccess.CanImportEmployees() Then
                Throw New UnauthorizedAccessException("Only an Application Admin or a Company Admin may import employees.")
            End If

            Dim own = If(SessionState.IsActive AndAlso SessionState.Current.HasValue, SessionState.Current.Value.RegistrationID, 0)
            Dim everyRegistration = DataAccess.MayImportIntoAnyRegistration()

            Dim table As New DataTable("FW_SavedImports")
            Using conn = OpenConnection()
                Using cmd As New SqlCommand(
                    "SELECT s.SavedImportID, s.ImportName, s.MappingData, s.LastUsedOn, s.UseCount, s.RegistrationID, " &
                    "       RegName = ISNULL(r.RegName, ''), " &
                    "       HasSourceFile = CAST(CASE WHEN s.SourceFileData IS NULL THEN 0 ELSE 1 END AS bit) " &
                    "FROM dbo.FW_SavedImports s " &
                    "LEFT JOIN dbo.FW_Registration r ON r.RegistrationID = s.RegistrationID " &
                    "WHERE s.TargetTable = @TargetTable AND s.DeletedFlag = 0 " &
                    "  AND (@Every = 1 OR s.RegistrationID = @Own) " &
                    "ORDER BY CASE WHEN s.LastUsedOn IS NULL THEN 1 ELSE 0 END, s.LastUsedOn DESC, s.ImportName, r.RegName", conn)
                    cmd.Parameters.Add("@TargetTable", SqlDbType.NVarChar, 128).Value = targetTable
                    cmd.Parameters.Add("@Every", SqlDbType.Bit).Value = everyRegistration
                    cmd.Parameters.Add("@Own", SqlDbType.Int).Value = own
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        ''' <summary>
        ''' The file a template was saved from, name and bytes - read only when the template is
        ''' chosen, so the list does not carry every template's file. Nothing when it has none.
        ''' </summary>
        Friend Shared Function GetSourceFile(templateId As Integer,
                                             registrationId As Integer,
                                             profile As AccessProfile) As (FileName As String, Data As Byte())?
            DataAccess.RequireEmployeeImportAccess(profile, registrationId)

            Using conn = OpenConnection()
                Using cmd As New SqlCommand(
                    "SELECT SourceFileName, SourceFileData FROM dbo.FW_SavedImports " &
                    "WHERE SavedImportID = @ID AND RegistrationID = @RegistrationID AND DeletedFlag = 0 " &
                    "  AND SourceFileData IS NOT NULL", conn)
                    cmd.Parameters.Add("@ID", SqlDbType.Int).Value = templateId
                    cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value = registrationId
                    Using reader = cmd.ExecuteReader()
                        If Not reader.Read() Then Return Nothing
                        Dim name = If(reader.IsDBNull(0), "file.csv", reader.GetString(0))
                        Return (name, CType(reader(1), Byte()))
                    End Using
                End Using
            End Using
        End Function

        ''' <summary>
        ''' Saves a mapping under a name, replacing the live template of that name if there is one.
        ''' The page asks before replacing; this does what it is told.
        '''
        ''' The file goes with it when one is given. Nothing leaves the stored copy as it is - a
        ''' mapping changed with no file loaded is still about the same file.
        ''' </summary>
        Friend Shared Function Save(registrationId As Integer,
                                    targetTable As String,
                                    templateName As String,
                                    mappingJson As String,
                                    profile As AccessProfile,
                                    actingUserId As Integer,
                                    Optional sourceFileName As String = Nothing,
                                    Optional sourceFileData As Byte() = Nothing) As Integer
            DataAccess.RequireEmployeeImportAccess(profile, registrationId)

            Dim name = If(templateName, String.Empty).Trim()
            If name = String.Empty Then Throw New InvalidOperationException("Give the Saved Import a name.")
            If name.Length > 100 Then Throw New InvalidOperationException("A Saved Import name is at most 100 characters.")

            DataAccess.LogUpdateAudit("FW_EmployeeImport", "FW_SavedImports", "Save", "BeforeSave", name, String.Empty,
                                      registrationId:=registrationId)

            Dim savedId = 0
            Dim succeeded = False
            Try
                Using conn = OpenConnection()
                    Using cmd As New SqlCommand(
                        "DECLARE @ID int; " &
                        "UPDATE dbo.FW_SavedImports " &
                        "SET MappingData = @MappingData, UpdatedBy = @UserID, UpdatedOn = SYSUTCDATETIME(), @ID = SavedImportID, " &
                        "    SourceFileName = COALESCE(@SourceFileName, SourceFileName), " &
                        "    SourceFileData = COALESCE(@SourceFileData, SourceFileData) " &
                        "WHERE RegistrationID = @RegistrationID AND TargetTable = @TargetTable " &
                        "  AND ImportName = @ImportName AND DeletedFlag = 0; " &
                        "IF @@ROWCOUNT = 0 " &
                        "BEGIN " &
                        "  INSERT INTO dbo.FW_SavedImports (RegistrationID, TargetTable, ImportName, MappingData, SourceFileName, SourceFileData, CreatedBy, CreatedOn) " &
                        "  VALUES (@RegistrationID, @TargetTable, @ImportName, @MappingData, @SourceFileName, @SourceFileData, @UserID, SYSUTCDATETIME()); " &
                        "  SET @ID = CAST(SCOPE_IDENTITY() AS int); " &
                        "END; " &
                        "SELECT @ID;", conn)
                        cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value = registrationId
                        cmd.Parameters.Add("@TargetTable", SqlDbType.NVarChar, 128).Value = targetTable
                        cmd.Parameters.Add("@ImportName", SqlDbType.NVarChar, 100).Value = name
                        cmd.Parameters.Add("@MappingData", SqlDbType.NVarChar, -1).Value = If(mappingJson, "{}")
                        cmd.Parameters.Add("@SourceFileName", SqlDbType.NVarChar, 260).Value =
                            If(sourceFileData Is Nothing OrElse String.IsNullOrWhiteSpace(sourceFileName), CObj(DBNull.Value), sourceFileName)
                        cmd.Parameters.Add("@SourceFileData", SqlDbType.VarBinary, -1).Value =
                            If(sourceFileData Is Nothing, CObj(DBNull.Value), sourceFileData)
                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = actingUserId
                        savedId = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                        succeeded = savedId > 0
                    End Using
                End Using
            Finally
                DataAccess.LogUpdateAudit("FW_EmployeeImport", "FW_SavedImports", "Save", "AfterSave",
                                          savedId.ToString(CultureInfo.InvariantCulture), mappingJson, succeeded,
                                          registrationId:=registrationId)
            End Try

            Return savedId
        End Function

        ''' <summary>Soft-deletes a template. Returns False when it was already gone.</summary>
        Friend Shared Function Delete(templateId As Integer,
                                      registrationId As Integer,
                                      profile As AccessProfile,
                                      actingUserId As Integer) As Boolean
            DataAccess.RequireEmployeeImportAccess(profile, registrationId)

            Dim key = templateId.ToString(CultureInfo.InvariantCulture)
            DataAccess.LogUpdateAudit("FW_EmployeeImport", "FW_SavedImports", "Delete", "BeforeSave", key, String.Empty,
                                      registrationId:=registrationId)

            Dim deleted = False
            Try
                Using conn = OpenConnection()
                    ' The registration is in the WHERE as well as checked above, so an id from
                    ' another company matches nothing rather than trusting the caller's pairing.
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_SavedImports " &
                        "SET DeletedFlag = 1, DeletedBy = @UserID, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UserID, UpdatedOn = SYSUTCDATETIME(), " &
                        "    SourceFileName = NULL, SourceFileData = NULL " &
                        "WHERE SavedImportID = @ID AND RegistrationID = @RegistrationID AND DeletedFlag = 0", conn)
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = templateId
                        cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value = registrationId
                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = actingUserId
                        deleted = cmd.ExecuteNonQuery() = 1
                    End Using
                End Using
            Finally
                DataAccess.LogUpdateAudit("FW_EmployeeImport", "FW_SavedImports", "Delete", "AfterSave", key, String.Empty, deleted,
                                          registrationId:=registrationId)
            End Try

            Return deleted
        End Function

        ''' <summary>
        ''' Records that an import used a template. After the import has committed, and never
        ''' allowed to fail it: a count that is one short is not worth a message about an import
        ''' that worked.
        ''' </summary>
        Friend Shared Sub MarkUsed(templateId As Integer, registrationId As Integer, profile As AccessProfile)
            If templateId <= 0 Then Return

            Try
                DataAccess.RequireEmployeeImportAccess(profile, registrationId)
                Using conn = OpenConnection()
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_SavedImports SET LastUsedOn = SYSUTCDATETIME(), UseCount = UseCount + 1 " &
                        "WHERE SavedImportID = @ID AND RegistrationID = @RegistrationID AND DeletedFlag = 0", conn)
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = templateId
                        cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value = registrationId
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            Catch ex As Exception
                Telemetry.Error(ex, "SavedImportDataAccess.MarkUsed")
            End Try
        End Sub
    End Class
End Namespace
