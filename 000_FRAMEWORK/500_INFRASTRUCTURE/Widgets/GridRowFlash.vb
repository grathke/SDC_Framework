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
    ''' Amber, deliberately. It is not the red the Switch User notice uses, because nothing has gone
    ''' wrong - a field changed column, a record was added. An earlier version alternated between
    ''' the two column colours, which said more and showed less: both are blue, so the alternation
    ''' was almost invisible.
    '''
    ''' Shared because two callers want it - the field picker when a field crosses between columns,
    ''' and a browse page when a row is inserted, where selecting and scrolling to the new record
    ''' already happens and the flash is what finishes the sentence.
    ''' </summary>
    Public Module GridRowFlash

        ''' <summary>What a flashing row shows, and the text on it.</summary>
        Public ReadOnly FlashBackColor As Color = Color.FromArgb(255, 193, 7)
        Public ReadOnly FlashForeColor As Color = Color.FromArgb(32, 32, 32)

        Private Const Steps As Integer = 8
        Private Const StepInterval As Integer = 190

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
            Dim stepsLeft = Steps

            Dim flashTimer As New Timer With {.Interval = StepInterval}

            AddHandler flashTimer.Tick,
                Sub(sender As Object, e As EventArgs)
                    If grid.IsDisposed OrElse row.Index < 0 Then
                        flashTimer.Stop()
                        flashTimer.Dispose()
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

                    Dim lit = row.DefaultCellStyle.SelectionBackColor <> FlashBackColor
                    row.DefaultCellStyle.SelectionBackColor = If(lit, FlashBackColor, settledBack)
                    row.DefaultCellStyle.SelectionForeColor = If(lit, FlashForeColor, settledText)
                End Sub

            flashTimer.Start()
        End Sub


    End Module
End Namespace
