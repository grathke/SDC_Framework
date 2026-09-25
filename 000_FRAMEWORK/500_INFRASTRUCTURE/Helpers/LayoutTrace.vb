Option Strict On
Option Explicit On

Imports System.Text
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' What a page's layout actually came out as, written to the log when it is on screen.
    '''
    ''' The framework could already answer any question about data from a log it had already
    ''' written - DbCostTrace, DbTripCounter, Telemetry, FW_ErrorLog, FW_FallbackUsageLog - and no
    ''' question about layout without running the application again. On 2026-09-23 a grid that was
    ''' visibly half the height of its space took three runs to explain, and the third only
    ''' succeeded because the probe was moved: read from inside the layout method it reported a
    ''' Dock.Fill grid 232 tall in a panel of 360, which cannot be true and was not. It was mid-pass.
    '''
    ''' So this is the layout counterpart of DbCostTrace, and it exists for the same reason: the
    ''' answer should already be in the log by the time somebody asks.
    '''
    ''' **Queued behind the message loop, from Shown.** The layout runs several times before a form
    ''' settles, and every reading taken during one of those passes describes something nobody will
    ''' ever see. Called through BeginInvoke it reports the arrangement the user is looking at.
    '''
    ''' **Once per form per session**, because a resize storm would otherwise fill the log with the
    ''' same line.
    '''
    ''' **Containers only.** A form has hundreds of controls and three or four that decide where
    ''' everything else goes. Logging the labels would bury the splitter.
    ''' </summary>
    Friend Module LayoutTrace

        ''' <summary>Forms already reported, so a re-layout does not write the line again.</summary>
        Private ReadOnly reported As New HashSet(Of String)(StringComparer.Ordinal)

        Private ReadOnly gate As New Object()

        ''' <summary>
        ''' Reports <paramref name="form"/>'s geometry once, from the message loop.
        '''
        ''' Call it from Shown. It queues itself, so the caller does not have to know that a form
        ''' laid out at Shown is not yet the form on screen.
        ''' </summary>
        Friend Sub ReportWhenSettled(form As Form)
            If form Is Nothing OrElse form.IsDisposed Then Return

            Try
                If Not form.IsHandleCreated Then
                    Report(form)
                    Return
                End If

                form.BeginInvoke(New MethodInvoker(Sub() Report(form)))
            Catch
                ' A diagnostic must never be the reason a page fails to open.
            End Try
        End Sub

        Friend Sub Report(form As Form)
            If form Is Nothing OrElse form.IsDisposed Then Return

            Dim key = form.GetType().Name
            SyncLock gate
                If reported.Contains(key) Then Return
                reported.Add(key)
            End SyncLock

            Try
                Dim line As New StringBuilder()
                line.Append("Layout ").Append(key).
                     Append(": client=").Append(form.ClientSize.Width).Append("x").Append(form.ClientSize.Height).
                     Append(" form=").Append(form.Width).Append("x").Append(form.Height).
                     Append(" zoom=").Append(ZoomOf(form)).
                     Append(" autoscale=").Append(form.AutoScaleMode.ToString()).
                     Append(" scaled=").Append(form.CurrentAutoScaleDimensions.Height.ToString("0.0", Globalization.CultureInfo.InvariantCulture)).
                     Append("/").Append(form.AutoScaleDimensions.Height.ToString("0.0", Globalization.CultureInfo.InvariantCulture))

                ' Depth 3 reaches a grid inside a splitter panel inside the form, which is the
                ' deepest arrangement the framework builds. Past that is field-level and noise.
                DescribeChildren(form, line, 1, 3)

                Program.Log(line.ToString())
            Catch
                ' As above. Measuring must never cost somebody their page.
            End Try
        End Sub

        Private Function ZoomOf(form As Form) As String
            Try
                Return PageZoom.CurrentFactor(form).ToString("0.00", Globalization.CultureInfo.InvariantCulture)
            Catch
                Return "n/a"
            End Try
        End Function

        ''' <summary>
        ''' Adds the children that decide size, and recurses into the ones that hold others.
        '''
        ''' A control earns its place by being a container, by being docked, or by being a grid -
        ''' the three ways a control takes space from its siblings. Everything else sits wherever
        ''' those three left room, and saying so for every label would hide them.
        ''' </summary>
        Private Sub DescribeChildren(parent As Control, line As StringBuilder, depth As Integer, maxDepth As Integer)
            If depth > maxDepth OrElse parent Is Nothing Then Return

            ' The area WinForms actually divides between docked children, which is the parent's
            ' client area less its padding and anything else it reserves. When a Dock.Fill control
            ' is smaller than the arithmetic says it should be, this is the number that explains
            ' it - and reading the properties one at a time never will, because the reason may be
            ' a property nobody thought to print.
            Dim display = parent.DisplayRectangle
            If display.Height <> parent.Height OrElse display.Width <> parent.Width OrElse
               display.X <> 0 OrElse display.Y <> 0 Then
                line.Append(" | ").Append(New String("."c, Math.Max(0, depth - 2))).
                     Append("[").Append(ControlLabel(parent)).Append(" display=").
                     Append(display.Width).Append("x").Append(display.Height).
                     Append("@").Append(display.X).Append(",").Append(display.Y).Append("]")
            End If

            For Each child As Control In parent.Controls
                If Not IsWorthReporting(child, depth) Then Continue For

                line.Append(" | ").Append(New String("."c, depth - 1)).
                     Append(ControlLabel(child)).
                     Append("=").Append(child.Width).Append("x").Append(child.Height).
                     Append("@").Append(child.Left).Append(",").Append(child.Top).
                     Append(" dock=").Append(child.Dock.ToString()).
                     Append(If(child.Visible, String.Empty, " HIDDEN"))

                ' The two ways a Dock.Fill control ends up smaller than the space it was given,
                ' with no sibling involved. Added on 2026-09-23 after a grid measured 232 in a
                ' panel of 360 whose only other child was a 38-pixel strip, and the line as it
                ' then stood could not say why - which is the one thing it exists to say.
                If Not child.Padding.Equals(Padding.Empty) Then
                    line.Append(" pad=").Append(child.Padding.Left).Append(",").Append(child.Padding.Top).
                         Append(",").Append(child.Padding.Right).Append(",").Append(child.Padding.Bottom)
                End If

                If Not child.Margin.Equals(New Padding(3)) Then
                    line.Append(" margin=").Append(child.Margin.Left).Append(",").Append(child.Margin.Top).
                         Append(",").Append(child.Margin.Right).Append(",").Append(child.Margin.Bottom)
                End If

                If Not child.MaximumSize.IsEmpty Then
                    line.Append(" max=").Append(child.MaximumSize.Width).Append("x").Append(child.MaximumSize.Height)
                End If

                If Not child.MinimumSize.IsEmpty Then
                    line.Append(" min=").Append(child.MinimumSize.Width).Append("x").Append(child.MinimumSize.Height)
                End If

                If child.Anchor <> (AnchorStyles.Top Or AnchorStyles.Left) Then
                    line.Append(" anchor=").Append(child.Anchor.ToString().Replace(", ", "+"))
                End If

                Dim split = TryCast(child, SplitContainer)
                If split IsNot Nothing Then
                    line.Append(" distance=").Append(split.SplitterDistance).
                         Append(" p1=").Append(split.Panel1.Height).
                         Append(" p2=").Append(split.Panel2.Height).
                         Append(If(split.Panel1Collapsed, " p1Collapsed", String.Empty)).
                         Append(If(split.Panel2Collapsed, " p2Collapsed", String.Empty))
                End If

                Dim qbeSplit = TryCast(child, QbeSplitPanel)
                If qbeSplit IsNot Nothing Then
                    line.Append(" distance=").Append(qbeSplit.SplitterDistance).
                         Append(" p1=").Append(qbeSplit.Panel1.Height).
                         Append(" p2=").Append(qbeSplit.Panel2.Height).
                         Append(If(qbeSplit.Panel1Collapsed, " p1Collapsed", String.Empty))
                End If

                Dim grid = TryCast(child, DataGridView)
                If grid IsNot Nothing Then
                    line.Append(" rows=").Append(grid.Rows.Count).
                         Append(" rowH=").Append(grid.RowTemplate.Height).
                         Append(" headerH=").Append(grid.ColumnHeadersHeight).
                         Append(" fits=").Append(RowsThatFit(grid))
                End If

                DescribeChildren(child, line, depth + 1, maxDepth)
            Next
        End Sub

        ''' <summary>
        ''' How many rows the grid could show at its current height.
        '''
        ''' The number the question is usually really about. A grid showing eleven rows in space
        ''' for twenty is a row cap; a grid showing six in space for six is a layout.
        ''' </summary>
        Private Function RowsThatFit(grid As DataGridView) As Integer
            Dim rowHeight = Math.Max(1, grid.RowTemplate.Height)
            Return Math.Max(0, (grid.Height - grid.ColumnHeadersHeight) \ rowHeight)
        End Function

        ''' <summary>
        ''' Whether a control belongs in the line. At the top two levels, everything does.
        '''
        ''' This started as a list of types, then became "anything docked", and each time the
        ''' control that mattered fell outside it - twice in one afternoon, on the same question.
        ''' A filter on a diagnostic is a bet that you already know what you are looking for, and
        ''' the whole reason to be reading the line is that you do not.
        '''
        ''' So the top two levels report every child. Deeper than that a form's individual fields
        ''' begin, hundreds of them, and none can take space from a docked sibling - so depth is
        ''' the limit rather than type, and it is a limit on volume rather than on what counts.
        ''' </summary>
        Private Function IsWorthReporting(child As Control, depth As Integer) As Boolean
            If child Is Nothing Then Return False
            If depth <= 2 Then Return True
            If child.Dock <> DockStyle.None Then Return True
            If TypeOf child Is SplitContainer OrElse TypeOf child Is SplitterPanel Then Return True
            If TypeOf child Is QbeSplitPanel OrElse TypeOf child.Parent Is QbeSplitPanel Then Return True
            If TypeOf child Is DataGridView Then Return True
            If TypeOf child Is TableLayoutPanel OrElse TypeOf child Is FlowLayoutPanel Then Return True
            Return False
        End Function

        Private Function ControlLabel(child As Control) As String
            If Not String.IsNullOrWhiteSpace(child.Name) Then Return child.Name
            Return child.GetType().Name
        End Function
    End Module
End Namespace
