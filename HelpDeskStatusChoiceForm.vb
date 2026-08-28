Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class HelpDeskStatusChoiceForm
        Inherits Form

        Private selectedStatusValue As String

        Public ReadOnly Property SelectedStatus As String
            Get
                Return selectedStatusValue
            End Get
        End Property

        Public Sub New()
            Text = "Select Status"
            StartPosition = FormStartPosition.CenterParent
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ShowInTaskbar = False
            ClientSize = New Size(430, 220)

            Dim prompt As New Label With {
                .Text = "SELECT A STATUS BEFORE SAVING.",
                .AutoSize = True,
                .Location = New Point(16, 14),
                .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold)
            }
            Controls.Add(prompt)

            Dim statuses = New String() {"In Progress", "Assigned", "Waiting for User", "Resolved", "Closed", "Reopened"}
            For index As Integer = 0 To statuses.Length - 1
                Dim button As New Button With {
                    .Text = statuses(index),
                    .Size = New Size(185, 34),
                    .Location = New Point(16 + (index Mod 2) * 195, 48 + (index \ 2) * 40),
                    .Tag = statuses(index)
                }
                AddHandler button.Click, AddressOf StatusButton_Click
                Controls.Add(button)
            Next

            Dim cancelButton As New Button With {
                .Text = "Cancel",
                .Size = New Size(90, 30),
                .Location = New Point(ClientSize.Width - 106, ClientSize.Height - 38),
                .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right,
                .DialogResult = DialogResult.Cancel
            }
            Controls.Add(cancelButton)
            CancelButton = cancelButton
        End Sub

        Private Sub StatusButton_Click(sender As Object, e As EventArgs)
            Dim button = TryCast(sender, Button)
            If button Is Nothing Then Return
            selectedStatusValue = button.Tag.ToString()
            DialogResult = DialogResult.OK
            Close()
        End Sub
    End Class
End Namespace
