Option Strict On
Option Explicit On

Imports System.Collections.Concurrent
Imports System.Globalization
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>
    ''' Where a fault goes to be counted.
    '''
    ''' Three kinds of thing arrive here. An exception that reached one of the two global handlers,
    ''' which is already shown to the user and written to startup.log; a fault from a deliberate
    ''' silent catch, where something cosmetic failed and the page carried on; and anything a caller
    ''' wants recorded without stopping.
    '''
    ''' Two rules govern all of it, and both are about not making a bad day worse.
    '''
    ''' It never throws. A telemetry call sits inside a catch block, on the failure path, and an
    ''' exception raised here would replace a small fault with a large one - or, in a global
    ''' handler, recurse. Every entry point swallows everything, including its own failures.
    '''
    ''' It never blocks. Faults arrive in bursts, and a burst is exactly when the application can
    ''' least afford a round trip per event. Notes are queued in memory and flushed together.
    ''' </summary>
    Public Module Telemetry

        ''' <summary>Where a fault was caught, which is not the same as where it happened.</summary>
        Public Enum FaultOrigin
            ''' <summary>Reached Application.ThreadException - thrown on the UI thread.</summary>
            ThreadException
            ''' <summary>Reached AppDomain.UnhandledException - thrown anywhere else.</summary>
            UnhandledException
            ''' <summary>Caught and deliberately swallowed, so the user saw nothing.</summary>
            Swallowed
        End Enum

        Private NotInheritable Class Note
            Public Property Fingerprint As String = String.Empty
            Public Property ExceptionType As String = String.Empty
            Public Property PageName As String = String.Empty
            Public Property Context As String = String.Empty
            Public Property Message As String = String.Empty
            Public Property StackTrace As String = String.Empty
            Public Property Origin As String = String.Empty
            Public Property SessionKind As String = String.Empty
            Public Property RegistrationID As Integer?
            Public Property UserID As Integer?
            Public Property MachineName As String = String.Empty
            Public Property AppVersion As String = String.Empty
        End Class

        ''' <summary>
        ''' Queued rather than written where it happens.
        '''
        ''' Bounded, because the case this exists for is a fault repeating faster than it can be
        ''' written. Past the cap, notes are dropped rather than allowed to grow without limit -
        ''' the thousandth copy of a fault tells you nothing the first one did not.
        ''' </summary>
        Private Const QueueCap As Integer = 500
        Private ReadOnly pending As New ConcurrentQueue(Of Note)()

        ''' <summary>Guards the flush, so two threads cannot write the same notes twice.</summary>
        Private ReadOnly flushLock As New Object()
        Private flushing As Boolean

        ''' <summary>
        ''' Drains the queue on its own, so a fault in a session that runs all afternoon does not
        ''' wait for the window to close to be written.
        '''
        ''' Started on the first note rather than at type load: an application that never faults
        ''' never starts a timer. A threadpool timer rather than a Forms one, because a fault can
        ''' arrive from a thread that has no message loop.
        ''' </summary>
        Private Const FlushIntervalMs As Integer = 30000
        Private ReadOnly timerLock As New Object()
        Private flushTimer As Timer

        Private Sub EnsureFlushTimer()
            If flushTimer IsNot Nothing Then Return

            SyncLock timerLock
                If flushTimer IsNot Nothing Then Return
                flushTimer = New Timer(Sub() Flush(), Nothing, FlushIntervalMs, FlushIntervalMs)
            End SyncLock
        End Sub

        ''' <summary>
        ''' Set while a flush is running, so a fault raised by telemetry's own database write does
        ''' not queue a note about itself and flush again on the way out.
        ''' </summary>
        <ThreadStatic>
        Private writingTelemetry As Boolean

        ''' <summary>
        ''' Records a fault. Never throws, never blocks, and returns whether it was queued - which
        ''' callers are free to ignore and mostly should.
        ''' </summary>
        ''' <param name="ex">The exception. Nothing is tolerated and produces no note.</param>
        ''' <param name="context">
        ''' Where the catch is, in the form "Type.Method". This is what makes a swallowed fault
        ''' findable, since the stack of a caught exception starts where it was thrown and not
        ''' where it was handled.
        ''' </param>
        ''' <param name="origin">How it was caught. Swallowed unless a global handler says otherwise.</param>
        Public Function [Error](ex As Exception,
                                context As String,
                                Optional origin As FaultOrigin = FaultOrigin.Swallowed) As Boolean
            If ex Is Nothing Then Return False
            If writingTelemetry Then Return False

            Try
                If pending.Count >= QueueCap Then Return False

                Dim note = BuildNote(ex, context, origin)
                If note Is Nothing Then Return False

                pending.Enqueue(note)
                EnsureFlushTimer()
                Return True
            Catch
                ' The whole point. A failure to record a failure is not worth a second failure.
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Writes everything queued, and clears the queue whether or not the write succeeded.
        '''
        ''' Called on a timer, at shutdown, and by anything that wants the record on disk before it
        ''' does something risky. Safe to call when there is nothing to write.
        ''' </summary>
        Public Sub Flush()
            If pending.IsEmpty Then Return

            SyncLock flushLock
                If flushing Then Return
                flushing = True
            End SyncLock

            Try
                writingTelemetry = True

                Dim batch As New List(Of Note)()
                Dim note As Note = Nothing
                While batch.Count < QueueCap AndAlso pending.TryDequeue(note)
                    batch.Add(note)
                End While

                If batch.Count = 0 Then Return

                WriteBatch(batch)
            Catch
                ' Dropped on purpose. The queue has already been emptied, so a database that is
                ' down cannot make the application accumulate notes until it runs out of memory.
            Finally
                writingTelemetry = False
                SyncLock flushLock
                    flushing = False
                End SyncLock
            End Try
        End Sub

        ''' <summary>
        ''' One statement for the whole batch, and an upsert rather than an insert.
        '''
        ''' A fault already seen increments its count and moves LastSeen; a new one is inserted.
        ''' MERGE by fingerprint does both in one round trip per note, against a unique index, and
        ''' the whole batch shares a connection.
        ''' </summary>
        Private Sub WriteBatch(batch As List(Of Note))
            ' An empty database name means "the configured one" - the builder only overrides the
            ' catalog when it is given a name. DataAccess.ConnectionString itself is private, and
            ' this is the accessor that exists rather than a new one widening that surface.
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()

                For Each note In batch
                    Try
                        Using cmd As New SqlCommand(
                            "MERGE dbo.FW_ErrorLog AS target " &
                            "USING (SELECT @Fingerprint AS Fingerprint) AS source " &
                            "ON target.Fingerprint = source.Fingerprint " &
                            "WHEN MATCHED THEN UPDATE SET " &
                            "  OccurrenceCount = target.OccurrenceCount + 1, " &
                            "  LastSeen = SYSUTCDATETIME(), " &
                            "  Message = @Message, " &
                            "  StackTrace = @StackTrace, " &
                            "  RegistrationID = @RegistrationID, " &
                            "  UserID = @UserID, " &
                            "  SessionKind = @SessionKind, " &
                            "  AppVersion = @AppVersion " &
                            "WHEN NOT MATCHED THEN INSERT " &
                            "  (Fingerprint, ExceptionType, PageName, Context, Message, StackTrace, " &
                            "   Origin, SessionKind, RegistrationID, UserID, MachineName, AppVersion) " &
                            "  VALUES (@Fingerprint, @ExceptionType, @PageName, @Context, @Message, @StackTrace, " &
                            "          @Origin, @SessionKind, @RegistrationID, @UserID, @MachineName, @AppVersion);", conn)

                            cmd.Parameters.AddWithValue("@Fingerprint", note.Fingerprint)
                            cmd.Parameters.AddWithValue("@ExceptionType", Clip(note.ExceptionType, 200))
                            cmd.Parameters.AddWithValue("@PageName", NullIfEmpty(Clip(note.PageName, 100)))
                            cmd.Parameters.AddWithValue("@Context", NullIfEmpty(Clip(note.Context, 200)))
                            cmd.Parameters.AddWithValue("@Message", NullIfEmpty(Clip(note.Message, 1000)))
                            cmd.Parameters.AddWithValue("@StackTrace", NullIfEmpty(note.StackTrace))
                            cmd.Parameters.AddWithValue("@Origin", note.Origin)
                            cmd.Parameters.AddWithValue("@SessionKind", NullIfEmpty(note.SessionKind))
                            cmd.Parameters.AddWithValue("@RegistrationID", If(note.RegistrationID.HasValue,
                                                                              CType(note.RegistrationID.Value, Object),
                                                                              DBNull.Value))
                            cmd.Parameters.AddWithValue("@UserID", If(note.UserID.HasValue,
                                                                      CType(note.UserID.Value, Object),
                                                                      DBNull.Value))
                            cmd.Parameters.AddWithValue("@MachineName", NullIfEmpty(Clip(note.MachineName, 100)))
                            cmd.Parameters.AddWithValue("@AppVersion", NullIfEmpty(Clip(note.AppVersion, 40)))

                            cmd.ExecuteNonQuery()
                        End Using
                    Catch
                        ' One bad note does not cost the rest of the batch.
                    End Try
                Next
            End Using
        End Sub

        Private Function BuildNote(ex As Exception, context As String, origin As FaultOrigin) As Note
            Dim exceptionType = ex.GetType().FullName
            Dim resolvedContext = If(context, String.Empty).Trim()
            Dim stack = If(ex.StackTrace, String.Empty)

            Dim session = SessionState.Current

            Return New Note With {
                .Fingerprint = ComputeFingerprint(exceptionType, resolvedContext, stack),
                .ExceptionType = exceptionType,
                .PageName = ResolvePageName(resolvedContext),
                .Context = resolvedContext,
                .Message = ex.Message,
                .StackTrace = stack,
                .Origin = origin.ToString(),
                .SessionKind = ResolveSessionKind(),
                .RegistrationID = If(session.HasValue, CType(session.Value.RegistrationID, Integer?), Nothing),
                .UserID = If(session.HasValue, CType(session.Value.UserID, Integer?), Nothing),
                .MachineName = SafeMachineName(),
                .AppVersion = SafeAppVersion()
            }
        End Function

        ''' <summary>
        ''' What makes two occurrences the same fault.
        '''
        ''' Exception type, where it was caught, and the top frames of the stack with their line
        ''' numbers removed. Line numbers are dropped on purpose: a fault that moves down a file
        ''' because somebody added a comment above it is the same fault, and a fingerprint that
        ''' changed on every edit would fill the table with one row per build.
        '''
        ''' The message is not part of it. Two failures of the same call with different record ids
        ''' in the text are one problem, and including the message would make them two.
        ''' </summary>
        Private Function ComputeFingerprint(exceptionType As String, context As String, stack As String) As String
            Dim material As New StringBuilder()
            material.Append(exceptionType).Append("|"c).Append(context).Append("|"c)

            Dim frames = If(stack, String.Empty).
                Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(line) StripLineNumber(line.Trim())).
                Take(5)

            For Each frame In frames
                material.Append(frame).Append(";"c)
            Next

            Using hasher = MD5.Create()
                Dim bytes = hasher.ComputeHash(Encoding.UTF8.GetBytes(material.ToString()))
                Dim hex As New StringBuilder(32)
                For Each b In bytes
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture))
                Next
                Return hex.ToString()
            End Using
        End Function

        ''' <summary>Removes the ":line 123" a stack frame carries when a pdb is present.</summary>
        Private Function StripLineNumber(frame As String) As String
            Dim marker = frame.LastIndexOf(":line ", StringComparison.OrdinalIgnoreCase)
            If marker < 0 Then Return frame
            Return frame.Substring(0, marker)
        End Function

        ''' <summary>
        ''' The page out of a context written as "FW_Employees_U.LoadRecord". A context that does
        ''' not name a type leaves this empty rather than guessing.
        ''' </summary>
        Private Function ResolvePageName(context As String) As String
            If String.IsNullOrWhiteSpace(context) Then Return String.Empty

            Dim separator = context.IndexOf("."c)
            If separator <= 0 Then Return String.Empty

            Return context.Substring(0, separator)
        End Function

        Private Function ResolveSessionKind() As String
            Try
                Return If(Program.InBrowserSession, "Thinfinity", "Desktop")
            Catch
                Return String.Empty
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
                Return Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            Catch
                Return String.Empty
            End Try
        End Function

        Private Function Clip(value As String, length As Integer) As String
            Dim text = If(value, String.Empty).Trim()
            If text.Length <= length Then Return text
            Return text.Substring(0, length)
        End Function

        Private Function NullIfEmpty(value As String) As Object
            If String.IsNullOrWhiteSpace(value) Then Return DBNull.Value
            Return value
        End Function

    End Module

End Namespace
