Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Diagnostics
Imports System.Linq
Imports System.Threading

Namespace SDC.Framework

    ''' <summary>
    ''' Counts trips to the database, so "how many queries does opening this page make?" can be
    ''' answered by reading rather than by arguing.
    '''
    ''' Through Microsoft.Data.SqlClient's own DiagnosticListener rather than by wrapping anything.
    ''' DataAccess opens a connection in 169 places, and a counter that had to be added to each of
    ''' them would be wrong the first time somebody wrote the hundred and seventieth. This sees
    ''' every command the client executes, including the ones inside helpers nobody remembers
    ''' calling - which are exactly the ones a count is for.
    '''
    ''' A command, not a connection: connections are pooled and reused, so opening one costs
    ''' nothing and counting them would answer a different question.
    '''
    ''' Costs a counter increment per command when nothing is listening, and the listener is only
    ''' attached when something asks for a count.
    ''' </summary>
    Friend NotInheritable Class DbTripCounter

        Private Sub New()
        End Sub

        Private Shared ReadOnly gate As New Object()
        Private Shared subscription As IDisposable = Nothing
        Private Shared listenerSubscription As IDisposable = Nothing
        Private Shared commandCount As Long = 0
        Private Shared attached As Boolean = False
        Private Shared trace As List(Of String) = Nothing

        ''' <summary>
        ''' Starts collecting what each command was, not just how many there were.
        '''
        ''' A count says a page open costs 31 trips. It does not say that four of them asked the
        ''' same question, which is the part worth fixing - so the text is collected and grouped.
        ''' One trace at a time: two pages opening at once would interleave, and a mixed list is
        ''' worse than no list.
        ''' </summary>
        Friend Shared Sub BeginTrace()
            SyncLock gate
                trace = New List(Of String)()
            End SyncLock
        End Sub

        ''' <summary>
        ''' The commands seen since BeginTrace, grouped and ordered by how often each was run.
        ''' </summary>
        Friend Shared Function EndTrace() As List(Of String)
            Dim collected As List(Of String)

            SyncLock gate
                collected = trace
                trace = Nothing
            End SyncLock

            Dim summary As New List(Of String)()
            If collected Is Nothing OrElse collected.Count = 0 Then
                Return summary
            End If

            For Each group In collected.GroupBy(Function(text) text, StringComparer.OrdinalIgnoreCase).
                                        OrderByDescending(Function(g) g.Count()).
                                        ThenBy(Function(g) g.Key)
                summary.Add(group.Count().ToString(Globalization.CultureInfo.InvariantCulture) & "x  " & group.Key)
            Next

            Return summary
        End Function

        Private Shared Sub RecordCommandText(payload As Object)
            Try
                Dim collecting As Boolean
                SyncLock gate
                    collecting = trace IsNot Nothing
                End SyncLock
                If Not collecting Then Return

                If payload Is Nothing Then Return

                ' By reflection because the payload is an internal anonymous type. A property that
                ' is renamed in a future SqlClient makes this quietly record nothing, which is the
                ' right failure for a diagnostic: nothing in the log rather than nothing running.
                Dim commandProperty = payload.GetType().GetProperty("Command")
                If commandProperty Is Nothing Then Return

                Dim command = TryCast(commandProperty.GetValue(payload), Common.DbCommand)
                If command Is Nothing Then Return

                Dim text = If(command.CommandText, String.Empty)
                text = System.Text.RegularExpressions.Regex.Replace(text, "\s+", " ").Trim()
                If text.Length > 110 Then text = text.Substring(0, 110)

                SyncLock gate
                    If trace IsNot Nothing Then trace.Add(text)
                End SyncLock
            Catch
            End Try
        End Sub

        ''' <summary>
        ''' Starts counting. Safe to call repeatedly - the listener is attached once.
        ''' </summary>
        Friend Shared Sub EnsureAttached()
            SyncLock gate
                If attached Then Return
                attached = True

                Try
                    listenerSubscription = DiagnosticListener.AllListeners.Subscribe(New ListenerObserver())
                Catch
                    ' No counting rather than no application.
                End Try
            End SyncLock
        End Sub

        Friend Shared ReadOnly Property Count As Long
            Get
                Return Interlocked.Read(commandCount)
            End Get
        End Property

        ''' <summary>
        ''' Whether the listener actually found SqlClient's diagnostic source. A count of zero means
        ''' nothing was executed OR nothing was being watched, and those are very different answers
        ''' to report.
        ''' </summary>
        Friend Shared ReadOnly Property IsCounting As Boolean
            Get
                SyncLock gate
                    Return subscription IsNot Nothing
                End SyncLock
            End Get
        End Property

        Private Class ListenerObserver
            Implements IObserver(Of DiagnosticListener)

            Public Sub OnNext(value As DiagnosticListener) Implements IObserver(Of DiagnosticListener).OnNext
                If value Is Nothing Then Return
                If Not String.Equals(value.Name, "SqlClientDiagnosticListener", StringComparison.Ordinal) Then Return

                SyncLock gate
                    If subscription IsNot Nothing Then Return
                    subscription = value.Subscribe(New CommandObserver())
                End SyncLock
            End Sub

            Public Sub OnCompleted() Implements IObserver(Of DiagnosticListener).OnCompleted
            End Sub

            Public Sub OnError([error] As Exception) Implements IObserver(Of DiagnosticListener).OnError
            End Sub
        End Class

        Private Class CommandObserver
            Implements IObserver(Of KeyValuePair(Of String, Object))

            ''' <summary>
            ''' After, not before. A command that threw still went to the server and still cost a
            ''' trip, and SqlClient raises WriteCommandError for those - so both are counted and a
            ''' failed query is not quietly free.
            ''' </summary>
            Public Sub OnNext(value As KeyValuePair(Of String, Object)) Implements IObserver(Of KeyValuePair(Of String, Object)).OnNext
                If value.Key Is Nothing Then Return

                If value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal) OrElse
                   value.Key.EndsWith("WriteCommandError", StringComparison.Ordinal) Then
                    Interlocked.Increment(commandCount)
                    RecordCommandText(value.Value)
                End If
            End Sub

            Public Sub OnCompleted() Implements IObserver(Of KeyValuePair(Of String, Object)).OnCompleted
            End Sub

            Public Sub OnError([error] As Exception) Implements IObserver(Of KeyValuePair(Of String, Object)).OnError
            End Sub
        End Class

    End Class

End Namespace
