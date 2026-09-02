Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Globalization
Imports Microsoft.Data.SqlClient

Namespace HelloWorld
    Public NotInheritable Class HelpDeskDataAccess
        Private Sub New()
        End Sub

        Public Class AdminDashboardSnapshot
            Public Property Kpis As DataTable
            Public Property ByStatus As DataTable
            Public Property ByPriority As DataTable
            Public Property ByCategory As DataTable
            Public Property ByRegistration As DataTable
            Public Property OldestAwaitingSupport As DataTable
        End Class

        Public Class IssueRecord
            Public Property IssueID As Integer
            Public Property RegistrationID As Integer
            Public Property ApplicationID As Integer?
            Public Property IssueNumber As String
            Public Property CategoryID As Integer?
            Public Property Subject As String
            Public Property Description As String

            ''' What should have happened. Asked for only on the categories flagged in
            ''' FW_HD_IssueCategories, because "how it should work" is meaningless on a question.
            Public Property ExpectedBehavior As String

            ''' How to make it happen again. Asked for on the defect categories only - a suggestion
            ''' has nothing to reproduce.
            Public Property StepsToReproduce As String

            ''' The page the report was raised from, captured rather than typed.
            Public Property ReportedFromPage As String
            Public Property ConversationText As String
            Public Property ConversationEntryCount As Integer
            Public Property Status As String
            Public Property Priority As String
            Public Property ClosedBy As Integer?
            Public Property ClosedOn As DateTime?
            Public Property FirstResponseOn As DateTime?
            Public Property ReporterUserID As Integer
            Public Property AssignedSupportUserID As Integer?
            Public Property CreatedBy As Integer
            Public Property CreatedOn As DateTime
            Public Property UpdatedBy As Integer?
            Public Property UpdatedOn As DateTime?
            Public Property RowVersion As Byte()
        End Class

        Private Shared Function ConnectionString() As String
            Dim configured = Environment.GetEnvironmentVariable("HELLOWORLD_DB_CONNECTION")
            If Not String.IsNullOrWhiteSpace(configured) Then Return configured.Trim()

            Dim server = Environment.GetEnvironmentVariable("HELLOWORLD_DB_SERVER")
            Dim user = Environment.GetEnvironmentVariable("HELLOWORLD_DB_USER")
            Dim password = Environment.GetEnvironmentVariable("HELLOWORLD_DB_PASSWORD")
            Dim database = Environment.GetEnvironmentVariable("HELLOWORLD_DB_NAME")
            If String.IsNullOrWhiteSpace(server) Then server = "BEELINK"
            If String.IsNullOrWhiteSpace(user) Then user = "sa"
            If String.IsNullOrWhiteSpace(password) Then password = String.Empty
            If String.IsNullOrWhiteSpace(database) Then database = "WX_Framework"
            Return "Server=" & server & ";User Id=" & user & ";Password=" & password & ";Encrypt=False;TrustServerCertificate=True;Initial Catalog=" & database & ";"
        End Function

        Public Shared Function GetIssueById(issueId As Integer, registrationId As Integer) As IssueRecord
            Using conn As New SqlConnection(ConnectionString())
                conn.Open()
                Using cmd As New SqlCommand("SELECT TOP 1 IssueID, RegistrationID, ApplicationID, IssueNumber, CategoryID, Subject, Description, ExpectedBehavior, StepsToReproduce, ReportedFromPage, ConversationText, ConversationEntryCount, Status, Priority, ClosedBy, ClosedOn, FirstResponseOn, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn, RowVersion FROM dbo.FW_HD_Issues WHERE IssueID = @IssueID AND RegistrationID = @RegistrationID AND ISNULL(DeletedFlag, 0) = 0", conn)
                    cmd.Parameters.AddWithValue("@IssueID", issueId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Using reader = cmd.ExecuteReader()
                        If reader.Read() Then Return ReadIssue(reader)
                    End Using
                End Using
            End Using
            Return Nothing
        End Function

        Public Shared Function GetCategories(registrationId As Integer) As DataTable
            Using conn As New SqlConnection(ConnectionString())
                conn.Open()
                Using cmd As New SqlCommand("SELECT CategoryID, CategoryName, ISNULL(DescribeThe, '') AS DescribeThe, ISNULL(RequiresExpectedBehavior, 0) AS RequiresExpectedBehavior, ISNULL(RequiresPage, 0) AS RequiresPage FROM dbo.FW_HD_IssueCategories WHERE (RegistrationID = @RegistrationID OR RegistrationID IS NULL) AND ISNULL(IsActive, 1) = 1 AND ISNULL(DeletedFlag, 0) = 0 ORDER BY DisplayOrder, CategoryName", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Using adapter As New SqlDataAdapter(cmd)
                        Dim result As New DataTable()
                        adapter.Fill(result)
                        Return result
                    End Using
                End Using
            End Using
        End Function

        Public Shared Function GetAdminDashboardKpis() As DataTable
            Return ExecuteDashboardQuery(
                "SELECT COUNT(*) AS TotalTickets, " &
                "SUM(CASE WHEN Status NOT IN ('Resolved', 'Closed') THEN 1 ELSE 0 END) AS OpenTickets, " &
                "SUM(CASE WHEN Status IN ('New', 'Reopened') THEN 1 ELSE 0 END) AS AwaitingSupport, " &
                "CAST(NULL AS DECIMAL(10, 2)) AS AvgFirstResponseHours, " &
                "AVG(CASE WHEN ClosedOn IS NOT NULL THEN CONVERT(DECIMAL(10, 2), DATEDIFF(MINUTE, CreatedOn, ClosedOn)) / 60.0 END) AS AvgResolutionHours " &
                "FROM dbo.FW_HD_Issues WHERE ISNULL(DeletedFlag, 0) = 0")
        End Function

        Public Shared Function GetAdminDashboardByStatus() As DataTable
            Return ExecuteDashboardQuery(
                "SELECT Status AS Bucket, COUNT(*) AS TicketCount " &
                "FROM dbo.FW_HD_Issues WHERE ISNULL(DeletedFlag, 0) = 0 " &
                "GROUP BY Status ORDER BY CASE Status WHEN 'New' THEN 1 WHEN 'Assigned' THEN 2 WHEN 'In Progress' THEN 3 WHEN 'Waiting for User' THEN 4 WHEN 'Reopened' THEN 5 WHEN 'Resolved' THEN 6 WHEN 'Closed' THEN 7 ELSE 8 END")
        End Function

        Public Shared Function GetAdminDashboardByPriority() As DataTable
            Return ExecuteDashboardQuery(
                "SELECT Priority AS Bucket, COUNT(*) AS TicketCount " &
                "FROM dbo.FW_HD_Issues WHERE ISNULL(DeletedFlag, 0) = 0 " &
                "GROUP BY Priority ORDER BY CASE Priority WHEN 'Critical' THEN 1 WHEN 'High' THEN 2 WHEN 'Normal' THEN 3 WHEN 'Low' THEN 4 ELSE 5 END")
        End Function

        Public Shared Function GetAdminDashboardByCategory() As DataTable
            Return ExecuteDashboardQuery(
                "SELECT ISNULL(category.CategoryName, 'Uncategorized') AS Bucket, COUNT(*) AS TicketCount " &
                "FROM dbo.FW_HD_Issues AS issue " &
                "LEFT JOIN dbo.FW_HD_IssueCategories AS category ON category.CategoryID = issue.CategoryID " &
                "WHERE ISNULL(issue.DeletedFlag, 0) = 0 " &
                "GROUP BY ISNULL(category.CategoryName, 'Uncategorized') ORDER BY TicketCount DESC, Bucket")
        End Function

        Public Shared Function GetAdminDashboardByRegistration() As DataTable
            Return ExecuteDashboardQuery(
                "SELECT issue.RegistrationID, ISNULL(registration.RegName, 'Unknown account') AS Bucket, COUNT(*) AS TicketCount " &
                "FROM dbo.FW_HD_Issues AS issue " &
                "LEFT JOIN dbo.FW_Registration AS registration ON registration.RegistrationID = issue.RegistrationID " &
                "WHERE ISNULL(issue.DeletedFlag, 0) = 0 " &
                "GROUP BY issue.RegistrationID, ISNULL(registration.RegName, 'Unknown account') ORDER BY TicketCount DESC, Bucket")
        End Function

        Public Shared Function GetOldestAwaitingSupportIssues() As DataTable
            Return ExecuteDashboardQuery(
                "SELECT TOP 10 issue.IssueID, issue.IssueNumber, issue.Subject, " &
                "ISNULL(registration.RegName, 'Unknown account') AS AccountName, issue.Status, issue.Priority, issue.CreatedOn " &
                "FROM dbo.FW_HD_Issues AS issue " &
                "LEFT JOIN dbo.FW_Registration AS registration ON registration.RegistrationID = issue.RegistrationID " &
                "WHERE ISNULL(issue.DeletedFlag, 0) = 0 AND issue.Status IN ('New', 'Reopened') " &
                "ORDER BY issue.CreatedOn")
        End Function

        Public Shared Function GetAdminDashboardSnapshot(Optional filterType As String = Nothing,
                                                          Optional filterValue As String = Nothing) As AdminDashboardSnapshot
            Using conn As New SqlConnection(ConnectionString())
                conn.Open()
                Using cmd As New SqlCommand("dbo.FW_HD_GetAdminDashboard", conn)
                    cmd.CommandType = CommandType.StoredProcedure
                    cmd.Parameters.AddWithValue("@FilterType", If(String.IsNullOrWhiteSpace(filterType), CType(DBNull.Value, Object), filterType))
                    cmd.Parameters.AddWithValue("@FilterValue", If(String.IsNullOrWhiteSpace(filterValue), CType(DBNull.Value, Object), filterValue))
                    Using reader = cmd.ExecuteReader()
                        Dim snapshot As New AdminDashboardSnapshot With {
                            .Kpis = LoadResultSet(reader),
                            .ByStatus = Nothing,
                            .ByPriority = Nothing,
                            .ByCategory = Nothing,
                            .ByRegistration = Nothing,
                            .OldestAwaitingSupport = Nothing
                        }

                            snapshot.ByStatus = LoadResultSet(reader)
                            snapshot.ByPriority = LoadResultSet(reader)
                            snapshot.ByCategory = LoadResultSet(reader)
                            snapshot.ByRegistration = LoadResultSet(reader)
                            snapshot.OldestAwaitingSupport = LoadResultSet(reader)
                        Return snapshot
                    End Using
                End Using
            End Using
        End Function

        Private Shared Function LoadResultSet(reader As SqlDataReader) As DataTable
            Dim result As New DataTable()
            result.Load(reader)
            Return result
        End Function

        Private Shared Function ExecuteDashboardQuery(sql As String) As DataTable
            Using conn As New SqlConnection(ConnectionString())
                conn.Open()
                Using cmd As New SqlCommand(sql, conn)
                    Using adapter As New SqlDataAdapter(cmd)
                        Dim result As New DataTable()
                        adapter.Fill(result)
                        Return result
                    End Using
                End Using
            End Using
        End Function

        Public Shared Sub DeleteIssue(issueId As Integer, registrationId As Integer, deletedBy As Integer)
            Using conn As New SqlConnection(ConnectionString())
                conn.Open()
                Using cmd As New SqlCommand("UPDATE dbo.FW_HD_Issues SET DeletedFlag = 1, DeletedBy = @DeletedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @DeletedBy, UpdatedOn = SYSUTCDATETIME() WHERE IssueID = @IssueID AND RegistrationID = @RegistrationID AND ISNULL(DeletedFlag, 0) = 0", conn)
                    cmd.Parameters.AddWithValue("@IssueID", issueId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@DeletedBy", deletedBy)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Shared Function SaveIssue(record As IssueRecord, responseText As String, attachment As HelpDeskAttachmentUpload) As Boolean
            If record Is Nothing Then Throw New ArgumentNullException(NameOf(record))
            Using conn As New SqlConnection(ConnectionString())
                conn.Open()
                Using trans = conn.BeginTransaction()
                    Try
                        If record.IssueID = 0 Then
                            If Not IsCurrentUserSupport() Then record.Status = "New"
                            record.AssignedSupportUserID = ResolveSupportUserIdForNewIssue(record.RegistrationID)
                            Using cmd As New SqlCommand("INSERT INTO dbo.FW_HD_Issues (RegistrationID, ApplicationID, IssueNumber, CategoryID, Subject, Description, ExpectedBehavior, StepsToReproduce, ReportedFromPage, ConversationText, ConversationEntryCount, Status, Priority, ReporterUserID, AssignedSupportUserID, CreatedBy, CreatedOn) VALUES (@RegistrationID, @ApplicationID, @IssueNumber, @CategoryID, @Subject, @Description, @ExpectedBehavior, @StepsToReproduce, @ReportedFromPage, @ConversationText, 1, @Status, @Priority, @ReporterUserID, @AssignedSupportUserID, @UserID, SYSUTCDATETIME()); SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, trans)
                                cmd.Parameters.AddWithValue("@ExpectedBehavior", If(String.IsNullOrWhiteSpace(record.ExpectedBehavior), CType(DBNull.Value, Object), record.ExpectedBehavior.Trim()))
                                cmd.Parameters.AddWithValue("@StepsToReproduce", If(String.IsNullOrWhiteSpace(record.StepsToReproduce), CType(DBNull.Value, Object), record.StepsToReproduce.Trim()))
                                cmd.Parameters.AddWithValue("@ReportedFromPage", If(String.IsNullOrWhiteSpace(record.ReportedFromPage), CType(DBNull.Value, Object), record.ReportedFromPage.Trim()))
                                record.ConversationText = FormatConversationEntry(record, CurrentAuthorName(), record.Description)
                                AddIssueParameters(cmd, record)
                                record.IssueID = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                            End Using
                        ElseIf Not String.IsNullOrWhiteSpace(responseText) OrElse record.Status <> String.Empty Then
                            Dim author = CurrentAuthorName()
                            Dim isSupport = IsCurrentUserSupport()
                            Using cmd As New SqlCommand("UPDATE dbo.FW_HD_Issues SET Status = CASE WHEN @IsSupport = 1 THEN @Status WHEN NULLIF(LTRIM(RTRIM(@ResponseText)), '') IS NOT NULL AND Status = 'Closed' THEN 'Reopened' ELSE Status END, Priority = CASE WHEN @IsSupport = 1 THEN @Priority ELSE Priority END, FirstResponseOn = CASE WHEN @IsSupport = 1 AND NULLIF(LTRIM(RTRIM(@ResponseText)), '') IS NOT NULL AND FirstResponseOn IS NULL THEN SYSUTCDATETIME() ELSE FirstResponseOn END, ClosedBy = CASE WHEN @IsSupport = 1 AND @Status = 'Closed' THEN @UserID WHEN @IsSupport = 1 THEN NULL WHEN NULLIF(LTRIM(RTRIM(@ResponseText)), '') IS NOT NULL AND Status = 'Closed' THEN NULL ELSE ClosedBy END, ClosedOn = CASE WHEN @IsSupport = 1 AND @Status = 'Closed' THEN SYSUTCDATETIME() WHEN @IsSupport = 1 THEN NULL WHEN NULLIF(LTRIM(RTRIM(@ResponseText)), '') IS NOT NULL AND Status = 'Closed' THEN NULL ELSE ClosedOn END, ConversationText = CASE WHEN NULLIF(LTRIM(RTRIM(@ResponseText)), '') IS NULL THEN ConversationText ELSE @ConversationText + CASE WHEN NULLIF(LTRIM(RTRIM(ConversationText)), '') IS NULL THEN '' ELSE CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + '----------------------------------------' + CHAR(13) + CHAR(10) + CHAR(13) + CHAR(10) + ConversationText END END, ConversationEntryCount = CASE WHEN NULLIF(LTRIM(RTRIM(@ResponseText)), '') IS NULL THEN ConversationEntryCount ELSE ISNULL(ConversationEntryCount, 0) + 1 END, UpdatedBy = @UserID, UpdatedOn = SYSUTCDATETIME() WHERE IssueID = @IssueID AND RegistrationID = @RegistrationID AND ISNULL(DeletedFlag, 0) = 0", conn, trans)
                                cmd.Parameters.AddWithValue("@Status", If(isSupport, record.Status, String.Empty))
                                cmd.Parameters.AddWithValue("@Priority", record.Priority)
                                cmd.Parameters.AddWithValue("@IsSupport", If(isSupport, 1, 0))
                                cmd.Parameters.AddWithValue("@ResponseText", If(responseText, String.Empty))
                                cmd.Parameters.AddWithValue("@ConversationText", FormatConversationEntry(record, author, If(responseText, String.Empty)))
                                cmd.Parameters.AddWithValue("@UserID", CurrentUserId())
                                cmd.Parameters.AddWithValue("@IssueID", record.IssueID)
                                cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                                If cmd.ExecuteNonQuery() = 0 Then Throw New InvalidOperationException("The issue was not found or is no longer active.")
                            End Using
                        End If

                        If attachment IsNot Nothing Then
                            Using cmd As New SqlCommand("INSERT INTO dbo.FW_HD_IssueAttachments (IssueID, ConversationEntryID, RegistrationID, FileName, ContentType, FileSize, FileData, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) VALUES (@IssueID, NULL, @RegistrationID, @FileName, @ContentType, @FileSize, @FileData, @UserID, SYSUTCDATETIME(), @UserID, SYSUTCDATETIME())", conn, trans)
                                cmd.Parameters.AddWithValue("@IssueID", record.IssueID)
                                cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                                cmd.Parameters.AddWithValue("@FileName", attachment.FileName)
                                cmd.Parameters.AddWithValue("@ContentType", attachment.ContentType)
                                cmd.Parameters.AddWithValue("@FileSize", attachment.FileSize)
                                cmd.Parameters.Add("@FileData", SqlDbType.VarBinary, -1).Value = attachment.FileData
                                cmd.Parameters.AddWithValue("@UserID", CurrentUserId())
                                cmd.ExecuteNonQuery()
                            End Using
                        End If

                        trans.Commit()
                        Return True
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        End Function

        Private Shared Sub AddIssueParameters(cmd As SqlCommand, record As IssueRecord)
            cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
            AddNullable(cmd, "@ApplicationID", record.ApplicationID)
            cmd.Parameters.AddWithValue("@IssueNumber", record.IssueNumber)
            AddNullable(cmd, "@CategoryID", record.CategoryID)
            cmd.Parameters.AddWithValue("@Subject", record.Subject)
            cmd.Parameters.AddWithValue("@Description", record.Description)
            cmd.Parameters.AddWithValue("@ConversationText", record.ConversationText)
            cmd.Parameters.AddWithValue("@Status", record.Status)
            cmd.Parameters.AddWithValue("@Priority", record.Priority)
            cmd.Parameters.AddWithValue("@ReporterUserID", record.ReporterUserID)
            AddNullable(cmd, "@AssignedSupportUserID", record.AssignedSupportUserID)
            cmd.Parameters.AddWithValue("@UserID", CurrentUserId())
        End Sub

        Private Shared Sub AddNullable(cmd As SqlCommand, name As String, value As Integer?)
            cmd.Parameters.AddWithValue(name, If(value.HasValue, CType(value.Value, Object), DBNull.Value))
        End Sub

        Private Shared Function ReadIssue(reader As SqlDataReader) As IssueRecord
            Return New IssueRecord With {
                .IssueID = Convert.ToInt32(reader("IssueID"), CultureInfo.InvariantCulture),
                .RegistrationID = Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture),
                .ApplicationID = NullableInt(reader("ApplicationID")),
                .IssueNumber = TextValue(reader("IssueNumber")),
                .CategoryID = NullableInt(reader("CategoryID")),
                .Subject = TextValue(reader("Subject")),
                .Description = TextValue(reader("Description")),
                .ConversationText = TextValue(reader("ConversationText")),
                .ConversationEntryCount = Convert.ToInt32(reader("ConversationEntryCount"), CultureInfo.InvariantCulture),
                .Status = TextValue(reader("Status")),
                .Priority = TextValue(reader("Priority")),
                .ClosedBy = NullableInt(reader("ClosedBy")),
                .ClosedOn = NullableDate(reader("ClosedOn")),
                .FirstResponseOn = NullableDate(reader("FirstResponseOn")),
                .ReporterUserID = Convert.ToInt32(reader("ReporterUserID"), CultureInfo.InvariantCulture),
                .AssignedSupportUserID = NullableInt(reader("AssignedSupportUserID")),
                .CreatedBy = Convert.ToInt32(reader("CreatedBy"), CultureInfo.InvariantCulture),
                .CreatedOn = Convert.ToDateTime(reader("CreatedOn"), CultureInfo.InvariantCulture),
                .UpdatedBy = NullableInt(reader("UpdatedBy")),
                .UpdatedOn = NullableDate(reader("UpdatedOn")),
                .RowVersion = DirectCast(reader("RowVersion"), Byte())
            }
        End Function

        Private Shared Function NullableInt(value As Object) As Integer?
            If value Is Nothing OrElse Convert.IsDBNull(value) Then Return Nothing
            Return Convert.ToInt32(value, CultureInfo.InvariantCulture)
        End Function

        Private Shared Function NullableDate(value As Object) As DateTime?
            If value Is Nothing OrElse Convert.IsDBNull(value) Then Return Nothing
            Return Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        End Function

        Private Shared Function TextValue(value As Object) As String
            Return If(value Is Nothing OrElse Convert.IsDBNull(value), String.Empty, value.ToString())
        End Function

        Private Shared Function CurrentUserId() As Integer
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then Return SessionState.Current.Value.UserID
            Return 0
        End Function

        Private Shared Function ResolveSupportUserIdForNewIssue(registrationId As Integer) As Integer
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return 0

            Dim userSupportId As Integer = 0
            Dim applicationSupportId As Integer = 0
            If DataAccess.GetHelpDeskRouting(registrationId, userSupportId, applicationSupportId) Then
                SessionState.UpdateHelpDeskRouting(userSupportId, applicationSupportId)
            End If

            Dim session = SessionState.Current.Value
            If session.IsApplicationAdminRole OrElse session.IsCompanyAdminRole Then Return session.HDApplicationSupport
            Return session.HDUserSupport
        End Function

        Private Shared Function CurrentAuthorName() As String
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                Dim session = SessionState.Current.Value
                Dim name = (session.FirstName & " " & session.LastName).Trim()
                If name <> String.Empty Then Return name
            End If
            Return "User " & CurrentUserId().ToString(CultureInfo.InvariantCulture)
        End Function

        Private Shared Function FormatConversationEntry(record As IssueRecord, author As String, body As String) As String
            Dim recipient = "SUPPORT"
            Dim sender = author
            If IsCurrentUserSupport() AndAlso record IsNot Nothing AndAlso record.ReporterUserID > 0 Then
                recipient = GetUserDisplayName(record.RegistrationID, record.ReporterUserID)
                sender = "SUPPORT"
            End If

            Return "[" & DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) & " UTC]" & Environment.NewLine &
                     "TO:   " & recipient & Environment.NewLine &
                   "FROM: " & sender & Environment.NewLine &
                     Environment.NewLine &
                   body.Trim()
        End Function

        Private Shared Function IsCurrentUserSupport() As Boolean
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return False
            Dim session = SessionState.Current.Value
            Return session.IsApplicationAdminRole OrElse session.IsCompanyAdminRole
        End Function

        Private Shared Function GetUserDisplayName(registrationId As Integer, userId As Integer) As String
            Using conn As New SqlConnection(ConnectionString())
                conn.Open()
                Using cmd As New SqlCommand("SELECT TOP 1 NULLIF(LTRIM(RTRIM(FirstLast)), '') FROM dbo.FW_Users WHERE RegistrationID = @RegistrationID AND UserID = @UserID", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not IsDBNull(result) Then Return result.ToString()
                End Using
            End Using

            Return "SUPPORT"
        End Function
    End Class
End Namespace