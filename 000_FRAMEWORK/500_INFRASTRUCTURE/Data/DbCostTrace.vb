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

                RaiseSlowFaultIfNeeded(subject, label, wholeTimer.ElapsedMilliseconds)
            Catch
                ' A measurement is never worth a failed page open, or a failed save.
            End Try
        End Sub

        ''' <summary>
        ''' Past this, an operation stops being a log line and becomes a fault.
        '''
        ''' Everything here was already measured and none of it was surfaced: the figures went to a
        ''' file nobody reads unless they already suspect something. A threshold turns the
        ''' measurement into something the health dashboard ranks, without anybody having to go
        ''' looking.
        '''
        ''' 750ms, and the margin is deliberate. Measured on 2026-09-23 the slowest data operation
        ''' was a 297ms conflict save; refreshes ran 16 to 139 and deletes and restores under 125.
        ''' 500 would have been silent too, but every one of those figures is a local network with
        ''' one user on it. On a cloud link each of a save's thirteen round trips carries more
        ''' latency, and a save that honestly takes 550ms there would raise a fault meaning nothing.
        '''
        ''' An alarm that is silent on a healthy system is the only kind worth having. One that
        ''' fires on normal behaviour teaches people to ignore it, and then it is not there for the
        ''' one that matters. Too high costs a missed warning; too low costs the instrument.
        '''
        ''' The right time to tune this is after the move to a cloud link, against real figures from
        ''' it. It is one constant.
        '''
        ''' It sits after the clock has already stopped for any dialog, so this measures work and
        ''' not somebody reading. Before PauseSaveClock existed a refused save would have tripped
        ''' this every time, at 2,441ms of a person looking at a message.
        ''' </summary>
        Friend Const SlowThresholdMilliseconds As Long = 750

        ''' <summary>
        ''' The measurements a page open carries, which this threshold deliberately does not judge.
        '''
        ''' A page open is not a data operation. On the employee browse, 300 of its 350 milliseconds
        ''' are WinForms building and painting a grid-heavy form for the first time in the process -
        ''' measured on 2026-09-23 as ctor=6 load=35 shown=300. Holding that to a database threshold
        ''' would be raising a data fault about rendering, and the first thing anybody did about it
        ''' would be to look in the wrong place.
        '''
        ''' A refresh, a save, a delete and a restore are close to pure data, and slow there always
        ''' means something.
        '''
        ''' **A list of the exempt ones, not of the judged ones**, which is the rule
        ''' HealthDataAccess.UnconfiguredList already follows: a measurement added later is held to
        ''' the threshold until somebody decides otherwise. The other way round, a new kind of
        ''' slowness would be free until it was noticed, and noticing is the whole point.
        ''' </summary>
        Private Shared ReadOnly ExemptLabels As String() = {"Page open"}

        ''' <summary>
        ''' Records a slow data operation as a fault, unless its measurement is exempt.
        '''
        ''' Shared so the browse refresh can use it too. That one writes its own log line through
        ''' FW_Base_B.ReportPostQuery rather than through Report, so a threshold living only in
        ''' Report would have missed the single most data-bound measurement the framework takes.
        ''' </summary>
        Friend Shared Sub RaiseSlowFaultIfNeeded(subject As String, label As String, milliseconds As Long)
            Try
                If milliseconds < SlowThresholdMilliseconds Then Return

                Dim resolvedLabel = If(label, String.Empty).Trim()
                For Each exempt In ExemptLabels
                    If String.Equals(resolvedLabel, exempt, StringComparison.OrdinalIgnoreCase) Then Return
                Next

                Telemetry.Slow(subject, resolvedLabel, milliseconds)
            Catch
                ' Recording that something was slow must never be the reason something else is.
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
