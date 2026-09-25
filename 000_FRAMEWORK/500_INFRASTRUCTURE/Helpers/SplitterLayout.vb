Option Strict On
Option Explicit On

Imports System
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Where a splitter is allowed to sit, which is not where the obvious arithmetic puts it.
    '''
    ''' **The splitter has width of its own.** The largest legal SplitterDistance is
    ''' Height - Panel2MinSize - **SplitterWidth**, and three places in this application computed
    ''' it as Height - Panel2MinSize. Six pixels, which matters only when the container is near its
    ''' own minimum - and then it does not merely look wrong. WinForms fills a rectangle for the
    ''' splitter during its next resize, the rectangle runs past the bottom edge, and
    ''' Graphics.FillRectangle reports "a generic error occurred in GDI+" from inside the layout,
    ''' where no caller can catch it. It reaches the user as an unhandled-exception dialog.
    '''
    ''' Found on 2026-09-21, opening a browse page through a real Thinfinity session at 1678x807.
    ''' At the development viewport of 2014x969 the container was never close enough to its
    ''' minimum for the six pixels to matter, which is why it had never been seen.
    '''
    ''' **A container too small for both minimums has no legal position at all.** There is nothing
    ''' to clamp to, and moving the splitter anyway is what produces the bad rectangle. It is left
    ''' where it is.
    ''' </summary>
    Public Module SplitterLayout

        ''' <summary>
        ''' Whether the container can hold both panels and the splitter between them.
        ''' </summary>
        Public Function CanPositionSplitter(split As SplitContainer) As Boolean
            If split Is Nothing OrElse split.IsDisposed Then Return False
            If split.Panel1Collapsed OrElse split.Panel2Collapsed Then Return False

            Return Extent(split) >= split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth
        End Function

        ''' <summary>
        ''' The largest distance this container will accept, or -1 when it will accept none.
        ''' </summary>
        Public Function MaxDistance(split As SplitContainer) As Integer
            If Not CanPositionSplitter(split) Then Return -1
            Return MaxDistance(Extent(split), split.Panel1MinSize, split.Panel2MinSize, split.SplitterWidth)
        End Function

        ''' <summary>
        ''' The same arithmetic for a control that is not a SplitContainer - QbeSplitPanel draws its
        ''' own bar but is bound by the same six pixels. -1 when no distance is legal.
        ''' </summary>
        Public Function MaxDistance(extent As Integer, panel1MinSize As Integer, panel2MinSize As Integer, splitterWidth As Integer) As Integer
            If extent < panel1MinSize + panel2MinSize + splitterWidth Then Return -1
            Return extent - panel2MinSize - splitterWidth
        End Function

        ''' <summary>
        ''' Puts the splitter as close to <paramref name="desired"/> as the container allows, and
        ''' returns False without touching it when no position is legal.
        '''
        ''' The catch is kept because a layout in progress can still refuse a value that was legal
        ''' when it was computed. Leaving the splitter where it is beats failing a page load.
        ''' </summary>
        Public Function TrySetDistance(split As SplitContainer, desired As Integer) As Boolean
            Dim largest = MaxDistance(split)
            If largest < 0 Then Return False

            Dim wanted = Math.Min(Math.Max(split.Panel1MinSize, desired), largest)
            If wanted = split.SplitterDistance Then Return True

            Try
                split.SplitterDistance = wanted
                Return True
            Catch
                Return False
            End Try
        End Function

        ''' <summary>The dimension the splitter divides: height when horizontal, width when not.</summary>
        Private Function Extent(split As SplitContainer) As Integer
            If split.Orientation = Orientation.Horizontal Then Return split.Height
            Return split.Width
        End Function

    End Module

End Namespace
