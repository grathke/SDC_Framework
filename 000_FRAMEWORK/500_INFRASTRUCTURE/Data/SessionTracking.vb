Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Globalization
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>
    ''' Who signed in, when, and when they stopped.
    '''
    ''' HEALTH_DASHBOARD_SPEC.md section 6. Until this existed nothing recorded a successful login:
    ''' the audit trail could prove who changed a row but not when they signed in, and "who is
    ''' using this right now" had no answer at all.
    '''
    ''' **One open session per process.** The id is held in memory from Begin to End, so a process
    ''' closes the row it opened rather than guessing which is its own. Switch User ends the
    ''' session and begins another, because it is a different person doing different things under
    ''' different permissions - one row covering both would be unreadable.
    '''
    ''' **Written immediately, both ends.** The start rides the sign-in, which is already talking
    ''' to the database; the end runs on the way out, where a queue would not be flushed in time on
    ''' every path. Neither throws: a failure to record a session must never stop somebody working.
    ''' </summary>
    Public Module SessionTracking

        ''' <summary>
        ''' The row this process opened, or zero when there is none.
        '''
        ''' Held rather than looked up. A process that searched for "my session" would have to
        ''' guess from user and time, and two people signing in as the same user within a second of
        ''' each other would make that guess wrong in the way nobody ever notices.
        ''' </summary>
        Private currentSessionId As Integer = 0

        Private ReadOnly gate As New Object()

        ''' <summary>How a session ended, and how much its end time can be trusted.</summary>
        Public Enum EndReason
            ''' <summary>The person left properly. Exact.</summary>
            [Exit]
            ''' <summary>VirtualUI reported the browser gone. Late by the disconnect grace.</summary>
            Disconnect
            ''' <summary>Closed by a reconciler because the process is gone. Last activity only.</summary>
            Crash
        End Enum

        ''' <summary>
        ''' Opens a session row for somebody who has just signed in.
        '''
        ''' Called from SessionStarter, which is the single place that runs everything a sign-in
        ''' does once the person is known - so Switch User gets this without knowing about it.
        '''
        ''' Ends any session this process still has open first. Reaching here with one already open
        ''' means Switch User, and leaving the previous row open would show one person connected
        ''' twice for ever.
        ''' </summary>
        Public Sub Begin(userId As Integer, registrationId As Integer, roleId As Integer)
            If userId <= 0 Then Return

            Try
                SyncLock gate
                    If currentSessionId > 0 Then
                        CloseSession(currentSessionId, EndReason.[Exit])
                        currentSessionId = 0
                    End If

                    Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                        conn.Open()

                        Using cmd As New SqlCommand(
                            "INSERT INTO dbo.FW_Session " &
                            "  (UserID, RegistrationID, RoleID, SessionKind, MachineName, AppVersion, ProcessID) " &
                            "OUTPUT INSERTED.SessionID " &
                            "VALUES (@UserID, @RegistrationID, @RoleID, @SessionKind, @MachineName, @AppVersion, @ProcessID)", conn)

                            cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId

                            cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value =
                                If(registrationId > 0, CType(registrationId, Object), DBNull.Value)

                            cmd.Parameters.Add("@RoleID", SqlDbType.Int).Value =
                                If(roleId > 0, CType(roleId, Object), DBNull.Value)

                            cmd.Parameters.Add("@SessionKind", SqlDbType.VarChar, 20).Value =
                                If(Program.InBrowserSession, "Thinfinity", "Desktop")

                            cmd.Parameters.Add("@MachineName", SqlDbType.VarChar, 100).Value = SafeMachineName()
                            cmd.Parameters.Add("@AppVersion", SqlDbType.VarChar, 40).Value = SafeAppVersion()
                            cmd.Parameters.Add("@ProcessID", SqlDbType.Int).Value = SafeProcessId()

                            Dim inserted = cmd.ExecuteScalar()
                            If inserted IsNot Nothing AndAlso Not Convert.IsDBNull(inserted) Then
                                currentSessionId = Convert.ToInt32(inserted, CultureInfo.InvariantCulture)
                            End If
                        End Using
                    End Using
                End SyncLock

            Catch ex As Exception
                Telemetry.Error(ex, "SessionTracking.Begin", Telemetry.FaultOrigin.Swallowed)
            End Try
        End Sub

        ''' <summary>
        ''' Closes this process's session.
        '''
        ''' Called on the way out, from the same paths that flush telemetry - Main's Finally and
        ''' both VirtualUI close handlers. Safe to call more than once: the second call finds
        ''' nothing open and does nothing, which matters because the forced-exit timer and the
        ''' ordinary close can both fire.
        ''' </summary>
        Public Sub [End](reason As EndReason)
            Try
                SyncLock gate
                    If currentSessionId <= 0 Then Return

                    CloseSession(currentSessionId, reason)
                    currentSessionId = 0
                End SyncLock

            Catch ex As Exception
                ' Nothing useful can be done about it here, and this runs while the process is
                ' already leaving. Recording it would need a flush that may never come.
            End Try
        End Sub

        ''' <summary>
        ''' Writes the end, and derives the last activity from what was already being counted.
        '''
        ''' LastActivityOn comes from FW_UsageCounter rather than from a write per click. The
        ''' counters bucket by hour, so this is the hour somebody last did something rather than
        ''' the minute - good enough to tell an afternoon at the desk from an afternoon away, which
        ''' is the question Active duration answers.
        '''
        ''' Guarded on EndedOn being null so a session closed by the reconciler is not reopened and
        ''' closed again with a worse reason.
        ''' </summary>
        Private Sub CloseSession(sessionId As Integer, reason As EndReason)
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()

                Using cmd As New SqlCommand(
                    "UPDATE s SET " &
                    "  EndedOn = SYSUTCDATETIME(), " &
                    "  EndReason = @Reason, " &
                    "  LastActivityOn = (SELECT MAX(u.HourUtc) FROM dbo.FW_UsageCounter u " &
                    "                    WHERE u.HourUtc >= s.StartedOn " &
                    "                      AND ISNULL(u.RegistrationID, -1) = ISNULL(s.RegistrationID, -1)) " &
                    "FROM dbo.FW_Session s " &
                    "WHERE s.SessionID = @ID AND s.EndedOn IS NULL", conn)

                    cmd.Parameters.Add("@ID", SqlDbType.Int).Value = sessionId
                    cmd.Parameters.Add("@Reason", SqlDbType.VarChar, 20).Value = reason.ToString()

                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' Closes sessions left open by a process that is gone.
        '''
        ''' A killed process - Task Manager, a reboot, the machine going down - never reaches End,
        ''' so its row would show somebody connected for ever. Run at startup, where a process that
        ''' has just started is proof that the ones before it are not running.
        '''
        ''' Only this machine's rows. Another server's open sessions may be perfectly alive, and
        ''' closing them from here would report people as gone while they are working.
        ''' </summary>
        Public Sub CloseAbandonedSessions()
            Try
                Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                    conn.Open()

                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_Session " &
                        "SET EndedOn = ISNULL(LastActivityOn, StartedOn), EndReason = 'Crash' " &
                        "WHERE EndedOn IS NULL " &
                        "  AND MachineName = @MachineName " &
                        "  AND ISNULL(ProcessID, 0) <> @ProcessID", conn)

                        cmd.Parameters.Add("@MachineName", SqlDbType.VarChar, 100).Value = SafeMachineName()
                        cmd.Parameters.Add("@ProcessID", SqlDbType.Int).Value = SafeProcessId()

                        Dim closed = cmd.ExecuteNonQuery()
                        If closed > 0 Then
                            ' The end time is the last thing they were known to be doing, not now.
                            ' Dating an abandoned session to the moment somebody else started the
                            ' application would invent hours nobody was there.
                            Program.Log("Closed " & closed.ToString(CultureInfo.InvariantCulture) &
                                        " abandoned session(s) from this machine")
                        End If
                    End Using
                End Using

            Catch ex As Exception
                Telemetry.Error(ex, "SessionTracking.CloseAbandonedSessions", Telemetry.FaultOrigin.Swallowed)
            End Try
        End Sub

        Private Function SafeMachineName() As String
            Try
                Return Environment.MachineName
            Catch
                Return String.Empty
            End Try
        End Function

        Private Function SafeAppVersion() As String
            Try
                Return Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            Catch
                Return String.Empty
            End Try
        End Function

        Private Function SafeProcessId() As Integer
            Try
                Return Diagnostics.Process.GetCurrentProcess().Id
            Catch
                Return 0
            End Try
        End Function

    End Module
End Namespace
