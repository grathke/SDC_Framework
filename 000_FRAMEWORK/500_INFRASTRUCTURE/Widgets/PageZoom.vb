Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Scales a form's contents from the keyboard, and keeps them centred.
    '''
    ''' F7 smaller, F8 larger, F9 back to normal - three adjacent keys, because a zoom somebody
    ''' reaches for repeatedly should be one hand movement rather than three.
    '''
    ''' Smaller on the left, because the keys are a physical row and a row of controls that
    ''' changes a magnitude should ascend left to right. Every zoom control anywhere puts the minus
    ''' left of the plus, and getting it backwards costs a wrong press every time until it is
    ''' learned.
    '''
    ''' Not F11 or F12: a browser takes those for fullscreen and developer tools before VirtualUI
    ''' ever sees them, and not Shift+F10, which is Windows' own context-menu key. F10 was the
    ''' reset until 2026-09-20 and is better off not being anything - Windows gives it to the menu
    ''' bar, so it arrives already spoken for on a form that has one.
    '''
    ''' Every zoom is applied to a snapshot of the original layout rather than to whatever is on
    ''' screen. Scaling relatively - ten per cent of what is already there - compounds its rounding
    ''' error, and a page pulls itself apart after a dozen presses. The snapshot is taken once, the
    ''' factor is absolute, and going back to 1.0 puts every control back exactly where it started.
    '''
    ''' A browser session scales the whole canvas by itself, which is a different thing: that makes
    ''' the picture bigger, this makes the page bigger inside a window that stays put.
    ''' </summary>
    Public NotInheritable Class PageZoom

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Original size is the floor. F10 undoes an F9 and stops there rather than going on to
        ''' shrink the page inside a window that has nowhere left to follow it.
        ''' </summary>
        Public Const Minimum As Single = 1.0F
        Public Const Maximum As Single = 2.0F
        Public Const Increment As Single = 0.1F

        ''' <summary>
        ''' A page that has something to settle before its zoom changes.
        '''
        ''' Called before anything is scaled, every time the factor actually changes. A browse page
        ''' closes Hot Fields here: the strip widens the window by a fixed amount the snapshot knows
        ''' nothing about, and the two cannot both decide how wide the page is.
        ''' </summary>
        Public Interface IZoomAware
            Sub BeforeZoom(newFactor As Single)
        End Interface

        ''' <summary>
        ''' A control as it was laid out, before anybody zoomed.
        '''
        ''' Bounds and font are enough for most controls. A grid and a split container also carry
        ''' measurements that are not bounds at all - header height, row height, the header's own
        ''' font, where the splitter sits - and scaling a grid's box without them gives bigger text
        ''' in rows too short to hold it.
        ''' </summary>
        Private Structure Original
            Public Bounds As Rectangle
            Public FontSize As Single
            Public Anchoring As AnchorStyles
            Public GridHeaderHeight As Integer
            Public GridRowHeight As Integer
            Public GridHeaderFontSize As Single
            Public GridFixedColumns As Dictionary(Of String, (Width As Integer, MinimumWidth As Integer))
            Public SplitterDistance As Integer
        End Structure

        Private NotInheritable Class State
            Public ReadOnly Layout As New Dictionary(Of Control, Original)()
            Public Factor As Single = 1.0F
            Public DesignSize As Size
            Public DesignMaximum As Size
            Public DesignMinimum As Size
            Public Indicator As Label

            ''' <summary>
            ''' The control the read-out lines up with: centred in the gap below it, and starting at
            ''' its left edge. Nothing means sit above the bottom edge at the far left, which is
            ''' what a page that names no anchor gets.
            '''
            ''' A control rather than two numbers, because the numbers would have to be scaled and
            ''' the control is measured where it actually is - after the zoom has moved it. Storing
            ''' a band captured at 100 per cent and multiplying by the factor was the first attempt
            ''' and is wrong the moment a page is attached while already zoomed.
            ''' </summary>
            Public IndicatorAnchor As Control
        End Class

        Private Shared ReadOnly states As New Dictionary(Of Form, State)()

        ''' <summary>
        ''' Wires the keys up, and applies a starting zoom if one is given.
        '''
        ''' Call it from the form's Load handler, not Shown. Load runs before the first paint, so a
        ''' remembered zoom is what the window opens as; applied from Shown the reader sees the
        ''' unzoomed page for a frame and then watches it jump.
        '''
        ''' The snapshot is safe this early because this application positions controls with
        ''' explicit Location and Size in the constructor - the numbers are already final. Anything
        ''' that needs the form actually laid out, like seating a caption against a control that
        ''' has been narrowed to its content, still belongs in Shown.
        '''
        ''' The exception is a page that re-lays itself out after Load. FW_Base_U collapses hidden
        ''' field rows once it is shown, and snapshots taken before that are of a taller page than
        ''' the real one; it attaches after the collapse and keeps the window invisible until then.
        ''' </summary>
        Public Shared Sub Attach(form As Form, Optional startingFactor As Single = -1.0F)
            If form Is Nothing OrElse states.ContainsKey(form) Then Return

            ' The page's class name, not its caption: a caption is an administrator's to change and
            ' this has to keep pointing at the same window afterwards.
            Dim pageName = form.GetType().Name
            If startingFactor < 0.0F Then startingFactor = PageZoomStore.FactorFor(pageName)

            Dim state As New State With {.DesignSize = form.ClientSize, .DesignMaximum = form.MaximumSize, .DesignMinimum = form.MinimumSize}
            Capture(form, state.Layout)
            states(form) = state

            form.KeyPreview = True

            AddHandler form.KeyDown,
                Sub(sender As Object, e As KeyEventArgs)
                    ' F7 smaller, F8 larger, F9 reset. It was F9, F10, F8 until 2026-09-19 and
                    ' F8, F9, F10 until 2026-09-20. The reset still sits after the pair it resets;
                    ' what changed is that the pair now ascends left to right.
                    Select Case e.KeyCode
                        Case Keys.F7
                            Apply(form, state.Factor - Increment)
                        Case Keys.F8
                            Apply(form, state.Factor + Increment)
                        Case Keys.F9
                            Apply(form, 1.0F)
                        Case Else
                            Return
                    End Select

                    e.Handled = True
                    e.SuppressKeyPress = True
                End Sub

            ' Added after the snapshot, so the zoom never scales its own read-out.
            AddIndicator(form, state)

            AddHandler form.FormClosed,
                Sub(sender As Object, e As FormClosedEventArgs)
                    ' On close, and only when it changed. The store decides that - a page opened
                    ' and closed without a key press writes nothing.
                    PageZoomStore.Remember(pageName, state.Factor)
                    states.Remove(form)
                End Sub

            If Math.Abs(startingFactor - 1.0F) > 0.001F Then
                ' Windows positions a CenterParent or CenterScreen form when it is shown, which
                ' would overrule the centring below. A form opening zoomed places itself.
                form.StartPosition = FormStartPosition.Manual
                Apply(form, startingFactor)
            End If

        End Sub

        ''' <summary>The zoom a form is on, for whatever will eventually remember it.</summary>
        ''' <summary>
        ''' Takes the layout snapshot again, for a page whose controls have deliberately moved
        ''' since it attached.
        '''
        ''' Apply rebuilds every control from the snapshot times the factor, so a page rearranged
        ''' after attaching is put back the moment the zoom changes - and a control added after
        ''' attaching is never scaled at all, because it is not in the snapshot. Re-taking it while
        ''' the page is at design scale answers both.
        '''
        ''' Refused above 100 per cent on purpose: captured while zoomed, the scaled bounds would
        ''' become the new design bounds and every later zoom would compound.
        ''' </summary>
        Public Shared Sub Recapture(form As Form)
            Dim state As State = Nothing
            If form Is Nothing OrElse Not states.TryGetValue(form, state) Then Return
            If Math.Abs(state.Factor - 1.0F) > 0.001F Then Return

            state.Layout.Clear()
            Capture(form, state.Layout)
            state.DesignSize = form.ClientSize
        End Sub
        ''' <summary>
        ''' Lines the zoom read-out up with a control: centred in the gap below it, starting at its
        ''' left edge. Call after Attach. Without one the read-out sits above the bottom edge at the
        ''' far left, as it always did.
        ''' </summary>
        Public Shared Sub SetIndicatorAnchor(form As Form, anchor As Control)
            Dim state As State = Nothing
            If form Is Nothing OrElse Not states.TryGetValue(form, state) Then Return

            state.IndicatorAnchor = anchor
            PlaceIndicator(form, state)
        End Sub
        Public Shared Function CurrentFactor(form As Form) As Single
            Dim state As State = Nothing
            If form Is Nothing OrElse Not states.TryGetValue(form, state) Then Return 1.0F

            Return state.Factor
        End Function

        Public Shared Sub Apply(form As Form, factor As Single)
            Dim state As State = Nothing
            If form Is Nothing OrElse Not states.TryGetValue(form, state) Then Return

            factor = Math.Max(Minimum, Math.Min(Maximum, factor))
            If Math.Abs(factor - state.Factor) < 0.001F Then Return

            Dim aware = TryCast(form, IZoomAware)
            If aware IsNot Nothing Then aware.BeforeZoom(factor)

            state.Factor = factor
            form.SuspendLayout()

            Try
                ResizeWindow(form, state, factor)

                For Each pair In state.Layout
                    Dim control = pair.Key
                    If control Is Nothing OrElse control.IsDisposed Then Continue For

                    Dim was = pair.Value

                    ' Anchoring is switched off for the duration. A right-anchored control is
                    ' repositioned by the layout pass after this one places it, so it lands at the
                    ' edge of the resized window rather than where the snapshot scaled it to -
                    ' which left the page centred, but not squarely.
                    Dim plain = If(Math.Abs(factor - 1.0F) < 0.001F, was.Anchoring, AnchorStyles.Top Or AnchorStyles.Left)
                    If control.Anchor <> plain Then control.Anchor = plain

                    control.Bounds = New Rectangle(CInt(was.Bounds.X * factor),
                                                   CInt(was.Bounds.Y * factor),
                                                   CInt(was.Bounds.Width * factor),
                                                   CInt(was.Bounds.Height * factor))

                    If control.Font IsNot Nothing AndAlso was.FontSize > 0 Then
                        Dim wanted = was.FontSize * factor
                        If Math.Abs(control.Font.Size - wanted) > 0.01F Then
                            control.Font = New Font(control.Font.FontFamily, wanted, control.Font.Style)
                        End If
                    End If

                    ScaleGridInterior(TryCast(control, DataGridView), was, factor)
                    ScaleSplitter(TryCast(control, SplitContainer), was, factor)
                Next

            Finally
                form.ResumeLayout(True)
            End Try

            ' After the layout pass, never inside it. Anchoring runs during ResumeLayout and pulls
            ' every anchored control back to the edge it was measured against, which silently undid
            ' the centring on every press.
            Centre(form, state)

            ' After the centring, which moves every top-level control - this one included.
            PlaceIndicator(form, state)
        End Sub


        ''' <summary>
        ''' Grows the window with its contents.
        '''
        ''' Scaling the controls alone makes a bigger page inside the same window, which is not a
        ''' zoom - it is a page that no longer fits. In a session the main menu pins MaximumSize to
        ''' its opening size so the window cannot be maximised inside a tab; that ceiling has to be
        ''' lifted to zoom past it, and put back when the zoom returns to normal.
        ''' </summary>
        Private Shared Sub ResizeWindow(form As Form, state As State, factor As Single)
            If state.DesignSize.IsEmpty Then Return

            ' Never smaller than the window opened at. Letting it shrink in step with the contents
            ' means the page always exactly fills it, there is never any slack, and nothing can be
            ' centred - which looked like the top of the page being nailed in place. Zooming out
            ' now leaves a margin, and the page sits in the middle of it.
            Dim wanted As New Size(Math.Max(state.DesignSize.Width, CInt(state.DesignSize.Width * factor)),
                                   Math.Max(state.DesignSize.Height, CInt(state.DesignSize.Height * factor)))

            If Math.Abs(factor - 1.0F) < 0.001F Then
                form.MaximumSize = state.DesignMaximum
                form.MinimumSize = state.DesignMinimum
            Else
                ' The maximum moves out of the way so zooming in has somewhere to go. The minimum
                ' stays: the window is deliberately never smaller than it opened.
                If Not state.DesignMaximum.IsEmpty Then
                    form.MaximumSize = New Size(Math.Max(state.DesignMaximum.Width, wanted.Width + 40),
                                                Math.Max(state.DesignMaximum.Height, wanted.Height + 40))
                End If

                form.MinimumSize = state.DesignMinimum
            End If

            ' Never larger than the screen it is on. A page zoomed past the monitor would put its
            ' own edges out of reach.
            Dim room = Screen.FromControl(form).WorkingArea
            form.ClientSize = New Size(Math.Min(wanted.Width, room.Width),
                                       Math.Min(wanted.Height, room.Height))

            ' The window itself is then centred on the screen. Centring the contents inside the
            ' window cannot help here - at this zoom they fill it - and what is off centre is the
            ' window in the space it has.
            form.Location = New Point(room.Left + ((room.Width - form.Width) \ 2),
                                      room.Top + ((room.Height - form.Height) \ 2))
        End Sub

        ''' <summary>
        ''' Puts the scaled page in the middle of the window it is in.
        '''
        ''' Only the top level moves. Everything below it is positioned relative to its parent and
        ''' has already travelled with it.
        ''' </summary>
        Private Shared Sub Centre(form As Form, state As State)
            Dim occupied = Rectangle.Empty

            For Each control As Control In form.Controls
                If Not control.Visible Then Continue For

                occupied = If(occupied = Rectangle.Empty, control.Bounds, Rectangle.Union(occupied, control.Bounds))
            Next

            If occupied = Rectangle.Empty Then Return

            Dim shiftX = ((form.ClientSize.Width - occupied.Width) \ 2) - occupied.Left
            Dim shiftY = ((form.ClientSize.Height - occupied.Height) \ 2) - occupied.Top

            ' Never pushed off the top or the left: a page taller than its window has to start at
            ' the edge and scroll, not centre itself half out of view.
            shiftX = Math.Max(0, shiftX)
            shiftY = Math.Max(0, shiftY)

            If shiftX = 0 AndAlso shiftY = 0 Then Return

            For Each control As Control In form.Controls
                control.Location = New Point(control.Left + shiftX, control.Top + shiftY)
            Next
        End Sub

        ''' <summary>
        ''' The parts of a grid that are not its box: header height, row height, the header's own
        ''' font, and the height of every row already on it.
        '''
        ''' Rows added later - a Find, a refresh - take RowTemplate.Height, so scaling the template
        ''' covers them. The rows already there do not read the template again, so they are set
        ''' one by one.
        ''' </summary>
        Private Shared Sub ScaleGridInterior(grid As DataGridView, was As Original, factor As Single)
            If grid Is Nothing Then Return

            Try
                If was.GridHeaderHeight > 0 Then grid.ColumnHeadersHeight = Math.Max(4, CInt(was.GridHeaderHeight * factor))

                If was.GridRowHeight > 0 Then
                    Dim rowHeight = Math.Max(4, CInt(was.GridRowHeight * factor))
                    grid.RowTemplate.Height = rowHeight
                    For Each row As DataGridViewRow In grid.Rows
                        If row.Height <> rowHeight Then row.Height = rowHeight
                    Next
                End If

                ' Fixed-width columns only. A Fill column already stretches with the grid - the
                ' browse grid shows more of itself as it grows - but a fixed one stays put inside a
                ' wider box, and the QBE grid's three 160px columns left the extra width sitting
                ' empty to the right of the last one.
                '
                ' Minimum before width, whichever way the zoom is going: a smaller minimum is always
                ' accepted, and a larger one raises the width itself before the exact width is set.
                If was.GridFixedColumns IsNot Nothing Then
                    For Each fixedColumn In was.GridFixedColumns
                        If Not grid.Columns.Contains(fixedColumn.Key) Then Continue For

                        Dim column = grid.Columns(fixedColumn.Key)
                        column.MinimumWidth = Math.Max(2, CInt(fixedColumn.Value.MinimumWidth * factor))
                        column.Width = Math.Max(column.MinimumWidth, CInt(fixedColumn.Value.Width * factor))
                    Next
                End If

                Dim headerFont = grid.ColumnHeadersDefaultCellStyle.Font
                If headerFont IsNot Nothing AndAlso was.GridHeaderFontSize > 0 Then
                    Dim wanted = was.GridHeaderFontSize * factor
                    If Math.Abs(headerFont.Size - wanted) > 0.01F Then
                        grid.ColumnHeadersDefaultCellStyle.Font = New Font(headerFont.FontFamily, wanted, headerFont.Style)
                    End If
                End If
            Catch
                ' A grid mid-rebind can refuse a row height. The next zoom sets it again.
            End Try
        End Sub

        ''' <summary>
        ''' Where a split container divides. Not a bound, so the snapshot's box scaling leaves it
        ''' where it was - and a QBE panel whose contents grew 30% inside a divider that did not
        ''' move is cut off at the bottom.
        ''' </summary>
        Private Shared Sub ScaleSplitter(split As SplitContainer, was As Original, factor As Single)
            If split Is Nothing OrElse was.SplitterDistance <= 0 OrElse split.Panel1Collapsed Then Return

            Try
                split.SplitterDistance = CInt(was.SplitterDistance * factor)
            Catch
                ' Outside the panels' minimum sizes. Leaving the splitter where it is beats failing.
            End Try
        End Sub

        ''' <summary>
        ''' Parks a page off-screen when a remembered zoom is waiting for it.
        '''
        ''' For a page that lays itself out after it is shown, so the zoom cannot attach at Load:
        ''' the page waits out of sight rather than being seen at normal size and then jumping.
        ''' Off-screen, not Opacity - Opacity makes a layered window, and in a VirtualUI session that
        ''' page did not come back, which from the chair looks like the application closing.
        '''
        ''' Pair it with AttachAndReveal. A page parked and never revealed is open, modal and
        ''' unreachable.
        ''' </summary>
        Public Shared Sub ParkIfZoomPending(form As Form)
            If form Is Nothing Then Return
            If Math.Abs(PageZoomStore.FactorFor(form.GetType().Name) - 1.0F) < 0.001F Then Return

            form.StartPosition = FormStartPosition.Manual
            form.Location = New Point(ParkedOffscreen, ParkedOffscreen)
        End Sub

        ''' <summary>
        ''' Attaches the zoom once a page has finished laying itself out, and brings it on screen.
        '''
        ''' The zoom centres the window as it applies. The Finally covers every case where it did
        ''' not - an exception, or a factor that clamped back to 1.0 and made Apply do nothing.
        ''' </summary>
        Public Shared Sub AttachAndReveal(form As Form)
            If form Is Nothing OrElse form.IsDisposed Then Return

            Try
                Attach(form)
            Finally
                If form.Left <= ParkedOffscreen \ 2 Then
                    Dim room = Screen.FromControl(form).WorkingArea
                    form.Location = New Point(room.Left + ((room.Width - form.Width) \ 2),
                                              room.Top + ((room.Height - form.Height) \ 2))
                End If
            End Try
        End Sub

        Private Const ParkedOffscreen As Integer = -32000

        ''' <summary>
        ''' A small read-out of the zoom, tucked into the bottom-left corner.
        '''
        ''' Bottom-left because the other bottom corner is taken: Save and Cancel sit there on a
        ''' maintenance page. Shown at 100% too, so the reader can see the page has a zoom at all
        ''' rather than only finding out once it has been changed.
        '''
        ''' A fixed small font that does not scale. It is a note about the page, not part of it.
        ''' </summary>
        Private Shared Sub AddIndicator(form As Form, state As State)
            state.Indicator = New Label() With {
                .Name = "Label_ZoomLevel",
                .AutoSize = True,
                .Font = New Font("Segoe UI", 8.0F, FontStyle.Regular),
                .ForeColor = Color.Gray,
                .BackColor = Color.Transparent,
                .TabStop = False,
                .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
            }

            form.Controls.Add(state.Indicator)
            PlaceIndicator(form, state)

            ' A page that re-lays itself out, or Hot Fields widening one, moves the corner.
            AddHandler form.ClientSizeChanged, Sub(sender As Object, e As EventArgs) PlaceIndicator(form, state)
        End Sub

        Private Shared Sub PlaceIndicator(form As Form, state As State)
            Dim indicator = state.Indicator
            If indicator Is Nothing OrElse indicator.IsDisposed Then Return

            ' The keys beside the reading. A percentage on its own says the page has a zoom and not
            ' how to work it, and nothing else on screen says either - the keys are not on a menu,
            ' a toolbar or a tooltip. Discreet enough to ignore, in the same grey as the number.
            indicator.Text = CInt(Math.Round(state.Factor * 100)).ToString(Globalization.CultureInfo.InvariantCulture) &
                             "%:  F7 smaller   F8 larger   F9 reset"
            ' Scaled, because the band is a design measurement like every other one here: the gap
            ' below the page's content grows with the zoom, and a band fixed at its 100 per cent
            ' value would leave the read-out drifting towards the top of it as the space opened up.
            ' Two up from dead centre. An AutoSize label is a little taller than the glyphs in it,
            ' with the slack below the baseline, so the arithmetic centres the box and the eye sees
            ' the text sitting low.
            Const OpticalLift As Integer = 2

            Dim band = 0
            Dim left = 4

            Dim anchor = state.IndicatorAnchor
            If anchor IsNot Nothing AndAlso Not anchor.IsDisposed AndAlso anchor.IsHandleCreated Then
                ' In the form's own coordinates, whatever the control is nested inside.
                Dim bounds = form.RectangleToClient(anchor.RectangleToScreen(anchor.ClientRectangle))
                band = form.ClientSize.Height - bounds.Bottom
                left = bounds.Left
            End If

            Dim top = If(band > indicator.Height,
                         form.ClientSize.Height - band + ((band - indicator.Height) \ 2) - OpticalLift,
                         form.ClientSize.Height - indicator.Height - 3)
            indicator.Location = New Point(Math.Max(0, left), Math.Max(0, top))
            indicator.BringToFront()
        End Sub

        ''' <summary>Records the whole tree, so a control nested three deep scales with the rest.</summary>
        Private Shared Sub Capture(parent As Control, into As Dictionary(Of Control, Original))
            For Each control As Control In parent.Controls
                Dim entry As New Original With {
                    .Bounds = control.Bounds,
                    .FontSize = If(control.Font Is Nothing, 0.0F, control.Font.Size),
                    .Anchoring = control.Anchor
                }

                Dim grid = TryCast(control, DataGridView)
                If grid IsNot Nothing Then
                    entry.GridHeaderHeight = grid.ColumnHeadersHeight
                    entry.GridRowHeight = grid.RowTemplate.Height
                    entry.GridHeaderFontSize = If(grid.ColumnHeadersDefaultCellStyle.Font Is Nothing, 0.0F,
                                                  grid.ColumnHeadersDefaultCellStyle.Font.Size)

                    ' The resolved mode, so a column inheriting Fill from its grid counts as Fill.
                    entry.GridFixedColumns = New Dictionary(Of String, (Width As Integer, MinimumWidth As Integer))(StringComparer.OrdinalIgnoreCase)
                    For Each column As DataGridViewColumn In grid.Columns
                        If String.IsNullOrEmpty(column.Name) Then Continue For
                        If column.InheritedAutoSizeMode <> DataGridViewAutoSizeColumnMode.None Then Continue For

                        entry.GridFixedColumns(column.Name) = (column.Width, column.MinimumWidth)
                    Next
                End If

                Dim split = TryCast(control, SplitContainer)
                If split IsNot Nothing AndAlso Not split.Panel1Collapsed Then
                    entry.SplitterDistance = split.SplitterDistance
                End If

                into(control) = entry

                If control.Controls.Count > 0 Then Capture(control, into)
            Next
        End Sub

    End Class

End Namespace
