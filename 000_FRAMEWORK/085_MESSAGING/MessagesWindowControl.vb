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

        ''' The unread key, shown only on the Inbox. Read mail is white everywhere, and Sent, Trash
        ''' and Archive have nothing yellow to explain - a legend for a colour that is not on screen
        ''' is just furniture. Held as fields so the folder switch can hide them; the flow panel
        ''' closes the gap by itself, because it skips invisible children.
        Private ReadOnly unreadSwatch As Label
        Private ReadOnly unreadLegend As Label
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
            ' What the yellow rows mean, on the strip directly above them. A legend rather than a
            ' tooltip: hover is the gesture that degrades under VirtualUI, where mouse-move events
            ' are coalesced and a tooltip arrives late or not at all, so the explanation of a
            ' colour should not be something the user has to find by hovering.
            unreadSwatch = New Label With {
                .Text = String.Empty,
                .Size = New Size(14, 14),
                .BackColor = Color.LightYellow,
                .BorderStyle = BorderStyle.FixedSingle,
                .Margin = New Padding(18, 9, 4, 0)
            }
            unreadLegend = New Label With {
                .Text = "unread",
                .AutoSize = True,
                .ForeColor = Color.FromArgb(110, 118, 128),
                .Margin = New Padding(0, 8, 0, 0)
            }

            tabs.Controls.AddRange({inboxButton, sentButton, archiveButton, trashButton, unreadSwatch, unreadLegend})

            messageGrid = New DataGridView With {.Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False, .AllowUserToDeleteRows = False, .RowHeadersVisible = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect, .MultiSelect = False, .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill}
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
            AddHandler messageGrid.CellClick, AddressOf MessageGrid_CellClick
            AddHandler messageGrid.CellDoubleClick, AddressOf MessageGrid_CellDoubleClick

            ' Clearing the selection during a load does not survive what happens next: a focused
            ' DataGridView with no current cell sets one at once, and that selects its row. So the
            ' newest message still arrived highlighted with its body in the preview, which reads
            ' as opened. Posting the clear puts it after focus and layout have settled.
            AddHandler Me.VisibleChanged, AddressOf Messages_VisibleChanged

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

        ''' <summary>
        ''' Takes the session's user and loads the folder. The only way in: InitializeForUser sat
        ''' beside this until 2026-09-17 doing the same job with no callers at all, which is one
        ''' path too many for "the panel learns who it is for".
        ''' </summary>
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

            Dim onInbox = String.Equals(currentFolder, "Inbox", StringComparison.OrdinalIgnoreCase)
            unreadSwatch.Visible = onInbox
            unreadLegend.Visible = onInbox

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
        ''' <summary>
        ''' Re-reads the folder and returns the inbox's unread count, so a caller that wanted the
        ''' count does not ask for it again. The menu's check is exactly that caller: it used to
        ''' count, then have this reload, which counted once more - three round trips for one
        ''' answer.
        ''' </summary>
        Public Function ReloadCurrentFolder() As Integer
            Return RefreshMessages()
        End Function

        Private Function RefreshMessages() As Integer
            messageGrid.Rows.Clear()
            previewTextBox.Clear()
            Try
                If currentUser Is Nothing OrElse Not SessionState.Current.HasValue Then
                    inboxButton.Text = "Inbox (0)"

                    ' Minus one is "I do not know yet", not "no unread mail". The panel is created
                    ' and put in its region before it is told who it is for, and the menu's first
                    ' check can arrive in that gap - it did, every sign-in, and a zero answered
                    ' there left the Messages tile with no badge until something counted again.
                    ' A caller that needs the number can count for itself.
                    Return -1
                End If

                Dim session = SessionState.Current.Value
                ' One round trip for the rows and the count. The count comes from the database
                ' rather than from the rows, because a reload is a check for new mail and the grid
                ' may be showing Sent or Trash, which say nothing about the inbox.
                Dim snapshot = MessagingDataAccess.GetFolderSnapshot(session.RegistrationID, currentUser.UserId, currentFolder)
                For Each row In snapshot.Rows
                    Dim rowIndex = messageGrid.Rows.Add(row.RecipientID,
                                                        row.MessageID,
                                                        row.FromName,
                                                        row.ToName,
                                                        row.Subject,
                                                        SessionTime.ToSessionZone(row.SentOn).ToString("g"),
                                                        row.IsRead)
                    ApplyReadStyle(messageGrid.Rows(rowIndex), row.IsRead)
                Next

                ' Nothing selected until the user selects something. CurrentCell first and then
                ' the selection: clearing the selection alone leaves the current cell set, and the
                ' grid re-selects that cell's row the moment it can - so the newest message still
                ' arrived highlighted, with its body in the preview, looking read before it was.
                messageGrid.CurrentCell = Nothing
                messageGrid.ClearSelection()
                PublishUnreadCount(snapshot.UnreadCount)
                UpdateTabVisuals()
                moveButton.Enabled = True
                deleteButton.Text = If(String.Equals(currentFolder, "Trash", StringComparison.OrdinalIgnoreCase), "Delete Forever", "Delete")
                deleteButton.Enabled = True
                Return snapshot.UnreadCount
            Catch ex As Microsoft.Data.SqlClient.SqlException
                inboxButton.Text = "Inbox (setup)"
                folderLabel.Text = "Messaging setup required"
                moveButton.Enabled = False
                deleteButton.Enabled = False
                previewTextBox.Text = "Run sql\011_messaging.sql to create or update the messaging tables."
                Return 0
            End Try
        End Function

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

        ''' <summary>
        ''' Shows the message. It does not mark it read - selection is not reading.
        '''
        ''' A DataGridView changes selection on its own more than once: when the first row is
        ''' added, and again whenever the grid takes focus and re-selects its current cell. Both
        ''' looked exactly like a click, so a new message was marked read by the Inbox merely
        ''' appearing, and the menu's asterisk went out before anyone had seen it. Reading is a
        ''' click, a double-click or View, and those say so.
        ''' </summary>
        Private Sub MessageGrid_SelectionChanged(sender As Object, e As EventArgs)
            LoadSelectedPreview()
        End Sub

        ''' <summary>
        ''' Leaves the list with nothing chosen each time the region is shown, so the first thing
        ''' highlighted is the message the user picked.
        ''' </summary>
        Private Sub Messages_VisibleChanged(sender As Object, e As EventArgs)
            If Not Me.Visible OrElse Not Me.IsHandleCreated Then Return

            Me.BeginInvoke(Sub()
                               messageGrid.ClearSelection()
                               messageGrid.CurrentCell = Nothing
                               previewTextBox.Clear()
                           End Sub)
        End Sub

        Private Sub MessageGrid_CellClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
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
            MarkRowRead(recipientId)
        End Sub

        ''' <summary>
        ''' Applies a read to the row on screen instead of reloading the folder.
        '''
        ''' Reading a message is not a check for new mail. It used to call RefreshMessages, which
        ''' cost two more queries, cleared the grid under the user - losing the selection and the
        ''' preview of the message they had just opened - and picked up new arrivals only as a side
        ''' effect. The timer, the Refresh button and returning to the menu all look for new mail;
        ''' this does not need to.
        '''
        ''' Everything that changed is already known: this row is read, and there is one less
        ''' unread. Only reachable from the Inbox - MarkSelectedMessageRead returns on any other
        ''' folder - so the rows on screen are the inbox and counting them is the same answer
        ''' CountUnread would give.
        ''' </summary>
        Private Sub MarkRowRead(recipientId As Integer)
            For Each row As DataGridViewRow In messageGrid.Rows
                If Convert.ToInt32(row.Cells("RecipientID").Value) = recipientId Then
                    row.Cells("IsRead").Value = True
                    ApplyReadStyle(row, True)
                    Exit For
                End If
            Next

            PublishUnreadCount(UnreadRowsOnScreen())
        End Sub

        ''' <summary>
        ''' The tab caption and the menu's marker, from one number, so the two can never disagree
        ''' about whether there is unread mail.
        ''' </summary>
        Private Sub PublishUnreadCount(unread As Integer)
            inboxButton.Text = "Inbox (" & unread.ToString() & ")"

            Dim menu = TryCast(Me.FindForm(), FW_MainMenu)
            If menu IsNot Nothing Then menu.SetNewMessageIndicator(unread)
        End Sub

        Private Function UnreadRowsOnScreen() As Integer
            Dim unread = 0
            For Each row As DataGridViewRow In messageGrid.Rows
                If Not Convert.ToBoolean(row.Cells("IsRead").Value) Then unread += 1
            Next
            Return unread
        End Function

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
