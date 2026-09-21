Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Text.Json
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>
    ''' What happened while the database could not be reached, written where it can be.
    '''
    ''' **The gap this closes.** Everything else this application records about its own health is
    ''' recorded in the database, which works for every failure except the one that matters most.
    ''' A database nobody can reach cannot be told that nobody can reach it. On 2026-09-21 the
    ''' server was down at startup, three operations failed, the login screen took 71 seconds to
    ''' appear - and the only trace was a line in startup.log that nothing reads.
    '''
    ''' **An outage is one event, not a stream of failures.** Each process keeps a single record
    ''' and moves its LastSeen, rather than appending. A hundred failed calls during one outage are
    ''' one thing that went wrong.
    '''
    ''' **One file per process, named by process id.** Over Thinfinity every session is its own
    ''' process on the same server, all writing to the same folder. A shared file would need a
    ''' lock, and an outage is the worst imaginable moment to be contending for one.
    '''
    ''' **Anything that can reach the database clears all of them, not just its own.** A process
    ''' that died during the outage left a file nobody else owns, and the next process to start is
    ''' the one that can finally write it.
    '''
    ''' Nothing here throws. A failure to record a failure must not become a second failure.
    ''' </summary>
    Public Module OutageJournal

        ''' <summary>
        ''' What one process saw while the database was unreachable.
        '''
        ''' Deliberately small and flat. It is written during an outage, when the machine may be
        ''' about to be restarted, and the less there is to serialize the less there is to get
        ''' half-written.
        ''' </summary>
        Public Class OutageRecord
            Public Property FirstSeenUtc As Date
            Public Property LastSeenUtc As Date
            Public Property Failures As Integer
            Public Property Context As String = String.Empty
            Public Property LastError As String = String.Empty
            Public Property MachineName As String = String.Empty
            Public Property AppVersion As String = String.Empty
            Public Property ProcessID As Integer
        End Class

        Private Const FilePrefix As String = "outage-"
        Private Const FileSuffix As String = ".json"

        ''' <summary>
        ''' The fingerprint every outage row shares, so repeats land on one row.
        '''
        ''' A constant rather than a hash of the message. The message carries the dates and the
        ''' duration, which differ every time - fingerprinting those would put a new row on the
        ''' page for every outage and lose the only number that matters, which is how many there
        ''' have been.
        ''' </summary>
        Private Const OutageFingerprint As String = "database-unreachable"

        Private ReadOnly gate As New Object()

        ''' <summary>
        ''' SQL Server error numbers that mean "the server could not be reached".
        '''
        ''' Not every database failure is an outage. A bad column name, a missing permission or a
        ''' deadlock are faults somebody should look at, and they belong in Needs Attention. These
        ''' say the server was not there at all, which is one event covered by one outage row.
        '''
        ''' 4060 and 18456 are deliberately absent. The server answered and refused - a wrong
        ''' database name or a bad password is a configuration fault, and reporting it as an outage
        ''' would send somebody to look at a network that is working perfectly.
        ''' </summary>
        Private ReadOnly UnreachableNumbers As New HashSet(Of Integer)(
            {-2, 2, 40, 53, 233, 258, 1231, 10053, 10054, 10060, 10061, 11001})

        ''' <summary>
        ''' Whether this failure means the database could not be reached.
        '''
        ''' Checks the inner exceptions too: a connection failure routinely arrives wrapped, and
        ''' testing only the outer one lets an outage through as an ordinary fault.
        ''' </summary>
        Public Function IsUnreachable(ex As Exception) As Boolean
            Dim current = ex

            While current IsNot Nothing
                Dim sqlError = TryCast(current, SqlException)
                If sqlError IsNot Nothing AndAlso UnreachableNumbers.Contains(sqlError.Number) Then Return True

                current = current.InnerException
            End While

            Return False
        End Function

        ''' <summary>
        ''' Notes that the database could not be reached.
        '''
        ''' Called from the places that already catch a database failure. Cheap enough to call on
        ''' every one: it touches a file in the application's own folder and nothing else.
        ''' </summary>
        Public Sub Note(context As String, ex As Exception)
            Try
                SyncLock gate
                    Dim path = PathForThisProcess()
                    Dim record = ReadRecord(path)

                    If record Is Nothing Then
                        record = New OutageRecord With {
                            .FirstSeenUtc = Date.UtcNow,
                            .MachineName = SafeMachineName(),
                            .AppVersion = SafeAppVersion(),
                            .ProcessID = SafeProcessId()
                        }
                    End If

                    record.LastSeenUtc = Date.UtcNow
                    record.Failures += 1
                    record.Context = If(context, String.Empty)
                    record.LastError = If(ex Is Nothing, String.Empty, ex.Message)

                    File.WriteAllText(path, JsonSerializer.Serialize(record))
                End SyncLock

            Catch
                ' Deliberately silent. This is the fallback for a failure; it has no fallback of
                ' its own, and a throw here would surface inside somebody else's Catch block.
            End Try
        End Sub

        ''' <summary>
        ''' Writes every journal on this machine and deletes it, on a connection already open.
        '''
        ''' Called from Telemetry's flush, which runs every thirty seconds and has just proved the
        ''' database is reachable. That proof is the whole trigger - there is no point asking
        ''' whether the outage is over, because a successful write is the answer.
        '''
        ''' **The row arrives already resolved.** By the time it can be written the outage is over
        ''' and there is nothing for anybody to do, so it would be wrong in a list of things that
        ''' need attention. It belongs in the history, where past events live. If it happens again,
        ''' Telemetry's upsert clears the resolved flag and it returns to the list - and that is
        ''' correct, because what needs attention then is the pattern rather than the incident.
        '''
        ''' **The file is deleted only after the row is written.** A delete that ran first would
        ''' lose the outage if the connection died mid-statement, which is not a far-fetched
        ''' failure thirty seconds after a database came back.
        ''' </summary>
        Friend Sub FlushAll(conn As SqlConnection)
            Dim files As String()

            Try
                files = Directory.GetFiles(Folder(), FilePrefix & "*" & FileSuffix)
            Catch
                Return
            End Try

            If files Is Nothing OrElse files.Length = 0 Then Return

            For Each path In files
                Try
                    Dim record = ReadRecord(path)
                    If record Is Nothing Then
                        ' Unreadable, half-written, or from a version that wrote something else.
                        ' Deleting it is right: it will never become readable, and left alone it
                        ' would be retried on every flush for ever.
                        File.Delete(path)
                        Continue For
                    End If

                    WriteOutageRow(conn, record)
                    File.Delete(path)

                Catch
                    ' Left in place on purpose. The next flush tries again, and a journal that
                    ' cannot be written is better kept than quietly dropped.
                End Try
            Next
        End Sub

        ''' <summary>
        ''' One fault row for an outage, marked resolved because it resolved itself.
        '''
        ''' Written through Telemetry.UpsertFault rather than a MERGE of its own, so the
        ''' fingerprint rule, the recur-on-repeat rule and LastSeen only moving forward are all
        ''' the same ones every other fault gets.
        '''
        ''' The resolve runs second because the upsert sets Resolved = 0 on a match, which is
        ''' exactly right for an ordinary fault and exactly wrong here.
        ''' </summary>
        Private Sub WriteOutageRow(conn As SqlConnection, record As OutageRecord)
            Dim seconds = CInt(Math.Max(0, (record.LastSeenUtc - record.FirstSeenUtc).TotalSeconds))

            Dim message = "The database could not be reached for " &
                          Describe(seconds) & ", from " &
                          record.FirstSeenUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) &
                          " to " &
                          record.LastSeenUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) &
                          " UTC. " & record.Failures.ToString(CultureInfo.InvariantCulture) &
                          " operation(s) failed, the last of them " & record.Context & ": " & record.LastError

            Dim note As New Telemetry.Note With {
                .OccurredUtc = record.LastSeenUtc,
                .Fingerprint = OutageFingerprint,
                .ExceptionType = "Database unreachable",
                .PageName = String.Empty,
                .Context = "OutageJournal",
                .Message = message,
                .StackTrace = String.Empty,
                .Origin = Telemetry.FaultOrigin.Swallowed.ToString(),
                .SessionKind = String.Empty,
                .MachineName = record.MachineName,
                .AppVersion = record.AppVersion
            }

            Telemetry.UpsertFault(conn, note)

            Using cmd As New SqlCommand(
                "UPDATE dbo.FW_ErrorLog SET " &
                "  Resolved = 1, ResolvedOn = SYSUTCDATETIME(), ResolvedSource = 'Recovered', " &
                "  Resolution = @Resolution, " &
                "  Acknowledged = 1, " &
                "  AcknowledgedOn = ISNULL(AcknowledgedOn, SYSUTCDATETIME()) " &
                "WHERE Fingerprint = @Fingerprint", conn)

                cmd.Parameters.Add("@Fingerprint", SqlDbType.VarChar, 64).Value = OutageFingerprint
                cmd.Parameters.Add("@Resolution", SqlDbType.NVarChar, 1000).Value =
                    "Recovered on its own. The database became reachable again and this record was " &
                    "written from the local journal kept while it was not."

                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ''' <summary>
        ''' A duration somebody would say out loud.
        '''
        ''' Whole minutes alone made 71 seconds read as "1 minutes", which is wrong twice over -
        ''' ungrammatical, and it throws away the eleven seconds that tell you this was a
        ''' connection timing out rather than a server being restarted. The remainder is kept
        ''' wherever there is one, and the singular is spelled.
        ''' </summary>
        Private Function Describe(seconds As Integer) As String
            If seconds < 60 Then Return Count(seconds, "second")

            If seconds < 3600 Then
                Dim minutes = seconds \ 60
                Dim rest = seconds Mod 60
                Return Count(minutes, "minute") & If(rest = 0, "", " " & Count(rest, "second"))
            End If

            Dim hours = seconds \ 3600
            Dim leftover = (seconds Mod 3600) \ 60
            Return Count(hours, "hour") & If(leftover = 0, "", " " & Count(leftover, "minute"))
        End Function

        Private Function Count(value As Integer, unit As String) As String
            Return value.ToString(CultureInfo.InvariantCulture) & " " & unit & If(value = 1, "", "s")
        End Function

        Private Function Folder() As String
            Return AppContext.BaseDirectory
        End Function

        Private Function PathForThisProcess() As String
            Return Path.Combine(Folder(), FilePrefix & SafeProcessId().ToString(CultureInfo.InvariantCulture) & FileSuffix)
        End Function

        Private Function ReadRecord(path As String) As OutageRecord
            Try
                If Not File.Exists(path) Then Return Nothing

                Dim text = File.ReadAllText(path)
                If String.IsNullOrWhiteSpace(text) Then Return Nothing

                Return JsonSerializer.Deserialize(Of OutageRecord)(text)

            Catch
                Return Nothing
            End Try
        End Function

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
