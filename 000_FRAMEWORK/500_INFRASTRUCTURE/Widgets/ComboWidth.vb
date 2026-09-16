Option Strict On
Option Explicit On

Imports System.Data
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Narrows a combo to its widest entry.
    '''
    ''' One owner, because two surfaces fill combos and neither knows about the other: the
    ''' maintenance helpers on FW_Base_U, and RegistrationComboHelper for the registration
    ''' selector on every browse page.
    ''' </summary>
    Public NotInheritable Class ComboWidth

        Private Sub New()
        End Sub

        ''' <summary>The narrowest a combo is allowed to become, whatever it holds.</summary>
        Public Const Minimum As Integer = 120

        ''' <summary>
        ''' Never wider than it already is: the caller's width is the ceiling, and this only takes
        ''' back what the content does not use.
        ''' </summary>
        Public Shared Sub FitToContent(combo As ComboBox, source As DataTable, displayMember As String)
            If combo Is Nothing OrElse source Is Nothing OrElse source.Rows.Count = 0 Then Return
            If String.IsNullOrWhiteSpace(displayMember) OrElse Not source.Columns.Contains(displayMember) Then Return

            Dim widest = 0
            Using g = combo.CreateGraphics()
                For Each row As DataRow In source.Rows
                    Dim text = Convert.ToString(row(displayMember))
                    If String.IsNullOrEmpty(text) Then Continue For
                    widest = Math.Max(widest, CInt(Math.Ceiling(g.MeasureString(text, combo.Font).Width)))
                Next
            End Using

            Narrow(combo, widest)
        End Sub

        ''' <summary>
        ''' The same, for a combo whose entries are already in place: Items filled by hand, or a
        ''' data source whose display text only GetItemText knows how to resolve. Call it after
        ''' the combo is populated, and after DisplayMember is set.
        ''' </summary>
        Public Shared Sub FitToContent(combo As ComboBox)
            If combo Is Nothing OrElse combo.Items.Count = 0 Then Return

            Dim widest = 0
            Using g = combo.CreateGraphics()
                For Each item In combo.Items
                    Dim text = combo.GetItemText(item)
                    If String.IsNullOrEmpty(text) Then Continue For
                    widest = Math.Max(widest, CInt(Math.Ceiling(g.MeasureString(text, combo.Font).Width)))
                Next
            End Using

            Narrow(combo, widest)
        End Sub

        Private Shared Sub Narrow(combo As ComboBox, widest As Integer)
            If widest = 0 Then Return

            ' The drop-down arrow and the text inset, which MeasureString knows nothing about.
            Dim needed = widest + SystemInformation.VerticalScrollBarWidth + 12
            combo.Width = Math.Max(Minimum, Math.Min(combo.Width, needed))
        End Sub

    End Class

End Namespace
