Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>
    ''' Everything the health page reads, in one round trip.
    '''
    ''' A separate file rather than more of DataAccess, following HelpDeskDataAccess and
    ''' MessagingDataAccess: this is one page's reporting, it is read-only apart from the
    ''' acknowledge, and it is the only code in the application that deliberately ignores the
    ''' registration predicate.
    '''
    ''' **It reports across every registration, on purpose.** HEALTH_DASHBOARD_SPEC.md section 5
    ''' records why and what makes it safe: the page is App Admin only, and it shows counts and
    ''' fault signatures, never record values. A customer-facing version would be a different page
    ''' with the predicate in place, not a flag on this one.
    '''
    ''' **One command, five result sets.** Five separate queries would be five round trips to say
    ''' one thing, and the panels could then disagree with each other because each would see a
    ''' slightly different moment. The window is computed once in SQL and every result set is cut
    ''' against the same value.
    ''' </summary>
    Public NotInheritable Class HealthDataAccess

        Private Sub New()
        End Sub

        ''' <summary>
        ''' The weights behind the needle, from HEALTH_DASHBOARD_SPEC.md section 3.
        '''
        ''' They were chosen before anybody had watched the number move, and the spec says to expect
        ''' to change them once there is a month of real data. They are here, together, so that is
        ''' one edit rather than a hunt.
        ''' </summary>
        Public Const SavePenaltyMax As Double = 50
        Public Const FaultPenaltyMax As Double = 30
        Public Const FallbackPenaltyMax As Double = 20

        ''' <summary>
        ''' Days for a fault's weight to halve. A fault from three weeks ago that has not recurred
        ''' is not current ill health, and without decay the needle only ever falls - which would
        ''' make it decoration rather than a reading.
        ''' </summary>
        Public Const FaultHalfLifeDays As Double = 7

        ''' <summary>Occurrences of unacknowledged fault, decayed, that costs the full fault penalty.</summary>
        Public Const FaultPressureAtMax As Double = 40

        ''' <summary>Fallbacks per 100 audited operations that costs the full fallback penalty.</summary>
        Public Const FallbackRateAtMax As Double = 10

        Public NotInheritable Class ActivityCount
            Public Property OperationType As String = String.Empty
            Public Property Count As Integer
        End Class

        ''' <summary>
        ''' A run of failed sign-ins against one name.
        '''
        ''' Grouped by name and reason rather than listed one per row: the pattern is what matters,
        ''' and forty rows of the same person mistyping their password says less than one row
        ''' saying forty.
        ''' </summary>
        Public NotInheritable Class LoginFailure
            Public Property AttemptedUserName As String = String.Empty
            Public Property Reason As String = String.Empty
            Public Property Attempts As Integer
            Public Property LastAttempt As Date
            Public Property RegistrationName As String = String.Empty
        End Class

        Public NotInheritable Class RegistrationRow
            Public Property ID As Integer
            Public Property Name As String = String.Empty
        End Class

        ''' <summary>
        ''' What Find cost, per page, over the window.
        '''
        ''' Two averages and two maxima, never merged. Section 9: a custom-SQL page applies its QBE
        ''' filters client-side, so its database time stays flat while its perceived time grows
        ''' with the table - and one blended number would hide exactly that.
        ''' </summary>
        Public NotInheritable Class SearchTiming
            Public Property PageName As String = String.Empty
            Public Property Searches As Integer
            Public Property DbAverage As Double
            Public Property DbMax As Integer
            Public Property PerceivedAverage As Double
            Public Property PerceivedMax As Integer

            ''' <summary>
            ''' Time spent outside the database - binding, hiding, fitting, painting, and on a
            ''' custom-SQL page the client-side QBE filtering.
            ''' </summary>
            Public ReadOnly Property ClientAverage As Double
                Get
                    Return Math.Max(0, PerceivedAverage - DbAverage)
                End Get
            End Property

            ''' <summary>
            ''' Where the time is going, which decides whether Query Store is even the right tool.
            '''
            ''' Query Store sees the query and nothing else. On a page whose time is client-side it
            ''' would show a fast, consistent query and send somebody down the wrong path entirely,
            ''' so the page says which case this is rather than recommending a tool blindly.
            ''' </summary>
            Public ReadOnly Property Diagnosis As String
                Get
                    If Searches = 0 Then Return String.Empty
                    If PerceivedAverage < 400 Then Return "fine"
                    If DbAverage >= PerceivedAverage * 0.6 Then Return "the query is slow"
                    Return "not the database - the time is client-side"
                End Get
            End Property
        End Class

        Public NotInheritable Class FaultLine
            Public Property ErrorLogID As Integer
            Public Property ExceptionType As String = String.Empty
            Public Property PageName As String = String.Empty
            Public Property Context As String = String.Empty
            Public Property OccurrenceCount As Integer
            Public Property LastSeen As Date
            Public Property Acknowledged As Boolean
            Public Property Origin As String = String.Empty
            Public Property Resolved As Boolean
            Public Property Resolution As String = String.Empty
            Public Property RecurredAfterResolved As Boolean

            ''' <summary>"2h ago", "3d ago" - the age as somebody would say it.</summary>
            Public ReadOnly Property Age As String
                Get
                    Dim span = Date.UtcNow - LastSeen
                    If span.TotalMinutes < 1 Then Return "just now"
                    If span.TotalMinutes < 60 Then Return CInt(span.TotalMinutes).ToString(CultureInfo.InvariantCulture) & "m ago"
                    If span.TotalHours < 24 Then Return CInt(span.TotalHours).ToString(CultureInfo.InvariantCulture) & "h ago"
                    Return CInt(span.TotalDays).ToString(CultureInfo.InvariantCulture) & "d ago"
                End Get
            End Property

            ''' <summary>What the fault is, in one line - type and where it was caught.</summary>
            Public ReadOnly Property Headline As String
                Get
                    Dim where = If(String.IsNullOrWhiteSpace(Context), PageName, Context)
                    If String.IsNullOrWhiteSpace(where) Then Return ExceptionType
                    Return ExceptionType & " - " & where
                End Get
            End Property
        End Class

        ''' <summary>
        ''' Everything on the page, as at one moment.
        '''
        ''' HasData is false when the window holds nothing at all. That is a real answer and not a
        ''' failure: a fresh installation, or a period nobody worked in. The gauge shows an
        ''' unpopulated dial rather than a confident zero, which would read as "everything is
        ''' broken" when it means "nothing has been measured".
        ''' </summary>
        Public NotInheritable Class HealthSnapshot
            Public Property WindowDays As Integer
            Public Property TakenAtUtc As Date = Date.UtcNow

            ''' <summary>Zero means every registration, which is the page's normal state.</summary>
            Public Property RegistrationID As Integer

            ''' <summary>
            ''' Whether SQL Server's Query Store is recording on this database.
            '''
            ''' A fact about the installation's health rather than a suggestion, which is why it is
            ''' shown rather than recommended. Query Store is a flight recorder: it captures every
            ''' query's text, its execution plan and its timings, and it is SQL Server's own - we
            ''' write nothing and maintain nothing. Switched off, none of that is being kept, and
            ''' switching it on after something turns slow gives no baseline and no record of when
            ''' the regression began, which is most of its value.
            ''' </summary>
            Public Property QueryStoreState As String = String.Empty

            Public Property SaveTotal As Integer
            Public Property SaveSucceeded As Integer

            Public Property FaultsUnacknowledged As Integer
            Public Property FaultsTotal As Integer
            Public Property FaultPressure As Double

            Public Property FallbackCount As Integer
            Public Property AuditedOperations As Integer

            Public Property Activity As New List(Of ActivityCount)()
            Public Property NeedsAttention As New List(Of FaultLine)()

            ''' <summary>
            ''' Every registration, for the scope selector.
            '''
            ''' Carried on the snapshot rather than fetched separately, because a second query
            ''' returning a dozen names is a second round trip on every open of the page to
            ''' populate a list that does not change while it is looked at.
            ''' </summary>
            Public Property Registrations As New List(Of RegistrationRow)()

            ''' <summary>What Find cost, per page. Empty until the counters have something.</summary>
            Public Property SearchTimings As New List(Of SearchTiming)()

            ''' <summary>Failed sign-ins in the window, worst first.</summary>
            Public Property LoginFailures As New List(Of LoginFailure)()

            Public Property Failed As Boolean
            Public Property FailureMessage As String = String.Empty

            Public ReadOnly Property HasData As Boolean
                Get
                    Return SaveTotal > 0 OrElse FaultsTotal > 0 OrElse FallbackCount > 0
                End Get
            End Property

            Public ReadOnly Property SaveSuccessRate As Double
                Get
                    If SaveTotal <= 0 Then Return 1
                    Return SaveSucceeded / CDbl(SaveTotal)
                End Get
            End Property

            Public ReadOnly Property FallbackRatePer100 As Double
                Get
                    If AuditedOperations <= 0 Then Return 0
                    Return (FallbackCount / CDbl(AuditedOperations)) * 100.0
                End Get
            End Property

            ''' <summary>
            ''' 100 minus penalties, floored at zero. Three inputs, each capped at its own maximum so
            ''' one bad input cannot swamp the other two and leave the number saying only one thing.
            ''' </summary>
            ''' <summary>
            ''' What failed saves are costing. Nothing clears this but time: a failed save is a
            ''' fact inside the window, and nothing marks one resolved.
            ''' </summary>
            Public ReadOnly Property SavePenalty As Double
                Get
                    If Not HasData Then Return 0
                    Return (1.0 - SaveSuccessRate) * SavePenaltyMax
                End Get
            End Property

            ''' <summary>
            ''' What unacknowledged faults are costing. This is the one that recovers on being
            ''' dealt with - acknowledging or fixing removes a fault from the sum outright.
            ''' </summary>
            Public ReadOnly Property FaultPenalty As Double
                Get
                    If Not HasData Then Return 0
                    Return Math.Min(FaultPenaltyMax, (FaultPressure / FaultPressureAtMax) * FaultPenaltyMax)
                End Get
            End Property

            ''' <summary>What fallbacks are costing. Ages out of the window like saves.</summary>
            Public ReadOnly Property FallbackPenalty As Double
                Get
                    If Not HasData Then Return 0
                    Return Math.Min(FallbackPenaltyMax, (FallbackRatePer100 / FallbackRateAtMax) * FallbackPenaltyMax)
                End Get
            End Property

            ''' <summary>
            ''' 100 minus penalties, floored at zero. Three inputs, each capped at its own maximum so
            ''' one bad input cannot swamp the other two and leave the number saying only one thing.
            '''
            ''' The three are properties rather than locals so the page can itemise the same
            ''' arithmetic instead of recomputing it. A breakdown that does not add up to the
            ''' needle is worse than no breakdown.
            ''' </summary>
            Public ReadOnly Property Score As Double
                Get
                    If Not HasData Then Return 0
                    Return Math.Max(0, 100.0 - SavePenalty - FaultPenalty - FallbackPenalty)
                End Get
            End Property
        End Class

        ''' <summary>
        ''' Reads the whole page. Never throws: a reporting page that takes the application down
        ''' when its own query fails is worse than one that says it could not read.
        ''' </summary>
        Public Shared Function GetSnapshot(windowDays As Integer,
                                           Optional registrationId As Integer = 0) As HealthSnapshot
            Dim days = Math.Max(1, windowDays)
            Dim snapshot As New HealthSnapshot With {
                .WindowDays = days,
                .RegistrationID = Math.Max(0, registrationId)
            }

            Try
                Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                    conn.Open()

                    Using cmd As New SqlCommand(SnapshotSql, conn)
                        cmd.Parameters.Add("@Days", SqlDbType.Int).Value = days
                        cmd.Parameters.Add("@HalfLife", SqlDbType.Float).Value = FaultHalfLifeDays

                        ' Zero means every registration, which is this page's normal state. Passed
                        ' as NULL rather than 0 so the predicate reads as "no filter" in SQL
                        ' instead of as a registration that does not exist.
                        cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value =
                            If(snapshot.RegistrationID > 0, CType(snapshot.RegistrationID, Object), DBNull.Value)

                        Using reader = cmd.ExecuteReader()
                            ReadSaveTotals(reader, snapshot)

                            If reader.NextResult() Then ReadFaultTotals(reader, snapshot)
                            If reader.NextResult() Then ReadFallbackTotals(reader, snapshot)
                            If reader.NextResult() Then ReadActivity(reader, snapshot)
                            If reader.NextResult() Then ReadNeedsAttention(reader, snapshot)
                            If reader.NextResult() Then ReadRegistrations(reader, snapshot)
                            If reader.NextResult() AndAlso reader.Read() Then
                                snapshot.QueryStoreState = SafeString(reader, "QueryStoreState")
                            End If
                            If reader.NextResult() Then ReadSearchTimings(reader, snapshot)
                            If reader.NextResult() Then ReadLoginFailures(reader, snapshot)
                        End Using
                    End Using
                End Using

            Catch ex As Exception
                snapshot.Failed = True
                snapshot.FailureMessage = ex.Message
                Telemetry.Error(ex, "HealthDataAccess.GetSnapshot")
            End Try

            Return snapshot
        End Function

        Private Shared Sub ReadSaveTotals(reader As SqlDataReader, snapshot As HealthSnapshot)
            If Not reader.Read() Then Return

            snapshot.SaveTotal = SafeInt(reader, "SaveTotal")
            snapshot.SaveSucceeded = SafeInt(reader, "SaveSucceeded")
            snapshot.AuditedOperations = SafeInt(reader, "AuditedOperations")
        End Sub

        Private Shared Sub ReadFaultTotals(reader As SqlDataReader, snapshot As HealthSnapshot)
            If Not reader.Read() Then Return

            snapshot.FaultsUnacknowledged = SafeInt(reader, "Unacknowledged")
            snapshot.FaultsTotal = SafeInt(reader, "FaultsTotal")
            snapshot.FaultPressure = SafeDouble(reader, "Pressure")
        End Sub

        Private Shared Sub ReadFallbackTotals(reader As SqlDataReader, snapshot As HealthSnapshot)
            If Not reader.Read() Then Return

            snapshot.FallbackCount = SafeInt(reader, "FallbackCount")
        End Sub

        Private Shared Sub ReadActivity(reader As SqlDataReader, snapshot As HealthSnapshot)
            While reader.Read()
                snapshot.Activity.Add(New ActivityCount With {
                    .OperationType = SafeString(reader, "OperationType"),
                    .Count = SafeInt(reader, "Total")
                })
            End While
        End Sub

        Private Shared Sub ReadLoginFailures(reader As SqlDataReader, snapshot As HealthSnapshot)
            While reader.Read()
                snapshot.LoginFailures.Add(New LoginFailure With {
                    .AttemptedUserName = SafeString(reader, "AttemptedUserName"),
                    .Reason = SafeString(reader, "Reason"),
                    .Attempts = SafeInt(reader, "Attempts"),
                    .LastAttempt = SafeDate(reader, "LastAttempt"),
                    .RegistrationName = SafeString(reader, "RegistrationName")
                })
            End While
        End Sub

        Private Shared Sub ReadSearchTimings(reader As SqlDataReader, snapshot As HealthSnapshot)
            While reader.Read()
                snapshot.SearchTimings.Add(New SearchTiming With {
                    .PageName = SafeString(reader, "PageName"),
                    .Searches = SafeInt(reader, "Searches"),
                    .DbAverage = SafeDouble(reader, "DbAverage"),
                    .DbMax = SafeInt(reader, "DbMax"),
                    .PerceivedAverage = SafeDouble(reader, "PerceivedAverage"),
                    .PerceivedMax = SafeInt(reader, "PerceivedMax")
                })
            End While
        End Sub

        Private Shared Sub ReadRegistrations(reader As SqlDataReader, snapshot As HealthSnapshot)
            While reader.Read()
                snapshot.Registrations.Add(New RegistrationRow With {
                    .ID = SafeInt(reader, "RegistrationID"),
                    .Name = SafeString(reader, "RegName")
                })
            End While
        End Sub

        Private Shared Sub ReadNeedsAttention(reader As SqlDataReader, snapshot As HealthSnapshot)
            While reader.Read()
                snapshot.NeedsAttention.Add(New FaultLine With {
                    .ErrorLogID = SafeInt(reader, "ErrorLogID"),
                    .ExceptionType = SafeString(reader, "ExceptionType"),
                    .PageName = SafeString(reader, "PageName"),
                    .Context = SafeString(reader, "Context"),
                    .OccurrenceCount = SafeInt(reader, "OccurrenceCount"),
                    .LastSeen = SafeDate(reader, "LastSeen"),
                    .Acknowledged = SafeBool(reader, "Acknowledged"),
                    .Origin = SafeString(reader, "Origin"),
                    .Resolved = SafeBool(reader, "Resolved"),
                    .Resolution = SafeString(reader, "Resolution"),
                    .RecurredAfterResolved = SafeBool(reader, "RecurredAfterResolved")
                })
            End While
        End Sub

        ''' <summary>Everything recorded about one fault, including what the list does not carry.</summary>
        Public NotInheritable Class FaultDetail
            Public Property ErrorLogID As Integer
            Public Property Fingerprint As String = String.Empty
            Public Property ExceptionType As String = String.Empty
            Public Property PageName As String = String.Empty
            Public Property Context As String = String.Empty
            Public Property Message As String = String.Empty
            Public Property StackTrace As String = String.Empty
            Public Property Origin As String = String.Empty
            Public Property SessionKind As String = String.Empty
            Public Property OccurrenceCount As Integer
            Public Property FirstSeen As Date
            Public Property LastSeen As Date
            Public Property MachineName As String = String.Empty
            Public Property AppVersion As String = String.Empty
            Public Property Found As Boolean
        End Class

        ''' <summary>
        ''' Reads one fault in full, for the detail window.
        '''
        ''' A second round trip, deliberately. StackTrace is nvarchar(max) and the list shows twenty
        ''' rows: carrying every stack trace on every refresh would be the page's largest read by
        ''' far, to display none of it. This one runs when somebody actually opens a fault.
        ''' </summary>
        Public Shared Function GetFaultDetail(errorLogId As Integer) As FaultDetail
            Dim detail As New FaultDetail With {.ErrorLogID = errorLogId}
            If errorLogId <= 0 Then Return detail

            Try
                Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                    conn.Open()

                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ErrorLogID, Fingerprint, ExceptionType, ISNULL(PageName, '') AS PageName, " &
                        "  ISNULL(Context, '') AS Context, ISNULL(Message, '') AS Message, " &
                        "  ISNULL(StackTrace, '') AS StackTrace, ISNULL(Origin, '') AS Origin, " &
                        "  ISNULL(SessionKind, '') AS SessionKind, OccurrenceCount, FirstSeen, LastSeen, " &
                        "  ISNULL(MachineName, '') AS MachineName, ISNULL(AppVersion, '') AS AppVersion " &
                        "FROM dbo.FW_ErrorLog WHERE ErrorLogID = @ID", conn)

                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = errorLogId

                        Using reader = cmd.ExecuteReader()
                            If Not reader.Read() Then Return detail

                            detail.Fingerprint = SafeString(reader, "Fingerprint")
                            detail.ExceptionType = SafeString(reader, "ExceptionType")
                            detail.PageName = SafeString(reader, "PageName")
                            detail.Context = SafeString(reader, "Context")
                            detail.Message = SafeString(reader, "Message")
                            detail.StackTrace = SafeString(reader, "StackTrace")
                            detail.Origin = SafeString(reader, "Origin")
                            detail.SessionKind = SafeString(reader, "SessionKind")
                            detail.OccurrenceCount = SafeInt(reader, "OccurrenceCount")
                            detail.FirstSeen = SafeDate(reader, "FirstSeen")
                            detail.LastSeen = SafeDate(reader, "LastSeen")
                            detail.MachineName = SafeString(reader, "MachineName")
                            detail.AppVersion = SafeString(reader, "AppVersion")
                            detail.Found = True
                        End Using
                    End Using
                End Using

            Catch ex As Exception
                Telemetry.Error(ex, "HealthDataAccess.GetFaultDetail")
            End Try

            Return detail
        End Function

        ''' <summary>
        ''' Marks a fault as looked at, which removes it from the score outright.
        '''
        ''' The only write this page performs. Scoped to the one row and to the acting user, and it
        ''' does not soft-delete: an acknowledged fault is still a fault that happened, and the
        ''' record of it should survive somebody deciding it is known.
        ''' </summary>
        ''' <summary>
        ''' Records that a fault has been fixed, and what the fix was.
        '''
        ''' Resolving also acknowledges. A fault somebody has fixed is certainly one they have
        ''' seen, and leaving it counting against the health score after it is fixed would be
        ''' perverse - the two flags mean different things but resolution implies the weaker claim.
        '''
        ''' RecurredAfterResolved is deliberately NOT cleared. It is the history of this fault
        ''' having come back before, and that stays true however many times it is fixed
        ''' afterwards - it is the reason somebody should be sceptical of the next fix.
        ''' </summary>
        ''' <summary>
        ''' <paramref name="resolvedSource"/> separates the two ways a fault stops mattering.
        ''' "User" is somebody deciding it is acceptable; "Claude" is the code having been changed.
        ''' Only the second predicts the fault will stop happening, which is why the next
        ''' recurrence should be read very differently depending on which it was.
        ''' </summary>
        Public Shared Function Resolve(errorLogId As Integer,
                                       userId As Integer,
                                       resolution As String,
                                       Optional resolvedSource As String = "User") As Boolean
            If errorLogId <= 0 Then Return False

            Try
                Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                    conn.Open()

                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_ErrorLog " &
                        "SET Resolved = 1, ResolvedBy = @UserID, ResolvedOn = SYSUTCDATETIME(), " &
                        "    Resolution = @Resolution, ResolvedSource = @Source, " &
                        "    Acknowledged = 1, " &
                        "    AcknowledgedBy = ISNULL(AcknowledgedBy, @UserID), " &
                        "    AcknowledgedOn = ISNULL(AcknowledgedOn, SYSUTCDATETIME()) " &
                        "WHERE ErrorLogID = @ID", conn)

                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = errorLogId
                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value =
                            If(userId > 0, CType(userId, Object), DBNull.Value)
                        cmd.Parameters.Add("@Resolution", SqlDbType.NVarChar, 1000).Value =
                            If(String.IsNullOrWhiteSpace(resolution),
                               CType(DBNull.Value, Object),
                               resolution.Trim())
                        cmd.Parameters.Add("@Source", SqlDbType.VarChar, 20).Value =
                            If(String.IsNullOrWhiteSpace(resolvedSource),
                               CType(DBNull.Value, Object),
                               resolvedSource.Trim())

                        Return cmd.ExecuteNonQuery() > 0
                    End Using
                End Using

            Catch ex As Exception
                Telemetry.Error(ex, "HealthDataAccess.Resolve")
                Return False
            End Try
        End Function

        Public Shared Function Acknowledge(errorLogId As Integer, userId As Integer) As Boolean
            If errorLogId <= 0 Then Return False

            Try
                Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                    conn.Open()

                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_ErrorLog " &
                        "SET Acknowledged = 1, AcknowledgedBy = @UserID, AcknowledgedOn = SYSUTCDATETIME() " &
                        "WHERE ErrorLogID = @ID AND Acknowledged = 0", conn)

                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = errorLogId
                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value =
                            If(userId > 0, CType(userId, Object), DBNull.Value)

                        Return cmd.ExecuteNonQuery() > 0
                    End Using
                End Using

            Catch ex As Exception
                Telemetry.Error(ex, "HealthDataAccess.Acknowledge")
                Return False
            End Try
        End Function

        ''' <summary>What happened when the health page asked to change Query Store.</summary>
        Public Class QueryStoreResult
            Public Property Succeeded As Boolean
            Public Property Message As String = String.Empty

            ''' <summary>The state as it stands after the attempt, read back rather than assumed.</summary>
            Public Property State As String = String.Empty
        End Class

        ''' <summary>
        ''' Turns SQL Server's Query Store on or off for this database.
        '''
        ''' **The database name cannot be a parameter.** ALTER DATABASE takes an identifier, not a
        ''' value, so this is the one place here that builds its statement as text. It is built from
        ''' DB_NAME() through QUOTENAME on the server, never from anything typed or passed in -
        ''' there is no input to inject through.
        '''
        ''' **ON also repairs READ_ONLY.** Query Store that has filled its quota stops recording and
        ''' reads as on, which is the worst of the three states because it looks healthy. Turning it
        ''' on names OPERATION_MODE = READ_WRITE explicitly, which puts it back. SIZE_BASED_CLEANUP_MODE
        ''' = AUTO is what stops it happening again: without it, full means stopped for ever.
        '''
        ''' **OFF discards what has been collected.** The history is not archived anywhere; switching
        ''' off throws it away. The page confirms before calling this, and that confirmation is the
        ''' only thing standing between a click and losing it.
        '''
        ''' **It needs ALTER DATABASE permission**, which the application's own login may not have.
        ''' A refusal is reported as a refusal rather than swallowed - a button that appears to work
        ''' and changes nothing is worse than one that says it cannot.
        '''
        ''' The state is read back from sys.database_query_store_options afterwards rather than
        ''' assumed from the statement having succeeded.
        ''' </summary>
        Public Shared Function SetQueryStore(enable As Boolean) As QueryStoreResult
            Dim result As New QueryStoreResult()

            Dim alterSql As String =
                If(enable,
                   "SET QUERY_STORE = ON (" &
                   "OPERATION_MODE = READ_WRITE, " &
                   "QUERY_CAPTURE_MODE = AUTO, " &
                   "MAX_STORAGE_SIZE_MB = 1024, " &
                   "SIZE_BASED_CLEANUP_MODE = AUTO, " &
                   "CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30))",
                   "SET QUERY_STORE = OFF")

            Try
                Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                    conn.Open()

                    Using cmd As New SqlCommand(
                        "DECLARE @stmt nvarchar(max) = " &
                        "  N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' " & alterSql & "';" &
                        "EXEC sp_executesql @stmt;", conn)

                        ' ALTER DATABASE is not instant on a busy database - it waits for the
                        ' database to be quiet enough to change an option. Left at the default
                        ' timeout this reads as a hung page.
                        cmd.CommandTimeout = 60
                        cmd.ExecuteNonQuery()
                    End Using

                    result.Succeeded = True
                    result.Message = If(enable,
                                        "Query Store is now recording.",
                                        "Query Store is off and its history has been discarded.")

                    Using stateCmd As New SqlCommand(
                        "SELECT ISNULL((SELECT TOP 1 actual_state_desc FROM sys.database_query_store_options), " &
                        "              'UNAVAILABLE') AS QueryStoreState;", conn)

                        Dim state = stateCmd.ExecuteScalar()
                        If state IsNot Nothing AndAlso Not Convert.IsDBNull(state) Then
                            result.State = Convert.ToString(state, CultureInfo.InvariantCulture)
                        End If
                    End Using
                End Using

            Catch ex As SqlException
                ' 5011 is the one ALTER DATABASE actually returns - "User does not have permission
                ' to alter database ... the database does not exist, or the database is not in a
                ' state that allows access checks", followed by 5069, "ALTER DATABASE statement
                ' failed". 262 and 15151 are the generic permission refusals and are kept for the
                ' shapes that raise those instead.
                '
                ' The list was 262 and 15151 alone until 2026-09-20, guessed rather than observed,
                ' and a probe login with no ALTER DATABASE proved it wrong on the first try. A
                ' refusal would have shown the raw server text - honest, and far worse than the
                ' sentence written for it.
                '
                ' Named rather than folded into the general failure, because the answer is
                ' completely different: nothing is broken, the login simply cannot do this.
                result.Succeeded = False
                result.Message = If(ex.Number = 5011 OrElse ex.Number = 5069 OrElse
                                    ex.Number = 262 OrElse ex.Number = 15151,
                                    "This database login does not have permission to change Query Store. " &
                                    "It needs ALTER DATABASE on " & SafeDatabaseName() & ".",
                                    "SQL Server refused the change: " & ex.Message)
                Telemetry.Error(ex, "HealthDataAccess.SetQueryStore")

            Catch ex As Exception
                result.Succeeded = False
                result.Message = "The change could not be made: " & ex.Message
                Telemetry.Error(ex, "HealthDataAccess.SetQueryStore")
            End Try

            Return result
        End Function

        ''' <summary>One fault that was dealt with, for the history window.</summary>
        Public Class FixHistoryLine
            Public Property ErrorLogID As Integer
            Public Property ExceptionType As String = String.Empty
            Public Property PageName As String = String.Empty
            Public Property ResolvedOn As Date
            Public Property ResolvedSource As String = String.Empty
            Public Property ResolvedByName As String = String.Empty
            Public Property Resolution As String = String.Empty
            Public Property OccurrenceCount As Integer
            Public Property RecurredAfterResolved As Boolean
            Public Property StillResolved As Boolean
        End Class

        ''' <summary>
        ''' Everything that has been fixed or accepted, newest first.
        '''
        ''' **Not filtered to Resolved = 1.** A fault that came back has its Resolved flag cleared
        ''' by Telemetry while its Resolution text is kept, and that row is the most useful one in
        ''' the list - it is a fix that did not hold. Filtering on the flag would hide exactly the
        ''' entries somebody opens this window to find. ResolvedOn being set is what makes a row
        ''' history; Resolved says whether it is still true.
        '''
        ''' The window is the page's window, and the registration filter is the page's filter, so
        ''' the history is the history of what the page has been showing.
        ''' </summary>
        Public Shared Function GetFixHistory(days As Integer, registrationId As Integer) As List(Of FixHistoryLine)
            Dim lines As New List(Of FixHistoryLine)()

            Try
                Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                    conn.Open()

                    Using cmd As New SqlCommand(
                        "SELECT TOP 200 e.ErrorLogID, e.ExceptionType, ISNULL(e.PageName, '') AS PageName, " &
                        "  e.ResolvedOn, ISNULL(e.ResolvedSource, '') AS ResolvedSource, " &
                        "  ISNULL(u.UserName, '') AS ResolvedByName, " &
                        "  ISNULL(e.Resolution, '') AS Resolution, e.OccurrenceCount, " &
                        "  ISNULL(e.RecurredAfterResolved, 0) AS RecurredAfterResolved, " &
                        "  ISNULL(e.Resolved, 0) AS StillResolved " &
                        "FROM dbo.FW_ErrorLog e " &
                        "LEFT JOIN dbo.FW_Users u ON u.UserId = e.ResolvedBy " &
                        "WHERE e.ResolvedOn IS NOT NULL " &
                        "  AND e.ResolvedOn >= DATEADD(day, -@Days, SYSUTCDATETIME()) " &
                        "  AND ISNULL(e.DeletedFlag, 0) = 0 " &
                        "  AND (@RegistrationID IS NULL OR e.RegistrationID = @RegistrationID) " &
                        "ORDER BY e.ResolvedOn DESC", conn)

                        cmd.Parameters.Add("@Days", SqlDbType.Int).Value = Math.Max(1, days)
                        cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value =
                            If(registrationId > 0, CType(registrationId, Object), DBNull.Value)

                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                lines.Add(New FixHistoryLine With {
                                    .ErrorLogID = SafeInt(reader, "ErrorLogID"),
                                    .ExceptionType = SafeString(reader, "ExceptionType"),
                                    .PageName = SafeString(reader, "PageName"),
                                    .ResolvedOn = SafeDate(reader, "ResolvedOn"),
                                    .ResolvedSource = SafeString(reader, "ResolvedSource"),
                                    .ResolvedByName = SafeString(reader, "ResolvedByName"),
                                    .Resolution = SafeString(reader, "Resolution"),
                                    .OccurrenceCount = SafeInt(reader, "OccurrenceCount"),
                                    .RecurredAfterResolved = SafeBool(reader, "RecurredAfterResolved"),
                                    .StillResolved = SafeBool(reader, "StillResolved")
                                })
                            End While
                        End Using
                    End Using
                End Using

            Catch ex As Exception
                Telemetry.Error(ex, "HealthDataAccess.GetFixHistory")
            End Try

            Return lines
        End Function

        ''' <summary>The database being reported on, for a message that has to name it.</summary>
        Private Shared Function SafeDatabaseName() As String
            Try
                Dim builder As New SqlConnectionStringBuilder(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                Return builder.InitialCatalog
            Catch
                Return "this database"
            End Try
        End Function

        ''' <summary>
        ''' Five result sets, one command, one window.
        '''
        ''' @Cutoff is computed once at the top so every set is cut against exactly the same moment.
        ''' Computing it per statement would let a long-running batch straddle a boundary, and the
        ''' panels would then disagree by a row nobody could account for.
        '''
        ''' Deliberately no registration predicate - see the class summary and spec section 5.
        '''
        ''' The fault pressure sum is done here rather than in VB because it needs every
        ''' unacknowledged row to compute and only one number comes back. POWER(0.5, age/halflife)
        ''' is the decay; an acknowledged fault is excluded rather than decayed, because
        ''' acknowledging is a statement that it is known, not that it is old.
        ''' </summary>
        ''' <summary>
        ''' "@RegistrationID IS NULL OR x.RegistrationID = @RegistrationID" is repeated rather than
        ''' built by string concatenation, so the command text is a constant and the same plan is
        ''' reused whichever way the filter is set.
        '''
        ''' A fault during login or startup has no registration at all - FW_ErrorLog.RegistrationID
        ''' is nullable for exactly that reason - so filtering to one registration correctly hides
        ''' those. They are still visible with the filter off, which is the page's normal state.
        ''' </summary>
        Private Const RegistrationFilter As String =
            "(@RegistrationID IS NULL OR {0}.RegistrationID = @RegistrationID)"

        Private Shared ReadOnly SnapshotSql As String =
            "DECLARE @Cutoff datetime2(0) = DATEADD(day, -@Days, SYSUTCDATETIME());" &
            vbCrLf &
            "SELECT " &
            "  COUNT(*) AS SaveTotal, " &
            "  SUM(CASE WHEN ISNULL(SaveSucceeded, 0) = 1 THEN 1 ELSE 0 END) AS SaveSucceeded, " &
            "  (SELECT COUNT(*) FROM dbo.FW_AuditTrail a2 " &
            "   WHERE a2.LoggedOn >= @Cutoff AND ISNULL(a2.DeletedFlag, 0) = 0 " &
            "     AND " & Scoped("a2") & ") AS AuditedOperations " &
            "FROM dbo.FW_AuditTrail a " &
            "WHERE a.LoggedOn >= @Cutoff AND a.Phase = 'AfterSave' AND ISNULL(a.DeletedFlag, 0) = 0 " &
            "  AND " & Scoped("a") & ";" &
            vbCrLf &
            "SELECT " &
            "  SUM(CASE WHEN ISNULL(Acknowledged, 0) = 0 THEN 1 ELSE 0 END) AS Unacknowledged, " &
            "  COUNT(*) AS FaultsTotal, " &
            "  ISNULL(SUM(CASE WHEN ISNULL(Acknowledged, 0) = 0 " &
            "                  THEN OccurrenceCount * POWER(0.5, " &
            "                       DATEDIFF(hour, LastSeen, SYSUTCDATETIME()) / 24.0 / @HalfLife) " &
            "                  ELSE 0 END), 0) AS Pressure " &
            "FROM dbo.FW_ErrorLog e " &
            "WHERE e.LastSeen >= @Cutoff AND ISNULL(e.DeletedFlag, 0) = 0 " &
            "  AND " & Scoped("e") & ";" &
            vbCrLf &
            "SELECT COUNT(*) AS FallbackCount " &
            "FROM dbo.FW_FallbackUsageLog f " &
            "WHERE f.LoggedOn >= @Cutoff AND ISNULL(f.DeletedFlag, 0) = 0 " &
            "  AND " & Scoped("f") & ";" &
            vbCrLf &
            "SELECT OperationType, COUNT(*) AS Total " &
            "FROM dbo.FW_AuditTrail t " &
            "WHERE t.LoggedOn >= @Cutoff AND t.Phase = 'AfterSave' AND ISNULL(t.DeletedFlag, 0) = 0 " &
            "  AND t.OperationType IS NOT NULL AND LTRIM(RTRIM(t.OperationType)) <> '' " &
            "  AND " & Scoped("t") & " " &
            "GROUP BY t.OperationType ORDER BY COUNT(*) DESC;" &
            vbCrLf &
            "SELECT TOP 20 n.ErrorLogID, n.ExceptionType, ISNULL(n.PageName, '') AS PageName, " &
            "  ISNULL(n.Context, '') AS Context, n.OccurrenceCount, n.LastSeen, " &
            "  ISNULL(n.Acknowledged, 0) AS Acknowledged, ISNULL(n.Origin, '') AS Origin, " &
            "  ISNULL(n.Resolved, 0) AS Resolved, ISNULL(n.Resolution, '') AS Resolution, " &
            "  ISNULL(n.RecurredAfterResolved, 0) AS RecurredAfterResolved " &
            "FROM dbo.FW_ErrorLog n " &
            "WHERE n.LastSeen >= @Cutoff AND ISNULL(n.DeletedFlag, 0) = 0 " &
            "  AND ISNULL(n.Resolved, 0) = 0 " &
            "  AND " & Scoped("n") & " " &
            "ORDER BY ISNULL(n.Acknowledged, 0), n.LastSeen DESC;" &
            vbCrLf &
            "SELECT RegistrationID, ISNULL(RegName, '') AS RegName " &
            "FROM dbo.FW_Registration ORDER BY RegName;" &
            vbCrLf &
            "SELECT ISNULL((SELECT TOP 1 actual_state_desc FROM sys.database_query_store_options), " &
            "              'UNAVAILABLE') AS QueryStoreState;" &
            vbCrLf &
            "SELECT TOP 12 u.PageName, " &
            "  SUM(u.EventCount) AS Searches, " &
            "  CASE WHEN SUM(u.EventCount) = 0 THEN 0 " &
            "       ELSE SUM(u.DbMillisTotal) * 1.0 / SUM(u.EventCount) END AS DbAverage, " &
            "  ISNULL(MAX(u.DbMillisMax), 0) AS DbMax, " &
            "  CASE WHEN SUM(u.EventCount) = 0 THEN 0 " &
            "       ELSE SUM(u.PerceivedMillisTotal) * 1.0 / SUM(u.EventCount) END AS PerceivedAverage, " &
            "  ISNULL(MAX(u.PerceivedMillisMax), 0) AS PerceivedMax " &
            "FROM dbo.FW_UsageCounter u " &
            "WHERE u.HourUtc >= @Cutoff AND u.Kind = 'Search' AND ISNULL(u.DeletedFlag, 0) = 0 " &
            "  AND " & Scoped("u") & " " &
            "GROUP BY u.PageName " &
            "ORDER BY MAX(u.PerceivedMillisMax) DESC;" &
            vbCrLf &
            "SELECT TOP 12 la.AttemptedUserName, la.Reason, COUNT(*) AS Attempts, " &
            "  MAX(la.AttemptedOn) AS LastAttempt, ISNULL(MAX(r.RegName), '') AS RegistrationName " &
            "FROM dbo.FW_LoginAttempt la " &
            "LEFT JOIN dbo.FW_Registration r ON r.RegistrationID = la.RegistrationID " &
            "WHERE la.AttemptedOn >= @Cutoff AND ISNULL(la.DeletedFlag, 0) = 0 " &
            "  AND " & Scoped("la") & " " &
            "GROUP BY la.AttemptedUserName, la.Reason " &
            "ORDER BY COUNT(*) DESC, MAX(la.AttemptedOn) DESC;"

        Private Shared Function Scoped(tableAlias As String) As String
            Return String.Format(CultureInfo.InvariantCulture, RegistrationFilter, tableAlias)
        End Function

        Private Shared Function SafeInt(reader As SqlDataReader, column As String) As Integer
            Dim ordinal = reader.GetOrdinal(column)
            If reader.IsDBNull(ordinal) Then Return 0
            Return Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture)
        End Function

        Private Shared Function SafeDouble(reader As SqlDataReader, column As String) As Double
            Dim ordinal = reader.GetOrdinal(column)
            If reader.IsDBNull(ordinal) Then Return 0
            Return Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture)
        End Function

        Private Shared Function SafeString(reader As SqlDataReader, column As String) As String
            Dim ordinal = reader.GetOrdinal(column)
            If reader.IsDBNull(ordinal) Then Return String.Empty
            Return Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)
        End Function

        Private Shared Function SafeBool(reader As SqlDataReader, column As String) As Boolean
            Dim ordinal = reader.GetOrdinal(column)
            If reader.IsDBNull(ordinal) Then Return False
            Return Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture)
        End Function

        Private Shared Function SafeDate(reader As SqlDataReader, column As String) As Date
            Dim ordinal = reader.GetOrdinal(column)
            If reader.IsDBNull(ordinal) Then Return Date.UtcNow
            Return Convert.ToDateTime(reader.GetValue(ordinal), CultureInfo.InvariantCulture)
        End Function

    End Class
End Namespace
