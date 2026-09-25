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
    ''' F8 smaller, F9 larger, F10 back to normal - three adjacent keys, because a zoom somebody
    ''' reaches for repeatedly should be one hand movement rather than three.
    '''
    ''' Smaller on the left, because the keys are a physical row and a row of controls that
    ''' changes a magnitude should ascend left to right. Every zoom control anywhere puts the minus
    ''' left of the plus, and getting it backwards costs a wrong press every time until it is
    ''' learned.
    '''
    ''' Not F11 or F12: a browser takes those for fullscreen and developer tools before the session
    ''' ever sees them, and not Shift+F10, which is Windows' own context-menu key.
    '''
    ''' F10 is Windows' menu-bar key, which is why the reset was moved off it in September. No page
    ''' here has a menu bar and the handler suppresses the press, so it costs nothing - but it is
    ''' the one of the three to check after any change to how a page handles keys.
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
        ''' How far out a page may be zoomed.
        '''
        ''' Was 1.0 until 2026-09-22, which meant zoom out did not exist: the smaller key on a page
        ''' at 100 per cent asked for 0.9, had it clamped straight back to 1.0 and discarded. That
        ''' was survivable while every page fitted its window. It stopped being survivable on a page
        ''' 841 tall in 872 of room, where the button row is below the fold and the one key that
        ''' could have reached it was the key that did nothing.
        ''' </summary>
        Public Const Minimum As Single = 0.5F
        Public Const Maximum As Single = 2.0F
        Public Const Increment As Single = 0.1F

        ''' <summary>
        ''' A page that has something to settle before its zoom changes.
        '''
        ''' BeforeZoom is called before anything is scaled, every time the factor actually changes;
        ''' AfterZoom once the page is scaled, sized and centred. A browse page closes Hot Fields in
        ''' the first and reopens it at the new scale in the second: the strip widens the window by
        ''' an amount the snapshot knows nothing about, so it has to be out of the way while the
        ''' snapshot is applied and put back against the result.
        ''' </summary>
        Public Interface IZoomAware
            Sub BeforeZoom(newFactor As Single)
            Sub AfterZoom(newFactor As Single)
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
            ''' The factor in force was chosen by the fit, not by the person.
            '''
            ''' Kept so the close does not write it to the store: it answers the window this page
            ''' happened to open in, and a browser left at 110 per cent for a day would otherwise
            ''' rewrite a zoom somebody deliberately set. A key press clears it, because that is a
            ''' choice and choices are remembered.
            ''' </summary>
            Public AutoFitted As Boolean = False

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
                    ' F8 smaller, F9 larger, F10 reset. Three in a row, the pair ascending left to
                    ' right and the reset after the pair it resets. It was F9, F10, F8 until
                    ' 2026-09-19, F8, F9, F10 until 2026-09-20, F7, F8, F9 until 2026-09-22, and
                    ' is back where it was.
                    '
                    ' F10 is Windows' menu-bar key, which is why it was given up in September.
                    ' Nothing here has a menu bar and the handler suppresses the press, so it costs
                    ' nothing - but it is the one of the three to check after any change to how a
                    ' page handles keys.
                    Select Case e.KeyCode
                        Case Keys.F8
                            state.AutoFitted = False
                            Apply(form, state.Factor - Increment)
                        Case Keys.F9
                            state.AutoFitted = False
                            Apply(form, state.Factor + Increment)
                        Case Keys.F10
                            state.AutoFitted = False
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
                    '
                    ' A factor the fit chose is not written at all. It describes the window this
                    ' page opened in, which moves with the browser's own zoom and the size of
                    ' somebody's window; storing it would turn a temporary fit into the preference.
                    If Not state.AutoFitted Then
                        PageZoomStore.Remember(pageName, state.Factor)
                    End If

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
                    ScaleSplitter(TryCast(control, QbeSplitPanel), was, factor)
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

            If aware IsNot Nothing Then aware.AfterZoom(factor)
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

            ' The window follows the contents in both directions, since 2026-09-22.
            '
            ' It used to be held at its opening size whatever the factor, so zooming out shrank the
            ' contents inside a window that stayed put and left a margin to centre them in. That is
            ' the nicer picture and it is useless for the case zoom out exists to solve: a page
            ' taller than the browser view is still taller than the view afterwards, and the button
            ' row is still below the fold. A window that shrinks reaches the buttons; a margin does
            ' not.
            Dim wanted As New Size(CInt(state.DesignSize.Width * factor),
                                   CInt(state.DesignSize.Height * factor))

            If Math.Abs(factor - 1.0F) < 0.001F Then
                form.MaximumSize = state.DesignMaximum
                form.MinimumSize = state.DesignMinimum
            Else
                ' Both limits move out of the way, so each direction has somewhere to go.
                If Not state.DesignMaximum.IsEmpty Then
                    form.MaximumSize = New Size(Math.Max(state.DesignMaximum.Width, wanted.Width + 40),
                                                Math.Max(state.DesignMaximum.Height, wanted.Height + 40))
                End If

                ' The minimum used to stay put, and that quietly cancelled zooming out on the two
                ' kinds of page that declare one. A browse page is 980x680 against a floor of
                ' 920x620, so it stopped around 91 per cent; the main menu's floor is taller than
                ' its own design height, so it could not move at all. In both the contents shrank
                ' inside a window that would not follow - which is the fault this was meant to fix,
                ' wearing a different hat.
                '
                ' The floor is a guard against somebody dragging a window too small to use, and a
                ' zoom is not that. It comes back at 1.0, above.
                If Not state.DesignMinimum.IsEmpty Then
                    form.MinimumSize = New Size(Math.Min(state.DesignMinimum.Width, wanted.Width),
                                                Math.Min(state.DesignMinimum.Height, wanted.Height))
                Else
                    form.MinimumSize = state.DesignMinimum
                End If
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

            ' SplitterLayout clamps and refuses, rather than this setting a value and swallowing
            ' whatever came back. Zooming out shrinks the container while the panel minimums stay
            ' where they are, so a factor small enough leaves no legal position at all - and
            ' pressing on anyway is what produces a splitter rectangle off the bottom of the
            ' control and a GDI+ error from inside the layout.
            SplitterLayout.TrySetDistance(split, CInt(was.SplitterDistance * factor))
        End Sub

        ''' <summary>The same for the browse page's own split panel, which clamps itself.</summary>
        Private Shared Sub ScaleSplitter(split As QbeSplitPanel, was As Original, factor As Single)
            If split Is Nothing OrElse was.SplitterDistance <= 0 OrElse split.Panel1Collapsed Then Return
            split.TrySetDistance(CInt(was.SplitterDistance * factor))
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
                ShrinkToFitRoom(form)
            Finally
                If form.Left <= ParkedOffscreen \ 2 Then
                    Dim room = Screen.FromControl(form).WorkingArea
                    form.Location = New Point(room.Left + ((room.Width - form.Width) \ 2),
                                              room.Top + ((room.Height - form.Height) \ 2))
                End If
            End Try
        End Sub

        ''' <summary>
        ''' Zooms a page out far enough to fit the room it has, when it does not.
        '''
        ''' A page is FixedDialog: it cannot be dragged taller, it cannot be scrolled, and anything
        ''' past the bottom edge of the browser view is simply unreachable - including OK and
        ''' Cancel. That happened on the employee page at 841 tall in 872 of room: correct at every
        ''' measurement, and missing its button row.
        '''
        ''' It is not an employee-page problem. It is "any page taller than the window", and which
        ''' pages qualify changes with the size of somebody's browser window rather than with
        ''' anything in the application.
        '''
        ''' **The fitted factor is deliberately not remembered.** It answers this window, not a
        ''' preference: a day working in a short browser window would otherwise rewrite the zoom
        ''' somebody chose, and they would find it changed on a machine where it fitted perfectly
        ''' well. A key press afterwards is a choice and is remembered as usual - see AutoFitted.
        ''' </summary>
        Private Shared Sub ShrinkToFitRoom(form As Form)
            Dim state As State = Nothing
            If form Is Nothing OrElse form.IsDisposed OrElse Not states.TryGetValue(form, state) Then Return

            Dim room = Screen.FromControl(form).WorkingArea
            If room.Width <= 0 OrElse room.Height <= 0 Then Return

            ' The window, not its client area: the title bar and borders take the space too, and on
            ' the page that prompted this they are the difference between fitting and not.
            If form.Width <= room.Width AndAlso form.Height <= room.Height Then Return

            Dim chromeWidth = Math.Max(0, form.Width - form.ClientSize.Width)
            Dim chromeHeight = Math.Max(0, form.Height - form.ClientSize.Height)

            Dim roomForClient As New Size(Math.Max(1, room.Width - chromeWidth),
                                          Math.Max(1, room.Height - chromeHeight))

            Dim byWidth = roomForClient.Width / CSng(Math.Max(1, state.DesignSize.Width))
            Dim byHeight = roomForClient.Height / CSng(Math.Max(1, state.DesignSize.Height))

            ' A little under what just fits. Rounding and the odd border pixel have been enough to
            ' leave the last row touching the edge, which reads as still being cut off.
            Dim fitted = Math.Min(byWidth, byHeight) - 0.02F
            fitted = CSng(Math.Floor(fitted * 100.0F) / 100.0F)

            If fitted >= 1.0F Then Return

            state.AutoFitted = True
            Apply(form, fitted)
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
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left
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
                             "%:  F8 smaller   F9 larger   F10 reset"
            ' Top left, since 2026-09-22. It sat at the bottom left for a year and read perfectly
            ' well there, right up until the page it was most needed on turned out to be taller
            ' than the window: on a clipped page the read-out is one of the things clipped, so the
            ' one control that could have said what the zoom was could not be seen. The top of a
            ' page is the part that is always there.
            '
            ' It keeps its left edge aligned with whatever anchor a page nominated, so it still
            ' lines up with that page's content rather than floating in the corner on its own.
            Dim left = 4

            Dim anchor = state.IndicatorAnchor
            If anchor IsNot Nothing AndAlso Not anchor.IsDisposed AndAlso anchor.IsHandleCreated Then
                ' In the form's own coordinates, whatever the control is nested inside.
                Dim bounds = form.RectangleToClient(anchor.RectangleToScreen(anchor.ClientRectangle))
                left = bounds.Left
            End If

            indicator.Location = New Point(Math.Max(0, left), 3)
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

                Dim qbeSplit = TryCast(control, QbeSplitPanel)
                If qbeSplit IsNot Nothing AndAlso Not qbeSplit.Panel1Collapsed Then
                    entry.SplitterDistance = qbeSplit.SplitterDistance
                End If

                into(control) = entry

                If control.Controls.Count > 0 Then Capture(control, into)
            Next
        End Sub

    End Class

End Namespace
