Option Strict On
Option Explicit On

Imports System.Diagnostics
Imports System.Globalization
Imports System.Text

Namespace SDC.Framework

    ''' <summary>
    ''' What a piece of work cost: how long it took, where the time went inside it, and how many
    ''' database round trips it made - with the statements grouped by how often each one ran.
    '''
    ''' One owner for a measurement both base classes were carrying their own copy of. Before
    ''' 2026-09-22 FW_Base_B and FW_Base_U each held the same five fields, the same MarkStep and
    ''' the same twenty-line report, differing only in their comments, and a fourth and fifth copy
    ''' were about to be written for the save and the delete.
    '''
    ''' It reports to startup.log rather than to telemetry. This is a developer finding out where
    ''' the time goes, not a fault anybody should be told about.
    '''
    ''' **A measurement never costs somebody their work.** Every method swallows its own faults,
    ''' and a trace that fails to start returns an instance that quietly does nothing rather than
    ''' Nothing, so no call site needs a guard.
    ''' </summary>
    Friend NotInheritable Class DbCostTrace

        ''' <summary>Restarted at each boundary. It measures the step, not the whole.</summary>
        Private ReadOnly stepTimer As Stopwatch

        ''' <summary>Runs from the first line to the report, and is the only honest total.</summary>
        Private ReadOnly wholeTimer As Stopwatch

        Private ReadOnly steps As New StringBuilder()
        Private ReadOnly label As String
        Private ReadOnly tripsAtStart As Long
        Private ReadOnly countsTrips As Boolean
        Private reported As Boolean

        Private Sub New(label As String, countTrips As Boolean)
            Me.label = If(label, String.Empty)
            Me.countsTrips = countTrips

            Dim [step] As Stopwatch = Nothing
            Dim whole As Stopwatch = Nothing
            Dim baseline As Long = 0

            Try
                If countTrips Then
                    DbTripCounter.EnsureAttached()
                    DbTripCounter.BeginTrace()
                    baseline = DbTripCounter.Count
                End If

                [step] = Stopwatch.StartNew()
                whole = Stopwatch.StartNew()
            Catch
                ' Left as Nothing, which every method below already tolerates.
            End Try

            Me.stepTimer = [step]
            Me.wholeTimer = whole
            Me.tripsAtStart = baseline
        End Sub

        ''' <summary>
        ''' Starts a trace that counts round trips and keeps the statements behind them.
        '''
        ''' The trip counter has one trace at a time, so a second Start replaces the first. That is
        ''' the behavior both base classes already had, and it holds because the work being
        ''' measured does not nest: a page open finishes reporting at Shown, long before anybody
        ''' can press Save.
        ''' </summary>
        Friend Shared Function Start(label As String) As DbCostTrace
            Return New DbCostTrace(label, True)
        End Function

        ''' <summary>
        ''' Starts a timing-only trace, for work whose steps are worth naming but whose trips are
        ''' counted by whatever is already measuring around it.
        ''' </summary>
        Friend Shared Function StartSteps() As DbCostTrace
            Return New DbCostTrace(String.Empty, False)
        End Function

        ''' <summary>The step breakdown as it stands, for a caller that writes its own report.</summary>
        Friend ReadOnly Property Breakdown As StringBuilder
            Get
                Return steps
            End Get
        End Property

        ''' <summary>The whole, start to now. Never the sum of the steps, which is always less.</summary>
        Friend ReadOnly Property ElapsedMilliseconds As Long
            Get
                Return If(wholeTimer Is Nothing, 0L, wholeTimer.ElapsedMilliseconds)
            End Get
        End Property

        ''' <summary>
        ''' Holds the clock while a dialog is on screen, for work that raises one mid-measurement.
        '''
        ''' The whole point of a measurement is that it measures the work. A refused save showed the
        ''' user a list of empty required fields and then reported 2,441ms - almost all of it
        ''' somebody reading a message, and none of it anything the application did. The same fault
        ''' was found on the delete and the restore on 2026-09-22, where placing the trace around the
        ''' click handler read 2,174ms and 4,790ms of confirmation dialogs. Those two were solved by
        ''' starting the trace after the dialog. The save cannot be: its dialogs are raised from
        ''' inside the steps being measured, and one of them - the concurrency overwrite - is
        ''' followed by more work that must still be counted.
        '''
        ''' Pause immediately before the dialog and resume immediately after. A path that returns
        ''' straight after its dialog needs no resume; a paused clock reports what it accumulated.
        ''' Pausing twice, resuming without pausing, or either after Report, all do nothing.
        ''' </summary>
        Friend Sub PauseClock()
            Try
                If wholeTimer IsNot Nothing AndAlso wholeTimer.IsRunning Then wholeTimer.Stop()
                If stepTimer IsNot Nothing AndAlso stepTimer.IsRunning Then stepTimer.Stop()
            Catch
                ' Measuring must never cost somebody their save.
            End Try
        End Sub

        ''' <summary>Starts the clock again after a dialog, for the work that follows it.</summary>
        Friend Sub ResumeClock()
            Try
                If reported Then Return
                If wholeTimer IsNot Nothing AndAlso Not wholeTimer.IsRunning Then wholeTimer.Start()
                If stepTimer IsNot Nothing AndAlso Not stepTimer.IsRunning Then stepTimer.Start()
            Catch
                ' Measuring must never cost somebody their save.
            End Try
        End Sub

        ''' <summary>
        ''' Closes off a step and names it.
        '''
        ''' Steps that cost nothing are left out. A breakdown of fifteen entries, eleven of them
        ''' zero, hides the two that matter.
        ''' </summary>
        Friend Sub Mark(name As String)
            Try
                If stepTimer Is Nothing Then Return

                Dim ms = stepTimer.ElapsedMilliseconds
                If ms > 0 Then
                    Append(name, "=", ms)
                End If

                stepTimer.Restart()
            Catch
                ' Measuring must never cost somebody their Find.
            End Try
        End Sub

        ''' <summary>
        ''' Adds a figure measured by somebody else as a part of the step it sits inside, rather
        ''' than as a step beside it.
        '''
        ''' **Written with a colon, not an equals sign, and this is not cosmetic.** A caller that
        ''' sums the name=value tokens to work out what it could not account for must not count
        ''' these twice. Written as "fetch=597[open=0]" they first broke the token itself, so fetch
        ''' dropped out of the sum and the line claimed other=602 on a refresh where nothing was
        ''' unaccounted for.
        '''
        ''' Zero is printed here where Mark leaves it out, because zero is the finding: an open
        ''' that cost nothing says the connection was pooled, and that is exactly what rules the
        ''' connection out as the cause of a slow first fetch.
        ''' </summary>
        Friend Sub Note(name As String, milliseconds As Integer)
            Try
                Append(name, ":", milliseconds)
            Catch
                ' A missing figure is not worth a failed refresh.
            End Try
        End Sub

        ''' <summary>
        ''' Writes the whole, the breakdown and the trip count, then the statements grouped and
        ''' counted - because the question is not what ran but what ran twice.
        '''
        ''' Reports once. A second call is ignored, so a report queued behind two paths that can
        ''' both reach it cannot produce two lines that disagree.
        '''
        ''' `finalStep` names whatever is still running when the report is reached - "shown" for a
        ''' page open, which is the gap between the last layout step and the page being somebody's
        ''' to type in. Work with nothing left to close off passes nothing.
        ''' </summary>
        Friend Sub Report(subject As String, Optional finalStep As String = Nothing)
            Try
                If reported OrElse wholeTimer Is Nothing Then Return
                reported = True

                If Not String.IsNullOrEmpty(finalStep) Then Mark(finalStep)

                Dim tripText = "not counted"
                If countsTrips AndAlso DbTripCounter.IsCounting Then
                    tripText = (DbTripCounter.Count - tripsAtStart).ToString(CultureInfo.InvariantCulture)
                End If

                Program.Log(label & " " & subject & ": " &
                            wholeTimer.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) &
                            "ms  " & steps.ToString() & "  trips=" & tripText)

                If countsTrips Then
                    For Each line In DbTripCounter.EndTrace()
                        Program.Log("    " & line)
                    Next
                End If
            Catch
                ' A measurement is never worth a failed page open, or a failed save.
            End Try
        End Sub

        Private Sub Append(name As String, separator As String, value As Integer)
            Append(name, separator, CLng(value))
        End Sub

        Private Sub Append(name As String, separator As String, value As Long)
            If steps.Length > 0 Then steps.Append(" ")
            steps.Append(name).Append(separator).Append(value.ToString(CultureInfo.InvariantCulture))
        End Sub

    End Class

End Namespace
