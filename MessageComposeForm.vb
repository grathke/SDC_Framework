Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class MessageComposeForm
        Inherits Form

        Private ReadOnly currentUserId As Integer
        Private ReadOnly registrationId As Integer
        Private ReadOnly recipientsList As ListBox
        Private ReadOnly ccList As ListBox
        Private ReadOnly subjectTextBox As TextBox
        Private ReadOnly bodyTextBox As TextBox

        Public Sub New(user As UserContext)
            Dim session = SessionState.Current
            currentUserId = If(user Is Nothing, 0, user.UserId)
            registrationId = If(session.HasValue, session.Value.RegistrationID, 0)
            Text = "Compose Message"
            StartPosition = FormStartPosition.CenterParent
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ClientSize = New Size(620, 570)

            Dim toLabel = CreateLabel("To", 18, 18)
            recipientsList = CreateRecipientList(18, 42)
            Dim ccLabel = CreateLabel("Cc", 318, 18)
            ccList = CreateRecipientList(318, 42)
            Dim subjectLabel = CreateLabel("Subject", 18, 220)
            subjectTextBox = New TextBox With {.Location = New Point(18, 244), .Width = 584}
            Dim bodyLabel = CreateLabel("Message", 18, 278)
            bodyTextBox = New TextBox With {.Location = New Point(18, 302), .Size = New Size(584, 190), .Multiline = True, .ScrollBars = ScrollBars.Vertical}
            Dim sendButton = New Button With {.Text = "Send", .Location = New Point(410, 520), .Size = New Size(90, 32)}
            Dim cancelButton = New Button With {.Text = "Cancel", .Location = New Point(510, 520), .Size = New Size(90, 32), .DialogResult = DialogResult.Cancel}
            AddHandler sendButton.Click, AddressOf SendButton_Click
            AcceptButton = sendButton
            CancelButton = cancelButton
            Controls.AddRange({toLabel, recipientsList, ccLabel, ccList, subjectLabel, subjectTextBox, bodyLabel, bodyTextBox, sendButton, cancelButton})
            LoadRecipients()
        End Sub

        Private Function CreateLabel(text As String, left As Integer, top As Integer) As Label
            Return New Label With {.Text = text, .Location = New Point(left, top), .AutoSize = True, .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)}
        End Function

        Private Function CreateRecipientList(left As Integer, top As Integer) As ListBox
            Return New ListBox With {.Location = New Point(left, top), .Size = New Size(284, 160), .SelectionMode = SelectionMode.MultiExtended, .DisplayMember = "DisplayName"}
        End Function

        Private Sub LoadRecipients()
            If registrationId <= 0 OrElse currentUserId <= 0 Then Return
            Dim users = MessagingDataAccess.GetRecipients(registrationId, currentUserId)
            recipientsList.DataSource = New List(Of MessagingDataAccess.MessageRecipientOption)(users)
            ccList.DataSource = New List(Of MessagingDataAccess.MessageRecipientOption)(users)
        End Sub

        Private Sub SendButton_Click(sender As Object, e As EventArgs)
            Try
                Dim toIds = recipientsList.SelectedItems.Cast(Of MessagingDataAccess.MessageRecipientOption)().Select(Function(item) item.UserID)
                Dim ccIds = ccList.SelectedItems.Cast(Of MessagingDataAccess.MessageRecipientOption)().Select(Function(item) item.UserID)
                MessagingDataAccess.SendMessage(registrationId, currentUserId, toIds, ccIds, subjectTextBox.Text, bodyTextBox.Text)
                DialogResult = DialogResult.OK
                Close()
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Message Not Sent", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub
    End Class
End Namespace
