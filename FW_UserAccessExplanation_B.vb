Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class FW_UserAccessExplanation_B
        Inherits Form

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly registrationId As Integer
        Private ReadOnly registrationLabel As Label
        Private ReadOnly closeButton As Button
        Private ReadOnly emailComboBox As ComboBox
        Private ReadOnly findUserButton As Button
        Private ReadOnly userLabel As Label
        Private ReadOnly tableComboBox As ComboBox
        Private ReadOnly roleComboBox As ComboBox
        Private ReadOnly explainButton As Button
        Private ReadOnly applyAccessChangesButton As Button
        Private ReadOnly comparisonGrid As DataGridView
        Private ReadOnly comparisonFootnote As Label
        Private ReadOnly resultTextBox As TextBox
        Private ReadOnly beforePermissions As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        Private selectedUserId As Integer
        Private loadingRoles As Boolean
        Private beforeWasConfigured As Boolean
        Private beforeRoleWasAssigned As Boolean

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = user
            accessProfile = profile
            registrationId = If(SessionState.Current.HasValue, SessionState.Current.Value.RegistrationID, 0)

            Me.Text = "USER ACCESS EXPLANATION"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New Size(820, 900)
            Me.MinimumSize = New Size(700, 700)
            Me.BackColor = Color.White

            registrationLabel = New Label() With {
                .Text = "REGISTRATION: " & GetRegistrationDisplayName(),
                .AutoSize = True,
                .Location = New Point(24, 22),
                .ForeColor = Color.DimGray
            }
            closeButton = New Button() With {
                .Text = "CLOSE",
                .Location = New Point(696, 18),
                .Size = New Size(100, 30),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right
            }
            Dim emailLabel = New Label() With {
                .Text = "USER EMAIL:",
                .AutoSize = True,
                .Location = New Point(24, 62),
                .ForeColor = Color.DimGray
            }
            emailComboBox = New ComboBox() With {
                .DropDownStyle = ComboBoxStyle.DropDown,
                .Location = New Point(110, 58),
                .Size = New Size(330, 24),
                .AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                .AutoCompleteSource = AutoCompleteSource.CustomSource,
                .DropDownWidth = 420
            }
            findUserButton = New Button() With {
                .Text = "FIND USER",
                .Location = New Point(450, 56),
                .Size = New Size(100, 28)
            }
            userLabel = New Label() With {
                .AutoSize = True,
                .Location = New Point(570, 62),
                .ForeColor = Color.DarkGreen
            }
            Dim tableLabel = New Label() With {
                .Text = "TABLE / MENU:",
                .AutoSize = True,
                .Location = New Point(24, 112),
                .ForeColor = Color.DimGray
            }
            tableComboBox = New ComboBox() With {
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Location = New Point(120, 108),
                .Size = New Size(300, 26),
                .DropDownWidth = 380
            }
            Dim roleLabel = New Label() With {
                .Text = "ROLE:",
                .AutoSize = True,
                .Location = New Point(450, 112),
                .ForeColor = Color.DimGray
            }
            roleComboBox = New ComboBox() With {
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Location = New Point(495, 108),
                .Size = New Size(285, 26),
                .DropDownWidth = 360
            }
            explainButton = New Button() With {
                .Text = "EXPLAIN ACCESS",
                .Location = New Point(24, 155),
                .Size = New Size(772, 30),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            }
            applyAccessChangesButton = New Button() With {
                .Text = "APPLY ACCESS CHANGES",
                .Location = New Point(24, 415),
                .Size = New Size(772, 30),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Visible = False
            }
            comparisonGrid = New DataGridView() With {
                .Location = New Point(24, 415),
                .Size = New Size(772, 190),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .ReadOnly = True,
                .RowHeadersVisible = False,
                .AutoGenerateColumns = False,
                .Visible = False
            }
            ApplyLightBlueHeaderStyle(comparisonGrid)
            comparisonGrid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "Permission", .HeaderText = "PERMISSION", .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})
            comparisonGrid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "Before", .HeaderText = "BEFORE", .Width = 120})
            comparisonGrid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "After", .HeaderText = "AFTER", .Width = 120})
            comparisonFootnote = New Label() With {
                .Text = "*NA = NOT AVAILABLE BEFORE CONFIGURATION",
                .AutoSize = True,
                .Location = New Point(24, 615),
                .Visible = False
            }
            resultTextBox = New TextBox() With {
                .Location = New Point(24, 195),
                .Size = New Size(772, 210),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Font = New Font("Consolas", 10.0F, FontStyle.Regular),
                .Multiline = True,
                .ReadOnly = True,
                .ScrollBars = ScrollBars.Vertical,
                .BackColor = Color.White
            }

            AddHandler Me.Load, AddressOf ExplanationPage_Load
            AddHandler closeButton.Click, AddressOf CloseButton_Click
            AddHandler findUserButton.Click, AddressOf FindUserButton_Click
            AddHandler emailComboBox.KeyDown, AddressOf EmailComboBox_KeyDown
            AddHandler explainButton.Click, AddressOf ExplainButton_Click
            AddHandler applyAccessChangesButton.Click, AddressOf ApplyAccessChangesButton_Click
            AddHandler tableComboBox.SelectedIndexChanged, AddressOf TableComboBox_SelectedIndexChanged
            AddHandler roleComboBox.SelectedIndexChanged, AddressOf RoleComboBox_SelectedIndexChanged

            Me.Controls.Add(registrationLabel)
            Me.Controls.Add(closeButton)
            Me.Controls.Add(emailLabel)
            Me.Controls.Add(emailComboBox)
            Me.Controls.Add(findUserButton)
            Me.Controls.Add(userLabel)
            Me.Controls.Add(tableLabel)
            Me.Controls.Add(tableComboBox)
            Me.Controls.Add(roleLabel)
            Me.Controls.Add(roleComboBox)
            Me.Controls.Add(explainButton)
            Me.Controls.Add(comparisonGrid)
            Me.Controls.Add(comparisonFootnote)
            Me.Controls.Add(applyAccessChangesButton)
            Me.Controls.Add(resultTextBox)
        End Sub

        Private Sub ExplanationPage_Load(sender As Object, e As EventArgs)
            LoadTableChoices()
            If Not LoadRoles() Then
                Return
            End If
            LoadEmailSuggestions()
            explainButton.Enabled = False
            ShowResult("ENTER OR SELECT A USER EMAIL, THEN SELECT A TABLE AND ROLE.")
        End Sub

        Private Function GetRegistrationDisplayName() As String
            Dim registration = DataAccess.GetRegistrationById(registrationId)
            If registration Is Nothing OrElse String.IsNullOrWhiteSpace(registration.RegName) Then
                Return registrationId.ToString()
            End If

            Return registration.RegName.Trim().ToUpperInvariant()
        End Function

        Private Sub LoadEmailSuggestions()
            Try
                Dim users = DataAccess.GetAccessDiagnosticUsersWithEmail(registrationId)
                Dim suggestions As New AutoCompleteStringCollection()
                For Each row As DataRow In users.Rows
                    Dim email = Convert.ToString(row("Email")).Trim()
                    If email <> String.Empty Then
                        suggestions.Add(email)
                    End If
                Next
                emailComboBox.AutoCompleteCustomSource = suggestions
                emailComboBox.Items.Clear()
                For Each suggestion As String In suggestions
                    emailComboBox.Items.Add(suggestion)
                Next
            Catch ex As Exception
                ShowResult("EMAIL LIST COULD NOT BE LOADED" & Environment.NewLine & ex.Message)
            End Try
        End Sub

        Private Sub EmailComboBox_KeyDown(sender As Object, e As KeyEventArgs)
            If e.KeyCode = Keys.Enter Then
                e.SuppressKeyPress = True
                FindUserButton_Click(sender, EventArgs.Empty)
            End If
        End Sub

        Private Sub FindUserButton_Click(sender As Object, e As EventArgs)
            ShowResult("EMAIL LOOKUP STARTED" & Environment.NewLine &
                       "SEARCHING FW_USERS FOR THE SELECTED EMAIL.")

            If registrationId <= 0 Then
                ShowResult("No active registration is available in the current session.")
                Return
            End If

            Try
                Dim user = DataAccess.GetAccessDiagnosticUserByEmail(emailComboBox.Text, registrationId)
                If user Is Nothing Then
                    selectedUserId = 0
                    userLabel.Text = "USER NOT FOUND"
                    roleComboBox.DataSource = Nothing
                    explainButton.Enabled = False
                    HideCorrectionControls()
                    ShowResult("EMAIL LOOKUP FAILED" & Environment.NewLine &
                               "No active user was found for that email address in Registration " & registrationId.ToString() & ".")
                    Return
                End If

                selectedUserId = Convert.ToInt32(user("UserID"))
                userLabel.Text = Convert.ToString(user("FirstLast")).ToUpperInvariant()
                If Not LoadRoles() Then
                    Return
                End If
                HideCorrectionControls()
                explainButton.Enabled = tableComboBox.SelectedIndex > 0 AndAlso roleComboBox.SelectedIndex > 0
                ShowResult("EMAIL LOOKUP SUCCESSFUL" & Environment.NewLine &
                           "User: " & userLabel.Text & Environment.NewLine &
                           "Select a table and role, then choose Explain Access.")
            Catch ex As Exception
                selectedUserId = 0
                userLabel.Text = "LOOKUP ERROR"
                roleComboBox.DataSource = Nothing
                explainButton.Enabled = False
                HideCorrectionControls()
                ShowResult("EMAIL LOOKUP FAILED" & Environment.NewLine & ex.Message)
            End Try
        End Sub

        Private Function LoadRoles() As Boolean
            loadingRoles = True
            Try
                Dim roles = DataAccess.GetAccessDiagnosticRegistrationRoles(selectedUserId, registrationId)
                Dim roleList = roles.Clone()
                roleList.Columns.Add("RoleDisplayName", GetType(String))

                Dim placeholder = roleList.NewRow()
                placeholder("RoleID") = 0
                placeholder("RoleName") = ""
                placeholder("DisplayOrder") = 0
                placeholder("IsAssigned") = 0
                placeholder("RoleDisplayName") = "MAKE A SELECTION"
                roleList.Rows.Add(placeholder)

                For Each row As DataRow In roles.Rows
                    Dim displayRow = roleList.NewRow()
                    displayRow("RoleID") = row("RoleID")
                    displayRow("RoleName") = row("RoleName")
                    displayRow("DisplayOrder") = row("DisplayOrder")
                    displayRow("IsAssigned") = row("IsAssigned")
                    displayRow("RoleDisplayName") = Convert.ToString(row("RoleName")).ToUpperInvariant() &
                        If(Convert.ToInt32(row("IsAssigned")) = 1, " (ASSIGNED)", " (AVAILABLE)")
                    roleList.Rows.Add(displayRow)
                Next

                roleComboBox.DataSource = roleList
                roleComboBox.DisplayMember = "RoleDisplayName"
                roleComboBox.ValueMember = "RoleID"
                roleComboBox.SelectedIndex = 0
                Return True
            Catch ex As Exception
                roleComboBox.DataSource = Nothing
                ShowResult("ROLES LOOKUP FAILED" & Environment.NewLine & ex.Message)
                Return False
            Finally
                loadingRoles = False
            End Try
        End Function

        Private Sub LoadTableChoices()
            Dim tables = DataAccess.GetExposedPageChoices()
            For Each tableRow As DataRow In tables.Rows
                tableRow("Table_Alias") = Convert.ToString(tableRow("Table_Alias")).ToUpperInvariant()
            Next
            Dim placeholder = tables.NewRow()
            placeholder("ID") = 0
            placeholder("SchemaID") = 0
            placeholder("Table_Alias") = "MAKE A SELECTION"
            placeholder("DB_Table") = String.Empty
            placeholder("WindowOrPage") = String.Empty
            tables.Rows.InsertAt(placeholder, 0)

            tableComboBox.DataSource = tables
            tableComboBox.DisplayMember = "Table_Alias"
            tableComboBox.ValueMember = "ID"
            tableComboBox.SelectedIndex = 0
        End Sub

        Private Sub TableComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If loadingRoles Then Return
            HideCorrectionControls()
            comparisonGrid.Rows.Clear()
            explainButton.Enabled = selectedUserId > 0 AndAlso tableComboBox.SelectedIndex > 0 AndAlso roleComboBox.SelectedIndex > 0
        End Sub

        Private Sub RoleComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If loadingRoles Then Return
            HideCorrectionControls()
            comparisonGrid.Rows.Clear()
            explainButton.Enabled = selectedUserId > 0 AndAlso tableComboBox.SelectedIndex > 0 AndAlso roleComboBox.SelectedIndex > 0
        End Sub

        Private Sub ExplainButton_Click(sender As Object, e As EventArgs)
            comparisonGrid.Rows.Clear()
            comparisonGrid.Visible = False
            Dim selectedTable = TryCast(tableComboBox.SelectedItem, DataRowView)
            Dim selectedRole = TryCast(roleComboBox.SelectedItem, DataRowView)
            If selectedUserId <= 0 OrElse selectedTable Is Nothing OrElse selectedRole Is Nothing Then
                ShowResult("Find a user and select both a table and a role first.")
                Return
            End If

            Try
                Dim dbTable = Convert.ToString(selectedTable("DB_Table"))
                Dim tableAlias = Convert.ToString(selectedTable("Table_Alias"))
                Dim schemaId = Convert.ToInt32(selectedTable("SchemaID"))
                Dim roleId = Convert.ToInt32(selectedRole("RoleID"))
                Dim roleName = Convert.ToString(selectedRole("RoleName"))
                Dim assigned = Convert.ToInt32(selectedRole("IsAssigned")) = 1
                Dim assignRole = Not assigned
                Dim analysisRegistration = DataAccess.GetRegistrationById(registrationId)
                Dim analysisRegistrationName = If(analysisRegistration Is Nothing OrElse String.IsNullOrWhiteSpace(analysisRegistration.RegName),
                                                  registrationId.ToString(), analysisRegistration.RegName.Trim()).ToUpperInvariant()
                Dim hasRoleDetail = DataAccess.GetAccessDiagnosticRoleDetails(roleId, registrationId).AsEnumerable().Any(
                    Function(row) String.Equals(Convert.ToString(row("DB_Table")), dbTable, StringComparison.OrdinalIgnoreCase))
                Dim capabilities As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
                For Each permission In New String() {"Can_Read", "Can_Create", "Can_Update", "Can_Delete", "Can_UseQBE", "Can_ViewAllRecords", "Can_ViewOnlyMyRecords"}
                    capabilities(permission) = GetNewRoleTableDefault(permission)
                Next

                Dim roleDetails = DataAccess.GetAccessDiagnosticRoleDetails(roleId, registrationId)
                Dim roleDetail = roleDetails.AsEnumerable().FirstOrDefault(
                    Function(row) String.Equals(Convert.ToString(row("DB_Table")), dbTable, StringComparison.OrdinalIgnoreCase))
                If roleDetail IsNot Nothing Then
                    For Each permission In capabilities.Keys.ToList()
                        capabilities(permission) = Convert.ToBoolean(roleDetail(permission))
                    Next
                End If

                Dim result As New StringBuilder()
                result.AppendLine("USER ACCESS EXPLANATION")
                result.AppendLine("User: " & userLabel.Text)
                result.AppendLine("Registration: " & analysisRegistrationName)
                result.AppendLine("Table / menu: " & tableAlias)
                result.AppendLine("Role: " & roleName & If(assigned, " (assigned)", " (not assigned)"))
                result.AppendLine("Table configuration: " & If(hasRoleDetail, "Present", "Missing"))
                result.AppendLine()
                result.AppendLine("USER ROLES")
                Dim userRoles = DataAccess.GetAccessDiagnosticRegistrationRoles(selectedUserId, registrationId)
                Dim roleNameWidth = userRoles.AsEnumerable().Select(Function(row) Convert.ToString(row("RoleName")).Length).DefaultIfEmpty(0).Max()
                For Each userRole As DataRow In userRoles.Rows
                    Dim displayRoleName = Convert.ToString(userRole("RoleName")).ToUpperInvariant()
                    result.AppendLine(displayRoleName.PadRight(roleNameWidth) &
                                      If(Convert.ToInt32(userRole("IsAssigned")) = 1, " (ASSIGNED)", " (AVAILABLE)"))
                Next
                result.AppendLine()
                If Not assigned Then
                    result.AppendLine("THIS ROLE IS AVAILABLE FOR THE REGISTRATION BUT IS NOT ASSIGNED TO THIS USER.")
                    result.AppendLine("SELECT THE APPLY ACCESS CHANGES BUTTON TO ADDRESS THIS ISSUE.")
                    result.AppendLine()
                ElseIf Not hasRoleDetail Then
                    result.AppendLine("THIS ROLE IS ASSIGNED TO THIS ORGANIZATION BUT IS NOT CONFIGURED FOR THIS TABLE.")
                    result.AppendLine("SELECT THE APPLY ACCESS CHANGES BUTTON TO ADDRESS THIS ISSUE.")
                    result.AppendLine()
                End If

                beforePermissions.Clear()
                beforeWasConfigured = hasRoleDetail
                beforeRoleWasAssigned = assigned
                For Each permission In capabilities
                    beforePermissions(permission.Key) = permission.Value
                Next
                result.AppendLine()
                result.AppendLine("ANALYSIS COMPLETED")
                If Not assigned OrElse Not hasRoleDetail Then
                    comparisonGrid.Visible = False
                    Dim registration = DataAccess.GetRegistrationById(registrationId)
                    Dim registrationName = If(registration Is Nothing OrElse String.IsNullOrWhiteSpace(registration.RegName),
                                              registrationId.ToString(), registration.RegName.Trim()).ToUpperInvariant()
                    applyAccessChangesButton.Visible = True
                Else
                    comparisonGrid.Visible = False
                    applyAccessChangesButton.Visible = False
                End If
                ShowResult(result.ToString())
                If assigned AndAlso hasRoleDetail Then
                    MessageBox.Show(Me,
                                    "NO ACCESS ISSUES FOUND." & Environment.NewLine &
                                    "THE SELECTED USER HAS ACCESS TO THE SELECTED TABLE." & Environment.NewLine &
                                    Environment.NewLine &
                                    "PERHAPS THERE IS NO MENU ITEM FOR THIS TABLE.",
                                    "ACCESS CHECK COMPLETE",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information)
                End If
            Catch ex As Exception
                ShowResult("The access explanation could not be completed." & Environment.NewLine & ex.Message)
            End Try
        End Sub

        Private Shared Sub AppendPermissionExplanation(result As StringBuilder, permissionName As String, allowed As Boolean)
            result.AppendLine(permissionName & ": " & If(allowed, "Allowed", "Denied"))
        End Sub

        Private Shared Function GetNewRoleTableDefault(permissionName As String) As Boolean
            Select Case permissionName
                Case "Can_Create", "Can_Update", "Can_Delete", "Can_UseQBE"
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        Private Sub PromptForAccessChanges(roleId As Integer, roleName As String, dbTable As String, schemaId As Integer,
                                           assignRole As Boolean, hasRoleDetail As Boolean, registrationName As String)
            Dim changes As New List(Of String)()
            If Not hasRoleDetail Then
                changes.Add((changes.Count + 1).ToString() & ". ADD " & dbTable.ToUpperInvariant() & " TO THE " & roleName.ToUpperInvariant() & " ROLE.")
            End If
            If assignRole Then
                changes.Add((changes.Count + 1).ToString() & ". ASSIGN THE " & roleName.ToUpperInvariant() & " ROLE TO " & userLabel.Text.ToUpperInvariant() & ".")
            End If

            Dim confirmationText = "THE FOLLOWING CHANGES WILL BE MADE:" & Environment.NewLine &
                                   Environment.NewLine & String.Join(Environment.NewLine & Environment.NewLine, changes) &
                                   Environment.NewLine & "REGISTRATION: " & registrationName & "." &
                                   Environment.NewLine & Environment.NewLine &
                                   "DO YOU WANT TO APPLY THESE CHANGES?"
            If MessageBox.Show(Me,
                               confirmationText,
                               "CONFIRM ACCESS CHANGES",
                               MessageBoxButtons.YesNo,
                               MessageBoxIcon.Question) = DialogResult.Yes Then
                ApplyAccessChanges(roleId, dbTable, schemaId, assignRole)
            End If
        End Sub

        Private Sub ApplyAccessChangesButton_Click(sender As Object, e As EventArgs)
            Dim selectedTable = TryCast(tableComboBox.SelectedItem, DataRowView)
            Dim selectedRole = TryCast(roleComboBox.SelectedItem, DataRowView)
            If selectedUserId <= 0 OrElse selectedTable Is Nothing OrElse selectedRole Is Nothing Then
                ShowResult("FIND A USER AND SELECT A TABLE AND ROLE FIRST.")
                Return
            End If

            Dim roleId = Convert.ToInt32(selectedRole("RoleID"))
            Dim roleName = Convert.ToString(selectedRole("RoleName"))
            Dim dbTable = Convert.ToString(selectedTable("DB_Table"))
            Dim schemaId = Convert.ToInt32(selectedTable("SchemaID"))
            Dim isAssigned = Convert.ToInt32(selectedRole("IsAssigned")) = 1
            Dim hasRoleDetail = DataAccess.GetAccessDiagnosticRoleDetails(roleId, registrationId).AsEnumerable().Any(
                Function(row) String.Equals(Convert.ToString(row("DB_Table")), dbTable, StringComparison.OrdinalIgnoreCase))
            Dim registration = DataAccess.GetRegistrationById(registrationId)
            Dim registrationName = If(registration Is Nothing OrElse String.IsNullOrWhiteSpace(registration.RegName),
                                      registrationId.ToString(), registration.RegName.Trim()).ToUpperInvariant()
            PromptForAccessChanges(roleId, roleName, dbTable, schemaId, Not isAssigned, hasRoleDetail, registrationName)
        End Sub

        Private Sub ApplyAccessChanges(roleId As Integer, dbTable As String, schemaId As Integer, assignRole As Boolean)
            Dim selectedTable = TryCast(tableComboBox.SelectedItem, DataRowView)
            Dim selectedRole = TryCast(roleComboBox.SelectedItem, DataRowView)
            If selectedUserId <= 0 OrElse selectedTable Is Nothing OrElse selectedRole Is Nothing Then
                ShowResult("Find a user and select both a table and a role first.")
                Return
            End If

            Dim requested As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            Dim existingDetails = DataAccess.GetAccessDiagnosticRoleDetails(roleId, registrationId)
            Dim existingDetail = existingDetails.AsEnumerable().FirstOrDefault(
                Function(row) String.Equals(Convert.ToString(row("DB_Table")), dbTable, StringComparison.OrdinalIgnoreCase))
            For Each permission In New String() {"Can_Create", "Can_Read", "Can_Update", "Can_Delete", "Can_UseQBE", "Can_ViewAllRecords", "Can_ViewOnlyMyRecords"}
                requested(permission) = If(existingDetail Is Nothing, GetNewRoleTableDefault(permission), Convert.ToBoolean(existingDetail(permission)))
            Next

            Try
                Dim trace = DataAccess.ApplyAccessDiagnosticChanges(selectedUserId, registrationId, roleId, schemaId, dbTable,
                                                                     requested, If(SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0), assignRole)
                If trace.Rows.Count = 0 Then Throw New InvalidOperationException("The access transaction returned no completion trace.")
                LoadRoles()
                roleComboBox.SelectedValue = roleId
                explainButton.Enabled = tableComboBox.SelectedIndex > 0 AndAlso roleComboBox.SelectedIndex > 0
                Dim afterDetails = DataAccess.GetAccessDiagnosticRoleDetails(roleId, registrationId)
                Dim afterDetail = afterDetails.AsEnumerable().FirstOrDefault(
                    Function(row) String.Equals(Convert.ToString(row("DB_Table")), dbTable, StringComparison.OrdinalIgnoreCase))
                If afterDetail Is Nothing Then Throw New InvalidOperationException("The table assignment could not be verified.")
                LoadPermissionComparison(beforePermissions, GetPermissionValues(afterDetail), beforeWasConfigured, beforeRoleWasAssigned, True, True)
                comparisonGrid.Visible = True
                comparisonFootnote.Visible = True
                applyAccessChangesButton.Visible = False
                MessageBox.Show(
                    Me,
                    "TABLE ADDED SUCCESSFULLY" & Environment.NewLine &
                    "CHECK THE EXPLANATION AREA FOR THE UPDATED ACCESS.",
                    "TABLE ASSIGNMENT COMPLETE",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information)
            Catch ex As Exception
                ShowResult("TABLE ASSIGNMENT FAILED" & Environment.NewLine & ex.Message.ToUpperInvariant())
            End Try
        End Sub

        Private Sub LoadPermissionComparison(beforeValues As IDictionary(Of String, Boolean),
                              afterValues As IDictionary(Of String, Boolean),
                              beforeConfigured As Boolean,
                              beforeRoleAssigned As Boolean,
                              afterRoleAssigned As Boolean,
                              afterTableConfigured As Boolean)
            comparisonGrid.Rows.Clear()
            comparisonGrid.Rows.Add("ROLE ASSIGNMENT",
                                    If(beforeRoleAssigned, "ASSIGNED", "NOT ASSIGNED"),
                                    If(afterValues Is Nothing, String.Empty, If(afterRoleAssigned, "ASSIGNED", "NOT ASSIGNED")))
            comparisonGrid.Rows.Add("TABLE CONFIGURATION",
                                    If(beforeConfigured, "PRESENT", "MISSING"),
                                    If(afterValues Is Nothing, String.Empty, If(afterTableConfigured, "PRESENT", "MISSING")))
            For Each permission In New (String, String)() {
                ("Can_Read", "Read records"),
                ("Can_Create", "Create records"),
                ("Can_Update", "Modify records"),
                ("Can_Delete", "Delete records"),
                ("Can_UseQBE", "Use QBE"),
                ("Can_ViewAllRecords", "View all records"),
                ("Can_ViewOnlyMyRecords", "View only my records")
            }
                Dim beforeText = If(Not beforeConfigured, "NA",
                                    If(beforeValues Is Nothing OrElse Not beforeValues.ContainsKey(permission.Item1), String.Empty, If(beforeValues(permission.Item1), "Yes", "No")))
                Dim afterText = If(afterValues Is Nothing OrElse Not afterValues.ContainsKey(permission.Item1), String.Empty, If(afterValues(permission.Item1), "Yes", "No"))
                comparisonGrid.Rows.Add(permission.Item2.ToUpperInvariant(), beforeText.ToUpperInvariant(), afterText.ToUpperInvariant())
            Next
            comparisonGrid.Height = comparisonGrid.ColumnHeadersHeight +
                                    (comparisonGrid.RowTemplate.Height * comparisonGrid.Rows.Count) + 2
        End Sub

        Private Shared Function GetPermissionValues(row As DataRow) As Dictionary(Of String, Boolean)
            Dim values As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            For Each permission In New String() {"Can_Read", "Can_Create", "Can_Update", "Can_Delete", "Can_UseQBE", "Can_ViewAllRecords", "Can_ViewOnlyMyRecords"}
                values(permission) = Convert.ToBoolean(row(permission))
            Next
            Return values
        End Function

        Private Sub ShowResult(message As String)
            resultTextBox.Text = If(message, String.Empty).ToUpperInvariant()
            resultTextBox.SelectionStart = 0
            resultTextBox.SelectionLength = 0
        End Sub

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub

        Private Sub HideCorrectionControls()
            comparisonGrid.Visible = False
            comparisonFootnote.Visible = False
            comparisonGrid.Rows.Clear()
            applyAccessChangesButton.Visible = False
        End Sub
    End Class
End Namespace
