Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Brings a CSV or JSON file of people into FW_Employees, each with a login, the chosen role
    ''' and a password.
    '''
    ''' Three steps, one per tab: where the file comes from and where it goes; which heading
    ''' feeds which field, with a default for anything the file does not carry; and a pre-check
    ''' that names every problem row by row, lets the rows be edited or skipped, and only then
    ''' offers Import.
    '''
    ''' **It writes through the same path FW_Employees_U does** - DataAccess.ImportEmployees runs
    ''' every row through the core of TrySaveGeneratedPageRecord, which owns creating the login
    ''' first, hashing the password on the UserId that insert returns, and assigning the role. An
    ''' import with its own SQL would bypass all three, and the difference would surface months
    ''' later as employees who cannot sign in.
    '''
    ''' **Nothing is written until every included row is clean, and then all of it or none.** A
    ''' file half-imported and half refused is worse than one refused outright, because the second
    ''' attempt then has to know what the first one did.
    '''
    ''' **The passwords leave in one place only**: the results file handed over when the import
    ''' finishes. The database keeps the hash, the audit trail keeps nothing, and closing the page
    ''' without that file means issuing new passwords by hand.
    '''
    ''' A Form rather than FW_Base_U: there is no record being maintained, no RowVersion, and no
    ''' single key - the same reason Roles_C is a Form.
    ''' </summary>
    Public Class FW_EmployeeImport
        Inherits Form

        Private Const PageName As String = "FW_EmployeeImport"
        Private Const NotMapped As String = "(not mapped)"

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        ' Source tab
        Private tabs As TabControl
        Private sourceTab As TabPage
        Private mapTab As TabPage
        Private checkTab As TabPage
        Private registrationLabel As Label
        Private registrationComboBox As ComboBox
        Private roleLabel As Label
        Private roleComboBox As ComboBox
        Private timeZoneLabel As Label
        Private timeZoneValueLabel As Label
        Private registrationTimeZone As (Id As Integer, Name As String)
        Private fileLabel As Label
        Private fileTextBox As TextBox
        Private chooseFileButton As Button
        Private headerCheckBox As CheckBox
        Private delimiterLabel As Label
        Private delimiterComboBox As ComboBox

        ''' <summary>Set while the page itself sets the file options, so doing so does not re-read the file.</summary>
        Private settingSourceOptions As Boolean
        Private sourceSummaryLabel As Label
        Private sourceGrid As DataGridView
        Private toMapButton As Button

        ' Map tab
        Private sourceTemplateComboBox As ComboBox
        Private templateNameTextBox As TextBox
        Private saveTemplateButton As Button
        Private deleteTemplateButton As Button
        Private templateInfoLabel As Label
        Private clearMappingButton As Button
        Private mapSummaryLabel As Label
        Private mapPreviewGrid As DataGridView
        Private targetTableComboBox As ComboBox
        Private targetList As ListBox
        Private mappedGrid As DataGridView
        Private sourceList As ListBox
        Private defaultValueLabel As Label
        Private defaultValueTextBox As TextBox
        Private choicesPanel As FlowLayoutPanel
        Private becomesLabel As Label
        Private mapButton As Button
        Private removeMappingButton As Button
        Private toCheckButton As Button

        ' Check tab
        Private checkSummaryLabel As Label
        Private checkGrid As DataGridView
        Private importButton As Button
        Private reportButton As Button
        Private batchNameTextBox As TextBox
        Private batchNoteTextBox As TextBox

        ''' <summary>Imports of this same file that have not been undone, read with the duplicate check.</summary>
        Private earlierImports As New List(Of String)()
        Private pastImportsButton As Button

        Private closeButton As Button

        Private plan As EmployeeImportPlan
        Private source As ImportSource
        Private sourceFileName As String = String.Empty
        Private sourceData As Byte()
        Private templates As DataTable
        Private currentTemplateId As Integer

        ''' <summary>The mapping changed since the check rows were built from it.</summary>
        Private mappingChangedSinceRows As Boolean = True


        Private suppressGridEvents As Boolean

        ''' <summary>Set while a middle-list selection drives the side lists, so they do not take the focus.</summary>
        Private highlightingPair As Boolean
        Private imported As Boolean
        Private resultsHtml As String = String.Empty
        Private resultsFileName As String = String.Empty

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = user
            accessProfile = profile

            Text = "Import Employees"
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            StartPosition = FormStartPosition.CenterParent
            ClientSize = New Size(1100, 700)
            Font = New Font("Segoe UI", 9.0F)

            BuildLayout()
            AddHandler Shown, AddressOf FW_EmployeeImport_Shown
            AddHandler FormClosing, AddressOf FW_EmployeeImport_FormClosing
        End Sub

#Region "Layout"

        Private Sub BuildLayout()
            tabs = New TabControl() With {
                .Location = New Point(12, 12),
                .Size = New Size(ClientSize.Width - 24, ClientSize.Height - 70)
            }
            sourceTab = New TabPage("1. Source")
            mapTab = New TabPage("2. Map Fields")
            checkTab = New TabPage("3. Check && Import")
            tabs.TabPages.AddRange({sourceTab, mapTab, checkTab})
            AddHandler tabs.Selecting, AddressOf Tabs_Selecting

            BuildSourceTab()
            BuildMapTab()
            BuildCheckTab()

            closeButton = New Button() With {
                .Name = "Button_Close",
                .Text = "Close",
                .Size = New Size(100, 30),
                .Location = New Point(ClientSize.Width - 112, ClientSize.Height - 45),
                .DialogResult = DialogResult.Cancel
            }

            Controls.AddRange(New Control() {tabs, closeButton})
            CancelButton = closeButton
        End Sub

        Private Shared Function BoldLabel(text As String, location As Point) As Label
            Return New Label() With {
                .Text = text,
                .Location = location,
                .AutoSize = True,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
            }
        End Function

        ''' <summary>
        ''' A caption for a field this page requires of everyone, painted as Base_U paints one:
        ''' the App Admin required colour, which means "declared required by the page" rather
        ''' than "required for this role". Referenced, never copied - the ARGB is load-bearing.
        ''' </summary>
        Private Shared Function RequiredLabel(text As String, location As Point) As Label
            Dim label = BoldLabel(text, location)
            label.BackColor = FW_Base_U.AppAdminRequiredBackColor
            label.Padding = New Padding(2, 1, 2, 1)
            Return label
        End Function

        Private Shared Function NewGrid(location As Point, size As Size) As DataGridView
            Dim grid As New DataGridView() With {
                .Location = location,
                .Size = size,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .RowHeadersVisible = False,
                .MultiSelect = False,
                .ColumnHeadersHeight = 30,
                .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            }
            grid.RowTemplate.Height = 24
            BrowseGridStandardizer.ApplyLightBlueHeaderStyle(grid)
            Return grid
        End Function

        Private Sub BuildSourceTab()
            Dim labelLeft = 16
            Dim fieldLeft = 170
            Dim y = 18

            ' First, because choosing one fills in everything after it - Import Into, the role,
            ' the file and its mapping (Glenn, 2026-09-24). Optional: Make a Selection leaves every
            ' field below to be filled in by hand. Wide enough for "name (registration)".
            Dim templateLabel = BoldLabel("Saved Imports", New Point(labelLeft, y + 3))
            sourceTemplateComboBox = New ComboBox() With {
                .Name = "ComboBox_SavedImport",
                .Location = New Point(fieldLeft, y),
                .Width = 340,
                .DropDownStyle = ComboBoxStyle.DropDownList
            }
            AddHandler sourceTemplateComboBox.SelectedIndexChanged, AddressOf SourceTemplateComboBox_SelectedIndexChanged
            templateInfoLabel = New Label() With {
                .Location = New Point(fieldLeft + 490, y + 3),
                .AutoSize = True,
                .ForeColor = Color.FromArgb(110, 110, 110)
            }

            y += 34
            registrationLabel = BoldLabel("Import Into", New Point(labelLeft, y + 3))
            registrationComboBox = New ComboBox() With {
                .Name = "ComboBox_RegistrationID",
                .Location = New Point(fieldLeft, y),
                .Width = 280,
                .DropDownStyle = ComboBoxStyle.DropDownList
            }
            AddHandler registrationComboBox.SelectedIndexChanged, AddressOf RegistrationComboBox_SelectedIndexChanged

            y += 34
            roleLabel = RequiredLabel("Role For Everyone", New Point(labelLeft, y + 3))
            roleComboBox = New ComboBox() With {
                .Name = "ComboBox_RoleID",
                .Location = New Point(fieldLeft, y),
                .Width = 280,
                .DropDownStyle = ComboBoxStyle.DropDownList
            }

            y += 34
            ' Shown, not chosen: the registration's own zone, which the write reads for itself. A
            ' file whose people are in several zones says so with a Time Zone column, mapped on the
            ' next tab, and that wins row by row.
            timeZoneLabel = BoldLabel("Time Zone", New Point(labelLeft, y + 3))
            timeZoneValueLabel = New Label() With {
                .Name = "Label_TimeZoneValue",
                .Location = New Point(fieldLeft, y + 3),
                .AutoSize = True
            }

            y += 40
            fileLabel = RequiredLabel("File", New Point(labelLeft, y + 3))
            fileTextBox = New TextBox() With {
                .Name = "TextBox_File",
                .Location = New Point(fieldLeft, y),
                .Width = 400,
                .ReadOnly = True,
                .BackColor = SystemColors.Control
            }
            chooseFileButton = New Button() With {
                .Name = "Button_ChooseFile",
                .Text = "Choose...",
                .Location = New Point(fieldLeft + 410, y - 2),
                .Size = New Size(100, 28)
            }
            AddHandler chooseFileButton.Click, AddressOf ChooseFileButton_Click

            Dim formatHint As New Label() With {
                .Text = "CSV or JSON",
                .Location = New Point(fieldLeft + 520, y + 4),
                .AutoSize = True,
                .ForeColor = Color.FromArgb(110, 110, 110)
            }

            ' Both hidden until a CSV is loaded: neither means anything before there is a file, nor
            ' for JSON, whose keys are its headings and which has no delimiter. The tick starts on
            ' a guess made from the file, and the grid below is there to check it against.
            y += 36
            headerCheckBox = New CheckBox() With {
                .Name = "CheckBox_HasHeaderRow",
                .Text = "First row holds the column headings",
                .Location = New Point(fieldLeft, y),
                .AutoSize = True,
                .Visible = False
            }
            AddHandler headerCheckBox.CheckedChanged, AddressOf SourceOptions_Changed

            delimiterLabel = New Label() With {
                .Text = "Delimiter",
                .Location = New Point(fieldLeft + 300, y + 2),
                .AutoSize = True,
                .Visible = False
            }
            delimiterComboBox = New ComboBox() With {
                .Name = "ComboBox_Delimiter",
                .Location = New Point(fieldLeft + 370, y - 2),
                .Width = 110,
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Visible = False
            }
            delimiterComboBox.Items.AddRange(New Object() {"Detect", "Comma", "Semicolon", "Tab", "Pipe"})
            delimiterComboBox.SelectedIndex = 0
            AddHandler delimiterComboBox.SelectedIndexChanged, AddressOf SourceOptions_Changed

            y += 34
            sourceSummaryLabel = New Label() With {
                .Text = "No file chosen.",
                .Location = New Point(labelLeft, y),
                .AutoSize = True,
                .ForeColor = Color.FromArgb(90, 90, 90)
            }

            y += 24
            sourceGrid = NewGrid(New Point(labelLeft, y), New Size(tabs.Width - 44, tabs.Height - y - 84))
            sourceGrid.ReadOnly = True
            sourceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect

            toMapButton = New Button() With {
                .Name = "Button_NextToMap",
                .Text = "Next: Map Fields >",
                .Size = New Size(160, 30),
                .Location = New Point(tabs.Width - 190, tabs.Height - 76),
                .Enabled = False
            }
            AddHandler toMapButton.Click, Sub(sender, e) tabs.SelectedTab = mapTab

            ' Where an import is found again, and undone - the only way in (no dashboard tile, Glenn
            ' 2026-09-24). Beside Saved Imports, because the moment somebody wonders whether a file
            ' went in already is the moment they are choosing it.
            pastImportsButton = New Button() With {
                .Name = "Button_PastImports",
                .Text = "Past Imports...",
                .Size = New Size(130, 28),
                .Location = New Point(fieldLeft + 350, 16)
            }
            AddHandler pastImportsButton.Click, AddressOf PastImportsButton_Click

            sourceTab.Controls.AddRange(New Control() {pastImportsButton, templateLabel, sourceTemplateComboBox, templateInfoLabel,
                                                       registrationLabel, registrationComboBox, roleLabel, roleComboBox,
                                                       timeZoneLabel, timeZoneValueLabel,
                                                       fileLabel, fileTextBox, chooseFileButton, formatHint,
                                                       headerCheckBox, delimiterLabel, delimiterComboBox,
                                                       sourceSummaryLabel, sourceGrid, toMapButton})

            ' Tab order top to bottom, in the order the tab is worked through. Set here rather than
            ' left to the order the controls were added: this page is a plain Form, not a generated
            ' maintenance page, so there is no Tab Order manager to set it. Labels and the read-only
            ' file box are not stops.
            Dim stops As Control() = {sourceTemplateComboBox, pastImportsButton, registrationComboBox, roleComboBox, chooseFileButton,
                                      headerCheckBox, delimiterComboBox, sourceGrid, toMapButton}
            For i = 0 To stops.Length - 1
                stops(i).TabIndex = i
                stops(i).TabStop = True
            Next
            fileTextBox.TabStop = False
        End Sub

        ''' <summary>
        ''' Three panes, pick-and-pair: the fields still to fill on the left, the file's columns on
        ''' the right, and only the pairs made so far in the middle. What is left to do is always in
        ''' view on both sides, and the middle reads as the whole mapping at a glance - a grid of
        ''' every field with a drop-down each hid both behind closed lists.
        '''
        ''' Clicks and double-clicks only, nothing driven by hover or dragging: in a Thinfinity
        ''' session those are the events that always arrive.
        ''' </summary>
        Private Sub BuildMapTab()
            Dim left = 16
            Dim width = tabs.Width - 44
            Dim y = 12

            ' Naming and saving happen here, where the mapping is. Choosing a template happens on
            ' the Source tab, before the file, so the file maps itself the moment it is read.
            Dim templateNameLabel = BoldLabel("Save As", New Point(left, y + 3))
            templateNameTextBox = New TextBox() With {
                .Name = "TextBox_ImportName",
                .Location = New Point(left + 110, y),
                .Width = 260,
                .MaxLength = 100
            }

            saveTemplateButton = New Button() With {.Name = "Button_SaveTemplate", .Text = "Save", .Size = New Size(70, 26), .Location = New Point(left + 380, y - 1)}
            deleteTemplateButton = New Button() With {.Name = "Button_DeleteTemplate", .Text = "Delete", .Size = New Size(70, 26), .Location = New Point(left + 455, y - 1)}
            AddHandler saveTemplateButton.Click, AddressOf SaveTemplateButton_Click
            AddHandler deleteTemplateButton.Click, AddressOf DeleteTemplateButton_Click

            Dim templateHint As New Label() With {
                .Text = "the Saved Import chosen on Source, or a new name",
                .Location = New Point(left + 535, y + 4),
                .AutoSize = True,
                .ForeColor = Color.FromArgb(110, 110, 110)
            }

            ' The file itself, a few rows of it, so the columns on the right have faces.
            y += 36
            mapPreviewGrid = NewGrid(New Point(left, y), New Size(width, 118))
            mapPreviewGrid.ReadOnly = True
            mapPreviewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect

            Dim paneTop = y + 118 + 36
            Dim paneBottom = tabs.Height - 162
            Dim sideWidth = 240
            Dim middleLeft = left + sideWidth + 12
            Dim rightLeft = left + width - sideWidth
            Dim middleWidth = rightLeft - 12 - middleLeft

            ' Left: which table, then its fields that nothing fills yet.
            ' Headings that say what each list holds: the fields being filled on the left, the
            ' file's columns on the right. "Goes To" and "From The File" were read the other way
            ' round twice on 2026-09-24.
            Dim goesToLabel = BoldLabel("Fill This Field", New Point(left, paneTop - 27))
            targetTableComboBox = New ComboBox() With {
                .Name = "ComboBox_TargetTable",
                .Location = New Point(left + 118, paneTop - 30),
                .Width = sideWidth - 118,
                .DropDownStyle = ComboBoxStyle.DropDownList
            }
            targetTableComboBox.Items.AddRange(New Object() {"Employee", "Login"})
            targetTableComboBox.SelectedIndex = 0
            AddHandler targetTableComboBox.SelectedIndexChanged, Sub(sender, e) RefreshTargetList()

            targetList = New ListBox() With {
                .Name = "List_TargetFields",
                .Location = New Point(left, paneTop),
                .Size = New Size(sideWidth, paneBottom - paneTop),
                .IntegralHeight = False,
                .DisplayMember = "Text"
            }
            AddHandler targetList.DoubleClick, Sub(sender, e) If sourceList.SelectedItem IsNot Nothing Then MapSelected()

            ' Middle: the pairs made so far.
            Dim mappedLabel = BoldLabel("Mapped", New Point(middleLeft, paneTop - 27))
            mappedGrid = NewGrid(New Point(middleLeft, paneTop), New Size(middleWidth, paneBottom - paneTop))
            mappedGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            mappedGrid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            mappedGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Field", .HeaderText = "Field", .ReadOnly = True, .Width = 150})
            mappedGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "From", .HeaderText = "From", .ReadOnly = True, .Width = 130})
            mappedGrid.Columns.Add(New DataGridViewTextBoxColumn With {
                .Name = "Default", .HeaderText = "Default", .Width = 100,
                .ToolTipText = "Used wherever the file's cell is blank. Click and type to change it."
            })
            mappedGrid.Columns.Add(New DataGridViewTextBoxColumn With {
                .Name = "Sample", .HeaderText = "First Row Becomes", .ReadOnly = True,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            })
            For Each column As DataGridViewColumn In mappedGrid.Columns
                column.SortMode = DataGridViewColumnSortMode.NotSortable
            Next
            AddHandler mappedGrid.CellValueChanged, AddressOf MappedGrid_CellValueChanged
            ' Clicking a pair highlights both halves - a click, not SelectionChanged. That also fired
            ' when the page re-selected a row after Map or a refresh, and moved the left list to a
            ' field nobody had picked (Glenn, 2026-09-24). Removing a pair takes the Remove button;
            ' a double-click that unmapped a field was too easy to do while meaning to look.
            AddHandler mappedGrid.CellClick, AddressOf MappedGrid_CellClick

            ' Right: the file's columns, with a default as the first thing that can be chosen.
            Dim fromLabel = BoldLabel("With This, From The File", New Point(rightLeft, paneTop - 27))
            sourceList = New ListBox() With {
                .Name = "List_SourceColumns",
                .Location = New Point(rightLeft, paneTop),
                .Size = New Size(sideWidth, paneBottom - paneTop - 124),
                .IntegralHeight = False,
                .DisplayMember = "Text"
            }
            AddHandler sourceList.SelectedIndexChanged, AddressOf SourceList_SelectedIndexChanged
            AddHandler sourceList.DoubleClick, Sub(sender, e) If targetList.SelectedItem IsNot Nothing Then MapSelected()

            defaultValueLabel = New Label() With {
                .Text = "Default value",
                .Location = New Point(rightLeft, paneBottom - 118),
                .AutoSize = True,
                .Enabled = False
            }
            defaultValueTextBox = New TextBox() With {
                .Name = "TextBox_DefaultValue",
                .Location = New Point(rightLeft, paneBottom - 98),
                .Width = sideWidth,
                .Enabled = False
            }
            AddHandler defaultValueTextBox.KeyDown, AddressOf DefaultValueTextBox_KeyDown
            AddHandler defaultValueTextBox.TextChanged, Sub(sender, e) UpdateBecomes()

            ' What the field selected on the left can take besides plain text - Today and Now for
            ' a date, Yes and No for a flag, the User Name patterns - offered as clicks, so nobody
            ' has to remember them (Glenn, 2026-09-24). Empty for a plain text field.
            choicesPanel = New FlowLayoutPanel() With {
                .Name = "Panel_DefaultChoices",
                .Location = New Point(rightLeft, paneBottom - 70),
                .Size = New Size(sideWidth, 48),
                .WrapContents = True,
                .FlowDirection = FlowDirection.LeftToRight
            }

            ' What the value in the box becomes for the file's first row, as it is typed.
            becomesLabel = New Label() With {
                .Name = "Label_DefaultBecomes",
                .Location = New Point(rightLeft, paneBottom - 18),
                .Size = New Size(sideWidth, 18),
                .AutoEllipsis = True,
                .ForeColor = Color.FromArgb(60, 60, 60)
            }
            AddHandler targetList.SelectedIndexChanged, Sub(sender, e)
                                                            UpdateDefaultBox()
                                                            RefreshDefaultChoices()
                                                        End Sub

            ' Under the middle: what to do with a selection.
            Dim buttonTop = paneBottom + 8
            mapButton = New Button() With {.Name = "Button_Map", .Text = "Map", .Size = New Size(90, 28), .Location = New Point(middleLeft, buttonTop), .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)}
            removeMappingButton = New Button() With {.Name = "Button_RemoveMapping", .Text = "Remove", .Size = New Size(90, 28), .Location = New Point(middleLeft + 96, buttonTop)}
            clearMappingButton = New Button() With {.Name = "Button_ClearMapping", .Text = "Clear All", .Size = New Size(90, 28), .Location = New Point(middleLeft + 210, buttonTop)}
            AddHandler mapButton.Click, Sub(sender, e) MapSelected()
            AddHandler removeMappingButton.Click, Sub(sender, e) RemoveSelectedMapping()
            AddHandler clearMappingButton.Click, AddressOf ClearMappingButton_Click

            Dim requiredNote As New Label() With {
                .Text = "* required",
                .Location = New Point(left, buttonTop + 6),
                .AutoSize = True,
                .ForeColor = Color.FromArgb(110, 110, 110)
            }

            mapSummaryLabel = New Label() With {
                .Location = New Point(left, buttonTop + 40),
                .Size = New Size(width, 36)
            }

            Dim backButton As New Button() With {.Text = "< Back", .Size = New Size(100, 30), .Location = New Point(left, tabs.Height - 76)}
            AddHandler backButton.Click, Sub(sender, e) tabs.SelectedTab = sourceTab

            toCheckButton = New Button() With {
                .Name = "Button_NextToCheck",
                .Text = "Next: Check Rows >",
                .Size = New Size(160, 30),
                .Location = New Point(tabs.Width - 190, tabs.Height - 76)
            }
            AddHandler toCheckButton.Click, Sub(sender, e) tabs.SelectedTab = checkTab

            mapTab.Controls.AddRange(New Control() {templateNameLabel, templateNameTextBox, saveTemplateButton,
                                                    deleteTemplateButton, templateHint, mapPreviewGrid,
                                                    goesToLabel, targetTableComboBox, targetList,
                                                    mappedLabel, mappedGrid,
                                                    fromLabel, sourceList, defaultValueLabel, defaultValueTextBox, choicesPanel, becomesLabel,
                                                    mapButton, removeMappingButton, clearMappingButton,
                                                    requiredNote, mapSummaryLabel, backButton, toCheckButton})
        End Sub

        ''' <summary>
        ''' The pre-check, read-only. An import is the whole file or nothing: a row with a problem is
        ''' fixed in the file, not here, and Import with any problem left shows what to fix and
        ''' writes nothing.
        ''' </summary>
        Private Sub BuildCheckTab()
            Dim left = 16
            Dim y = 16

            checkSummaryLabel = New Label() With {
                .Location = New Point(left, y + 4),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
            }

            reportButton = New Button() With {
                .Name = "Button_Report",
                .Text = "Show Problems",
                .Size = New Size(150, 28),
                .Location = New Point(tabs.Width - 190, y),
                .Visible = False
            }
            AddHandler reportButton.Click, AddressOf ReportButton_Click

            y += 40
            checkGrid = NewGrid(New Point(left, y), New Size(tabs.Width - 44, tabs.Height - y - 124))
            checkGrid.ReadOnly = True
            checkGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            checkGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None

            ' The import's name and note: required, and asked for here, at the gateway, because an
            ' import that commits becomes a batch under Past Imports and this is how it is found -
            ' and undone - later (Glenn, 2026-09-24). Import stays disabled until both are given.
            Dim batchTop = tabs.Height - 114
            Dim batchNameLabel = RequiredLabel("Import Name", New Point(left, batchTop + 3))
            batchNameTextBox = New TextBox() With {
                .Name = "TextBox_BatchName",
                .Location = New Point(left + 100, batchTop),
                .Width = 260,
                .MaxLength = 100
            }
            Dim batchNoteLabel = RequiredLabel("Note", New Point(left + 380, batchTop + 3))
            batchNoteTextBox = New TextBox() With {
                .Name = "TextBox_BatchNote",
                .Location = New Point(left + 430, batchTop),
                .Width = tabs.Width - 44 - 430,
                .MaxLength = 1000
            }
            AddHandler batchNameTextBox.TextChanged, Sub(sender, e) UpdateImportAvailability()
            AddHandler batchNoteTextBox.TextChanged, Sub(sender, e) UpdateImportAvailability()

            Dim backButton As New Button() With {.Text = "< Back", .Size = New Size(100, 30), .Location = New Point(left, tabs.Height - 76)}
            AddHandler backButton.Click, Sub(sender, e) tabs.SelectedTab = mapTab

            importButton = New Button() With {
                .Name = "Button_Import",
                .Text = "Import",
                .Size = New Size(180, 30),
                .Location = New Point(tabs.Width - 210, tabs.Height - 76),
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
                .Enabled = False
            }
            AddHandler importButton.Click, AddressOf ImportButton_Click

            checkTab.Controls.AddRange(New Control() {checkSummaryLabel, reportButton, checkGrid,
                                                      batchNameLabel, batchNameTextBox, batchNoteLabel, batchNoteTextBox,
                                                      backButton, importButton})
        End Sub

#End Region

#Region "Opening"

        ''' <summary>
        ''' Refused here as well as at the write: a page that opens and then fails at Import has
        ''' wasted somebody's mapping. The write refuses too, because a page is not a boundary.
        ''' </summary>
        Private Sub FW_EmployeeImport_Shown(sender As Object, e As EventArgs)
            If Not DataAccess.CanImportEmployees() Then
                MessageBox.Show(Me, "Only an Application Admin or a Company Admin may import employees.",
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Close()
                Return
            End If

            Try
                Cursor = Cursors.WaitCursor
                Dim targets = ImportTargetSchema.ReadEmployeeTargets()
                plan = New EmployeeImportPlan(targets)
                LoadRegistrations()
                LoadTemplates()
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.Shown")
                MessageBox.Show(Me, "The import could not be prepared: " & ex.Message,
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Close()
            Finally
                Cursor = Cursors.Default
            End Try
        End Sub

        ''' <summary>
        ''' The field captions of the registration being imported into, laid onto the targets, so
        ''' the import says "Reports To" wherever that registration's Employees page does. By the
        ''' registration chosen in Import Into, not the signed-in role: a caption belongs to the
        ''' registration (a stored procedure carries it to every role), and an App Admin importing
        ''' into SARALAND must see SARALAND's. One query, re-run when the registration changes. A
        ''' failure leaves the readable names.
        ''' </summary>
        Private Sub ApplyRegistrationCaptions()
            If plan Is Nothing Then Return

            Dim captions As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Try
                captions = DataAccess.GetRegistrationFieldCaptions(SelectedRegistrationId(),
                                                                   ImportTargetSchema.EmployeesTable, ImportTargetSchema.UsersTable)
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.ApplyRegistrationCaptions")
            End Try

            For Each target In plan.Targets
                Dim caption As String = Nothing
                target.CaptionOverride = If(captions.TryGetValue(target.Key, caption), caption, String.Empty)
            Next

            RefreshMapping()
        End Sub

        ''' <summary>
        ''' The registration combo: shown to an Application Admin only, and filled by the helper the
        ''' browse pages use. A Company Admin imports into the session's registration, sees no
        ''' combo, and everything on the page follows that registration (Glenn, 2026-09-24).
        ''' </summary>
        Private Sub LoadRegistrations()
            Dim own = OwnRegistrationId()
            Dim canChoose = DataAccess.MayImportIntoAnyRegistration()

            registrationLabel.Visible = canChoose
            registrationComboBox.Visible = canChoose

            If canChoose Then
                RegistrationComboHelper.Populate(registrationComboBox, own, includePlaceholder:=False)
            End If

            RegistrationChanged()
        End Sub

        Private Shared Function OwnRegistrationId() As Integer
            Return If(SessionState.IsActive AndAlso SessionState.Current.HasValue, SessionState.Current.Value.RegistrationID, 0)
        End Function

        Private Function SelectedRegistrationId() As Integer
            If Not registrationComboBox.Visible Then Return OwnRegistrationId()

            Dim registrationId As Integer
            Return If(RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId), registrationId, 0)
        End Function

        Private Function SelectedRegistrationName() As String
            If registrationComboBox.Visible AndAlso registrationComboBox.SelectedItem IsNot Nothing Then
                Return registrationComboBox.Text
            End If
            Return "your registration"
        End Function

        Private Sub RegistrationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If plan Is Nothing Then Return
            RegistrationChanged()
        End Sub

        ''' <summary>Roles and templates both belong to a registration, and are re-read with it.</summary>
        Private Sub RegistrationChanged()
            LoadRoles()
            LoadRegistrationTimeZone()
            ApplyRegistrationCaptions()

            ' The Saved Imports list covers every registration and is not re-read here. But one
            ' belongs to its registration: moved off it by hand, the choice is let go - quietly,
            ' keeping the file and mapping - and one with no file takes its columns with it. Not
            ' when choosing the Saved Import is what moved the registration.
            If plan Is Nothing OrElse applyingSavedImport Then Return
            Dim chosen = ChosenTemplate()
            If chosen IsNot Nothing AndAlso RegistrationOf(chosen) <> SelectedRegistrationId() Then
                SelectTemplateQuietly(Nothing)
                If Not FileLoaded() AndAlso templateHeadings IsNot Nothing Then LeaveTemplateOnly()
            End If
        End Sub

        ''' <summary>Set while choosing a Saved Import moves Import Into, so that move does not let it go.</summary>
        Private applyingSavedImport As Boolean

        ''' <summary>
        ''' The registration's roles, starting on Make a Selection - never defaulted.
        '''
        ''' It used to open on the lowest role, and that was decided against on 2026-09-24: the
        ''' role goes to every person in the file, and a default nobody looked at hands a whole
        ''' file of people the wrong permissions without a word - the same reason the time zone
        ''' is never defaulted either. GetSelectableRolesByRegistration still does the filtering
        ''' that matters: no App Admin role is ever offered.
        '''
        ''' Re-read when the registration changes, because roles belong to a registration and the
        ''' previous one's list would be silently wrong rather than visibly empty.
        ''' </summary>
        Private Sub LoadRoles()
            roleComboBox.DataSource = Nothing

            Dim registrationId = SelectedRegistrationId()
            If registrationId <= 0 Then Return

            Try
                Dim roles = DataAccess.GetSelectableRolesByRegistration(registrationId)

                Dim placeholder = roles.NewRow()
                placeholder("ID") = 0
                placeholder("RoleName") = "Make a Selection"
                roles.Rows.InsertAt(placeholder, 0)

                roleComboBox.DisplayMember = "RoleName"
                roleComboBox.ValueMember = "ID"
                roleComboBox.DataSource = roles
                ComboWidth.FitToContent(roleComboBox, roles, "RoleName")
                roleComboBox.SelectedIndex = 0
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.LoadRoles")
            End Try
        End Sub

        ''' <summary>
        ''' The selected registration's time zone, shown so nobody is surprised by it. A
        ''' registration with none is shown in red and stops the import at the Source tab.
        ''' </summary>
        Private Sub LoadRegistrationTimeZone()
            registrationTimeZone = (0, String.Empty)
            Try
                registrationTimeZone = DataAccess.GetRegistrationTimeZone(SelectedRegistrationId())
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.LoadRegistrationTimeZone")
            End Try

            If registrationTimeZone.Id > 0 Then
                timeZoneValueLabel.Text = registrationTimeZone.Name
                timeZoneValueLabel.ForeColor = Color.Black
            Else
                timeZoneValueLabel.Text = "None set for this registration"
                timeZoneValueLabel.ForeColor = Color.Firebrick
            End If
        End Sub

        ''' <summary>
        ''' What the Source tab still lacks, in tab order, with the control to put the focus on.
        ''' One list for both places that ask - leaving the tab and pressing Import - so the two
        ''' cannot come to disagree about what "ready" means.
        ''' </summary>
        Private Function MissingSourceFields() As List(Of (Caption As String, Field As Control))
            Dim missing As New List(Of (Caption As String, Field As Control))()
            If SelectedRegistrationId() <= 0 Then missing.Add(("Import Into", CType(registrationComboBox, Control)))
            If DataAccess.IsEmptyComboSelection(roleComboBox) Then missing.Add(("Role For Everyone", CType(roleComboBox, Control)))
            If registrationTimeZone.Id <= 0 Then
                missing.Add(("Time Zone - the registration has none; set it on the Registration page", CType(registrationComboBox, Control)))
            End If
            If source Is Nothing OrElse Not source.Loaded OrElse source.RowCount = 0 Then missing.Add(("File", CType(chooseFileButton, Control)))
            Return missing
        End Function

        ''' <summary>Says what is missing and puts the focus on the first of it. True when nothing is.</summary>
        Private Function ConfirmSourceComplete() As Boolean
            Dim missing = MissingSourceFields()
            If missing.Count = 0 Then Return True

            tabs.SelectedTab = sourceTab
            MessageBox.Show(Me,
                            "Required before going on:" & Environment.NewLine & Environment.NewLine &
                            String.Join(Environment.NewLine, missing.Select(Function(m) m.Caption)),
                            "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
            If missing(0).Field.Visible AndAlso missing(0).Field.Enabled Then missing(0).Field.Focus()
            Return False
        End Function

        Private Function SelectedRoleId() As Integer
            If roleComboBox.SelectedValue Is Nothing Then Return 0
            Dim value As Integer
            Return If(Integer.TryParse(Convert.ToString(roleComboBox.SelectedValue, CultureInfo.InvariantCulture), value), value, 0)
        End Function

#End Region

#Region "Source"

        ''' <summary>
        ''' Picks the file through the session's own picker, which decides between OpenFileDialog
        ''' and VirtualUI's. In a browser session the process runs on the server, so OpenFileDialog
        ''' would browse a machine the user has never seen.
        ''' </summary>
        Private Sub ChooseFileButton_Click(sender As Object, e As EventArgs)
            ChooseFile(If(sender Is sourceTemplateComboBox, "template", "button"))
        End Sub

        ''' <summary>
        ''' The one way a file comes in, whether Choose... was pressed or a template was chosen.
        '''
        ''' Each step writes a line to startup.log. On 2026-09-24 a template opened the upload
        ''' dialog, a file was picked, and the grid never filled - with no error logged anywhere -
        ''' so the next attempt has to say where it stopped: the picker returning nothing, the read
        ''' failing, or the rows arriving and something clearing them.
        ''' </summary>
        Private Function ChooseFile(openedBy As String) As Boolean
            If imported Then Return False

            Try
                Program.Log("Import: file picker opened by " & openedBy)
                Dim chosen = HelpDeskAttachmentPickers.ForCurrentSession().Pick(Me)
                If chosen Is Nothing Then
                    Program.Log("Import: file picker returned nothing")
                    Return False
                End If
                Program.Log("Import: file picker returned " & chosen.FileName & ", " &
                            If(chosen.FileData Is Nothing, "no data", chosen.FileData.Length.ToString(CultureInfo.InvariantCulture) & " bytes"))

                Return LoadFile(chosen.FileName, chosen.FileData)
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.ChooseFile")
                Program.Log("Import: choosing the file failed - " & ex.Message)
                MessageBox.Show(Me, "The file could not be read: " & ex.Message,
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Loads a file's bytes as the file - from the picker, or from a template's stored copy.
        ''' One path, so a file that came from a template is on screen exactly as an upload leaves
        ''' it: the grid, the headings tick, the delimiter, the summary line.
        ''' </summary>
        Private Function LoadFile(fileName As String, data As Byte()) As Boolean
            If imported Then Return False

            Try
                sourceFileName = fileName
                sourceData = data
                fileTextBox.Text = fileName

                ' A new file starts from the reader's own judgement: the delimiter detected, and
                ' the headings tick on the guess - unless a template is chosen, which knows what
                ' the last file of this shape needed. Whatever was set for the last file says
                ' nothing about this one.
                Dim template = ChosenTemplate()
                settingSourceOptions = True
                Try
                    delimiterComboBox.SelectedIndex = 0
                    If Not ImportSourceFormat.IsJson(sourceFileName) Then
                        Dim fromTemplate = If(template Is Nothing, Nothing,
                                              EmployeeImportPlan.TemplateHasHeaderRow(Convert.ToString(template("MappingData"), CultureInfo.InvariantCulture)))
                        headerCheckBox.Checked = If(fromTemplate.HasValue, fromTemplate.Value,
                                                    ImportCsvReader.FirstRowLooksLikeHeadings(sourceData))
                    End If
                Finally
                    settingSourceOptions = False
                End Try

                ' The columns the mapping on screen reads, before the file replaces them - whether
                ' it came from a template or was made by hand, and edits included. The file keeps
                ' that mapping; what the file does not have is said, not silently dropped.
                Dim expected = plan.Mappings.Where(Function(m) m.SourceColumn <> String.Empty).
                                             Select(Function(m) m.SourceColumn).
                                             Distinct(StringComparer.OrdinalIgnoreCase).ToList()

                ReadSource()

                ' The file's columns take over from the template's - but only a file that read. One
                ' that failed leaves the template's columns and mapping as they were.
                If FileLoaded() Then templateHeadings = Nothing
                Program.Log("Import: read " & If(source Is Nothing, "nothing",
                            If(source.Loaded,
                               source.RowCount.ToString(CultureInfo.InvariantCulture) & " rows, " &
                               source.Rows.Columns.Count.ToString(CultureInfo.InvariantCulture) & " columns",
                               "failed: " & source.Problem)) &
                            "; grid shows " & sourceGrid.Rows.Count.ToString(CultureInfo.InvariantCulture) & " rows")

                ' The page stays here: the one look that matters on this tab is whether the file
                ' was read right (Glenn, 2026-09-24). The mapping was laid when the template was
                ' chosen, and may since have been changed on Map Fields - it is not laid again.
                If FileLoaded() AndAlso expected.Count > 0 Then
                    Dim headings As New HashSet(Of String)(SourceHeadings(), StringComparer.OrdinalIgnoreCase)
                    Dim missing = expected.Where(Function(h) Not headings.Contains(h)).ToList()
                    Dim mapped = Enumerable.Count(plan.Mappings, Function(m) m.IsUsed)

                    sourceSummaryLabel.Text &= "  " & If(template Is Nothing, "The mapping",
                                                         "Saved Import '" & Convert.ToString(template("ImportName"), CultureInfo.InvariantCulture) & "'") &
                                               " maps " & Plural(mapped, "field") &
                                               If(missing.Count > 0, "; " & Plural(missing.Count, "column") & " it reads are not in this file", String.Empty) & "."

                    If missing.Count > 0 Then
                        MessageBox.Show(Me,
                                        "This file has no column called:" & Environment.NewLine & Environment.NewLine &
                                        String.Join(Environment.NewLine, missing) &
                                        Environment.NewLine & Environment.NewLine &
                                        "The fields those fed are left unmapped. Map them by hand or leave them empty.",
                                        "Mapping Applied With Gaps", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    End If
                    Program.Log("Import: mapping kept, " & mapped.ToString(CultureInfo.InvariantCulture) & " fields; " &
                                missing.Count.ToString(CultureInfo.InvariantCulture) & " columns missing")
                End If

                Return source IsNot Nothing AndAlso source.Loaded
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.LoadFile")
                Program.Log("Import: loading the file failed - " & ex.Message)
                MessageBox.Show(Me, "The file could not be read: " & ex.Message,
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End Try
        End Function

        Private Sub SourceOptions_Changed(sender As Object, e As EventArgs)
            If settingSourceOptions Then Return
            If sourceData IsNot Nothing AndAlso Not imported Then ReadSource()
        End Sub

        Private Function ChosenDelimiter() As String
            Select Case delimiterComboBox.SelectedIndex
                Case 1 : Return ","
                Case 2 : Return ";"
                Case 3 : Return vbTab
                Case 4 : Return "|"
                Case Else : Return String.Empty
            End Select
        End Function

        ''' <summary>
        ''' Reads the held bytes with the chosen options. Pairs whose column is gone are unmapped.
        ''' The template chosen on Source stays chosen: re-reading with another delimiter is the
        ''' same file, and the mapping made from it is kept rather than applied over again.
        ''' </summary>
        Private Sub ReadSource()
            Dim isJson = ImportSourceFormat.IsJson(sourceFileName)
            source = ImportSourceFormat.Read(sourceFileName, sourceData, isJson OrElse headerCheckBox.Checked, ChosenDelimiter())

            ' Shown for a CSV even when it failed to read - another delimiter may be the fix.
            headerCheckBox.Visible = Not isJson
            delimiterLabel.Visible = Not isJson
            delimiterComboBox.Visible = Not isJson

            sourceGrid.DataSource = Nothing
            mapPreviewGrid.DataSource = Nothing
            plan.Rows.Clear()
            mappingChangedSinceRows = True

            If Not source.Loaded Then
                sourceSummaryLabel.Text = source.Problem
                sourceSummaryLabel.ForeColor = Color.Firebrick
                toMapButton.Enabled = False
                RefreshMapping()
                Return
            End If

            sourceGrid.DataSource = source.Rows
            mapPreviewGrid.DataSource = source.Rows
            For Each grid In {sourceGrid, mapPreviewGrid}
                For Each column As DataGridViewColumn In grid.Columns
                    column.SortMode = DataGridViewColumnSortMode.NotSortable
                    column.Width = 130
                Next
            Next

            Dim details = If(isJson,
                             "JSON",
                             "CSV, " & DescribeDelimiter(source.Delimiter) & ", " & source.EncodingName)
            sourceSummaryLabel.ForeColor = Color.FromArgb(90, 90, 90)
            sourceSummaryLabel.Text = Plural(source.RowCount, "row") & " and " &
                                      Plural(source.Rows.Columns.Count, "column") & " (" & details & ")."
            toMapButton.Enabled = source.RowCount > 0

            plan.DropMissingColumns(SourceHeadings())
            RefreshMapping()
        End Sub

        Private Shared Function DescribeDelimiter(delimiter As String) As String
            Select Case delimiter
                Case "," : Return "comma-separated"
                Case ";" : Return "semicolon-separated"
                Case "Tab" : Return "tab-separated"
                Case "|" : Return "pipe-separated"
                Case Else : Return "separated by '" & delimiter & "'"
            End Select
        End Function

        ''' <summary>
        ''' The file's columns: the loaded file's, or - a template chosen with no file - the ones
        ''' the template recorded, so its mapping can be changed before the file arrives.
        ''' </summary>
        Private Function SourceHeadings() As List(Of String)
            If FileLoaded() Then Return source.Rows.Columns.Cast(Of DataColumn)().Select(Function(c) c.ColumnName).ToList()
            If templateHeadings IsNot Nothing Then Return New List(Of String)(templateHeadings)
            Return New List(Of String)()
        End Function

        Private Function FileLoaded() As Boolean
            Return source IsNot Nothing AndAlso source.Loaded
        End Function

        Private Shared Function Plural(count As Integer, noun As String) As String
            Return count.ToString("N0", CultureInfo.CurrentCulture) & " " & noun & If(count = 1, String.Empty, "s")
        End Function

#End Region

#Region "Map"

        ''' <summary>A list entry: what it says, and what it stands for.</summary>
        Private NotInheritable Class PickItem
            Public Property Text As String = String.Empty
            Public Property Mapping As ImportFieldMapping
            Public Property Heading As String = String.Empty
            Public Property IsDefaultEntry As Boolean

            Public Overrides Function ToString() As String
                Return Text
            End Function
        End Class

        Private Const DefaultEntryText As String = "Enter a Default Value..."

        Private Function ChosenTargetTable() As String
            Return If(targetTableComboBox.SelectedIndex = 1, ImportTargetSchema.UsersTable, ImportTargetSchema.EmployeesTable)
        End Function

        ''' <summary>Redraws all three panes from the plan.</summary>
        Private Sub RefreshMapping()
            RefreshTargetList()
            RefreshSourceList()
            RefreshMappedGrid()
            UpdateMapSummary()
        End Sub

        ''' <summary>
        ''' The chosen table's fields, in alphabetical order, mapped or not. Required ones carry a
        ''' star, which is what Next will ask about; mapped ones say so.
        ''' </summary>
        Private Sub RefreshTargetList()
            Dim keep = TryCast(targetList.SelectedItem, PickItem)?.Mapping
            Dim keepIndex = targetList.SelectedIndex

            targetList.BeginUpdate()
            Try
                targetList.Items.Clear()
                Dim table = ChosenTargetTable()
                Dim fields = plan.Mappings.Where(Function(m) String.Equals(m.Target.TableName, table, StringComparison.OrdinalIgnoreCase)).
                                           OrderBy(Function(m) m.Target.DisplayName, StringComparer.CurrentCultureIgnoreCase).
                                           ToList()

                ' Every field, mapped or not - a mapped one stays and says so, the way a used
                ' column does on the right. Mapping it again replaces what it had.
                For Each mapping In fields
                    Dim target = mapping.Target
                    Dim text = If(target.IsRequired, "* ", "   ") & target.DisplayName &
                               If(mapping.IsUsed, "   (mapped)",
                                  If(target.GeneratedWhenBlank, "   (made if blank)", String.Empty))
                    targetList.Items.Add(New PickItem With {.Text = text, .Mapping = mapping})
                Next

                ' Stay where the user was, so mapping down a list is click-click-Map, repeatedly.
                Dim same = targetList.Items.Cast(Of PickItem)().FirstOrDefault(Function(i) i.Mapping Is keep)
                If same IsNot Nothing Then
                    targetList.SelectedItem = same
                ElseIf targetList.Items.Count > 0 AndAlso keepIndex >= 0 Then
                    targetList.SelectedIndex = Math.Min(keepIndex, targetList.Items.Count - 1)
                End If
            Finally
                targetList.EndUpdate()
            End Try
        End Sub

        ''' <summary>
        ''' The default entry, then every column in the file in alphabetical order - the file's own
        ''' order is in the preview above. A used column stays and says so: one
        ''' column can feed two fields - Email to the employee and to the login - and a column that
        ''' vanished once used could never be mapped the second time.
        ''' </summary>
        Private Sub RefreshSourceList()
            Dim keepHeading = TryCast(sourceList.SelectedItem, PickItem)?.Heading
            Dim keepDefault = TryCast(sourceList.SelectedItem, PickItem)?.IsDefaultEntry

            sourceList.BeginUpdate()
            Try
                sourceList.Items.Clear()
                sourceList.Items.Add(New PickItem With {.Text = DefaultEntryText, .IsDefaultEntry = True})

                Dim used As New HashSet(Of String)(plan.Mappings.Where(Function(m) m.SourceColumn <> String.Empty).
                                                                 Select(Function(m) m.SourceColumn),
                                                   StringComparer.OrdinalIgnoreCase)
                For Each heading In SourceHeadings().OrderBy(Function(h) h, StringComparer.CurrentCultureIgnoreCase)
                    sourceList.Items.Add(New PickItem With {
                        .Text = heading & If(used.Contains(heading), "   (used)", String.Empty),
                        .Heading = heading
                    })
                Next

                Dim same = sourceList.Items.Cast(Of PickItem)().
                                      FirstOrDefault(Function(i) (keepDefault.GetValueOrDefault() AndAlso i.IsDefaultEntry) OrElse
                                                                 (Not i.IsDefaultEntry AndAlso keepHeading IsNot Nothing AndAlso
                                                                  String.Equals(i.Heading, keepHeading, StringComparison.OrdinalIgnoreCase)))
                If same IsNot Nothing Then sourceList.SelectedItem = same
            Finally
                sourceList.EndUpdate()
            End Try
        End Sub

        ''' <summary>The pairs made so far, employee first, in the table's own column order.</summary>
        Private Sub RefreshMappedGrid()
            suppressGridEvents = True
            Try
                mappedGrid.Rows.Clear()
                Dim doubtful = DoubtfulPairs()
                For Each mapping In plan.UsedMappings()
                    Dim from = If(mapping.SourceColumn <> String.Empty, mapping.SourceColumn, "(default)")
                    Dim index = mappedGrid.Rows.Add(EmployeeImportPlan.Caption(mapping.Target), from, mapping.DefaultValue, SampleFor(mapping))
                    Dim row = mappedGrid.Rows(index)
                    row.Tag = mapping
                    If mapping.Target.IsRequired Then row.Cells("Field").Style.Font = New Font(mappedGrid.Font, FontStyle.Bold)

                    ' A column that names another field, kept on purpose: still marked, so the
                    ' choice stays in sight rather than being made once and forgotten.
                    Dim current = mapping
                    Dim doubt = doubtful.FirstOrDefault(Function(p) p.Mapping Is current)
                    If doubt.Named IsNot Nothing Then
                        row.Cells("From").Style.BackColor = Color.FromArgb(255, 235, 190)
                        row.Cells("From").ToolTipText = "'" & mapping.SourceColumn & "' looks like it is for " & EmployeeImportPlan.Caption(doubt.Named)
                    End If

                    Dim sample = Convert.ToString(row.Cells("Sample").Value, CultureInfo.InvariantCulture)
                    If sample.Contains("   (") AndAlso Not mapping.Target.GeneratedWhenBlank Then
                        row.Cells("Sample").Style.ForeColor = Color.Firebrick
                    End If
                Next
            Finally
                suppressGridEvents = False
            End Try
        End Sub

        ''' <summary>What the first row of the file becomes in this field, and why not when it cannot.</summary>
        Private Function SampleFor(mapping As ImportFieldMapping) As String
            Dim sample = String.Empty
            If source IsNot Nothing AndAlso source.Loaded AndAlso source.RowCount > 0 AndAlso
               mapping.SourceColumn <> String.Empty AndAlso source.Rows.Columns.Contains(mapping.SourceColumn) Then
                sample = Convert.ToString(source.Rows.Rows(0)(mapping.SourceColumn), CultureInfo.InvariantCulture).Trim()
            End If
            If sample = String.Empty Then sample = mapping.DefaultValue

            ' A User Name pattern shows what it makes for the first person, not its braces.
            If IsUserNameTarget(mapping.Target) AndAlso ImportRules.IsUserNamePattern(sample) Then
                Dim person = FirstRowPerson()
                Dim made = ImportRules.ExpandUserNamePattern(sample, person.First, person.Last, person.Email)
                Return If(made = String.Empty, "(made from the name)", made) & If(person.IsExample, "  (example)", String.Empty)
            End If

            If sample <> String.Empty AndAlso Not mapping.Target.GeneratedWhenBlank Then
                Dim converted = ImportRules.ConvertValue(sample, mapping.Target.SqlType, mapping.Target.MaxLength)
                If converted.Problem <> String.Empty Then
                    sample &= "   (" & converted.Problem & ")"
                ElseIf TypeOf converted.Value Is Date Then
                    sample = CDate(converted.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                End If
            ElseIf sample = String.Empty AndAlso mapping.Target.GeneratedWhenBlank Then
                sample = If(String.Equals(mapping.Target.Name, "Password", StringComparison.OrdinalIgnoreCase),
                            "(a 6-digit PIN is made)", "(made from the name)")
            End If

            Return sample
        End Function

        Private Sub UpdateMapSummary()
            Dim used = plan.Mappings.Where(Function(m) m.IsUsed).ToList()
            Dim missing = plan.UnfilledRequiredTargets()
            Dim fromFile = used.Where(Function(m) m.SourceColumn <> String.Empty).Select(Function(m) m.SourceColumn).
                                Distinct(StringComparer.OrdinalIgnoreCase).Count()
            Dim totalHeadings = SourceHeadings().Count

            Dim text = Plural(used.Count, "field") & " mapped; " &
                       fromFile.ToString(CultureInfo.CurrentCulture) & " of " &
                       totalHeadings.ToString(CultureInfo.CurrentCulture) & " file columns used."
            If missing.Count > 0 Then
                text &= "  Required and not mapped: " & String.Join(", ", missing.Select(Function(t) EmployeeImportPlan.Caption(t))) & "."
                mapSummaryLabel.ForeColor = Color.Firebrick
            Else
                mapSummaryLabel.ForeColor = Color.FromArgb(60, 60, 60)
            End If
            mapSummaryLabel.Text = text
        End Sub

        ''' <summary>The value box is live only while the default entry is the thing chosen.</summary>
        ''' <summary>
        ''' The default box is open whenever a field is chosen on the left. With the default entry
        ''' on the right it is the whole value; with a file column it fills that column's blank
        ''' cells - so a column and its default are mapped in one go, which is what somebody
        ''' reaching for "User Name, and a default for the blanks" expects (Glenn, 2026-09-24).
        ''' </summary>
        Private Sub SourceList_SelectedIndexChanged(sender As Object, e As EventArgs)
            UpdateDefaultBox()
            Dim picked = TryCast(sourceList.SelectedItem, PickItem)
            If picked IsNot Nothing AndAlso picked.IsDefaultEntry AndAlso Not highlightingPair Then defaultValueTextBox.Focus()
            UpdateBecomes()
        End Sub

        Private Sub UpdateDefaultBox()
            Dim picked = TryCast(sourceList.SelectedItem, PickItem)
            Dim open = SelectedTarget() IsNot Nothing AndAlso picked IsNot Nothing
            defaultValueLabel.Enabled = open
            defaultValueTextBox.Enabled = open
            defaultValueLabel.Text = If(picked IsNot Nothing AndAlso Not picked.IsDefaultEntry,
                                        "Default, where the file is blank", "Default value")
        End Sub

        ''' <summary>The field selected on the left, or Nothing.</summary>
        Private Function SelectedTarget() As ImportTargetColumn
            Return TryCast(targetList.SelectedItem, PickItem)?.Mapping?.Target
        End Function

        Private Shared Function IsUserNameTarget(target As ImportTargetColumn) As Boolean
            Return target IsNot Nothing AndAlso
                   String.Equals(target.Key, EmployeeImportPlan.UserNameKey, StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' The choices for the field selected on the left, as links under the default box. Only
        ''' what the field can take: a date gets Today and Now, a time Now, a yes/no field Yes and
        ''' No, User Name its patterns; plain text gets none, because anything goes there.
        ''' </summary>
        Private Sub RefreshDefaultChoices()
            choicesPanel.SuspendLayout()
            Try
                choicesPanel.Controls.Clear()

                Dim target = SelectedTarget()
                Dim choices As New List(Of String)()
                If IsUserNameTarget(target) Then
                    choices.AddRange({"{F}{Last}", "{First}.{Last}", "{First}{L}", "{Email}", "{EmailName}"})
                ElseIf target IsNot Nothing Then
                    Select Case target.SqlType.ToLowerInvariant()
                        Case "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset" : choices.AddRange({"Today", "Now"})
                        Case "time" : choices.Add("Now")
                        Case "bit" : choices.AddRange({"Yes", "No"})
                    End Select
                End If

                If choices.Count = 0 Then Return

                ' Named, because the choices follow the field selected on the LEFT, and a column of
                ' the same name selected on the right read as the one being offered for (2026-09-24).
                choicesPanel.Controls.Add(New Label With {
                    .Text = "For " & EmployeeImportPlan.Caption(target) & ":",
                    .AutoSize = True,
                    .Margin = New Padding(0, 3, 2, 0),
                    .Font = New Font(Font, FontStyle.Bold)
                })
                For Each choice In choices
                    Dim link As New LinkLabel With {.Text = choice, .AutoSize = True, .Margin = New Padding(2, 3, 2, 0)}
                    Dim value = choice
                    AddHandler link.LinkClicked, Sub(sender, e) UseDefaultChoice(value)
                    choicesPanel.Controls.Add(link)
                Next

                If IsUserNameTarget(target) Then
                    Dim more As New LinkLabel With {.Text = "more...", .AutoSize = True, .Margin = New Padding(2, 3, 2, 0)}
                    AddHandler more.LinkClicked, Sub(sender, e) ShowUserNameTokens()
                    choicesPanel.Controls.Add(more)
                End If
            Finally
                choicesPanel.ResumeLayout()
            End Try

            UpdateBecomes()
        End Sub

        ''' <summary>
        ''' A choice clicked is mapped at once, and shows in the middle grid with what the first row
        ''' becomes. It used to fill the box and wait for Map, and a choice clicked but never mapped
        ''' was dropped without a word - a User Name pattern then imported as names made from the
        ''' person's name instead (Glenn, 2026-09-24). A choice is complete as it stands; a value
        ''' typed, or a pattern built with "more...", still waits for Map or Enter.
        '''
        ''' A file column already chosen on the right stays chosen - the choice is then the default
        ''' for its blanks. With nothing chosen there, the default entry is picked, and the choice is
        ''' the whole value.
        ''' </summary>
        Private Sub UseDefaultChoice(value As String)
            If sourceList.SelectedItem Is Nothing Then
                Dim entry = sourceList.Items.Cast(Of PickItem)().FirstOrDefault(Function(i) i.IsDefaultEntry)
                If entry IsNot Nothing Then sourceList.SelectedItem = entry
            End If
            defaultValueTextBox.Text = value
            MapSelected()
        End Sub

        Private Sub ShowUserNameTokens()
            MessageBox.Show(Me,
                            "A User Name default can be a pattern. Each {token} is replaced for every person:" &
                            Environment.NewLine & Environment.NewLine &
                            String.Join(Environment.NewLine, ImportRules.UserNameTokens.Select(Function(t) t.Token & "   " & t.Meaning)) &
                            Environment.NewLine & Environment.NewLine &
                            "Anything outside the braces is kept as typed: sar-{F}{Last} gives sar-bohare. " &
                            "Accents, apostrophes and spaces are taken out of names, and a name already in use is numbered.",
                            "User Name Patterns", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        ''' <summary>
        ''' What the value in the default box becomes, shown under it as it is typed: for the file's
        ''' first row, or for an example person when no file is loaded.
        ''' </summary>
        Private Sub UpdateBecomes()
            Dim target = SelectedTarget()
            Dim text = defaultValueTextBox.Text.Trim()
            If target Is Nothing OrElse Not defaultValueTextBox.Enabled OrElse text = String.Empty Then
                becomesLabel.Text = String.Empty
                Return
            End If

            becomesLabel.ForeColor = Color.FromArgb(60, 60, 60)
            If IsUserNameTarget(target) Then
                If Not ImportRules.IsUserNamePattern(text) Then
                    becomesLabel.Text = "Becomes: " & text
                    Return
                End If

                Dim sample = FirstRowPerson()
                Dim result = ImportRules.ExpandUserNamePattern(text, sample.First, sample.Last, sample.Email)
                becomesLabel.Text = "Becomes: " & If(result = String.Empty, "(made from the name)", result) &
                                    If(sample.IsExample, "  (example)", String.Empty)
                Return
            End If

            Dim converted = ImportRules.ConvertValue(text, target.SqlType, target.MaxLength)
            If converted.Problem <> String.Empty Then
                becomesLabel.ForeColor = Color.Firebrick
                becomesLabel.Text = converted.Problem
            ElseIf TypeOf converted.Value Is Date Then
                becomesLabel.Text = "Becomes: " & CDate(converted.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ElseIf TypeOf converted.Value Is Boolean Then
                becomesLabel.Text = "Becomes: " & If(CBool(converted.Value), "Yes", "No")
            Else
                becomesLabel.Text = "Becomes: " & Convert.ToString(converted.Value, CultureInfo.InvariantCulture)
            End If
        End Sub

        ''' <summary>
        ''' The first person in the file, as far as the mapping says who they are - or Bob O'Hare,
        ''' when there is no file or the names are not mapped yet.
        ''' </summary>
        Private Function FirstRowPerson() As (First As String, Last As String, Email As String, IsExample As Boolean)
            Dim first = FirstRowValue(ImportTargetSchema.EmployeesTable & ".FirstName")
            Dim last = FirstRowValue(ImportTargetSchema.EmployeesTable & ".LastName")
            Dim email = FirstRowValue(ImportTargetSchema.EmployeesTable & ".Email")
            If email = String.Empty Then email = FirstRowValue(ImportTargetSchema.UsersTable & ".Email")

            If first = String.Empty AndAlso last = String.Empty Then
                Return ("Bob", "O'Hare", "bob.ohare@example.com", True)
            End If
            Return (first, last, email, False)
        End Function

        Private Function FirstRowValue(key As String) As String
            Dim mapping = plan.MappingFor(key)
            If mapping Is Nothing Then Return String.Empty

            If FileLoaded() AndAlso source.RowCount > 0 AndAlso mapping.SourceColumn <> String.Empty AndAlso
               source.Rows.Columns.Contains(mapping.SourceColumn) Then
                Dim value = Convert.ToString(source.Rows.Rows(0)(mapping.SourceColumn), CultureInfo.InvariantCulture).Trim()
                If value <> String.Empty Then Return value
            End If
            Return If(mapping.DefaultValue, String.Empty).Trim()
        End Function

        ''' <summary>Picking a pair in the middle shows both its halves.</summary>
        Private Sub MappedGrid_CellClick(sender As Object, e As DataGridViewCellEventArgs)
            If suppressGridEvents OrElse e.RowIndex < 0 Then Return

            Dim mapping = TryCast(mappedGrid.Rows(e.RowIndex).Tag, ImportFieldMapping)
            If mapping IsNot Nothing Then HighlightPair(mapping)
        End Sub

        ''' <summary>
        ''' Selects the pair's field on the left - switching Goes To to its table if need be - and
        ''' its column on the right, or the default entry with the value in the box beneath. The
        ''' focus stays in the middle, where the user clicked.
        ''' </summary>
        Private Sub HighlightPair(mapping As ImportFieldMapping)
            highlightingPair = True
            Try
                Dim tableIndex = If(String.Equals(mapping.Target.TableName, ImportTargetSchema.UsersTable, StringComparison.OrdinalIgnoreCase), 1, 0)
                If targetTableComboBox.SelectedIndex <> tableIndex Then targetTableComboBox.SelectedIndex = tableIndex

                targetList.SelectedItem = targetList.Items.Cast(Of PickItem)().FirstOrDefault(Function(i) i.Mapping Is mapping)

                If mapping.SourceColumn <> String.Empty Then
                    sourceList.SelectedItem = sourceList.Items.Cast(Of PickItem)().
                                                        FirstOrDefault(Function(i) Not i.IsDefaultEntry AndAlso
                                                                                   String.Equals(i.Heading, mapping.SourceColumn, StringComparison.OrdinalIgnoreCase))
                    defaultValueTextBox.Text = mapping.DefaultValue
                Else
                    sourceList.SelectedItem = sourceList.Items.Cast(Of PickItem)().FirstOrDefault(Function(i) i.IsDefaultEntry)
                    defaultValueTextBox.Text = mapping.DefaultValue
                End If
            Finally
                highlightingPair = False
            End Try
        End Sub

        Private Sub DefaultValueTextBox_KeyDown(sender As Object, e As KeyEventArgs)
            If e.KeyCode = Keys.Enter Then
                e.SuppressKeyPress = True
                MapSelected()
            End If
        End Sub

        ''' <summary>
        ''' Pairs the field chosen on the left with the column - or the default - chosen on the
        ''' right. The one path for the Map button and a double-click on either list.
        ''' </summary>
        Private Sub MapSelected()
            Dim target = TryCast(targetList.SelectedItem, PickItem)
            Dim chosen = TryCast(sourceList.SelectedItem, PickItem)

            If target Is Nothing Then
                MessageBox.Show(Me, "Choose a field on the left to map.", "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                targetList.Focus()
                Return
            End If
            If chosen Is Nothing Then
                MessageBox.Show(Me, "Choose a column from the file on the right, or Enter a Default Value.",
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                sourceList.Focus()
                Return
            End If

            ' Asked before anything changes: a field mapped for the first time goes to the end of
            ' the middle grid, and one mapped again keeps its place.
            Dim wasMapped = target.Mapping.IsUsed

            If chosen.IsDefaultEntry Then
                Dim value = defaultValueTextBox.Text.Trim()
                If value = String.Empty Then
                    MessageBox.Show(Me, "Type the default value in the box under the list, then Map.",
                                    "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    defaultValueTextBox.Focus()
                    Return
                End If
                target.Mapping.SourceColumn = String.Empty
                target.Mapping.DefaultValue = value
            Else
                ' A heading that plainly names another field - "User Name" being mapped to Email -
                ' is asked about first. On 2026-09-24 exactly that pairing was made without anyone
                ' meaning to, and every row came back "not an email address". Only an exact match
                ' of the letters and digits counts: this is a question, not the automatic matching
                ' Glenn ruled out, and a heading that names nothing is never questioned.
                Dim namesField = HeadingNamesOtherField(chosen.Heading, target.Mapping.Target)
                If namesField IsNot Nothing AndAlso
                   MessageBox.Show(Me,
                                   "The column '" & chosen.Heading & "' looks like it is for " & EmployeeImportPlan.Caption(namesField) &
                                   "." & Environment.NewLine & Environment.NewLine &
                                   "Map it to " & EmployeeImportPlan.Caption(target.Mapping.Target) & " anyway?",
                                   "Import Employees", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                    Return
                End If

                ' The column, and whatever is in the box as the default for its blank cells -
                ' empty for none.
                target.Mapping.SourceColumn = chosen.Heading
                target.Mapping.DefaultValue = defaultValueTextBox.Text.Trim()
            End If
            If Not wasMapped Then plan.PlaceLast(target.Mapping)
            defaultValueTextBox.Clear()

            mappingChangedSinceRows = True
            RefreshMapping()
            SelectMappedRow(target.Mapping)
        End Sub

        ''' <summary>
        ''' The field a heading names, when it names one other than the target - compared as letters
        ''' and digits only, so "User Name", "user_name" and "USERNAME" all name User Name. The
        ''' target's own name, and the same field on the other table (Email on the login as well as
        ''' the employee), never count against it. Nothing when the heading names no field at all.
        ''' </summary>
        Private Function HeadingNamesOtherField(heading As String, target As ImportTargetColumn) As ImportTargetColumn
            Dim key = ImportRules.LettersAndDigits(heading)
            If key = String.Empty Then Return Nothing

            Dim named = plan.Targets.Where(Function(t) ImportRules.LettersAndDigits(t.Name) = key OrElse
                                                       ImportRules.LettersAndDigits(t.FormattedName) = key OrElse
                                                       ImportRules.LettersAndDigits(t.DisplayName) = key).ToList()
            If named.Count = 0 Then Return Nothing
            If named.Any(Function(t) String.Equals(t.Name, target.Name, StringComparison.OrdinalIgnoreCase)) Then Return Nothing
            Return named(0)
        End Function

        ''' <summary>
        ''' Takes the selected pair apart; the field stays selected on the left, no longer marked
        ''' mapped, with the list switched to its table so it is in view.
        ''' </summary>
        Private Sub RemoveSelectedMapping()
            If mappedGrid.CurrentRow Is Nothing Then
                MessageBox.Show(Me, "Choose a mapped field in the middle to remove.", "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim mapping = TryCast(mappedGrid.CurrentRow.Tag, ImportFieldMapping)
            If mapping Is Nothing Then Return

            mapping.SourceColumn = String.Empty
            mapping.DefaultValue = String.Empty
            mappingChangedSinceRows = True

            targetTableComboBox.SelectedIndex = If(String.Equals(mapping.Target.TableName, ImportTargetSchema.UsersTable,
                                                                 StringComparison.OrdinalIgnoreCase), 1, 0)
            RefreshMapping()
            targetList.SelectedItem = targetList.Items.Cast(Of PickItem)().FirstOrDefault(Function(i) i.Mapping Is mapping)
        End Sub

        ''' <summary>A new pair lands selected and in view, like any added row.</summary>
        Private Sub SelectMappedRow(mapping As ImportFieldMapping)
            Dim row = mappedGrid.Rows.Cast(Of DataGridViewRow)().FirstOrDefault(Function(r) r.Tag Is mapping)
            If row Is Nothing Then Return

            mappedGrid.CurrentCell = row.Cells("Field")
            mappedGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index - 2)
        End Sub

        ''' <summary>
        ''' The Default cell is the one thing edited in place: it fills a mapped column's blank
        ''' cells, or is the whole value for a field mapped to a default. Emptying it on the latter
        ''' unmaps the field, which leaves the middle list.
        ''' </summary>
        Private Sub MappedGrid_CellValueChanged(sender As Object, e As DataGridViewCellEventArgs)
            If suppressGridEvents OrElse e.RowIndex < 0 OrElse mappedGrid.Columns(e.ColumnIndex).Name <> "Default" Then Return

            Dim row = mappedGrid.Rows(e.RowIndex)
            Dim mapping = TryCast(row.Tag, ImportFieldMapping)
            If mapping Is Nothing Then Return

            mapping.DefaultValue = If(Convert.ToString(row.Cells("Default").Value, CultureInfo.InvariantCulture), String.Empty).Trim()
            mappingChangedSinceRows = True

            ' Redrawn after the edit has finished, not inside it - clearing the grid that is still
            ' committing the cell would throw.
            BeginInvoke(New Action(Sub()
                                       RefreshMapping()
                                       If mapping.IsUsed Then SelectMappedRow(mapping)
                                   End Sub))
        End Sub

        Private Sub ClearMappingButton_Click(sender As Object, e As EventArgs)
            For Each mapping In plan.Mappings
                mapping.SourceColumn = String.Empty
                mapping.DefaultValue = String.Empty
            Next
            mappingChangedSinceRows = True
            SelectTemplateQuietly(Nothing)
            RefreshMapping()
        End Sub

#End Region

#Region "Templates"

        Private Const NoTemplate As String = "Make a Selection"

        ''' <summary>Set while the page changes the template selection itself, so doing so does not apply it.</summary>
        Private settingTemplate As Boolean

        ''' <summary>
        ''' The chosen template's columns while there is no file - Nothing otherwise. While it is
        ''' set, Map Fields works on these instead of a file's, and Save keeps them.
        ''' </summary>
        Private templateHeadings As List(Of String)

        ''' <summary>The chosen template's headings setting, kept for a Save made with no file loaded.</summary>
        Private templateHasHeaderRow As Boolean = True

        ''' <summary>A Saved Import in the list: what it says, and its row.</summary>
        Private NotInheritable Class SavedImportItem
            Public Property Text As String = String.Empty
            Public Property Row As DataRow

            Public Overrides Function ToString() As String
                Return Text
            End Function
        End Class

        Private Shared Function RegistrationOf(row As DataRow) As Integer
            Return If(row Is Nothing, 0, Convert.ToInt32(row("RegistrationID"), CultureInfo.InvariantCulture))
        End Function

        ''' <summary>
        ''' Every Saved Import the session may use, into the list at the top of the Source tab,
        ''' with the one named for the current registration selected - quietly, because re-reading
        ''' the list is not choosing one. Somebody who can import into more than one registration
        ''' sees each name with its registration in brackets; everyone else sees names alone.
        ''' </summary>
        Private Sub LoadTemplates(Optional selectName As String = "")
            templates = Nothing
            settingTemplate = True
            Try
                sourceTemplateComboBox.Items.Clear()
                sourceTemplateComboBox.Items.Add(NoTemplate)

                Try
                    templates = SavedImportDataAccess.ListAccessible(EmployeeImportPlan.TargetTableName, accessProfile)
                    Dim showRegistration = registrationComboBox.Visible
                    For Each row As DataRow In templates.Rows
                        Dim name = Convert.ToString(row("ImportName"), CultureInfo.InvariantCulture)
                        If showRegistration Then name &= " (" & Convert.ToString(row("RegName"), CultureInfo.InvariantCulture) & ")"
                        sourceTemplateComboBox.Items.Add(New SavedImportItem With {.Text = name, .Row = row})
                    Next
                Catch ex As Exception
                    Telemetry.Error(ex, "FW_EmployeeImport.LoadTemplates")
                End Try

                SelectItemFor(FindTemplate(selectName))
            Finally
                settingTemplate = False
            End Try

            UpdateTemplateInfo()
        End Sub

        ''' <summary>Selects a Saved Import - or none, for Nothing - without applying it.</summary>
        Private Sub SelectTemplateQuietly(row As DataRow)
            settingTemplate = True
            Try
                SelectItemFor(row)
            Finally
                settingTemplate = False
            End Try
            UpdateTemplateInfo()
        End Sub

        Private Sub SelectItemFor(row As DataRow)
            Dim item = sourceTemplateComboBox.Items.OfType(Of SavedImportItem)().FirstOrDefault(Function(i) i.Row Is row)
            If item IsNot Nothing Then
                sourceTemplateComboBox.SelectedItem = item
            Else
                sourceTemplateComboBox.SelectedIndex = 0
            End If
            currentTemplateId = TemplateId(ChosenTemplate())
        End Sub

        ''' <summary>
        ''' A Saved Import by name in the registration being imported into - names are unique
        ''' within a registration, not across them.
        ''' </summary>
        Private Function FindTemplate(name As String) As DataRow
            If templates Is Nothing OrElse String.IsNullOrWhiteSpace(name) Then Return Nothing
            Dim registrationId = SelectedRegistrationId()
            Return templates.Rows.Cast(Of DataRow)().
                                  FirstOrDefault(Function(r) RegistrationOf(r) = registrationId AndAlso
                                                             String.Equals(Convert.ToString(r("ImportName"), CultureInfo.InvariantCulture),
                                                                           name.Trim(), StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>The Saved Import chosen on the Source tab, or Nothing.</summary>
        Private Function ChosenTemplate() As DataRow
            Return TryCast(sourceTemplateComboBox.SelectedItem, SavedImportItem)?.Row
        End Function

        Private Shared Function TemplateId(row As DataRow) As Integer
            Return If(row Is Nothing, 0, Convert.ToInt32(row("SavedImportID"), CultureInfo.InvariantCulture))
        End Function

        ''' <summary>When the chosen template was last used and how often - what decides whether it is worth keeping.</summary>
        Private Sub UpdateTemplateInfo()
            Dim row = ChosenTemplate()
            If row Is Nothing Then
                templateInfoLabel.Text = If(sourceTemplateComboBox.Items.Count > 1, String.Empty, "none saved yet")
                Return
            End If

            Dim uses = Convert.ToInt32(row("UseCount"), CultureInfo.InvariantCulture)
            templateInfoLabel.Text = If(IsDBNull(row("LastUsedOn")),
                                        "never used",
                                        "last used " & CDate(row("LastUsedOn")).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &
                                        ", " & Plural(uses, "import"))
        End Sub

        ''' <summary>
        ''' Choosing a template. Its name goes into the Map Fields name box, so a Save there updates
        ''' it. With a file already read it is applied at once - after asking, when that would
        ''' replace pairs somebody has made.
        ''' </summary>
        Private Sub SourceTemplateComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If settingTemplate OrElse plan Is Nothing Then Return

            Dim row = ChosenTemplate()
            currentTemplateId = TemplateId(row)
            UpdateTemplateInfo()
            If row Is Nothing Then
                ' Back to no template, and no file: its columns and its mapping go with it.
                If Not FileLoaded() Then LeaveTemplateOnly()
                Return
            End If

            templateNameTextBox.Text = Convert.ToString(row("ImportName"), CultureInfo.InvariantCulture)
            If imported Then Return

            ' Its registration first, because the roles and the time zone below belong to it.
            Dim savedRegistration = RegistrationOf(row)
            If savedRegistration <> SelectedRegistrationId() AndAlso registrationComboBox.Visible Then
                applyingSavedImport = True
                Try
                    registrationComboBox.SelectedValue = savedRegistration
                Finally
                    applyingSavedImport = False
                End Try
            End If

            ' The role it was saved with, shown and changeable. One no longer offered - deleted,
            ' made inactive - leaves the box on Make a Selection rather than guessing another.
            Dim savedRole = EmployeeImportPlan.TemplateRoleId(Convert.ToString(row("MappingData"), CultureInfo.InvariantCulture))
            If savedRole > 0 Then
                Dim roles = TryCast(roleComboBox.DataSource, DataTable)
                If roles IsNot Nothing AndAlso roles.Rows.Cast(Of DataRow)().Any(Function(r) Convert.ToInt32(r("ID"), CultureInfo.InvariantCulture) = savedRole) Then
                    roleComboBox.SelectedValue = savedRole
                End If
            End If

            ' No file yet: the template's own file, stored when it was saved, is loaded straight
            ' into the grid - no picker - exactly as an upload would leave it, and the template
            ' laid over it (Glenn, 2026-09-24). A template saved before files were kept shows its
            ' columns as headings instead, and Choose... is still there.
            If Not FileLoaded() Then
                If LoadTemplateFile(row) Then Return
                ShowTemplateColumns(row)
                Return
            End If

            If plan.Mappings.Any(Function(m) m.IsUsed) AndAlso
               MessageBox.Show(Me, "Replace the current mapping with the Saved Import '" & templateNameTextBox.Text & "'?",
                               "Import Employees", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            ApplyChosenTemplate()
        End Sub

        ''' <summary>
        ''' The file columns a template expects, as a grid with headings and no rows, and a line
        ''' saying so. Only a picture of the file to come: nothing is mapped until it is chosen.
        ''' </summary>
        ''' <summary>
        ''' Loads the file stored with a template and lays the template over it. False when the
        ''' template has no stored file, or it could not be read - the caller shows the headings.
        ''' One round trip, and only here: the list of templates does not carry their files.
        ''' </summary>
        Private Function LoadTemplateFile(row As DataRow) As Boolean
            If row Is Nothing OrElse Not Convert.ToBoolean(row("HasSourceFile"), CultureInfo.InvariantCulture) Then Return False

            Try
                Dim stored = SavedImportDataAccess.GetSourceFile(TemplateId(row), RegistrationOf(row), accessProfile)
                If Not stored.HasValue Then Return False

                Program.Log("Import: template file " & stored.Value.FileName & ", " &
                            stored.Value.Data.Length.ToString(CultureInfo.InvariantCulture) & " bytes")

                ' Nothing mapped yet, so LoadFile keeps nothing; the template is laid on after.
                For Each mapping In plan.Mappings
                    mapping.SourceColumn = String.Empty
                    mapping.DefaultValue = String.Empty
                Next
                templateHeadings = Nothing

                If Not LoadFile(stored.Value.FileName, stored.Value.Data) Then Return False
                ApplyChosenTemplate()
                Return True
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.LoadTemplateFile")
                MessageBox.Show(Me, "The Saved Import's file could not be read: " & ex.Message,
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End Try
        End Function

        Private Sub ShowTemplateColumns(row As DataRow)
            Dim json = Convert.ToString(row("MappingData"), CultureInfo.InvariantCulture)
            templateHeadings = EmployeeImportPlan.TemplateSourceColumns(json)
            templateHasHeaderRow = If(EmployeeImportPlan.TemplateHasHeaderRow(json), True)

            Dim shape As New DataTable("TemplateColumns")
            For Each heading In templateHeadings
                shape.Columns.Add(heading, GetType(String))
            Next

            sourceGrid.DataSource = shape
            mapPreviewGrid.DataSource = shape
            For Each grid In {sourceGrid, mapPreviewGrid}
                For Each column As DataGridViewColumn In grid.Columns
                    column.SortMode = DataGridViewColumnSortMode.NotSortable
                    column.Width = 130
                Next
            Next

            ' The template's own mapping, over its own columns - ready to change on Map Fields.
            plan.ApplyJson(json, templateHeadings)
            mappingChangedSinceRows = True
            QuestionDoubtfulPairs()
            RefreshMapping()
            toMapButton.Enabled = True

            sourceSummaryLabel.ForeColor = Color.FromArgb(90, 90, 90)
            sourceSummaryLabel.Text = "Saved Import '" & Convert.ToString(row("ImportName"), CultureInfo.InvariantCulture) &
                                      "': " & Plural(templateHeadings.Count, "column") & ", " &
                                      Plural(Enumerable.Count(plan.Mappings, Function(m) m.IsUsed), "field") & " mapped. " &
                                      "Next opens the mapping to change it; choose the file to import."
        End Sub

        ''' <summary>
        ''' The pairs whose file column plainly names a different field - Email filled from "User
        ''' Name". Map asks about exactly this before making such a pair; a Saved Import brings its
        ''' pairs back without Map, and on 2026-09-24 one saved that way came back every time it
        ''' was chosen and turned four rows red. The same test as Map, by HeadingNamesOtherField.
        ''' </summary>
        Private Function DoubtfulPairs() As List(Of (Mapping As ImportFieldMapping, Named As ImportTargetColumn))
            Return plan.UsedMappings().
                        Where(Function(m) m.SourceColumn <> String.Empty).
                        Select(Function(m) (Mapping:=m, Named:=HeadingNamesOtherField(m.SourceColumn, m.Target))).
                        Where(Function(p) p.Named IsNot Nothing).ToList()
        End Function

        ''' <summary>
        ''' Asked once, when a Saved Import is applied: keep the doubtful pairs, or unmap them to be
        ''' mapped again. Kept ones stay marked in the middle grid, so the choice is still visible.
        ''' </summary>
        Private Sub QuestionDoubtfulPairs()
            Dim doubtful = DoubtfulPairs()
            If doubtful.Count = 0 Then Return

            Dim lines = doubtful.Select(Function(p) "   " & EmployeeImportPlan.Caption(p.Mapping.Target) & " is filled from the column '" &
                                                     p.Mapping.SourceColumn & "', which looks like it is for " &
                                                     EmployeeImportPlan.Caption(p.Named))
            If MessageBox.Show(Me,
                               "This Saved Import pairs:" & Environment.NewLine & Environment.NewLine &
                               String.Join(Environment.NewLine, lines) & Environment.NewLine & Environment.NewLine &
                               "Keep " & If(doubtful.Count = 1, "it", "them") & " as saved?" & Environment.NewLine &
                               "No unmaps " & If(doubtful.Count = 1, "it", "them") & ", to be mapped again on Map Fields.",
                               "Check The Saved Import", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                               MessageBoxDefaultButton.Button2) = DialogResult.Yes Then
                Return
            End If

            ' The column goes; a default typed for its blanks goes with it, because it was only
            ' ever the fallback for that column.
            For Each pair In doubtful
                pair.Mapping.SourceColumn = String.Empty
                pair.Mapping.DefaultValue = String.Empty
            Next
            mappingChangedSinceRows = True
        End Sub

        ''' <summary>Back to nothing chosen: no columns, no mapping, no Map Fields until something is.</summary>
        Private Sub LeaveTemplateOnly()
            templateHeadings = Nothing
            sourceGrid.DataSource = Nothing
            mapPreviewGrid.DataSource = Nothing
            For Each mapping In plan.Mappings
                mapping.SourceColumn = String.Empty
                mapping.DefaultValue = String.Empty
            Next
            mappingChangedSinceRows = True
            RefreshMapping()
            toMapButton.Enabled = False
            sourceSummaryLabel.Text = "No file chosen."
            sourceSummaryLabel.ForeColor = Color.FromArgb(90, 90, 90)
        End Sub

        ''' <summary>
        ''' Lays the chosen template's pairs and defaults over the file just read, and says which
        ''' of its headings this file does not have, rather than leaving those fields blank without
        ''' a word.
        ''' </summary>
        Private Sub ApplyChosenTemplate()
            Dim row = ChosenTemplate()
            If row Is Nothing Then Return

            Try
                Dim missing = plan.ApplyJson(Convert.ToString(row("MappingData"), CultureInfo.InvariantCulture), SourceHeadings())
                mappingChangedSinceRows = True
                QuestionDoubtfulPairs()
                RefreshMapping()

                ' Said on the Source tab, where the page stays, so it is plain the file is mapped
                ' without going to look.
                Dim mapped = Enumerable.Count(plan.Mappings, Function(m) m.IsUsed)
                sourceSummaryLabel.Text &= "  Saved Import '" & Convert.ToString(row("ImportName"), CultureInfo.InvariantCulture) &
                                           "' mapped " & Plural(mapped, "field") &
                                           If(missing.Count > 0, "; " & Plural(missing.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "column") & " it expects are not in this file", String.Empty) &
                                           "."

                If missing.Count > 0 Then
                    MessageBox.Show(Me,
                                    "This file has no column called:" & Environment.NewLine & Environment.NewLine &
                                    String.Join(Environment.NewLine, missing.Distinct(StringComparer.OrdinalIgnoreCase)) &
                                    Environment.NewLine & Environment.NewLine &
                                    "The fields those fed are left unmapped. Map them by hand or leave them empty.",
                                    "Saved Import Applied With Gaps", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.ApplyChosenTemplate")
                MessageBox.Show(Me, "The Saved Import could not be read: " & ex.Message, "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub

        ''' <summary>
        ''' Saves the mapping under the name in the box - the chosen template's name, to update it,
        ''' or a new one. Replacing an existing template is asked about first.
        ''' </summary>
        Private Sub SaveTemplateButton_Click(sender As Object, e As EventArgs)
            Dim name = templateNameTextBox.Text.Trim()
            If name = String.Empty Then
                MessageBox.Show(Me, "Type a name for the Saved Import, then Save.",
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                templateNameTextBox.Focus()
                Return
            End If

            If Not plan.Mappings.Any(Function(m) m.IsUsed) Then
                MessageBox.Show(Me, "Nothing is mapped yet, so there is nothing to save.",
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If FindTemplate(name) IsNot Nothing AndAlso
               MessageBox.Show(Me, "Replace the Saved Import '" & name & "' with this mapping?",
                               "Replace Saved Import", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            Try
                SavedImportDataAccess.Save(SelectedRegistrationId(), EmployeeImportPlan.TargetTableName, name,
                                              plan.MappingToJson(If(FileLoaded(), headerCheckBox.Checked, templateHasHeaderRow),
                                                                 SourceHeadings(), SelectedRoleId()), accessProfile,
                                              SessionState.ActingUserID,
                                              If(FileLoaded(), sourceFileName, Nothing),
                                              If(FileLoaded(), sourceData, Nothing))
                LoadTemplates(name)
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.SaveTemplateButton_Click")
                MessageBox.Show(Me, "The Saved Import could not be saved: " & ex.Message, "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub

        ''' <summary>Deletes the template named in the box, after saying which and when it was last used.</summary>
        Private Sub DeleteTemplateButton_Click(sender As Object, e As EventArgs)
            Dim row = FindTemplate(templateNameTextBox.Text)
            If row Is Nothing Then
                MessageBox.Show(Me, "There is no Saved Import called '" & templateNameTextBox.Text.Trim() & "'.",
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim name = Convert.ToString(row("ImportName"), CultureInfo.InvariantCulture)
            Dim lastUsed = If(IsDBNull(row("LastUsedOn")), "It has never been used.",
                              "It was last used " & CDate(row("LastUsedOn")).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) & ".")
            If MessageBox.Show(Me,
                               "Delete the Saved Import '" & name & "'?" & Environment.NewLine & Environment.NewLine &
                               lastUsed & " Nobody in this registration will be offered it again. " &
                               "The mapping on screen is left as it is.",
                               "Delete Saved Import", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                Return
            End If

            Try
                SavedImportDataAccess.Delete(TemplateId(row), RegistrationOf(row), accessProfile, SessionState.ActingUserID)
                templateNameTextBox.Clear()
                LoadTemplates()
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.DeleteTemplateButton_Click")
                MessageBox.Show(Me, "The Saved Import could not be deleted: " & ex.Message, "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub

#End Region

#Region "Check"

        ''' <summary>
        ''' Stops a tab being opened before it has anything to show, and runs the pre-check when
        ''' its tab is reached - by the Next button or the tab header alike.
        ''' </summary>
        Private Sub Tabs_Selecting(sender As Object, e As TabControlCancelEventArgs)
            If e.TabPage Is sourceTab OrElse plan Is Nothing Then Return

            ' Map Fields needs columns to map from: a file's, or a chosen template's - which is
            ' how a template's mapping is changed with no file loaded (Glenn, 2026-09-24). The
            ' role does not enter into a mapping, so it is not asked for here.
            If e.TabPage Is mapTab AndAlso Not imported Then
                If Not FileLoaded() AndAlso templateHeadings Is Nothing Then
                    e.Cancel = True
                    BeginInvoke(New Action(Sub()
                                               MessageBox.Show(Me, "Choose a file, or a Saved Import to change its mapping.",
                                                               "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                                           End Sub))
                End If
                Return
            End If

            ' The Check tab needs the whole Source tab - the file, the role and the time zone.
            ' Nothing can be checked without rows, and asking here rather than at Import is what
            ' stops a check being done for an import that then cannot start.
            If Not imported AndAlso MissingSourceFields().Count > 0 Then
                e.Cancel = True
                BeginInvoke(New Action(Sub() ConfirmSourceComplete()))
                Return
            End If

            If e.TabPage IsNot checkTab OrElse imported Then Return

            Dim missing = plan.UnfilledRequiredTargets()
            If missing.Count > 0 Then
                e.Cancel = True
                MessageBox.Show(Me,
                                "These fields are required and nothing fills them:" & Environment.NewLine & Environment.NewLine &
                                String.Join(Environment.NewLine, missing.Select(Function(t) EmployeeImportPlan.Caption(t))) &
                                Environment.NewLine & Environment.NewLine &
                                "Map each to a column in the file, or give it a default value.",
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If mappingChangedSinceRows OrElse plan.Rows.Count = 0 Then
                plan.BuildRows(source.Rows)
                mappingChangedSinceRows = False
                RunCheck()
            End If
        End Sub

        ''' <summary>
        ''' One query for every user name in use, then the check - which needs no database at all -
        ''' then one more for the duplicate warnings.
        ''' </summary>
        Private Sub RunCheck()
            Try
                Cursor = Cursors.WaitCursor
                plan.Check(DataAccess.GetAllUserNamesLower())
                RefreshDuplicateWarnings()
                SuggestBatchName()
                RefreshCheckGrid(rebuildColumns:=True)
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.RunCheck")
                MessageBox.Show(Me, "The check could not run: " & ex.Message, "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Finally
                Cursor = Cursors.Default
            End Try
        End Sub

        ''' <summary>
        ''' Marks the rows that look like somebody already in the registration, and notes any
        ''' import of this same file that has not been undone. Warnings only: a failure to read
        ''' them is logged and the check stands without them.
        ''' </summary>
        Private Sub RefreshDuplicateWarnings()
            earlierImports = New List(Of String)()
            Try
                Dim evidence = ImportBatchDataAccess.GetDuplicateEvidence(SelectedRegistrationId(),
                                                                          ImportBatchRequest.HashOf(sourceData), accessProfile)
                plan.FlagLikelyDuplicates(evidence.People)
                earlierImports = evidence.EarlierImports
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.RefreshDuplicateWarnings")
                plan.FlagLikelyDuplicates(Enumerable.Empty(Of (FirstName As String, LastName As String, Email As String))())
            End Try
        End Sub

        ''' <summary>
        ''' A starting name for the import, only while the box is empty: the Saved Import's name or
        ''' the file's, with today's date. The note is never suggested - saying what the import was
        ''' for is the point of asking.
        ''' </summary>
        Private Sub SuggestBatchName()
            If batchNameTextBox.Text.Trim() <> String.Empty Then Return

            Dim chosen = ChosenTemplate()
            Dim stem = If(chosen IsNot Nothing, Convert.ToString(chosen("ImportName"), CultureInfo.InvariantCulture), FileStem())
            Dim suggestion = stem & " - " & DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            batchNameTextBox.Text = If(suggestion.Length > 100, suggestion.Substring(0, 100), suggestion)
        End Sub

        Private Sub UpdateImportAvailability()
            If importButton Is Nothing OrElse plan Is Nothing Then Return
            importButton.Enabled = Not imported AndAlso plan.CanImport AndAlso
                                   batchNameTextBox.Text.Trim() <> String.Empty AndAlso
                                   batchNoteTextBox.Text.Trim() <> String.Empty
        End Sub

        Private Sub RefreshCheckGrid(rebuildColumns As Boolean)
            Dim active = plan.ActiveMappings()

            If rebuildColumns Then
                checkGrid.Columns.Clear()
                checkGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "RowNumber", .HeaderText = "Row", .Width = 45, .Frozen = True})
                checkGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Status", .HeaderText = "Status", .Width = 320, .Frozen = True})
                checkGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "EmployeeID", .HeaderText = "Employee ID", .Width = 90, .Visible = False})
                For Each mapping In active
                    checkGrid.Columns.Add(New DataGridViewTextBoxColumn With {
                        .Name = mapping.Target.Key,
                        .HeaderText = EmployeeImportPlan.Caption(mapping.Target),
                        .Width = 130
                    })
                Next
                For Each column As DataGridViewColumn In checkGrid.Columns
                    column.SortMode = DataGridViewColumnSortMode.NotSortable
                Next

                checkGrid.Rows.Clear()
                For Each importRow In plan.Rows
                    Dim index = checkGrid.Rows.Add()
                    checkGrid.Rows(index).Tag = importRow
                Next
            End If

            For Each gridRow As DataGridViewRow In checkGrid.Rows
                Dim importRow = DirectCast(gridRow.Tag, EmployeeImportRow)
                gridRow.Cells("RowNumber").Value = importRow.RowNumber
                gridRow.Cells("EmployeeID").Value = If(importRow.EmployeeId > 0, CObj(importRow.EmployeeId), Nothing)
                For Each mapping In active
                    Dim cell = gridRow.Cells(mapping.Target.Key)
                    cell.Value = importRow.ValueOf(mapping.Target.Key)

                    ' The same marks the problems page uses: red where the file is wrong, green
                    ' where the import supplies the value.
                    Dim problem As String = Nothing
                    If importRow.FieldProblems.TryGetValue(mapping.Target.Key, problem) Then
                        cell.Style.BackColor = Color.FromArgb(251, 213, 213)
                        cell.ToolTipText = problem
                    ElseIf importRow.Supplied.ContainsKey(mapping.Target.Key) Then
                        cell.Style.BackColor = Color.FromArgb(216, 240, 216)
                        cell.ToolTipText = "Supplied by the import: " & importRow.Supplied(mapping.Target.Key)
                    Else
                        cell.Style.BackColor = Color.Empty
                        cell.ToolTipText = String.Empty
                    End If
                Next
                PaintCheckRow(gridRow, importRow)
            Next

            UpdateCheckSummary()
        End Sub

        Private Shared Function StatusText(importRow As EmployeeImportRow) As String
            If importRow.EmployeeId > 0 Then Return "Imported"
            If importRow.Problems.Count > 0 Then Return String.Join("; ", importRow.Problems)
            Dim remarks = importRow.Warnings.Concat(importRow.Notes).ToList()
            Return "Ready" & If(remarks.Count > 0, " - " & String.Join("; ", remarks), String.Empty)
        End Function

        Private Shared Sub PaintCheckRow(gridRow As DataGridViewRow, importRow As EmployeeImportRow)
            Dim status = gridRow.Cells("Status")
            status.Value = StatusText(importRow)
            status.ToolTipText = Convert.ToString(status.Value, CultureInfo.InvariantCulture)

            If importRow.EmployeeId > 0 Then
                status.Style.BackColor = Color.FromArgb(225, 245, 225)
            ElseIf importRow.Problems.Count > 0 Then
                status.Style.BackColor = Color.FromArgb(251, 213, 213)
                status.Style.ForeColor = Color.FromArgb(160, 0, 0)
            ElseIf importRow.Warnings.Count > 0 Then
                ' Amber: worth a look, and no bar to importing.
                status.Style.BackColor = Color.FromArgb(255, 235, 190)
                status.Style.ForeColor = Color.FromArgb(120, 70, 0)
            Else
                status.Style.BackColor = Color.Empty
                status.Style.ForeColor = Color.Empty
            End If
        End Sub

        Private Sub UpdateCheckSummary()
            If imported Then
                checkSummaryLabel.ForeColor = Color.DarkGreen
                checkSummaryLabel.Text = Plural(Enumerable.Count(plan.Rows, Function(r) r.EmployeeId > 0), "employee") &
                                         " imported into " & SelectedRegistrationName() & "."
                importButton.Enabled = False
                reportButton.Text = "Show Results"
                reportButton.Visible = True
                Return
            End If

            Dim problems = plan.ProblemCount
            If problems > 0 Then
                checkSummaryLabel.ForeColor = Color.Firebrick
                checkSummaryLabel.Text = Plural(plan.Rows.Count, "row") & ": " &
                                         problems.ToString("N0", CultureInfo.CurrentCulture) & " with problems. " &
                                         "An import writes every row or none - Show Problems lists what to fix in the file."
            Else
                checkSummaryLabel.ForeColor = Color.DarkGreen
                checkSummaryLabel.Text = Plural(plan.Rows.Count, "row") & ", all ready."
            End If

            ' Said, never counted against the import.
            Dim warnings = plan.WarningCount
            If warnings > 0 Then
                checkSummaryLabel.Text &= " " & Plural(warnings, "row") & " may already be here (amber)."
            End If
            If earlierImports.Count > 0 Then
                checkSummaryLabel.Text &= " This file was imported before."
            End If

            reportButton.Text = "Show Problems"
            reportButton.Visible = problems > 0
            UpdateImportAvailability()
            importButton.Text = "Import " & Plural(plan.Rows.Count, "Employee")
        End Sub

#End Region

#Region "Import"

        ''' <summary>
        ''' All or none. With any problem in the file nothing is written and the problems page
        ''' opens; with none, every row is written in one transaction and the results file follows.
        ''' A refusal from the database part way through is the same outcome as a problem found by
        ''' the check - nothing written, and the page naming the row.
        ''' </summary>
        Private Sub ImportButton_Click(sender As Object, e As EventArgs)
            If imported OrElse plan Is Nothing OrElse plan.Rows.Count = 0 Then Return

            ' Viewing as somebody else is for looking, not writing - the rule every save follows.
            If Not SwitchedUserGuard.AllowWrite(Me, "IMPORT EMPLOYEES") Then Return

            ' Asked again, not assumed: the Source tab can be revisited and a combo put back to
            ' Make a Selection after the check has run.
            If Not ConfirmSourceComplete() Then Return

            If plan.ProblemCount > 0 Then
                ShowProblems(String.Empty)
                Return
            End If

            Dim batchName = batchNameTextBox.Text.Trim()
            Dim batchNote = batchNoteTextBox.Text.Trim()
            If batchName = String.Empty OrElse batchNote = String.Empty Then
                MessageBox.Show(Me, "Give the import a name and a note first. They are how it is found under Past Imports - " &
                                "and undone, if it was a mistake.", "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Information)
                If batchName = String.Empty Then batchNameTextBox.Focus() Else batchNoteTextBox.Focus()
                Return
            End If

            Dim registrationId = SelectedRegistrationId()
            Dim roleId = SelectedRoleId()
            Dim timeZoneId = registrationTimeZone.Id

            ' Read again rather than trusted from the check: somebody may have been added, or this
            ' file imported, since. Then asked, never enforced (Glenn, 2026-09-24).
            RefreshDuplicateWarnings()
            RefreshCheckGrid(rebuildColumns:=False)
            If Not ConfirmDespiteDuplicates() Then Return

            Dim count = plan.Rows.Count
            If MessageBox.Show(Me,
                               "Import " & Plural(count, "employee") & " into " & SelectedRegistrationName() &
                               ", each with a login and the role " & roleComboBox.Text & "?" &
                               Environment.NewLine & Environment.NewLine &
                               "Time zone, where the file gives none: " & registrationTimeZone.Name &
                               Environment.NewLine & Environment.NewLine &
                               "Either all of them are written or none are. A results file with every user name and " &
                               "password follows - it is the only copy of the passwords.",
                               "Import Employees", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            Dim employees As List(Of Dictionary(Of String, Object)) = Nothing
            Dim logins As List(Of Dictionary(Of String, Object)) = Nothing
            Dim rowsWritten As List(Of EmployeeImportRow) = Nothing
            plan.BuildWriteValues(employees, logins, rowsWritten)

            Dim summary = JsonSerializer.Serialize(New Dictionary(Of String, Object) From {
                {"file", sourceFileName},
                {"rows", count},
                {"registrationId", registrationId},
                {"roleId", roleId},
                {"timeZoneId", timeZoneId},
                {"templateId", currentTemplateId},
                {"batchName", batchName}
            })
            DataAccess.LogUpdateAudit(PageName, EmployeeImportPlan.TargetTableName, "Import", "BeforeSave", String.Empty, summary,
                                      registrationId:=registrationId)

            Dim batch As New ImportBatchRequest With {
                .BatchName = batchName,
                .Note = batchNote,
                .SavedImportId = currentTemplateId,
                .FileName = sourceFileName,
                .FileHash = ImportBatchRequest.HashOf(sourceData)
            }
            batch.SourceRows.AddRange(rowsWritten.Select(Function(r) r.RowNumber))

            Dim failedIndex = -1
            Dim batchId = 0
            Dim ids As List(Of Integer)
            Try
                Cursor = Cursors.WaitCursor
                ids = DataAccess.ImportEmployees(registrationId, roleId, employees, logins, accessProfile,
                                                 SessionState.ActingUserID, batch, failedIndex, batchId)
            Catch ex As Exception
                Cursor = Cursors.Default
                Telemetry.Error(ex, "FW_EmployeeImport.ImportButton_Click")
                DataAccess.LogUpdateAudit(PageName, EmployeeImportPlan.TargetTableName, "Import", "AfterSave", String.Empty,
                                          summary, False, registrationId:=registrationId)

                Dim where = String.Empty
                If failedIndex >= 0 AndAlso failedIndex < rowsWritten.Count Then
                    Dim failedRow = rowsWritten(failedIndex)
                    failedRow.AddProblem(Nothing, "The database refused this row: " & ex.Message)
                    where = "Row " & failedRow.RowNumber.ToString(CultureInfo.InvariantCulture) & ": "
                    RefreshCheckGrid(rebuildColumns:=False)
                    SelectCheckRow(failedRow)
                End If

                ShowProblems(where & ex.Message)
                Return
            Finally
                Cursor = Cursors.Default
            End Try

            For i = 0 To rowsWritten.Count - 1
                rowsWritten(i).EmployeeId = ids(i)
            Next
            imported = True

            AuditImportedRows(rowsWritten, registrationId, batchId)
            SavedImportDataAccess.MarkUsed(currentTemplateId, registrationId, accessProfile)

            LockAfterImport()
            RefreshCheckGrid(rebuildColumns:=False)
            SelectCheckRow(rowsWritten.FirstOrDefault())

            Dim chosen = ChosenTemplate()
            resultsHtml = EmployeeImportReport.ResultsHtml(plan, sourceFileName, SelectedRegistrationName(), roleComboBox.Text,
                                                           registrationTimeZone.Name,
                                                           If(chosen Is Nothing, String.Empty, Convert.ToString(chosen("ImportName"), CultureInfo.InvariantCulture)),
                                                           batchId, batchName)
            resultsFileName = FileStem() & "-import-results-" & DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) & ".htm"
            ShowResults()
        End Sub

        ''' <summary>
        ''' The sign-in sheet, in a window, with Open In Browser to print or save it. Shown at once
        ''' after the import, and again from Show Results for as long as the page is open.
        ''' </summary>
        Private Sub ShowResults()
            If String.IsNullOrEmpty(resultsHtml) Then Return

            Try
                Using viewer As New ImportReportViewer("Import Employees - Results", resultsHtml, resultsFileName,
                                                       "Open the import results")
                    viewer.ShowDialog(Me)
                End Using
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.ShowResults")
                MessageBox.Show(Me, "The employees were imported, but the results page could not be shown: " & ex.Message &
                                Environment.NewLine & "Press Show Results to try again before closing.",
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub

        Private Function FileStem() As String
            Return System.IO.Path.GetFileNameWithoutExtension(If(String.IsNullOrWhiteSpace(sourceFileName), "employees", sourceFileName))
        End Function

        ''' <summary>The problems page, in a window, with Save to take it away.</summary>
        Private Sub ShowProblems(failureMessage As String)
            Try
                Dim html = EmployeeImportReport.ProblemsHtml(plan, source.Rows, sourceFileName, SelectedRegistrationName(), failureMessage)
                Using viewer As New ImportReportViewer("Import Employees - Nothing Imported", html,
                                                       FileStem() & "-import-problems-" &
                                                       DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) & ".htm",
                                                       "Open the import problems page")
                    viewer.ShowDialog(Me)
                End Using
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.ShowProblems")
                MessageBox.Show(Me, "Nothing was imported. The problems page could not be shown: " & ex.Message,
                                "Import Employees", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub

        ''' <summary>One button, two jobs: the problems page before an import, the results page after one.</summary>
        Private Sub ReportButton_Click(sender As Object, e As EventArgs)
            If imported Then
                ShowResults()
            Else
                ShowProblems(String.Empty)
            End If
        End Sub

        ''' <summary>
        ''' One audit row per employee, after the commit, keyed by the EmployeeID it was given -
        ''' the same AfterSave a Create on FW_Employees_U writes. The password is never in it.
        '''
        ''' A round trip per row, on purpose: each person imported is a record created, and the
        ''' audit trail is read one record at a time. A failure here is logged and not reported,
        ''' as it is for every other save - the import has already happened.
        ''' </summary>
        Private Sub AuditImportedRows(rowsWritten As List(Of EmployeeImportRow), registrationId As Integer, batchId As Integer)
            For Each importRow In rowsWritten
                Try
                    Dim snapshot = importRow.Values.Where(Function(pair) Not String.Equals(pair.Key, EmployeeImportPlan.PasswordKey, StringComparison.OrdinalIgnoreCase)).
                                                    ToDictionary(Function(pair) pair.Key, Function(pair) pair.Value)
                    snapshot(EmployeeImportPlan.PasswordKey) = "[REDACTED]"
                    snapshot("SourceRow") = importRow.RowNumber.ToString(CultureInfo.InvariantCulture)
                    snapshot("ImportBatchID") = batchId.ToString(CultureInfo.InvariantCulture)

                    DataAccess.LogUpdateAudit(PageName, EmployeeImportPlan.TargetTableName, "Import", "AfterSave",
                                              importRow.EmployeeId.ToString(CultureInfo.InvariantCulture),
                                              JsonSerializer.Serialize(snapshot), True, registrationId:=registrationId)
                Catch ex As Exception
                    Telemetry.Error(ex, "FW_EmployeeImport.AuditImportedRows")
                End Try
            Next
        End Sub

        Private Sub LockAfterImport()
            checkGrid.Columns("EmployeeID").Visible = True
            chooseFileButton.Enabled = False
            headerCheckBox.Enabled = False
            delimiterComboBox.Enabled = False
            registrationComboBox.Enabled = False
            roleComboBox.Enabled = False
            importButton.Enabled = False
            batchNameTextBox.ReadOnly = True
            batchNoteTextBox.ReadOnly = True
        End Sub

        ''' <summary>
        ''' Yes or No when the file looks like people already here, or has been imported before and
        ''' not undone. No writes nothing. Nothing to ask means True without a word.
        ''' </summary>
        Private Function ConfirmDespiteDuplicates() As Boolean
            Dim flagged = plan.Rows.Where(Function(r) r.Warnings.Count > 0).ToList()
            If flagged.Count = 0 AndAlso earlierImports.Count = 0 Then Return True

            Dim text As New StringBuilder()
            If earlierImports.Count > 0 Then
                text.AppendLine("This same file was imported before, and not undone:")
                For Each earlier In earlierImports.Take(5)
                    text.Append("   ").AppendLine(earlier)
                Next
                text.AppendLine()
            End If

            If flagged.Count > 0 Then
                text.Append(Plural(flagged.Count, "row")).AppendLine(" may already be here:")
                Dim firstKey = ImportTargetSchema.EmployeesTable & ".FirstName"
                Dim lastKey = ImportTargetSchema.EmployeesTable & ".LastName"
                For Each importRow In flagged.Take(15)
                    text.Append("   Row ").Append(importRow.RowNumber.ToString(CultureInfo.InvariantCulture)).Append(" ").
                         Append((importRow.ValueOf(firstKey) & " " & importRow.ValueOf(lastKey)).Trim()).
                         Append(" - ").AppendLine(String.Join("; ", importRow.Warnings))
                Next
                If flagged.Count > 15 Then text.Append("   and ").Append((flagged.Count - 15).ToString(CultureInfo.InvariantCulture)).AppendLine(" more, amber in the grid")
                text.AppendLine()
            End If

            text.Append("Import anyway?")
            Return MessageBox.Show(Me, text.ToString(), "Import Employees - Possible Duplicates",
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) = DialogResult.Yes
        End Function

        ''' <summary>Past Imports, over this page. It lists; this page keeps whatever is on it.</summary>
        Private Sub PastImportsButton_Click(sender As Object, e As EventArgs)
            Try
                Using page As New FW_ImportBatches_B(currentUser, accessProfile)
                    page.ShowDialog(Me)
                End Using
            Catch ex As Exception
                Telemetry.Error(ex, "FW_EmployeeImport.PastImportsButton_Click")
                MessageBox.Show(Me, "Past Imports could not be opened: " & ex.Message, "Import Employees",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub

        ''' <summary>
        ''' Selected and scrolled into view, near the top: the current cell on the first visible
        ''' column is what scrolls, and a row selected off-screen reads as nothing having happened.
        ''' </summary>
        Private Sub SelectCheckRow(importRow As EmployeeImportRow)
            If importRow Is Nothing Then Return

            Dim gridRow = checkGrid.Rows.Cast(Of DataGridViewRow)().FirstOrDefault(Function(r) r.Tag Is importRow)
            If gridRow Is Nothing Then Return

            Dim firstVisible = checkGrid.Columns.GetFirstColumn(DataGridViewElementStates.Visible)
            If firstVisible IsNot Nothing Then checkGrid.CurrentCell = gridRow.Cells(firstVisible.Index)
            checkGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, gridRow.Index - 2)
        End Sub

        ''' <summary>
        ''' Asked every time after an import, because the page cannot tell whether the results were
        ''' printed or saved - that happens in the browser, out of its sight - and closing without
        ''' them loses every password on them.
        ''' </summary>
        Private Sub FW_EmployeeImport_FormClosing(sender As Object, e As FormClosingEventArgs)
            If Not imported OrElse String.IsNullOrEmpty(resultsHtml) Then Return

            If MessageBox.Show(Me,
                               "Have you printed or saved the results?" & Environment.NewLine & Environment.NewLine &
                               "It is the only copy of the new passwords. Once this page closes they cannot be shown again, " &
                               "and each person would need a new one set by hand.",
                               "Import Employees", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                e.Cancel = True
                tabs.SelectedTab = checkTab
                reportButton.Focus()
            End If
        End Sub

#End Region
    End Class
End Namespace
