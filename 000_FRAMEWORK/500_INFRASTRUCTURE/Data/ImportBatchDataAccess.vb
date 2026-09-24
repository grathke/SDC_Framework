Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>
    ''' What an import says about itself before it is written: the name and note it is found by
    ''' later, and where it came from. Required - DataAccess.ImportEmployees refuses to start
    ''' without a name and a note, which is what makes them the gateway rather than a nicety.
    ''' </summary>
    Friend NotInheritable Class ImportBatchRequest
        Public Property BatchName As String = String.Empty
        Public Property Note As String = String.Empty
        Public Property SavedImportId As Integer
        Public Property FileName As String = String.Empty

        ''' <summary>SHA-256 of the file's bytes, so the same file imported twice can be warned about.</summary>
        Public Property FileHash As Byte()

        ''' <summary>The file's row number for each record, in the order the records are written.</summary>
        Public Property SourceRows As New List(Of Integer)()

        Public Shared Function HashOf(data As Byte()) As Byte()
            If data Is Nothing Then Return Nothing
            Return Security.Cryptography.SHA256.HashData(data)
        End Function
    End Class

    ''' <summary>What an Undo would remove and change, counted before anyone is asked to confirm it.</summary>
    Friend NotInheritable Class ImportBatchUndoImpact
        Public Property BatchName As String = String.Empty
        Public Property RegistrationId As Integer
        Public Property UndoneOn As Date?
        Public Property PeopleInBatch As Integer
        Public Property PeopleStillPresent As Integer
        Public Property IssuesReported As Integer
        Public Property IssuesAssigned As Integer
        Public Property ReportsOutsideBatch As Integer
        Public Property MessagesKept As Integer
        Public Property IncludesCurrentUser As Boolean
    End Class

    ''' <summary>
    ''' Import batches - FW_ImportBatches and FW_ImportBatchPeople: created inside an import's own
    ''' transaction, listed on the Past Imports page, and undone from it.
    '''
    ''' Every call outside the import is authorised as the import is, by
    ''' DataAccess.RequireEmployeeImportAccess against the batch's own registration: undoing another
    ''' company's import is the same breach as importing into it.
    '''
    ''' **Undo is a physical delete**, approved by Glenn on 2026-09-24 for this one purpose -
    ''' taking back an import made in error, which should happen soon after it and before the
    ''' people in it have done much. See Undo for exactly what goes and what stays.
    ''' </summary>
    Friend NotInheritable Class ImportBatchDataAccess

        Private Const PageName As String = "FW_ImportBatches_B"
        Private Const BatchTable As String = "FW_ImportBatches"

        Private Sub New()
        End Sub

        Private Shared Function OpenConnection() As SqlConnection
            Dim conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
            conn.Open()
            Return conn
        End Function

#Region "Inside the import's transaction"

        ''' <summary>
        ''' The batch row, the first write of the import's transaction - so an import that rolls
        ''' back leaves no batch, and one that commits cannot be without one. One round trip.
        ''' </summary>
        Friend Shared Function Insert(conn As SqlConnection, tx As SqlTransaction,
                                      registrationId As Integer, request As ImportBatchRequest,
                                      peopleCount As Integer, actingUserId As Integer) As Integer
            Using cmd As New SqlCommand(
                "INSERT INTO dbo.FW_ImportBatches (RegistrationID, SavedImportID, BatchName, Note, FileName, FileHash, PeopleCount, ImportedBy, ImportedOn, CreatedBy, CreatedOn) " &
                "VALUES (@RegistrationID, @SavedImportID, @BatchName, @Note, @FileName, @FileHash, @PeopleCount, @UserID, SYSUTCDATETIME(), @UserID, GETDATE()); " &
                "SELECT CAST(SCOPE_IDENTITY() AS int);", conn, tx)
                cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value = registrationId
                cmd.Parameters.Add("@SavedImportID", SqlDbType.Int).Value = If(request.SavedImportId > 0, CObj(request.SavedImportId), DBNull.Value)
                cmd.Parameters.Add("@BatchName", SqlDbType.NVarChar, 100).Value = request.BatchName.Trim()
                cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = request.Note.Trim()
                cmd.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = If(String.IsNullOrWhiteSpace(request.FileName), CObj(DBNull.Value), request.FileName)
                cmd.Parameters.Add("@FileHash", SqlDbType.VarBinary, 32).Value = If(request.FileHash Is Nothing, CObj(DBNull.Value), request.FileHash)
                cmd.Parameters.Add("@PeopleCount", SqlDbType.Int).Value = peopleCount
                cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = actingUserId
                Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
            End Using
        End Function

        ''' <summary>
        ''' One row per person, with their name and user name copied as they were imported - so a
        ''' batch that has been undone still says who was in it. The login's id is read from the
        ''' employee row the save path just wrote. One round trip per 500 people.
        ''' </summary>
        Friend Shared Sub AddPeople(conn As SqlConnection, tx As SqlTransaction, batchId As Integer,
                                    employeeIds As IList(Of Integer), sourceRows As IList(Of Integer))
            Const chunk = 500
            For start = 0 To employeeIds.Count - 1 Step chunk
                Dim sql As New StringBuilder()
                sql.Append("INSERT INTO dbo.FW_ImportBatchPeople (ImportBatchID, EmployeeID, UserId, FirstName, LastName, UserName, SourceRow) ")
                sql.Append("SELECT @Batch, e.EmployeeID, e.UserId, e.FirstName, e.LastName, e.UserName, v.SourceRow ")
                sql.Append("FROM (VALUES ")

                Using cmd As New SqlCommand() With {.Connection = conn, .Transaction = tx}
                    cmd.Parameters.Add("@Batch", SqlDbType.Int).Value = batchId
                    Dim last = Math.Min(start + chunk, employeeIds.Count) - 1
                    For i = start To last
                        If i > start Then sql.Append(", ")
                        sql.Append("(@E").Append(i).Append(", @R").Append(i).Append(")")
                        cmd.Parameters.Add("@E" & i.ToString(CultureInfo.InvariantCulture), SqlDbType.Int).Value = employeeIds(i)
                        cmd.Parameters.Add("@R" & i.ToString(CultureInfo.InvariantCulture), SqlDbType.Int).Value =
                            If(sourceRows IsNot Nothing AndAlso i < sourceRows.Count, CObj(sourceRows(i)), DBNull.Value)
                    Next
                    sql.Append(") v (EmployeeID, SourceRow) JOIN dbo.FW_Employees e ON e.EmployeeID = v.EmployeeID;")

                    cmd.CommandText = sql.ToString()
                    Dim written = cmd.ExecuteNonQuery()
                    If written <> last - start + 1 Then
                        Throw New InvalidOperationException("The import batch could not record everybody it imported.")
                    End If
                End Using
            Next
        End Sub

#End Region

#Region "Before an import"

        ''' <summary>
        ''' What the pre-check compares the file with: the registration's people, and any import of
        ''' this same file that has not been undone. One round trip, two result sets.
        ''' </summary>
        Friend Shared Function GetDuplicateEvidence(registrationId As Integer, fileHash As Byte(), profile As AccessProfile) _
            As (People As List(Of (FirstName As String, LastName As String, Email As String)), EarlierImports As List(Of String))

            DataAccess.RequireEmployeeImportAccess(profile, registrationId)

            Dim people As New List(Of (FirstName As String, LastName As String, Email As String))()
            Dim earlier As New List(Of String)()

            Using conn = OpenConnection()
                Using cmd As New SqlCommand(
                    "SELECT FirstName, LastName, Email FROM dbo.FW_Employees " &
                    "WHERE RegistrationId = @RegistrationID AND ISNULL(DeletedFlag, 0) = 0; " &
                    "SELECT BatchName, ImportedOn FROM dbo.FW_ImportBatches " &
                    "WHERE RegistrationID = @RegistrationID AND FileHash = @FileHash AND UndoneOn IS NULL " &
                    "ORDER BY ImportedOn DESC;", conn)
                    cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value = registrationId
                    cmd.Parameters.Add("@FileHash", SqlDbType.VarBinary, 32).Value = If(fileHash Is Nothing, CObj(DBNull.Value), fileHash)

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            people.Add((TextOf(reader, 0), TextOf(reader, 1), TextOf(reader, 2)))
                        End While
                        reader.NextResult()
                        While reader.Read()
                            earlier.Add(TextOf(reader, 0) & " (" & reader.GetDateTime(1).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) & " UTC)")
                        End While
                    End Using
                End Using
            End Using

            Return (people, earlier)
        End Function

        Private Shared Function TextOf(reader As SqlDataReader, ordinal As Integer) As String
            Return If(reader.IsDBNull(ordinal), String.Empty, Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture))
        End Function

#End Region

#Region "Past Imports"

        ''' <summary>
        ''' The batch's registration, read so every call below is authorised against the batch itself,
        ''' and refused unless it is the registration the page is working in - the one chosen in the
        ''' registration combo, or the session's when the combo is hidden (Glenn, 2026-09-24). An id
        ''' from another company matches nothing an import may touch, whoever is asking.
        ''' </summary>
        Private Shared Function AuthorizeBatch(batchId As Integer, registrationId As Integer, profile As AccessProfile) As Integer
            Dim actual = RegistrationOf(batchId)
            If actual <> registrationId Then
                Throw New UnauthorizedAccessException("That import belongs to another registration.")
            End If
            DataAccess.RequireEmployeeImportAccess(profile, actual)
            Return actual
        End Function

        Private Shared Function RegistrationOf(batchId As Integer) As Integer
            Using conn = OpenConnection()
                Using cmd As New SqlCommand("SELECT RegistrationID FROM dbo.FW_ImportBatches WHERE ImportBatchID = @ID", conn)
                    cmd.Parameters.Add("@ID", SqlDbType.Int).Value = batchId
                    Dim value = cmd.ExecuteScalar()
                    If value Is Nothing OrElse Convert.IsDBNull(value) Then
                        Throw New InvalidOperationException("That import no longer exists.")
                    End If
                    Return Convert.ToInt32(value, CultureInfo.InvariantCulture)
                End Using
            End Using
        End Function

        ''' <summary>The batch and everybody in it, for the People page. Two round trips: the authorisation, then one read.</summary>
        Friend Shared Function GetBatchWithPeople(batchId As Integer, registrationId As Integer, profile As AccessProfile) As (Batch As DataRow, People As DataTable)
            AuthorizeBatch(batchId, registrationId, profile)

            Dim batch As New DataTable("Batch")
            Dim people As New DataTable("People")
            Using conn = OpenConnection()
                Using cmd As New SqlCommand(
                    "SELECT b.ImportBatchID, b.BatchName, b.Note, b.FileName, b.PeopleCount, b.ImportedOn, b.UndoneOn, b.UndoneCount, " &
                    "       RegName = ISNULL(r.RegName, ''), ImportedByName = ISNULL(u.FirstLast, ''), UndoneByName = ISNULL(x.FirstLast, ''), " &
                    "       SavedImportName = ISNULL(s.ImportName, '') " &
                    "FROM dbo.FW_ImportBatches b " &
                    "LEFT JOIN dbo.FW_Registration r ON r.RegistrationID = b.RegistrationID " &
                    "LEFT JOIN dbo.FW_Users u ON u.UserId = b.ImportedBy " &
                    "LEFT JOIN dbo.FW_Users x ON x.UserId = b.UndoneBy " &
                    "LEFT JOIN dbo.FW_SavedImports s ON s.SavedImportID = b.SavedImportID " &
                    "WHERE b.ImportBatchID = @ID; " &
                    "SELECT p.SourceRow, p.FirstName, p.LastName, p.UserName, p.EmployeeID, p.UserId, p.RemovedOn, " &
                    "       StillPresent = CAST(CASE WHEN e.EmployeeID IS NULL THEN 0 ELSE 1 END AS bit) " &
                    "FROM dbo.FW_ImportBatchPeople p " &
                    "LEFT JOIN dbo.FW_Employees e ON e.EmployeeID = p.EmployeeID " &
                    "WHERE p.ImportBatchID = @ID " &
                    "ORDER BY ISNULL(p.SourceRow, 2147483647), p.LastName, p.FirstName;", conn)
                    cmd.Parameters.Add("@ID", SqlDbType.Int).Value = batchId
                    Using reader = cmd.ExecuteReader()
                        batch.Load(reader)
                        people.Load(reader)
                    End Using
                End Using
            End Using

            If batch.Rows.Count = 0 Then Throw New InvalidOperationException("That import no longer exists.")
            Return (batch.Rows(0), people)
        End Function

        ''' <summary>The note and its row version, for Edit Note.</summary>
        Friend Shared Function GetNote(batchId As Integer, registrationId As Integer, profile As AccessProfile) As (BatchName As String, Note As String, RowVersion As Byte())
            AuthorizeBatch(batchId, registrationId, profile)

            Using conn = OpenConnection()
                Using cmd As New SqlCommand("SELECT BatchName, Note, RowVersion FROM dbo.FW_ImportBatches WHERE ImportBatchID = @ID", conn)
                    cmd.Parameters.Add("@ID", SqlDbType.Int).Value = batchId
                    Using reader = cmd.ExecuteReader()
                        If Not reader.Read() Then Throw New InvalidOperationException("That import no longer exists.")
                        Return (TextOf(reader, 0), TextOf(reader, 1), CType(reader(2), Byte()))
                    End Using
                End Using
            End Using
        End Function

        ''' <summary>
        ''' Changes a batch's note. Returns False when somebody else changed the batch since it was
        ''' read - the caller says so and reads it again; nothing is overwritten silently.
        ''' </summary>
        Friend Shared Function UpdateNote(batchId As Integer, registrationId As Integer, note As String, rowVersion As Byte(),
                                          profile As AccessProfile, actingUserId As Integer) As Boolean
            AuthorizeBatch(batchId, registrationId, profile)

            Dim text = If(note, String.Empty).Trim()
            If text = String.Empty Then Throw New InvalidOperationException("An import needs a note - say what it was for.")
            If text.Length > 1000 Then Throw New InvalidOperationException("A note is at most 1000 characters.")

            Dim key = batchId.ToString(CultureInfo.InvariantCulture)
            DataAccess.LogUpdateAudit(PageName, BatchTable, "Update", "BeforeSave", key, String.Empty, registrationId:=registrationId)

            Dim saved = False
            Try
                Using conn = OpenConnection()
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_ImportBatches SET Note = @Note, UpdatedBy = @UserID, UpdatedOn = GETDATE() " &
                        "WHERE ImportBatchID = @ID AND RowVersion = @RowVersion", conn)
                        cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = text
                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = actingUserId
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = batchId
                        cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = rowVersion
                        saved = cmd.ExecuteNonQuery() = 1
                    End Using
                End Using
            Finally
                DataAccess.LogUpdateAudit(PageName, BatchTable, "Update", "AfterSave", key,
                                          JsonSerializer.Serialize(New Dictionary(Of String, String) From {{"Note", text}}),
                                          saved, registrationId:=registrationId)
            End Try

            Return saved
        End Function

        ''' <summary>
        ''' Counts what Undo would do, for the confirmation - nothing is changed. One round trip
        ''' after the authorisation.
        ''' </summary>
        Friend Shared Function GetUndoImpact(batchId As Integer, registrationId As Integer, profile As AccessProfile, actingUserId As Integer) As ImportBatchUndoImpact
            AuthorizeBatch(batchId, registrationId, profile)

            Using conn = OpenConnection()
                Using cmd As New SqlCommand(
                    PeopleTablesSql &
                    "SELECT b.BatchName, b.RegistrationID, b.UndoneOn, " &
                    "  (SELECT COUNT(*) FROM dbo.FW_ImportBatchPeople WHERE ImportBatchID = @Batch), " &
                    "  (SELECT COUNT(*) FROM @P p WHERE EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.EmployeeID = p.EmployeeID)), " &
                    "  (SELECT COUNT(*) FROM dbo.FW_HD_Issues WHERE ReporterUserID IN (SELECT UserId FROM @U)), " &
                    "  (SELECT COUNT(*) FROM dbo.FW_HD_Issues WHERE AssignedSupportUserID IN (SELECT UserId FROM @U) AND ReporterUserID NOT IN (SELECT UserId FROM @U)), " &
                    "  (SELECT COUNT(*) FROM dbo.FW_Employees WHERE AssignedManagerID IN (SELECT EmployeeID FROM @P) AND EmployeeID NOT IN (SELECT EmployeeID FROM @P)) + " &
                    "  (SELECT COUNT(*) FROM dbo.FW_Users WHERE AssignedManagerID IN (SELECT UserId FROM @U) AND UserId NOT IN (SELECT UserId FROM @U)), " &
                    "  (SELECT COUNT(*) FROM dbo.FW_Messages WHERE FromUserID IN (SELECT UserId FROM @U) OR ToUserID IN (SELECT UserId FROM @U)), " &
                    "  CAST(CASE WHEN EXISTS (SELECT 1 FROM @U WHERE UserId IN (@Acting, @SessionUser)) THEN 1 ELSE 0 END AS bit) " &
                    "FROM dbo.FW_ImportBatches b WHERE b.ImportBatchID = @Batch;", conn)
                    AddUndoParameters(cmd, batchId, actingUserId)
                    Using reader = cmd.ExecuteReader()
                        If Not reader.Read() Then Throw New InvalidOperationException("That import no longer exists.")
                        Return New ImportBatchUndoImpact With {
                            .BatchName = TextOf(reader, 0),
                            .RegistrationId = reader.GetInt32(1),
                            .UndoneOn = If(reader.IsDBNull(2), CType(Nothing, Date?), reader.GetDateTime(2)),
                            .PeopleInBatch = reader.GetInt32(3),
                            .PeopleStillPresent = reader.GetInt32(4),
                            .IssuesReported = reader.GetInt32(5),
                            .IssuesAssigned = reader.GetInt32(6),
                            .ReportsOutsideBatch = reader.GetInt32(7),
                            .MessagesKept = reader.GetInt32(8),
                            .IncludesCurrentUser = reader.GetBoolean(9)
                        }
                    End Using
                End Using
            End Using
        End Function

        ''' <summary>
        ''' The people an Undo acts on: everybody in the batch not already removed, as @P (employee
        ''' and login) and @U (logins alone). Shared by the count and the delete, so the
        ''' confirmation describes exactly the rows the delete touches.
        ''' </summary>
        Private Const PeopleTablesSql As String =
            "DECLARE @P TABLE (EmployeeID int PRIMARY KEY, UserId int NOT NULL); " &
            "INSERT INTO @P (EmployeeID, UserId) SELECT EmployeeID, UserId FROM dbo.FW_ImportBatchPeople " &
            "  WHERE ImportBatchID = @Batch AND RemovedOn IS NULL; " &
            "DECLARE @U TABLE (UserId int PRIMARY KEY); " &
            "INSERT INTO @U (UserId) SELECT DISTINCT UserId FROM @P; "

        Private Shared Sub AddUndoParameters(cmd As SqlCommand, batchId As Integer, actingUserId As Integer)
            cmd.Parameters.Add("@Batch", SqlDbType.Int).Value = batchId
            cmd.Parameters.Add("@Acting", SqlDbType.Int).Value = actingUserId
            cmd.Parameters.Add("@SessionUser", SqlDbType.Int).Value =
                If(SessionState.IsActive AndAlso SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)
        End Sub

        ''' <summary>
        ''' Takes an import back: a physical delete of everybody in it, in one transaction.
        '''
        ''' **Removed:** the employees and their logins; their role assignments; their own settings
        ''' and traces - sessions, sign-in attempts, zooms, saved searches, grid layouts, hints and
        ''' Switch User rows; and the Help Desk issues they reported, with those issues' attachments.
        '''
        ''' **Changed, not removed:** issues assigned to them are unassigned; anybody outside the
        ''' batch who reported to one of them has no manager; a registration naming one as its
        ''' company admin names nobody.
        '''
        ''' **Kept:** messages, which store the sender's name; the error and fallback logs; the
        ''' audit trail; and the batch itself with its people, marked removed, as the record that
        ''' the import happened and was taken back.
        '''
        ''' Refused when the batch is already undone, or when it contains the signed-in person.
        ''' Returns how many people were removed.
        ''' </summary>
        Friend Shared Function Undo(batchId As Integer, registrationId As Integer, profile As AccessProfile, actingUserId As Integer) As Integer
            AuthorizeBatch(batchId, registrationId, profile)
            ReadOnlyPreview.Refuse("Undo Import")

            Dim key = batchId.ToString(CultureInfo.InvariantCulture)
            DataAccess.LogUpdateAudit(PageName, BatchTable, "UndoImport", "BeforeSave", key, String.Empty, registrationId:=registrationId)

            Dim removed = 0
            Dim beforeImages As New DataTable("Employees")
            Dim succeeded = False
            Try
                Using conn = OpenConnection()
                    Using tx = conn.BeginTransaction()
                        Try
                            Using cmd As New SqlCommand(
                                "SET XACT_ABORT ON; " &
                                "DECLARE @UndoneOn datetime2(0); " &
                                "SELECT @UndoneOn = UndoneOn FROM dbo.FW_ImportBatches WITH (UPDLOCK, HOLDLOCK) WHERE ImportBatchID = @Batch; " &
                                "IF @@ROWCOUNT = 0 THROW 50001, 'That import no longer exists.', 1; " &
                                "IF @UndoneOn IS NOT NULL THROW 50002, 'That import has already been undone.', 1; " &
                                PeopleTablesSql &
                                "IF EXISTS (SELECT 1 FROM @U WHERE UserId IN (@Acting, @SessionUser)) " &
                                "  THROW 50003, 'This import includes the person signed in. Sign in as somebody else to undo it.', 1; " &
                                "SELECT e.* FROM dbo.FW_Employees e WHERE e.EmployeeID IN (SELECT EmployeeID FROM @P); " &
                                "DELETE a FROM dbo.FW_HD_IssueAttachments a JOIN dbo.FW_HD_Issues i ON i.IssueID = a.IssueID " &
                                "  WHERE i.ReporterUserID IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_HD_Issues WHERE ReporterUserID IN (SELECT UserId FROM @U); " &
                                "UPDATE dbo.FW_HD_Issues SET AssignedSupportUserID = NULL WHERE AssignedSupportUserID IN (SELECT UserId FROM @U); " &
                                "UPDATE dbo.FW_Employees SET AssignedManagerID = NULL WHERE AssignedManagerID IN (SELECT EmployeeID FROM @P); " &
                                "UPDATE dbo.FW_Users SET AssignedManagerID = NULL WHERE AssignedManagerID IN (SELECT UserId FROM @U); " &
                                "UPDATE dbo.FW_Registration SET CompanyAdmin_UserID = NULL WHERE CompanyAdmin_UserID IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_EmployeeRoles WHERE EmployeeID IN (SELECT EmployeeID FROM @P); " &
                                "DELETE FROM dbo.FW_Session WHERE UserID IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_LoginAttempt WHERE UserID IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_PageZooms WHERE UserID IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_SavedQBE WHERE UserID IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_TableLayouts WHERE UserID IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_UserUiHints WHERE UserID IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_SwitchUser WHERE UserId IN (SELECT UserId FROM @U); " &
                                "DELETE FROM dbo.FW_Employees WHERE EmployeeID IN (SELECT EmployeeID FROM @P); " &
                                "DECLARE @Removed int = @@ROWCOUNT; " &
                                "DELETE FROM dbo.FW_Users WHERE UserId IN (SELECT UserId FROM @U) " &
                                "  AND NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = dbo.FW_Users.UserId); " &
                                "UPDATE dbo.FW_ImportBatchPeople SET RemovedOn = SYSUTCDATETIME() WHERE ImportBatchID = @Batch AND RemovedOn IS NULL; " &
                                "UPDATE dbo.FW_ImportBatches SET UndoneBy = @Acting, UndoneOn = SYSUTCDATETIME(), UndoneCount = @Removed, " &
                                "  UpdatedBy = @Acting, UpdatedOn = GETDATE() WHERE ImportBatchID = @Batch; " &
                                "SELECT @Removed;", conn, tx)
                                AddUndoParameters(cmd, batchId, actingUserId)
                                cmd.CommandTimeout = 120

                                Using reader = cmd.ExecuteReader()
                                    beforeImages.Load(reader)
                                    If Not reader.IsClosed AndAlso reader.Read() Then
                                        removed = reader.GetInt32(0)
                                    End If
                                End Using
                            End Using

                            tx.Commit()
                            succeeded = True
                        Catch
                            Try
                                tx.Rollback()
                            Catch telemetryEx As Exception
                                Telemetry.Error(telemetryEx, "ImportBatchDataAccess.Undo")
                            End Try
                            Throw
                        End Try
                    End Using
                End Using
            Finally
                DataAccess.LogUpdateAudit(PageName, BatchTable, "UndoImport", "AfterSave", key,
                                          JsonSerializer.Serialize(New Dictionary(Of String, Object) From {{"removed", removed}}),
                                          succeeded, registrationId:=registrationId)
            End Try

            AuditRemovedPeople(beforeImages, registrationId)
            Return removed
        End Function

        ''' <summary>
        ''' One audit row per person removed, holding the employee row as it was - the record an
        ''' Undo leaves of who these people were. After the commit and never allowed to fail it,
        ''' as for every other save. Password columns and binary values are left out.
        ''' </summary>
        Private Shared Sub AuditRemovedPeople(beforeImages As DataTable, registrationId As Integer)
            For Each row As DataRow In beforeImages.Rows
                Try
                    Dim snapshot As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
                    For Each column As DataColumn In beforeImages.Columns
                        If column.ColumnName.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
                        Dim value = row(column)
                        If Convert.IsDBNull(value) OrElse TypeOf value Is Byte() Then Continue For
                        snapshot(column.ColumnName) = value
                    Next

                    DataAccess.LogUpdateAudit(PageName, "FW_Employees", "UndoImport", "BeforeSave",
                                              Convert.ToString(row("EmployeeID"), CultureInfo.InvariantCulture),
                                              JsonSerializer.Serialize(snapshot), True, registrationId:=registrationId)
                Catch ex As Exception
                    Telemetry.Error(ex, "ImportBatchDataAccess.AuditRemovedPeople")
                End Try
            Next
        End Sub

#End Region
    End Class
End Namespace
