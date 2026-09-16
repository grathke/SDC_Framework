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
    ''' F9 larger, F10 smaller, F8 back to normal. Not F11 or F12: a browser takes those for
    ''' fullscreen and developer tools before VirtualUI ever sees them, and not Shift+F10, which is
    ''' Windows' own context-menu key.
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

        ''' <summary>A control as it was laid out, before anybody zoomed.</summary>
        Private Structure Original
            Public Bounds As Rectangle
            Public FontSize As Single
            Public Anchoring As AnchorStyles
        End Structure

        Private NotInheritable Class State
            Public ReadOnly Layout As New Dictionary(Of Control, Original)()
            Public Factor As Single = 1.0F
            Public DesignSize As Size
            Public DesignMaximum As Size
            Public DesignMinimum As Size
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
                    Select Case e.KeyCode
                        Case Keys.F9
                            Apply(form, state.Factor + Increment)
                        Case Keys.F10
                            Apply(form, state.Factor - Increment)
                        Case Keys.F8
                            Apply(form, 1.0F)
                        Case Else
                            Return
                    End Select

                    e.Handled = True
                    e.SuppressKeyPress = True
                End Sub

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
                Next

            Finally
                form.ResumeLayout(True)
            End Try

            ' After the layout pass, never inside it. Anchoring runs during ResumeLayout and pulls
            ' every anchored control back to the edge it was measured against, which silently undid
            ' the centring on every press.
            Centre(form, state)
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

        ''' <summary>Records the whole tree, so a control nested three deep scales with the rest.</summary>
        Private Shared Sub Capture(parent As Control, into As Dictionary(Of Control, Original))
            For Each control As Control In parent.Controls
                into(control) = New Original With {
                    .Bounds = control.Bounds,
                    .FontSize = If(control.Font Is Nothing, 0.0F, control.Font.Size),
                    .Anchoring = control.Anchor
                }

                If control.Controls.Count > 0 Then Capture(control, into)
            Next
        End Sub

    End Class

End Namespace
