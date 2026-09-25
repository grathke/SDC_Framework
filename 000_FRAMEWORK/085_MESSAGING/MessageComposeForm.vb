Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
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

            ' Subject and Message are required, and look it: the asterisk and the App Admin blue on
            ' the label, green on the field with the focus, red once visited and left empty - the
            ' same rules as every maintenance page, from the same owner (Glenn, 2026-09-25).
            indicators = New FieldIndicators(FW_Base_U.AppAdminRequiredBackColor,
                                             excludeFromFocus:=Function(control) control Is sendButton OrElse control Is cancelButton)
            MarkRequired(subjectLabel, subjectTextBox, "Subject")
            MarkRequired(bodyLabel, bodyTextBox, "Message")
            AddHandler Shown, Sub(sender As Object, e As EventArgs) indicators.Wire(Me)

            LoadRecipients()
        End Sub

        Private ReadOnly indicators As FieldIndicators

        Private Sub MarkRequired(label As Label, field As Control, caption As String)
            label.Text &= " *"
            label.BackColor = FW_Base_U.AppAdminRequiredBackColor
            indicators.AddRequired(field, caption, Me)
        End Sub

        Private Function CreateLabel(text As String, left As Integer, top As Integer) As Label
            Return New Label With {.Text = text, .Location = New Point(left, top), .AutoSize = True, .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)}
        End Function

        ''' <summary>
        ''' A plain click selects and a second click unselects - MultiSimple, not MultiExtended.
        ''' Extended needs Ctrl or Shift held with the click, which the browser does not carry
        ''' through Thinfinity, and which nothing on screen tells anybody to try (2026-09-25).
        ''' </summary>
        Private Function CreateRecipientList(left As Integer, top As Integer) As ListBox
            Return New ListBox With {.Location = New Point(left, top), .Size = New Size(284, 160), .SelectionMode = SelectionMode.MultiSimple, .DisplayMember = "DisplayName"}
        End Function

        ''' <summary>The first Cc entry, and the one selected on open. UserID 0 is nobody.</summary>
        Private Shared ReadOnly NoCc As New MessagingDataAccess.MessageRecipientOption With {.UserID = 0, .DisplayName = "(None)"}

        Private settingCcSelection As Boolean
        Private lastCcClickIndex As Integer = -1

        Private Sub LoadRecipients()
            If registrationId <= 0 OrElse currentUserId <= 0 Then Return
            Dim users = MessagingDataAccess.GetRecipients(registrationId, currentUserId)
            recipientsList.DataSource = New List(Of MessagingDataAccess.MessageRecipientOption)(users)

            Dim ccChoices As New List(Of MessagingDataAccess.MessageRecipientOption) From {NoCc}
            ccChoices.AddRange(users)
            ccList.DataSource = ccChoices

            AddHandler ccList.MouseDown, Sub(sender As Object, e As MouseEventArgs) lastCcClickIndex = ccList.IndexFromPoint(e.Location)
            AddHandler ccList.SelectedIndexChanged, AddressOf CcList_SelectedIndexChanged

            ' After the window exists, not here. A bound list box selects its first entry when it is
            ' filled - which Cc'd the first person on every message nobody unselected them from -
            ' and it does so again when its handle is created, undoing a selection cleared earlier.
            AddHandler Shown, Sub(sender As Object, e As EventArgs)
                                  recipientsList.ClearSelected()
                                  SetCcSelection(Sub()
                                                     ccList.ClearSelected()
                                                     ccList.SetSelected(0, True)
                                                 End Sub)
                              End Sub
        End Sub

        ''' <summary>
        ''' (None) and a person cannot both be chosen. Picking a person unselects (None); picking
        ''' (None) clears everybody; unselecting the last person puts (None) back.
        ''' </summary>
        Private Sub CcList_SelectedIndexChanged(sender As Object, e As EventArgs)
            If settingCcSelection OrElse ccList.Items.Count = 0 Then Return

            Dim noneChosen = ccList.GetSelected(0)
            Dim peopleChosen = ccList.SelectedIndices.Cast(Of Integer)().Any(Function(index) index > 0)

            If noneChosen AndAlso peopleChosen Then
                ' Whichever was clicked last wins. MouseDown records the row before the list toggles it.
                If lastCcClickIndex = 0 Then
                    SetCcSelection(Sub()
                                       ccList.ClearSelected()
                                       ccList.SetSelected(0, True)
                                   End Sub)
                Else
                    SetCcSelection(Sub() ccList.SetSelected(0, False))
                End If
            ElseIf Not noneChosen AndAlso Not peopleChosen Then
                SetCcSelection(Sub() ccList.SetSelected(0, True))
            End If
        End Sub

        Private Sub SetCcSelection(change As Action)
            settingCcSelection = True
            Try
                change()
            Finally
                settingCcSelection = False
            End Try
        End Sub

        Private Sub SendButton_Click(sender As Object, e As EventArgs)
            Try
                Dim toIds = recipientsList.SelectedItems.Cast(Of MessagingDataAccess.MessageRecipientOption)().Select(Function(item) item.UserID)
                Dim ccIds = ccList.SelectedItems.Cast(Of MessagingDataAccess.MessageRecipientOption)().
                    Where(Function(item) item.UserID > 0).Select(Function(item) item.UserID)
                MessagingDataAccess.SendMessage(registrationId, currentUserId, toIds, ccIds, subjectTextBox.Text, bodyTextBox.Text)
                DialogResult = DialogResult.OK
                Close()
            Catch ex As Exception
                ' A refused send names what is missing, so the red borders say the same thing.
                indicators.MarkAllRequiredTouched()
                MessageBox.Show(Me, ex.Message, "Message Not Sent", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub
    End Class
End Namespace
