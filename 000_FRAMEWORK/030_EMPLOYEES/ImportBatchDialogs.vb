Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Globalization
Imports System.Text
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>Edit Note on Past Imports: the batch's name, shown, and its note, changed.</summary>
    Friend NotInheritable Class ImportBatchNoteDialog
        Inherits Form

        Private ReadOnly noteTextBox As TextBox
        Private ReadOnly saveButton As Button

        Private Sub New(batchName As String, note As String)
            Text = "Import Note"
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            StartPosition = FormStartPosition.CenterParent
            ClientSize = New Size(520, 250)
            Font = New Font("Segoe UI", 9.0F)

            Dim nameLabel As New Label() With {
                .Text = batchName,
                .Location = New Point(12, 12),
                .Size = New Size(496, 20),
                .AutoEllipsis = True,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
            }
            noteTextBox = New TextBox() With {
                .Name = "TextBox_Note",
                .Location = New Point(12, 40),
                .Size = New Size(496, 150),
                .Multiline = True,
                .ScrollBars = ScrollBars.Vertical,
                .MaxLength = 1000,
                .Text = note
            }
            AddHandler noteTextBox.TextChanged, Sub(sender, e) saveButton.Enabled = noteTextBox.Text.Trim() <> String.Empty

            saveButton = New Button() With {.Text = "Save", .Size = New Size(90, 30), .Location = New Point(322, 204), .DialogResult = DialogResult.OK}
            Dim cancelButton As New Button() With {.Text = "Cancel", .Size = New Size(90, 30), .Location = New Point(418, 204), .DialogResult = DialogResult.Cancel}

            Controls.AddRange(New Control() {nameLabel, noteTextBox, saveButton, cancelButton})
            Me.CancelButton = cancelButton
        End Sub

        ''' <summary>The new note, or Nothing when cancelled or unchanged.</summary>
        Friend Shared Function Ask(owner As IWin32Window, batchName As String, note As String) As String
            Using dialog As New ImportBatchNoteDialog(batchName, note)
                If dialog.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
                Dim result = dialog.noteTextBox.Text.Trim()
                If result = String.Empty OrElse String.Equals(result, If(note, String.Empty).Trim(), StringComparison.Ordinal) Then Return Nothing
                Return result
            End Using
        End Function
    End Class

    ''' <summary>
    ''' The double dog dare before Undo Import (Glenn, 2026-09-24). First a plain Yes or No that
    ''' names the import and counts what goes; then this, where Undo stays disabled until UNDO is
    ''' typed. A reflexive Enter gets through neither.
    ''' </summary>
    Friend NotInheritable Class ImportBatchUndoDialog
        Inherits Form

        Private Const Word As String = "UNDO"

        Private Sub New(name As String, people As Integer)
            Text = "Undo Import - Are You Sure?"
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            StartPosition = FormStartPosition.CenterParent
            ClientSize = New Size(480, 190)
            Font = New Font("Segoe UI", 9.0F)

            Dim message As New Label() With {
                .Text = "This permanently deletes " & Plural(people) & " from " & name & "." & Environment.NewLine &
                        "It cannot be taken back." & Environment.NewLine & Environment.NewLine &
                        "Type " & Word & " to confirm.",
                .Location = New Point(12, 12),
                .Size = New Size(456, 84),
                .ForeColor = Color.FromArgb(150, 0, 0)
            }
            Dim confirmTextBox As New TextBox() With {
                .Name = "TextBox_ConfirmUndo",
                .Location = New Point(12, 104),
                .Width = 160,
                .CharacterCasing = CharacterCasing.Upper
            }
            Dim undoButton As New Button() With {
                .Text = "Undo Import", .Size = New Size(110, 30), .Location = New Point(262, 148),
                .DialogResult = DialogResult.OK, .Enabled = False
            }
            Dim cancelButton As New Button() With {.Text = "Cancel", .Size = New Size(90, 30), .Location = New Point(378, 148), .DialogResult = DialogResult.Cancel}
            AddHandler confirmTextBox.TextChanged, Sub(sender, e) undoButton.Enabled = confirmTextBox.Text.Trim() = Word

            Controls.AddRange(New Control() {message, confirmTextBox, undoButton, cancelButton})
            Me.CancelButton = cancelButton
            ActiveControl = confirmTextBox
        End Sub

        Private Shared Function Plural(count As Integer) As String
            Return count.ToString("N0", CultureInfo.CurrentCulture) & If(count = 1, " person", " people")
        End Function

        ''' <summary>Both confirmations. True only when the first is Yes and UNDO is typed in the second.</summary>
        Friend Shared Function Confirm(owner As IWin32Window, name As String, impact As ImportBatchUndoImpact) As Boolean
            Dim text As New StringBuilder()
            text.Append("Undo ").Append(name).AppendLine("?").AppendLine()
            text.Append("Permanently deleted: ").Append(Plural(impact.PeopleStillPresent)).AppendLine(" - each employee, their login, role and settings.")
            If impact.PeopleStillPresent < impact.PeopleInBatch Then
                text.Append("   (").Append((impact.PeopleInBatch - impact.PeopleStillPresent).ToString(CultureInfo.CurrentCulture)).
                     AppendLine(" of the import were already deleted.)")
            End If
            If impact.IssuesReported > 0 Then
                text.Append("Also deleted: ").Append(impact.IssuesReported.ToString(CultureInfo.CurrentCulture)).
                     AppendLine(" Help Desk issue(s) they reported, with attachments.")
            End If
            If impact.IssuesAssigned > 0 Then
                text.Append("Unassigned: ").Append(impact.IssuesAssigned.ToString(CultureInfo.CurrentCulture)).
                     AppendLine(" Help Desk issue(s) assigned to them.")
            End If
            If impact.ReportsOutsideBatch > 0 Then
                text.Append("Left with no manager: ").Append(Plural(impact.ReportsOutsideBatch)).
                     AppendLine(" outside this import who reported to one of them.")
            End If
            If impact.MessagesKept > 0 Then
                text.Append("Kept: ").Append(impact.MessagesKept.ToString(CultureInfo.CurrentCulture)).
                     AppendLine(" message(s) to or from them.")
            End If
            text.AppendLine().Append("The import itself stays on Past Imports, marked undone.")

            If MessageBox.Show(owner, text.ToString(), "Undo Import", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                               MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
                Return False
            End If

            Using dialog As New ImportBatchUndoDialog(name, impact.PeopleStillPresent)
                Return dialog.ShowDialog(owner) = DialogResult.OK
            End Using
        End Function
    End Class
End Namespace
