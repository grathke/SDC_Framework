Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Flashes a grid row to say something happened to it, then leaves it exactly as it was.
    '''
    ''' The same shape as the Switch User notice on the main menu - a few steps on a timer,
    ''' alternating, then still - rather than a third way of saying "look here".
    '''
    ''' It flashes the **selection bar**, so the row must be selected - which both callers do anyway,
    ''' and which is sensible in itself: flashing a row the grid has not pointed at tells somebody to
    ''' look at something that is not marked. An unselected branch was written and taken out again;
    ''' nothing needed it, and an unexercised path is one nobody finds out is wrong.
    '''
    ''' It flashes the bar and not the text. The row it is called for is the selected one
    ''' and it has not moved, so the bar is both what the eye is already on and the only thing
    ''' covering its whole width. Changing the font instead works but can clip a fixed-height row,
    ''' and a grid that sizes to its rows would jump while being read.
    '''
    ''' It introduces no colour of its own. The bar alternates between the selection colour the row
    ''' already has and the background of the page the grid is sitting on, so the row reads as
    ''' blinking on and off rather than as turning a third colour that means nothing. Every page
    ''' therefore flashes in its own palette, and a page restyled later follows without being told.
    '''
    ''' It was a hard-coded amber until 2026-09-20. The reason it was amber is worth keeping,
    ''' because it still governs the choice: an earlier version than that alternated between the two
    ''' column colours and was almost invisible, both being blue. Contrast is the whole mechanism,
    ''' which is why the off state is the page background - the one colour on screen guaranteed to
    ''' differ from a selection bar - and not another shade of the bar.
    '''
    ''' Shared because two callers want it - the field picker when a field crosses between columns,
    ''' and a browse page when a row is inserted, where selecting and scrolling to the new record
    ''' already happens and the flash is what finishes the sentence.
    ''' </summary>
    Public Module GridRowFlash

        Private Const Steps As Integer = 8
        Private Const StepInterval As Integer = 190

        ''' <summary>
        ''' The page behind the grid, which is what the bar blinks to.
        '''
        ''' The form rather than the grid's own BackgroundColor: the grid's is the empty area below
        ''' the last row, which on a full grid is never on screen, and on several pages is left at
        ''' the default while the form carries the page's actual colour. Falls back to the grid and
        ''' then to Control, so a grid not yet on a form still flashes rather than throwing.
        ''' </summary>
        Private Function PageBackColorFor(grid As DataGridView) As Color
            Dim host = grid.FindForm()
            If host IsNot Nothing Then Return host.BackColor
            If Not grid.BackgroundColor.IsEmpty Then Return grid.BackgroundColor
            Return SystemColors.Control
        End Function

        ''' <summary>
        ''' Text that stays readable against the page background for the off half of the blink.
        '''
        ''' The row's own unselected colour where it has one - a tinted or deleted row has earned
        ''' that colour and should keep it - then the grid's default, then ControlText.
        ''' </summary>
        Private Function PageForeColorFor(grid As DataGridView, row As DataGridViewRow) As Color
            If Not row.DefaultCellStyle.ForeColor.IsEmpty Then Return row.DefaultCellStyle.ForeColor
            If Not grid.DefaultCellStyle.ForeColor.IsEmpty Then Return grid.DefaultCellStyle.ForeColor
            Return SystemColors.ControlText
        End Function

        ''' <summary>
        ''' Flashes the row and restores what it had. The colours it settles back to are read before
        ''' the flash rather than assumed, so a caller that tints its rows - a column tint, a deleted
        ''' row, anything else - gets its own colours back rather than the grid's defaults.
        '''
        ''' The timer owns itself: it stops after its steps, and stops early if the grid or the row
        ''' goes away, so nothing is left ticking against something disposed.
        ''' </summary>
        Public Sub Flash(grid As DataGridView, row As DataGridViewRow)
            If grid Is Nothing OrElse row Is Nothing OrElse grid.IsDisposed Then Return

            Dim settledBack = row.DefaultCellStyle.SelectionBackColor
            Dim settledText = row.DefaultCellStyle.SelectionForeColor

            ' Read once, before the first tick. Resolving it per tick would read the colour the
            ' flash itself has just written half the time.
            Dim pageBack = PageBackColorFor(grid)
            Dim pageText = PageForeColorFor(grid, row)

            Dim stepsLeft = Steps

            Dim flashTimer As New Timer With {.Interval = StepInterval}

            AddHandler flashTimer.Tick,
                Sub(sender As Object, e As EventArgs)
                    If grid.IsDisposed OrElse row.Index < 0 Then
                        flashTimer.Stop()
                        flashTimer.Dispose()
                        row.DefaultCellStyle.SelectionBackColor = settledBack
                        row.DefaultCellStyle.SelectionForeColor = settledText
                        Return
                    End If

                    stepsLeft -= 1

                    If stepsLeft <= 0 Then
                        flashTimer.Stop()
                        flashTimer.Dispose()
                        row.DefaultCellStyle.SelectionBackColor = settledBack
                        row.DefaultCellStyle.SelectionForeColor = settledText
                        Return
                    End If

                    ' Driven by the step count rather than by comparing the current colour against
                    ' the one being set. The comparison worked only because the off state was a
                    ' constant; with the off state read from the page, a page whose background
                    ' happens to equal the selection colour would never alternate at all.
                    Dim off = (stepsLeft Mod 2) = 1
                    row.DefaultCellStyle.SelectionBackColor = If(off, pageBack, settledBack)
                    row.DefaultCellStyle.SelectionForeColor = If(off, pageText, settledText)
                End Sub

            flashTimer.Start()
        End Sub


    End Module
End Namespace
