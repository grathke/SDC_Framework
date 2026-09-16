Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class FW_UserAccessDiagnostic_B
        Inherits FW_Base_B

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly registrationLabel As Label
        Private ReadOnly registrationComboBox As ComboBox
        Private ReadOnly tableLabel As Label
        Private ReadOnly tableComboBox As ComboBox
        Private ReadOnly roleLabel As Label
        Private ReadOnly roleComboBox As ComboBox
        Private ReadOnly checkAccessButton As Button
        Private ReadOnly applyChangesButton As Button
        Private ReadOnly analysisResultLabel As Label
        Private ReadOnly diagnosticResultTextBox As TextBox
        Private ReadOnly permissionsGrid As DataGridView
        Private selectedRegistrationId As Integer
        Private selectedUserId As Integer
        Private loadingRoles As Boolean
        Private loadingRegistrations As Boolean
        Private diagnosticPageReady As Boolean
        Private diagnosticLayoutInProgress As Boolean

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, "FW_USERS")
            currentUser = user
            accessProfile = profile
            selectedRegistrationId = GetSessionRegistrationId()
            ClientSize = New Size(ClientSize.Width, 980)

            registrationLabel = New Label() With {
                .Text = "Registration:",
                .AutoSize = True,
                .Location = New Point(20, 78),
                .ForeColor = Color.DimGray
            }
            registrationComboBox = New ComboBox() With {
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Location = New Point(110, 74),
                .Size = New Size(275, 26),
                .DropDownWidth = 280
            }
            tableLabel = New Label() With {
                .Text = "Table / Menu:",
                .AutoSize = True,
                .Location = New Point(350, 78),
                .ForeColor = Color.DimGray
            }
            tableComboBox = New ComboBox() With {
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Location = New Point(435, 74),
                .Size = New Size(260, 26),
                .DropDownWidth = 360
            }
            roleLabel = New Label() With {
                .Text = "Role:",
                .AutoSize = True,
                .ForeColor = Color.DimGray
            }
            roleComboBox = New ComboBox() With {
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Size = New Size(260, 26),
                .DropDownWidth = 360
            }
            checkAccessButton = New Button() With {
                .Text = "Check Access",
                .Size = New Size(120, 30)
            }
            applyChangesButton = New Button() With {
                .Text = "Apply Changes",
                .Size = New Size(120, 30)
            }
            analysisResultLabel = New Label() With {
                .Text = "Analysis Result",
                .AutoSize = True,
                .ForeColor = Color.DimGray
            }
            diagnosticResultTextBox = New TextBox() With {
                .Multiline = True,
                .ReadOnly = True,
                .ScrollBars = ScrollBars.Vertical,
                .BackColor = Color.White,
                .Height = 90
            }
            permissionsGrid = New DataGridView() With {
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .ReadOnly = False,
                .MultiSelect = False,
                .RowHeadersVisible = False,
                .AutoGenerateColumns = False,
                .AllowUserToOrderColumns = False,
                .BackgroundColor = Color.White
            }
            ApplyLightBlueHeaderStyle(permissionsGrid)
            permissionsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "Permission",
                .HeaderText = "Permission",
                .DataPropertyName = "Permission",
                .ReadOnly = True,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            })
            permissionsGrid.Columns.Add(New DataGridViewCheckBoxColumn() With {
                .Name = "Allowed",
                .HeaderText = "Allowed",
                .Width = 80
            })
            AddHandler registrationComboBox.SelectedIndexChanged, AddressOf RegistrationComboBox_SelectedIndexChanged
            AddHandler Me.Load, AddressOf DiagnosticPage_Load
            AddHandler Me.Shown, AddressOf DiagnosticPage_Shown
            AddHandler checkAccessButton.Click, AddressOf CheckAccessButton_Click
            AddHandler applyChangesButton.Click, AddressOf ApplyChangesButton_Click
            AddHandler permissionsGrid.CurrentCellDirtyStateChanged, AddressOf PermissionsGrid_CurrentCellDirtyStateChanged

            Controls.Add(registrationLabel)
            Controls.Add(registrationComboBox)
            Controls.Add(tableLabel)
            Controls.Add(tableComboBox)
            Controls.Add(roleLabel)
            Controls.Add(roleComboBox)
            Controls.Add(checkAccessButton)
            Controls.Add(applyChangesButton)
            Controls.Add(analysisResultLabel)
            Controls.Add(diagnosticResultTextBox)
            Controls.Add(permissionsGrid)
            registrationLabel.BringToFront()
            registrationComboBox.BringToFront()
            tableLabel.BringToFront()
            tableComboBox.BringToFront()
        End Sub

        Protected Overrides Function OnlyUseQbe() As Boolean
            Return True
        End Function

        Protected Overrides Sub NotifyBrowseSelectionChanged()
            Dim userId = GetSelectedRecordIdForCustomAction()
            selectedUserId = If(userId.HasValue, userId.Value, 0)
            LoadSelectedUserRoles()
        End Sub

        Private Sub LoadSelectedUserRoles()
            loadingRoles = True
            Try
                roleComboBox.DataSource = Nothing
                If selectedUserId <= 0 OrElse selectedRegistrationId <= 0 Then
                    Return
                End If

                Dim roles = DataAccess.GetAccessDiagnosticRegistrationRoles(selectedUserId, selectedRegistrationId)
                If Not roles.Columns.Contains("RoleDisplayName") Then
                    roles.Columns.Add("RoleDisplayName", GetType(String))
                End If
                For Each roleRow As DataRow In roles.Rows
                    Dim roleName = Convert.ToString(roleRow("RoleName"))
                    Dim isAssigned = Convert.ToInt32(roleRow("IsAssigned")) = 1
                    roleRow("RoleDisplayName") = roleName & If(isAssigned, " (Assigned)", " (Available)")
                Next

                roleComboBox.DisplayMember = "RoleDisplayName"
                roleComboBox.ValueMember = "RoleID"
                roleComboBox.DataSource = roles
                ComboWidth.FitToContent(roleComboBox)
                Dim assignedRows = roles.AsEnumerable().Where(Function(row) Convert.ToInt32(row("IsAssigned")) = 1).ToList()
                roleComboBox.SelectedIndex = If(assignedRows.Count = 1, roles.Rows.IndexOf(assignedRows(0)), -1)
            Finally
                loadingRoles = False
            End Try
        End Sub

        Protected Overrides Function GetVisibleQbeRowCount() As Integer
            Return 3
        End Function

        Protected Overrides Function TryBuildFiltersFromQbe(ByRef filters As Dictionary(Of String, String),
                                                             ByRef validationMessage As String) As Boolean
            Return MyBase.TryBuildFiltersFromQbe(filters, validationMessage)
        End Function

        Protected Overrides Function BuildBrowseListingTitle(registrationId As Integer, tableName As String) As String
            Return "User Access Diagnostic"
        End Function

        Protected Overrides Function GetActiveBaseSql() As String
            Dim activeSql = MyBase.GetActiveBaseSql()
            If String.IsNullOrWhiteSpace(activeSql) Then
                Return activeSql
            End If

            Dim registrationId As Integer = 0
            If Not RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId) Then
                registrationId = selectedRegistrationId
            End If
            If registrationId <= 0 Then
                Return activeSql
            End If

            Dim registrationValue = registrationId.ToString(Globalization.CultureInfo.InvariantCulture)
            Dim explicitPredicatePattern = "((?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?RegistrationID\]?)\s*=\s*(?:@RegistrationID|\?|\d+)"
            If Regex.IsMatch(activeSql, explicitPredicatePattern, RegexOptions.IgnoreCase) Then
                Return Regex.Replace(activeSql,
                                     explicitPredicatePattern,
                                     "$1 = " & registrationValue,
                                     RegexOptions.IgnoreCase)
            End If

            Dim orderMatch = Regex.Match(activeSql, "\s+ORDER\s+BY\s+", RegexOptions.IgnoreCase)
            Dim predicate = "[RegistrationID] = " & registrationValue
            If orderMatch.Success Then
                Dim beforeOrder = activeSql.Substring(0, orderMatch.Index)
                Dim conjunction = If(Regex.IsMatch(beforeOrder, "\bWHERE\b", RegexOptions.IgnoreCase), " AND ", " WHERE ")
                Return beforeOrder & conjunction & predicate & activeSql.Substring(orderMatch.Index)
            End If

            Dim separator = If(Regex.IsMatch(activeSql, "\bWHERE\b", RegexOptions.IgnoreCase), " AND ", " WHERE ")
            Return activeSql & separator & predicate
        End Function

        Protected Overrides Function TryGetActiveRegistrationId(ByRef registrationId As Integer) As Boolean
            If RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId) Then
                selectedRegistrationId = registrationId
                Return True
            End If

            registrationId = selectedRegistrationId
            Return registrationId > 0
        End Function

        Private Sub DiagnosticPage_Load(sender As Object, e As EventArgs)
            Dim canChooseRegistration = accessProfile IsNot Nothing AndAlso
                                         accessProfile.Can("FW_USERS", AccessCapability.ViewAllRecords)
            loadingRegistrations = True
            Try
                RegistrationComboHelper.Populate(registrationComboBox, selectedRegistrationId, True)
                RegistrationComboHelper.UpdateLabelForSelection(registrationLabel, registrationComboBox)
                LoadExposedTableChoices()
            Finally
                loadingRegistrations = False
                diagnosticPageReady = True
            End Try

            registrationLabel.Visible = canChooseRegistration
            registrationComboBox.Visible = canChooseRegistration
            tableLabel.Visible = True
            tableComboBox.Visible = True
        End Sub

        Private Sub RegistrationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If loadingRegistrations Then
                Return
            End If

            ClearBrowseGridForPendingQuery()
            RegistrationComboHelper.UpdateLabelForSelection(registrationLabel, registrationComboBox)

            Dim registrationId As Integer
            If RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId) Then
                selectedRegistrationId = registrationId
                LoadExposedTableChoices()
                LoadSelectedUserRoles()
            Else
                selectedRegistrationId = 0
            End If
        End Sub

        Private Sub LoadExposedTableChoices()
            Dim choices As DataTable
            Try
                choices = DataAccess.GetExposedPageChoices()
            Catch ex As Exception
                choices = New DataTable("ExposedPages")
                choices.Columns.Add("ID", GetType(Integer))
                choices.Columns.Add("Table_Alias", GetType(String))
                choices.Columns.Add("DB_Table", GetType(String))
                choices.Columns.Add("WindowOrPage", GetType(String))
                MessageBox.Show(Me,
                                "The exposed table list could not be loaded." & Environment.NewLine & ex.Message,
                                "Diagnostic Table List",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
            End Try

            Dim placeholder = choices.NewRow()
            placeholder("ID") = 0
            placeholder("Table_Alias") = "Make a Selection"
            placeholder("DB_Table") = String.Empty
            placeholder("WindowOrPage") = String.Empty
            choices.Rows.InsertAt(placeholder, 0)

            tableComboBox.DataSource = Nothing
            tableComboBox.DisplayMember = "Table_Alias"
            tableComboBox.ValueMember = "ID"
            tableComboBox.DataSource = choices
            ComboWidth.FitToContent(tableComboBox)
            tableComboBox.SelectedIndex = 0
        End Sub

        Private Sub DiagnosticPage_Shown(sender As Object, e As EventArgs)
            LoadExposedTableChoices()
            TrimWindowToContent()
        End Sub

        ''' <summary>
        ''' Takes the window down to where its last control ends.
        '''
        ''' Once, from Shown, after the layout has settled - not from ApplyPageSpecificLayout, which
        ''' is the layout pass itself and would be re-entered by the resize this causes. The page is
        ''' a fixed height built from constants above, so there is nothing to recompute afterwards.
        ''' </summary>
        Private Sub TrimWindowToContent()
            If applyChangesButton Is Nothing Then Return

            Dim requiredHeight = applyChangesButton.Bottom + 16
            If requiredHeight > 0 AndAlso Me.ClientSize.Height <> requiredHeight Then
                Me.ClientSize = New Size(Me.ClientSize.Width, requiredHeight)
            End If
        End Sub

        ''' <summary>
        ''' Result rows the browse grid is built to show. Everything below moves with it, and the
        ''' window is trimmed to its content on Shown, so the page grows by exactly this much.
        ''' </summary>
        Private Const BrowseRowsShown As Integer = 4

        ''' <summary>
        ''' Lines the analysis box is built to show before it scrolls.
        '''
        ''' What it holds is now the header and the roles evaluated - five lines and a blank, then
        ''' ANALYSIS COMPLETED - since the seven permission sentences moved into the grid above. It
        ''' was left at twelve lines' worth when they went, which is half again more than anything
        ''' put in it.
        '''
        ''' Counted in lines and multiplied by the font's own height rather than set in pixels, so
        ''' the box holds this many lines whatever the DPI, and so changing it is a decision about
        ''' content rather than arithmetic. The window height follows from where this box ends.
        ''' </summary>
        Private Const DiagnosticResultLines As Integer = 8

        Protected Overrides Sub ApplyPageSpecificLayout()
            If diagnosticLayoutInProgress OrElse IsDisposed OrElse Disposing Then
                Return
            End If

            diagnosticLayoutInProgress = True
            Try
            If registrationComboBox Is Nothing OrElse
               registrationLabel Is Nothing OrElse
                    tableComboBox Is Nothing Then
                Return
            End If

            Dim qbeButton = FindButtonStartingWithText(Me, "QBE")
            Dim closeButton = FindButtonByText(Me, "Close")

            ' Anchored to whichever action button is actually present. This used to hang off the Enum
            ' button and did nothing at all when that button was absent - a lookup by caption fails
            ' silently, so the combo would simply have stayed where it was built with nothing to say
            ' why. The Enum button has since been removed entirely.
            Dim rightAnchor As Control = If(closeButton, qbeButton)
            If rightAnchor IsNot Nothing Then
                registrationComboBox.Left = rightAnchor.Left + rightAnchor.Width - registrationComboBox.Width
                registrationComboBox.Top = 42
                registrationLabel.Left = Math.Max(8, registrationComboBox.Left - registrationLabel.PreferredWidth - 8)
                registrationLabel.Top = registrationComboBox.Top + 5
            End If

            Dim browseSplit = FindBrowseSplitContainer(Me)
            If browseSplit Is Nothing Then
                Return
            End If

            Dim compactTop = Not registrationComboBox.Visible
            If compactTop AndAlso browseSplit.Tag Is Nothing Then
                MoveDiagnosticActionRow(-70)
                browseSplit.Top = Math.Max(92, browseSplit.Top - 70)
                browseSplit.Tag = "DiagnosticBrowseCompacted"
            End If

            ' The action row belongs to FW_Base_B, which lays out Close, the colour picker and QBE
            ' on one line, each placed off its neighbour. This page used to move two of those three
            ' to y=78 and leave the picker where the base class had put it, which is how Color ended
            ' up on a different line from the Close button it is supposed to sit beside. Nothing is
            ' moved now; the browse area is placed under whatever the row turns out to be.
            Dim actionRowBottom = 78
            If closeButton IsNot Nothing Then actionRowBottom = Math.Max(actionRowBottom, closeButton.Bottom)
            If qbeButton IsNot Nothing Then actionRowBottom = Math.Max(actionRowBottom, qbeButton.Bottom)

            browseSplit.Top = actionRowBottom + 12

            ' Provisional, so the QBE panel has room to size itself. Replaced below by the only two
            ' things that decide how tall this area should be: the QBE panel, and the rows we want
            ' under it.
            browseSplit.Height = 205
            Dim qbePanelHeight = ApplyDiagnosticQbeLayout(browseSplit)

            ' Its former 410 left most of the results grid empty - a QBE search for a user returns
            ' one row, or a handful - while the permissions and the analysis sat below the fold.
            '
            ' Built from qbePanelHeight, not from SplitterDistance. The splitter is derived from
            ' browseSplit.Height, so setting the height back from it feeds each pass its own output:
            ' the QBE panel grew every time and the results grid was squeezed to nothing. The panel
            ' height comes from the QBE grid's rows and settles on the first pass.
            Dim resultsGrid = FindResultsGrid(browseSplit.Panel2)
            If qbePanelHeight > 0 AndAlso resultsGrid IsNot Nothing Then
                Dim rowsHeight = resultsGrid.ColumnHeadersHeight +
                                 (resultsGrid.RowTemplate.Height * BrowseRowsShown) + 8
                browseSplit.Height = qbePanelHeight + browseSplit.SplitterWidth + rowsHeight
                browseSplit.SplitterDistance = qbePanelHeight
            End If

            tableLabel.Left = browseSplit.Left
            tableLabel.Top = browseSplit.Bottom + 18
            tableComboBox.Left = tableLabel.Left
            tableComboBox.Top = tableLabel.Bottom + 2
            tableComboBox.Width = 260
            roleLabel.Left = tableComboBox.Right + 12
            roleLabel.Top = tableLabel.Top + 5
            roleComboBox.Left = roleLabel.Right + 8
            roleComboBox.Top = tableComboBox.Top
            checkAccessButton.Left = roleComboBox.Right + 12
            checkAccessButton.Top = tableComboBox.Top - 2

            ' The analysis comes before the grid, because that is the order the page is used in:
            ' find the user, ask, read what it says, then change what is wrong. It used to sit under
            ' the grid, so the answer to the question you just asked was below the thing you would
            ' change in response to it.
            analysisResultLabel.Left = browseSplit.Left
            analysisResultLabel.Top = tableComboBox.Bottom + 8
            diagnosticResultTextBox.Left = browseSplit.Left
            diagnosticResultTextBox.Top = analysisResultLabel.Bottom + 4
            diagnosticResultTextBox.Width = browseSplit.Width

            ' Sized for what it now holds. The seven permission sentences moved into the grid
            ' above, and what is left is the header and the role list - a dozen lines at most, with
            ' a scrollbar for the rest. It briefly filled the page, which only made the empty space
            ' bigger.
            diagnosticResultTextBox.Height = (diagnosticResultTextBox.Font.Height * DiagnosticResultLines) + 10

            permissionsGrid.Left = browseSplit.Left
            permissionsGrid.Top = diagnosticResultTextBox.Bottom + 12
            permissionsGrid.Width = browseSplit.Width
            permissionsGrid.Height = 180
            permissionsGrid.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right

            ' Apply Changes commits what is ticked in the grid, so it stays with the grid rather
            ' than with Check Access, which acts on the selectors above.
            applyChangesButton.Top = permissionsGrid.Bottom + 8
            applyChangesButton.Left = permissionsGrid.Right - applyChangesButton.Width

            ' The window height is NOT set here. Assigning ClientSize inside this method resizes
            ' the form, which re-enters FW_Base_B's layout pass; that pass lays the QBE panel out
            ' again and then finds this method guarded, so ApplyDiagnosticQbeLayout never runs on it
            ' and the panel is left at the base class's full height. It looked exactly like the
            ' compacting had stopped working. TrimWindowToContent does it once, from Shown.
            diagnosticResultTextBox.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            checkAccessButton.BringToFront()
            applyChangesButton.BringToFront()
            registrationComboBox.BringToFront()
            registrationLabel.BringToFront()
            tableComboBox.BringToFront()
            tableLabel.BringToFront()
            tableComboBox.BringToFront()
            tableLabel.BringToFront()
            analysisResultLabel.BringToFront()
            diagnosticResultTextBox.BringToFront()
            permissionsGrid.BringToFront()
            Finally
                diagnosticLayoutInProgress = False
            End Try
        End Sub

        Private Sub CheckAccessButton_Click(sender As Object, e As EventArgs)
            ShowDiagnosticResult("Checking access...")
            Dim userId = GetSelectedRecordIdForCustomAction()
            If Not userId.HasValue Then
                ShowDiagnosticResult("Select a user record in the grid first.")
                Return
            End If

            Dim registrationId = GetSelectedRegistrationId()
            Dim selectedTable = TryCast(tableComboBox.SelectedItem, DataRowView)
            Dim missingSelections As New List(Of String)()
            If registrationId <= 0 Then missingSelections.Add("registration")
            If selectedTable Is Nothing OrElse Convert.ToInt32(selectedTable("ID")) <= 0 Then missingSelections.Add("table or menu item")
            Dim selectedRole = TryCast(roleComboBox.SelectedItem, DataRowView)
            If selectedRole Is Nothing OrElse Convert.ToInt32(If(selectedRole.Row.Table.Columns.Contains("RoleID"), selectedRole("RoleID"), 0)) <= 0 Then
                missingSelections.Add("role")
            End If
            If missingSelections.Count > 0 Then
                ShowDiagnosticResult("Select a " & String.Join(", ", missingSelections) & " first.")
                Return
            End If

            Dim dbTable = Convert.ToString(selectedTable("DB_Table"))
            Dim tableAlias = Convert.ToString(selectedTable("Table_Alias"))
            Try
                Dim selectedRoleId = Convert.ToInt32(selectedRole("RoleID"))
                Dim selectedRoleName = Convert.ToString(selectedRole("RoleName"))
                Dim selectedRoleAssigned = IsSelectedRoleAssigned(selectedRole)
                LoadPermissionsGrid(selectedRoleId, registrationId, dbTable)
                Dim diagnosticRows = DataAccess.GetAccessDiagnostic(userId.Value, registrationId, dbTable)
            Dim roleNames As New List(Of String)()
            Dim capabilities As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            For Each capability In New String() {"Can_Create", "Can_Read", "Can_Update", "Can_Delete", "Can_UseQBE", "Can_ViewAllRecords", "Can_ViewOnlyMyRecords"}
                capabilities(capability) = False
            Next

            For Each diagnosticRow As DataRow In diagnosticRows.Rows
                If selectedRoleAssigned AndAlso
                   Not diagnosticRow.IsNull("RoleID") AndAlso
                   Convert.ToInt32(diagnosticRow("RoleID")) <> selectedRoleId Then
                    Continue For
                End If

                If Not diagnosticRow.IsNull("RoleName") Then
                    Dim roleName = Convert.ToString(diagnosticRow("RoleName"))
                    If Not roleNames.Contains(roleName) Then
                        roleNames.Add(roleName)
                    End If
                End If

                For Each capability In capabilities.Keys.ToList()
                    If Convert.ToBoolean(diagnosticRow(capability)) Then
                        capabilities(capability) = True
                    End If
                Next
            Next

            Dim result As New StringBuilder()
            result.AppendLine("User Access Diagnostic")
            result.AppendLine("Table: " & tableAlias)
            result.AppendLine("Registration ID: " & registrationId.ToString())
            result.AppendLine("Selected Role Permissions: " & selectedRoleName & If(selectedRoleAssigned, " (Assigned)", " (Available - not assigned yet)"))
            result.AppendLine("Assigned Role Evaluated: " & If(roleNames.Count = 0, "None", String.Join(", ", roleNames)))
            result.AppendLine()

            If Not selectedRoleAssigned Then
                result.AppendLine("The selected role is not assigned to this user for this registration.")
                result.AppendLine("The permissions grid can be edited and applied to create RoleDetails and assign the role.")
                result.AppendLine("Analysis is only run against a role assigned to the selected user.")
                result.AppendLine()
                result.AppendLine("ANALYSIS COMPLETED")
                ShowDiagnosticResult(result.ToString())
                Return
            End If

            If roleNames.Count = 0 Then
                result.AppendLine("No active role is assigned for this user and registration.")
                result.AppendLine("Permissions cannot be evaluated until a role is assigned.")
                result.AppendLine()
                result.AppendLine("ANALYSIS COMPLETED")
                ShowDiagnosticResult(result.ToString())
                Return
            End If

            ' The seven permissions used to be written out here as well, each as a sentence naming
            ' its column. The grid above says the same thing in a form that can be changed, so the
            ' text was a copy of it that could only be read - and the two sat one above the other
            ' saying it twice.
            result.AppendLine()
            result.AppendLine("ANALYSIS COMPLETED")

                ShowDiagnosticResult(result.ToString())
            Catch ex As Exception
                ShowDiagnosticResult("Access check failed or timed out." & Environment.NewLine & ex.Message)
            End Try
        End Sub

        Private Sub ApplyChangesButton_Click(sender As Object, e As EventArgs)
            Dim userId = GetSelectedRecordIdForCustomAction()
            Dim registrationId = GetSelectedRegistrationId()
            Dim selectedTable = TryCast(tableComboBox.SelectedItem, DataRowView)
            Dim selectedRole = TryCast(roleComboBox.SelectedItem, DataRowView)
            Dim missing As New List(Of String)()
            If registrationId <= 0 Then missing.Add("REGISTRATION")
            If Not userId.HasValue Then missing.Add("USER")
            If selectedTable Is Nothing OrElse Convert.ToInt32(selectedTable("ID")) <= 0 Then missing.Add("TABLE OR MENU")
            If selectedRole Is Nothing OrElse Not selectedRole.Row.Table.Columns.Contains("RoleID") OrElse Convert.ToInt32(selectedRole("RoleID")) <= 0 Then missing.Add("ROLE")
            If missing.Count > 0 Then
                MessageBox.Show(Environment.NewLine & String.Join(Environment.NewLine, missing),
                                "THE FOLLOWING IS REQUIRED",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
                Return
            End If

            Dim requested As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            permissionsGrid.EndEdit()
            For Each gridRow As DataGridViewRow In permissionsGrid.Rows
                Dim permissionName = Convert.ToString(gridRow.Tag)
                requested(permissionName) = Convert.ToBoolean(If(gridRow.Cells("Allowed").Value, False))
            Next

            Dim confirmation = MessageBox.Show("APPLY THE SELECTED PERMISSION CHANGES?", "CONFIRM PERMISSION CHANGES", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            If confirmation <> DialogResult.Yes Then Return

            Try
                Dim schemaId = DataAccess.GetRoleSchemaIdByTable(Convert.ToString(selectedTable("DB_Table")))
                If schemaId <= 0 Then Throw New InvalidOperationException("The selected table has no active RoleSchema record.")
                Dim trace = DataAccess.ApplyAccessDiagnosticChanges(userId.Value,
                                                                     registrationId,
                                                                     Convert.ToInt32(selectedRole("RoleID")),
                                                                     schemaId,
                                                                     Convert.ToString(selectedTable("DB_Table")),
                                                                     requested,
                                                                     If(SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0),
                                                                     Not IsSelectedRoleAssigned(selectedRole))
                If trace.Rows.Count = 0 Then
                    Throw New InvalidOperationException("The transaction returned no completion trace.")
                End If

                Dim traceRow = trace.Rows(0)
                Dim traceMessage =
                    "PERMISSION CHANGE TRACE" & Environment.NewLine &
                    "OPERATION: " & Convert.ToString(traceRow("Operation")) & Environment.NewLine &
                    "ROWS AFFECTED: " & Convert.ToString(traceRow("RowsAffected")) & Environment.NewLine &
                    "USERID: " & Convert.ToString(traceRow("UserID")) & Environment.NewLine &
                    "REGISTRATIONID: " & Convert.ToString(traceRow("RegistrationID")) & Environment.NewLine &
                    "ROLEID: " & Convert.ToString(traceRow("RoleID")) & Environment.NewLine &
                    "SCHEMAID: " & Convert.ToString(traceRow("SchemaID")) & Environment.NewLine &
                    "DB_TABLE: " & Convert.ToString(traceRow("DB_Table")) & Environment.NewLine &
                    "COMMITTED: " & Convert.ToString(traceRow("Committed"))
                DataAccess.InvalidateRoleMetadataCache()
                CheckAccessButton_Click(sender, e)
                ShowDiagnosticResult(diagnosticResultTextBox.Text & Environment.NewLine & Environment.NewLine & traceMessage)
            Catch ex As Exception
                ShowDiagnosticResult("PERMISSION CHANGES FAILED." & Environment.NewLine & ex.Message.ToUpperInvariant())
            End Try
        End Sub

        Private Function IsSelectedRoleAssigned(selectedRole As DataRowView) As Boolean
            Return selectedRole IsNot Nothing AndAlso
                   selectedRole.Row.Table.Columns.Contains("IsAssigned") AndAlso
                   Convert.ToInt32(selectedRole("IsAssigned")) = 1
        End Function

        Private Sub PermissionsGrid_CurrentCellDirtyStateChanged(sender As Object, e As EventArgs)
            If permissionsGrid.IsCurrentCellDirty Then
                permissionsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit)
            End If
        End Sub

        Private Sub LoadPermissionsGrid(roleId As Integer, registrationId As Integer, dbTable As String)
            permissionsGrid.Rows.Clear()
            Dim details = DataAccess.GetAccessDiagnosticRoleDetails(roleId, registrationId)
            Dim detail As DataRow = Nothing
            For Each row As DataRow In details.Rows
                If String.Equals(Convert.ToString(row("DB_Table")), dbTable, StringComparison.OrdinalIgnoreCase) Then
                    detail = row
                    Exit For
                End If
            Next

            For Each permission In New (String, String)() {
                ("Can_Create", "Can Create"),
                ("Can_Read", "Can Read"),
                ("Can_Update", "Can Modify"),
                ("Can_Delete", "Can Delete"),
                ("Can_UseQBE", "Can Use QBE"),
                ("Can_ViewAllRecords", "Can View All Records"),
                ("Can_ViewOnlyMyRecords", "Can View Only My Records")
            }
                Dim currentValue = detail IsNot Nothing AndAlso Convert.ToBoolean(detail(permission.Item1))
                Dim rowIndex = permissionsGrid.Rows.Add(permission.Item2, currentValue)
                permissionsGrid.Rows(rowIndex).Tag = permission.Item1
            Next
        End Sub

        Private Function GetSelectedRegistrationId() As Integer
            Dim registrationId As Integer = 0
            If RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId) Then
                selectedRegistrationId = registrationId
            End If
            Return selectedRegistrationId
        End Function

        Private Sub ShowDiagnosticResult(message As String)
            diagnosticResultTextBox.Text = message
            diagnosticResultTextBox.Visible = True
        End Sub

        ''' <summary>
        ''' Sizes the QBE panel to its rows, and returns the height it decided on - zero when there
        ''' was nothing to size.
        '''
        ''' The return value is computed from the QBE grid's own header and row heights and nothing
        ''' else. That is what makes it safe for the caller to set browseSplit.Height from it:
        ''' SplitterDistance is not, because this method derives it from browseSplit.Height, so
        ''' setting the height back from the splitter feeds each layout pass its own output.
        ''' </summary>
        Private Function ApplyDiagnosticQbeLayout(browseSplit As SplitContainer) As Integer
            If browseSplit Is Nothing OrElse browseSplit.Panel1Collapsed Then
                Return 0
            End If

            browseSplit.BackColor = Color.White
            browseSplit.Panel1.BackColor = Color.White

            Dim qbeGrid = FindQbeGrid(browseSplit.Panel1)
            If qbeGrid Is Nothing Then
                Return 0
            End If

            Dim qbePanel = TryCast(qbeGrid.Parent, Panel)
            If qbePanel IsNot Nothing Then
                qbePanel.BackColor = Color.White
            End If

            MoveQbeControlsUp(qbePanel, qbeGrid)

            Dim visibleRows = GetVisibleQbeRowCount()
            Dim gridHeight = qbeGrid.ColumnHeadersHeight + (qbeGrid.RowTemplate.Height * visibleRows) + 2
            qbeGrid.Height = gridHeight

            Dim panelHeight = Math.Max(120, qbeGrid.Bottom + 6)
            If qbePanel IsNot Nothing Then
                qbePanel.Height = panelHeight
            End If

            browseSplit.Panel1MinSize = panelHeight
            browseSplit.SplitterDistance = Math.Min(panelHeight, browseSplit.Height - browseSplit.Panel2MinSize)
            Return panelHeight
        End Function

        Private Sub MoveQbeControlsUp(qbePanel As Panel, qbeGrid As DataGridView)
            If qbePanel Is Nothing OrElse qbeGrid Is Nothing Then
                Return
            End If

            Dim activeFilterLabel = qbePanel.Controls.OfType(Of Label)().FirstOrDefault(Function(label) label.Text.StartsWith("Active Filter", StringComparison.OrdinalIgnoreCase))
            If activeFilterLabel IsNot Nothing Then
                activeFilterLabel.Top = 2
            End If

            Dim moveUp = Math.Max(0, Math.Min(qbeGrid.Top \ 2, qbeGrid.Top - 22))
            If moveUp <= 0 Then
                Return
            End If

            For Each control In GetQbeControlsToMove(qbePanel, qbeGrid)
                control.Top = Math.Max(0, control.Top - moveUp)
            Next
        End Sub

        Private Function GetQbeControlsToMove(qbePanel As Panel, qbeGrid As DataGridView) As IEnumerable(Of Control)
            Dim controls As New List(Of Control) From {qbeGrid}

            Dim imagePanel = qbePanel.Controls.OfType(Of Panel)().FirstOrDefault()
            If imagePanel IsNot Nothing Then
                controls.Add(imagePanel)
            End If

            For Each buttonText In New String() {"Find", "Clear", "Save", "Retrieve"}
                Dim button = FindButtonByText(qbePanel, buttonText)
                If button IsNot Nothing Then
                    controls.Add(button)
                End If
            Next

            Return controls
        End Function

        ''' <summary>
        ''' The results grid. Panel2 holds it and nothing else that is a grid, so the first one
        ''' found is it - unlike Panel1, where the QBE grid has to be told apart by its columns.
        ''' </summary>
        Private Shared Function FindResultsGrid(parent As Control) As DataGridView
            For Each child As Control In parent.Controls
                Dim grid = TryCast(child, DataGridView)
                If grid IsNot Nothing Then
                    Return grid
                End If

                Dim nested = FindResultsGrid(child)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function FindQbeGrid(parent As Control) As DataGridView
            For Each child As Control In parent.Controls
                Dim grid = TryCast(child, DataGridView)
                If grid IsNot Nothing AndAlso grid.Columns.Contains("FriendlyName") AndAlso grid.Columns.Contains("Operator") Then
                    Return grid
                End If

                Dim nested = FindQbeGrid(child)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

        Private Sub MoveDiagnosticActionRow(offset As Integer)
            Dim qbeButton = FindButtonStartingWithText(Me, "QBE")
            If qbeButton Is Nothing OrElse qbeButton.Tag IsNot Nothing Then
                Return
            End If

            qbeButton.Top += offset
            qbeButton.Tag = "DiagnosticActionRowMoved"

            For Each actionCaption In New String() {"Close", "Enum", "Show Deleted", "Find"}
                Dim button = FindButtonByText(Me, actionCaption)
                If button IsNot Nothing Then
                    button.Top += offset
                End If
            Next
        End Sub

        Private Shared Function FindButtonByText(parent As Control, text As String) As Button
            For Each child As Control In parent.Controls
                Dim button = TryCast(child, Button)
                If button IsNot Nothing AndAlso String.Equals(button.Text, text, StringComparison.OrdinalIgnoreCase) Then
                    Return button
                End If

                Dim nested = FindButtonByText(child, text)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function FindButtonStartingWithText(parent As Control, text As String) As Button
            For Each child As Control In parent.Controls
                Dim button = TryCast(child, Button)
                If button IsNot Nothing AndAlso button.Text.StartsWith(text, StringComparison.OrdinalIgnoreCase) Then
                    Return button
                End If

                Dim nested = FindButtonStartingWithText(child, text)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function FindBrowseSplitContainer(parent As Control) As SplitContainer
            For Each child As Control In parent.Controls
                Dim split = TryCast(child, SplitContainer)
                If split IsNot Nothing Then
                    Return split
                End If

                Dim nested = FindBrowseSplitContainer(child)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

    End Class
End Namespace
