Option Strict On
Option Explicit On

Imports System
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class NumericTextBoxHelper
        ''' <summary>
        ''' Configure a TextBox to accept only whole numbers (0-9, no decimals)
        ''' Used for Bit and Int fields
        ''' </summary>
        Public Shared Sub ConfigureWholeNumberOnly(textBox As TextBox)
            AddHandler textBox.KeyPress, Sub(sender, e)
                ' Only allow digits and control keys
                If Not Char.IsDigit(e.KeyChar) AndAlso Not Char.IsControl(e.KeyChar) Then
                    e.Handled = True
                End If
            End Sub
        End Sub

        ''' <summary>
        ''' Configure a TextBox to accept decimal numbers (0-9 and one decimal point)
        ''' Used for Decimal fields
        ''' </summary>
        Public Shared Sub ConfigureDecimalOnly(textBox As TextBox)
            AddHandler textBox.KeyPress, Sub(sender, e)
                ' Allow digits, control keys, and a single decimal point
                If Not Char.IsDigit(e.KeyChar) AndAlso Not Char.IsControl(e.KeyChar) AndAlso e.KeyChar <> "."c Then
                    e.Handled = True
                ElseIf e.KeyChar = "."c AndAlso textBox.Text.Contains(".") Then
                    ' Prevent multiple decimal points
                    e.Handled = True
                End If
            End Sub
        End Sub

        ''' <summary>
        ''' Configure a TextBox to accept currency format (0-9, decimal point, and optional negative sign)
        ''' Used for Currency/Money fields
        ''' </summary>
        Public Shared Sub ConfigureCurrencyOnly(textBox As TextBox)
            AddHandler textBox.KeyPress, Sub(sender, e)
                ' Allow digits, control keys, decimal point, and negative sign at start
                If Not Char.IsDigit(e.KeyChar) AndAlso Not Char.IsControl(e.KeyChar) AndAlso e.KeyChar <> "."c AndAlso e.KeyChar <> "-"c Then
                    e.Handled = True
                ElseIf e.KeyChar = "."c AndAlso textBox.Text.Contains(".") Then
                    ' Prevent multiple decimal points
                    e.Handled = True
                ElseIf e.KeyChar = "-"c AndAlso (textBox.SelectionStart <> 0 OrElse textBox.Text.Contains("-")) Then
                    ' Prevent negative sign except at the start, and only one
                    e.Handled = True
                End If
            End Sub
        End Sub

        ''' <summary>
        ''' Configure a TextBox to accept percentage format (0-9 and optional decimal point)
        ''' Used for Percentage fields
        ''' </summary>
        Public Shared Sub ConfigurePercentageOnly(textBox As TextBox)
            AddHandler textBox.KeyPress, Sub(sender, e)
                ' Allow digits, control keys, and a single decimal point
                If Not Char.IsDigit(e.KeyChar) AndAlso Not Char.IsControl(e.KeyChar) AndAlso e.KeyChar <> "."c Then
                    e.Handled = True
                ElseIf e.KeyChar = "."c AndAlso textBox.Text.Contains(".") Then
                    ' Prevent multiple decimal points
                    e.Handled = True
                End If
            End Sub
        End Sub
    End Class
End Namespace
