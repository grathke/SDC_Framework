Option Strict On
Option Explicit On

Imports System
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' A SplitContainer that does not put an error dialog in front of somebody because it could
    ''' not paint its own splitter.
    '''
    ''' **The failure is inside WinForms, not in this application.** `SplitContainer.OnLayout`
    ''' calls `ResizeSplitContainer`, which calls `RepaintSplitterRect`, which takes a `Graphics`
    ''' from the control and fills a rectangle with it. That `Graphics` is unusable and
    ''' `FillRectangle` throws `ExternalException` - "a generic error occurred in GDI+". Nothing
    ''' outside the control can catch it, because nothing outside is on the stack: the resize
    ''' arrives as a window message and the throw happens in the base class's own layout. It
    ''' surfaces as an unhandled-exception dialog.
    '''
    ''' Seen first on 2026-09-21, opening a browse page through a **real Thinfinity session** -
    ''' never in `--tf-dev`, and never on the desktop. It is the delivery path, so it is what a
    ''' user would meet. Worth knowing when it will not reproduce: running the executable directly
    ''' gives VirtualUI's own dev server and the fault does not occur there.
    '''
    ''' **What it is not: a degenerate size.** This said so until 2026-09-23, and the 58 lines this
    ''' class had logged by then refuted it - every one identical, and every constraint satisfied
    ''' with room to spare:
    '''
    '''     size=940x562  client=940x562  distance=150  width=6
    '''     min1=120  min2=120  collapsed1=False  visible=True  handle=True
    '''
    ''' 562 less the splitter and the distance leaves 406 against a minimum of 120. The control is
    ''' visible, it has a handle, and its client area matches its size exactly. Nothing about it is
    ''' degenerate, and a reader sent looking for a bad size will not find one.
    '''
    ''' **What the shape points at, without claiming it.** It fires once per session, at the first
    ''' browse page open, on a control in good order, and only in a real session. That reads as no
    ''' usable device context at that instant rather than anything about geometry - a timing
    ''' condition in the delivery path. Not proven, and deliberately left as a description of the
    ''' evidence rather than a second guess dressed as a cause.
    '''
    ''' **Swallowing an exception is normally the wrong instinct.** It is right here because what
    ''' failed is a repaint of a splitter bar: cosmetic, redone by the next layout pass, and
    ''' already lost by the time anybody could react. The alternative is a dialog about GDI+ on
    ''' every page open.
    '''
    ''' **It records rather than merely surviving.** Every catch writes the control's size and
    ''' splitter distance to the log, once per control, so the question of *why* the control was
    ''' degenerate can be answered from evidence rather than from theories. Once per control
    ''' because a resize storm would otherwise fill the log with the same line.
    ''' </summary>
    Public Class SafeSplitContainer
        Inherits SplitContainer

        Private reported As Boolean

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            Try
                MyBase.OnLayout(e)
            Catch ex As ExternalException
                Report("OnLayout", ex)
            End Try
        End Sub

        ''' <summary>
        ''' The same guard for the paint itself.
        '''
        ''' `RepaintSplitterRect` is reached from the layout, which is where it was seen, but a
        ''' control whose `Graphics` cannot be created will fail its ordinary paint for the same
        ''' reason and from the same cause.
        ''' </summary>
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Try
                MyBase.OnPaint(e)
            Catch ex As ExternalException
                Report("OnPaint", ex)
            End Try
        End Sub

        Private Sub Report(where As String, ex As ExternalException)
            If reported Then Return
            reported = True

            ' To the health dashboard as well as the log file.
            '
            ' Catching this on 2026-09-21 stopped the dialog, which was the point, and also stopped
            ' the telemetry, which was not. The same failure sits in FW_ErrorLog as ErrorLogID 5,
            ' context Program.OnThreadException, recorded back when it still reached the global
            ' handler - and nothing has been recorded since, though it has happened in every
            ' session. A fault that is handled is still a fault, and one nobody can see is one
            ' nobody can decide about.
            '
            ' No flood: FW_ErrorLog is fingerprinted and merged, so repeats raise OccurrenceCount
            ' rather than adding rows, and the reported flag above already holds this to once per
            ' control per session.
            '
            ' Swallowed is the honest origin. It is caught here and the user is never told, which
            ' is the right call for a splitter repaint and exactly what that value means.
            Try
                Telemetry.Error(ex, "SafeSplitContainer." & where, Telemetry.FaultOrigin.Swallowed)
            Catch
                ' Reporting a paint failure must not become a second one.
            End Try

            Try
                Program.Log("SafeSplitContainer caught " & where & " on " &
                            If(Name, "(unnamed)") & ": " & ex.Message &
                            "  size=" & Width.ToString(Globalization.CultureInfo.InvariantCulture) &
                            "x" & Height.ToString(Globalization.CultureInfo.InvariantCulture) &
                            " client=" & ClientSize.Width.ToString(Globalization.CultureInfo.InvariantCulture) &
                            "x" & ClientSize.Height.ToString(Globalization.CultureInfo.InvariantCulture) &
                            " distance=" & SplitterDistance.ToString(Globalization.CultureInfo.InvariantCulture) &
                            " width=" & SplitterWidth.ToString(Globalization.CultureInfo.InvariantCulture) &
                            " min1=" & Panel1MinSize.ToString(Globalization.CultureInfo.InvariantCulture) &
                            " min2=" & Panel2MinSize.ToString(Globalization.CultureInfo.InvariantCulture) &
                            " collapsed1=" & Panel1Collapsed.ToString() &
                            " visible=" & Visible.ToString() &
                            " handle=" & IsHandleCreated.ToString())
            Catch
                ' Reporting a paint failure must not become a second one.
            End Try
        End Sub

    End Class

End Namespace
