Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Draws a rule across a combo's list after a given entry.
    '''
    ''' For a list with a short head and a long tail - the session time zone offers the nine US
    ''' zones and then several hundred others - where the break between the two is worth seeing
    ''' rather than inferring from where the names stop starting with "US".
    '''
    ''' A ComboBox has no separator of its own, so the list is owner-drawn: the entry before the
    ''' break is given a few pixels of extra height and the rule is drawn in that gap. Everything
    ''' else is drawn the way the control would have drawn it.
    ''' </summary>
    Public NotInheritable Class ComboSeparator

        Private Sub New()
        End Sub

        ''' <summary>The room the rule needs. Half above it, half below.</summary>
        Private Const GapHeight As Integer = 7

        ''' <summary>
        ''' Puts a rule after the entry at <paramref name="lastIndexBeforeBreak"/>.
        '''
        ''' Call it once, after the combo is filled. Attaching it twice would draw every entry
        ''' twice, so the combo is marked and a second call does nothing.
        ''' </summary>
        Public Shared Sub After(combo As ComboBox, lastIndexBeforeBreak As Integer)
            If combo Is Nothing OrElse lastIndexBeforeBreak < 0 Then Return
            If combo.DrawMode = DrawMode.OwnerDrawVariable Then Return

            Dim rowHeight = combo.ItemHeight
            combo.DrawMode = DrawMode.OwnerDrawVariable

            AddHandler combo.MeasureItem,
                Sub(sender As Object, e As MeasureItemEventArgs)
                    e.ItemHeight = rowHeight + If(e.Index = lastIndexBeforeBreak, GapHeight, 0)
                End Sub

            AddHandler combo.DrawItem,
                Sub(sender As Object, e As DrawItemEventArgs)
                    If e.Index < 0 OrElse e.Index >= combo.Items.Count Then
                        e.DrawBackground()
                        Return
                    End If

                    ' The closed box raises this too, with the selected entry and the box's own
                    ' bounds. It gets the text and nothing else - no highlight, because the box is
                    ' showing what the session is on rather than offering a choice, and no rule,
                    ' which would sit across the control itself.
                    Dim inList = (e.State And DrawItemState.ComboBoxEdit) <> DrawItemState.ComboBoxEdit

                    If inList Then
                        e.DrawBackground()
                    Else
                        Using background As New SolidBrush(combo.BackColor)
                            e.Graphics.FillRectangle(background, e.Bounds)
                        End Using
                    End If

                    Dim caption = combo.GetItemText(combo.Items(e.Index))
                    Dim textBounds = New Rectangle(e.Bounds.X + 1, e.Bounds.Y,
                                                   e.Bounds.Width - 2,
                                                   If(inList, rowHeight, e.Bounds.Height))

                    TextRenderer.DrawText(e.Graphics, caption, combo.Font, textBounds,
                                          If(inList, e.ForeColor, combo.ForeColor),
                                          TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or
                                          TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)

                    If inList AndAlso e.Index = lastIndexBeforeBreak Then
                        Dim ruleY = e.Bounds.Bottom - (GapHeight \ 2)
                        Using linePen As New Pen(SystemColors.ControlDark)
                            e.Graphics.DrawLine(linePen, e.Bounds.Left + 4, ruleY, e.Bounds.Right - 4, ruleY)
                        End Using
                    End If

                End Sub
        End Sub

    End Class

End Namespace
