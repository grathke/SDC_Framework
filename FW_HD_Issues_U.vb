Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class FW_HD_Issues_U
        Inherits FW_Base_U

        Private ReadOnly issueId As Integer
        Private ReadOnly registrationId As Integer
        Private issue As HelpDeskDataAccess.IssueRecord
        Private originalStatus As String
        Private originalPriority As String
        Private categoryComboBox As ComboBox
        Private priorityComboBox As ComboBox
        Private statusComboBox As ComboBox
        Private subjectTextBox As TextBox
        Private descriptionTextBox As TextBox
        Private conversationHistoryPanel As FlowLayoutPanel
        Private responseTextBox As TextBox
        Private statusValueLabel As Label
        Private categoryLabel As Label
        Private priorityLabel As Label
        Private subjectLabel As Label
        Private descriptionLabel As Label
        Private responseLabel As Label
        Private statusLabel As Label
        Private conversationHistoryLabel As Label
        Private descriptionRequiredBorder As Panel
        Private responseRequiredBorder As Panel
        Private categoryRequiredBorder As Panel
        Private priorityRequiredBorder As Panel
        Private statusRequiredBorder As Panel
        Private subjectRequiredBorder As Panel
        Private attachmentButton As Button
        Private thinfinityButton As Button
        Private pendingAttachment As HelpDeskAttachmentUpload

        Private Const ConversationSeparator As String = "----------------------------------------"

        Public Sub New(Optional selectedIssueId As Integer = 0, Optional selectedRegistrationId As Integer = 0)
            issueId = selectedIssueId
            registrationId = If(selectedRegistrationId > 0, selectedRegistrationId, ResolveRegistrationId())
            Me.Text = If(issueId > 0, "Help Desk Issue Maintenance", "Help Desk Issue Maintenance - New")
            Me.FormBorderStyle = FormBorderStyle.Sizable
            Me.MinimumSize = New Size(920, 720)
            Me.ClientSize = New Size(900, 680)
            BuildLayout()
            AddHandler Me.Resize, AddressOf HelpDeskIssue_Resize
            ApplyMode()
            BindToForm()
        End Sub

        Protected Overrides Function OkButtonText() As String
            Return "Save"
        End Function

        Protected Overrides Function ShouldWarnOnCancel() As Boolean
            Return True
        End Function

        Protected Overrides Function GetTableNameOverride() As String
            Return "FW_HD_Issues"
        End Function

        Protected Overrides Sub BindToFormInternal()
            issue = If(issueId > 0, HelpDeskDataAccess.GetIssueById(issueId, registrationId), New HelpDeskDataAccess.IssueRecord With {
                .RegistrationID = registrationId,
                .IssueNumber = "HD-" & DateTime.UtcNow.ToString("yyyyMMddHHmmss", Globalization.CultureInfo.InvariantCulture),
                .Status = "New",
                .Priority = "Normal",
                .ReporterUserID = CurrentUserId()
            })
            If issue Is Nothing Then Throw New InvalidOperationException("The issue was not found in the current registration.")
            originalStatus = issue.Status
            originalPriority = issue.Priority
            CaptureOriginalRowVersion(issue.RowVersion)
            ConfigureLookupCombo(categoryComboBox, HelpDeskDataAccess.GetCategories(registrationId), "CategoryID", "CategoryName", If(issue.CategoryID, 0))
            priorityComboBox.SelectedItem = issue.Priority
            statusComboBox.SelectedItem = If(String.IsNullOrWhiteSpace(issue.Status), "New", issue.Status)
            statusValueLabel.DataBindings.Clear()
            statusValueLabel.DataBindings.Add("Text", issue, "Status", True)
            statusValueLabel.Text = statusComboBox.SelectedItem.ToString()
            BindText(subjectTextBox, issue, "Subject", issue.Subject)
            BindText(descriptionTextBox, issue, "Description", issue.Description)
            RenderConversationHistory(issue.ConversationText)
            UpdateHelpDeskRequiredState()
        End Sub

        Protected Overrides Sub ApplyMode()
            subjectTextBox.ReadOnly = issueId > 0
            descriptionTextBox.ReadOnly = issueId > 0
            categoryComboBox.Enabled = issueId = 0
            priorityComboBox.Enabled = issueId = 0 OrElse IsCurrentUserSupport()
            statusComboBox.Visible = False
            statusComboBox.Enabled = False
            statusLabel.Visible = True
            statusValueLabel.Visible = True
            responseTextBox.ReadOnly = False
            responseTextBox.Visible = issueId > 0
            responseLabel.Visible = issueId > 0
            responseRequiredBorder.Visible = issueId > 0 AndAlso String.IsNullOrWhiteSpace(responseTextBox.Text)
            UpdateResponseRequiredState()
        End Sub

        Protected Overrides Function GetAdditionalValidationMessageLines() As IEnumerable(Of String)
            Dim validationLines As New List(Of String)()

            If GetComboSelectedIdOrZero(categoryComboBox) <= 0 Then validationLines.Add("Category is required.")
            If priorityComboBox.SelectedItem Is Nothing OrElse String.IsNullOrWhiteSpace(priorityComboBox.SelectedItem.ToString()) Then validationLines.Add("Priority is required.")
            If String.IsNullOrWhiteSpace(statusValueLabel.Text) Then validationLines.Add("Status is required.")
            If String.IsNullOrWhiteSpace(subjectTextBox.Text) Then validationLines.Add("Subject is required.")
            If issueId = 0 AndAlso String.IsNullOrWhiteSpace(descriptionTextBox.Text) Then
                validationLines.Add("Request is required.")
            End If

            If issueId > 0 AndAlso String.IsNullOrWhiteSpace(responseTextBox.Text) Then
                validationLines.Add("Response is required.")
            End If

            Return validationLines
        End Function

        Protected Overrides Function TryBuildRecord() As Boolean
            If issueId = 0 AndAlso String.IsNullOrWhiteSpace(subjectTextBox.Text) Then
                Return False
            End If
            If issueId = 0 AndAlso String.IsNullOrWhiteSpace(descriptionTextBox.Text) Then
                Return False
            End If
            If issueId > 0 AndAlso String.IsNullOrWhiteSpace(responseTextBox.Text) Then
                Return False
            End If

            If IsCurrentUserSupport() Then
                Using statusChooser As New HelpDeskStatusChoiceForm()
                    If statusChooser.ShowDialog(Me) <> DialogResult.OK Then Return False
                    issue.Status = statusChooser.SelectedStatus
                    statusValueLabel.Text = statusChooser.SelectedStatus
                End Using
            End If

            issue.Subject = subjectTextBox.Text.Trim()
            issue.Description = descriptionTextBox.Text.Trim()
            issue.CategoryID = GetComboSelectedIdOrZero(categoryComboBox)
            If Not issue.CategoryID.HasValue OrElse issue.CategoryID.Value = 0 Then issue.CategoryID = Nothing
            If priorityComboBox.SelectedItem IsNot Nothing Then issue.Priority = priorityComboBox.SelectedItem.ToString()
            If Not IsCurrentUserSupport() Then issue.Status = statusValueLabel.Text.Trim()
            Return True
        End Function

        Protected Overrides Function SaveRecord() As Boolean
            Try
                If Not HelpDeskDataAccess.SaveIssue(issue, responseTextBox.Text, pendingAttachment) Then Return False
                pendingAttachment = Nothing
                responseTextBox.Clear()
                Return True
            Catch ex As Exception
                MessageBox.Show(Me, "The issue could not be saved: " & ex.Message, "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End Try
        End Function

        Private Sub BuildLayout()
            categoryComboBox = New ComboBox() With {.Name = "ComboBox_CategoryID", .Location = New Point(150, 20), .Size = New Size(300, 26), .DropDownStyle = ComboBoxStyle.DropDownList}
            priorityComboBox = New ComboBox() With {.Name = "ComboBox_Priority", .Location = New Point(150, 60), .Size = New Size(300, 26), .DropDownStyle = ComboBoxStyle.DropDownList}
            priorityComboBox.Items.AddRange(New Object() {"Low", "Normal", "High", "Critical"})
            statusComboBox = New ComboBox() With {.Name = "ComboBox_Status", .Location = New Point(150, 100), .Size = New Size(300, 26), .DropDownStyle = ComboBoxStyle.DropDownList}
            statusComboBox.Items.AddRange(New Object() {"New", "Assigned", "In Progress", "Waiting for User", "Resolved", "Closed", "Reopened"})
            statusValueLabel = New Label() With {
                .Name = "Label_StatusValue",
                .Location = New Point(150, 104),
                .Size = New Size(300, 22),
                .AutoSize = False,
                .TextAlign = ContentAlignment.MiddleLeft,
                .BorderStyle = BorderStyle.None,
                .BackColor = Color.White
            }
            AddHandler statusValueLabel.Paint, AddressOf StatusValueLabel_Paint
            subjectTextBox = New TextBox() With {.Name = "TextBox_Subject", .Location = New Point(150, 140), .Size = New Size(700, 26)}
            descriptionTextBox = New TextBox() With {.Name = "TextBox_Description", .Location = New Point(150, 180), .Size = New Size(700, 70), .Multiline = True, .ScrollBars = ScrollBars.Vertical}
            responseTextBox = New TextBox() With {.Name = "TextBox_Response", .Location = New Point(150, 270), .Size = New Size(700, 90), .Multiline = True, .ScrollBars = ScrollBars.Vertical}
            conversationHistoryPanel = New FlowLayoutPanel() With {
                .Name = "Panel_ConversationHistory",
                .Location = New Point(150, 380),
                .Size = New Size(700, 210),
                .AutoScroll = True,
                .FlowDirection = FlowDirection.TopDown,
                .WrapContents = False,
                .BackColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle,
                .Padding = New Padding(6),
                .TabStop = False
            }
            categoryLabel = AddLabel("Category *", 20, 20)
            priorityLabel = AddLabel("Priority *", 20, 60)
            statusLabel = AddLabel("Status *", 20, 100)
            subjectLabel = AddLabel("Subject *", 20, 140)
            descriptionLabel = AddLabel("Request *", 20, 180)
            responseLabel = AddLabel("New Response *", 20, 270)
            conversationHistoryLabel = AddLabel("Conversation History", 20, 380)
            Dim requiredLabelBackColor = Color.FromArgb(221, 235, 247)
            categoryLabel.BackColor = requiredLabelBackColor
            priorityLabel.BackColor = requiredLabelBackColor
            statusLabel.BackColor = requiredLabelBackColor
            subjectLabel.BackColor = requiredLabelBackColor
            descriptionLabel.BackColor = Color.FromArgb(221, 235, 247)
            responseLabel.BackColor = Color.FromArgb(221, 235, 247)
            Me.Controls.AddRange({categoryComboBox, priorityComboBox, statusComboBox, statusValueLabel, subjectTextBox, descriptionTextBox, conversationHistoryPanel, responseTextBox})
            categoryRequiredBorder = CreateRequiredBorder(categoryComboBox)
            priorityRequiredBorder = CreateRequiredBorder(priorityComboBox)
            statusRequiredBorder = CreateRequiredBorder(statusComboBox)
            subjectRequiredBorder = CreateRequiredBorder(subjectTextBox)
            Me.Controls.AddRange({categoryRequiredBorder, priorityRequiredBorder, statusRequiredBorder, subjectRequiredBorder})
            categoryRequiredBorder.SendToBack()
            priorityRequiredBorder.SendToBack()
            statusRequiredBorder.SendToBack()
            subjectRequiredBorder.SendToBack()
            descriptionRequiredBorder = New Panel() With {
                .BackColor = Color.Red,
                .Location = New Point(descriptionTextBox.Left - 2, descriptionTextBox.Top - 2),
                .Size = New Size(descriptionTextBox.Width + 4, descriptionTextBox.Height + 4)
            }
            Me.Controls.Add(descriptionRequiredBorder)
            descriptionRequiredBorder.SendToBack()
            descriptionTextBox.BringToFront()
            responseRequiredBorder = New Panel() With {
                .BackColor = Color.Red,
                .Location = New Point(responseTextBox.Left - 2, responseTextBox.Top - 2),
                .Size = New Size(responseTextBox.Width + 4, responseTextBox.Height + 4)
            }
            Me.Controls.Add(responseRequiredBorder)
            responseRequiredBorder.SendToBack()
            responseTextBox.BringToFront()
            attachmentButton = New Button() With {.Name = "Button_AttachFile", .Text = "Attach File", .Location = New Point(150, 525), .Size = New Size(130, 34)}
            thinfinityButton = New Button() With {.Name = "Button_AttachViaThinfinity", .Text = "Attach via Thinfinity", .Location = New Point(290, 525), .Size = New Size(170, 34)}
            AddHandler attachmentButton.Click, AddressOf AttachFile_Click
            AddHandler thinfinityButton.Click, AddressOf AttachViaThinfinity_Click
            Me.Controls.AddRange({attachmentButton, thinfinityButton})
            LayoutControls()
            BindDirtyHandlers()
        End Sub

        Private Sub UpdateResponseRequiredState()
            Dim responseRequired = issueId > 0
            Dim hasResponse = Not String.IsNullOrWhiteSpace(responseTextBox.Text)
            Dim requestRequired = issueId = 0
            Dim hasRequest = Not String.IsNullOrWhiteSpace(descriptionTextBox.Text)
            descriptionLabel.Visible = requestRequired
            descriptionRequiredBorder.Visible = requestRequired AndAlso Not hasRequest
            responseLabel.Visible = responseRequired
            responseTextBox.Visible = responseRequired
            responseRequiredBorder.Visible = responseRequired AndAlso Not hasResponse
            attachmentButton.Enabled = Not responseRequired OrElse hasResponse
            thinfinityButton.Enabled = Not responseRequired OrElse hasResponse
        End Sub

        Private Sub HelpDeskIssue_Resize(sender As Object, e As EventArgs)
            LayoutControls()
        End Sub

        Private Sub LayoutControls()
            If subjectTextBox Is Nothing Then Return

            Dim inputLeft = 150
            Dim inputRight = 50
            Dim inputWidth = Math.Max(300, ClientSize.Width - inputLeft - inputRight)
            Dim actionTop = ClientSize.Height - 80
            Dim attachmentTop = actionTop - 75

            subjectTextBox.Width = inputWidth
            descriptionTextBox.Width = inputWidth
            responseTextBox.Width = inputWidth
            conversationHistoryPanel.Width = inputWidth
            Dim conversationTop = If(issueId > 0, 380, 280)
            conversationHistoryPanel.Top = conversationTop
            conversationHistoryLabel.Top = conversationTop + 4
            conversationHistoryPanel.Height = Math.Max(140, attachmentTop - conversationHistoryPanel.Top - 15)

            attachmentButton.Top = attachmentTop
            thinfinityButton.Top = attachmentTop

            responseRequiredBorder.Location = New Point(responseTextBox.Left - 2, responseTextBox.Top - 2)
            responseRequiredBorder.Size = New Size(responseTextBox.Width + 4, responseTextBox.Height + 4)
            descriptionRequiredBorder.Location = New Point(descriptionTextBox.Left - 2, descriptionTextBox.Top - 2)
            descriptionRequiredBorder.Size = New Size(descriptionTextBox.Width + 4, descriptionTextBox.Height + 4)
            categoryRequiredBorder.Location = New Point(categoryComboBox.Left - 2, categoryComboBox.Top - 2)
            categoryRequiredBorder.Size = New Size(categoryComboBox.Width + 4, categoryComboBox.Height + 4)
            priorityRequiredBorder.Location = New Point(priorityComboBox.Left - 2, priorityComboBox.Top - 2)
            priorityRequiredBorder.Size = New Size(priorityComboBox.Width + 4, priorityComboBox.Height + 4)
            statusRequiredBorder.Location = New Point(statusValueLabel.Left - 2, statusValueLabel.Top - 2)
            statusRequiredBorder.Size = New Size(statusValueLabel.Width + 4, statusValueLabel.Height + 4)
            subjectRequiredBorder.Location = New Point(subjectTextBox.Left - 2, subjectTextBox.Top - 2)
            subjectRequiredBorder.Size = New Size(subjectTextBox.Width + 4, subjectTextBox.Height + 4)

            cancelActionButton.Location = New Point(ClientSize.Width - 20 - cancelActionButton.Width, actionTop)
            okButton.Location = New Point(cancelActionButton.Left - 10 - okButton.Width, actionTop)
            enumButton.Location = New Point(20, actionTop)

            If issue IsNot Nothing Then
                RenderConversationHistory(issue.ConversationText)
            End If
        End Sub

        Private Sub RenderConversationHistory(conversationText As String)
            If conversationHistoryPanel Is Nothing Then Return

            conversationHistoryPanel.SuspendLayout()
            conversationHistoryPanel.Controls.Clear()

            Dim entries = If(conversationText, String.Empty).Split(New String() {ConversationSeparator}, StringSplitOptions.RemoveEmptyEntries)
            Dim entryWidth = Math.Max(100, conversationHistoryPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12)

            For entryIndex = 0 To entries.Length - 1
                Dim entryText = entries(entryIndex).Trim()
                If entryText.Length = 0 Then Continue For

                Dim entryPanel As New Panel() With {
                    .Width = entryWidth,
                    .Margin = New Padding(0),
                    .Padding = New Padding(10, 8, 10, 8),
                    .BackColor = If(entryIndex Mod 2 = 0, Color.FromArgb(231, 244, 231), Color.FromArgb(245, 250, 241))
                }
                Dim textWidth = Math.Max(80, entryWidth - entryPanel.Padding.Horizontal)
                Dim textSize = TextRenderer.MeasureText(entryText,
                                                        Font,
                                                        New Size(textWidth, Integer.MaxValue),
                                                        TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
                Dim entryLabel As New Label() With {
                    .Text = entryText,
                    .Location = New Point(entryPanel.Padding.Left, entryPanel.Padding.Top),
                    .Size = New Size(textWidth, Math.Max(Font.Height, textSize.Height)),
                    .AutoSize = False
                }
                entryPanel.Height = entryLabel.Height + entryPanel.Padding.Vertical
                entryPanel.Controls.Add(entryLabel)
                conversationHistoryPanel.Controls.Add(entryPanel)

                If entryIndex < entries.Length - 1 Then
                    conversationHistoryPanel.Controls.Add(New Panel() With {
                        .Width = entryWidth,
                        .Height = 1,
                        .Margin = New Padding(0, 8, 0, 8),
                        .BackColor = Color.FromArgb(103, 135, 103)
                    })
                End If
            Next

            conversationHistoryPanel.ResumeLayout()
        End Sub

        Private Function AddLabel(text As String, x As Integer, y As Integer) As Label
            Dim label = New Label() With {.Text = text, .Location = New Point(x, y + 4), .AutoSize = True}
            Me.Controls.Add(label)
            Return label
        End Function

        Private Sub BindText(control As TextBox, source As Object, propertyName As String, value As String)
            control.DataBindings.Clear()
            control.DataBindings.Add("Text", source, propertyName, True)
            control.Text = If(value, String.Empty)
        End Sub

        Private Sub BindDirtyHandlers()
            AddHandler subjectTextBox.TextChanged, AddressOf MarkDirty
            AddHandler descriptionTextBox.TextChanged, AddressOf MarkDirty
            AddHandler descriptionTextBox.TextChanged, AddressOf DescriptionTextBox_TextChanged
            AddHandler responseTextBox.TextChanged, AddressOf MarkDirty
            AddHandler responseTextBox.TextChanged, AddressOf ResponseTextBox_TextChanged
            AddHandler categoryComboBox.SelectedValueChanged, AddressOf HelpDeskFieldChanged
            AddHandler priorityComboBox.SelectedValueChanged, AddressOf MarkDirty
            AddHandler priorityComboBox.SelectedValueChanged, AddressOf HelpDeskFieldChanged
            AddHandler statusComboBox.SelectedValueChanged, AddressOf HelpDeskFieldChanged
            AddHandler subjectTextBox.TextChanged, AddressOf HelpDeskFieldChanged
        End Sub

        Private Sub ResponseTextBox_TextChanged(sender As Object, e As EventArgs)
            UpdateResponseRequiredState()
        End Sub

        Private Sub StatusValueLabel_Paint(sender As Object, e As PaintEventArgs)
            Using borderPen As New Pen(Color.FromArgb(160, 160, 160))
                e.Graphics.DrawRectangle(borderPen, 0, 0, statusValueLabel.Width - 1, statusValueLabel.Height - 1)
            End Using
        End Sub

        Private Sub HelpDeskFieldChanged(sender As Object, e As EventArgs)
            MarkDirty(sender, e)
            UpdateHelpDeskRequiredState()
        End Sub

        Private Sub UpdateHelpDeskRequiredState()
            If categoryRequiredBorder Is Nothing Then Return
            categoryRequiredBorder.Visible = GetComboSelectedIdOrZero(categoryComboBox) <= 0
            priorityRequiredBorder.Visible = priorityComboBox.SelectedItem Is Nothing OrElse String.IsNullOrWhiteSpace(priorityComboBox.SelectedItem.ToString())
            statusRequiredBorder.Visible = String.IsNullOrWhiteSpace(statusValueLabel.Text)
            subjectRequiredBorder.Visible = String.IsNullOrWhiteSpace(subjectTextBox.Text)
        End Sub

        Private Shared Function CreateRequiredBorder(control As Control) As Panel
            Return New Panel With {
                .BackColor = Color.Red,
                .Location = New Point(control.Left - 2, control.Top - 2),
                .Size = New Size(control.Width + 4, control.Height + 4),
                .Visible = False
            }
        End Function

        Private Sub DescriptionTextBox_TextChanged(sender As Object, e As EventArgs)
            UpdateResponseRequiredState()
        End Sub

        Private Sub AttachFile_Click(sender As Object, e As EventArgs)
            pendingAttachment = New WindowsHelpDeskAttachmentPicker().Pick(Me)
            If pendingAttachment IsNot Nothing Then MessageBox.Show(Me, "Attached: " & pendingAttachment.FileName, "Attachment", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub AttachViaThinfinity_Click(sender As Object, e As EventArgs)
            MessageBox.Show(Me, "Thinfinity attachment is not configured in this application yet.", "Attachment", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Function ResolveRegistrationId() As Integer
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then Return SessionState.Current.Value.RegistrationID
            Return 0
        End Function

        Private Function CurrentUserId() As Integer
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then Return SessionState.Current.Value.UserID
            Return 0
        End Function

        Private Function IsCurrentUserSupport() As Boolean
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return False
            Dim session = SessionState.Current.Value
            Return session.IsApplicationAdminRole OrElse session.IsCompanyAdminRole
        End Function
    End Class
End Namespace