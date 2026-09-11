Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class MessagesWindowControl
        Inherits UserControl
        Implements IAccessControlledControl

        Private ReadOnly messageGrid As DataGridView
        Private ReadOnly inboxButton As Button
        Private ReadOnly sentButton As Button
        Private ReadOnly archiveButton As Button
        Private ReadOnly trashButton As Button
        Private ReadOnly newButton As Button
        Private ReadOnly viewButton As Button
        Private ReadOnly moveButton As Button
        Private ReadOnly deleteButton As Button
        Private ReadOnly previewTextBox As TextBox
        Private ReadOnly messageToolTip As ToolTip
        Private ReadOnly folderLabel As Label
        Private currentFolder As String = "Inbox"
        Private currentUser As UserContext

        Public Sub New()
            Dock = DockStyle.Fill
            BackColor = Color.White
            Padding = New Padding(2)

            Dim header = New Panel With {.Dock = DockStyle.Top, .Height = 64}
            folderLabel = New Label With {.Text = "Inbox", .Dock = DockStyle.Left, .Width = 110, .TextAlign = ContentAlignment.MiddleLeft, .Font = New Font("Segoe UI", 11.0F, FontStyle.Bold)}
            Dim refresh = New Button With {.Text = "Refresh", .Dock = DockStyle.Right, .Width = 90}
            AddHandler refresh.Click, Sub(sender, e) RefreshMessages()
            header.Controls.Add(refresh)
            header.Controls.Add(folderLabel)

            Dim tabs = New FlowLayoutPanel With {.Dock = DockStyle.Top, .Height = 32, .WrapContents = False}
            inboxButton = CreateTab("Inbox (0)", AddressOf Inbox_Click, 72, "Inbox")
            sentButton = CreateTab("Sent", AddressOf Sent_Click, 58)
            archiveButton = CreateTab("Archive", AddressOf Archive_Click, 70)
            trashButton = CreateTab("Trash", AddressOf Trash_Click, 58)
            tabs.Controls.AddRange({inboxButton, sentButton, archiveButton, trashButton})

            messageGrid = New DataGridView With {.Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False, .AllowUserToDeleteRows = False, .RowHeadersVisible = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect, .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill}
            messageGrid.Columns.Add("RecipientID", "RecipientID")
            messageGrid.Columns("RecipientID").Visible = False
            messageGrid.Columns.Add("MessageID", "MessageID")
            messageGrid.Columns("MessageID").Visible = False
            messageGrid.Columns.Add("FromName", "From")
            messageGrid.Columns.Add("ToName", "To")
            messageGrid.Columns.Add("Subject", "Subject")
            messageGrid.Columns.Add("SentOn", "Date")
            messageGrid.Columns.Add("IsRead", "IsRead")
            messageGrid.Columns("IsRead").Visible = False
            ApplyLightBlueHeaderStyle(messageGrid)
            AddHandler messageGrid.SelectionChanged, AddressOf MessageGrid_SelectionChanged
            AddHandler messageGrid.CellDoubleClick, AddressOf MessageGrid_CellDoubleClick

            messageToolTip = New ToolTip With {
                .AutoPopDelay = 15000,
                .InitialDelay = 500,
                .ReshowDelay = 100,
                .ShowAlways = True
            }

            previewTextBox = New TextBox With {
                .Dock = DockStyle.Bottom,
                .Height = 92,
                .Multiline = True,
                .ReadOnly = True,
                .ScrollBars = ScrollBars.Vertical,
                .BackColor = Color.FromArgb(250, 250, 250)
            }

            Dim footer = New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 42, .WrapContents = False}
            newButton = CreateCommandButton("New")
            viewButton = CreateCommandButton("View")
            moveButton = CreateCommandButton("Archive")
            deleteButton = CreateCommandButton("Delete")
            AddHandler newButton.Click, AddressOf New_Click
            AddHandler viewButton.Click, AddressOf View_Click
            AddHandler moveButton.Click, AddressOf Move_Click
            AddHandler deleteButton.Click, AddressOf Delete_Click
            footer.Controls.Add(newButton)
            footer.Controls.Add(viewButton)
            footer.Controls.Add(moveButton)
            footer.Controls.Add(deleteButton)

            Controls.Add(messageGrid)
            Controls.Add(previewTextBox)
            Controls.Add(footer)
            Controls.Add(tabs)
            Controls.Add(header)

        End Sub

        Public Sub InitializeForUser(user As UserContext)
            currentUser = user
            RefreshMessages()
        End Sub

        Public Sub ApplyAccess(profile As AccessProfile, tableName As String) Implements IAccessControlledControl.ApplyAccess
            Dim session = SessionState.Current
            If session.HasValue Then
                currentUser = New UserContext With {
                    .UserId = session.Value.UserID,
                    .FirstName = session.Value.FirstName,
                    .LastName = session.Value.LastName,
                    .Email = String.Empty
                }
                RefreshMessages()
            End If
        End Sub

        Private Function CreateTab(text As String, handler As EventHandler, width As Integer, Optional folderName As String = Nothing) As Button
            Dim button = New Button With {.Text = text, .AutoSize = False, .Width = width, .Height = 28, .FlatStyle = FlatStyle.Flat}
            button.Tag = If(folderName, text)
            AddHandler button.Click, handler
            Return button
        End Function

        Private Function CreateCommandButton(text As String) As Button
            Return New Button With {.Text = text, .Width = 78, .Height = 30}
        End Function

        Private Sub Inbox_Click(sender As Object, e As EventArgs)
            ActivateFolder("Inbox")
        End Sub

        Private Sub Sent_Click(sender As Object, e As EventArgs)
            ActivateFolder("Sent")
        End Sub

        Private Sub Archive_Click(sender As Object, e As EventArgs)
            ActivateFolder("Archive")
        End Sub

        Private Sub Trash_Click(sender As Object, e As EventArgs)
            ActivateFolder("Trash")
        End Sub

        Private Sub ActivateFolder(folderName As String)
            currentFolder = folderName
            folderLabel.Text = folderName
            UpdateTabVisuals()
            RefreshMessages()
        End Sub

        Private Sub UpdateTabVisuals()
            Dim activeColor = Color.FromArgb(30, 145, 190)
            Dim inactiveColor = Color.FromArgb(242, 245, 248)
            Dim activeTextColor = Color.White
            Dim inactiveTextColor = Color.FromArgb(45, 53, 63)

            For Each tabButton In New Button() {inboxButton, sentButton, archiveButton, trashButton}
                Dim isActive = String.Equals(Convert.ToString(tabButton.Tag), currentFolder, StringComparison.OrdinalIgnoreCase)
                tabButton.BackColor = If(isActive, activeColor, inactiveColor)
                tabButton.ForeColor = If(isActive, activeTextColor, inactiveTextColor)
                tabButton.Font = New Font(tabButton.Font, If(isActive, FontStyle.Bold, FontStyle.Regular))
            Next

            folderLabel.Text = currentFolder & " Messages"
            moveButton.Text = If(String.Equals(currentFolder, "Trash", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(currentFolder, "Archive", StringComparison.OrdinalIgnoreCase), "Restore", "Archive")
            deleteButton.Text = If(String.Equals(currentFolder, "Trash", StringComparison.OrdinalIgnoreCase), "Delete Forever", "Delete")
        End Sub

        ''' <summary>
        ''' Re-reads the current folder, keeping the control and the folder the user is on.
        '''
        ''' Called by the ribbon's Messages tile when the region is already showing messages: the
        ''' click used to build a second control and reload it from nothing, which flashed the
        ''' region and threw away the selection. A click that would otherwise do nothing is worth
        ''' more as "check for new mail".
        ''' </summary>
        Public Sub ReloadCurrentFolder()
            RefreshMessages()
        End Sub

        Private Sub RefreshMessages()
            messageGrid.Rows.Clear()
            previewTextBox.Clear()
            Try
                If currentUser Is Nothing OrElse Not SessionState.Current.HasValue Then
                    inboxButton.Text = "Inbox (0)"
                    Return
                End If

                Dim session = SessionState.Current.Value
                Dim rows = MessagingDataAccess.GetFolderRows(session.RegistrationID, currentUser.UserId, currentFolder)
                For Each row In rows
                    Dim rowIndex = messageGrid.Rows.Add(row.RecipientID,
                                                        row.MessageID,
                                                        row.FromName,
                                                        row.ToName,
                                                        row.Subject,
                                                        row.SentOn.ToLocalTime().ToString("g"),
                                                        row.IsRead)
                    ApplyReadStyle(messageGrid.Rows(rowIndex), row.IsRead)
                Next
                inboxButton.Text = "Inbox (" & MessagingDataAccess.CountUnread(session.RegistrationID, currentUser.UserId).ToString() & ")"
                UpdateTabVisuals()
                moveButton.Enabled = True
                deleteButton.Text = If(String.Equals(currentFolder, "Trash", StringComparison.OrdinalIgnoreCase), "Delete Forever", "Delete")
                deleteButton.Enabled = True
            Catch ex As Microsoft.Data.SqlClient.SqlException
                inboxButton.Text = "Inbox (setup)"
                folderLabel.Text = "Messaging setup required"
                moveButton.Enabled = False
                deleteButton.Enabled = False
                previewTextBox.Text = "Run sql\\011_messaging.sql to create or update the messaging tables."
            End Try
        End Sub

        Private Sub New_Click(sender As Object, e As EventArgs)
            Using compose As New MessageComposeForm(currentUser)
                If compose.ShowDialog(Me) = DialogResult.OK Then RefreshMessages()
            End Using
        End Sub

        Private Sub View_Click(sender As Object, e As EventArgs)
            If messageGrid.SelectedRows.Count = 0 Then Return
            LoadSelectedPreview()
            MarkSelectedMessageRead()
        End Sub

        Private Sub MessageGrid_SelectionChanged(sender As Object, e As EventArgs)
            LoadSelectedPreview()
            MarkSelectedMessageRead()
        End Sub

        Private Sub MessageGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex >= 0 Then
                MarkSelectedMessageRead()
            End If
        End Sub

        Private Sub MarkSelectedMessageRead()
            If messageGrid.SelectedRows.Count = 0 OrElse currentUser Is Nothing OrElse Not String.Equals(currentFolder, "Inbox", StringComparison.OrdinalIgnoreCase) Then
                Return
            End If

            Dim selectedRow = messageGrid.SelectedRows(0)
            Dim isRead = Convert.ToBoolean(selectedRow.Cells("IsRead").Value)
            If isRead Then
                Return
            End If

            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return
            End If

            Dim recipientId = Convert.ToInt32(selectedRow.Cells("RecipientID").Value)
            MarkRecipientRead(recipientId)
        End Sub

        Private Sub MarkRecipientRead(recipientId As Integer)
            If currentUser Is Nothing OrElse Not SessionState.Current.HasValue Then
                Return
            End If

            Dim session = SessionState.Current.Value
            MessagingDataAccess.MarkRecipientRead(recipientId, session.RegistrationID, currentUser.UserId)
            RefreshMessages()
        End Sub

        Private Sub LoadSelectedPreview()
            If messageGrid.SelectedRows.Count = 0 OrElse currentUser Is Nothing OrElse Not SessionState.Current.HasValue Then
                previewTextBox.Clear()
                Return
            End If

            Dim row = messageGrid.SelectedRows(0)
            Dim messageId = Convert.ToInt32(row.Cells("MessageID").Value)
            Dim session = SessionState.Current.Value
            previewTextBox.Text = MessagingDataAccess.GetMessageBody(messageId, session.RegistrationID, currentUser.UserId)
            messageToolTip.SetToolTip(previewTextBox, If(previewTextBox.Text.Length > 300, previewTextBox.Text, String.Empty))
        End Sub

        Private Sub Move_Click(sender As Object, e As EventArgs)
            If messageGrid.SelectedRows.Count = 0 OrElse currentUser Is Nothing Then Return
            Dim recipientId = Convert.ToInt32(messageGrid.SelectedRows(0).Cells("RecipientID").Value)
            If String.Equals(currentFolder, "Trash", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(currentFolder, "Archive", StringComparison.OrdinalIgnoreCase) Then
                MessagingDataAccess.RestoreRecipient(recipientId, currentUser.UserId, currentFolder)
                RefreshMessages()
                Return
            End If
            Dim targetFolder = "Archive"
            MessagingDataAccess.MoveRecipient(recipientId, currentUser.UserId, currentFolder, targetFolder)
            RefreshMessages()
        End Sub

        Private Sub ApplyReadStyle(row As DataGridViewRow, isRead As Boolean)
            If row Is Nothing Then
                Return
            End If

            row.DefaultCellStyle.BackColor = If(isRead, Color.White, Color.LightYellow)
            row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(190, 220, 245)
        End Sub

        Private Sub Delete_Click(sender As Object, e As EventArgs)
            If messageGrid.SelectedRows.Count = 0 OrElse currentUser Is Nothing Then Return
            Dim recipientId = Convert.ToInt32(messageGrid.SelectedRows(0).Cells("RecipientID").Value)
            If currentFolder = "Trash" Then
                If MessageBox.Show(Me, "Delete this message permanently? This cannot be undone.", "Delete Forever", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                    Dim session = SessionState.Current
                    If session.HasValue Then
                        MessagingDataAccess.DeleteRecipientPermanently(recipientId, currentUser.UserId, session.Value.RegistrationID)
                        RefreshMessages()
                    End If
                End If
                Return
            End If
            Dim isRead = Convert.ToBoolean(messageGrid.SelectedRows(0).Cells("IsRead").Value)
            If Not isRead Then
                MessageBox.Show(Me, "Unread messages must be viewed before they can be deleted.", "Message Not Deleted", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            MessagingDataAccess.MoveRecipient(recipientId, currentUser.UserId, currentFolder, "Trash")
            RefreshMessages()
        End Sub
    End Class
End Namespace
