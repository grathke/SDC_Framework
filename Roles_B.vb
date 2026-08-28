Option Strict On
Option Explicit On

Imports System.Data
Imports System.Drawing
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class Roles_B
        Inherits FW_Base_B

        Private Const DefaultSelectSql As String = "SELECT ID, RegistrationID, RoleName, IsActive, UpdatedOn FROM dbo.FW_Roles WHERE RegistrationID = @RegistrationID"

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly accessTableName As String
        Private ReadOnly titleLabel As Label
        Private activeSqlText As String = DefaultSelectSql
        Private currentDbTableName As String = String.Empty
        Private ReadOnly registrationLabel As Label
        Private ReadOnly sqlTextBox As TextBox
        Private ReadOnly registrationComboBox As ComboBox
        Private ReadOnly rolesGrid As DataGridView
        Private ReadOnly enumButton As Button
        Private ReadOnly showDeletedButton As Button
        Private ReadOnly restoreDeletedButton As Button
        Private ReadOnly showNormalButton As Button
        Private ReadOnly newButton As Button
        Private ReadOnly modifyButton As Button
        Private ReadOnly deleteButton As Button
        Private ReadOnly closeButton As Button
        Private showDeletedRecordsOnly As Boolean = False
        Private isLoadingRegistrations As Boolean = False
        Private missingMaintenanceKeyInResult As Boolean = False
        Private missingMaintenanceKeyWarningShown As Boolean = False
        Private Shared ReadOnly ShowDeletedText As String = "Show Deleted"
        Private Shared ReadOnly ShowNormalText As String = "Show Normal"
        Private ReadOnly initialRegistrationId As Integer

        Private Structure GridViewState
            Public HasSelection As Boolean
            Public SelectedRoleId As Integer
            Public SelectedRowOffsetFromTop As Integer
            Public FallbackFirstDisplayedIndex As Integer
        End Structure

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing, Optional tableName As String = "ROLES")
            MyBase.New(user, profile, tableName, False)

            currentUser = user
            accessProfile = profile
            accessTableName = If(String.IsNullOrWhiteSpace(tableName), "ROLES", tableName)
            initialRegistrationId = GetSessionRegistrationId()

            Me.Text = "Roles Listing"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.MinimumSize = New Size(560, 350)
            Me.ClientSize = New Size(620, 600)
            Me.BackColor = Color.White

            titleLabel = New Label() With {
                .Text = "ROLES LISTING",
                .Font = New Font("Segoe UI", 14.0F, FontStyle.Bold),
                .AutoSize = True,
                .Location = New Point(20, 15)
            }
            registrationLabel = New Label() With {
                .Text = "Registration:",
                .AutoSize = True,
                .Location = New Point(300, 18),
                .ForeColor = Color.DimGray
            }

            registrationComboBox = New ComboBox() With {
                .Location = New Point(390, 14),
                .Size = New Size(225, 26),
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .DropDownWidth = 280
            }

            sqlTextBox = New TextBox() With {
                .Location = New Point(-10000, -10000),
                .Size = New Size(1, 1),
                .Visible = False,
                .Enabled = False,
                .Multiline = True,
                .Text = DefaultSelectSql
            }

            rolesGrid = New DataGridView() With {
                .Location = New Point(20, 140),
                .Size = New Size(Me.ClientSize.Width - 40, Me.ClientSize.Height - 260),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right,
                .ReadOnly = False,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .MultiSelect = False,
                .RowHeadersVisible = False,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                .AutoGenerateColumns = True,
                .EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            }
            ApplyBrowseGridStandard(rolesGrid)

            enumButton = New Button() With {.Text = "Enum", .Size = New Size(80, 32), .Location = New Point(20, 84)}
            newButton = New Button() With {.Text = "New", .Size = New Size(80, 32), .Location = New Point(108, 84)}
            modifyButton = New Button() With {.Text = "Modify", .Size = New Size(80, 32), .Location = New Point(196, 84)}
            deleteButton = New Button() With {.Text = "Delete", .Size = New Size(80, 32), .Location = New Point(284, 84)}
            showDeletedButton = New Button() With {.Text = ShowDeletedText, .Size = New Size(130, 32), .Location = New Point(372, 84)}
            restoreDeletedButton = New Button() With {.Text = "Restore", .Size = New Size(64, 32), .Location = New Point(372, 84), .Visible = False}
            showNormalButton = New Button() With {.Text = "Normal", .Size = New Size(64, 32), .Location = New Point(438, 84), .Visible = False}
            closeButton = New Button() With {.Text = "Close", .Size = New Size(80, 32), .Location = New Point(372, 84)}

            ConfigureActionButton(enumButton)
            ConfigureActionButton(newButton)
            ConfigureActionButton(modifyButton)
            ConfigureActionButton(deleteButton)
            ConfigureActionButton(showDeletedButton)
            ConfigureActionButton(restoreDeletedButton)
            ConfigureActionButton(showNormalButton)
            ConfigureActionButton(closeButton)

            AddHandler registrationComboBox.SelectedIndexChanged, AddressOf RegistrationComboBox_SelectedIndexChanged
            AddHandler enumButton.Click, AddressOf EnumButton_Click
            AddHandler newButton.Click, AddressOf NewButton_Click
            AddHandler modifyButton.Click, AddressOf ModifyButton_Click
            AddHandler deleteButton.Click, AddressOf DeleteButton_Click
            AddHandler showDeletedButton.Click, AddressOf ShowDeletedButton_Click
            AddHandler restoreDeletedButton.Click, AddressOf RestoreDeletedButton_Click
            AddHandler showNormalButton.Click, AddressOf ShowNormalButton_Click
            AddHandler closeButton.Click, AddressOf CloseButton_Click
            AddHandler rolesGrid.CellDoubleClick, AddressOf RolesGrid_CellDoubleClick
            AddHandler rolesGrid.SelectionChanged, AddressOf RolesGrid_SelectionChanged
            AddHandler rolesGrid.CellBeginEdit, AddressOf RolesGrid_CellBeginEdit
            AddHandler Me.Resize, AddressOf RolesForm_Resize
            AddHandler Me.FormClosing, AddressOf Roles_B_FormClosing
            AddHandler Me.Load, AddressOf Roles_B_Load
            AddHandler Me.Shown, AddressOf Roles_B_Shown

            Me.Controls.Add(titleLabel)
            Me.Controls.Add(registrationLabel)
            Me.Controls.Add(registrationComboBox)
            Me.Controls.Add(sqlTextBox)
            Me.Controls.Add(rolesGrid)
            Me.Controls.Add(enumButton)
            Me.Controls.Add(newButton)
            Me.Controls.Add(modifyButton)
            Me.Controls.Add(deleteButton)
            Me.Controls.Add(showDeletedButton)
            Me.Controls.Add(restoreDeletedButton)
            Me.Controls.Add(showNormalButton)
            Me.Controls.Add(closeButton)

            RolesForm_Resize(Me, EventArgs.Empty)
        End Sub

        Private Sub Roles_B_Shown(sender As Object, e As EventArgs)
            BeginInvoke(New MethodInvoker(AddressOf FocusRolesGridOnEntry))
        End Sub

        Private Sub FocusRolesGridOnEntry()
            FocusGridForBrowseEntry(rolesGrid)
        End Sub

        Private Sub ConfigureActionButton(button As Button)
            button.FlatStyle = FlatStyle.Flat
            button.FlatAppearance.BorderColor = Color.FromArgb(170, 170, 170)
            button.FlatAppearance.BorderSize = 1
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 243, 255)
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 232, 248)
            button.BackColor = Color.White
            button.ForeColor = Color.Black
            button.UseVisualStyleBackColor = False
        End Sub

        Private Sub LoadInternalSqlFromRoleTable()
            Try
                Dim activeSession = SessionState.Current
                If Not activeSession.HasValue OrElse activeSession.Value.RegistrationID <= 0 Then
                    activeSqlText = DefaultSelectSql
                    sqlTextBox.Text = activeSqlText
                    Return
                End If

                Dim registrationId = activeSession.Value.RegistrationID
                Dim pageName = Me.GetType().Name
                currentDbTableName = DataAccess.GetDbTableFromRoleTableByWindowOrPage(registrationId, pageName).Trim()
                Dim displayTitle = EntityDisplayNameHelper.BuildEntityListingTitle(registrationId, currentDbTableName, "Roles")
                Me.Text = displayTitle
                titleLabel.Text = displayTitle
                Dim sql = DataAccess.GetTableSqlFromRoleTableByWindowOrPage(registrationId, pageName)

                If Not String.IsNullOrWhiteSpace(sql) Then
                    sql = sql.Trim()
                    activeSqlText = sql
                    sqlTextBox.Text = activeSqlText
                    Return
                End If

                activeSqlText = DefaultSelectSql
                sqlTextBox.Text = activeSqlText
            Catch
                currentDbTableName = String.Empty
                activeSqlText = DefaultSelectSql
                sqlTextBox.Text = activeSqlText
            End Try
        End Sub

        Private Sub RolesForm_Resize(sender As Object, e As EventArgs)
            Dim gridRightEdge As Integer = Me.ClientSize.Width - 20
            Dim comboRightEdge As Integer = gridRightEdge - 20
            registrationComboBox.Left = comboRightEdge - registrationComboBox.Width

            Dim buttonTop As Integer = 84
            Dim buttonLeft As Integer = 20
            Dim buttonGap As Integer = 8

            newButton.Top = buttonTop
            newButton.Left = buttonLeft
            modifyButton.Top = buttonTop
            modifyButton.Left = newButton.Right + buttonGap
            deleteButton.Top = buttonTop
            deleteButton.Left = modifyButton.Right + buttonGap
            enumButton.Top = buttonTop
            enumButton.Left = deleteButton.Right + buttonGap
            showDeletedButton.Top = buttonTop
            showDeletedButton.Left = enumButton.Right + buttonGap
            restoreDeletedButton.Top = buttonTop
            restoreDeletedButton.Left = showDeletedButton.Left
            showNormalButton.Top = buttonTop
            showNormalButton.Left = restoreDeletedButton.Right + 2

            closeButton.Top = buttonTop
            closeButton.Left = gridRightEdge - closeButton.Width

            rolesGrid.Width = Me.ClientSize.Width - 40
            rolesGrid.Height = Me.ClientSize.Height - 260
            GridColumnsManager.FitVisibleColumnsToAvailableWidth(rolesGrid)
        End Sub

        Private Sub ApplyAccess()
            enumButton.Enabled = True
            newButton.Enabled = True
            modifyButton.Enabled = True
            deleteButton.Enabled = True

            Dim showRegistrationPicker = IsAppAdminSession()
            registrationLabel.Visible = showRegistrationPicker
            registrationComboBox.Visible = showRegistrationPicker
            registrationComboBox.Enabled = showRegistrationPicker

            UpdateShowDeletedButtonState()
        End Sub

        Private Sub RolesGrid_SelectionChanged(sender As Object, e As EventArgs)
            Dim disableForMissingKey = missingMaintenanceKeyInResult

            If showDeletedRecordsOnly Then
                newButton.Enabled = False
                modifyButton.Enabled = False
                deleteButton.Enabled = False
                restoreDeletedButton.Enabled = restoreDeletedButton.Visible AndAlso rolesGrid.SelectedRows.Count > 0 AndAlso Not disableForMissingKey
                Return
            End If

            If rolesGrid.SelectedRows.Count > 0 Then
                modifyButton.Enabled = Not disableForMissingKey
                deleteButton.Enabled = Not disableForMissingKey
            Else
                modifyButton.Enabled = False
                deleteButton.Enabled = False
            End If

            restoreDeletedButton.Enabled = restoreDeletedButton.Visible AndAlso rolesGrid.SelectedRows.Count > 0 AndAlso Not disableForMissingKey
        End Sub

        Private Sub ConfigureIsActiveColumn(grid As DataGridView)
            If grid Is Nothing OrElse grid.Columns Is Nothing Then Return
            If Not grid.Columns.Contains("IsActive") Then Return
            Dim col = grid.Columns("IsActive")
            If Not TypeOf col Is DataGridViewCheckBoxColumn Then
                grid.Columns.Remove(col)
                Dim checkCol As New DataGridViewCheckBoxColumn() With {
                    .DataPropertyName = "IsActive",
                    .HeaderText = "Is Active",
                    .Width = 70,
                    .AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                    .ReadOnly = False
                }
                checkCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                grid.Columns.Add(checkCol)
            Else
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                col.ReadOnly = False
            End If
        End Sub

        Private Sub ConfigureRoleNameColumn(grid As DataGridView)
            If grid Is Nothing OrElse grid.Columns Is Nothing Then Return
            If Not grid.Columns.Contains("RoleName") Then Return
            Dim col = grid.Columns("RoleName")
            col.ReadOnly = True
        End Sub



        Private Sub RolesGrid_CellBeginEdit(sender As Object, e As DataGridViewCellCancelEventArgs)
            ' Prevent editing RoleName on the first row (\"New Role\" placeholder)
            If e.RowIndex = 0 AndAlso e.ColumnIndex >= 0 Then
                Dim col = rolesGrid.Columns(e.ColumnIndex)
                If col IsNot Nothing AndAlso String.Equals(col.DataPropertyName, "RoleName", StringComparison.OrdinalIgnoreCase) Then
                    e.Cancel = True
                End If
            End If
        End Sub

        Private Sub RolesGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then
                Return
            End If

            rolesGrid.ClearSelection()
            rolesGrid.Rows(e.RowIndex).Selected = True
            rolesGrid.CurrentCell = rolesGrid.Rows(e.RowIndex).Cells(e.ColumnIndex)

            If modifyButton.Visible AndAlso modifyButton.Enabled Then
                modifyButton.PerformClick()
            End If
        End Sub

        Private Sub Roles_B_Load(sender As Object, e As EventArgs)
            LoadInternalSqlFromRoleTable()
            If IsAppAdminSession() Then
                LoadRegistrations()
            End If
            ApplyAccess()
            RefreshGrid()
        End Sub

        Private Sub LoadRegistrations()
            Try
                isLoadingRegistrations = True
                
                RegistrationComboHelper.Populate(registrationComboBox, initialRegistrationId, True)
                RegistrationComboHelper.UpdateLabelForSelection(registrationLabel, registrationComboBox)
            Catch ex As Exception
                MessageBox.Show("Error loading registrations: " & ex.Message, "Error")
            Finally
                isLoadingRegistrations = False
            End Try
        End Sub

        Private Sub RegistrationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If Not isLoadingRegistrations Then
                RegistrationComboHelper.UpdateLabelForSelection(registrationLabel, registrationComboBox)
                RefreshGrid()
            End If
        End Sub

        Private Function PromptForTableName() As String
            Dim dlg As New Form()
            dlg.Text = "Select Database Table"
            dlg.StartPosition = FormStartPosition.CenterParent
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog
            dlg.MaximizeBox = False
            dlg.MinimizeBox = False
            dlg.ClientSize = New Size(320, 140)

            Dim lbl As New Label() With {
                .Text = "Select Table:",
                .Location = New Point(15, 15),
                .AutoSize = True
            }
            dlg.Controls.Add(lbl)

            ' Create ComboBox dropdown
            Dim combo As New ComboBox() With {
                .Location = New Point(15, 40),
                .Size = New Size(290, 25),
                .DropDownStyle = ComboBoxStyle.DropDownList
            }
            
            ' Load database tables with aliases
            Dim tablesWithAliases = DataAccess.GetDatabaseTablesWithAliases()
            For Each tableData In tablesWithAliases
                combo.Items.Add(tableData.TableName)
            Next
            
            If combo.Items.Count > 0 Then
                combo.SelectedIndex = 0
            End If
            
            dlg.Controls.Add(combo)

            Dim okBtn As New Button() With {
                .Text = "OK",
                .Location = New Point(145, 85),
                .Size = New Size(75, 32),
                .DialogResult = DialogResult.OK
            }
            dlg.Controls.Add(okBtn)

            Dim cancelBtn As New Button() With {
                .Text = "Cancel",
                .Location = New Point(230, 85),
                .Size = New Size(75, 32),
                .DialogResult = DialogResult.Cancel
            }
            dlg.Controls.Add(cancelBtn)

            dlg.AcceptButton = okBtn
            dlg.CancelButton = cancelBtn

            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso combo.SelectedIndex >= 0 Then
                Return combo.SelectedItem.ToString()
            Else
                Return String.Empty
            End If
        End Function

        Private Sub RefreshGrid(Optional selectedRoleId As Integer? = Nothing)
            Dim viewState = CaptureGridViewState()
            If selectedRoleId.HasValue Then
                viewState.HasSelection = True
                viewState.SelectedRoleId = selectedRoleId.Value
            End If

            Try
                Dim registrationId As Integer
                If Not TryGetActiveRegistrationId(registrationId) Then
                    rolesGrid.DataSource = Nothing
                    rolesGrid.Rows.Clear()
                    rolesGrid.Refresh()
                    rolesGrid.ClearSelection()
                    Return
                End If

                ApplyCrudButtonCaptions(registrationId)
                UpdateShowDeletedButtonState()

                Dim activeSql = GetActiveBaseSql()
                Dim dt = DataAccess.ExecuteCustomQuery(activeSql,
                                                       registrationId,
                                                       showDeletedRecordsOnly,
                                                       ResolveCurrentRoleFieldTableName(),
                                                       "ID")
                rolesGrid.DataSource = dt
                ApplyFriendlyColumnHeaders(rolesGrid)
                MaintenanceKeyGuard.HidePkColumn(rolesGrid)
                HideRegistrationIdColumn(rolesGrid)
                HideSoftDeleteColumns(rolesGrid)
                UpdateMaintenanceKeyAvailability()
                ConfigureRoleNameColumn(rolesGrid)
                ConfigureIsActiveColumn(rolesGrid)
                GridColumnsManager.FitVisibleColumnsToAvailableWidth(rolesGrid)
                titleLabel.Text = "ROLES LISTING"
                RestoreGridViewState(viewState)
                UpdateShowDeletedButtonState()
            Catch ex As Exception
                MessageBox.Show("Failed to load roles: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Protected Overrides Sub ApplyCrudButtonCaptions(registrationId As Integer)
            ' Roles_B uses simple CRUD captions from Registration only
            If registrationId <= 0 Then
                Return
            End If

            Dim captions = DataAccess.GetCrudButtonCaptions(registrationId)
            newButton.Text = captions.CreateCaption
            modifyButton.Text = captions.UpdateCaption
            deleteButton.Text = captions.DeleteCaption
        End Sub

        Private Sub HideRegistrationIdColumn(grid As DataGridView)
            Return
        End Sub

        Private Function CaptureGridViewState() As GridViewState
            Dim state As New GridViewState With {
                .HasSelection = False,
                .SelectedRoleId = 0,
                .SelectedRowOffsetFromTop = 0,
                .FallbackFirstDisplayedIndex = 0
            }

            If rolesGrid.Rows Is Nothing OrElse rolesGrid.Rows.Count = 0 Then
                Return state
            End If

            If rolesGrid.FirstDisplayedScrollingRowIndex >= 0 Then
                state.FallbackFirstDisplayedIndex = rolesGrid.FirstDisplayedScrollingRowIndex
            End If

            Dim selectedId = SelectedRoleId()
            If selectedId.HasValue Then
                state.HasSelection = True
                state.SelectedRoleId = selectedId.Value

                If rolesGrid.FirstDisplayedScrollingRowIndex >= 0 AndAlso rolesGrid.SelectedRows.Count > 0 Then
                    state.SelectedRowOffsetFromTop = rolesGrid.SelectedRows(0).Index - rolesGrid.FirstDisplayedScrollingRowIndex
                End If
            End If

            Return state
        End Function

        Private Sub RestoreGridViewState(state As GridViewState)
            If rolesGrid.Rows Is Nothing OrElse rolesGrid.Rows.Count = 0 Then
                Return
            End If

            rolesGrid.ClearSelection()

            If state.HasSelection Then
                Dim selectedIndex = FindRowIndexByRoleId(state.SelectedRoleId)
                If selectedIndex >= 0 Then
                    rolesGrid.Rows(selectedIndex).Selected = True
                    
                    ' Find first visible column
                    Dim firstVisibleCell As DataGridViewCell = Nothing
                    For Each col As DataGridViewColumn In rolesGrid.Columns
                        If col.Visible Then
                            firstVisibleCell = rolesGrid.Rows(selectedIndex).Cells(col.Index)
                            Exit For
                        End If
                    Next
                    
                    If firstVisibleCell IsNot Nothing Then
                        rolesGrid.CurrentCell = firstVisibleCell
                    End If

                    Dim targetTop = selectedIndex - state.SelectedRowOffsetFromTop
                    targetTop = Math.Max(0, Math.Min(targetTop, rolesGrid.RowCount - 1))

                    Try
                        rolesGrid.FirstDisplayedScrollingRowIndex = targetTop
                    Catch
                    End Try
                    Return
                End If
            End If

            ' If no selection exists, select the first row
            If rolesGrid.Rows.Count > 0 Then
                rolesGrid.Rows(0).Selected = True
                
                ' Find first visible column for CurrentCell
                Dim firstVisibleCell As DataGridViewCell = Nothing
                For Each col As DataGridViewColumn In rolesGrid.Columns
                    If col.Visible Then
                        firstVisibleCell = rolesGrid.Rows(0).Cells(col.Index)
                        Exit For
                    End If
                Next
                
                If firstVisibleCell IsNot Nothing Then
                    rolesGrid.CurrentCell = firstVisibleCell
                End If
            End If

            Dim fallbackTop = Math.Max(0, Math.Min(state.FallbackFirstDisplayedIndex, rolesGrid.RowCount - 1))
            Try
                rolesGrid.FirstDisplayedScrollingRowIndex = fallbackTop
            Catch
            End Try
        End Sub

        Private Function FindRowIndexByRoleId(roleId As Integer) As Integer
            Dim keyColumnName = ResolveMaintenanceKeyColumnName()
            If rolesGrid.Rows Is Nothing OrElse rolesGrid.Rows.Count = 0 OrElse String.IsNullOrWhiteSpace(keyColumnName) OrElse Not rolesGrid.Columns.Contains(keyColumnName) Then
                Return -1
            End If

            For i As Integer = 0 To rolesGrid.Rows.Count - 1
                Dim row = rolesGrid.Rows(i)
                Dim raw = row.Cells(keyColumnName).Value
                If raw Is Nothing OrElse IsDBNull(raw) Then
                    Continue For
                End If

                Dim rowId As Integer
                If Integer.TryParse(raw.ToString(), rowId) AndAlso rowId = roleId Then
                    Return i
                End If
            Next

            Return -1
        End Function

        Private Function SelectedRoleId() As Integer?
            If rolesGrid.SelectedRows.Count = 0 Then
                Return Nothing
            End If

            Dim row = rolesGrid.SelectedRows(0)
            Dim keyColumnName = ResolveMaintenanceKeyColumnName()
            If row Is Nothing OrElse String.IsNullOrWhiteSpace(keyColumnName) OrElse Not rolesGrid.Columns.Contains(keyColumnName) Then
                Return Nothing
            End If

            Dim raw = row.Cells(keyColumnName).Value
            If raw Is Nothing OrElse IsDBNull(raw) Then
                Return Nothing
            End If

            Dim roleId As Integer
            If Integer.TryParse(raw.ToString(), roleId) Then
                Return roleId
            End If

            Return Nothing
        End Function

        Private Function ResolveMaintenanceKeyColumnName() As String
            Dim table = TryCast(rolesGrid.DataSource, DataTable)
            If table IsNot Nothing AndAlso table.Columns IsNot Nothing Then
                If table.Columns.Contains("PK") Then
                    Return "PK"
                End If

                If table.Columns.Contains("ID") Then
                    Return "ID"
                End If
            End If

            Return MaintenanceKeyGuard.ResolveMaintenanceKeyColumnName(rolesGrid,
                                                                       New String() {"PK"})
        End Function

        Private Sub UpdateMaintenanceKeyAvailability()
            missingMaintenanceKeyInResult = String.IsNullOrWhiteSpace(ResolveMaintenanceKeyColumnName())
            MaintenanceKeyGuard.UpdateAvailabilityAndMaybeWarn(missingMaintenanceKeyInResult,
                                                               missingMaintenanceKeyWarningShown,
                                                               Me)
        End Sub

        Private Function EnsureMaintenanceKeyAvailable(actionName As String) As Boolean
            Return MaintenanceKeyGuard.EnsureAvailable(missingMaintenanceKeyInResult, actionName, Me)
        End Function

        Protected Overrides Function GetActiveBaseSql() As String
            If String.IsNullOrWhiteSpace(activeSqlText) Then
                Return DefaultSelectSql
            End If

            Return activeSqlText
        End Function

        Protected Overrides Function TryGetActiveRegistrationId(ByRef registrationId As Integer) As Boolean
            If registrationComboBox IsNot Nothing AndAlso registrationComboBox.Visible Then
                If TryResolveRegistrationIdFromCombo(registrationId) Then
                    Return True
                End If

                Return False
            End If

            If IsCompanyAdminOnlySession() Then
                registrationId = GetSessionRegistrationId()
                Return registrationId > 0
            End If

            If TryResolveRegistrationIdFromCombo(registrationId) Then
                Return True
            End If

            registrationId = GetSessionRegistrationId()
            Return registrationId > 0
        End Function

        Private Function TryResolveRegistrationIdFromCombo(ByRef registrationId As Integer) As Boolean
            registrationId = 0

            If registrationComboBox Is Nothing Then
                Return False
            End If

            If registrationComboBox.SelectedValue Is Nothing OrElse IsDBNull(registrationComboBox.SelectedValue) Then
                Return False
            End If

            If TypeOf registrationComboBox.SelectedValue Is Integer Then
                registrationId = CInt(registrationComboBox.SelectedValue)
                Return registrationId > 0
            End If

            If TypeOf registrationComboBox.SelectedValue Is Long Then
                registrationId = CInt(CLng(registrationComboBox.SelectedValue))
                Return registrationId > 0
            End If

            If TypeOf registrationComboBox.SelectedValue Is Decimal Then
                registrationId = Decimal.ToInt32(CDec(registrationComboBox.SelectedValue))
                Return registrationId > 0
            End If

            If TypeOf registrationComboBox.SelectedValue Is DataRowView Then
                Dim rowView = DirectCast(registrationComboBox.SelectedValue, DataRowView)
                Dim idValue = rowView("ID")
                If Integer.TryParse(Convert.ToString(idValue), registrationId) Then
                    Return registrationId > 0
                End If

                registrationId = GetSessionRegistrationId()
                Return registrationId > 0
            End If

            Try
                If Integer.TryParse(registrationComboBox.SelectedValue.ToString(), registrationId) Then
                    Return registrationId > 0
                End If
            Catch
                Return False
            End Try

            Return False
        End Function

        Private Function IsCompanyAdminOnlySession() As Boolean
            Dim activeSession = SessionState.Current
            If Not activeSession.HasValue Then
                Return False
            End If

            Return activeSession.Value.IsCompanyAdminRole AndAlso Not activeSession.Value.IsApplicationAdminRole
        End Function

        Protected Overrides Function ResolveCurrentRoleFieldTableName() As String
            If Not String.IsNullOrWhiteSpace(currentDbTableName) Then
                Return currentDbTableName.Trim()
            End If

            If accessTableName.Equals("ROLES", StringComparison.OrdinalIgnoreCase) Then
                Return "FW_Roles"
            End If

            Return accessTableName
        End Function

        Private Sub EnumButton_Click(sender As Object, e As EventArgs)
            RefreshGrid()
        End Sub

        Private Sub NewButton_Click(sender As Object, e As EventArgs)
            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                Return
            End If

            Try
                Using dlg As New Roles_C(registrationId)
                    If dlg.ShowDialog(Me) = DialogResult.OK Then
                        RefreshGrid()
                    End If
                End Using
            Catch ex As Exception
                MessageBox.Show("Error creating role: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub ModifyButton_Click(sender As Object, e As EventArgs)
            If Not EnsureMaintenanceKeyAvailable("Modify") Then
                Return
            End If

            Dim roleId = SelectedRoleId()
            If Not roleId.HasValue Then
                MessageBox.Show("Select a role first.", "Roles Maintenance", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Try
                OpenRoleMaintenance(rolesGrid.SelectedRows(0))
            Catch ex As Exception
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub OpenRoleMaintenance(row As DataGridViewRow)
            If row Is Nothing Then
                Return
            End If

            Dim roleId = SelectedRoleId()
            If Not roleId.HasValue Then
                MessageBox.Show("Select a role first.", "Roles Maintenance", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim roleName = Convert.ToString(row.Cells("RoleName").Value)
            If String.IsNullOrWhiteSpace(roleName) Then
                roleName = ""
            End If

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                MessageBox.Show("No active registration.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            Using dlg As New Roles_U(roleId.Value, registrationId, roleName)
                dlg.ShowDialog(Me)
            End Using

            RefreshGrid(roleId.Value)
        End Sub

        Private Sub DeleteButton_Click(sender As Object, e As EventArgs)
            If Not EnsureMaintenanceKeyAvailable("Delete") Then
                Return
            End If

            If Not DeletedViewGuard.TableSupportsDeletedView(ResolveCurrentRoleFieldTableName()) OrElse Not DeletedViewGuard.ResultHasDeletedFlagColumn(rolesGrid) Then
                MessageBox.Show("Delete is unavailable because the current table does not support DeletedFlag.", "Delete Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim roleId = SelectedRoleId()
            If Not roleId.HasValue Then
                MessageBox.Show("Select a role first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim selectedRow = rolesGrid.SelectedRows(0)
            Dim roleName = selectedRow.Cells("RoleName").Value.ToString()

            If MessageBox.Show("Delete this record?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                Try
                    Dim updatedBy = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)
                    DataAccess.DeleteRole(roleId.Value, updatedBy)
                    RefreshGrid()
                    FW_EntityCrudAdapter.ShowAutoClosingMessage(Me, "Role deleted.", "Delete", MessageBoxIcon.Information, 1000)
                Catch ex As Exception
                    MessageBox.Show($"Error deleting role: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End If
        End Sub

        Private Sub ShowDeletedButton_Click(sender As Object, e As EventArgs)
            If Not showDeletedButton.Visible OrElse Not showDeletedButton.Enabled Then
                Return
            End If

            showDeletedRecordsOnly = True
            UpdateShowDeletedButtonState()
            RefreshGrid()
        End Sub

        Private Sub RestoreDeletedButton_Click(sender As Object, e As EventArgs)
            If Not restoreDeletedButton.Visible OrElse Not showDeletedRecordsOnly Then
                Return
            End If

            If Not EnsureMaintenanceKeyAvailable("Restore") Then
                Return
            End If

            Dim roleId = SelectedRoleId()
            If Not roleId.HasValue Then
                MessageBox.Show("Select a role first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If MessageBox.Show("Restore this role?", "Confirm Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            Try
                Dim updatedBy = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)
                DataAccess.RestoreRole(roleId.Value, updatedBy)
                RefreshGrid()
                FW_EntityCrudAdapter.ShowAutoClosingMessage(Me, "Role restored.", "Restore", MessageBoxIcon.Information, 1000)
            Catch ex As Exception
                MessageBox.Show($"Error restoring role: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub ShowNormalButton_Click(sender As Object, e As EventArgs)
            If Not showNormalButton.Visible Then
                Return
            End If

            showDeletedRecordsOnly = False
            UpdateShowDeletedButtonState()
            RefreshGrid()
        End Sub

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub

        Private Sub Roles_B_FormClosing(sender As Object, e As FormClosingEventArgs)
            ' Central close pipeline for both Close button and window X.
        End Sub

        Private Sub UpdateShowDeletedButtonState()
            Dim supportsDeletedView = DeletedViewGuard.TableSupportsDeletedView(ResolveCurrentRoleFieldTableName()) AndAlso DeletedViewGuard.ResultHasDeletedFlagColumn(rolesGrid)
            If Not supportsDeletedView Then
                showDeletedRecordsOnly = False
            End If

            Dim isAdmin = IsAppAdminSession() OrElse IsCompanyAdminSession()
            Dim showSplitDeletedActions = isAdmin AndAlso supportsDeletedView AndAlso showDeletedRecordsOnly
            showDeletedButton.Visible = isAdmin AndAlso Not showSplitDeletedActions
            showDeletedButton.Enabled = supportsDeletedView
            showDeletedButton.Text = If(supportsDeletedView, ShowDeletedText, "Deleted N/A")

            restoreDeletedButton.Visible = showSplitDeletedActions
            showNormalButton.Visible = showSplitDeletedActions
            restoreDeletedButton.Enabled = showSplitDeletedActions AndAlso rolesGrid.SelectedRows.Count > 0 AndAlso Not missingMaintenanceKeyInResult
            showNormalButton.Enabled = showSplitDeletedActions

            newButton.Enabled = Not showDeletedRecordsOnly
            RolesGrid_SelectionChanged(Me, EventArgs.Empty)

            RolesForm_Resize(Me, EventArgs.Empty)
        End Sub

    End Class
End Namespace
