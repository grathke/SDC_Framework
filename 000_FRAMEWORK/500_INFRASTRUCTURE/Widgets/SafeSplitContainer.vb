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
    ''' from the control and fills a rectangle with it. When the control has been sized through
    ''' something degenerate, that `Graphics` is unusable and `FillRectangle` throws
    ''' `ExternalException` - "a generic error occurred in GDI+". Nothing outside the control can
    ''' catch it, because nothing outside is on the stack: the resize arrives as a window message
    ''' and the throw happens in the base class's own layout. It surfaces as an unhandled-exception
    ''' dialog.
    '''
    ''' Seen first on 2026-09-21, opening a browse page through a **real Thinfinity session** at
    ''' 1678x807 - never in `--tf-dev` at 2014x969, and never on the desktop. It is the delivery
    ''' path, so it is what a user would meet.
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
