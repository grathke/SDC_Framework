Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Sizes a preview window to what it has to show, or to the screen when that is smaller.
    '''
    ''' A page is laid out at whatever size its fields need, and nothing in the generator stops
    ''' that exceeding a monitor - twenty-three rows in one column comes to about 1261 pixels, and
    ''' the OK button ends up below the bottom edge where nobody can reach it. A maintenance page
    ''' is FixedDialog and not drag-resizable, so there is no way out of that at runtime.
    '''
    ''' Here the answer is to scroll: the window is capped to the working area and the full layout
    ''' becomes the scrollable content, so everything is reachable whatever the page's size. This
    ''' is a property of previewing, not of this page or this session - a preview that fitted on
    ''' one monitor and not another would be worse than one that always scrolls when it must.
    '''
    ''' It does not make the underlying page any shorter. A generated page that is taller than a
    ''' screen is a real defect in that page, and the preview showing it honestly is the point.
    ''' </summary>
    Public Module PreviewWindow

        ''' <summary>Room left for the window's own border and caption.</summary>
        Private Const ChromeAllowance As Integer = 60

        ''' <summary>
        ''' Gives the form the size it asked for, or as much of it as the screen allows with the
        ''' rest reachable by scrolling.
        ''' </summary>
        Public Sub Fit(form As Form, wanted As Size)
            If form Is Nothing Then Return

            Dim available = Screen.FromControl(form).WorkingArea

            Dim width = Math.Min(wanted.Width, Math.Max(320, available.Width - ChromeAllowance))
            Dim height = Math.Min(wanted.Height, Math.Max(240, available.Height - ChromeAllowance))

            ' The full layout is the scrollable content, so a capped window scrolls to the rest
            ' rather than clipping it. Set before the client size, or the first layout pass decides
            ' there is nothing to scroll to.
            form.AutoScroll = True
            form.AutoScrollMinSize = wanted
            form.ClientSize = New Size(width, height)
        End Sub

        ''' <summary>Whether a wanted size would have to be capped on this screen.</summary>
        Public Function ExceedsScreen(form As Form, wanted As Size) As Boolean
            If form Is Nothing Then Return False

            Dim available = Screen.FromControl(form).WorkingArea
            Return wanted.Width > available.Width - ChromeAllowance OrElse
                   wanted.Height > available.Height - ChromeAllowance
        End Function

    End Module
End Namespace
