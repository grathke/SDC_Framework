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

Namespace SDC.Framework
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

        ''' <summary>
        ''' Whether the generated browse page shows the Hot Fields strip.
        ''' </summary>
        ''' <remarks>
        ''' Stored on the request so reopening it shows the answer as it was left, and written to
        ''' FW_Pages.UseHotFields when the page is generated - which is what the running page reads.
        ''' </remarks>
        Private displayHotFieldsCheckBox As CheckBox
        Private ownerComboBox As ComboBox
        Private ownerBorderPanel As Panel
        Private underlyingTableNameTextBox As TextBox
        Private tableAliasTextBox As TextBox
        Private generateBrowsePageCheckBox As CheckBox
        Private useQbeOnlyCheckBox As CheckBox
        Private generateMaintenancePageCheckBox As CheckBox
        Private useRegistrationIdCheckBox As CheckBox
        Private browseSqlTextBox As TextBox
        Private browseFieldsTextBox As TextBox
        Private hotFieldsTextBox As TextBox
        Private maintenanceFieldsTextBox As TextBox
        Private directionsTextBox As ListBox
        Private lookupFieldsTextBox As TextBox
        Private adminRequiredFieldsTextBox As TextBox
        Private menuCallerComboBox As ComboBox
        Private iconFileNameTextBox As TextBox
        Private selectIconButton As Button
        Private iconPreviewBox As PictureBox
        Private generatedPageIdTextBox As TextBox
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
        Private enabledTablesButton As Button
        Private selectFieldsButton As Button
        ' The caption line the base class draws on the form: caption at y=15, Tab Order button at
        ' y=10, both about 28 tall. Questions start below it, and the form is taller by the delta
        ' against the 12 the layout used before.
        Private Const MakeASelection As String = DataAccess.EmptyComboPlaceholder

        Private Const PageHeaderBandHeight As Integer = 50
        Private Const PageHeaderBandDelta As Integer = 38

        ''' Extra room for the Browse SQL editor, and the matching growth in the form so nothing
        ''' below it is squeezed.
        Private Const BrowseSqlExtraHeight As Integer = 160

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
        Private browsePageHasManualChanges As Boolean
        Private maintenancePageHasManualChanges As Boolean

        ''' <summary>
        ''' Set once the user has answered all three prompts to regenerate over their own edits.
        ''' Lasts only while this page is open, and suppresses the ordinary overwrite prompt: the
        ''' question it asks has already been answered, twice over and in stronger terms.
        ''' </summary>
        Private manualChangesOverridden As Boolean

        ''' <summary>
        ''' Set when the user declines to regenerate over their own edits. The page is loaded far
        ''' enough to ask the question before it is shown, so declining cannot simply close a form
        ''' that has not opened yet - the caller reads OpenCancelled instead and never shows it.
        ''' </summary>
        Private openWasCancelled As Boolean

        ''' <summary>
        ''' True when the page decided during construction that it should not open. The caller must
        ''' check this before ShowDialog and dispose the page without showing it.
        ''' </summary>
        Public ReadOnly Property OpenCancelled As Boolean
            Get
                Return openWasCancelled
            End Get
        End Property

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
            Return DataAccess.GeneratedPagesTable
        End Function

        Protected Overrides Function GetPageName() As String
            Return NameOf(PageGeneration_U)
        End Function

        Protected Overrides Function ResolveAuditRecordKey() As String
            Return generatedPageIdTextBox.Text.Trim()
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

            generatedPageIdTextBox = New TextBox With {.Name = "TextBox_GeneratedPageID", .Visible = False}
            Controls.Add(generatedPageIdTextBox)
            requestNameTextBox = AddEntryField(fields, "RequestName", False, 34, 150, False, "1. Request Name")
            pageBaseNameTextBox = AddEntryField(fields, "PageBaseName", False, 34, 150, False, "2. Pages To Generate")
            ' Who the pages belong to. One answer for the prefix and the folder, which were a
            ' checkbox and a constant until 2026-09-18 and could disagree.
            '
            ' "Make a Selection" is the default and is not a valid answer: the old checkbox
            ' defaulted to unticked, which silently meant "not framework" and was right only by
            ' accident. An owner decides where files are written and what every table and page is
            ' called, which is too much to infer from somebody not touching a control.
            ownerComboBox = New ComboBox With {
                .Name = "ComboBox_Owner",
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Width = 220,
                .Margin = New Padding(8, 4, 0, 0),
                .BackColor = FW_Base_U.AppAdminRequiredBackColor
            }
            PopulateOwnerCombo()

            ' The red border a required field shows when it is empty. Base_U builds one as a panel
            ' behind the control, and this page lays its own fields out, so it is built here the
            ' same way rather than bent into the base's helper: a panel one pixel larger than the
            ' combo on every side, showing through as a border.
            ' Sized from the combo, not auto-sized. An AutoSize panel holding a docked control
            ' measures to nothing - the control fills the panel, the panel shrinks to its contents,
            ' and both end up zero wide. The combo was invisible on the page until this was fixed.
            ownerBorderPanel = New Panel With {
                .Name = "Panel_OwnerRequiredBorder",
                .Padding = New Padding(1),
                .Size = New Size(ownerComboBox.Width + 2, ownerComboBox.Height + 2),
                .Margin = New Padding(8, 2, 0, 0)
            }
            ownerComboBox.Margin = New Padding(0)
            ownerComboBox.Dock = DockStyle.Fill
            ownerBorderPanel.Controls.Add(ownerComboBox)

            AddControlBesideField(fields, pageBaseNameTextBox, ownerBorderPanel)
            RefreshOwnerRequiredBorder()
            browsePageNameTextBox = AddEntryField(fields, "BrowsePageName", False, 34, 150, False, "3. Browse Page Name")
            maintenancePageNameTextBox = AddEntryField(fields, "MaintenancePageName", False, 34, 150, False, "4. Maintenance Page Name")
            generateBrowsePageCheckBox = New CheckBox With {.Text = "Generate", .Checked = False, .AutoSize = True, .Margin = New Padding(8, 6, 0, 0)}
            useQbeOnlyCheckBox = New CheckBox With {.Name = "CheckBox_UseQbeOnly", .Text = "Use QBE only", .Checked = False, .AutoSize = True, .Margin = New Padding(8, 6, 0, 0)}
            displayHotFieldsCheckBox = New CheckBox With {.Name = "CheckBox_UseHotFields", .Text = "Display Hotfields", .Checked = False, .AutoSize = True, .Margin = New Padding(8, 6, 0, 0)}
            generateMaintenancePageCheckBox = New CheckBox With {.Text = "Generate", .Checked = False, .AutoSize = True, .Margin = New Padding(8, 6, 0, 0)}
            ' Sized to its contents. Left at its default size it kept the width of an empty panel,
            ' and with WrapContents off the third checkbox was simply clipped off the end rather
            ' than wrapping into view.
            Dim browseGenerationOptions As New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .WrapContents = False,
                .AutoSize = True,
                .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .FlowDirection = FlowDirection.LeftToRight,
                .Padding = New Padding(0)
            }
            browseGenerationOptions.Controls.Add(generateBrowsePageCheckBox)
            browseGenerationOptions.Controls.Add(useQbeOnlyCheckBox)
            browseGenerationOptions.Controls.Add(displayHotFieldsCheckBox)
            AddControlBesideField(fields, browsePageNameTextBox, browseGenerationOptions)
            AddControlBesideField(fields, maintenancePageNameTextBox, generateMaintenancePageCheckBox)
            AddHandler pageBaseNameTextBox.Leave, AddressOf PageBaseNameTextBox_Leave
            AddHandler browsePageNameTextBox.Leave, AddressOf PageNameTextBox_Leave
            AddHandler maintenancePageNameTextBox.Leave, AddressOf PageNameTextBox_Leave
            AddHandler ownerComboBox.SelectedIndexChanged, AddressOf OwnerComboBox_SelectedIndexChanged
            AddHandler generateBrowsePageCheckBox.CheckedChanged, AddressOf GenerateBrowsePageCheckBox_CheckedChanged
            AddHandler useQbeOnlyCheckBox.CheckedChanged, AddressOf BrowseOptionCheckBox_CheckedChanged
            AddHandler displayHotFieldsCheckBox.CheckedChanged, AddressOf BrowseOptionCheckBox_CheckedChanged

            ' A tick is a change. Only text boxes marked the form dirty, so Save && Generate - which
            ' saves only when the request is new or dirty - skipped the save entirely for a request
            ' whose only edit was a checkbox.
            '
            ' Ticking Create as Framework Pages usually rewrites the page names too, and *that*
            ' marked it dirty, which is why this went unnoticed: it failed only when the names
            ' already read FW_..., so the rewrite changed no text. The request then generated with
            ' the setting it had on disk - unticked - and wrote Employees_B where the box said
            ' FW_Employees_B.
            AddHandler ownerComboBox.SelectedIndexChanged, AddressOf MarkDirty
            AddHandler generateBrowsePageCheckBox.CheckedChanged, AddressOf MarkDirty
            AddHandler generateMaintenancePageCheckBox.CheckedChanged, AddressOf MarkDirty
            AddHandler useQbeOnlyCheckBox.CheckedChanged, AddressOf MarkDirty
            AddHandler displayHotFieldsCheckBox.CheckedChanged, AddressOf MarkDirty
            underlyingTableNameTextBox = AddEntryField(fields, "UnderlyingTableName", True, 34, 150, False, "5. Underlying Table Name")
            AddHandler underlyingTableNameTextBox.TextChanged, AddressOf UnderlyingTableNameTextBox_TextChanged
            Dim underlyingTableRow = fields.GetRow(underlyingTableNameTextBox)
            fields.Controls.Remove(underlyingTableNameTextBox)
            ' Question 5 puts the chosen table beside the button that chose it. Both live in a
            ' FlowLayoutPanel so the framework hosts their focus borders instead of adding a panel
            ' to the question grid.
            tableSelectionPanel = New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .AutoSize = False,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .Padding = New Padding(0)
            }
            selectTableButton = New Button With {
                .Text = "Select Table",
                .Size = New Size(110, 32),
                .FlatStyle = FlatStyle.Standard,
                .UseVisualStyleBackColor = True,
                .Margin = New Padding(0, 0, 8, 0)
            }
            AddHandler selectTableButton.Click, AddressOf SelectTableButton_Click
            tableSelectionPanel.Controls.Add(selectTableButton)

            ' Beside the picker, because this is where a missing table is noticed: the list is open,
            ' the table is not in it, and going out to a dashboard to enable it and coming back is
            ' the long way round. App Admin only, being a decision about the whole application.
            enabledTablesButton = New Button With {
                .Text = "Tables…",
                .Size = New Size(80, 32),
                .FlatStyle = FlatStyle.Standard,
                .UseVisualStyleBackColor = True,
                .Margin = New Padding(0, 0, 8, 0),
                .Visible = SessionState.IsApplicationAdmin
            }
            Dim enabledTablesTip As New ToolTip()
            enabledTablesTip.SetToolTip(enabledTablesButton, "Which tables are available at all")
            AddHandler enabledTablesButton.Click, AddressOf EnabledTablesButton_Click
            tableSelectionPanel.Controls.Add(enabledTablesButton)
            ' Nudged down so the shorter text box sits on the button's centre line rather than its top.
            underlyingTableNameTextBox.Margin = New Padding(0, 3, 0, 0)

            ' The box is read-only, so clicking it can only mean "I want to change this" - and the
            ' one way to change it is the button beside it. The button's own handler is invoked
            ' rather than the picker being opened again here, so there is a single path to a table
            ' being chosen however the user asks for it.
            '
            ' Click, not hover. The application is served through Thinfinity VirtualUI, where
            ' mouse-move events are coalesced and hover fires late or not at all, while a click
            ' always arrives.
            underlyingTableNameTextBox.Cursor = Cursors.Hand
            AddHandler underlyingTableNameTextBox.Click, AddressOf SelectTableButton_Click

            tableSelectionPanel.Controls.Add(underlyingTableNameTextBox)
            fields.Controls.Add(tableSelectionPanel, 1, underlyingTableRow)
            ' A single-line row now that the two sit side by side - the same height AddEntryField gives
            ' every other one-line question, so question 6 closes up behind it.
            fields.RowStyles(underlyingTableRow).Height = 46

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
            ' What the page is called. Typed rather than derived, and stored on the request, so it
            ' survives the next generation - the generator used to work the caption out from the
            ' table name every time and write it over FW_Pages.Table_Alias, so a name chosen by hand
            ' lasted until somebody generated the page again.
            '
            ' Editable, unlike most of the fields on this form. Left empty it derives as before.
            tableAliasTextBox = AddEntryField(fields, "TableAlias", False, 34, 260, False, "5a. Page Caption")

            AddQuestionRow(fields, "SelectFields", "6. Select Fields", fieldSelectionPanel, 46)

            browseFieldsTextBox = AddEntryField(fields, "BrowseFields", True, 34, 780, False, "   " & ChrW(8226) & " _B Fields")
            hotFieldsTextBox = AddEntryField(fields, "HotFields", True, 34, 780, False, "   " & ChrW(8226) & " Hot Fields")
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
            ComboWidth.FitToContent(menuCallerComboBox)
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
            ' The whole panel, less the row the Validate button sits on. The box was 165 in a panel
            ' of 265, so sixty pixels of the room already reserved for it went unused and the SQL
            ' scrolled four lines earlier than it needed to.
            browseSqlTextBox.Dock = DockStyle.Top
            browseSqlTextBox.Height = 165 + BrowseSqlExtraHeight
            browseSqlPanel.Controls.Add(browseSqlTextBox)
            validateSqlButton = New Button With {
                .Text = "Validate SQL",
                .Size = New Size(110, 32),
                .Location = New Point(browseSqlPanel.Width - 110, 170 + BrowseSqlExtraHeight),
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

            ' Nothing on screen said what this does, and unticked it produces a page that lists
            ' every registration's rows - which reads as the registration combo being broken rather
            ' than as a setting nobody made. FW_Employees_B shipped that way.
            Dim registrationNote As New Label With {
                .Name = "Note_UseRegistrationID",
                .Text = "Tick for a table whose rows belong to one registration." & Environment.NewLine &
                        "Without it the page lists every registration's rows.",
                .AutoSize = False,
                .Size = New Size(330, 32),
                .Anchor = AnchorStyles.Left Or AnchorStyles.Top,
                .Margin = New Padding(12, 2, 0, 0),
                .ForeColor = Color.DimGray,
                .TextAlign = ContentAlignment.MiddleLeft
            }
            AddControlBesideField(fields, useRegistrationIdCheckBox, registrationNote)

            Dim registrationTip As New ToolTip()
            Dim registrationTipText =
                "Adds WHERE <alias>.[RegistrationID] = @RegistrationID to the browse SQL." & Environment.NewLine &
                Environment.NewLine &
                "The registration combo on the page fills in the value: an App Admin can change it," & Environment.NewLine &
                "everybody else gets their own registration." & Environment.NewLine &
                Environment.NewLine &
                "Leave it off only for a table genuinely shared across registrations - a lookup list," & Environment.NewLine &
                "or one with no RegistrationID column at all."
            registrationTip.SetToolTip(useRegistrationIdCheckBox, registrationTipText)
            registrationTip.SetToolTip(registrationNote, registrationTipText)

            UpdateRegistrationOptionState()
            AddHandler useRegistrationIdCheckBox.CheckedChanged, AddressOf UseRegistrationIdCheckBox_CheckedChanged
            AddHandler useRegistrationIdCheckBox.CheckedChanged, AddressOf MarkDirty
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

            ' The difference between the two buttons is the whole of what somebody needs to know
            ' here, and neither caption says it: Save stores the request, Save & Generate also
            ' writes the files. The same distinction the update dialog has to spell out.
            Dim footerTips As New ToolTip()
            footerTips.SetToolTip(okButton, "Saves the request only, does not update any page.")
            footerTips.SetToolTip(generatePagesButton, "Saves the request and updates the pages.")

            ApplyQuestionTabOrder()
        End Sub

        ''' <summary>
        ''' Re-reads the row's concurrency token after generation has written to it.
        '''
        ''' Silent on failure: a token that cannot be re-read leaves the old one in place, and the
        ''' next save asks about a conflict - which is the safe direction. Generation has already
        ''' succeeded by this point, and a message about a token nobody has heard of would only
        ''' confuse a report that is otherwise good news.
        ''' </summary>
        Private Sub RecaptureRowVersion(generationRequestId As Integer)
            If generationRequestId <= 0 Then Return

            Try
                Dim row = DataAccess.GetPageGenerationById(generationRequestId)
                If row Is Nothing OrElse Not row.Table.Columns.Contains("RowVersion") OrElse row.IsNull("RowVersion") Then Return

                originalRowVersion = CType(DirectCast(row("RowVersion"), Byte()).Clone(), Byte())
                CaptureOriginalRowVersion(originalRowVersion)
            Catch
            End Try
        End Sub

        Private Sub GeneratePagesButton_Click(sender As Object, e As EventArgs)
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

            Dim browsePagePath = PageGenerator.GeneratedPagePath(Environment.CurrentDirectory, browsePageNameTextBox.Text)
            Dim maintenancePagePath = PageGenerator.GeneratedPagePath(Environment.CurrentDirectory, maintenancePageNameTextBox.Text)
            Dim overwriteExistingPages = False
            If manualChangesOverridden Then
                ' Already authorised, three times over, when the page was opened.
                overwriteExistingPages = True
            ElseIf (generateBrowsePageCheckBox.Checked AndAlso File.Exists(browsePagePath)) OrElse
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

            ' Generation writes to this very row - the hashes of the source it just emitted - so the
            ' RowVersion the page is holding is now a version behind. Saving afterwards asked
            ' "changed by another user, overwrite?", which was this page, one second earlier, and
            ' the honest answer to the question is that it was never a conflict at all.
            RecaptureRowVersion(generationRequestId)
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
            Dim sqlReady = IsGenerationResultPresent(result, "FW_Pages", String.Empty)
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
            ' Files and database rows are both skipped into one list, so the heading cannot claim
            ' either. It said "SKIPPED BECAUSE THE FILE ALREADY EXISTS" over entries like
            ' "FW_Pages:FW_Employees_B", which is a row whose SQL already matched - no file involved, and
            ' nothing for the reader to go and look at. Splitting them says which is which.
            Dim skippedRecords = skippedPageResults.Where(Function(entry) entry.StartsWith("FW_Pages", StringComparison.OrdinalIgnoreCase)).ToList()
            Dim skippedFiles = skippedPageResults.Where(Function(entry) Not entry.StartsWith("FW_Pages", StringComparison.OrdinalIgnoreCase)).ToList()

            If skippedFiles.Count > 0 Then
                lines.Add(String.Empty)
                lines.Add("SKIPPED BECAUSE THE FILE ALREADY EXISTS:")
                lines.AddRange(skippedFiles)
            End If
            If skippedRecords.Count > 0 Then
                lines.Add(String.Empty)
                lines.Add("ALREADY REGISTERED, NOTHING TO CHANGE:")
                lines.AddRange(skippedRecords)
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

        ''' <summary>
        ''' Compares a generated file against the hash recorded when it was generated. False when
        ''' there is no baseline: a page generated before the baseline existed is not evidence of a
        ''' manual change, and treating it as one would lock a request nobody had touched.
        ''' </summary>
        ''' <param name="generatedHalf">
        ''' Whether the page is written in two files, with the generator owning only one of them.
        ''' True for a _U, which the generator splits; False for a _B, which is still one file.
        '''
        ''' It matters because this is the check that locks the page. Hashing the wrong half
        ''' would report somebody's own code as a manual change to be warned about and offered
        ''' for destruction - the exact opposite of what the split is for.
        ''' </param>
        Private Shared Function FileDiffersFromBaseline(dataRow As DataRowView,
                                                        hashColumn As String,
                                                        pageName As String,
                                                        Optional generatedHalf As Boolean = False) As Boolean
            If dataRow Is Nothing OrElse String.IsNullOrWhiteSpace(pageName) Then Return False
            If Not dataRow.Row.Table.Columns.Contains(hashColumn) OrElse dataRow.Row.IsNull(hashColumn) Then Return False

            Dim expectedHash = DbText(dataRow.Row(hashColumn)).Trim()
            If expectedHash = String.Empty Then Return False

            Dim pagePath = PageGenerator.GeneratedPagePath(Environment.CurrentDirectory, pageName)
            If generatedHalf Then pagePath = PageGenerator.GeneratedHalfPath(pagePath)
            If Not File.Exists(pagePath) Then Return False

            Dim hasher As SHA256 = SHA256.Create()
            Using hasher
                Using stream = File.OpenRead(pagePath)
                    Dim currentHash = Convert.ToHexString(hasher.ComputeHash(stream))
                    Return Not String.Equals(expectedHash, currentHash, StringComparison.OrdinalIgnoreCase)
                End Using
            End Using
        End Function

        Private Sub DetectManualMaintenancePageChanges()
            pageHasManualChanges = False
            browsePageHasManualChanges = False
            maintenancePageHasManualChanges = False
            manualChangesOverridden = False
            If isNewRecord Then Return

            Dim pagePath = PageGenerator.GeneratedPagePath(Environment.CurrentDirectory, maintenancePageNameTextBox.Text)

            Dim row = formBindingSource.Current
            Dim dataRow = TryCast(row, DataRowView)

            ' Both generated pages are checked. Only the _U page used to be, so a hand-edited _B was
            ' overwritten by the next generation with no warning at all.
            maintenancePageHasManualChanges = FileDiffersFromBaseline(dataRow, "GeneratedMaintenanceHash", maintenancePageNameTextBox.Text, generatedHalf:=True)
            browsePageHasManualChanges = FileDiffersFromBaseline(dataRow, "GeneratedBrowseHash", browsePageNameTextBox.Text)
            pageHasManualChanges = maintenancePageHasManualChanges OrElse browsePageHasManualChanges

            If Not File.Exists(pagePath) Then
                ProtectManualPageChanges()
                Return
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

            ' Every open of an existing request, not only the ones with something at risk. What the
            ' two buttons do is the thing somebody needs to know before pressing one, and a page
            ' with no hand-written code simply leaves out the line about custom code.
            ProtectManualPageChanges()
        End Sub

        ''' <summary>
        ''' The changed files, named, one per line, for a message.
        ''' </summary>
        Private Function ChangedPageFileList() As String
            Dim changed As New List(Of String)()
            If browsePageHasManualChanges Then changed.Add(browsePageNameTextBox.Text.Trim() & ".vb")
            If maintenancePageHasManualChanges Then changed.Add(maintenancePageNameTextBox.Text.Trim() & ".Generated.vb")
            Return String.Join(Environment.NewLine, changed)
        End Function

        ''' <summary>
        ''' The hand-written half, named, when there is one on disk to name.
        '''
        ''' A maintenance page is two files: the .Generated half, rewritten in full every time, and
        ''' the file named after the page, written once at birth and never read or rewritten again.
        ''' Only the second survives a regeneration, and the message said so without naming it -
        ''' which read as covering the file it did name, the one being replaced.
        '''
        ''' Nothing when the page has no such file, which is every browse page: a _B is a single
        ''' generated file, so there is no untouched half and nothing reassuring to say.
        ''' </summary>
        Private Function UntouchedPageFileList() As String
            Dim pageName = maintenancePageNameTextBox.Text.Trim()
            If pageName = String.Empty Then Return String.Empty

            Dim handWritten = PageGenerator.GeneratedPagePath(Environment.CurrentDirectory, pageName)
            If String.IsNullOrWhiteSpace(handWritten) OrElse Not File.Exists(handWritten) Then Return String.Empty

            Return "CUSTOM CODE IN " & IO.Path.GetFileName(handWritten).ToUpperInvariant() & " IS LEFT UNCHANGED."
        End Function

        ''' <summary>
        ''' Asks whether to regenerate over manual changes, then unlocks the page or abandons it.
        '''
        ''' One question, defaulting to No. Answering No abandons the open: the page never appears,
        ''' the user is back on the browse list they started from, and their edits are untouched.
        '''
        ''' It was three escalating prompts until 2026-09-18, the second of which said "any code you
        ''' added by hand is not in the page request" and "it cannot be put back" - which is not
        ''' true. The file being replaced is the .Generated half, which the generator rewrites in
        ''' full by design; the half named after the page holds hand-written code and is never
        ''' rewritten at all. Three warnings, escalating to "PERMANENT - LAST CHANCE", were guarding
        ''' something that was not at risk, at the moment somebody was trying to do something else.
        ''' </summary>
        ''' <summary>
        ''' Whether the request asks for this half, read from the row rather than from the check box.
        '''
        ''' The boxes are data-bound and are still unticked while this runs - the check happens as
        ''' the record loads, before binding has caught up. Reading them gave a message that named a
        ''' file and then said nothing about what would happen to it.
        ''' </summary>
        Private Function RequestGenerates(columnName As String) As Boolean
            Dim dataRow = TryCast(formBindingSource.Current, DataRowView)
            If dataRow Is Nothing OrElse Not dataRow.Row.Table.Columns.Contains(columnName) Then Return True
            If dataRow.Row.IsNull(columnName) Then Return True

            Return Convert.ToBoolean(dataRow.Row(columnName))
        End Function

        Private Sub ProtectManualPageChanges()
            ' The files that changed, then what happens to each kind, then the question. Named by
            ' file rather than by whose work it was: the change may be the other developer's, or
            ' from a generator that has since altered what it writes. All that is known, and all
            ' that matters, is that the file no longer matches what was generated.
            '
            ' "Updated", never "regenerated". Regenerate describes what the tool does; update
            ' describes what happens to the page, which is what the decision is about.
            Dim message As New StringBuilder()

            ' The file list can be empty: this dialog also opens when the page's fields no longer
            ' match the saved request, where no file has been touched at all. Leading blank lines
            ' for a list that is not there is how the message came to start with white space.
            Dim changedFiles = ChangedPageFileList()
            If changedFiles <> String.Empty Then
                message.AppendLine(changedFiles)
                message.AppendLine()
            End If

            Dim generatesMaintenance = RequestGenerates("GenerateMaintenancePage")
            Dim generatesBrowse = RequestGenerates("GenerateBrowsePage")

            ' One sentence, naming what the request actually asks for. Two lines saying the same
            ' thing about two halves read as two separate warnings when they are one statement.
            If generatesMaintenance OrElse generatesBrowse Then
                Dim subject = If(generatesMaintenance AndAlso generatesBrowse,
                                 "THE BROWSE AND MAINTENANCE PAGES WILL BE UPDATED FROM SELECTIONS MADE HERE.",
                                 If(generatesMaintenance,
                                    "THE MAINTENANCE PAGE WILL BE UPDATED FROM SELECTIONS MADE HERE.",
                                    "THE BROWSE PAGE WILL BE UPDATED FROM SELECTIONS MADE HERE."))
                message.AppendLine(subject)
            End If

            ' A maintenance page is two files and only one of them is rewritten, so hand-written
            ' code survives. Said here because the dialog names the file that does not survive, and
            ' unqualified reassurance read as covering that one.
            If generatesMaintenance Then
                Dim untouched = UntouchedPageFileList()
                If untouched <> String.Empty Then
                    message.AppendLine()
                    message.AppendLine(untouched)
                End If
            End If

            ' A browse page is one file with no companion, so there is nowhere for hand-written code
            ' to survive. The dialog said nothing about that until 2026-09-18 - silent in the one
            ' case where something is actually lost. Splitting a _B the way a _U is split would fix
            ' it properly; saying so is what can be done without touching every existing page.
            '
            ' Only when there is something to replace. A browse page nobody has edited loses
            ' nothing, and warning about it anyway is how a warning stops being read.
            If generatesBrowse AndAlso browsePageHasManualChanges Then
                message.AppendLine()
                message.AppendLine("CHANGES MADE BY HAND IN " &
                                   browsePageNameTextBox.Text.Trim().ToUpperInvariant() &
                                   ".VB WILL BE REPLACED.")
            End If

            ' When, not just what. This dialog opens the request; nothing is written until Save &
            ' Generate is pressed, and Save alone never touches a file. Without the line the message
            ' reads as though answering Yes updates the page there and then.
            message.AppendLine()
            message.AppendLine("THIS HAPPENS WHEN SAVE & GENERATE IS PRESSED.")
            message.AppendLine()
            message.AppendLine("SAVE ON ITS OWN DOES NOT UPDATE ANY PAGE.")
            message.AppendLine()
            message.Append("CONTINUE?")

            Dim proceed = MessageBox.Show(Me,
                            message.ToString(),
                            If(generatesBrowse AndAlso generatesMaintenance,
                               "UPDATE THESE PAGES?",
                               "UPDATE THIS PAGE?"),
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2)

            If proceed = DialogResult.Yes Then
                ' Unlocked, and the overwrite prompt at generation is skipped: that question has
                ' just been answered.
                manualChangesOverridden = True
                RefreshPageCaption()
                Return
            End If

            ' Declined. The page used to open read-only with nothing on it but Close, which read
            ' as the request having opened anyway. Nothing on it could be edited, saved or
            ' generated, so there was nothing to stay for: abandon the open instead.
            openWasCancelled = True
        End Sub

        ''' <summary>
        ''' Whether the generation report mentions a given page, under either name it can have.
        '''
        ''' The class is FW_Employees_B and so is the file. Pages generated before 2026-09-18 carry
        ''' the unprefixed name - Employees_B.vb - which is why both are looked for. The report names
        ''' the path it wrote, so looking only for the class name found nothing, the page decided
        ''' generation was incomplete, said so, and stayed open after a generate that had in fact
        ''' worked.
        ''' </summary>
        Private Shared Function IsGenerationResultPresent(result As PageGenerationResult, marker As String, nameMarker As String) As Boolean
            If MentionsName(result, marker, nameMarker) Then Return True

            If Not String.IsNullOrWhiteSpace(nameMarker) AndAlso nameMarker.StartsWith("FW_", StringComparison.OrdinalIgnoreCase) Then
                Return MentionsName(result, marker, nameMarker.Substring(3))
            End If

            Return False
        End Function

        Private Shared Function MentionsName(result As PageGenerationResult, marker As String, nameMarker As String) As Boolean
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

        ''' <summary>
        ''' A page name typed over by hand gets the same squeeze the derived one does.
        '''
        ''' Both boxes are editable, and a name is only ever a class name - so a space typed into
        ''' one is not a name the generator can refuse politely, it is a name nobody can compile.
        ''' Structural underscores survive: the owner prefix and the _B or _U are kept.
        ''' </summary>
        Private Sub PageNameTextBox_Leave(sender As Object, e As EventArgs)
            Dim textBox = TryCast(sender, TextBox)
            If textBox Is Nothing Then Return

            Dim identifier = PageGenerator.ToPageIdentifier(textBox.Text)
            If Not String.Equals(identifier, textBox.Text, StringComparison.Ordinal) Then
                textBox.Text = identifier
            End If
        End Sub

        Private Sub ApplyGeneratedPageNames(baseName As String)
            Dim normalizedBaseName = RemoveOwnerPrefix(baseName)
            Dim owner = SelectedOwner()
            Dim pagePrefix = If(owner Is Nothing, String.Empty, owner.Prefix & "_")

            ' The base name stays as typed, because it is also the caption - "Test Employee" reads
            ' as a page. The two page names are class names, so they take the identifier form of it.
            Dim identifier = PageGenerator.ToPageIdentifier(normalizedBaseName)

            pageBaseNameTextBox.Text = normalizedBaseName
            browsePageNameTextBox.Text = pagePrefix & identifier & "_B"
            maintenancePageNameTextBox.Text = pagePrefix & identifier & "_U"
        End Sub

        ''' <summary>
        ''' The owner chosen, or nothing while the picker still says "Make a Selection".
        ''' </summary>
        Private Function SelectedOwner() As PageOwners.Owner
            Return TryCast(ownerComboBox.SelectedItem, PageOwners.Owner)
        End Function

        ''' <summary>
        ''' Red while no owner is chosen, and gone once one is - the same signal every other
        ''' required field on a maintenance page gives.
        ''' </summary>
        Private Sub RefreshOwnerRequiredBorder()
            If ownerBorderPanel Is Nothing Then Return

            Dim settled = If(ownerBorderPanel.Parent Is Nothing, SystemColors.Control, ownerBorderPanel.Parent.BackColor)
            ownerBorderPanel.BackColor = If(SelectedOwner() Is Nothing, Color.Red, settled)
        End Sub

        ''' <summary>
        ''' Fills the picker from the folders at the repository root, so adding an application is
        ''' adding a folder and nothing else. Nothing here lists the owners, and nothing here can
        ''' therefore fall behind them.
        ''' </summary>
        Private Sub PopulateOwnerCombo()
            ownerComboBox.Items.Clear()
            ownerComboBox.Items.Add(PageOwners.NoSelection)

            Dim owners = PageOwners.All(Environment.CurrentDirectory)
            For Each pageOwner In owners
                ownerComboBox.Items.Add(pageOwner)
            Next

            ' No owners means the application is not running from the repository - launched from
            ' bin, or served from a deploy folder - and generation could not write a file anywhere
            ' useful either. Said here, where somebody is about to choose, rather than leaving an
            ' empty list to be puzzled over.
            If owners.Count = 0 Then
                ownerComboBox.Items.Add("No owners found - run from the repository")
                ownerComboBox.Enabled = False
            Else
                ownerComboBox.Enabled = True
            End If

            ownerComboBox.SelectedIndex = 0
        End Sub

        ''' <summary>
        ''' Puts the picker on a stored owner, or back to "Make a Selection" when the request has
        ''' none - or names a folder that no longer exists, which is the same thing to somebody
        ''' about to generate.
        ''' </summary>
        Private Sub SelectOwnerByFolder(folderName As String)
            If Not String.IsNullOrWhiteSpace(folderName) Then
                For index = 0 To ownerComboBox.Items.Count - 1
                    Dim owner = TryCast(ownerComboBox.Items(index), PageOwners.Owner)
                    If owner IsNot Nothing AndAlso String.Equals(owner.FolderName, folderName.Trim(), StringComparison.OrdinalIgnoreCase) Then
                        ownerComboBox.SelectedIndex = index
                        Return
                    End If
                Next
            End If

            ownerComboBox.SelectedIndex = 0
        End Sub

        Private Sub OwnerComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            RefreshOwnerRequiredBorder()

            If pageBaseNameTextBox Is Nothing Then
                Return
            End If

            Dim baseName = pageBaseNameTextBox.Text.Trim()
            If baseName <> String.Empty Then
                ApplyGeneratedPageNames(baseName)
            End If
        End Sub

        ''' <summary>
        ''' A base name with any owner's prefix taken off, so switching owner renames the pages
        ''' rather than stacking prefixes: FW_Widget picked as CTY becomes CTY_Widget, not
        ''' CTY_FW_Widget.
        ''' </summary>
        Private Function RemoveOwnerPrefix(value As String) As String
            Dim result = If(value, String.Empty).Trim()

            For Each pageOwner In PageOwners.All(Environment.CurrentDirectory)
                Dim prefix = pageOwner.Prefix & "_"
                If result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return result.Substring(prefix.Length)
                End If
            Next

            Return result
        End Function

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
                generatedPageIdTextBox.Text = String.Empty
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
                generatedPageIdTextBox.Text = DbText(row("GeneratedPageID"))
                requestNameTextBox.Text = DbText(row("RequestName"))
                pageBaseNameTextBox.Text = DbText(row("PageBaseName"))
                ' The owner the request was saved with. A request from before owners existed, or one
                ' naming a folder that has since gone, reopens on "Make a Selection" rather than
                ' on a guess.
                If row.Table.Columns.Contains("Owner") AndAlso Not row.IsNull("Owner") Then
                    SelectOwnerByFolder(Convert.ToString(row("Owner")))
                Else
                    SelectOwnerByFolder(String.Empty)
                End If
                RefreshOwnerRequiredBorder()
                browsePageNameTextBox.Text = DbText(row("BrowsePageName"))
                maintenancePageNameTextBox.Text = DbText(row("MaintenancePageName"))
                underlyingTableNameTextBox.Text = DbText(row("UnderlyingTableName"))
                tableAliasTextBox.Text = If(row.Table.Columns.Contains("TableAlias"), DbText(row("TableAlias")), String.Empty)
                useRegistrationIdCheckBox.Checked = Convert.ToBoolean(row("UseRegistrationID"))
                UpdateRegistrationOptionState()
                useQbeOnlyCheckBox.Checked = generateBrowsePageCheckBox.Checked AndAlso
                                             If(row.Table.Columns.Contains("UseQbeOnly") AndAlso Not row.IsNull("UseQbeOnly"), Convert.ToBoolean(row("UseQbeOnly")), False)
                browseFieldsTextBox.Text = DbText(row("BrowseFields"))
                ' Guarded like UseQbeOnly and IconFileName above: a request read before sql/083 has
                ' been applied has no such column, and a page generator that will not open is a
                ' worse outcome than a tick list that starts empty.
                hotFieldsTextBox.Text = If(row.Table.Columns.Contains("HotFields"), DbText(row("HotFields")), String.Empty)
                maintenanceFieldsTextBox.Text = DbText(row("MaintenanceFields"))
                browseSqlTextBox.Text = DbText(row("BrowseSql"))
                column2Fields = If(row.Table.Columns.Contains("Column2Fields"), DbText(row("Column2Fields")), String.Empty)
                lookupSpecs = DbText(row("LookupFields"))
                lookupFieldsTextBox.Text = LookupFieldNames(lookupSpecs)
                LoadLookupTargetsFromText(lookupSpecs)
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
            generatedPageIdTextBox.ReadOnly = True
            createdByTextBox.ReadOnly = True
            createdOnTextBox.ReadOnly = True
            updatedByTextBox.ReadOnly = True
            updatedOnTextBox.ReadOnly = True
            deletedFlagCheckBox.Enabled = False
        End Sub

        Protected Overrides Function TryBuildRecord() As Boolean
            Return True
        End Function

        ''' <summary>
        ''' lookupFieldsTextBox is deliberately absent from this list. Every other box shows its
        ''' column verbatim, but this one shows the field names parsed out of the stored sentences -
        ''' bound, the binding would write the sentences straight back over them.
        ''' </summary>
        Private Sub BindFormControls()
            For Each control In New Control() {requestNameTextBox, pageBaseNameTextBox, browsePageNameTextBox, maintenancePageNameTextBox, underlyingTableNameTextBox, browseFieldsTextBox, maintenanceFieldsTextBox, adminRequiredFieldsTextBox}
                Dim fieldName = control.Name.Substring("TextBox_".Length)
                control.DataBindings.Clear()
                control.DataBindings.Add("Text", formBindingSource, fieldName, True, DataSourceUpdateMode.Never)
            Next
            generateBrowsePageCheckBox.DataBindings.Clear()
            generateMaintenancePageCheckBox.DataBindings.Clear()
            generateBrowsePageCheckBox.DataBindings.Add("Checked", formBindingSource, "GenerateBrowsePage", True, DataSourceUpdateMode.Never)
            generateMaintenancePageCheckBox.DataBindings.Add("Checked", formBindingSource, "GenerateMaintenancePage", True, DataSourceUpdateMode.Never)
            ' The owner picker is not data-bound: its items are objects read from the folders, and
                ' a binding would have to match one of them to a stored string anyway. LoadRequest
                ' selects it by folder name instead.
            Dim pageGenerationTable = TryCast(formBindingSource.DataSource, DataTable)
            useQbeOnlyCheckBox.DataBindings.Clear()
            If pageGenerationTable IsNot Nothing AndAlso pageGenerationTable.Columns.Contains("UseQbeOnly") Then
                useQbeOnlyCheckBox.DataBindings.Add("Checked", formBindingSource, "UseQbeOnly", True, DataSourceUpdateMode.Never)
            End If

            If pageGenerationTable IsNot Nothing AndAlso pageGenerationTable.Columns.Contains("UseHotFields") Then
                displayHotFieldsCheckBox.DataBindings.Add("Checked", formBindingSource, "UseHotFields", True, DataSourceUpdateMode.Never)
            End If
            useRegistrationIdCheckBox.DataBindings.Clear()
            useRegistrationIdCheckBox.DataBindings.Add("Checked", formBindingSource, "UseRegistrationID", True, DataSourceUpdateMode.Never)
            browseSqlTextBox.DataBindings.Clear()
            browseSqlTextBox.DataBindings.Add("Text", formBindingSource, "BrowseSql", True, DataSourceUpdateMode.Never)
        End Sub

        Private Sub BrowseSqlTextBox_TextChanged(sender As Object, e As EventArgs)
            validatedBrowseSql = String.Empty
        End Sub

        ''' <summary>
        ''' Generate owns the two options beside it: both describe a browse page that is going to
        ''' exist, so neither can be set for one that is not.
        ''' </summary>
        Private Sub GenerateBrowsePageCheckBox_CheckedChanged(sender As Object, e As EventArgs)
            If Not generateBrowsePageCheckBox.Checked Then
                useQbeOnlyCheckBox.Checked = False
                displayHotFieldsCheckBox.Checked = False
            End If

            MarkDirty(sender, e)
            RefreshSavedPageDocumentTemplate()
        End Sub

        ''' <summary>
        ''' Either option implies the page itself, so choosing one turns Generate on.
        ''' </summary>
        ''' <remarks>
        ''' The two rules cannot chase each other. Turning Generate off clears both, and a cleared
        ''' box does not turn it back on; turning one on sets Generate, which is already on by then
        ''' and so clears nothing.
        ''' </remarks>
        Private Sub BrowseOptionCheckBox_CheckedChanged(sender As Object, e As EventArgs)
            If useQbeOnlyCheckBox.Checked OrElse displayHotFieldsCheckBox.Checked Then
                generateBrowsePageCheckBox.Checked = True
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


        Private Sub SetIconFileName(fileName As String)
            iconFileNameTextBox.Text = IconPicker.IconChoiceDisplay(fileName)
            iconPreviewBox.Image = IconPicker.ResolveIconImage(fileName)
        End Sub

        ''' <summary>
        ''' Offers the shared picker and records what came back.
        '''
        ''' The dialog itself lives in IconPicker, because the dashboards open the same one. What
        ''' stays here is what only this page knows: that a new choice makes the request dirty and
        ''' the saved-page document worth rebuilding.
        ''' </summary>
        Private Sub SelectIconButton_Click(sender As Object, e As EventArgs)
            Dim chosen = IconPicker.Choose(Me, iconFileNameTextBox.Text)
            If String.IsNullOrWhiteSpace(chosen) Then Return
            If String.Equals(IconPicker.IconChoiceValue(iconFileNameTextBox.Text), chosen, StringComparison.OrdinalIgnoreCase) Then Return

            SetIconFileName(chosen)
            MarkDirty(sender, e)
            RefreshSavedPageDocumentTemplate()
        End Sub

        Private Sub SelectFieldsButton_Click(sender As Object, e As EventArgs)
            Dim tableName = underlyingTableNameTextBox.Text.Trim()
            If tableName.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) Then
                tableName = tableName.Substring(4)
            End If

            ' RowVersion is never a field anybody picks. It is the concurrency token: the framework
            ' reads and writes it on every save, and a page that put it on screen would be showing a
            ' row of hex nobody can act on. Offering it only invited it to be ticked by mistake.
            Dim fields = DataAccess.GetTableFieldNames(tableName).
                Where(Function(field) Not String.Equals(field, "RowVersion", StringComparison.OrdinalIgnoreCase)).
                OrderBy(Function(field) field, StringComparer.OrdinalIgnoreCase).
                ToList()
            If fields.Count = 0 Then
                MessageBox.Show(Me, "CHOOSE A TABLE FIRST WITH SELECT TABLE.", "SELECT FIELDS", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            UpdateRegistrationOptionState(fields)

            Dim primaryKeyField = DataAccess.GetPrimaryKeyFieldName(tableName)
            Dim computedFields = DataAccess.GetComputedColumnNames(tableName)
            ' 314 wider than it was: 224 for the browse grid's Displays column and 150 for the
            ' maintenance grid's, less the 60 the Edit button column gave back. The left panel is
            ' fixed and the right takes what remains, so each change in width lands on the grid that
            ' gained or lost the column.
            '
            ' 55 wider again on 2026-09-09 for the browse grid's HF column: the left panel went from
            ' 560 to 615 and the dialog from 1294 to 1349, the same delta, so the maintenance grid
            ' keeps the width it had rather than paying for a column it does not carry.
            Using dialog As New Form With {
                .Text = "Select _B and _U Fields",
                .StartPosition = FormStartPosition.CenterParent,
                .ClientSize = New Size(1349, 620),
                .MinimizeBox = False,
                .MaximizeBox = False
            }
                Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2, .Padding = New Padding(10)}
                layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 615))
                layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
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
                ' MinimumWidth so a Fill column cannot vanish. Adding Displays pushed the fixed
                ' columns past the panel width and the field name silently collapsed to a sliver -
                ' a grid of unlabelled checkboxes. Out of room it now scrolls, which is visible.
                browseGrid.Columns.Add(New DataGridViewTextBoxColumn With {
                    .Name = "FieldName",
                    .HeaderText = "Field",
                    .ReadOnly = True,
                    .MinimumWidth = 130,
                    .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
                })
                ' Whether the field appears in the Hot Fields panel, which is a different question
                ' from whether it appears in the grid - the panel exists to show what the grid does
                ' not. Beside the field name rather than at the end, so the two ticks that decide
                ' where a field is seen sit either side of the field they decide it for.
                browseGrid.Columns.Add(New DataGridViewCheckBoxColumn With {
                    .Name = "HotField",
                    .HeaderText = "HF",
                    .Width = 55
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

                ' What the field points at, read from the table's declared relationships. Hidden
                ' because it is certain and not a choice - the lookup table and its key come from
                ' the foreign key, and only the column to display is up to anyone.
                browseGrid.Columns.Add(New DataGridViewTextBoxColumn With {
                    .Name = "LookupTarget",
                    .Visible = False
                })

                ' The one choice: which column of the lookup table the grid shows in place of the
                ' identifier. Pre-filled with a suggestion, empty where the field points nowhere,
                ' and empty on purpose means show the identifier as before.
                browseGrid.Columns.Add(New DataGridViewComboBoxColumn With {
                    .Name = "Displays",
                    .HeaderText = "Displays",
                    .Width = 150
                })
                Dim savedBrowseFields = browseFieldsTextBox.Text.Trim()
                Dim savedHotFields = hotFieldsTextBox.Text.Trim()
                Dim orderByFields = ParseOrderByFields(browseSqlTextBox.Text)
                Dim orderByDirections = ParseOrderByDirections(browseSqlTextBox.Text)

                ' Read once for the table, not once per field: a foreign key lookup per row would be
                ' one query per column for information that arrives in a single answer.
                Dim relationships = DataAccess.GetColumnRelationships(tableName)
                Dim lookupColumnCache As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
                Dim lookupRegistrationCache As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
                Dim savedDisplayColumns = ParseBrowseDisplayColumns(browseSqlTextBox.Text)

                ' A lookup column is ordered by what it displays, so ORDER BY names FirstLast where
                ' the grid row is AssignedManagerID. Translated back here, otherwise reopening a
                ' request loses the Order By tick on every field that shows a lookup.
                For Each pair In savedDisplayColumns
                    If orderByFields.RemoveAll(Function(name) String.Equals(name, pair.Value, StringComparison.OrdinalIgnoreCase)) > 0 Then
                        orderByFields.Add(pair.Key)
                    End If

                    Dim direction As String = Nothing
                    If orderByDirections.TryGetValue(pair.Value, direction) Then
                        orderByDirections.Remove(pair.Value)
                        orderByDirections(pair.Key) = direction
                    End If
                Next

                ' Positional, so the HF value sits between the field name and Order By exactly as the
                ' columns were added. A column inserted above without a value inserted here would
                ' shift every value after it one cell to the left, silently.
                For Each field In fields
                    Dim rowIndex = browseGrid.Rows.Add(
                        If(savedBrowseFields = String.Empty, False, ContainsField(savedBrowseFields, field)),
                        field,
                        If(savedHotFields = String.Empty, False, ContainsField(savedHotFields, field)),
                        orderByFields.Any(Function(orderField) String.Equals(orderField, field, StringComparison.OrdinalIgnoreCase)),
                        If(orderByDirections.ContainsKey(field), orderByDirections(field), "ASC"))

                    ConfigureLookupDisplayCell(browseGrid.Rows(rowIndex), field, relationships, savedDisplayColumns, lookupColumnCache)
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
                    .MinimumWidth = 130,
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

                ' The same two columns the browse grid carries, filled from the same reading of the
                ' declared relationships. Where one exists the four questions have three answers
                ' already - the table and its key are certain, and only the column to show is a
                ' choice - so ticking Lookup asks nothing and this is where the choice is made.
                maintenanceGrid.Columns.Add(New DataGridViewTextBoxColumn With {
                    .Name = "LookupTarget",
                    .Visible = False
                })
                maintenanceGrid.Columns.Add(New DataGridViewComboBoxColumn With {
                    .Name = "Displays",
                    .HeaderText = "Displays",
                    .Width = 150
                })

                ' Which column of a two-column _U the field lands in. A cell rather than a marker
                ' row in the grid: a marker row has to be skipped by every loop that reads this
                ' grid - seeding, the Include, Required and Lookup handlers, the Displays
                ' mirroring, the save and the ordering - and the day one of them forgets, a
                ' phantom field is generated. A cell is read by the loops that care and ignored
                ' by the rest.
                '
                ' Choosing 2 does not move the row. The grid order is the tab order and is
                ' maintained by Move Up and Move Down; rows that jump under the cursor while the
                ' choice is being made fight those buttons and lose the reader's place. The
                ' second column is shown by tint instead.
                Dim columnChoice As New DataGridViewComboBoxColumn With {
                    .Name = "Column",
                    .HeaderText = "Column",
                    .Width = 70
                }
                columnChoice.Items.Add(Column1Choice)
                columnChoice.Items.Add(Column2Choice)
                maintenanceGrid.Columns.Add(columnChoice)

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
                                ' A field that is not on the page is not in a column of it.
                                row.Cells("Column").Value = Column1Choice
                                ApplyColumnTint(row)
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

                            ' Ticking Lookup asks where the values come from. The generator needs a
                            ' table, the column it saves and the column it shows, and cannot guess
                            ' any of them - so ticking used to record the field name alone and
                            ' generation then refused it as malformed. Untick and the answer goes.
                            If eventArgs.ColumnIndex = maintenanceGrid.Columns("Lookup").Index AndAlso Not seedingSelectionGrids Then
                                Dim fieldName = Convert.ToString(row.Cells("FieldName").Value)
                                If isLookup Then
                                    ' The declared relationship is the whole answer: the table and
                                    ' its key come from the foreign key and the column to show from
                                    ' the Displays cell. A field with no relationship cannot be a
                                    ' lookup at all, which is why its tick is disabled rather than
                                    ' refused here - declare the foreign key and it becomes one.
                                    Dim relationship As DataAccess.ColumnRelationship = Nothing
                                    Dim spec As String = String.Empty
                                    If relationships.TryGetValue(fieldName, relationship) Then
                                        spec = BuildLookupSpecFromRelationship(fieldName,
                                                                               relationship,
                                                                               Convert.ToString(row.Cells("Displays").Value),
                                                                               lookupRegistrationCache)
                                    End If

                                    If spec = String.Empty Then
                                        row.Cells("Lookup").Value = False
                                    Else
                                        lookupTargets(fieldName) = spec
                                        SyncDisplaysCellFromSpec(row, spec)
                                    End If
                                Else
                                    lookupTargets.Remove(fieldName)
                                End If
                            End If
                        ElseIf eventArgs.ColumnIndex = maintenanceGrid.Columns("Column").Index Then
                            ' Putting a field in a column is asking for it on the page, the same
                            ' way Required and Lookup are. Nothing else follows: the row stays
                            ' where it is and only its tint changes.
                            If String.Equals(Convert.ToString(row.Cells("Column").Value), Column2Choice, StringComparison.Ordinal) Then
                                row.Cells("Include").Value = True
                            End If
                            ApplyColumnTint(row)
                        ElseIf eventArgs.ColumnIndex = maintenanceGrid.Columns("Displays").Index AndAlso Not seedingSelectionGrids Then
                            ' Changing what a ticked lookup shows rewrites its spec in place. Left
                            ' untouched, the grid would show one column and the generated page
                            ' would build its combo on another.
                            Dim fieldName = Convert.ToString(row.Cells("FieldName").Value)
                            Dim relationship As DataAccess.ColumnRelationship = Nothing
                            If Convert.ToBoolean(row.Cells("Lookup").Value) AndAlso relationships.TryGetValue(fieldName, relationship) Then
                                Dim spec = BuildLookupSpecFromRelationship(fieldName,
                                                                           relationship,
                                                                           Convert.ToString(row.Cells("Displays").Value),
                                                                           lookupRegistrationCache)
                                If spec = String.Empty Then
                                    lookupTargets.Remove(fieldName)
                                Else
                                    lookupTargets(fieldName) = spec
                                End If
                            End If
                        End If
                        NormalizeSelectionGridOrder(maintenanceGrid)

                        ' Ticking a placeholder places it, which asks the same question moving it
                        ' does. After the reorder, for the same reason: it takes the column of
                        ' wherever it has just landed.
                        If eventArgs.ColumnIndex = maintenanceGrid.Columns("Include").Index AndAlso
                           Convert.ToBoolean(row.Cells("Include").Value) Then
                            ApplyPlaceholderColumnFromNeighbour(maintenanceGrid, Convert.ToString(row.Cells("FieldName").Value))
                        End If
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

                            IncludeComputedFieldSources(maintenanceGrid, fieldName)
                        ElseIf eventArgs.ColumnIndex = browseGrid.Columns("Displays").Index AndAlso Not seedingSelectionGrids Then
                            ' What a foreign key shows on the browse page is almost always what it
                            ' should show on the maintenance page - they are the same column of the
                            ' same table, read by the same person. Answering it twice invited them
                            ' to disagree, and the pair that disagreed silently was the browse grid
                            ' showing a name while the maintenance combo showed something else.
                            '
                            ' Only when the right-hand grid offers the same choice, and only when
                            ' this is the user's own edit rather than the seeding pass.
                            Dim chosenDisplay = Convert.ToString(row.Cells("Displays").Value)
                            If Not String.IsNullOrWhiteSpace(chosenDisplay) Then
                                For Each maintenanceRow As DataGridViewRow In maintenanceGrid.Rows
                                    If Not String.Equals(Convert.ToString(maintenanceRow.Cells("FieldName").Value), fieldName, StringComparison.OrdinalIgnoreCase) Then Continue For

                                    Dim displayCell = TryCast(maintenanceRow.Cells("Displays"), DataGridViewComboBoxCell)
                                    If displayCell IsNot Nothing AndAlso displayCell.Items.Contains(chosenDisplay) Then
                                        displayCell.Value = chosenDisplay

                                        ' A ticked lookup carries the column inside its spec, so the
                                        ' spec is rewritten too - otherwise the cell would say one
                                        ' thing and the generated page build another.
                                        Dim relationship As DataAccess.ColumnRelationship = Nothing
                                        If Convert.ToBoolean(maintenanceRow.Cells("Lookup").Value) AndAlso relationships.TryGetValue(fieldName, relationship) Then
                                            Dim mirroredSpec = BuildLookupSpecFromRelationship(fieldName, relationship, chosenDisplay, lookupRegistrationCache)
                                            If mirroredSpec <> String.Empty Then lookupTargets(fieldName) = mirroredSpec
                                        End If
                                    End If

                                    Exit For
                                Next
                            End If
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
                Dim specDisplayColumns = ParseSpecDisplayColumns(lookupTargets)

                ' Nothing is ticked on the user's behalf. A declared foreign key once seeded itself
                ' as a lookup, which also put the field on the page - Lookup implies Include - and
                ' a new request therefore opened with fields already chosen that nobody had chosen.
                ' What the page holds is the request's decision, not the schema's.
                seedingSelectionGrids = True
                For Each field In fields
                    Dim maintenanceRowIndex = maintenanceGrid.Rows.Add(
                        ContainsField(savedMaintenanceFields, field) OrElse
                            lookupTargets.ContainsKey(field) OrElse
                            ContainsField(adminRequiredFieldsTextBox.Text, field),
                        field,
                        ContainsField(adminRequiredFieldsTextBox.Text, field),
                        lookupTargets.ContainsKey(field))

                    Dim maintenanceRow = maintenanceGrid.Rows(maintenanceRowIndex)
                    maintenanceRow.Cells("Column").Value = If(ContainsField(column2Fields, field), Column2Choice, Column1Choice)
                    ApplyColumnTint(maintenanceRow)
                    ConfigureLookupDisplayCell(maintenanceRow, field, relationships, specDisplayColumns, lookupColumnCache)

                    ' Nothing to point at, so nothing to tick. A spec written before the foreign
                    ' keys were declared keeps its tick, so it can still be removed - it just
                    ' cannot be recreated, which is honest: the answer now comes from the schema.
                    If Not relationships.ContainsKey(field) AndAlso Not lookupTargets.ContainsKey(field) Then
                        maintenanceRow.Cells("Lookup").ReadOnly = True
                        maintenanceRow.Cells("Lookup").Style.BackColor = SystemColors.Control
                    End If

                    ' A computed column is the database's to fill. It cannot be written at all, and
                    ' on a new record it has no value until after the save - so requiring it would
                    ' block every save on a rule nobody could satisfy. Greyed the same way a Lookup
                    ' with nothing to point at is, and a tick carried by an older saved request is
                    ' cleared rather than honoured.
                    If computedFields.Contains(field) Then
                        DisableSelectionCell(maintenanceRow, "Required", "COMPUTED - it cannot be written, so it cannot be Required.")

                        ' A greyed cell says only that it cannot be ticked, never why. Both cells
                        ' carry the reason so it is found from whichever one is hovered - the field
                        ' name is the wider target and the likelier one.
                        maintenanceRow.Cells("FieldName").ToolTipText = "COMPUTED - the database fills this column."
                    End If
                Next

                ' The placeholder pool: vertical space, carried in MaintenanceFields as though it
                ' were a field. That is the whole of the mechanism - an entry in that list is
                ' ordered by Move Up and Move Down and placed by the Column cell, so a placeholder
                ' inherits both without either being taught what it is.
                '
                ' Unticked, they sort to the top of the unselected block: OrderSelectionGrid falls
                ' back to name order and "(" sorts ahead of every letter. That is where they are
                ' wanted - directly under the last chosen field, rather than at the foot of a long
                ' table.
                Const placeholderReason As String =
                    "Vertical space on the _U page. It names no column, so it cannot be Required, a Lookup, or display anything."
                For number = 1 To PageGenerator.PlaceholderPoolSize
                    For Each token In New String() {PageGenerator.BlankLinePlaceholder(number),
                                                    PageGenerator.DividerPlaceholder(number)}
                        Dim placeholderIndex = maintenanceGrid.Rows.Add(ContainsField(savedMaintenanceFields, token), token, False, False)
                        Dim placeholderRow = maintenanceGrid.Rows(placeholderIndex)
                        placeholderRow.Cells("Column").Value = If(ContainsField(column2Fields, token), Column2Choice, Column1Choice)
                        ApplyColumnTint(placeholderRow)

                        ' Not ConfigureLookupDisplayCell: there is no field to find a relationship
                        ' for, and the Displays cell is disabled rather than filled.
                        DisableSelectionCell(placeholderRow, "Required", placeholderReason)
                        DisableSelectionCell(placeholderRow, "Lookup", placeholderReason)
                        DisableSelectionCell(placeholderRow, "Displays", placeholderReason)
                        placeholderRow.Cells("FieldName").ToolTipText = placeholderReason
                    Next
                Next
                seedingSelectionGrids = False
                OrderSelectionGrid(maintenanceGrid, savedMaintenanceFields)
                layout.Controls.Add(CreateSelectionPanel("_U Maintenance Fields", maintenanceGrid, Nothing, offerColumnSuggestion:=True), 1, 0)

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
                        ' Past the placeholders. A page of blank lines and no fields is not a page.
                        If Not maintenanceGrid.Rows.Cast(Of DataGridViewRow)().
                               Any(Function(row) Convert.ToBoolean(row.Cells("Include").Value) AndAlso
                                                 Not PageGenerator.IsPlaceholderField(Convert.ToString(row.Cells("FieldName").Value))) Then
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
                    hotFieldsTextBox.Text = JoinCheckedGridFields(browseGrid, "HotField", requireInclude:=False)
                    maintenanceFieldsTextBox.Text = JoinIncludedGridFields(maintenanceGrid)
                    column2Fields = JoinColumnTwoFields(maintenanceGrid)
                    lookupSpecs = JoinLookupFields(maintenanceGrid)
                    lookupFieldsTextBox.Text = LookupFieldNames(lookupSpecs)
                    adminRequiredFieldsTextBox.Text = JoinCheckedGridFields(maintenanceGrid, "Required")
                    browseSqlTextBox.Text = BuildGeneratedBrowseSql(tableName, browseGrid, fields, primaryKeyField, orderByFields)

                    ' Explicitly, because the two answers that live outside a bound text box -
                    ' the lookup specs and the column map - can be the only thing that changed.
                    ' Save and Generate skips the save when the record is clean, so a page would
                    ' be generated from the stored request rather than from what is on screen.
                    MarkDirty(Me, EventArgs.Empty)
                    RefreshSavedPageDocumentTemplate()
                End If
            End Using

        End Sub

        ''' <summary>
        ''' The same checklist the admin dashboard and Roles_U open, not a third copy of it. Nothing
        ''' needs refreshing afterwards: Select Table reads the list when it is clicked.
        ''' </summary>
        Private Sub EnabledTablesButton_Click(sender As Object, e As EventArgs)
            Using tables As New FW_EnabledTables()
                tables.ShowDialog(Me)
            End Using
        End Sub

        Private Sub SelectTableButton_Click(sender As Object, e As EventArgs)
            Dim tables = DataAccess.GetDatabaseTables()
            If tables.Count = 0 Then
                MessageBox.Show(Me, "NO TABLES WERE FOUND.", "SELECT TABLE", MessageBoxButtons.OK, MessageBoxIcon.Information)
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

                ' The list reads upper-cased, which is easier to scan and keeps the dialog uniform,
                ' but the name carried away from here is the one the database uses. Storing what was
                ' displayed is how FW_USERS came to sit beside FW_Users in the audit trail, for the
                ' same table.
                Dim tableList As New ListBox With {.Dock = DockStyle.Fill}
                Dim actualNameByDisplay As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                For Each tableName In tables
                    Dim displayName = tableName.ToUpperInvariant()
                    actualNameByDisplay(displayName) = tableName
                    tableList.Items.Add(displayName)
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
                    Dim selectedDisplay = tableList.SelectedItem.ToString()
                    Dim selectedTable As String = Nothing
                    If Not actualNameByDisplay.TryGetValue(selectedDisplay, selectedTable) Then
                        selectedTable = selectedDisplay
                    End If
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
            hotFieldsTextBox.Text = String.Empty
            tableAliasTextBox.Text = String.Empty
            maintenanceFieldsTextBox.Text = String.Empty
            column2Fields = String.Empty
            lookupTargets.Clear()
            lookupSpecs = String.Empty
            lookupFieldsTextBox.Text = String.Empty
            adminRequiredFieldsTextBox.Text = String.Empty
            browseSqlTextBox.Text = String.Empty
            validatedBrowseSql = String.Empty
            okButton.Enabled = True
            RefreshSavedPageDocumentTemplate()
        End Sub

        ''' <param name="offerColumnSuggestion">
        ''' Whether to offer the two-column split. Only the _U grid has a Column cell to fill, and
        ''' a button that did nothing on the _B grid would read as one that was broken.
        ''' </param>
        Private Shared Function CreateSelectionPanel(caption As String,
                                                     grid As DataGridView,
                                                     Optional note As String = Nothing,
                                                     Optional offerColumnSuggestion As Boolean = False) As Control
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
            If offerColumnSuggestion Then
                Dim suggestButton As New Button With {.Text = "Suggest Columns", .AutoSize = True, .Margin = New Padding(18, 3, 3, 3)}
                AddHandler suggestButton.Click, Sub(sender As Object, e As EventArgs) SuggestColumns(grid)
                actions.Controls.Add(suggestButton)
            End If
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
            ' The rows are moved, not copied. Rebuilding them from their values discarded anything
            ' held on the cell rather than in it - a combo cell's own item list, or a cell swapped
            ' for a different type - and the value then landed in a fresh cell that did not accept
            ' it: "DataGridViewComboBoxCell value is not valid". Moving the row objects keeps
            ' whatever each cell was configured with.
            ' Unchosen placeholders sit past the unchosen fields. Left to the name sort they lead
            ' it - "(" sorts ahead of every letter - which puts the pool between the chosen fields
            ' and the alphabetical list of what is left, interrupting the reading of both.
            ' Included placeholders are untouched: the term is zero for every chosen row.
            Dim placeholdersLast = Function(included As Boolean, fieldName As String) _
                If(Not included AndAlso PageGenerator.IsPlaceholderField(fieldName), 1, 0)

            Dim rows = grid.Rows.Cast(Of DataGridViewRow)().
                Select(Function(row) New With {
                    .Row = row,
                    .FieldName = Convert.ToString(row.Cells("FieldName").Value),
                    .Included = Convert.ToBoolean(row.Cells("Include").Value)
                }).
                OrderByDescending(Function(item) item.Included).
                ThenBy(Function(item) If(item.Included AndAlso savedPositions.ContainsKey(item.FieldName), savedPositions(item.FieldName), Integer.MaxValue)).
                ThenBy(Function(item) placeholdersLast(item.Included, item.FieldName)).
                ThenBy(Function(item) item.FieldName, StringComparer.OrdinalIgnoreCase).
                Select(Function(item) item.Row).
                ToList()

            If rows.Count = 0 Then Return

            ' Removed and reinserted one at a time rather than cleared and re-added: clearing the
            ' collection disposes the rows, and a disposed row cannot go back in.
            For targetIndex = 0 To rows.Count - 1
                Dim row = rows(targetIndex)
                If row.Index = targetIndex Then Continue For

                grid.Rows.Remove(row)
                grid.Rows.Insert(targetIndex, row)
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

            ' After the reorder, never before: the column is read from where the row has landed.
            ApplyPlaceholderColumnFromNeighbour(grid, selectedField)

            grid.ClearSelection()
            For Each row As DataGridViewRow In grid.Rows
                If String.Equals(Convert.ToString(row.Cells("FieldName").Value), selectedField, StringComparison.OrdinalIgnoreCase) Then
                    row.Selected = True
                    grid.CurrentCell = row.Cells("FieldName")
                    Exit For
                End If
            Next
        End Sub

        ''' <summary>
        ''' A placeholder takes the column of the field it was dropped beside.
        '''
        ''' Grid order and the Column cell answer two different questions - where a row sits within
        ''' its column, and which column that is - and for a field the answer to the second is
        ''' deliberate. For a blank line it is not: nobody moves a divider under Termination Date
        ''' meaning to put it at the foot of the other column, which is exactly what happened on
        ''' 2026-09-15 and read as the feature being broken.
        '''
        ''' The cell stays editable, and an explicit answer holds until the row is moved again.
        ''' Moving it is the gesture that re-asks the question.
        ''' </summary>
        ''' <summary>
        ''' Ticking a computed field also ticks the fields it is made of.
        '''
        ''' FirstLast is read-only on a maintenance page, because the database fills it - so a page
        ''' that has it and nothing else shows a name with no way to change it. The columns behind
        ''' it come from the expression SQL Server stores, so this is knowledge rather than a guess.
        '''
        ''' Adds, never removes. Unticking the computed field later leaves FirstName and LastName
        ''' where they are, because by then they are fields in their own right - the same rule the
        ''' Required and Lookup ticks follow.
        ''' </summary>
        Private Sub IncludeComputedFieldSources(grid As DataGridView, fieldName As String)
            If String.IsNullOrWhiteSpace(fieldName) OrElse grid Is Nothing Then Return

            Dim tableName = underlyingTableNameTextBox.Text.Trim()
            If tableName.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) Then tableName = tableName.Substring(4)
            If tableName = String.Empty Then Return

            Dim sources As List(Of String) = Nothing
            If Not DataAccess.GetComputedColumnSources(tableName).TryGetValue(fieldName.Trim(), sources) Then Return
            If sources Is Nothing OrElse sources.Count = 0 Then Return

            For Each source In sources
                For Each maintenanceRow As DataGridViewRow In grid.Rows
                    If String.Equals(Convert.ToString(maintenanceRow.Cells("FieldName").Value), source, StringComparison.OrdinalIgnoreCase) Then
                        maintenanceRow.Cells("Include").Value = True
                        Exit For
                    End If
                Next
            Next
        End Sub

        Private Shared Sub ApplyPlaceholderColumnFromNeighbour(grid As DataGridView, fieldName As String)
            If grid Is Nothing OrElse Not grid.Columns.Contains("Column") Then Return
            If Not PageGenerator.IsPlaceholderField(fieldName) Then Return

            Dim included = grid.Rows.Cast(Of DataGridViewRow)().
                Where(Function(row) Convert.ToBoolean(row.Cells("Include").Value)).ToList()
            Dim position = included.FindIndex(Function(row) String.Equals(Convert.ToString(row.Cells("FieldName").Value),
                                                                         fieldName, StringComparison.OrdinalIgnoreCase))
            If position < 0 Then Return

            ' The field above it, or the field below when it has been moved to the very top. A run
            ' of placeholders is stepped over rather than read - one of them has no more answer to
            ' give than the row being placed.
            Dim column = NeighbourColumnChoice(included, position, -1)
            If column = String.Empty Then column = NeighbourColumnChoice(included, position, 1)
            If column = String.Empty Then Return
            If String.Equals(Convert.ToString(included(position).Cells("Column").Value), column, StringComparison.Ordinal) Then Return

            included(position).Cells("Column").Value = column
            ApplyColumnTint(included(position))
        End Sub

        ''' <summary>
        ''' The Column cell of the nearest real field in one direction, or empty when there is none.
        ''' </summary>
        Private Shared Function NeighbourColumnChoice(included As List(Of DataGridViewRow),
                                                      position As Integer,
                                                      direction As Integer) As String
            Dim index = position + direction
            While index >= 0 AndAlso index < included.Count
                Dim neighbourName = Convert.ToString(included(index).Cells("FieldName").Value)
                If Not PageGenerator.IsPlaceholderField(neighbourName) Then
                    Return Convert.ToString(included(index).Cells("Column").Value)
                End If
                index += direction
            End While
            Return String.Empty
        End Function

        Private Shared Function ContainsField(value As String, field As String) As Boolean
            Return value.Split({",", ";"}, StringSplitOptions.RemoveEmptyEntries).Any(Function(item) String.Equals(item.Trim(), field, StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function JoinCheckedItems(list As CheckedListBox) As String
            Return String.Join(", ", list.CheckedItems.Cast(Of Object)().Select(Function(item) item.ToString()))
        End Function

        ''' <summary>
        ''' The fields with the named column ticked.
        ''' </summary>
        ''' <param name="requireInclude">
        ''' Whether the field must also be in the page. True for Order By and Admin Required, which
        ''' describe a column the page already selects - you cannot sort by what is not there.
        '''
        ''' False for Hot Fields, which is the one answer that means the opposite: the panel exists
        ''' to show what the grid does not, so requiring Include collected nothing at all for a field
        ''' ticked HF and not _B. That is what it did on 2026-09-09, silently, because a helper was
        ''' reused without noticing the precondition it carried.
        ''' </param>
        Private Shared Function JoinCheckedGridFields(grid As DataGridView,
                                                       columnName As String,
                                                       Optional requireInclude As Boolean = True) As String
            Dim selectedFields As New List(Of String)()
            For Each row As DataGridViewRow In grid.Rows
                If requireInclude AndAlso Not Convert.ToBoolean(row.Cells("Include").Value) Then
                    Continue For
                End If

                If Convert.ToBoolean(row.Cells(columnName).Value) Then
                    selectedFields.Add(Convert.ToString(row.Cells("FieldName").Value))
                End If
            Next
            Return String.Join(", ", selectedFields)
        End Function

        ''' <summary>
        ''' Where each ticked lookup field gets its values, keyed by field name and holding the
        ''' whole "Field -> Table.Value displayed as Display" sentence the generator parses.
        ''' </summary>
        Private ReadOnly lookupTargets As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' The lookup specifications as stored: one sentence per lookup, carrying the table, the
        ''' column saved, the column shown and the scope. This is what goes to the database and what
        ''' the generator parses.
        '''
        ''' The box on the page shows only the field names, because that is the part worth reading -
        ''' the sentences are for the generator, and four of them make an unreadable line.
        ''' </summary>
        Private lookupSpecs As String = String.Empty

        ''' <summary>
        ''' The _U fields that sit in the second column, as stored. Column one is absence: a field
        ''' not named here is in column one, so a request that has never answered the question
        ''' generates the single-column page it always did.
        '''
        ''' No order is kept here. MaintenanceFields already carries the order Move Up and Move
        ''' Down arranged, and the generated page reads that one list twice - the fields not named
        ''' here down the left, the fields named here down the right, each in that same order. Two
        ''' orders that can disagree is the thing this avoids.
        ''' </summary>
        Private column2Fields As String = String.Empty

        ''' <summary>The field names from a set of lookup specs, in the order they were written.</summary>
        Private Shared Function LookupFieldNames(specs As String) As String
            If String.IsNullOrWhiteSpace(specs) Then Return String.Empty

            Dim names As New List(Of String)()
            For Each entry In specs.Split(","c)
                Dim spec = entry.Trim()
                If spec = String.Empty Then Continue For

                Dim arrow = spec.IndexOf("->", StringComparison.Ordinal)
                Dim fieldName = If(arrow > 0, spec.Substring(0, arrow).Trim(), spec)
                If fieldName <> String.Empty Then names.Add(fieldName)
            Next

            Return String.Join(", ", names)
        End Function

        ''' <summary>
        ''' True while the selection grids are being filled from a saved request. Seeding a ticked
        ''' Lookup cell raises CellValueChanged exactly as a click does, and without this the picker
        ''' would open for every lookup the request already has, the moment Select Fields is opened.
        ''' </summary>
        Private seedingSelectionGrids As Boolean

        ''' <summary>
        ''' The lookup specs to save, rebuilt from the declared relationship rather than replayed
        ''' from the sentence they were loaded with.
        '''
        ''' A stored spec names a table and a key, and a key can be renamed after it is written.
        ''' FW_Gender.ID became GenderID and every saved spec still said ID, which the generator
        ''' then emitted into a maintenance page that threw on load. Re-saving could not correct it
        ''' because the sentence was only ever loaded and written back; the schema was consulted
        ''' just once, when the Lookup box was first ticked.
        '''
        ''' LookupTarget is refreshed from the foreign keys every time this dialog is built, and is
        ''' already what the browse SQL reads. Reading it here gives both sides one owner, so they
        ''' cannot describe the same relationship differently.
        '''
        ''' A lookup with no declared relationship keeps the spec it was given. It cannot be
        ''' rebuilt from a schema that never described it, and dropping it would delete the answer
        ''' rather than preserve it.
        ''' </summary>
        Private Function JoinLookupFields(grid As DataGridView) As String
            Dim specs As New List(Of String)()
            Dim registrationCache As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

            For Each row As DataGridViewRow In grid.Rows
                If Not Convert.ToBoolean(row.Cells("Include").Value) Then Continue For
                If Not Convert.ToBoolean(row.Cells("Lookup").Value) Then Continue For

                Dim fieldName = Convert.ToString(row.Cells("FieldName").Value)
                If String.IsNullOrWhiteSpace(fieldName) Then Continue For

                Dim rebuilt = BuildLookupSpecFromTarget(grid, row, fieldName, registrationCache)
                If Not String.IsNullOrWhiteSpace(rebuilt) Then
                    specs.Add(rebuilt)
                    Continue For
                End If

                Dim spec As String = Nothing
                If lookupTargets.TryGetValue(fieldName, spec) AndAlso Not String.IsNullOrWhiteSpace(spec) Then
                    specs.Add(spec)
                End If
            Next
            Return String.Join(", ", specs)
        End Function

        ''' <summary>
        ''' The spec a row's own relationship describes, or empty where the row points nowhere.
        ''' </summary>
        Private Shared Function BuildLookupSpecFromTarget(grid As DataGridView,
                                                          row As DataGridViewRow,
                                                          fieldName As String,
                                                          registrationCache As Dictionary(Of String, Boolean)) As String
            If grid Is Nothing OrElse Not grid.Columns.Contains("LookupTarget") Then Return String.Empty

            Dim target = Convert.ToString(row.Cells("LookupTarget").Value)
            If String.IsNullOrWhiteSpace(target) Then Return String.Empty

            Dim pieces = target.Split("."c)
            If pieces.Length <> 2 Then Return String.Empty

            Return BuildLookupSpecFromRelationship(fieldName,
                                                   New DataAccess.ColumnRelationship With {
                                                       .LookupTable = pieces(0),
                                                       .KeyColumn = pieces(1)
                                                   },
                                                   Convert.ToString(row.Cells("Displays").Value),
                                                   registrationCache)
        End Function

        ''' <summary>The display column each saved lookup spec names, keyed by its field.</summary>
        Private Shared Function ParseSpecDisplayColumns(specs As Dictionary(Of String, String)) As Dictionary(Of String, String)
            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If specs Is Nothing Then Return result

            For Each pair In specs
                Dim display = ParseSpecDisplayColumn(pair.Value)
                If Not String.IsNullOrWhiteSpace(display) Then result(pair.Key) = display
            Next
            Return result
        End Function

        Private Shared Function ParseSpecDisplayColumn(spec As String) As String
            If String.IsNullOrWhiteSpace(spec) Then Return String.Empty

            Dim match = Regex.Match(spec, "(?i)\bdisplayed\s+as\s+(?<display>\w+)")
            Return If(match.Success, match.Groups("display").Value, String.Empty)
        End Function

        ''' <summary>
        ''' Builds the spec sentence the generator parses, from a relationship rather than from four
        ''' answers. The table and its key come from the foreign key, the display column from the
        ''' grid, and the scope from whether rows actually populate a RegistrationID - the same
        ''' test the picker's checkbox makes, so both routes decide it the same way.
        ''' </summary>
        Private Shared Function BuildLookupSpecFromRelationship(fieldName As String,
                                                                relationship As DataAccess.ColumnRelationship,
                                                                displayColumn As String,
                                                                registrationCache As Dictionary(Of String, Boolean)) As String
            If relationship Is Nothing OrElse String.IsNullOrWhiteSpace(displayColumn) Then Return String.Empty

            Dim filterByRegistration As Boolean
            If registrationCache Is Nothing OrElse Not registrationCache.TryGetValue(relationship.LookupTable, filterByRegistration) Then
                filterByRegistration = DataAccess.CountLookupRegistrations(relationship.LookupTable) > 0
                If registrationCache IsNot Nothing Then registrationCache(relationship.LookupTable) = filterByRegistration
            End If

            Return fieldName & " -> " & relationship.LookupTable & "." & relationship.KeyColumn &
                   " displayed as " & displayColumn.Trim() &
                   If(filterByRegistration, " filtered by registration", " not filtered by registration")
        End Function

        ''' <summary>
        ''' Shows in the Displays cell what a spec says it displays, so an answer given through the
        ''' picker and one chosen from the dropdown cannot disagree on screen. A picker answer can
        ''' name a table the dropdown never offered, so its column is added rather than dropped.
        ''' </summary>
        Private Shared Sub SyncDisplaysCellFromSpec(row As DataGridViewRow, spec As String)
            Dim display = ParseSpecDisplayColumn(spec)
            If String.IsNullOrWhiteSpace(display) Then Return

            Dim cell = TryCast(row.Cells("Displays"), DataGridViewComboBoxCell)
            If cell Is Nothing Then
                row.Cells("Displays").Value = display
                Return
            End If

            If Not cell.Items.Contains(display) Then cell.Items.Add(display)
            cell.Value = display
        End Sub

        ''' <summary>
        ''' The columns of a lookup table, read once per table however many rows and grids ask.
        ''' Both selection grids offer the same choices, and a query per row for an answer that
        ''' cannot differ between them is a query wasted.
        ''' </summary>
        Private Shared Function LookupTableColumns(tableName As String,
                                                   columnCache As Dictionary(Of String, List(Of String))) As List(Of String)
            Dim columns As List(Of String) = Nothing
            If columnCache IsNot Nothing AndAlso columnCache.TryGetValue(tableName, columns) Then Return columns

            columns = DataAccess.GetTableColumnList(tableName)
            If columnCache IsNot Nothing Then columnCache(tableName) = columns
            Return columns
        End Function

        ''' <summary>
        ''' Fills in a row's relationship: the hidden target, and the dropdown of columns that could
        ''' be shown in place of the identifier. Used by both grids - the browse grid shows the
        ''' choice in a column, the maintenance grid shows it in a combo box - because the
        ''' relationship being offered is the same relationship.
        '''
        ''' A field pointing nowhere gets an empty, read-only cell rather than an empty dropdown -
        ''' offering a choice that cannot be made is worse than offering none. It still shows a
        ''' display column if one was answered by hand, since a lookup can be declared where no
        ''' foreign key is.
        ''' </summary>
        Private Shared Sub ConfigureLookupDisplayCell(row As DataGridViewRow,
                                                     fieldName As String,
                                                     relationships As Dictionary(Of String, DataAccess.ColumnRelationship),
                                                     savedDisplayColumns As Dictionary(Of String, String),
                                                     columnCache As Dictionary(Of String, List(Of String)))
            Dim savedDisplay As String = Nothing
            If savedDisplayColumns IsNot Nothing Then savedDisplayColumns.TryGetValue(fieldName, savedDisplay)

            Dim relationship As DataAccess.ColumnRelationship = Nothing
            If relationships Is Nothing OrElse Not relationships.TryGetValue(fieldName, relationship) Then
                Dim blank As New DataGridViewTextBoxCell()
                row.Cells("Displays") = blank
                blank.ReadOnly = True
                blank.Style.BackColor = SystemColors.Control
                If Not String.IsNullOrWhiteSpace(savedDisplay) Then blank.Value = savedDisplay
                Return
            End If

            row.Cells("LookupTarget").Value = relationship.LookupTable & "." & relationship.KeyColumn

            Dim choices = LookupTableColumns(relationship.LookupTable, columnCache)
            Dim cell = TryCast(row.Cells("Displays"), DataGridViewComboBoxCell)
            If cell Is Nothing Then Return

            cell.Items.Clear()
            ' Blank is a real answer: show the identifier, as the grid did before this existed.
            cell.Items.Add(String.Empty)
            For Each column In choices
                cell.Items.Add(column)
            Next

            ' What was already chosen beats the suggestion: a request reopens with the answer it
            ' was given, not with the guess it started from.
            Dim chosen = savedDisplay
            If String.IsNullOrWhiteSpace(chosen) Then
                chosen = DataAccess.SuggestDisplayColumn(relationship.LookupTable)
            End If

            cell.Value = If(Not String.IsNullOrWhiteSpace(chosen) AndAlso cell.Items.Contains(chosen),
                            chosen,
                            String.Empty)
            cell.ToolTipText = "Shows a column of " & relationship.LookupTable &
                               " instead of the identifier. Blank shows " & fieldName & " itself."
        End Sub

        ''' <summary>
        ''' Rebuilds the lookup targets from a saved request, so reopening one and touching the grid
        ''' does not quietly discard the lookups it already had.
        ''' </summary>
        Private Sub LoadLookupTargetsFromText(storedValue As String)
            lookupTargets.Clear()
            If String.IsNullOrWhiteSpace(storedValue) Then Return

            For Each entry In storedValue.Split(","c)
                Dim spec = entry.Trim()
                If spec = String.Empty Then Continue For

                Dim arrow = spec.IndexOf("->", StringComparison.Ordinal)
                Dim fieldName = If(arrow > 0, spec.Substring(0, arrow).Trim(), spec)
                If fieldName <> String.Empty Then lookupTargets(fieldName) = spec
            Next
        End Sub

        ''' <summary>
        ''' Below this many fields a page stays in one column. A generated _U is 55 pixels of
        ''' furniture plus 42 a row, so nine rows is a 433-pixel box - splitting that produces a
        ''' wide, half-empty form, which is worse than a tall narrow one. Past it the single column
        ''' starts running off the screen.
        ''' </summary>
        Private Const TwoColumnThreshold As Integer = 10

        ''' <summary>
        ''' Fills the Column cells with a guess, for the fields currently included.
        '''
        ''' A button rather than something that happens on its own. The guess is only meaningful
        ''' once the fields are ticked, and one that re-applied itself on every reopen would
        ''' overrule a page deliberately left in one column - with nothing on screen to say it had.
        '''
        ''' The order is not touched. The split is a cut through the list the Move buttons
        ''' arranged, never a re-sort of it: everything before the cut is column one, everything
        ''' after it column two.
        ''' </summary>
        Private Shared Sub SuggestColumns(grid As DataGridView)
            Dim includedRows = grid.Rows.Cast(Of DataGridViewRow)().
                Where(Function(row) Convert.ToBoolean(row.Cells("Include").Value)).
                ToList()
            Dim includedFields = includedRows.Select(Function(row) Convert.ToString(row.Cells("FieldName").Value)).ToList()
            Dim splitAt = SuggestedColumnSplit(includedFields)

            For position = 0 To includedRows.Count - 1
                includedRows(position).Cells("Column").Value = If(splitAt > 0 AndAlso position >= splitAt, Column2Choice, Column1Choice)
                ApplyColumnTint(includedRows(position))
            Next

            ' Anything not on the page is not in a column of it, whatever it was left holding.
            For Each row As DataGridViewRow In grid.Rows
                If Convert.ToBoolean(row.Cells("Include").Value) Then Continue For
                row.Cells("Column").Value = Column1Choice
                ApplyColumnTint(row)
            Next
        End Sub

        ''' <summary>
        ''' Where to cut the included fields into two columns, or zero for one column.
        '''
        ''' The midpoint, rounded up, so the left column is the same height as the right or one
        ''' row taller - a left column shorter than the right reads as a page that stopped early.
        '''
        ''' The address block is never cut through. Smarty fills City, State and Zip from the
        ''' street line and the Zip Coder button sits against Zip, so those four belong together;
        ''' where the midpoint lands inside them the cut moves to whichever end of the block is
        ''' nearer, and if that would empty a column the page stays in one.
        ''' </summary>
        Private Shared Function SuggestedColumnSplit(includedFields As List(Of String)) As Integer
            If includedFields.Count < TwoColumnThreshold Then Return 0

            Dim midpoint = CInt(Math.Ceiling(includedFields.Count / 2.0))
            Dim addressGroup = PageGenerator.AddressGroupFields(includedFields)
            If addressGroup.Count > 0 Then
                Dim positions = addressGroup.
                    Select(Function(field) includedFields.FindIndex(Function(item) String.Equals(item, field, StringComparison.OrdinalIgnoreCase))).
                    Where(Function(index) index >= 0).
                    ToList()
                If positions.Count > 0 Then
                    Dim firstAddress = positions.Min()
                    Dim pastLastAddress = positions.Max() + 1
                    If midpoint > firstAddress AndAlso midpoint < pastLastAddress Then
                        midpoint = If(midpoint - firstAddress <= pastLastAddress - midpoint, firstAddress, pastLastAddress)
                    End If
                End If
            End If

            ' A cut at either end is not a split.
            If midpoint <= 0 OrElse midpoint >= includedFields.Count Then Return 0
            Return midpoint
        End Function

        ''' <summary>The two answers the Column cell offers. Text, because the cell is a combo.</summary>
        Private Const Column1Choice As String = "1"
        Private Const Column2Choice As String = "2"

        ''' The wash behind a second-column row. Pale enough to read through, strong enough to
        ''' group - the row says which side it is on without being moved to that side.
        Private Shared ReadOnly Column2RowBackColor As Color = Color.FromArgb(238, 244, 250)

        ''' <summary>
        ''' A cell whose question this row cannot answer: unticked, read-only, greyed and carrying
        ''' the reason. One helper rather than a copy per case - a greyed cell says only that it
        ''' cannot be ticked, never why, and two copies of that rule is how one of them ends up
        ''' without the tooltip.
        ''' </summary>
        Private Shared Sub DisableSelectionCell(row As DataGridViewRow, columnName As String, reason As String)
            If row.DataGridView Is Nothing OrElse Not row.DataGridView.Columns.Contains(columnName) Then Return

            Dim cell = row.Cells(columnName)
            ' Only a tick can be cleared. A combo cell holds no items for a row like this, and
            ' writing False into one raises "value is not valid".
            If TypeOf cell Is DataGridViewCheckBoxCell Then cell.Value = False
            cell.ReadOnly = True
            cell.Style.BackColor = SystemColors.Control
            cell.ToolTipText = reason
        End Sub

        Private Shared Sub ApplyColumnTint(row As DataGridViewRow)
            If row.DataGridView Is Nothing OrElse Not row.DataGridView.Columns.Contains("Column") Then Return

            Dim inColumnTwo = String.Equals(Convert.ToString(row.Cells("Column").Value), Column2Choice, StringComparison.Ordinal)
            row.DefaultCellStyle.BackColor = If(inColumnTwo, Column2RowBackColor, Color.Empty)
        End Sub

        ''' <summary>
        ''' The included fields marked column two, in grid order. Grid order is the whole of the
        ''' ordering answer: column one is this list's complement, read in the same order.
        ''' </summary>
        Private Shared Function JoinColumnTwoFields(grid As DataGridView) As String
            Dim selectedFields As New List(Of String)()
            For Each row As DataGridViewRow In grid.Rows
                If Not Convert.ToBoolean(row.Cells("Include").Value) Then Continue For
                If String.Equals(Convert.ToString(row.Cells("Column").Value), Column2Choice, StringComparison.Ordinal) Then
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

        ''' <summary>
        ''' One ORDER BY term: the column, with the table alias in front of it discarded. Written
        ''' once because two parsers read the same clause and reading it two ways would mean an
        ''' ORDER BY that restores its field in one place and the alias "E" in the other.
        ''' </summary>
        Private Const OrderByFieldPattern As String =
            "^(?:\[?[A-Za-z_][\w]*\]?\s*\.\s*)?\[?(?<field>[A-Za-z_][\w]*)\]?"

        Private Shared Function ParseOrderByFields(sql As String) As List(Of String)
            Dim result As New List(Of String)()
            Dim match = Regex.Match(sql, "(?is)ORDER\s+BY\s+(?<fields>.+?)\s*$")
            If Not match.Success Then Return result

            For Each part In match.Groups("fields").Value.Split(","c)
                Dim fieldMatch = Regex.Match(part.Trim(), OrderByFieldPattern)
                If fieldMatch.Success Then result.Add(fieldMatch.Groups("field").Value)
            Next
            Return result
        End Function

        Private Shared Function ParseOrderByDirections(sql As String) As Dictionary(Of String, String)
            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim match = Regex.Match(sql, "(?is)ORDER\s+BY\s+(?<fields>.+?)\s*$")
            If Not match.Success Then Return result

            For Each part In match.Groups("fields").Value.Split(","c)
                Dim fieldMatch = Regex.Match(part.Trim(), OrderByFieldPattern & "\s*(?<direction>ASC|DESC)?")
                If fieldMatch.Success Then
                    result(fieldMatch.Groups("field").Value) = If(String.Equals(fieldMatch.Groups("direction").Value, "DESC", StringComparison.OrdinalIgnoreCase), "DESC", "ASC")
                End If
            Next
            Return result
        End Function

        ''' <summary>
        ''' Reads the display columns back out of a saved request's SQL, so reopening one shows the
        ''' column that was chosen rather than the suggestion. The SQL is where the choice already
        ''' lives - Order By and its direction are recovered the same way - so nothing has to be
        ''' stored twice and the two can never disagree.
        '''
        ''' Only aliases that a LEFT JOIN introduced count, which is what keeps the primary key's
        ''' own AS PK out of the result.
        ''' </summary>
        Private Shared Function ParseBrowseDisplayColumns(sql As String) As Dictionary(Of String, String)
            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If String.IsNullOrWhiteSpace(sql) Then Return result

            Dim joinAliases As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each joinMatch As Match In Regex.Matches(sql,
                "(?i)\bLEFT\s+JOIN\s+(?:dbo\s*\.\s*)?\[?[\w]+\]?\s+(?:AS\s+)?(?<alias>[A-Za-z_][\w]*)\s+ON\b")
                joinAliases.Add(joinMatch.Groups("alias").Value)
            Next
            If joinAliases.Count = 0 Then Return result

            For Each selectMatch As Match In Regex.Matches(sql,
                "(?i)(?<alias>[A-Za-z_][\w]*)\s*\.\s*\[?(?<column>[\w]+)\]?\s+AS\s+\[?(?<field>[\w]+)\]?")
                If joinAliases.Contains(selectMatch.Groups("alias").Value) Then
                    result(selectMatch.Groups("field").Value) = selectMatch.Groups("column").Value
                End If
            Next

            Return result
        End Function

        Private Shared Function BracketIdentifier(name As String) As String
            Return "[" & If(name, String.Empty).Replace("]", "]]", StringComparison.Ordinal) & "]"
        End Function

        ''' <summary>
        ''' A short alias in the hand-written style - the table's initial, past any FW_ prefix, so
        ''' FW_Entity is E and FW_Gender is G. Numbered when two tables would claim the same
        ''' letter, which two lookups pointing at the same table always would.
        ''' </summary>
        Private Shared Function BuildTableAlias(tableName As String, usedAliases As HashSet(Of String)) As String
            Dim stem = If(tableName, String.Empty).Trim()
            If stem.StartsWith("FW_", StringComparison.OrdinalIgnoreCase) Then stem = stem.Substring(3)

            Dim letter = "T"
            For Each character In stem
                If Char.IsLetter(character) Then
                    letter = Char.ToUpperInvariant(character).ToString()
                    Exit For
                End If
            Next

            Dim candidate = letter
            Dim suffix = 2
            While usedAliases.Contains(candidate)
                candidate = letter & suffix.ToString(Globalization.CultureInfo.InvariantCulture)
                suffix += 1
            End While

            usedAliases.Add(candidate)
            Return candidate
        End Function

        ''' <summary>
        ''' Turns the browse grid's relationship columns into LEFT JOINs, and returns the
        ''' expression each field displays in place of its identifier.
        '''
        ''' LEFT, not INNER: a row whose lookup is empty still belongs in the grid. A field with no
        ''' declared relationship, or one left blank in Displays, produces no join and keeps showing
        ''' the identifier - blank is a deliberate answer, not a missing one.
        ''' </summary>
        Private Shared Function BuildLookupJoins(browseGrid As DataGridView,
                                                 baseAlias As String,
                                                 usedAliases As HashSet(Of String),
                                                 joins As List(Of String)) As Dictionary(Of String, String)
            Dim displayExpressions As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            For Each row As DataGridViewRow In browseGrid.Rows
                If Not Convert.ToBoolean(row.Cells("Include").Value) Then Continue For

                Dim fieldName = Convert.ToString(row.Cells("FieldName").Value)
                Dim target = Convert.ToString(row.Cells("LookupTarget").Value)
                Dim displayColumn = Convert.ToString(row.Cells("Displays").Value)
                If String.IsNullOrWhiteSpace(fieldName) OrElse
                   String.IsNullOrWhiteSpace(target) OrElse
                   String.IsNullOrWhiteSpace(displayColumn) Then Continue For

                Dim pieces = target.Split("."c)
                If pieces.Length <> 2 Then Continue For

                Dim lookupAlias = BuildTableAlias(pieces(0), usedAliases)
                joins.Add("LEFT JOIN dbo." & BracketIdentifier(pieces(0)) & " " & lookupAlias &
                          " ON " & baseAlias & "." & BracketIdentifier(fieldName) &
                          " = " & lookupAlias & "." & BracketIdentifier(pieces(1)))
                displayExpressions(fieldName) = lookupAlias & "." & BracketIdentifier(displayColumn)
            Next

            Return displayExpressions
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

            ' Every column carries its table alias. Not decoration: the registration and
            ' view-only-mine predicates are appended to this SQL at runtime as plain column names,
            ' and FW_Users - the commonest lookup target - has a RegistrationID and a UserID of its
            ' own. Unqualified, those predicates become ambiguous the moment a join exists.
            Dim usedAliases As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim baseAlias = BuildTableAlias(tableName, usedAliases)
            Dim joins As New List(Of String)()
            Dim displayExpressions = BuildLookupJoins(browseGrid, baseAlias, usedAliases, joins)

            Dim selectParts = selectedFields.Select(
                Function(field)
                    If String.Equals(field, primaryKeyField, StringComparison.OrdinalIgnoreCase) Then
                        Return baseAlias & "." & BracketIdentifier(field) & " AS PK"
                    End If

                    ' Aliased back to the column it replaces, so the field-level permissions and
                    ' caption overrides keyed on that name still find it. Alias it to anything
                    ' else and a role denied the field would see the name in its place.
                    Dim displayExpression As String = Nothing
                    If displayExpressions.TryGetValue(field, displayExpression) Then
                        Return displayExpression & " AS " & BracketIdentifier(field)
                    End If

                    Return baseAlias & "." & BracketIdentifier(field)
                End Function)
            Dim sql As New StringBuilder()
            sql.AppendLine("SELECT")
            sql.AppendLine("    " & String.Join("," & Environment.NewLine & "    ", selectParts))
            sql.AppendLine("FROM dbo." & BracketIdentifier(tableName) & " " & baseAlias)
            For Each joinClause In joins
                sql.AppendLine(joinClause)
            Next
            If hasRegistrationField Then
                sql.AppendLine("WHERE " & baseAlias & ".[RegistrationID] = @RegistrationID")
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
                ' Ordering follows what the column shows. Sorting a column of manager names by the
                ' identifier behind them would look like no sort at all.
                Dim displayExpression As String = Nothing
                If displayExpressions.TryGetValue(field, displayExpression) Then
                    orderByParts.Add(displayExpression & " " & direction)
                Else
                    orderByParts.Add(baseAlias & "." & BracketIdentifier(field) & " " & direction)
                End If
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
                    {"Owner", If(SelectedOwner() Is Nothing, String.Empty, SelectedOwner().FolderName)},
                    {"UseQbeOnly", useQbeOnlyCheckBox.Checked},
                    {"UseHotFields", displayHotFieldsCheckBox.Checked},
                    {"UnderlyingTableName", DbSaveValue(underlyingTableNameTextBox.Text)},
                    {"UseRegistrationID", useRegistrationIdCheckBox.Checked},
                    {"BrowseFields", DbSaveValue(browseFieldsTextBox.Text)},
                    {"HotFields", DbSaveValue(hotFieldsTextBox.Text)},
                    {"TableAlias", DbSaveValue(tableAliasTextBox.Text)},
                    {"MaintenanceFields", DbSaveValue(maintenanceFieldsTextBox.Text)},
                    {"BrowseSql", DbSaveValue(browseSqlTextBox.Text)},
                    {"LookupFields", DbSaveValue(lookupSpecs)},
                    {"Column2Fields", DbSaveValue(column2Fields)},
                    {"AdminRequiredFields", DbSaveValue(adminRequiredFieldsTextBox.Text)},
                    {"MenuCaller", DbSaveValue(SelectedMenuCaller())},
                    {"IconFileName", DbSaveValue(IconPicker.IconChoiceValue(iconFileNameTextBox.Text))}
                }
                If Not DataAccess.SavePageGeneration(isNewRecord, Integer.Parse(If(String.IsNullOrWhiteSpace(generatedPageIdTextBox.Text), "0", generatedPageIdTextBox.Text)), values, originalRowVersion) Then
                    ' A deleted record is not a conflict to overwrite.
                    If HandleRecordDeletedDuringSave() Then Return False
                    Return ConfirmConcurrencyOverwrite()
                End If
                Dim savedRequestId = If(String.IsNullOrWhiteSpace(generatedPageIdTextBox.Text),
                                        DataAccess.GetPageGenerationId(requestNameTextBox.Text, browsePageNameTextBox.Text, maintenancePageNameTextBox.Text),
                                        Integer.Parse(generatedPageIdTextBox.Text))
                If savedRequestId > 0 Then
                    Dim savedRow = DataAccess.GetPageGenerationById(savedRequestId)
                    If savedRow IsNot Nothing Then
                        generatedPageIdTextBox.Text = DbText(savedRow("GeneratedPageID"))
                        recordId = savedRequestId
                        isNewRecord = False
                        If Not savedRow.IsNull("RowVersion") Then
                            originalRowVersion = CType(DirectCast(savedRow("RowVersion"), Byte()).Clone(), Byte())
                            CaptureOriginalRowVersion(originalRowVersion)
                        End If
                    End If
                End If
                hasUnsavedChanges = False
                ApplyPageSettingsWithoutGenerating()
                Return True
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message.ToUpperInvariant(), "SAVE PAGE REQUEST", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Writes the parts of a page that are data, so they take effect without generating.
        ''' </summary>
        ''' <remarks>
        ''' A browse page reads its columns, caption and Hot Fields setting from FW_Pages at runtime
        ''' and never names them in code, so changing them needs a row and not a rebuild. They were
        ''' only ever written by the generator, which meant regenerating a page - rewriting its
        ''' source, invalidating its hash - to change a column it reads from the database anyway.
        '''
        ''' Updates a page that exists; never creates one. Generating is what brings a page into
        ''' being, and a row for a page with no file would be a page that cannot open and an entry
        ''' in lists that name real pages.
        '''
        ''' The Hot Fields LIST is deliberately not written here. An App Admin owns it once the page
        ''' exists, ticking fields on the panel itself, and a save from this form would wipe that
        ''' with no warning. Generating is the deliberate act that takes it back to the request.
        ''' </remarks>
        Private Sub ApplyPageSettingsWithoutGenerating()
            Dim pageName = browsePageNameTextBox.Text.Trim()
            If Not generateBrowsePageCheckBox.Checked OrElse pageName = String.Empty Then
                Return
            End If

            Dim tableName = underlyingTableNameTextBox.Text.Trim()
            If tableName = String.Empty Then
                Return
            End If

            If Not DataAccess.CheckIfPageRecordExists(0, pageName) Then
                Return
            End If

            Dim applied As New List(Of String)()

            Dim aliasValue = tableAliasTextBox.Text.Trim()
            Dim sqlValue = browseSqlTextBox.Text.Trim()
            If sqlValue <> String.Empty Then
                If DataAccess.UpsertPageRecord(0, pageName, tableName, aliasValue, sqlValue, CurrentUserId()) Then
                    applied.Add("grid columns and their order")
                    If aliasValue <> String.Empty Then
                        applied.Add("the page caption")
                    End If
                End If
            End If

            ' Ticking a Hot Field is asking for the panel, the same rule the generator applies.
            Dim wantsHotFields = displayHotFieldsCheckBox.Checked OrElse hotFieldsTextBox.Text.Trim() <> String.Empty
            If DataAccess.SetPageUsesHotFields(pageName, wantsHotFields) Then
                applied.Add(If(wantsHotFields, "Hot Fields on", "Hot Fields off"))
            End If

            If applied.Count = 0 Then
                Return
            End If

            ' Said plainly rather than left to be discovered, because the split is the whole point:
            ' what a page reads from the database changed just now, and what is compiled into it did
            ' not. Somebody who does not know which is which should not have to find out by testing.
            MessageBox.Show(Me,
                            "SAVED, AND APPLIED TO " & pageName.ToUpperInvariant() & " WITHOUT GENERATING:" &
                            Environment.NewLine & "  " & String.Join(Environment.NewLine & "  ", applied) &
                            Environment.NewLine & Environment.NewLine &
                            "THESE STILL NEED GENERATE: THE _U PAGE'S FIELDS, ITS LOOKUPS, THE MENU OR " &
                            "DASHBOARD BUTTON, AND THE HOT FIELDS SELECTION.",
                            "SAVE PAGE REQUEST",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
        End Sub

        ''' <summary>Whose name goes on a generation request: the acting user, so an administrator
        ''' viewing as somebody else is recorded as having generated the page.</summary>
        Private Shared Function CurrentUserId() As Integer
            Return SessionState.ActingUserID
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
                Replace("{{LOOKUP_FIELDS}}", ValueOrDefault(lookupSpecs), StringComparison.Ordinal).
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
                "- If Generate Browse Page is False, do not create or overwrite the `_B` page, its generated dashboard icon, or its generated FW_Pages record.",
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
                "- If Generate Browse Page is False, do not create or overwrite the _B page, its generated dashboard icon, or its generated FW_Pages record.",
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
