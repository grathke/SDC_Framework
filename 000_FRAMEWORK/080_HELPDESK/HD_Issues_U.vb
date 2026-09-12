Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

Namespace SDC.Framework
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

        ''' The files already on this issue, one link each. Flows left to right and wraps, so a
        ''' issue with one attachment costs one line and one with six costs two.
        Private attachmentsPanel As FlowLayoutPanel

        ''' What GetAttachments last returned, kept so the conversation entries can draw their own
        ''' links without asking the database again for every entry.
        Private attachments As New List(Of HelpDeskAttachmentSummary)()
        Private responseTextBox As TextBox
        Private statusValueLabel As Label
        Private categoryLabel As Label
        Private priorityLabel As Label
        Private subjectLabel As Label
        Private descriptionLabel As Label
        Private responseLabel As Label
        Private statusLabel As Label
        Private conversationHistoryLabel As Label
        Private ReadOnly reportingPage As String = String.Empty
        Private expectedBehaviorLabel As Label
        Private expectedBehaviorTextBox As TextBox
        Private stepsToReproduceLabel As Label
        Private stepsToReproduceTextBox As TextBox
        Private categoryWantsExpectedBehavior As Boolean
        Private categoryWantsStepsToReproduce As Boolean
        Private categoryDescribeThe As String = String.Empty
        Private categoryTable As DataTable
        Private copyForClaudeButton As Button
        Private statusRequiredBorder As Panel
        Private attachmentButton As Button
        ''' Files chosen but not yet saved. A list because a user attaches a screenshot *and* a
        ''' log, and the previous single field meant the second choice silently discarded the
        ''' first.
        Private ReadOnly pendingAttachments As New List(Of HelpDeskAttachmentUpload)()

        Private Const ConversationSeparator As String = "----------------------------------------"

        Protected Overrides Function BuildMaintenanceTitle() As String
            Return If(issueId > 0, "Help Desk Issue Maintenance", "Help Desk Issue Maintenance - New")
        End Function

        Public Sub New(Optional selectedIssueId As Integer = 0, Optional selectedRegistrationId As Integer = 0, Optional reportedFromPage As String = "")
            issueId = selectedIssueId
            registrationId = If(selectedRegistrationId > 0, selectedRegistrationId, ResolveRegistrationId())

            ' Empty when the help desk was opened from the main menu rather than from a page. That
            ' is not a gap to fill in later: it decides which categories can be chosen, because a
            ' defect cannot be reported without the page it happened on.
            reportingPage = If(reportedFromPage, String.Empty).Trim()

            Me.FormBorderStyle = FormBorderStyle.Sizable
            Me.MinimumSize = New Size(920, 900)
            Me.ClientSize = New Size(900, 880)
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
            categoryTable = HelpDeskDataAccess.GetCategories(registrationId)
            ConfigureLookupCombo(categoryComboBox, categoryTable, "CategoryID", "CategoryName", If(issue.CategoryID, 0))
            ApplyCategoryRules()
            priorityComboBox.SelectedItem = issue.Priority
            statusComboBox.SelectedItem = If(String.IsNullOrWhiteSpace(issue.Status), "New", issue.Status)
            statusValueLabel.DataBindings.Clear()
            statusValueLabel.DataBindings.Add("Text", issue, "Status", True)
            statusValueLabel.Text = statusComboBox.SelectedItem.ToString()
            BindText(subjectTextBox, issue, "Subject", issue.Subject)
            BindText(descriptionTextBox, issue, "Description", issue.Description)
            RenderConversationHistory(issue.ConversationText)
            LoadAttachments()
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
            SetFieldRequired(responseTextBox, issueId > 0)

            ' A developer diagnostic rather than part of the workflow - the report it copies names
            ' the source file behind the page - so it is offered to an App Admin only. Tidiness
            ' rather than a control: everything in the report is already on this form, and the
            ' button is not a boundary anything depends on.
            '
            ' Narrower than IsCurrentUserSupport, deliberately. A Company Admin acts as support here
            ' - they set priority and status - but is not who the source file name is for.
            copyForClaudeButton.Visible = IsCurrentUserApplicationAdmin()

            UpdateResponseRequiredState()
        End Sub

        Protected Overrides Function GetAdditionalValidationMessageLines() As IEnumerable(Of String)
            Dim validationLines As New List(Of String)()

            ' Category, Priority, Status, Subject and the describe fields all carry the Required tag,
            ' so the shared check names them by caption. Only Status needs saying here: it is shown
            ' in a label rather than an input, so nothing else notices it is blank.
            If String.IsNullOrWhiteSpace(statusValueLabel.Text) Then validationLines.Add("Status is required.")

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
            issue.ExpectedBehavior = If(expectedBehaviorTextBox.Visible, expectedBehaviorTextBox.Text.Trim(), String.Empty)
            issue.StepsToReproduce = If(stepsToReproduceTextBox.Visible, stepsToReproduceTextBox.Text.Trim(), String.Empty)
            issue.ReportedFromPage = If(String.IsNullOrWhiteSpace(reportingPage), "MainMenu", reportingPage)
            issue.CategoryID = GetComboSelectedIdOrZero(categoryComboBox)
            If Not issue.CategoryID.HasValue OrElse issue.CategoryID.Value = 0 Then issue.CategoryID = Nothing
            If priorityComboBox.SelectedItem IsNot Nothing Then issue.Priority = priorityComboBox.SelectedItem.ToString()
            If Not IsCurrentUserSupport() Then issue.Status = statusValueLabel.Text.Trim()
            Return True
        End Function

        Protected Overrides Function SaveRecord() As Boolean
            Try
                If Not HelpDeskDataAccess.SaveIssue(issue, responseTextBox.Text, pendingAttachments) Then Return False
                pendingAttachments.Clear()
                responseTextBox.Clear()
                LoadAttachments()
                Return True
            Catch ex As Exception
                MessageBox.Show(Me, "The issue could not be saved: " & ex.Message, "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End Try
        End Function

        Private Sub BuildLayout()
            categoryComboBox = AddComboField("CategoryID", 20, True, 20, 300, "Category")
            priorityComboBox = AddComboField("Priority", 60, True, 20, 300)
            priorityComboBox.Items.AddRange(New Object() {"Low", "Normal", "High", "Critical"})

            ' Status is the documented exception. The combo is never shown - it holds the value while
            ' statusValueLabel displays it and a chooser dialog sets it - so the required border has
            ' to track the label the user actually sees. AddComboField owns a border for its own
            ' combo, which here would be a red rectangle behind an invisible control.
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
            subjectTextBox = AddField("Subject", 140, False, True, 20, False, 700)
            descriptionTextBox = AddField("Description", 180, False, True, 20, True, 700, 70, "Describe The Problem")
            expectedBehaviorTextBox = AddField("ExpectedBehavior", 260, False, True, 20, True, 700, 70, "How It Should Behave")
            stepsToReproduceTextBox = AddField("StepsToReproduce", 340, False, True, 20, True, 700, 70, "Steps To Reproduce")
            responseTextBox = AddField("Response", 270, False, True, 20, True, 700, 90, "New Response")

            ' A new response is written to the conversation table against the issue, not to a column
            ' of FW_HD_Issues, so this control maps to nothing by design.
            DeclareUnboundField("TextBox_Response", "New responses are rows in the issue conversation, not a column of FW_HD_Issues.")
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
            attachmentsPanel = New FlowLayoutPanel() With {
                .Name = "Panel_Attachments",
                .Location = New Point(150, 600),
                .Size = New Size(700, 30),
                .FlowDirection = FlowDirection.LeftToRight,
                .WrapContents = True,
                .AutoScroll = False,
                .BackColor = Color.Transparent,
                .Padding = New Padding(0),
                .TabStop = False
            }
            Me.Controls.Add(attachmentsPanel)

            statusLabel = AddLabel("Status *", 20, 100, "Status")
            conversationHistoryLabel = AddLabel("Conversation History", 20, 380)

            ' The labels the helpers created, kept so the page can position them and rename the
            ' first one as the category changes.
            categoryLabel = FieldLabel("CategoryID")
            priorityLabel = FieldLabel("Priority")
            subjectLabel = FieldLabel("Subject")
            descriptionLabel = FieldLabel("Description")
            expectedBehaviorLabel = FieldLabel("ExpectedBehavior")
            stepsToReproduceLabel = FieldLabel("StepsToReproduce")
            responseLabel = FieldLabel("Response")

            ' Category and Priority are painted by AddComboField. Status is not, because its label
            ' is the page's own.
            statusLabel.BackColor = FW_Base_U.AppAdminRequiredBackColor

            Me.Controls.AddRange({statusComboBox, statusValueLabel, conversationHistoryPanel})
            statusRequiredBorder = CreateRequiredBorder(statusComboBox)
            Me.Controls.Add(statusRequiredBorder)
            statusRequiredBorder.SendToBack()
            copyForClaudeButton = New Button() With {.Name = "Button_CopyReport", .Text = "Copy Report", .Location = New Point(470, 525), .Size = New Size(130, 34)}
            AddHandler copyForClaudeButton.Click, AddressOf CopyForClaudeButton_Click
            Me.Controls.Add(copyForClaudeButton)
            ' One button. There were two - one for the desktop dialog and one saying Thinfinity
            ' was not configured - which made the user choose between two implementations of a
            ' single action, only one of which is ever correct. The environment decides now.
            attachmentButton = New Button() With {.Name = "Button_AttachFile", .Text = "Attach File", .Location = New Point(150, 525), .Size = New Size(130, 34)}
            AddHandler attachmentButton.Click, AddressOf AttachFile_Click
            Me.Controls.Add(attachmentButton)
            LayoutControls()
            BindDirtyHandlers()
        End Sub

        Private Sub UpdateResponseRequiredState()
            Dim responseRequired = issueId > 0
            Dim hasResponse = Not String.IsNullOrWhiteSpace(responseTextBox.Text)
            Dim requestRequired = issueId = 0
            Dim hasRequest = Not String.IsNullOrWhiteSpace(descriptionTextBox.Text)
            descriptionLabel.Visible = requestRequired
            descriptionTextBox.Visible = requestRequired
            SetFieldRequired(descriptionTextBox, requestRequired)

            ' Only asked for on the categories flagged for them, and required only while asked for:
            ' a hidden field must never be the reason a save is refused.
            Dim wantsExpected = requestRequired AndAlso categoryWantsExpectedBehavior
            Dim wantsSteps = requestRequired AndAlso categoryWantsStepsToReproduce
            expectedBehaviorLabel.Visible = wantsExpected
            expectedBehaviorTextBox.Visible = wantsExpected
            SetFieldRequired(expectedBehaviorTextBox, wantsExpected)
            stepsToReproduceLabel.Visible = wantsSteps
            stepsToReproduceTextBox.Visible = wantsSteps
            SetFieldRequired(stepsToReproduceTextBox, wantsSteps)
            responseLabel.Visible = responseRequired
            responseTextBox.Visible = responseRequired
            SetFieldRequired(responseTextBox, responseRequired)
            attachmentButton.Enabled = Not responseRequired OrElse hasResponse
        End Sub

        Private Sub HelpDeskIssue_Resize(sender As Object, e As EventArgs)
            LayoutControls()
        End Sub

        Private Sub LayoutControls()
            If subjectTextBox Is Nothing Then Return

            Dim captions As Label() = {categoryLabel, priorityLabel, statusLabel, subjectLabel,
                                         descriptionLabel, expectedBehaviorLabel, stepsToReproduceLabel,
                                         responseLabel, conversationHistoryLabel}
            Dim widestCaption = 0
            For Each caption In captions
                If caption Is Nothing Then Continue For
                widestCaption = Math.Max(widestCaption, caption.Left + caption.PreferredWidth)
            Next

            Dim inputLeft = Math.Max(150, widestCaption + 12)
            Dim inputRight = 50
            Dim inputWidth = Math.Max(300, ClientSize.Width - inputLeft - inputRight)

            For Each field As Control In New Control() {categoryComboBox, priorityComboBox, statusComboBox,
                                                       statusValueLabel, subjectTextBox, descriptionTextBox,
                                                       expectedBehaviorTextBox, stepsToReproduceTextBox,
                                                       responseTextBox, conversationHistoryPanel}
                If field IsNot Nothing Then field.Left = inputLeft
            Next
            Dim actionTop = ClientSize.Height - 80
            Dim attachmentTop = actionTop - 75

            subjectTextBox.Width = inputWidth
            descriptionTextBox.Width = inputWidth
            responseTextBox.Width = inputWidth
            conversationHistoryPanel.Width = inputWidth
            Dim stackTop = descriptionTextBox.Top
            For Each field As Control In New Control() {descriptionTextBox, expectedBehaviorTextBox, stepsToReproduceTextBox, responseTextBox}
                If Not field.Visible Then Continue For
                field.Top = stackTop
                stackTop = field.Bottom + 30
            Next

            descriptionLabel.Top = descriptionTextBox.Top
            expectedBehaviorLabel.Top = expectedBehaviorTextBox.Top
            stepsToReproduceLabel.Top = stepsToReproduceTextBox.Top
            responseLabel.Top = responseTextBox.Top

            expectedBehaviorTextBox.Width = inputWidth
            stepsToReproduceTextBox.Width = inputWidth

            Dim conversationTop = stackTop
            conversationHistoryPanel.Top = conversationTop
            conversationHistoryLabel.Top = conversationTop + 4
            ' The attachment strip sits between the conversation and the buttons, and only takes
            ' room when there is something in it - an issue with no files should not leave a gap.
            Dim attachmentsHeight = If(attachmentsPanel.Controls.Count > 0, 30, 0)
            attachmentsPanel.Left = inputLeft
            attachmentsPanel.Width = inputWidth
            attachmentsPanel.Height = attachmentsHeight
            attachmentsPanel.Top = attachmentTop - attachmentsHeight - 8
            attachmentsPanel.Visible = attachmentsHeight > 0

            conversationHistoryPanel.Height = Math.Max(120, attachmentsPanel.Top - conversationHistoryPanel.Top - 15)

            attachmentButton.Top = attachmentTop
            copyForClaudeButton.Top = attachmentTop
            copyForClaudeButton.Left = attachmentButton.Right + 10

            ' The framework re-seats the borders it owns. Status is the page's own, and tracks the
            ' label the user sees rather than the hidden combo it belongs to.
            statusRequiredBorder.Location = New Point(statusValueLabel.Left - 2, statusValueLabel.Top - 2)
            statusRequiredBorder.Size = New Size(statusValueLabel.Width + 4, statusValueLabel.Height + 4)
            RefreshRequiredBorderGeometry()

            cancelActionButton.Location = New Point(ClientSize.Width - 20 - cancelActionButton.Width, actionTop)
            okButton.Location = New Point(cancelActionButton.Left - 10 - okButton.Width, actionTop)

            If issue IsNot Nothing Then
                RenderConversationHistory(issue.ConversationText)
            End If
        End Sub

        ''' <summary>
        ''' Draws one link per attachment already saved against this issue.
        '''
        ''' Names and sizes only - GetAttachments leaves the bytes in the database, and a file is
        ''' read when somebody clicks it rather than when the page opens. An issue with six
        ''' screenshots would otherwise pull six files across the wire to draw six labels.
        ''' </summary>
        Private Sub LoadAttachments()
            If attachmentsPanel Is Nothing Then Return

            attachmentsPanel.SuspendLayout()
            attachmentsPanel.Controls.Clear()

            Try
                attachments = If(issue IsNot Nothing AndAlso issue.IssueID > 0,
                                 HelpDeskDataAccess.GetAttachments(issue.IssueID, ResolveRegistrationId()),
                                 New List(Of HelpDeskAttachmentSummary)())

                If issue IsNot Nothing AndAlso issue.IssueID > 0 Then
                    For Each file In attachments
                        Dim link As New LinkLabel() With {
                            .Text = file.FileName & "  [" & StampOf(file.CreatedOn) & "]",
                            .AutoSize = True,
                            .Margin = New Padding(0, 6, 16, 0),
                            .Tag = file.AttachmentID
                        }
                        AddHandler link.LinkClicked, AddressOf Attachment_LinkClicked
                        attachmentsPanel.Controls.Add(link)
                    Next
                End If
            Catch ex As Exception
                ' A page that cannot list its attachments is still a usable page. Say so once, in
                ' the strip itself, rather than with a dialog the user cannot act on.
                attachmentsPanel.Controls.Add(New Label() With {
                    .Text = "Attachments unavailable: " & ex.Message,
                    .AutoSize = True,
                    .ForeColor = Color.FromArgb(150, 60, 60),
                    .Margin = New Padding(0, 6, 16, 0)
                })
            Finally
                attachmentsPanel.ResumeLayout()
            End Try

            ' Redrawn because the entries carry the links now, and they were built before the
            ' attachments were known.
            If issue IsNot Nothing Then RenderConversationHistory(issue.ConversationText)

            LayoutControls()
        End Sub

        ''' <summary>
        ''' Which attachments belong to the entry starting at a given stamp.
        '''
        ''' The pairing is by time, because nothing in the database says which entry a file came
        ''' with - FW_HD_IssueAttachments.ConversationEntryID points at a table that does not exist
        ''' yet. Both stamps come from the same save, minute precision, one from .NET and one from
        ''' SQL, so the rule is "the latest entry at or before the file" rather than equality: a
        ''' save that straddles a minute boundary would otherwise pair with nothing.
        '''
        ''' It can be wrong when two saves land in the same minute. That is cosmetic - the link
        ''' appears under the neighbouring message and still downloads the right file - and it
        ''' stops being a guess when the conversation table arrives.
        ''' </summary>
        Private Function AttachmentsForEntry(entryStamps As List(Of DateTime), entryIndex As Integer) As List(Of HelpDeskAttachmentSummary)
            Dim owned As New List(Of HelpDeskAttachmentSummary)()
            Dim stamp = entryStamps(entryIndex)
            If stamp = DateTime.MinValue Then Return owned

            For Each file In attachments
                If file.CreatedOn = DateTime.MinValue Then Continue For

                ' The entry this file belongs to: the latest one that had already been written when
                ' the file was. Entries with no readable stamp cannot win.
                Dim bestIndex = -1
                Dim best = DateTime.MinValue
                For candidate = 0 To entryStamps.Count - 1
                    Dim candidateStamp = entryStamps(candidate)
                    If candidateStamp = DateTime.MinValue Then Continue For
                    If candidateStamp > file.CreatedOn.AddMinutes(1) Then Continue For
                    If bestIndex = -1 OrElse candidateStamp > best Then
                        bestIndex = candidate
                        best = candidateStamp
                    End If
                Next

                If bestIndex = entryIndex Then owned.Add(file)
            Next

            Return owned
        End Function

        ''' <summary>
        ''' The "[yyyy-MM-dd HH:mm UTC]" that FormatConversationEntry writes at the head of an
        ''' entry, or MinValue if this entry does not carry one - older entries, or text a user
        ''' pasted in.
        ''' </summary>
        Private Shared Function StampFromEntry(entryText As String) As DateTime
            Dim opened = If(entryText, String.Empty).IndexOf("["c)
            If opened < 0 Then Return DateTime.MinValue

            Dim closed = entryText.IndexOf("]"c, opened + 1)
            If closed < 0 Then Return DateTime.MinValue

            Dim inner = entryText.Substring(opened + 1, closed - opened - 1).Replace(" UTC", String.Empty).Trim()

            Dim parsed As DateTime
            If DateTime.TryParseExact(inner, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
                Return parsed
            End If

            Return DateTime.MinValue
        End Function

        ''' <summary>
        ''' The same stamp the conversation entries carry, deliberately.
        '''
        ''' An attachment's CreatedOn is SYSUTCDATETIME() written in the transaction that wrote the
        ''' entry, and FormatConversationEntry stamps entries "yyyy-MM-dd HH:mm UTC". Matching the
        ''' format is what lets the eye pair a file with the message it arrived with, until the
        ''' conversation table makes the relationship real.
        ''' </summary>
        Private Shared Function StampOf(createdOn As DateTime) As String
            If createdOn = DateTime.MinValue Then Return "no date"
            Return createdOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) & " UTC"
        End Function

        Private Sub Attachment_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
            Dim link = TryCast(sender, LinkLabel)
            If link Is Nothing OrElse link.Tag Is Nothing Then Return

            Try
                Dim file = HelpDeskDataAccess.GetAttachmentData(CInt(link.Tag), ResolveRegistrationId())
                If file Is Nothing Then
                    MessageBox.Show(Me, "That attachment is no longer available.", "Attachment", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return
                End If

                HelpDeskAttachmentPickers.DeliveryForCurrentSession().Deliver(Me, file)
            Catch ex As Exception
                MessageBox.Show(Me, "The attachment could not be opened: " & ex.Message, "Attachment", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub RenderConversationHistory(conversationText As String)
            If conversationHistoryPanel Is Nothing Then Return

            conversationHistoryPanel.SuspendLayout()
            conversationHistoryPanel.Controls.Clear()

            Dim entries = If(conversationText, String.Empty).Split(New String() {ConversationSeparator}, StringSplitOptions.RemoveEmptyEntries)
            Dim entryWidth = Math.Max(100, conversationHistoryPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12)

            Dim entryStamps As New List(Of DateTime)()
            For Each rawEntry In entries
                entryStamps.Add(StampFromEntry(rawEntry))
            Next

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
                entryPanel.Controls.Add(entryLabel)

                ' The files that arrived with this message, under its text.
                Dim nextTop = entryLabel.Bottom + 6
                For Each file In AttachmentsForEntry(entryStamps, entryIndex)
                    Dim link As New LinkLabel() With {
                        .Text = file.FileName,
                        .AutoSize = True,
                        .Location = New Point(entryPanel.Padding.Left, nextTop),
                        .Tag = file.AttachmentID
                    }
                    AddHandler link.LinkClicked, AddressOf Attachment_LinkClicked
                    entryPanel.Controls.Add(link)
                    nextTop = link.Bottom + 2
                Next

                entryPanel.Height = (nextTop - entryLabel.Top) + entryPanel.Padding.Vertical
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

        ''' <param name="fieldName">
        ''' The field this labels, so the label is named Label_&lt;FieldName&gt; and the shared
        ''' required message can quote the caption the user is reading rather than a column name.
        ''' </param>
        Private Function AddLabel(text As String, x As Integer, y As Integer, Optional fieldName As String = Nothing) As Label
            Dim label = New Label() With {.Text = text, .Location = New Point(x, y + 4), .AutoSize = True}
            If Not String.IsNullOrWhiteSpace(fieldName) Then label.Name = "Label_" & fieldName.Trim()
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
            AddHandler categoryComboBox.SelectedValueChanged, Sub() ApplyCategoryRules()
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

        ''' Category, Priority, Status and Subject are always required, so there is nothing for this
        ''' page to decide and nothing for it to paint: Base_U owns their borders once adopted.
        Private Sub UpdateHelpDeskRequiredState()
        End Sub

        ''' <summary>
        ''' Which of the describe fields this category asks for. The answer is data - two flags on
        ''' FW_HD_IssueCategories - so adding a category is a row, not an edit here.
        ''' </summary>
        Private Sub ApplyCategoryRules()
            categoryWantsExpectedBehavior = False
            categoryWantsStepsToReproduce = False
            categoryDescribeThe = String.Empty

            If categoryTable IsNot Nothing Then
                Dim selectedId = GetComboSelectedIdOrZero(categoryComboBox)
                For Each row As DataRow In categoryTable.Rows
                    If Convert.ToInt32(row("CategoryID")) <> selectedId Then Continue For
                    If categoryTable.Columns.Contains("RequiresExpectedBehavior") Then
                        categoryWantsExpectedBehavior = Convert.ToBoolean(row("RequiresExpectedBehavior"))
                    End If
                    If categoryTable.Columns.Contains("RequiresPage") Then
                        categoryWantsStepsToReproduce = Convert.ToBoolean(row("RequiresPage"))
                    End If
                    If categoryTable.Columns.Contains("DescribeThe") Then
                        categoryDescribeThe = Convert.ToString(row("DescribeThe")).Trim()
                    End If
                    Exit For
                Next
            End If

            ApplyDescribeCaption()
            UpdateResponseRequiredState()
            LayoutControls()
        End Sub

        ''' <summary>
        ''' The first box asks about whatever was chosen, so it is named after it: Suggestion gives
        ''' "Describe The Suggestion", Feature Request gives "Describe The Request". The last word of
        ''' the category carries the meaning, so that is the word used.
        ''' </summary>
        Private Sub ApplyDescribeCaption()
            If descriptionLabel Is Nothing Then Return

            ' The category names the word, and falls back to its last word when it does not.
            Dim subject = If(categoryDescribeThe <> String.Empty, categoryDescribeThe, LastWordOf(categoryComboBox.Text))
            descriptionLabel.Text = If(subject = String.Empty, "Describe The Problem *", "Describe The " & subject & " *")
        End Sub

        Private Shared Function LastWordOf(caption As String) As String
            Dim words = If(caption, String.Empty).
                Split({" "c, "/"c}, StringSplitOptions.RemoveEmptyEntries).
                Where(Function(word) word.Trim().Length > 0).
                ToList()

            If words.Count = 0 Then Return String.Empty

            Return words(words.Count - 1).Trim()
        End Function

        ''' <summary>
        ''' The whole report as text, for pasting somewhere it can be acted on. It names the page so
        ''' the file to open is obvious, then states what happened, what should have happened and
        ''' how to reproduce it - which is the difference between a report that can be worked and
        ''' one that starts with a round of questions.
        ''' </summary>
        Private Function BuildReportText() As String
            Dim lines As New List(Of String)()
            Dim page = If(String.IsNullOrWhiteSpace(reportingPage), "Main Menu", reportingPage)

            lines.Add("HELP DESK REPORT")
            lines.Add(New String("="c, 60))
            lines.Add("")
            lines.Add("PAGE: " & page)
            If Not String.IsNullOrWhiteSpace(reportingPage) Then
                lines.Add("SOURCE FILE: " & reportingPage & ".vb")
            End If
            lines.Add("CATEGORY: " & categoryComboBox.Text)
            lines.Add("PRIORITY: " & priorityComboBox.Text)
            lines.Add("STATUS: " & statusValueLabel.Text)
            If issueId > 0 Then lines.Add("TICKET: " & issueId.ToString(Globalization.CultureInfo.InvariantCulture))
            lines.Add("REGISTRATION: " & registrationId.ToString(Globalization.CultureInfo.InvariantCulture))
            lines.Add("REPORTED: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm", Globalization.CultureInfo.InvariantCulture))
            lines.Add("")
            lines.Add("SUBJECT")
            lines.Add(If(String.IsNullOrWhiteSpace(subjectTextBox.Text), "(none given)", subjectTextBox.Text.Trim()))
            lines.Add("")
            lines.Add("WHAT IS WRONG")
            lines.Add(If(String.IsNullOrWhiteSpace(descriptionTextBox.Text), "(none given)", descriptionTextBox.Text.Trim()))

            If expectedBehaviorTextBox.Visible Then
                lines.Add("")
                lines.Add("HOW IT SHOULD BEHAVE")
                lines.Add(If(String.IsNullOrWhiteSpace(expectedBehaviorTextBox.Text), "(none given)", expectedBehaviorTextBox.Text.Trim()))
            End If

            If stepsToReproduceTextBox.Visible Then
                lines.Add("")
                lines.Add("STEPS TO REPRODUCE")
                lines.Add(If(String.IsNullOrWhiteSpace(stepsToReproduceTextBox.Text), "(none given)", stepsToReproduceTextBox.Text.Trim()))
            End If

            lines.Add("")
            lines.Add(New String("-"c, 60))
            lines.Add("Investigate the page named above before changing anything, confirm the")
            lines.Add("behaviour described, and say what the fix would be before applying it.")

            Return String.Join(Environment.NewLine, lines)
        End Function

        Private Sub CopyForClaudeButton_Click(sender As Object, e As EventArgs)
            Try
                Clipboard.SetText(BuildReportText())
                MessageBox.Show(Me,
                                "THE REPORT HAS BEEN COPIED TO THE CLIPBOARD.",
                                "COPY REPORT",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information)
            Catch ex As Exception
                MessageBox.Show(Me,
                                "THE REPORT COULD NOT BE COPIED: " & ex.Message,
                                "COPY REPORT",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error)
            End Try
        End Sub

        ''' <summary>
        ''' The border Base_U adopts for this control. Tagging it hands the *when* to the framework -
        ''' red once visited and empty - while this page keeps the *whether*, by setting or clearing
        ''' the control's Required tag as its conditional rules change.
        ''' </summary>
        ''' The ticket form lays its own fields out in the order they are answered, and it is the
        ''' form used to report a problem - so neither the tab order manager nor a Help Desk button
        ''' belongs on it.
        Protected Overrides Function SupportsTabOrderManager() As Boolean
            Return False
        End Function

        Private Shared Function CreateRequiredBorder(control As Control) As Panel
            control.Tag = "Required"
            Return New Panel With {
                .Tag = "RequiredBorder_" & control.Name,
                .BackColor = Color.Red,
                .Location = New Point(control.Left - 2, control.Top - 2),
                .Size = New Size(control.Width + 4, control.Height + 4),
                .Visible = False
            }
        End Function

        ''' Marks a control required, or not, for the shared border rule. A control that is not
        ''' required never goes red however empty it is.
        Private Shared Sub SetFieldRequired(control As Control, required As Boolean)
            If control Is Nothing Then Return
            control.Tag = If(required, "Required", Nothing)
        End Sub

        Private Sub DescriptionTextBox_TextChanged(sender As Object, e As EventArgs)
            UpdateResponseRequiredState()
        End Sub

        Private Sub AttachFile_Click(sender As Object, e As EventArgs)
            Dim chosen = HelpDeskAttachmentPickers.ForCurrentSession().Pick(Me)
            If chosen Is Nothing Then Return

            pendingAttachments.Add(chosen)

            Dim waiting = If(pendingAttachments.Count = 1,
                             "1 file will be attached when you save.",
                             pendingAttachments.Count.ToString() & " files will be attached when you save.")
            MessageBox.Show(Me, "Attached: " & chosen.FileName & Environment.NewLine & Environment.NewLine & waiting,
                            "Attachment", MessageBoxButtons.OK, MessageBoxIcon.Information)
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

        Private Function IsCurrentUserApplicationAdmin() As Boolean
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return False
            Return SessionState.Current.Value.IsApplicationAdminRole
        End Function
    End Class
End Namespace
