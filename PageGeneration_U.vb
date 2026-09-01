Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text.RegularExpressions
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class PageGeneration_U
        Inherits FW_Base_U

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
            Private ReadOnly formBindingSource As New BindingSource()
        Private recordId As Integer
        Private requestNameTextBox As TextBox
        Private pageBaseNameTextBox As TextBox
        Private browsePageNameTextBox As TextBox
        Private maintenancePageNameTextBox As TextBox
        Private createAsFrameworkPagesCheckBox As CheckBox
        Private underlyingTableNameTextBox As TextBox
        Private generateBrowsePageCheckBox As CheckBox
        Private useQbeOnlyCheckBox As CheckBox
        Private generateMaintenancePageCheckBox As CheckBox
        Private useRegistrationIdCheckBox As CheckBox
        Private browseSqlTextBox As TextBox
        Private browseFieldsTextBox As TextBox
        Private maintenanceFieldsTextBox As TextBox
        Private directionsTextBox As ListBox
        Private lookupFieldsTextBox As TextBox
        Private adminRequiredFieldsTextBox As TextBox
        Private menuCallerComboBox As ComboBox
        Private iconFileNameTextBox As TextBox
        Private selectIconButton As Button
        Private iconPreviewBox As PictureBox
        Private pageRequestIdTextBox As TextBox
        Private createdByTextBox As TextBox
        Private createdOnTextBox As TextBox
        Private updatedByTextBox As TextBox
        Private updatedOnTextBox As TextBox
        Private deletedFlagCheckBox As CheckBox
        Private validateSqlButton As Button
        Private copyRequestButton As Button
        Private generatePagesButton As Button
        Private previewCodeButton As Button
        Private selectTableButton As Button
        Private selectFieldsButton As Button
        ' The caption line the base class draws on the form: caption at y=15, Tab Order button at
        ' y=10, both about 28 tall. Questions start below it, and the form is taller by the delta
        ' against the 12 the layout used before.
        Private Const MakeASelection As String = DataAccess.EmptyComboPlaceholder

        Private Const PageHeaderBandHeight As Integer = 50
        Private Const PageHeaderBandDelta As Integer = 38

        ''' Extra room for the Browse SQL editor, and the matching growth in the form so nothing
        ''' below it is squeezed.
        Private Const BrowseSqlExtraHeight As Integer = 60

        Private tableSelectionPanel As FlowLayoutPanel
        Private fieldSelectionPanel As FlowLayoutPanel
        Private iconSelectionPanel As FlowLayoutPanel
        Private directionsPanel As Panel
        Private directionsToggleButton As Button
        Private originalRowVersion As Byte()
        Private isNewRecord As Boolean
        Private validatedBrowseSql As String = String.Empty
        Private ReadOnly pageRequiredBorderPanels As New List(Of Panel)()
        Private pageRequiredValidationActivated As Boolean
        Private suppressMenuCallerPrompt As Boolean
        Private suppressSaveConfirmation As Boolean
        Private pageHasManualChanges As Boolean

        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New()
            recordId = id
            currentUser = user
            accessProfile = profile
            isNewRecord = id <= 0


            StartPosition = FormStartPosition.CenterParent
            ClientSize = New Size(1100, 705 + PageHeaderBandDelta + BrowseSqlExtraHeight)
            MinimumSize = New Size(900, 675 + PageHeaderBandDelta + BrowseSqlExtraHeight)

            BuildLayout()
            BindToForm()
        End Sub

        Protected Overrides Function GetTableNameOverride() As String
            Return "FW_PageGeneration_B_U"
        End Function

        Protected Overrides Function GetPageName() As String
            Return NameOf(PageGeneration_U)
        End Function

        Protected Overrides Function ResolveAuditRecordKey() As String
            Return pageRequestIdTextBox.Text.Trim()
        End Function

        Protected Overrides Function ResolveAuditOperationType() As String
            Return If(isNewRecord, "Create", "Update")
        End Function

        ''' The manual-changes warning is part of the title, not something appended to it after the
        ''' fact, so it survives a refresh.
        Protected Overrides Function BuildMaintenanceTitle() As String
            Dim title = If(isNewRecord, "New Page Generation Request", "Edit Page Generation Request")
            If pageHasManualChanges Then title &= " - MANUAL PAGE CHANGES DETECTED"
            Return title
        End Function

        Protected Overrides Function ShouldWarnOnCancel() As Boolean
            Return True
        End Function

        ' The questions are numbered and laid out in the order they must be answered, so there is
        ' no tab order for a user to rearrange.
        Protected Overrides Function SupportsTabOrderManager() As Boolean
            Return False
        End Function

        Private Sub BuildLayout()
            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Padding = New Padding(12, PageHeaderBandHeight, 12, 8), .AutoScroll = True}
            root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            Controls.Add(root)

            Dim fields As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .AutoSize = True}
            fields.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 215))
            fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            root.Controls.Add(fields, 0, 0)

            pageRequestIdTextBox = New TextBox With {.Name = "TextBox_PageRequestID", .Visible = False}
            Controls.Add(pageRequestIdTextBox)
            requestNameTextBox = AddEntryField(fields, "RequestName", False, 34, 150, False, "1. Request Name")
            pageBaseNameTextBox = AddEntryField(fields, "PageBaseName", False, 34, 150, False, "2. Pages To Generate")
            createAsFrameworkPagesCheckBox = New CheckBox With {
                .Name = "CheckBox_CreateAsFrameworkPages",
                .Text = "Create as Framework Pages",
                .Checked = False,
                .AutoSize = True,
                .Margin = New Padding(8, 6, 0, 0)
            }
            AddControlBesideField(fields, pageBaseNameTextBox, createAsFrameworkPagesCheckBox)
            browsePageNameTextBox = AddEntryField(fields, "BrowsePageName", False, 34, 150, False, "3. Browse Page Name")
            maintenancePageNameTextBox = AddEntryField(fields, "MaintenancePageName", False, 34, 150, False, "4. Maintenance Page Name")
            generateBrowsePageCheckBox = New CheckBox With {.Text = "Generate", .Checked = False, .AutoSize = True, .Margin = New Padding(8, 6, 0, 0)}
            useQbeOnlyCheckBox = New CheckBox With {.Name = "CheckBox_UseQbeOnly", .Text = "Use QBE only", .Checked = False, .AutoSize = True, .Margin = New Padding(8, 6, 0, 0)}
            generateMaintenancePageCheckBox = New CheckBox With {.Text = "Generate", .Checked = False, .AutoSize = True, .Margin = New Padding(8, 6, 0, 0)}
            Dim browseGenerationOptions As New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .Padding = New Padding(0)
            }
            browseGenerationOptions.Controls.Add(generateBrowsePageCheckBox)
            browseGenerationOptions.Controls.Add(useQbeOnlyCheckBox)
            AddControlBesideField(fields, browsePageNameTextBox, browseGenerationOptions)
            AddControlBesideField(fields, maintenancePageNameTextBox, generateMaintenancePageCheckBox)
            AddHandler pageBaseNameTextBox.Leave, AddressOf PageBaseNameTextBox_Leave
            AddHandler createAsFrameworkPagesCheckBox.CheckedChanged, AddressOf CreateAsFrameworkPagesCheckBox_CheckedChanged
            AddHandler generateBrowsePageCheckBox.CheckedChanged, AddressOf GenerateBrowsePageCheckBox_CheckedChanged
            underlyingTableNameTextBox = AddEntryField(fields, "UnderlyingTableName", True, 34, 150, False, "5. Underlying Table Name")
            AddHandler underlyingTableNameTextBox.TextChanged, AddressOf UnderlyingTableNameTextBox_TextChanged
            Dim underlyingTableRow = fields.GetRow(underlyingTableNameTextBox)
            fields.Controls.Remove(underlyingTableNameTextBox)
            ' Question 5 stacks the chosen table under the button that chose it. Both live in a
            ' FlowLayoutPanel so the framework hosts their focus borders instead of adding a panel
            ' to the question grid.
            tableSelectionPanel = New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .AutoSize = False,
                .WrapContents = False,
                .FlowDirection = FlowDirection.TopDown,
                .Padding = New Padding(0)
            }
            selectTableButton = New Button With {
                .Text = "Select Table",
                .Size = New Size(110, 32),
                .FlatStyle = FlatStyle.Standard,
                .UseVisualStyleBackColor = True,
                .Margin = New Padding(0, 1, 0, 4)
            }
            AddHandler selectTableButton.Click, AddressOf SelectTableButton_Click
            tableSelectionPanel.Controls.Add(selectTableButton)
            tableSelectionPanel.Controls.Add(underlyingTableNameTextBox)
            fields.Controls.Add(tableSelectionPanel, 1, underlyingTableRow)
            fields.RowStyles(underlyingTableRow).Height = 80

            ' Question 6 is the Select Fields button. The four fields it fills hang off it as
            ' bullets rather than questions of their own, because none of them is answered by
            ' typing - the popup writes all four.
            fieldSelectionPanel = New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .AutoSize = False,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .Padding = New Padding(0)
            }
            selectFieldsButton = New Button With {
                .Text = "Select Fields",
                .Size = New Size(110, 32),
                .FlatStyle = FlatStyle.Standard,
                .UseVisualStyleBackColor = True,
                .Margin = New Padding(0, 1, 8, 1)
            }
            AddHandler selectFieldsButton.Click, AddressOf SelectFieldsButton_Click
            fieldSelectionPanel.Controls.Add(selectFieldsButton)
            AddQuestionRow(fields, "SelectFields", "6. Select Fields", fieldSelectionPanel, 46)

            browseFieldsTextBox = AddEntryField(fields, "BrowseFields", True, 34, 780, False, "   " & ChrW(8226) & " _B Fields")
            maintenanceFieldsTextBox = AddEntryField(fields, "MaintenanceFields", True, 34, 780, False, "   " & ChrW(8226) & " _U Fields")
            lookupFieldsTextBox = AddEntryField(fields, "LookupFields", True, 34, 780, False, "   " & ChrW(8226) & " Lookup Fields")
            adminRequiredFieldsTextBox = AddEntryField(fields, "AdminRequiredFields", True, 34, 780, False, "   " & ChrW(8226) & " Admin Required Fields")
            ' Menu Caller is chosen, not typed. The value has to match a real caller for the
            ' generator to place a dashboard icon, and a typo used to produce no icon silently.
            menuCallerComboBox = New ComboBox With {
                .Name = "ComboBox_MenuCaller",
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Width = 260,
                .Anchor = AnchorStyles.Left Or AnchorStyles.Top,
                .Margin = New Padding(0, 1, 0, 1)
            }
            menuCallerComboBox.Items.Add(MakeASelection)
            For Each callerOption In GetMainMenuCallerOptions()
                menuCallerComboBox.Items.Add(callerOption)
            Next
            For Each callerOption In GetDashboardCallerOptions()
                menuCallerComboBox.Items.Add(callerOption)
            Next
            menuCallerComboBox.SelectedIndex = 0
            AddHandler menuCallerComboBox.SelectedIndexChanged, AddressOf MenuCallerComboBox_SelectedIndexChanged
            AddQuestionRow(fields, "MenuCaller", "7. Menu Caller", menuCallerComboBox, 46)
            ' The dashboard button picture. Chosen from assets\images rather than typed, because a
            ' name that does not match a file there produces a button with the default glyph and no
            ' explanation. Only the file name is stored; the images live beside the exe.
            iconFileNameTextBox = AddEntryField(fields, "IconFileName", True, 34, 200, False, "8. Dashboard Icon")
            Dim iconRow = fields.GetRow(iconFileNameTextBox)
            fields.Controls.Remove(iconFileNameTextBox)
            iconSelectionPanel = New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .AutoSize = False,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .Padding = New Padding(0)
            }
            selectIconButton = New Button With {
                .Text = "Select Icon",
                .Size = New Size(110, 32),
                .FlatStyle = FlatStyle.Standard,
                .UseVisualStyleBackColor = True,
                .Margin = New Padding(0, 1, 8, 1)
            }
            AddHandler selectIconButton.Click, AddressOf SelectIconButton_Click
            iconPreviewBox = New PictureBox With {
                .Size = New Size(32, 32),
                .SizeMode = PictureBoxSizeMode.Zoom,
                .Margin = New Padding(8, 1, 0, 1)
            }
            iconSelectionPanel.Controls.Add(selectIconButton)
            iconSelectionPanel.Controls.Add(iconFileNameTextBox)
            iconSelectionPanel.Controls.Add(iconPreviewBox)
            fields.Controls.Add(iconSelectionPanel, 1, iconRow)
            fields.RowStyles(iconRow).Height = 46

            browseSqlTextBox = AddEntryField(fields, "BrowseSql", False, 165 + BrowseSqlExtraHeight, 780, True, "9. Browse SQL")
            AddHandler browseSqlTextBox.TextChanged, AddressOf BrowseSqlTextBox_TextChanged
            Dim browseSqlRow = fields.GetRow(browseSqlTextBox)
            fields.Controls.Remove(browseSqlTextBox)
            fields.RowStyles(browseSqlRow).Height = 205 + BrowseSqlExtraHeight
            Dim browseSqlPanel As New Panel With {
                .Dock = DockStyle.Fill,
                .Width = 780,
                .Height = 205 + BrowseSqlExtraHeight
            }
            browseSqlTextBox.Dock = DockStyle.Top
            browseSqlTextBox.Height = 165
            browseSqlPanel.Controls.Add(browseSqlTextBox)
            validateSqlButton = New Button With {
                .Text = "Validate SQL",
                .Size = New Size(110, 32),
                .Location = New Point(browseSqlPanel.Width - 110, 170),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            AddHandler validateSqlButton.Click, AddressOf ValidateSqlButton_Click
            browseSqlPanel.Controls.Add(validateSqlButton)
            fields.Controls.Add(browseSqlPanel, 1, browseSqlRow)
            useRegistrationIdCheckBox = New CheckBox With {
                .Name = "CheckBox_UseRegistrationID",
                .Text = "Use RegistrationID",
                .AutoSize = True,
                .Anchor = AnchorStyles.Left Or AnchorStyles.Top,
                .Margin = New Padding(0, 4, 0, 0)
            }
            AddFieldControl(fields, useRegistrationIdCheckBox, "10. Use RegistrationID from selected table")
            UpdateRegistrationOptionState()
            AddHandler useRegistrationIdCheckBox.CheckedChanged, AddressOf UseRegistrationIdCheckBox_CheckedChanged
            ApplyPageRequiredFieldStyling()

            directionsPanel = New Panel With {
                .Dock = DockStyle.Fill,
                .Height = 38,
                .Margin = New Padding(0, 8, 0, 0)
            }
            directionsToggleButton = New Button With {
                .Text = "SHOW GENERATION DIRECTIONS",
                .Dock = DockStyle.Top,
                .Height = 32,
                .FlatStyle = FlatStyle.Standard,
                .UseVisualStyleBackColor = True
            }
            directionsTextBox = New ListBox With {
                .Name = "ListBox_Directions",
                .Dock = DockStyle.Fill,
                .Visible = False,
                .Margin = New Padding(0, 6, 0, 0),
                .HorizontalScrollbar = True,
                .IntegralHeight = False,
                .SelectionMode = SelectionMode.None,
                .Font = New Font("Consolas", 9.0F),
                .FormattingEnabled = True
            }
            SetDirectionsDocument()
            AddHandler directionsToggleButton.Click, AddressOf DirectionsToggleButton_Click
            directionsPanel.Controls.Add(directionsTextBox)
            directionsPanel.Controls.Add(directionsToggleButton)
            root.Controls.Remove(fields)
            root.Controls.Add(directionsPanel, 0, 0)
            root.Controls.Add(fields, 0, 1)

            Dim metadata As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .AutoSize = True}
            metadata.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 215))
            metadata.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            root.Controls.Add(metadata, 0, 2)
            createdByTextBox = AddEntryField(metadata, "CreatedBy", True, 34, 180)
            createdOnTextBox = AddEntryField(metadata, "CreatedOn", True, 34, 220)
            updatedByTextBox = AddEntryField(metadata, "UpdatedBy", True, 34, 180)
            updatedOnTextBox = AddEntryField(metadata, "UpdatedOn", True, 34, 220)
            deletedFlagCheckBox = New CheckBox With {.Text = "DeletedFlag", .AutoSize = True, .Enabled = False}
            metadata.Controls.Add(deletedFlagCheckBox, 1, metadata.RowCount)
            metadata.RowCount += 1
            metadata.Visible = False

            Dim footer As New Panel With {
                .Dock = DockStyle.Bottom,
                .Height = 54,
                .Padding = New Padding(12, 8, 12, 8)
            }
            Controls.Add(footer)

            copyRequestButton = New Button With {
                .Text = "Preview",
                .Size = New Size(115, 36),
                .Location = New Point(footer.Width - 680, 8),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            AddHandler copyRequestButton.Click, AddressOf PreviewRequestButton_Click
            footer.Controls.Add(copyRequestButton)

            previewCodeButton = New Button With {
                .Text = "Preview Code",
                .Size = New Size(130, 36),
                .Location = New Point(footer.Width - 560, 8),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            AddHandler previewCodeButton.Click, AddressOf PreviewGeneratedCodeButton_Click
            footer.Controls.Add(previewCodeButton)

            generatePagesButton = New Button With {
                .Text = "Save && Generate",
                .Size = New Size(145, 36),
                .Location = New Point(footer.Width - 430, 8),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .Enabled = True
            }
            AddHandler generatePagesButton.Click, AddressOf GeneratePagesButton_Click
            footer.Controls.Add(generatePagesButton)

            okButton.Location = New Point(footer.Width - 270, 8)
            okButton.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            footer.Controls.Add(okButton)
            cancelActionButton.Location = New Point(footer.Width - 135, 8)
            cancelActionButton.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            footer.Controls.Add(cancelActionButton)
            okButton.Text = "Save"
            okButton.Enabled = True

            ApplyQuestionTabOrder()
        End Sub

        Private Sub GeneratePagesButton_Click(sender As Object, e As EventArgs)
            If pageHasManualChanges Then
                ShowManualPageChangesWarning()
                Return
            End If

            If Not generateBrowsePageCheckBox.Checked AndAlso Not generateMaintenancePageCheckBox.Checked Then
                MessageBox.Show(Me, "SELECT AT LEAST ONE PAGE TARGET TO GENERATE.", "GENERATE PAGES", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If Not generateBrowsePageCheckBox.Checked AndAlso generateMaintenancePageCheckBox.Checked Then
                MessageBox.Show(Me, "A MAINTENANCE PAGE REQUIRES A BROWSE PAGE. SELECT GENERATE BROWSE PAGE OR UNCHECK GENERATE MAINTENANCE PAGE.", "GENERATE PAGES", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            ' Validate before anything is asked or written. Prompting to overwrite page files for a
            ' request that cannot be generated asks the user to authorise work that will not happen.
            If Not ValidateAndBuildForSave() Then
                Return
            End If

            Dim browsePagePath = Path.Combine(Environment.CurrentDirectory, browsePageNameTextBox.Text.Trim() & ".vb")
            Dim maintenancePagePath = Path.Combine(Environment.CurrentDirectory, maintenancePageNameTextBox.Text.Trim() & ".vb")
            Dim overwriteExistingPages = False
            If (generateBrowsePageCheckBox.Checked AndAlso File.Exists(browsePagePath)) OrElse
                (generateMaintenancePageCheckBox.Checked AndAlso File.Exists(maintenancePagePath)) Then
                Dim selectedTargets As New List(Of String)()
                If generateBrowsePageCheckBox.Checked Then selectedTargets.Add("_B")
                If generateMaintenancePageCheckBox.Checked Then selectedTargets.Add("_U")
                Dim targetText = String.Join(" AND ", selectedTargets)
                Dim targetFileText = If(selectedTargets.Count = 1, "TARGET PAGE FILE", "TARGET PAGE FILES")
                Dim overwriteChoice = MessageBox.Show(Me,
                                                       "ONE OR MORE SELECTED " & targetFileText & " ALREADY EXIST." & Environment.NewLine & Environment.NewLine &
                                                       "YES: REGENERATE AND OVERWRITE THE " & targetText & " PAGE FILE" & If(selectedTargets.Count = 1, String.Empty, "S") & "." & Environment.NewLine &
                                                       "NO: RETURN WITHOUT SAVING OR GENERATING.",
                                                       "REGENERATE PAGES",
                                                       MessageBoxButtons.YesNo,
                                                       MessageBoxIcon.Question)
                If overwriteChoice <> DialogResult.Yes Then
                    Return
                End If
                overwriteExistingPages = True
            End If

            suppressSaveConfirmation = True
            Try
                If (isNewRecord OrElse hasUnsavedChanges) AndAlso Not SaveRecord() Then
                    Return
                End If
            Finally
                suppressSaveConfirmation = False
            End Try

            Dim generationRequestId = If(isNewRecord,
                                         DataAccess.GetPageGenerationId(requestNameTextBox.Text, browsePageNameTextBox.Text, maintenancePageNameTextBox.Text),
                                         recordId)
            If generationRequestId <= 0 Then
                MessageBox.Show(Me, "THE PAGE REQUEST WAS SAVED BUT ITS ID COULD NOT BE RESOLVED.", "GENERATE PAGES", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            Dim result = PageGenerator.Generate(generationRequestId, Environment.CurrentDirectory, overwriteExistingPages)
            Dim blockingErrors = result.Errors.Where(Function(errorMessage) Not errorMessage.StartsWith("ICON WARNING:", StringComparison.OrdinalIgnoreCase)).ToList()
            If blockingErrors.Count > 0 Then
                MessageBox.Show(Me, String.Join(Environment.NewLine, blockingErrors).ToUpperInvariant(), "GENERATE PAGES", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            Dim lines As New List(Of String) From {"PAGE GENERATION COMPLETED SUCCESSFULLY."}
            Dim browsePageName = browsePageNameTextBox.Text.Trim()
            Dim maintenancePageName = maintenancePageNameTextBox.Text.Trim()
            Dim browseReady = Not generateBrowsePageCheckBox.Checked OrElse IsGenerationResultPresent(result, ".vb", browsePageName)
            Dim maintenanceReady = Not generateMaintenancePageCheckBox.Checked OrElse IsGenerationResultPresent(result, ".vb", maintenancePageName)
            Dim pagesReady = browseReady AndAlso maintenanceReady
            Dim sqlReady = IsGenerationResultPresent(result, "FW_RoleTables", String.Empty)
            Dim createdPageResults = GetGenerationResults(result.CreatedFiles, False)
            Dim createdIconResults = GetGenerationResults(result.CreatedFiles, True)
            Dim skippedPageResults = GetGenerationResults(result.SkippedFiles, False)
            Dim skippedIconResults = GetGenerationResults(result.SkippedFiles, True)
            If createdPageResults.Count > 0 Then
                lines.Add(String.Empty)
                lines.Add("CREATED:")
                lines.AddRange(createdPageResults)
            End If
            If createdIconResults.Count > 0 Then
                lines.Add(String.Empty)
                lines.Add("ICON:")
                lines.AddRange(createdIconResults)
            End If
            If skippedPageResults.Count > 0 Then
                lines.Add(String.Empty)
                lines.Add("SKIPPED BECAUSE THE FILE ALREADY EXISTS:")
                lines.AddRange(skippedPageResults)
            End If
            If skippedIconResults.Count > 0 Then
                lines.Add(String.Empty)
                lines.Add("ICON:")
                lines.AddRange(skippedIconResults)
            End If
            Dim iconWarnings = result.Errors.Where(Function(errorMessage) errorMessage.StartsWith("ICON WARNING:", StringComparison.OrdinalIgnoreCase)).ToList()
            If iconWarnings.Count > 0 Then
                lines.Add(String.Empty)
                lines.Add("ICON WARNING:")
                lines.AddRange(iconWarnings)
            End If
            If Not pagesReady OrElse Not sqlReady Then
                lines.Add(String.Empty)
                lines.Add("GENERATION INCOMPLETE:")
                If Not pagesReady Then lines.Add("THE SELECTED PAGE TARGETS WERE NOT BOTH CREATED OR CONFIRMED.")
                If Not sqlReady Then lines.Add("THE FW_ROLETABLES SQL RECORD WAS NOT CREATED OR CONFIRMED.")
            End If
            MessageBox.Show(Me,
                            String.Join(Environment.NewLine, lines).ToUpperInvariant(),
                            "GENERATE PAGES",
                            MessageBoxButtons.OK,
                            If(pagesReady AndAlso sqlReady, MessageBoxIcon.Information, MessageBoxIcon.Warning))

            If pagesReady AndAlso sqlReady Then
                CloseAfterSuccessfulCommand()
            End If

        End Sub

        Private Sub DetectManualMaintenancePageChanges()
            pageHasManualChanges = False
            If isNewRecord Then Return

            Dim pagePath = Path.Combine(Environment.CurrentDirectory, maintenancePageNameTextBox.Text.Trim() & ".vb")
            If Not File.Exists(pagePath) Then Return

            Dim row = formBindingSource.Current
            Dim dataRow = TryCast(row, DataRowView)
            If dataRow IsNot Nothing AndAlso dataRow.Row.Table.Columns.Contains("GeneratedMaintenanceHash") AndAlso
               Not dataRow.Row.IsNull("GeneratedMaintenanceHash") Then
                Dim expectedHash = DbText(dataRow.Row("GeneratedMaintenanceHash")).Trim()
                If expectedHash <> String.Empty Then
                    Dim hasher As SHA256 = SHA256.Create()
                    Using hasher
                        Using stream = File.OpenRead(pagePath)
                            Dim currentHash = Convert.ToHexString(hasher.ComputeHash(stream))
                            pageHasManualChanges = Not String.Equals(expectedHash, currentHash, StringComparison.OrdinalIgnoreCase)
                        End Using
                    End Using
                End If
            End If

            Dim savedFields = maintenanceFieldsTextBox.Text.Split({",", ";"}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(field) field.Trim()).
                ToHashSet(StringComparer.OrdinalIgnoreCase)
            Dim source = File.ReadAllText(pagePath)
            Dim pageFields As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each match As Match In Regex.Matches(source, "(?i)(?:AddField\(\s*""(?<field>[A-Za-z_]\w*)""|\.Name\s*=\s*""TextBox_(?<field>[A-Za-z_]\w*)"")")
                pageFields.Add(match.Groups("field").Value)
            Next

            pageHasManualChanges = pageHasManualChanges OrElse pageFields.Any(Function(field) Not savedFields.Contains(field))
            If pageHasManualChanges Then ProtectManualPageChanges()
        End Sub

        Private Sub ProtectManualPageChanges()
            For Each control As Control In GetAllControls(Me)
                If TypeOf control Is Button Then
                    control.Enabled = control Is cancelActionButton
                ElseIf TypeOf control Is TextBox Then
                    DirectCast(control, TextBox).ReadOnly = True
                ElseIf TypeOf control Is ComboBox OrElse TypeOf control Is CheckBox Then
                    control.Enabled = False
                End If
            Next
            cancelActionButton.Enabled = True
            cancelActionButton.Text = "Close"
            RefreshPageCaption()
            MessageBox.Show(Me,
                            "THE GENERATED _U PAGE HAS BEEN CHANGED IN VS CODE." & Environment.NewLine & Environment.NewLine &
                            "SAVE AND SAVE & GENERATE ARE DISABLED TO PROTECT THE MANUAL CHANGES." & Environment.NewLine &
                            Environment.NewLine &
                            "ONLY CLOSE IS AVAILABLE.",
                            "MANUAL PAGE CHANGES DETECTED",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub

        Private Shared Iterator Function GetAllControls(parent As Control) As IEnumerable(Of Control)
            For Each child As Control In parent.Controls
                Yield child
                For Each descendant In GetAllControls(child)
                    Yield descendant
                Next
            Next
        End Function

        Private Sub ShowManualPageChangesWarning()
            MessageBox.Show(Me,
                            "THIS _U PAGE CONTAINS MANUAL VS CODE CHANGES THAT ARE NOT IN THE PAGE GENERATION REQUEST." & Environment.NewLine & Environment.NewLine &
                            "SAVE AND REGENERATION ARE DISABLED TO PROTECT THOSE CHANGES. SELECT CLOSE TO LEAVE THIS PAGE.",
                            "MANUAL PAGE CHANGES",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub

        Private Shared Function IsGenerationResultPresent(result As PageGenerationResult, marker As String, nameMarker As String) As Boolean
            Return result.CreatedFiles.Concat(result.SkippedFiles).Any(Function(item) item.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0 AndAlso (String.IsNullOrWhiteSpace(nameMarker) OrElse item.IndexOf(nameMarker, StringComparison.OrdinalIgnoreCase) >= 0))
        End Function

        Private Shared Function GetGenerationResults(results As IReadOnlyList(Of String), iconResults As Boolean) As List(Of String)
            Dim selected As New List(Of String)()
            For Each result In results
                ' Matched on the word, not on one dashboard name and one exact phrasing. The old
                ' test missed "icon image updated" and every Dashboard_Company entry, and a
                ' misclassified line lands under CREATED with no ICON heading above it.
                Dim isIconResult = result.IndexOf(" icon", StringComparison.OrdinalIgnoreCase) >= 0
                If isIconResult = iconResults Then
                    selected.Add(result)
                End If
            Next
            Return selected
        End Function

        Private Sub ApplyQuestionTabOrder()
            directionsToggleButton.TabStop = False
            directionsTextBox.TabStop = False
            SetFocusTargetTabIndex(requestNameTextBox, 0)
            SetFocusTargetTabIndex(pageBaseNameTextBox, 1)
            SetFocusTargetTabIndex(browsePageNameTextBox, 2)
            SetFocusTargetTabIndex(maintenancePageNameTextBox, 3)

            Dim browseOptions = TryCast(generateBrowsePageCheckBox.Parent, FlowLayoutPanel)
            If browseOptions IsNot Nothing Then
                Dim browseNameBorder = TryCast(browsePageNameTextBox.Parent, Panel)
                If browseNameBorder IsNot Nothing Then
                    browseNameBorder.TabIndex = 0
                End If
                generateBrowsePageCheckBox.TabIndex = 1
                useQbeOnlyCheckBox.TabIndex = 2
            End If

            Dim maintenanceOptions = TryCast(generateMaintenancePageCheckBox.Parent, FlowLayoutPanel)
            If maintenanceOptions IsNot Nothing Then
                Dim maintenanceNameBorder = TryCast(maintenancePageNameTextBox.Parent, Panel)
                If maintenanceNameBorder IsNot Nothing Then
                    maintenanceNameBorder.TabIndex = 0
                End If
                generateMaintenancePageCheckBox.TabIndex = 1
            End If

            If tableSelectionPanel IsNot Nothing Then
                tableSelectionPanel.TabIndex = 4
                selectTableButton.TabIndex = 0
                underlyingTableNameTextBox.TabIndex = 1
            End If

            If fieldSelectionPanel IsNot Nothing Then
                fieldSelectionPanel.TabIndex = 5
                selectFieldsButton.TabIndex = 0
            End If

            SetFocusTargetTabIndex(browseFieldsTextBox, 6)
            SetFocusTargetTabIndex(maintenanceFieldsTextBox, 7)
            SetFocusTargetTabIndex(lookupFieldsTextBox, 8)
            SetFocusTargetTabIndex(adminRequiredFieldsTextBox, 9)
            SetFocusTargetTabIndex(menuCallerComboBox, 10)

            If iconSelectionPanel IsNot Nothing Then
                iconSelectionPanel.TabIndex = 11
                selectIconButton.TabIndex = 0
                iconFileNameTextBox.TabIndex = 1
            End If

            SetFocusTargetTabIndex(browseSqlTextBox, 12)
            SetFocusTargetTabIndex(useRegistrationIdCheckBox, 13)
            copyRequestButton.TabIndex = 14
            previewCodeButton.TabIndex = 15
            generatePagesButton.TabIndex = 16
            okButton.TabIndex = 17
            cancelActionButton.TabIndex = 18
        End Sub

        Private Shared Sub SetFocusTargetTabIndex(control As Control, tabIndex As Integer)
            If control Is Nothing Then
                Return
            End If

            Dim wrapperPanel = TryCast(control.Parent, Panel)
            If wrapperPanel IsNot Nothing AndAlso
               Not TypeOf wrapperPanel Is FlowLayoutPanel AndAlso
               Not TypeOf wrapperPanel Is TableLayoutPanel Then
                Dim flowParent = TryCast(wrapperPanel.Parent, FlowLayoutPanel)
                If flowParent IsNot Nothing Then
                    flowParent.TabIndex = tabIndex
                    wrapperPanel.TabIndex = 0
                    control.TabIndex = 0
                    Return
                End If

                wrapperPanel.TabIndex = tabIndex
                control.TabIndex = 0
                Return
            End If

            control.TabIndex = tabIndex
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            BeginInvoke(New Action(Sub() requestNameTextBox.Focus()))
        End Sub

        Private Sub DirectionsToggleButton_Click(sender As Object, e As EventArgs)
            RefreshSavedPageDocumentTemplate()
            directionsTextBox.Visible = Not directionsTextBox.Visible
            directionsToggleButton.Text = If(directionsTextBox.Visible,
                                              "HIDE GENERATION DIRECTIONS",
                                              "SHOW GENERATION DIRECTIONS")
            directionsPanel.Height = If(directionsTextBox.Visible, 240, 38)
        End Sub

        ' A drop-down left on its placeholder is empty, the same as a blank text box. This is the
        ' page-local required rule; the framework has its own for metadata-driven required fields.
        Private Shared Function IsPageRequiredFieldEmpty(control As Control) As Boolean
            If control Is Nothing Then Return True

            Dim combo = TryCast(control, ComboBox)
            If combo IsNot Nothing Then
                If combo.SelectedIndex <= 0 Then Return True
                Return String.Equals(combo.Text.Trim(), MakeASelection, StringComparison.OrdinalIgnoreCase)
            End If

            Return String.IsNullOrWhiteSpace(control.Text)
        End Function

        Private Function AddQuestionRow(parent As TableLayoutPanel, caption As String, labelCaption As String, content As Control, rowHeight As Integer) As Integer
            Dim row = parent.RowCount
            parent.RowCount += 1
            parent.RowStyles.Add(New RowStyle(SizeType.Absolute, rowHeight))
            parent.Controls.Add(New Label With {
                .Name = "Label_" & caption,
                .Text = labelCaption,
                .AutoSize = False,
                .Size = New Size(215, 26),
                .Anchor = AnchorStyles.Left Or AnchorStyles.Top,
                .Margin = New Padding(0),
                .TextAlign = ContentAlignment.MiddleLeft
            }, 0, row)
            parent.Controls.Add(content, 1, row)
            Return row
        End Function

        Private Function AddEntryField(parent As TableLayoutPanel, caption As String, readOnlyValue As Boolean, rowHeight As Integer, width As Integer, Optional multiline As Boolean = False, Optional labelCaption As String = Nothing) As TextBox
            Dim row = parent.RowCount
            parent.RowCount += 1
            parent.RowStyles.Add(New RowStyle(SizeType.Absolute, If(multiline, rowHeight, rowHeight + 12)))
            parent.Controls.Add(New Label With {
                .Name = "Label_" & caption,
                .Text = If(labelCaption, caption),
                .AutoSize = False,
                .Size = New Size(215, 26),
                .Anchor = AnchorStyles.Left Or AnchorStyles.Top,
                .Margin = New Padding(0),
                .TextAlign = ContentAlignment.MiddleLeft
            }, 0, row)
            Dim textBox As New TextBox With {.Name = "TextBox_" & caption, .Width = width, .Height = If(multiline, rowHeight - 8, 26), .ReadOnly = readOnlyValue, .Multiline = multiline, .WordWrap = False, .ScrollBars = If(multiline, ScrollBars.Both, ScrollBars.Horizontal), .BorderStyle = BorderStyle.FixedSingle, .Anchor = AnchorStyles.Left Or AnchorStyles.Top}
            parent.Controls.Add(textBox, 1, row)
            AddHandler textBox.TextChanged, AddressOf MarkDirty
            Return textBox
        End Function

        Private Sub ApplyPageRequiredFieldStyling()
            For Each fieldName In New String() {"RequestName", "PageBaseName", "BrowsePageName", "MaintenancePageName", "UnderlyingTableName", "MenuCaller", "IconFileName", "BrowseSql"}
                Dim labelMatches = Controls.Find("Label_" & fieldName, True)
                Dim controlMatches = Controls.Find("TextBox_" & fieldName, True)
                If controlMatches.Length = 0 Then
                    controlMatches = Controls.Find("ComboBox_" & fieldName, True)
                End If
                If labelMatches.Length = 0 OrElse controlMatches.Length = 0 Then
                    Continue For
                End If

                Dim labelControl = labelMatches(0)
                If Not labelControl.Text.EndsWith(" *", StringComparison.Ordinal) Then
                    labelControl.Text &= " *"
                End If

                Dim textBox = controlMatches(0)
                textBox.Tag = "Required"
                Dim fieldParent = TryCast(textBox.Parent, TableLayoutPanel)
                Dim flowParent = TryCast(textBox.Parent, FlowLayoutPanel)
                Dim originalWidth = textBox.Width
                Dim originalHeight = textBox.Height
                Dim borderPanel As New Panel With {
                    .BackColor = SystemColors.Control,
                    .Dock = DockStyle.None,
                    .Anchor = AnchorStyles.Left Or AnchorStyles.Top,
                    .Size = New Size(originalWidth + 2, originalHeight + 2),
                    .MinimumSize = New Size(originalWidth + 2, originalHeight + 2),
                    .Padding = New Padding(1),
                    .Margin = New Padding(0),
                    .Tag = "PageRequiredBorder_" & fieldName
                }
                Dim originalMargin = textBox.Margin
                textBox.Margin = New Padding(0)
                textBox.BackColor = SystemColors.Window

                If fieldParent IsNot Nothing Then
                    Dim fieldColumn = fieldParent.GetColumn(textBox)
                    Dim fieldRow = fieldParent.GetRow(textBox)
                    fieldParent.Controls.Remove(textBox)
                    fieldParent.Controls.Add(borderPanel, fieldColumn, fieldRow)
                Else
                    If flowParent Is Nothing Then
                        Continue For
                    End If

                    Dim childIndex = flowParent.Controls.GetChildIndex(textBox)
                    flowParent.Controls.Remove(textBox)
                    borderPanel.Margin = originalMargin
                    flowParent.Controls.Add(borderPanel)
                    flowParent.Controls.SetChildIndex(borderPanel, childIndex)
                End If

                borderPanel.Controls.Add(textBox)
                textBox.MinimumSize = New Size(originalWidth, originalHeight)
                textBox.Size = New Size(originalWidth, originalHeight)
                textBox.Dock = DockStyle.Fill
                textBox.BringToFront()
                pageRequiredBorderPanels.Add(borderPanel)
                Dim capturedTextBox = textBox
                Dim capturedBorderPanel = borderPanel
                AddHandler textBox.TextChanged,
                    Sub(borderSender, borderEventArgs)
                        If pageRequiredValidationActivated Then
                            SetPageRequiredVisual(capturedTextBox, capturedBorderPanel, IsPageRequiredFieldEmpty(capturedTextBox))
                        End If
                    End Sub

            Next
        End Sub

        Protected Overrides Function GetAdditionalValidationMessageLines() As IEnumerable(Of String)
            pageRequiredValidationActivated = True
            Dim validationLines As New List(Of String)()
            For Each borderPanel In pageRequiredBorderPanels
                Dim fieldName = borderPanel.Tag.ToString().Replace("PageRequiredBorder_", String.Empty, StringComparison.Ordinal)
                Dim matches = Controls.Find("TextBox_" & fieldName, True)
                If matches.Length = 0 Then
                    matches = Controls.Find("ComboBox_" & fieldName, True)
                End If
                If matches.Length = 0 Then
                    Continue For
                End If

                SetPageRequiredVisual(matches(0), borderPanel, IsPageRequiredFieldEmpty(matches(0)))
            Next
            If String.IsNullOrWhiteSpace(requestNameTextBox.Text) Then
                validationLines.Add("REQUESTNAME IS REQUIRED.")
            End If
            If String.IsNullOrWhiteSpace(browseFieldsTextBox.Text) Then
                validationLines.Add("SELECT AT LEAST ONE _B DATA GRID FIELD.")
            End If
            If String.IsNullOrWhiteSpace(maintenanceFieldsTextBox.Text) Then
                validationLines.Add("SELECT AT LEAST ONE _U MAINTENANCE FIELD.")
            End If

            Dim sql = browseSqlTextBox.Text.Trim()
            If sql <> String.Empty Then
                If Not sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) Then
                    If validationLines.Count > 0 Then validationLines.Add(String.Empty)
                    validationLines.Add("BROWSESQL MUST BE A SELECT STATEMENT.")
                Else
                    Dim validationMessage = DataAccess.ValidatePageGenerationSql(sql, 0)
                    If validationMessage <> String.Empty Then
                        If validationLines.Count > 0 Then validationLines.Add(String.Empty)
                        validationLines.Add(validationMessage.ToUpperInvariant())
                    End If
                End If
            End If
            Return validationLines
        End Function

        Private Shared Sub SetPageRequiredVisual(textBox As Control, borderPanel As Panel, isEmpty As Boolean)
            borderPanel.BackColor = If(isEmpty, Color.Red, SystemColors.Control)
            textBox.BackColor = SystemColors.Window
            textBox.BringToFront()
            borderPanel.BringToFront()
            textBox.BringToFront()
        End Sub

        Private Sub PageBaseNameTextBox_Leave(sender As Object, e As EventArgs)
            Dim baseName = pageBaseNameTextBox.Text.Trim()
            If baseName = String.Empty Then
                Return
            End If

            ApplyGeneratedPageNames(baseName)
            generateBrowsePageCheckBox.Checked = True
            generateMaintenancePageCheckBox.Checked = True
        End Sub

        Private Sub CreateAsFrameworkPagesCheckBox_CheckedChanged(sender As Object, e As EventArgs)
            If pageBaseNameTextBox Is Nothing Then
                Return
            End If

            Dim baseName = pageBaseNameTextBox.Text.Trim()
            If baseName <> String.Empty Then
                ApplyGeneratedPageNames(baseName)
            End If
        End Sub

        Private Sub ApplyGeneratedPageNames(baseName As String)
            Dim normalizedBaseName = RemoveFrameworkPrefix(baseName)
            Dim pagePrefix = If(createAsFrameworkPagesCheckBox.Checked, "FW_", String.Empty)
            pageBaseNameTextBox.Text = normalizedBaseName
            browsePageNameTextBox.Text = pagePrefix & normalizedBaseName & "_B"
            maintenancePageNameTextBox.Text = pagePrefix & normalizedBaseName & "_U"
        End Sub

        Private Shared Function RemoveFrameworkPrefix(value As String) As String
            Dim result = If(value, String.Empty).Trim()
            If result.StartsWith("FW_", StringComparison.OrdinalIgnoreCase) Then
                result = result.Substring(3)
            End If
            Return result
        End Function

        Private Sub AddFieldControl(parent As TableLayoutPanel, control As Control, labelCaption As String)
            Dim row = parent.RowCount
            parent.RowCount += 1
            parent.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))
            parent.Controls.Add(New Label With {
                .Name = "Label_" & control.Name.Substring(control.Name.IndexOf("_", StringComparison.Ordinal) + 1),
                .Text = labelCaption,
                .AutoSize = False,
                .Size = New Size(215, 26),
                .Anchor = AnchorStyles.Left Or AnchorStyles.Top,
                .TextAlign = ContentAlignment.MiddleLeft
            }, 0, row)
            parent.Controls.Add(control, 1, row)
        End Sub

        Private Sub AddControlBesideField(parent As TableLayoutPanel, fieldControl As Control, sideControl As Control)
            Dim row = parent.GetRow(fieldControl)
            parent.Controls.Remove(fieldControl)
            Dim panel As New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .Padding = New Padding(0)
            }
            panel.Controls.Add(fieldControl)
            panel.Controls.Add(sideControl)
            parent.Controls.Add(panel, 1, row)
        End Sub

        Private Sub UnderlyingTableNameTextBox_TextChanged(sender As Object, e As EventArgs)
            If Not loading Then
                UpdateRegistrationOptionState()
            End If
        End Sub

        Private Sub MenuCallerComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If loading Then Return
            MarkDirty(sender, e)
            RefreshSavedPageDocumentTemplate()
        End Sub

        Private Function SelectedMenuCaller() As String
            If menuCallerComboBox Is Nothing OrElse menuCallerComboBox.SelectedIndex <= 0 Then
                Return String.Empty
            End If
            Return menuCallerComboBox.SelectedItem.ToString()
        End Function

        Private Sub SelectMenuCaller(value As String)
            If menuCallerComboBox Is Nothing Then Return
            Dim wanted = If(value, String.Empty).Trim()
            For index As Integer = 0 To menuCallerComboBox.Items.Count - 1
                If String.Equals(menuCallerComboBox.Items(index).ToString(), wanted, StringComparison.OrdinalIgnoreCase) Then
                    menuCallerComboBox.SelectedIndex = index
                    Return
                End If
            Next

            ' A saved request may name a caller that is no longer offered. Keep the value visible
            ' rather than silently reverting it to Make a Selection.
            If wanted <> String.Empty Then
                menuCallerComboBox.Items.Add(wanted)
                menuCallerComboBox.SelectedIndex = menuCallerComboBox.Items.Count - 1
            Else
                menuCallerComboBox.SelectedIndex = 0
            End If
        End Sub
        Private Shared Function GetMainMenuCallerOptions() As List(Of String)
            Return New List(Of String) From {
                "Main Menu"
            }
        End Function

        ' Discovered from the compiled classes rather than listed here, so the drop-down cannot
        ' drift from what the generator can actually place an icon on. Window controls and browse
        ' pages are deliberately absent: neither can take a generated icon.
        Private Shared Function GetDashboardCallerOptions() As List(Of String)
            Return PageGenerator.DashboardCallers()
        End Function

        Private Function PromptForCallerSelection(title As String, typedValue As String, candidates As List(Of String)) As String
            Using dialog As New Form With {
                .Text = title,
                .StartPosition = FormStartPosition.CenterParent,
                .ClientSize = New Size(520, 360),
                .MinimizeBox = False,
                .MaximizeBox = False
            }
                Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Padding = New Padding(10)}
                layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
                layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
                layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))

                Dim prompt As New Label With {
                    .Dock = DockStyle.Fill,
                    .AutoSize = True,
                    .Text = "You entered '" & typedValue & "'. Select the exact caller:"
                }

                Dim optionsList As New ListBox With {.Dock = DockStyle.Fill}
                For Each candidate In candidates
                    optionsList.Items.Add(candidate)
                Next
                If optionsList.Items.Count > 0 Then
                    optionsList.SelectedIndex = 0
                End If

                Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
                Dim cancelButton As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .AutoSize = True}
                Dim selectButton As New Button With {.Text = "Select", .DialogResult = DialogResult.OK, .AutoSize = True}
                actions.Controls.Add(cancelButton)
                actions.Controls.Add(selectButton)

                layout.Controls.Add(prompt, 0, 0)
                layout.Controls.Add(optionsList, 0, 1)
                layout.Controls.Add(actions, 0, 2)
                dialog.Controls.Add(layout)
                dialog.AcceptButton = selectButton
                dialog.CancelButton = cancelButton

                If dialog.ShowDialog(Me) = DialogResult.OK AndAlso optionsList.SelectedItem IsNot Nothing Then
                    Return optionsList.SelectedItem.ToString()
                End If
            End Using

            Return String.Empty
        End Function

        Private Sub UpdateRegistrationOptionState(Optional fields As List(Of String) = Nothing)
            Dim selectedTableName = underlyingTableNameTextBox.Text.Trim()
            Dim hasRegistrationField = fields IsNot Nothing AndAlso
                                        fields.Any(Function(field) String.Equals(field, "RegistrationID", StringComparison.OrdinalIgnoreCase))
            If fields Is Nothing Then
                Dim tableName = selectedTableName
                If tableName.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) Then
                    tableName = tableName.Substring(4)
                End If

                If tableName <> String.Empty Then
                    hasRegistrationField = DataAccess.GetTableFieldNames(tableName).Any(
                        Function(field) String.Equals(field, "RegistrationID", StringComparison.OrdinalIgnoreCase))
                End If
            End If

            useRegistrationIdCheckBox.Enabled = hasRegistrationField
            If Not hasRegistrationField Then
                useRegistrationIdCheckBox.Checked = False
            End If

            useRegistrationIdCheckBox.Text = "Use RegistrationID"

            Dim labelMatches = Controls.Find("Label_UseRegistrationID", True)
            If labelMatches.Length > 0 Then
                Dim tableName = selectedTableName
                labelMatches(0).Text = If(hasRegistrationField,
                                          "10. Use RegistrationID from """ & tableName & """",
                                          "10. Use RegistrationID from selected table (N/A)")
            End If
        End Sub

        Private Sub UpdateBrowseSqlRegistrationFilter()
            Dim tableName = underlyingTableNameTextBox.Text.Trim()
            If tableName.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) Then
                tableName = tableName.Substring(4)
            End If

            Dim sql = RemoveRegistrationIdPredicate(browseSqlTextBox.Text)
            Dim fields = If(tableName = String.Empty, New List(Of String)(), DataAccess.GetTableFieldNames(tableName))
            Dim hasRegistrationField = fields.Any(Function(field) String.Equals(field, "RegistrationID", StringComparison.OrdinalIgnoreCase))
            If useRegistrationIdCheckBox.Checked AndAlso hasRegistrationField Then
                sql = AppendRegistrationIdPredicate(sql)
            End If

            If Not String.Equals(browseSqlTextBox.Text, sql, StringComparison.Ordinal) Then
                browseSqlTextBox.Text = sql
            End If
        End Sub

        Private Shared Function RemoveRegistrationIdPredicate(sql As String) As String
            Dim cleaned = If(sql, String.Empty)
            cleaned = Regex.Replace(cleaned,
                                    "(?i)(\s+AND\s+)?(?:[A-Za-z_]\w*\s*\.\s*)?\[?RegistrationID\]?\s*=\s*@RegistrationID(?=\s|$)",
                                    String.Empty)
            cleaned = Regex.Replace(cleaned, "(?i)\s+WHERE\s*(?=(ORDER\s+BY|GROUP\s+BY|HAVING|UNION|$))", " ")
            Return NormalizeSqlClauseLineBreaks(cleaned.Trim())
        End Function

        Private Shared Function AppendRegistrationIdPredicate(sql As String) As String
            Dim clauseIndex = Regex.Match(sql, "(?i)\s+(ORDER\s+BY|GROUP\s+BY|HAVING|UNION)\b").Index
            Dim body = If(clauseIndex > 0, sql.Substring(0, clauseIndex), sql).TrimEnd()
            Dim suffix = If(clauseIndex > 0, sql.Substring(clauseIndex), String.Empty)
            Dim normalizedSuffix = If(String.IsNullOrWhiteSpace(suffix), String.Empty, Environment.NewLine & suffix.TrimStart())
            If Regex.IsMatch(body, "(?i)\bWHERE\b") Then
                Return NormalizeSqlClauseLineBreaks(body & Environment.NewLine & "AND [RegistrationID] = @RegistrationID" & normalizedSuffix)
            End If

            Return NormalizeSqlClauseLineBreaks(body & Environment.NewLine & "WHERE [RegistrationID] = @RegistrationID" & normalizedSuffix)
        End Function

        Private Shared Function NormalizeSqlClauseLineBreaks(sql As String) As String
            If String.IsNullOrWhiteSpace(sql) Then
                Return String.Empty
            End If

            Return Regex.Replace(sql,
                                 "(?i)\s+(ORDER\s+BY|GROUP\s+BY|HAVING|UNION)\b",
                                 Environment.NewLine & "$1")
        End Function

        Protected Overrides Sub BindToFormInternal()
            validatedBrowseSql = String.Empty
            If isNewRecord Then
                Dim schema = DataAccess.GetPageGenerationSchema()
                Dim newRow = schema.NewRow()
                schema.Rows.Add(newRow)
                formBindingSource.DataSource = schema
                BindFormControls()
                pageRequestIdTextBox.Text = String.Empty
                createdByTextBox.Text = currentUser.UserId.ToString()
                createdOnTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                deletedFlagCheckBox.Checked = False
                Return
            End If

            Try
                Dim row = DataAccess.GetPageGenerationById(recordId)
                If row Is Nothing Then Throw New InvalidOperationException("The selected page request no longer exists.")
                formBindingSource.DataSource = row.Table
                formBindingSource.Position = row.Table.Rows.IndexOf(row)
                BindFormControls()
                pageRequestIdTextBox.Text = DbText(row("PageRequestID"))
                requestNameTextBox.Text = DbText(row("RequestName"))
                pageBaseNameTextBox.Text = DbText(row("PageBaseName"))
                If row.Table.Columns.Contains("CreateAsFrameworkPages") AndAlso Not row.IsNull("CreateAsFrameworkPages") Then
                    createAsFrameworkPagesCheckBox.Checked = Convert.ToBoolean(row("CreateAsFrameworkPages"))
                Else
                    createAsFrameworkPagesCheckBox.Checked = False
                End If
                browsePageNameTextBox.Text = DbText(row("BrowsePageName"))
                maintenancePageNameTextBox.Text = DbText(row("MaintenancePageName"))
                underlyingTableNameTextBox.Text = DbText(row("UnderlyingTableName"))
                useRegistrationIdCheckBox.Checked = Convert.ToBoolean(row("UseRegistrationID"))
                UpdateRegistrationOptionState()
                useQbeOnlyCheckBox.Checked = generateBrowsePageCheckBox.Checked AndAlso
                                             If(row.Table.Columns.Contains("UseQbeOnly") AndAlso Not row.IsNull("UseQbeOnly"), Convert.ToBoolean(row("UseQbeOnly")), False)
                browseFieldsTextBox.Text = DbText(row("BrowseFields"))
                maintenanceFieldsTextBox.Text = DbText(row("MaintenanceFields"))
                browseSqlTextBox.Text = DbText(row("BrowseSql"))
                lookupFieldsTextBox.Text = DbText(row("LookupFields"))
                adminRequiredFieldsTextBox.Text = DbText(row("AdminRequiredFields"))
                SelectMenuCaller(DbText(row("MenuCaller")))
                SetIconFileName(If(row.Table.Columns.Contains("IconFileName"), DbText(row("IconFileName")), String.Empty))
                createdByTextBox.Text = DbText(row("CreatedBy"))
                createdOnTextBox.Text = DbText(row("CreatedOn"))
                updatedByTextBox.Text = DbText(row("UpdatedBy"))
                updatedOnTextBox.Text = DbText(row("UpdatedOn"))
                deletedFlagCheckBox.Checked = Convert.ToBoolean(row("DeletedFlag"))
                originalRowVersion = CType(DirectCast(row("RowVersion"), Byte()).Clone(), Byte())
                CaptureOriginalRowVersion(originalRowVersion)
                DetectManualMaintenancePageChanges()
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message.ToUpperInvariant(), "LOAD PAGE REQUEST", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Protected Overrides Sub ApplyMode()
            pageRequestIdTextBox.ReadOnly = True
            createdByTextBox.ReadOnly = True
            createdOnTextBox.ReadOnly = True
            updatedByTextBox.ReadOnly = True
            updatedOnTextBox.ReadOnly = True
            deletedFlagCheckBox.Enabled = False
        End Sub

        Protected Overrides Function TryBuildRecord() As Boolean
            Return True
        End Function

        Private Sub BindFormControls()
            For Each control In New Control() {requestNameTextBox, pageBaseNameTextBox, browsePageNameTextBox, maintenancePageNameTextBox, underlyingTableNameTextBox, browseFieldsTextBox, maintenanceFieldsTextBox, lookupFieldsTextBox, adminRequiredFieldsTextBox}
                Dim fieldName = control.Name.Substring("TextBox_".Length)
                control.DataBindings.Clear()
                control.DataBindings.Add("Text", formBindingSource, fieldName, True, DataSourceUpdateMode.Never)
            Next
            generateBrowsePageCheckBox.DataBindings.Clear()
            generateMaintenancePageCheckBox.DataBindings.Clear()
            generateBrowsePageCheckBox.DataBindings.Add("Checked", formBindingSource, "GenerateBrowsePage", True, DataSourceUpdateMode.Never)
            generateMaintenancePageCheckBox.DataBindings.Add("Checked", formBindingSource, "GenerateMaintenancePage", True, DataSourceUpdateMode.Never)
            createAsFrameworkPagesCheckBox.DataBindings.Clear()
            Dim pageGenerationTable = TryCast(formBindingSource.DataSource, DataTable)
            If pageGenerationTable IsNot Nothing AndAlso pageGenerationTable.Columns.Contains("CreateAsFrameworkPages") Then
                createAsFrameworkPagesCheckBox.DataBindings.Add("Checked", formBindingSource, "CreateAsFrameworkPages", True, DataSourceUpdateMode.Never)
            End If
            useQbeOnlyCheckBox.DataBindings.Clear()
            If pageGenerationTable IsNot Nothing AndAlso pageGenerationTable.Columns.Contains("UseQbeOnly") Then
                useQbeOnlyCheckBox.DataBindings.Add("Checked", formBindingSource, "UseQbeOnly", True, DataSourceUpdateMode.Never)
            End If
            useRegistrationIdCheckBox.DataBindings.Clear()
            useRegistrationIdCheckBox.DataBindings.Add("Checked", formBindingSource, "UseRegistrationID", True, DataSourceUpdateMode.Never)
            browseSqlTextBox.DataBindings.Clear()
            browseSqlTextBox.DataBindings.Add("Text", formBindingSource, "BrowseSql", True, DataSourceUpdateMode.Never)
        End Sub

        Private Sub BrowseSqlTextBox_TextChanged(sender As Object, e As EventArgs)
            validatedBrowseSql = String.Empty
        End Sub

        Private Sub GenerateBrowsePageCheckBox_CheckedChanged(sender As Object, e As EventArgs)
            If Not generateBrowsePageCheckBox.Checked Then
                useQbeOnlyCheckBox.Checked = False
            End If

            MarkDirty(sender, e)
            RefreshSavedPageDocumentTemplate()
        End Sub

        Private Sub UseRegistrationIdCheckBox_CheckedChanged(sender As Object, e As EventArgs)
            MarkDirty(sender, e)
            If Not loading Then
                UpdateBrowseSqlRegistrationFilter()
            End If
            RefreshSavedPageDocumentTemplate()
        End Sub

        Private Sub ValidateSqlButton_Click(sender As Object, e As EventArgs)
            If Not ValidateRequiredPageFields("VALIDATE SQL") Then
                Return
            End If

            Dim sql = browseSqlTextBox.Text.Trim()
            If Not sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) Then
                MessageBox.Show(Me, "BROWSESQL MUST BE A SELECT STATEMENT.", "VALIDATE SQL", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Dim validationMessage = DataAccess.ValidatePageGenerationSql(sql, 0)
            If validationMessage <> String.Empty Then
                validatedBrowseSql = String.Empty
                MessageBox.Show(Me, validationMessage.ToUpperInvariant(), "VALIDATE SQL", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            validatedBrowseSql = sql
            MessageBox.Show(Me, "SQL VALIDATION PASSED. SAVE AND GENERATE PAGES ARE ENABLED.", "VALIDATE SQL", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub


        ' Shows exactly what "Save && Generate" would produce - the emitted source for each page and
        ' every file, database row and dashboard change it would make - without writing anything.
        Private Sub PreviewGeneratedCodeButton_Click(sender As Object, e As EventArgs)
            If Not generateBrowsePageCheckBox.Checked AndAlso Not generateMaintenancePageCheckBox.Checked Then
                MessageBox.Show(Me, "SELECT AT LEAST ONE PAGE TARGET TO PREVIEW.", "PREVIEW CODE", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            ' The generator reads the saved request, so a preview of unsaved edits would show stale
            ' code. Saving is an explicit choice here rather than a silent side effect of previewing.
            If isNewRecord OrElse hasUnsavedChanges Then
                Dim saveChoice = MessageBox.Show(Me,
                                                 "THE PAGE REQUEST MUST BE SAVED BEFORE ITS GENERATED CODE CAN BE PREVIEWED." & Environment.NewLine & Environment.NewLine &
                                                 "YES: SAVE THE REQUEST AND PREVIEW THE CODE." & Environment.NewLine &
                                                 "NO: RETURN WITHOUT SAVING.",
                                                 "PREVIEW CODE",
                                                 MessageBoxButtons.YesNo,
                                                 MessageBoxIcon.Question)
                If saveChoice <> DialogResult.Yes Then Return
                If Not ValidateAndBuildForSave() Then Return
                suppressSaveConfirmation = True
                Try
                    If Not SaveRecord() Then Return
                Finally
                    suppressSaveConfirmation = False
                End Try
            End If

            Dim previewRequestId = If(isNewRecord,
                                      DataAccess.GetPageGenerationId(requestNameTextBox.Text, browsePageNameTextBox.Text, maintenancePageNameTextBox.Text),
                                      recordId)
            If previewRequestId <= 0 Then
                MessageBox.Show(Me, "THE PAGE REQUEST ID COULD NOT BE RESOLVED.", "PREVIEW CODE", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            Dim workspaceRoot = Environment.CurrentDirectory
            Dim plan = PageGenerator.Preview(previewRequestId, workspaceRoot)
            If Not plan.IsValid Then
                MessageBox.Show(Me,
                                ("GENERATION WOULD STOP WITH THESE ERRORS:" & Environment.NewLine & Environment.NewLine &
                                 String.Join(Environment.NewLine, plan.Errors)).ToUpperInvariant(),
                                "PREVIEW CODE",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error)
                Return
            End If

            ShowGeneratedCodePreview(plan, workspaceRoot)
        End Sub

        Private Sub ShowGeneratedCodePreview(plan As PageGenerationPlan, workspaceRoot As String)
            Using preview As New Form With {
                .Text = "Generated Code Preview",
                .StartPosition = FormStartPosition.CenterParent,
                .ClientSize = New Size(1000, 700),
                .MinimizeBox = False,
                .MaximizeBox = True,
                .ShowInTaskbar = False
            }
                Dim tabs As New TabControl With {.Dock = DockStyle.Fill}

                Dim summaryTab As New TabPage("Summary")
                summaryTab.Controls.Add(BuildPreviewTextBox(String.Join(Environment.NewLine, PageGenerator.DescribePlannedWork(plan, workspaceRoot))))
                tabs.TabPages.Add(summaryTab)

                If plan.GenerateBrowsePage Then
                    Dim browseTab As New TabPage(PreviewTabCaption(plan.BrowsePageName, plan.BrowseSource))
                    browseTab.Controls.Add(BuildPreviewTextBox(plan.BrowseSource))
                    tabs.TabPages.Add(browseTab)
                End If
                If plan.GenerateMaintenancePage Then
                    Dim maintenanceTab As New TabPage(PreviewTabCaption(plan.MaintenancePageName, plan.MaintenanceSource))
                    maintenanceTab.Controls.Add(BuildPreviewTextBox(plan.MaintenanceSource))
                    tabs.TabPages.Add(maintenanceTab)
                End If

                Dim previewFooter As New Panel With {.Dock = DockStyle.Bottom, .Height = 54, .Padding = New Padding(12, 8, 12, 8)}

                ' The footer shows on every tab, so a bare "Compile Check" reads as belonging to
                ' whichever tab is open. The caption carries the scope instead.
                Dim compileScope = If(plan.GenerateBrowsePage AndAlso plan.GenerateMaintenancePage,
                                      "Compile Both Pages",
                                      "Compile Page")
                Dim compileButton As New Button With {
                    .Text = compileScope,
                    .Size = New Size(160, 36),
                    .Location = New Point(12, 8),
                    .Anchor = AnchorStyles.Top Or AnchorStyles.Left
                }
                AddHandler compileButton.Click,
                    Sub()
                        RunCompileCheck(preview, plan, workspaceRoot)
                    End Sub
                previewFooter.Controls.Add(compileButton)

                Dim closeButton As New Button With {
                    .Text = "Close",
                    .Size = New Size(115, 36),
                    .Location = New Point(preview.ClientSize.Width - 135, 8),
                    .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                    .DialogResult = DialogResult.OK
                }
                previewFooter.Controls.Add(closeButton)

                preview.Controls.Add(tabs)
                preview.Controls.Add(previewFooter)
                preview.AcceptButton = closeButton
                preview.CancelButton = closeButton
                preview.ShowDialog(Me)
            End Using
        End Sub

        ' A generated _B page is short by design - FW_Base_B does the work - so the line count is
        ' stated on the tab rather than left to look like a truncated preview.
        Private Shared Function PreviewTabCaption(pageName As String, source As String) As String
            Dim lineCount = If(String.IsNullOrEmpty(source), 0, source.Split({Environment.NewLine, vbLf}, StringSplitOptions.None).Length)
            Return pageName & ".vb  (" & lineCount.ToString(Globalization.CultureInfo.InvariantCulture) & " lines)"
        End Function

        Private Shared Function BuildPreviewTextBox(content As String) As TextBox
            Return New TextBox With {
                .Dock = DockStyle.Fill,
                .Multiline = True,
                .ReadOnly = True,
                .WordWrap = False,
                .ScrollBars = ScrollBars.Both,
                .Font = New Font("Consolas", 9.0F),
                .BackColor = Color.White,
                .Text = content
            }
        End Function

        ' Only a real compile proves the emitted pages would build. The preview text on its own
        ' cannot show a template mistake that the compiler would reject.
        Private Shared Sub RunCompileCheck(owner As Form, plan As PageGenerationPlan, workspaceRoot As String)
            Dim result As PageCompileResult = Nothing
            Dim work = Task.Run(Sub()
                                    result = PageGenerator.CompileCheck(plan, workspaceRoot)
                                End Sub)
            FW_BusyDialog.WaitFor(owner, work, "COMPILE CHECK", "COMPILING THE GENERATED SOURCE...")

            If result Is Nothing Then
                MessageBox.Show(owner, "THE COMPILE CHECK DID NOT RETURN A RESULT.", "COMPILE CHECK", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            MessageBox.Show(owner,
                            String.Join(Environment.NewLine, result.Messages),
                            "COMPILE CHECK",
                            MessageBoxButtons.OK,
                            If(result.Succeeded, MessageBoxIcon.Information, MessageBoxIcon.Error))
        End Sub

        Private Sub PreviewRequestButton_Click(sender As Object, e As EventArgs)
            If Not ValidateRequiredPageFields("PREVIEW") Then
                Return
            End If

            Dim sqlValidationMessage = DataAccess.ValidatePageGenerationSql(browseSqlTextBox.Text.Trim(), 0)
            If sqlValidationMessage <> String.Empty Then
                MessageBox.Show(Me, sqlValidationMessage.ToUpperInvariant(), "PREVIEW", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Try
                Using preview As New Form With {
                    .Text = "Page Document Preview",
                    .StartPosition = FormStartPosition.CenterParent,
                    .ClientSize = New Size(900, 650),
                    .MinimizeBox = False,
                    .MaximizeBox = True,
                    .ShowInTaskbar = False
                }
                    Dim documentList As New ListBox With {
                        .Dock = DockStyle.Fill,
                        .HorizontalScrollbar = True,
                        .IntegralHeight = False,
                        .SelectionMode = SelectionMode.None,
                        .Font = New Font("Consolas", 9.0F),
                        .FormattingEnabled = True
                    }
                    documentList.Items.AddRange(BuildGeneratedPageRequest().Split({Environment.NewLine}, StringSplitOptions.None))
                    preview.Controls.Add(documentList)
                    preview.ShowDialog(Me)
                End Using
            Catch ex As Exception
                MessageBox.Show(Me, "COULD NOT PREVIEW THE REQUEST: " & ex.Message.ToUpperInvariant(), "PREVIEW", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub

        Private Function ValidateRequiredPageFields(actionTitle As String) As Boolean
            Dim errorMessage As String = String.Empty
            GetAdditionalValidationMessageLines()
            If DataAccess.ValidateRequiredControls(Me, errorMessage) Then
                Return True
            End If

            MessageBox.Show(Me,
                            errorMessage.ToUpperInvariant(),
                            actionTitle,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
            Return False
        End Function

        Private Shared Function DashboardImagesFolder() As String
            Dim candidates As String() = {
                Path.Combine(Application.StartupPath, "assets", "images"),
                Path.Combine(Application.StartupPath, "..", "..", "..", "assets", "images"),
                Path.Combine(Application.StartupPath, "..", "..", "..", "..", "assets", "images")
            }
            For Each candidate In candidates
                Dim fullPath = Path.GetFullPath(candidate)
                If Directory.Exists(fullPath) Then Return fullPath
            Next
            Return String.Empty
        End Function

        ''' A choice is either a file in assets\images or one of the built-in glyphs, marked with
        ''' the system: prefix. One place resolves both to a picture.
        Private Shared Function ResolveIconImage(choice As String) As Image
            Dim wanted = If(choice, String.Empty).Trim()
            If wanted = String.Empty Then Return Nothing

            Dim systemName = PageGenerator.SystemIconName(wanted)
            If systemName.Length > 0 Then
                Select Case systemName.ToUpperInvariant()
                    Case "APPLICATION" : Return SystemIcons.Application.ToBitmap()
                    Case "ASTERISK" : Return SystemIcons.Asterisk.ToBitmap()
                    Case "ERROR" : Return SystemIcons.Error.ToBitmap()
                    Case "EXCLAMATION" : Return SystemIcons.Exclamation.ToBitmap()
                    Case "HAND" : Return SystemIcons.Hand.ToBitmap()
                    Case "INFORMATION" : Return SystemIcons.Information.ToBitmap()
                    Case "QUESTION" : Return SystemIcons.Question.ToBitmap()
                    Case "SHIELD" : Return SystemIcons.Shield.ToBitmap()
                    Case "WARNING" : Return SystemIcons.Warning.ToBitmap()
                    Case "WINLOGO" : Return SystemIcons.WinLogo.ToBitmap()
                    Case Else : Return Nothing
                End Select
            End If

            Dim folder = DashboardImagesFolder()
            If folder = String.Empty Then Return Nothing
            Dim fullPath = Path.Combine(folder, wanted)
            If Not File.Exists(fullPath) Then Return Nothing

            Try
                ' Read through a stream so the preview does not lock the file.
                Using stream As New FileStream(fullPath, FileMode.Open, FileAccess.Read)
                    Return Image.FromStream(stream)
                End Using
            Catch
                Return Nothing
            End Try
        End Function

        ''' What the user sees in the list for a stored choice.
        Private Shared Function IconChoiceDisplay(choice As String) As String
            Dim systemName = PageGenerator.SystemIconName(choice)
            Return If(systemName.Length > 0, systemName & " (system)", If(choice, String.Empty).Trim())
        End Function

        ''' What is stored for a displayed choice.
        Private Shared Function IconChoiceValue(display As String) As String
            Dim text = If(display, String.Empty).Trim()
            If text.EndsWith(" (system)", StringComparison.OrdinalIgnoreCase) Then
                Return PageGenerator.SystemIconPrefix & text.Substring(0, text.Length - " (system)".Length)
            End If
            Return text
        End Function

        Private Sub SetIconFileName(fileName As String)
            iconFileNameTextBox.Text = IconChoiceDisplay(fileName)
            iconPreviewBox.Image = ResolveIconImage(fileName)
        End Sub

        ''' One tile per icon, laid out like the dashboard it is choosing for: system glyphs on the
        ''' left, the files in assets\images on the right. Picking by sight beats picking a file
        ''' name and then finding out what it looks like.
        Private Sub SelectIconButton_Click(sender As Object, e As EventArgs)
            Dim folder = DashboardImagesFolder()
            Dim files As New List(Of String)()
            If folder <> String.Empty Then
                files = Directory.GetFiles(folder).
                    Where(Function(item) {".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico"}.
                        Contains(Path.GetExtension(item).ToLowerInvariant())).
                    Select(Function(item) Path.GetFileName(item)).
                    OrderBy(Function(item) item, StringComparer.OrdinalIgnoreCase).
                    ToList()
            End If

            Dim chosen = IconChoiceValue(iconFileNameTextBox.Text)
            Dim tiles As New List(Of Panel)()

            Using dialog As New Form With {
                .Text = "Select Dashboard Icon",
                .StartPosition = FormStartPosition.CenterParent,
                .ClientSize = New Size(780, 560),
                .MinimizeBox = False,
                .MaximizeBox = False,
                .FormBorderStyle = FormBorderStyle.FixedDialog
            }
                Dim layout As New TableLayoutPanel With {
                    .Dock = DockStyle.Fill,
                    .ColumnCount = 2,
                    .RowCount = 2,
                    .Padding = New Padding(10)
                }
                layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
                layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
                layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
                layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 48))

                Dim systemGroup As New GroupBox With {.Text = "System Icons", .Dock = DockStyle.Fill}
                Dim fileGroup As New GroupBox With {.Text = "File Graphics", .Dock = DockStyle.Fill}
                Dim systemFlow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(8)}
                Dim fileFlow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(8)}
                systemGroup.Controls.Add(systemFlow)
                fileGroup.Controls.Add(fileFlow)

                Dim applyButton As New Button With {.Text = "Select", .DialogResult = DialogResult.OK, .AutoSize = True, .Enabled = chosen <> String.Empty}

                Dim highlight = Sub()
                                    For Each tile In tiles
                                        Dim value = Convert.ToString(tile.Tag)
                                        Dim isChosen = String.Equals(value, chosen, StringComparison.OrdinalIgnoreCase)
                                        tile.BackColor = If(isChosen, Color.FromArgb(221, 235, 247), SystemColors.Control)
                                        tile.BorderStyle = If(isChosen, BorderStyle.FixedSingle, BorderStyle.None)
                                    Next
                                    applyButton.Enabled = chosen <> String.Empty
                                End Sub

                Dim addTile = Sub(host As FlowLayoutPanel, value As String, caption As String)
                                  Dim tile As New Panel With {
                                      .Size = New Size(104, 104),
                                      .Margin = New Padding(6),
                                      .Tag = value,
                                      .Cursor = Cursors.Hand
                                  }
                                  Dim picture As New PictureBox With {
                                      .Size = New Size(48, 48),
                                      .Location = New Point(28, 10),
                                      .SizeMode = PictureBoxSizeMode.Zoom,
                                      .Image = ResolveIconImage(value)
                                  }
                                  Dim captionLabel As New Label With {
                                      .Text = caption,
                                      .AutoSize = False,
                                      .Size = New Size(100, 32),
                                      .Location = New Point(2, 64),
                                      .TextAlign = ContentAlignment.TopCenter
                                  }
                                  tile.Controls.Add(picture)
                                  tile.Controls.Add(captionLabel)

                                  ' The picture and the caption fill the tile, so the click has to be
                                  ' taken on all three or half the tile would be dead.
                                  For Each clickable As Control In New Control() {tile, picture, captionLabel}
                                      AddHandler clickable.Click, Sub()
                                                                      chosen = value
                                                                      highlight()
                                                                  End Sub
                                      AddHandler clickable.DoubleClick, Sub()
                                                                            chosen = value
                                                                            dialog.DialogResult = DialogResult.OK
                                                                            dialog.Close()
                                                                        End Sub
                                  Next

                                  tiles.Add(tile)
                                  host.Controls.Add(tile)
                              End Sub

                For Each systemName In PageGenerator.SystemIconNames
                    addTile(systemFlow, PageGenerator.SystemIconPrefix & systemName, systemName)
                Next
                For Each fileName In files
                    addTile(fileFlow, fileName, fileName)
                Next

                If files.Count = 0 Then
                    fileFlow.Controls.Add(New Label With {
                        .Text = "No images found in assets\images.",
                        .AutoSize = True,
                        .ForeColor = Color.DimGray,
                        .Margin = New Padding(6)
                    })
                End If

                Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
                Dim cancelButton As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .AutoSize = True}
                actions.Controls.Add(cancelButton)
                actions.Controls.Add(applyButton)

                layout.Controls.Add(systemGroup, 0, 0)
                layout.Controls.Add(fileGroup, 1, 0)
                layout.Controls.Add(actions, 1, 1)
                dialog.Controls.Add(layout)
                dialog.AcceptButton = applyButton
                dialog.CancelButton = cancelButton

                highlight()

                If dialog.ShowDialog(Me) = DialogResult.OK AndAlso chosen <> String.Empty Then
                    If Not String.Equals(IconChoiceValue(iconFileNameTextBox.Text), chosen, StringComparison.OrdinalIgnoreCase) Then
                        SetIconFileName(chosen)
                        MarkDirty(sender, e)
                        RefreshSavedPageDocumentTemplate()
                    End If
                End If
            End Using
        End Sub
        Private Sub SelectFieldsButton_Click(sender As Object, e As EventArgs)
            Dim tableName = underlyingTableNameTextBox.Text.Trim()
            If tableName.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) Then
                tableName = tableName.Substring(4)
            End If

            Dim fields = DataAccess.GetTableFieldNames(tableName).
                OrderBy(Function(field) field, StringComparer.OrdinalIgnoreCase).
                ToList()
            If fields.Count = 0 Then
                MessageBox.Show(Me, "CHOOSE A TABLE FIRST WITH SELECT TABLE.", "SELECT FIELDS", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            UpdateRegistrationOptionState(fields)

            Dim primaryKeyField = DataAccess.GetPrimaryKeyFieldName(tableName)
            Using dialog As New Form With {
                .Text = "Select _B and _U Fields",
                .StartPosition = FormStartPosition.CenterParent,
                .ClientSize = New Size(980, 620),
                .MinimizeBox = False,
                .MaximizeBox = False
            }
                Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2, .Padding = New Padding(10)}
                layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 35))
                layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 65))
                layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
                layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 92))

                Dim browseGrid As New DataGridView With {
                    .Dock = DockStyle.Fill,
                    .AllowUserToAddRows = False,
                    .AllowUserToDeleteRows = False,
                    .AllowUserToResizeRows = False,
                    .AutoGenerateColumns = False,
                    .RowHeadersVisible = False,
                    .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    .MultiSelect = False,
                    .EditMode = DataGridViewEditMode.EditOnEnter
                }
                AddSelectionGroupSeparator(browseGrid)
                browseGrid.Columns.Add(New DataGridViewCheckBoxColumn With {
                    .Name = "Include",
                    .HeaderText = "Use in _B",
                    .Width = 80
                })
                browseGrid.Columns.Add(New DataGridViewTextBoxColumn With {
                    .Name = "FieldName",
                    .HeaderText = "Field",
                    .ReadOnly = True,
                    .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
                })
                browseGrid.Columns.Add(New DataGridViewCheckBoxColumn With {
                    .Name = "OrderBy",
                    .HeaderText = "Order By",
                    .Width = 75
                })
                browseGrid.Columns.Add(New DataGridViewComboBoxColumn With {
                    .Name = "Direction",
                    .HeaderText = "Direction",
                    .Width = 80,
                    .DataSource = New List(Of String) From {"ASC", "DESC"}
                })
                Dim savedBrowseFields = browseFieldsTextBox.Text.Trim()
                Dim orderByFields = ParseOrderByFields(browseSqlTextBox.Text)
                Dim orderByDirections = ParseOrderByDirections(browseSqlTextBox.Text)
                For Each field In fields
                    browseGrid.Rows.Add(
                        If(savedBrowseFields = String.Empty, False, ContainsField(savedBrowseFields, field)),
                        field,
                        orderByFields.Any(Function(orderField) String.Equals(orderField, field, StringComparison.OrdinalIgnoreCase)),
                        If(orderByDirections.ContainsKey(field), orderByDirections(field), "ASC"))
                Next
                OrderSelectionGrid(browseGrid, savedBrowseFields)
                layout.Controls.Add(CreateSelectionPanel("_B Data Grid Fields and Order By",
                                                         browseGrid,
                                                         "PrimaryKeyDetectedAutomatically"), 0, 0)

                Dim maintenanceGrid As New DataGridView With {
                    .Dock = DockStyle.Fill,
                    .AllowUserToAddRows = False,
                    .AllowUserToDeleteRows = False,
                    .AllowUserToResizeRows = False,
                    .AutoGenerateColumns = False,
                    .RowHeadersVisible = False,
                    .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    .MultiSelect = False,
                    .EditMode = DataGridViewEditMode.EditOnEnter
                }
                AddSelectionGroupSeparator(maintenanceGrid)
                maintenanceGrid.Columns.Add(New DataGridViewCheckBoxColumn With {
                    .Name = "Include",
                    .HeaderText = "Use in _U",
                    .Width = 85
                })
                maintenanceGrid.Columns.Add(New DataGridViewTextBoxColumn With {
                    .Name = "FieldName",
                    .HeaderText = "Field",
                    .ReadOnly = True,
                    .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
                })
                maintenanceGrid.Columns.Add(New DataGridViewCheckBoxColumn With {
                    .Name = "Required",
                    .HeaderText = "Admin Required",
                    .Width = 125
                })
                maintenanceGrid.Columns.Add(New DataGridViewCheckBoxColumn With {
                    .Name = "Lookup",
                    .HeaderText = "Lookup",
                    .Width = 90
                })
                AddHandler maintenanceGrid.CurrentCellDirtyStateChanged,
                    Sub(gridSender, eventArgs)
                        If maintenanceGrid.IsCurrentCellDirty Then
                            maintenanceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit)
                        End If
                    End Sub
                AddHandler maintenanceGrid.CellValueChanged,
                    Sub(gridSender, eventArgs)
                        If eventArgs.RowIndex < 0 Then Return

                        Dim row = maintenanceGrid.Rows(eventArgs.RowIndex)
                        If eventArgs.ColumnIndex = maintenanceGrid.Columns("Include").Index Then
                            Dim isIncluded = Convert.ToBoolean(row.Cells("Include").Value)
                            If Not isIncluded Then
                                row.Cells("Required").Value = False
                                row.Cells("Lookup").Value = False
                            End If
                        ElseIf eventArgs.ColumnIndex = maintenanceGrid.Columns("Required").Index OrElse
                               eventArgs.ColumnIndex = maintenanceGrid.Columns("Lookup").Index Then
                            ' One-directional on purpose. Ticking Required or Lookup means the field
                            ' has to be on the page, so Include is switched on. Unticking implies
                            ' nothing - a field is quite normally included and not required - so
                            ' Include is left alone. Assigning the expression here instead cleared
                            ' Include, and the row was then sorted back down with the unselected
                            ' fields as though it had never been chosen.
                            Dim isRequired = Convert.ToBoolean(row.Cells("Required").Value)
                            Dim isLookup = Convert.ToBoolean(row.Cells("Lookup").Value)
                            If isRequired OrElse isLookup Then
                                row.Cells("Include").Value = True
                            End If
                        End If
                        NormalizeSelectionGridOrder(maintenanceGrid)
                    End Sub

                AddHandler browseGrid.CurrentCellDirtyStateChanged,
                    Sub(gridSender, eventArgs)
                        If browseGrid.IsCurrentCellDirty Then
                            browseGrid.CommitEdit(DataGridViewDataErrorContexts.Commit)
                        End If
                    End Sub
                AddHandler browseGrid.CellValueChanged,
                    Sub(gridSender, eventArgs)
                        If eventArgs.RowIndex < 0 Then Return

                        Dim row = browseGrid.Rows(eventArgs.RowIndex)
                        Dim fieldName = Convert.ToString(row.Cells("FieldName").Value)
                        If eventArgs.ColumnIndex = browseGrid.Columns("Include").Index AndAlso Convert.ToBoolean(row.Cells("Include").Value) Then
                            For Each maintenanceRow As DataGridViewRow In maintenanceGrid.Rows
                                If String.Equals(Convert.ToString(maintenanceRow.Cells("FieldName").Value), fieldName, StringComparison.OrdinalIgnoreCase) Then
                                    maintenanceRow.Cells("Include").Value = True
                                    Exit For
                                End If
                            Next
                        ElseIf eventArgs.ColumnIndex = browseGrid.Columns("OrderBy").Index Then
                            If Convert.ToBoolean(row.Cells("OrderBy").Value) Then
                                If Not orderByFields.Any(Function(orderField) String.Equals(orderField, fieldName, StringComparison.OrdinalIgnoreCase)) Then
                                    orderByFields.Add(fieldName)
                                End If
                            Else
                                orderByFields.RemoveAll(Function(orderField) String.Equals(orderField, fieldName, StringComparison.OrdinalIgnoreCase))
                            End If
                        End If
                        NormalizeSelectionGridOrder(browseGrid)
                    End Sub

                Dim savedMaintenanceFields = maintenanceFieldsTextBox.Text.Trim()
                For Each field In fields
                    maintenanceGrid.Rows.Add(
                        ContainsField(savedMaintenanceFields, field) OrElse
                            ContainsField(lookupFieldsTextBox.Text, field) OrElse
                            ContainsField(adminRequiredFieldsTextBox.Text, field),
                        field,
                        ContainsField(adminRequiredFieldsTextBox.Text, field),
                        ContainsField(lookupFieldsTextBox.Text, field))
                Next
                OrderSelectionGrid(maintenanceGrid, savedMaintenanceFields)
                layout.Controls.Add(CreateSelectionPanel("_U Maintenance Fields", maintenanceGrid), 1, 0)

                Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
                Dim cancelButton As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .AutoSize = True}
                Dim applyButton As New Button With {.Text = "Apply", .AutoSize = True}
                actions.Controls.Add(cancelButton)
                actions.Controls.Add(applyButton)
                Dim footerPanel As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1}
                footerPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 65))
                footerPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 35))
                footerPanel.Controls.Add(actions, 1, 0)
                layout.Controls.Add(footerPanel, 0, 1)
                layout.SetColumnSpan(footerPanel, 2)
                dialog.Controls.Add(layout)
                dialog.AcceptButton = applyButton
                dialog.CancelButton = cancelButton

                AddHandler applyButton.Click,
                    Sub(buttonSender, buttonEventArgs)
                        Dim missingSelections As New List(Of String)()
                        If Not browseGrid.Rows.Cast(Of DataGridViewRow)().Any(Function(row) Convert.ToBoolean(row.Cells("Include").Value)) Then
                            missingSelections.Add("SELECT AT LEAST ONE _B DATA GRID FIELD.")
                        End If
                        If Not maintenanceGrid.Rows.Cast(Of DataGridViewRow)().Any(Function(row) Convert.ToBoolean(row.Cells("Include").Value)) Then
                            missingSelections.Add("SELECT AT LEAST ONE _U MAINTENANCE FIELD.")
                        End If
                        If missingSelections.Count > 0 Then
                            MessageBox.Show(dialog, String.Join(Environment.NewLine, missingSelections), "SELECT FIELDS", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            Return
                        End If
                        dialog.DialogResult = DialogResult.OK
                    End Sub

                If dialog.ShowDialog(Me) = DialogResult.OK Then
                    browseFieldsTextBox.Text = JoinCheckedGridFields(browseGrid, "Include")
                    maintenanceFieldsTextBox.Text = JoinIncludedGridFields(maintenanceGrid)
                    lookupFieldsTextBox.Text = JoinCheckedGridFields(maintenanceGrid, "Lookup")
                    adminRequiredFieldsTextBox.Text = JoinCheckedGridFields(maintenanceGrid, "Required")
                    browseSqlTextBox.Text = BuildGeneratedBrowseSql(tableName, browseGrid, fields, primaryKeyField, orderByFields)
                    RefreshSavedPageDocumentTemplate()
                End If
            End Using

        End Sub

        Private Sub SelectTableButton_Click(sender As Object, e As EventArgs)
            Dim tables = DataAccess.GetDatabaseTables()
            If tables.Count = 0 Then
                MessageBox.Show(Me, "NO FW_, AS_, OR CRM_ TABLES WERE FOUND.", "SELECT TABLE", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Using dialog As New Form With {
                .Text = "Select Underlying Table",
                .StartPosition = FormStartPosition.CenterParent,
                .ClientSize = New Size(420, 520),
                .MinimizeBox = False,
                .MaximizeBox = False
            }
                Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Padding = New Padding(10)}
                layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
                layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 44))

                Dim tableList As New ListBox With {.Dock = DockStyle.Fill}
                For Each tableName In tables
                    tableList.Items.Add(tableName)
                Next

                Dim currentTable = underlyingTableNameTextBox.Text.Trim()
                For index As Integer = 0 To tableList.Items.Count - 1
                    If String.Equals(tableList.Items(index).ToString(), currentTable, StringComparison.OrdinalIgnoreCase) Then
                        tableList.SelectedIndex = index
                        Exit For
                    End If
                Next

                Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
                Dim cancelButton As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .AutoSize = True}
                Dim applyButton As New Button With {.Text = "Select", .DialogResult = DialogResult.OK, .AutoSize = True}
                actions.Controls.Add(cancelButton)
                actions.Controls.Add(applyButton)
                layout.Controls.Add(tableList, 0, 0)
                layout.Controls.Add(actions, 0, 1)
                dialog.Controls.Add(layout)
                dialog.AcceptButton = applyButton
                dialog.CancelButton = cancelButton

                If dialog.ShowDialog(Me) = DialogResult.OK AndAlso tableList.SelectedItem IsNot Nothing Then
                    Dim selectedTable = tableList.SelectedItem.ToString()
                    If Not String.Equals(underlyingTableNameTextBox.Text.Trim(), selectedTable, StringComparison.OrdinalIgnoreCase) Then
                        underlyingTableNameTextBox.Text = selectedTable
                        ClearTableDependentSelections()
                        UpdateRegistrationOptionState()
                        RefreshSavedPageDocumentTemplate()
                    End If
                End If
            End Using
        End Sub

        Private Sub ClearTableDependentSelections()
            browseFieldsTextBox.Text = String.Empty
            maintenanceFieldsTextBox.Text = String.Empty
            lookupFieldsTextBox.Text = String.Empty
            adminRequiredFieldsTextBox.Text = String.Empty
            browseSqlTextBox.Text = String.Empty
            validatedBrowseSql = String.Empty
            okButton.Enabled = True
            RefreshSavedPageDocumentTemplate()
        End Sub

        Private Shared Function CreateSelectionPanel(caption As String,
                                                     grid As DataGridView,
                                                     Optional note As String = Nothing) As Control
            Dim hasNote = Not String.IsNullOrWhiteSpace(note)
            Dim panel As New TableLayoutPanel With {.Dock = DockStyle.Fill, .RowCount = If(hasNote, 4, 3), .ColumnCount = 1}
            panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
            panel.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            If hasNote Then
                panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 28))
            End If
            panel.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))
            panel.Controls.Add(New Label With {.Text = caption, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft}, 0, 0)
            panel.Controls.Add(grid, 0, 1)

            Dim actionsRow = 2
            If hasNote Then
                panel.Controls.Add(New Label With {
                    .Text = note,
                    .Dock = DockStyle.Fill,
                    .ForeColor = Color.DimGray,
                    .TextAlign = ContentAlignment.MiddleLeft
                }, 0, 2)
                actionsRow = 3
            End If

            Dim actions As New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .FlowDirection = FlowDirection.LeftToRight,
                .WrapContents = False,
                .Padding = New Padding(0, 3, 0, 0)
            }
            Dim moveUpButton As New Button With {.Text = "Move Up", .AutoSize = True}
            Dim moveDownButton As New Button With {.Text = "Move Down", .AutoSize = True}
            AddHandler moveUpButton.Click, Sub(sender As Object, e As EventArgs) MoveSelectedField(grid, -1)
            AddHandler moveDownButton.Click, Sub(sender As Object, e As EventArgs) MoveSelectedField(grid, 1)
            actions.Controls.Add(moveUpButton)
            actions.Controls.Add(moveDownButton)
            panel.Controls.Add(actions, 0, actionsRow)
            Return panel
        End Function

        Private Shared Sub AddSelectionGroupSeparator(grid As DataGridView)
            AddHandler grid.CellPainting,
                Sub(sender As Object, eventArgs As DataGridViewCellPaintingEventArgs)
                    If eventArgs.RowIndex <= 0 OrElse eventArgs.ColumnIndex < 0 Then Return
                    If Convert.ToBoolean(grid.Rows(eventArgs.RowIndex).Cells("Include").Value) OrElse
                       Not Convert.ToBoolean(grid.Rows(eventArgs.RowIndex - 1).Cells("Include").Value) Then Return

                    eventArgs.Paint(eventArgs.CellBounds, eventArgs.PaintParts)
                    Using separatorPen As New Pen(Color.DimGray, 2.0F)
                        eventArgs.Graphics.DrawLine(separatorPen,
                                                    eventArgs.CellBounds.Left,
                                                    eventArgs.CellBounds.Top,
                                                    eventArgs.CellBounds.Right,
                                                    eventArgs.CellBounds.Top)
                    End Using
                    eventArgs.Handled = True
                End Sub
        End Sub

        Private Shared Sub NormalizeSelectionGridOrder(grid As DataGridView)
            Dim selectedFields = grid.Rows.Cast(Of DataGridViewRow)().
                Where(Function(row) Convert.ToBoolean(row.Cells("Include").Value)).
                Select(Function(row) Convert.ToString(row.Cells("FieldName").Value)).
                ToList()
            OrderSelectionGrid(grid, String.Join(", ", selectedFields))
        End Sub

        Private Shared Sub OrderSelectionGrid(grid As DataGridView, savedFields As String)
            Dim savedOrder = savedFields.Split({",", ";"}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(field) field.Trim()).
                ToList()
            Dim savedPositions = savedOrder.
                Select(Function(field, index) New With {field, index}).
                ToDictionary(Function(item) item.field, Function(item) item.index, StringComparer.OrdinalIgnoreCase)
            Dim rows = grid.Rows.Cast(Of DataGridViewRow)().
                Select(Function(row) New With {
                    .Values = row.Cells.Cast(Of DataGridViewCell)().Select(Function(cell) cell.Value).ToArray(),
                    .FieldName = Convert.ToString(row.Cells("FieldName").Value),
                    .Included = Convert.ToBoolean(row.Cells("Include").Value)
                }).
                OrderByDescending(Function(item) item.Included).
                ThenBy(Function(item) If(item.Included AndAlso savedPositions.ContainsKey(item.FieldName), savedPositions(item.FieldName), Integer.MaxValue)).
                ThenBy(Function(item) item.FieldName, StringComparer.OrdinalIgnoreCase).
                ToList()

            grid.Rows.Clear()
            For Each row In rows
                grid.Rows.Add(row.Values)
            Next
        End Sub

        Private Shared Sub MoveSelectedField(grid As DataGridView, direction As Integer)
            If grid.SelectedRows.Count = 0 Then Return

            Dim selectedIndex = grid.SelectedRows(0).Index
            If Not Convert.ToBoolean(grid.Rows(selectedIndex).Cells("Include").Value) Then Return

            Dim includedFields = grid.Rows.Cast(Of DataGridViewRow)().
                Where(Function(row) Convert.ToBoolean(row.Cells("Include").Value)).
                Select(Function(row) Convert.ToString(row.Cells("FieldName").Value)).
                ToList()
            Dim selectedField = Convert.ToString(grid.Rows(selectedIndex).Cells("FieldName").Value)
            Dim selectedPosition = includedFields.FindIndex(Function(field) String.Equals(field, selectedField, StringComparison.OrdinalIgnoreCase))
            Dim targetPosition = selectedPosition + direction
            If selectedPosition < 0 OrElse targetPosition < 0 OrElse targetPosition >= includedFields.Count Then Return

            Dim targetField = includedFields(targetPosition)
            includedFields(targetPosition) = selectedField
            includedFields(selectedPosition) = targetField
            OrderSelectionGrid(grid, String.Join(", ", includedFields))

            grid.ClearSelection()
            For Each row As DataGridViewRow In grid.Rows
                If String.Equals(Convert.ToString(row.Cells("FieldName").Value), selectedField, StringComparison.OrdinalIgnoreCase) Then
                    row.Selected = True
                    grid.CurrentCell = row.Cells("FieldName")
                    Exit For
                End If
            Next
        End Sub

        Private Shared Function ContainsField(value As String, field As String) As Boolean
            Return value.Split({",", ";"}, StringSplitOptions.RemoveEmptyEntries).Any(Function(item) String.Equals(item.Trim(), field, StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function JoinCheckedItems(list As CheckedListBox) As String
            Return String.Join(", ", list.CheckedItems.Cast(Of Object)().Select(Function(item) item.ToString()))
        End Function

        Private Shared Function JoinCheckedGridFields(grid As DataGridView, columnName As String) As String
            Dim selectedFields As New List(Of String)()
            For Each row As DataGridViewRow In grid.Rows
                If Convert.ToBoolean(row.Cells("Include").Value) AndAlso Convert.ToBoolean(row.Cells(columnName).Value) Then
                    selectedFields.Add(Convert.ToString(row.Cells("FieldName").Value))
                End If
            Next
            Return String.Join(", ", selectedFields)
        End Function

        Private Shared Function JoinIncludedGridFields(grid As DataGridView) As String
            Dim selectedFields As New List(Of String)()
            For Each row As DataGridViewRow In grid.Rows
                If Convert.ToBoolean(row.Cells("Include").Value) Then
                    selectedFields.Add(Convert.ToString(row.Cells("FieldName").Value))
                End If
            Next
            Return String.Join(", ", selectedFields)
        End Function

        Private Shared Function ParseOrderByFields(sql As String) As List(Of String)
            Dim result As New List(Of String)()
            Dim match = Regex.Match(sql, "(?is)ORDER\s+BY\s+(?<fields>.+?)\s*$")
            If Not match.Success Then Return result

            For Each part In match.Groups("fields").Value.Split(","c)
                Dim fieldMatch = Regex.Match(part.Trim(), "^\[?(?<field>[A-Za-z_][\w]*)\]?")
                If fieldMatch.Success Then result.Add(fieldMatch.Groups("field").Value)
            Next
            Return result
        End Function

        Private Shared Function ParseOrderByDirections(sql As String) As Dictionary(Of String, String)
            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim match = Regex.Match(sql, "(?is)ORDER\s+BY\s+(?<fields>.+?)\s*$")
            If Not match.Success Then Return result

            For Each part In match.Groups("fields").Value.Split(","c)
                Dim fieldMatch = Regex.Match(part.Trim(), "^\[?(?<field>[A-Za-z_][\w]*)\]?\s*(?<direction>ASC|DESC)?")
                If fieldMatch.Success Then
                    result(fieldMatch.Groups("field").Value) = If(String.Equals(fieldMatch.Groups("direction").Value, "DESC", StringComparison.OrdinalIgnoreCase), "DESC", "ASC")
                End If
            Next
            Return result
        End Function

        Private Function BuildGeneratedBrowseSql(tableName As String,
                                                 browseGrid As DataGridView,
                                                 fields As List(Of String),
                                                 primaryKeyField As String,
                                                 orderByFields As List(Of String)) As String
            If String.IsNullOrWhiteSpace(primaryKeyField) Then Return String.Empty

            Dim selectedFields = browseGrid.Rows.Cast(Of DataGridViewRow)().
                Where(Function(row) Convert.ToBoolean(row.Cells("Include").Value)).
                Select(Function(row) Convert.ToString(row.Cells("FieldName").Value)).
                ToList()
            If Not selectedFields.Any(Function(field) String.Equals(field, primaryKeyField, StringComparison.OrdinalIgnoreCase)) Then
                selectedFields.Insert(0, primaryKeyField)
            End If

            Dim hasRegistrationField = useRegistrationIdCheckBox.Checked AndAlso fields.Any(Function(field) String.Equals(field, "RegistrationID", StringComparison.OrdinalIgnoreCase))

            Dim selectParts = selectedFields.Select(
                Function(field) If(String.Equals(field, primaryKeyField, StringComparison.OrdinalIgnoreCase),
                                   "[" & field & "] AS PK",
                                   "[" & field & "]"))
            Dim sql As New StringBuilder()
            sql.AppendLine("SELECT")
            sql.AppendLine("    " & String.Join("," & Environment.NewLine & "    ", selectParts))
            sql.AppendLine("FROM dbo.[" & tableName.Replace("]", "]]", StringComparison.Ordinal) & "]")
            If hasRegistrationField Then
                sql.AppendLine("WHERE [RegistrationID] = @RegistrationID")
            End If
            Dim resolvedOrderByFields = If(orderByFields Is Nothing, New List(Of String)(), orderByFields).
                Where(Function(field) fields.Any(Function(tableField) String.Equals(tableField, field, StringComparison.OrdinalIgnoreCase))).
                ToList()
            If resolvedOrderByFields.Count = 0 Then
                resolvedOrderByFields.Add(primaryKeyField)
            End If

            Dim orderByParts As New List(Of String)()
            For Each field In resolvedOrderByFields
                Dim direction = "ASC"
                For Each row As DataGridViewRow In browseGrid.Rows
                    If String.Equals(Convert.ToString(row.Cells("FieldName").Value), field, StringComparison.OrdinalIgnoreCase) Then
                        direction = If(String.Equals(Convert.ToString(row.Cells("Direction").Value), "DESC", StringComparison.OrdinalIgnoreCase), "DESC", "ASC")
                        Exit For
                    End If
                Next
                orderByParts.Add("[" & field.Replace("]", "]]", StringComparison.Ordinal) & "] " & direction)
            Next
            sql.Append("ORDER BY " & String.Join(", ", orderByParts))
            Return sql.ToString()
        End Function

        Protected Overrides Function SaveRecord() As Boolean
            Try
                RefreshSavedPageDocumentTemplate()
                Dim values As New Dictionary(Of String, Object) From {
                    {"RequestName", DbSaveValue(requestNameTextBox.Text)},
                    {"PageBaseName", DbSaveValue(pageBaseNameTextBox.Text)},
                    {"BrowsePageName", DbSaveValue(browsePageNameTextBox.Text)},
                    {"MaintenancePageName", DbSaveValue(maintenancePageNameTextBox.Text)},
                    {"GenerateBrowsePage", generateBrowsePageCheckBox.Checked},
                    {"GenerateMaintenancePage", generateMaintenancePageCheckBox.Checked},
                    {"CreateAsFrameworkPages", createAsFrameworkPagesCheckBox.Checked},
                    {"UseQbeOnly", useQbeOnlyCheckBox.Checked},
                    {"UnderlyingTableName", DbSaveValue(underlyingTableNameTextBox.Text)},
                    {"UseRegistrationID", useRegistrationIdCheckBox.Checked},
                    {"BrowseFields", DbSaveValue(browseFieldsTextBox.Text)},
                    {"MaintenanceFields", DbSaveValue(maintenanceFieldsTextBox.Text)},
                    {"BrowseSql", DbSaveValue(browseSqlTextBox.Text)},
                    {"LookupFields", DbSaveValue(lookupFieldsTextBox.Text)},
                    {"AdminRequiredFields", DbSaveValue(adminRequiredFieldsTextBox.Text)},
                    {"MenuCaller", DbSaveValue(SelectedMenuCaller())},
                    {"IconFileName", DbSaveValue(IconChoiceValue(iconFileNameTextBox.Text))}
                }
                If Not DataAccess.SavePageGeneration(isNewRecord, Integer.Parse(If(String.IsNullOrWhiteSpace(pageRequestIdTextBox.Text), "0", pageRequestIdTextBox.Text)), values, originalRowVersion) Then
                    ' A deleted record is not a conflict to overwrite.
                    If HandleRecordDeletedDuringSave() Then Return False
                    Return ConfirmConcurrencyOverwrite()
                End If
                Dim savedRequestId = If(String.IsNullOrWhiteSpace(pageRequestIdTextBox.Text),
                                        DataAccess.GetPageGenerationId(requestNameTextBox.Text, browsePageNameTextBox.Text, maintenancePageNameTextBox.Text),
                                        Integer.Parse(pageRequestIdTextBox.Text))
                If savedRequestId > 0 Then
                    Dim savedRow = DataAccess.GetPageGenerationById(savedRequestId)
                    If savedRow IsNot Nothing Then
                        pageRequestIdTextBox.Text = DbText(savedRow("PageRequestID"))
                        recordId = savedRequestId
                        isNewRecord = False
                        If Not savedRow.IsNull("RowVersion") Then
                            originalRowVersion = CType(DirectCast(savedRow("RowVersion"), Byte()).Clone(), Byte())
                            CaptureOriginalRowVersion(originalRowVersion)
                        End If
                    End If
                End If
                hasUnsavedChanges = False
                Return True
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message.ToUpperInvariant(), "SAVE PAGE REQUEST", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End Try
        End Function

        Private Shared Function DbSaveValue(value As String) As Object
            Return If(String.IsNullOrWhiteSpace(value), CType(Nothing, Object), value.Trim())
        End Function

        Private Shared Function DbText(value As Object) As String
            Return If(value Is Nothing OrElse IsDBNull(value), String.Empty, Convert.ToString(value, Globalization.CultureInfo.InvariantCulture))
        End Function

        Private Function CopyGeneratedPageRequestToClipboard() As Boolean
            Try
                Clipboard.SetText(BuildGeneratedPageRequest())
                Return True
            Catch
                ' A clipboard failure must not turn a successful database save into a failed save.
                Return False
            End Try
        End Function

        Private Function BuildGeneratedPageRequest() As String
            Dim template = GenerationDocumentTemplate()

            Return template.
                Replace("{{REQUEST_NAME}}", ValueOrDefault(requestNameTextBox.Text), StringComparison.Ordinal).
                Replace("{{PAGE_BASE_NAME}}", ValueOrDefault(pageBaseNameTextBox.Text), StringComparison.Ordinal).
                Replace("{{USE_REGISTRATION_ID}}", If(useRegistrationIdCheckBox.Checked, "Yes", "No"), StringComparison.Ordinal).
                Replace("{{USE_QBE_ONLY}}", If(useQbeOnlyCheckBox.Checked, "Yes", "No"), StringComparison.Ordinal).
                Replace("{{BROWSE_PAGE_NAME}}", ValueOrDefault(browsePageNameTextBox.Text), StringComparison.Ordinal).
                Replace("{{MAINTENANCE_PAGE_NAME}}", ValueOrDefault(maintenancePageNameTextBox.Text), StringComparison.Ordinal).
                Replace("{{UNDERLYING_TABLE}}", ValueOrDefault(underlyingTableNameTextBox.Text), StringComparison.Ordinal).
                Replace("{{LOOKUP_FIELDS}}", ValueOrDefault(lookupFieldsTextBox.Text), StringComparison.Ordinal).
                Replace("{{ADMIN_REQUIRED_FIELDS}}", ValueOrDefault(adminRequiredFieldsTextBox.Text), StringComparison.Ordinal).
                Replace("{{MENU_CALLER}}", ValueOrDefault(SelectedMenuCaller()), StringComparison.Ordinal).
                Replace("{{BROWSE_SQL}}", browseSqlTextBox.Text.Trim(), StringComparison.Ordinal).
                Replace("{{BROWSE_FIELDS}}", ValueOrDefault(browseFieldsTextBox.Text), StringComparison.Ordinal).
                Replace("{{MAINTENANCE_FIELDS}}", ValueOrDefault(maintenanceFieldsTextBox.Text), StringComparison.Ordinal).
                Replace("{{GENERATION_DIRECTIONS}}", DefaultGenerationDirections(), StringComparison.Ordinal)
        End Function

            Private Sub RefreshSavedPageDocumentTemplate()
                If directionsTextBox Is Nothing Then Return
                SetDirectionsDocument()
            End Sub

        Private Sub SetDirectionsDocument()
            If directionsTextBox Is Nothing Then Return
            directionsTextBox.Items.Clear()
            directionsTextBox.Items.AddRange(GenerationDocumentTemplate().
                                             Replace("{{GENERATION_DIRECTIONS}}", DefaultGenerationDirections(), StringComparison.Ordinal).
                                             Split({Environment.NewLine}, StringSplitOptions.None))
        End Sub

        Private Shared Function GenerationDocumentTemplate() As String
            Return String.Join(Environment.NewLine, {
                "# New Browse and Maintenance Page Request",
                "",
                "**Pages will be inherited from `FW_Base_B` and `FW_Base_U`.**",
                "",
                "**1. Request Name:**",
                "Answer: `{{REQUEST_NAME}}`",
                "",
                "## Page Request",
                "",
                "**2. Generate Browse Page (_B):**",
                "Answer: `True` or `False` (new requests default to `True`)",
                "",
                "**2a. Use QBE Only on the Browse Page (_B):**",
                "Answer: `Yes` or `No` (new requests default to `No`; hides CRUD buttons on the generated `_B` page)",
                "",
                "**3. Generate Maintenance Page (_U):**",
                "Answer: `True` or `False` (new requests default to `True`)",
                "",
                "**4. Pages To Generate:**",
                "Answer: `{{PAGE_BASE_NAME}}`",
                "",
                "**5. Browse Page Name:**",
                "Answer: `{{PAGE_BASE_NAME}}_B`",
                "",
                "**6. Maintenance Page Name:**",
                "Answer: `{{PAGE_BASE_NAME}}_U`",
                "",
                "**7. Underlying Table:**",
                "Answer: `{{UNDERLYING_TABLE}}`",
                "",
                "**8. Lookup Fields:**",
                "Answer: `{{LOOKUP_FIELDS}}`",
                "",
                "**9. Admin Required Fields:**",
                "Answer: `{{ADMIN_REQUIRED_FIELDS}}`",
                "",
                "**10. Menu or Caller to Open the Page:**",
                "Answer: `{{MENU_CALLER}}`",
                "",
                "## Browse SQL",
                "",
                "**11. Browse SQL:**",
                "```sql",
                "{{BROWSE_SQL}}",
                "```",
                "",
                "**12. Use RegistrationID from `{{UNDERLYING_TABLE}}`:**",
                "Answer: `{{USE_REGISTRATION_ID}}`",
                "",
                "**13. Use QBE Only on the Browse Page (_B):**",
                "Answer: `{{USE_QBE_ONLY}}`",
                "",
                "## Field Selection Metadata",
                "",
                "**_B data grid fields:**",
                "Value: `{{BROWSE_FIELDS}}`",
                "",
                "**_U maintenance fields:**",
                "Value: `{{MAINTENANCE_FIELDS}}`",
                "",
                "## Complete Generation Directions",
                "",
                "{{GENERATION_DIRECTIONS}}",
                "",
                "## Request-Specific Rules",
                "",
                "- Use the exact database field names from the selected table.",
                "- Every selected `_U` field appears on the maintenance page.",
                "- A selected `_U` field is not automatically Admin Required or a Lookup.",
                "- Admin Required and Lookup are independent choices.",
                "- If Menu Caller is not explicit (for example Main Menu or Dashboard), choose the exact caller from the prompt list.",
                "- RegistrationID appears in SELECT only when it is explicitly selected in `_B data grid fields`; Use RegistrationID only controls the WHERE filter.",
                "- Create only the page targets whose Generate checkbox is checked.",
                "- If Generate Browse Page is False, do not create or overwrite the `_B` page, its generated dashboard icon, or its generated FW_RoleTables record.",
                "- If Generate Maintenance Page is False, do not create or overwrite the `_U` page or its generated maintenance baseline.",
                "- Generate Maintenance Page requires Generate Browse Page; an `_U` page is never generated by itself.",
                "- Create-only mode is the default: if target `_B`/`_U` pages or SQL already exist, report that to the user and skip updates.",
                "- Explicit regeneration may overwrite only the requested existing `_B` and `_U` page implementations and generated SQL or metadata.",
                "- During regeneration, preserve the existing dashboard icon, ActionKey, caption, position, target registration, and click handler.",
                "- Deleting pages and removing their dashboard icon or metadata is a separate explicit operation and is not part of regeneration.",
                "- Admin Required fields use a trailing `*` and a red empty-control border during save validation.",
                "- Lookup fields use the shared lookup-control pattern and are not automatically required.",
                "- The supplied SQL must expose the maintenance key explicitly as `PK`.",
                "- Do not include `DeletedFlag` or `RowVersion` in the supplied SQL; the framework handles them automatically.",
                "- Use the active session user and access profile.",
                "- Preserve RowVersion through load, clone, form binding, and save.",
                "",
                "## COMPLETION CONFIRMATION",
                "",
                "PAGE GENERATION REQUEST DOCUMENT COMPLETED SUCCESSFULLY."
            })
        End Function

        Private Shared Function ValueOrDefault(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return "Not specified"
            Return value.Trim()
        End Function

        Private Shared Function DefaultGenerationDirections() As String
            Return String.Join(Environment.NewLine, {
                "PAGE GENERATION IMPLEMENTATION DIRECTIONS",
                "",
                "FIELD SELECTION",
                "- Generate the browse page from the selected _B Data Grid Fields.",
                "- Generate the maintenance page from the selected _U Maintenance Fields.",
                "- Generate Browse Page and Generate Maintenance Page are persisted per request and default to True.",
                "- Use QBE Only is persisted per request and defaults to No.",
                "- When Use QBE Only is Yes, the generated _B page overrides OnlyUseQbe() and hides CRUD buttons while retaining QBE search.",
                "- At least one target must be selected; a maintenance page requires a browse page.",
                "- Create only the page targets whose Generate checkbox is checked.",
                "- If Generate Browse Page is False, do not create or overwrite the _B page, its generated dashboard icon, or its generated FW_RoleTables record.",
                "- If Generate Maintenance Page is False, do not create or overwrite the _U page or its generated maintenance baseline.",
                "- Generate Maintenance Page requires Generate Browse Page; an _U page is never generated by itself.",
                "- _B selection is independent and may be used without selecting any _U field.",
                "- Every selected _U field appears on the maintenance page.",
                "- A selected _U field is not automatically Admin Required or a Lookup.",
                "- A normal _U field remains editable without required validation.",
                "- Every generated _U caption height matches the height of its edit control.",
                "",
                "_U FIELD RULES",
                "- Admin Required and Lookup are independent choices.",
                "- Selecting Admin Required or Lookup automatically selects Use in _U.",
                "- Unselecting Use in _U clears Admin Required and Lookup.",
                "- Admin Required labels use a trailing *.",
                "- Empty Admin Required controls show a 1-pixel red outline during save validation.",
                "- A filled Admin Required control clears its red border on the next save attempt.",
                "- PageGeneration_U uses a 1-pixel red outline for empty required textboxes only after validation begins.",
                "- Generated _U pages must inherit required-field styling and validation from FW_Base_U.",
                "- Generated _U pages must use the shared AddField and required-validation contract.",
                "- Generated _U pages must not create page-local red-border panels or duplicate required-field event handlers.",
                "- Lookup fields use the shared lookup-control pattern and are not automatically required.",
                "- Missing required fields appear in one combined validation message.",
                "",
                "BROWSE SQL",
                "- The browse SQL must expose the table primary key explicitly as PK.",
                "- When Use Registration ID is checked and the table contains RegistrationID, generate WHERE [RegistrationID] = @RegistrationID.",
                "- Use Registration ID controls only the WHERE predicate and does not auto-add RegistrationID to displayed columns.",
                "- Include RegistrationID in SELECT only when it is explicitly selected in _B Data Grid Fields.",
                "- When appended, the RegistrationID predicate must be the last WHERE condition.",
                "- Order By fields are independent of displayed _B fields and may include fields not shown in the grid.",
                "- The order in which Order By fields are checked determines their order in the ORDER BY clause.",
                "- Each selected Order By field defaults to ASC and may be changed to DESC.",
                "- If no explicit Order By field is selected, default to PK ASC.",
                "- When Use Registration ID is unchecked, do not generate a RegistrationID predicate.",
                "- The user may edit or remove the RegistrationID filter before validation.",
                "- Validate the final SQL before saving the request.",
                "- Do not include DeletedFlag or RowVersion in supplied browse SQL.",
                "",
                "PAGE AND DATA CONTRACT",
                "- Use FW_Base_B for the browse page and FW_Base_U for the maintenance page.",
                "- Browse and maintenance page names must end in _B and _U.",
                "- Create-only mode is the default: generate page files and SQL only when they do not already exist.",
                "- If target page files or SQL artifacts already exist, do not update them unless explicit regeneration or overwrite was requested.",
                "- Explicit regeneration may overwrite only the requested _B and _U page implementations and generated SQL or metadata.",
                "- During regeneration, preserve the existing dashboard icon, ActionKey, caption, position, target registration, and click handler.",
                "- Deleting pages and removing their dashboard icon or metadata is a separate explicit operation.",
                "- The underlying table must exist in dbo and have a primary key.",
                "- Every selected _B and _U field must exist in the underlying table.",
                "- If Menu Caller is generic (for example Main Menu or Dashboard), prompt for an explicit caller selection from a list.",
                "- Pass the active user and access profile to both generated pages.",
                "- Preserve RowVersion through load, binding, editing, and save.",
                "- Keep all generated-page writes inside the shared data and audit contracts.",
                "- Help Desk may retain documented state-dependent required rules for ticket creation and response workflows.",
                "- Run the browse framework preflight before declaring the generated pages complete.",
                "- Run a duplicate-logic check before generating the pages; consolidate repeated shared behavior instead of creating a second implementation.",
                "- Run the applicable _B/_U regression checks for inheritance, field bindings, SQL ownership, required fields, lookup fields, permissions, and callers.",
                "- Build the application after the regression checks pass.",
                "- Manually verify create, update, cancel, SQL validation, required-field validation, and the generated page workflow."
            })
        End Function

    End Class
End Namespace
