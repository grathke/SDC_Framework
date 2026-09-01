Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class Roles_U
        Inherits Form

        Private _roleId As Integer
        Private _registrationId As Integer
        Private _roleName As String
        Private suppressRoleSelectionChange As Boolean
        Private suppressRightGridPermissionRules As Boolean
        Private suppressRightGridPersistence As Boolean
        Private suppressRoleFieldPersistence As Boolean

        ' Left side - Available tables
        Private leftLabel As Label
        Private leftGrid As DataGridView

        ' Right side - Role permissions
        Private rightLabel As Label
        Private rightGrid As DataGridView

        ' Role settings controls
        Private roleNameLabel As Label
        Private roleNameComboBox As ComboBox
        Private displayOrderLabel As Label
        Private displayOrderTextBox As TextBox
        Private isActiveCheckBox As CheckBox
        Private typApplicationAdminCheckBox As CheckBox
        Private typCompanyAdminCheckBox As CheckBox

        ' Bottom grid - Role Fields
        Private roleFieldsLabel As Label
        Private roleFieldsGrid As DataGridView
        Private _roleFieldsTable As DataTable

        ' Buttons
        Private addTableButton As Button
        Private removeTableButton As Button
        Private okButton As Button
        Private Shadows cancelButton As Button
        Private ReadOnly focusOriginalBackColors As New Dictionary(Of Control, Color)()

        Public Sub New(roleId As Integer, registrationId As Integer, roleName As String)
            _roleId = roleId
            _registrationId = registrationId
            _roleName = roleName

            InitializeComponent()
            LoadRoleSelector()
            DataAccess.SyncRoleSchemaWithDatabase()
            LoadLeftGrid()
            LoadRightGrid()
            LoadDisplayOrder()
            LoadRoleFieldsGrid()
        End Sub

        ''' Roles_U does not inherit Base_U, so it owns its caption. Kept in one place here for the
        ''' same reason Base_U pages use BuildMaintenanceTitle.
        Private Function BuildRolesTitle() As String
            Return $"Edit Role: {_roleName}"
        End Function

        Private Sub InitializeComponent()
            Me.Text = BuildRolesTitle()
            Me.ClientSize = New Size(1160, 855)
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False

            ' Role Name
            roleNameLabel = New Label With {
                .Text = "Role Name:",
                .Location = New Point(10, 10),
                .Size = New Size(100, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(roleNameLabel)

            roleNameComboBox = New ComboBox With {
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Location = New Point(120, 10),
                .Size = New Size(300, 20),
                .FormattingEnabled = True
            }
            AddHandler roleNameComboBox.SelectedIndexChanged, AddressOf RoleNameComboBox_SelectedIndexChanged
            Me.Controls.Add(roleNameComboBox)

            ' Display Order
            displayOrderLabel = New Label With {
                .Text = "Display Order:",
                .Location = New Point(440, 10),
                .Size = New Size(120, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(displayOrderLabel)

            displayOrderTextBox = New TextBox With {
                .Location = New Point(570, 10),
                .Size = New Size(80, 20),
                .BorderStyle = BorderStyle.FixedSingle
            }
            NumericTextBoxHelper.ConfigureWholeNumberOnly(displayOrderTextBox)
            AddHandler displayOrderTextBox.TextChanged, AddressOf DisplayOrderTextBox_TextChanged
            Me.Controls.Add(displayOrderTextBox)

            isActiveCheckBox = New CheckBox With {
                .Name = "CheckBox_IsActive",
                .Text = "Is Active",
                .Location = New Point(690, 10),
                .Size = New Size(90, 20),
                .TabStop = False,
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(isActiveCheckBox)

            typApplicationAdminCheckBox = New CheckBox With {
                .Text = "App Admin",
                .Location = New Point(795, 10),
                .Size = New Size(105, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(typApplicationAdminCheckBox)

            typCompanyAdminCheckBox = New CheckBox With {
                .Text = "Company Admin",
                .Location = New Point(910, 10),
                .Size = New Size(130, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(typCompanyAdminCheckBox)

            ' Left panel label
            leftLabel = New Label With {
                .Text = "Available Tables:",
                .Location = New Point(10, 40),
                .Size = New Size(160, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(leftLabel)

            ' Left grid
            leftGrid = New DataGridView With {
                .Location = New Point(10, 65),
                .Size = New Size(160, 250),
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .ReadOnly = True,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .AutoGenerateColumns = False
            }
            ApplyLightBlueHeaderStyle(leftGrid)
            leftGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            leftGrid.RowHeadersVisible = False
            leftGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .DataPropertyName = "Table_Alias",
                .HeaderText = "Table",
                .ReadOnly = True
            })
            Me.Controls.Add(leftGrid)

            ' Button - Add Table
            addTableButton = New Button With {
                .Text = "Add ▶",
                .Location = New Point(180, 150),
                .Size = New Size(70, 35),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            AddHandler addTableButton.Click, AddressOf AddTableButton_Click
            Me.Controls.Add(addTableButton)

            ' Button - Remove Table
            removeTableButton = New Button With {
                .Text = "◀ Remove",
                .Location = New Point(180, 190),
                .Size = New Size(70, 35),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            AddHandler removeTableButton.Click, AddressOf RemoveTableButton_Click
            Me.Controls.Add(removeTableButton)

            ' Right panel label
            rightLabel = New Label With {
                .Text = "Role Permissions (Editable):",
                .Location = New Point(260, 40),
                .Size = New Size(450, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(rightLabel)

            ' Right grid
            rightGrid = New DataGridView With {
                .Location = New Point(260, 65),
                .Size = New Size(890, 250),
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .ReadOnly = False,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .AutoGenerateColumns = False
            }
            ApplyLightBlueHeaderStyle(rightGrid)
            rightGrid.RowHeadersVisible = False
            ' Hidden data columns (needed for cell editing logic)
            rightGrid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "ID", .DataPropertyName = "ID", .HeaderText = "ID", .Visible = False})
            rightGrid.Columns.Add(New DataGridViewTextBoxColumn() With {.Name = "SchemaID", .DataPropertyName = "SchemaID", .HeaderText = "SchemaID", .Visible = False})
            ' Visible columns
            rightGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "Table_Alias",
                .DataPropertyName = "Table_Alias",
                .HeaderText = "Table",
                .Width = 160,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                .ReadOnly = True
            })
            For Each def In New (String, String)() {
                ("Can_Create", "Create"),
                ("Can_Read", "Read Only"),
                ("Can_Update", "Update"),
                ("Can_Delete", "Delete"),
                ("Can_ViewAllRecords", "By RegID"),
                ("Can_ViewOnlyMyRecords", "View Mine"),
                ("Can_UseQBE", "Use QBE")
            }
                Dim col As New DataGridViewCheckBoxColumn() With {
                    .Name = def.Item1,
                    .DataPropertyName = def.Item1,
                    .HeaderText = def.Item2,
                    .Width = 70,
                    .AutoSizeMode = DataGridViewAutoSizeColumnMode.None
                }
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                rightGrid.Columns.Add(col)
            Next
            rightGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "OverrideCaption",
                .DataPropertyName = "OverrideCaption",
                .HeaderText = "Menu Caption",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            })
            AddHandler rightGrid.CellEndEdit, AddressOf RightGrid_CellEndEdit
            AddHandler rightGrid.RowValidated, AddressOf RightGrid_RowValidated
            AddHandler rightGrid.CurrentCellDirtyStateChanged, AddressOf RightGrid_CurrentCellDirtyStateChanged
            AddHandler rightGrid.CellValueChanged, AddressOf RightGrid_CellValueChanged
            AddHandler rightGrid.SelectionChanged, AddressOf RightGrid_SelectionChanged
            Me.Controls.Add(rightGrid)

            ' OK Button
            okButton = New Button With {
                .Text = "OK",
                .Location = New Point(910, 800),
                .Size = New Size(80, 35)
            }
            AddHandler okButton.Click, AddressOf OkButton_Click
            Me.Controls.Add(okButton)

            ' Cancel Button
            cancelButton = New Button With {
                .Text = "Cancel",
                .Location = New Point(1000, 800),
                .Size = New Size(80, 35),
                .DialogResult = DialogResult.Cancel
            }
            AddHandler cancelButton.Click, AddressOf CancelButton_Click
            Me.Controls.Add(cancelButton)
            Me.CancelButton = cancelButton
            AddHandler Me.Shown, AddressOf Roles_U_Shown

            ' Role Fields section label
            roleFieldsLabel = New Label With {
                .Text = "Role Fields (Field-Level Permissions):",
                .Location = New Point(10, 328),
                .Size = New Size(400, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(roleFieldsLabel)

            ' Role Fields grid
            roleFieldsGrid = New DataGridView With {
                .Location = New Point(10, 353),
                .Size = New Size(1070, 430),
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .ReadOnly = False,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .AutoGenerateColumns = False
            }
            ApplyLightBlueHeaderStyle(roleFieldsGrid)
            roleFieldsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            roleFieldsGrid.RowHeadersVisible = False
            ' Hidden data columns - ID, SchemaID, TableName, FieldName exist in DataTable but not displayed
            roleFieldsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "FieldName",
                .DataPropertyName = "FieldName",
                .HeaderText = "Field",
                .Width = 180,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            })
            For Each rfDef In New (String, String, Integer, Boolean)() {
                ("CA_CanChange", "CA Can Change", 110, False),
                ("Can_Create", "Create", 60, False),
                ("Can_Read", "Read", 60, False),
                ("Can_Update", "Update", 60, False),
                ("IsActive", "Active", 60, False),
                ("IsRequired", "Required", 60, False),
                ("IsUnique", "Unique", 60, False),
                ("Make_Invisible", "Hide", 60, False)
            }
                Dim rfCol As New DataGridViewCheckBoxColumn() With {
                    .Name = rfDef.Item1,
                    .DataPropertyName = rfDef.Item1,
                    .HeaderText = rfDef.Item2,
                    .Width = rfDef.Item3,
                    .AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                    .ThreeState = rfDef.Item4
                }
                rfCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                roleFieldsGrid.Columns.Add(rfCol)
            Next
            roleFieldsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "OrderBy",
                .DataPropertyName = "OrderBy",
                .HeaderText = "Order",
                .Width = 58,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            })
            roleFieldsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "OverrideCaption",
                .DataPropertyName = "OverrideCaption",
                .HeaderText = "Control Override Caption",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            })
            AddHandler roleFieldsGrid.CellEndEdit, AddressOf RoleFieldsGrid_CellEndEdit
            AddHandler roleFieldsGrid.CellFormatting, AddressOf RoleFieldsGrid_CellFormatting
            AddHandler roleFieldsGrid.CurrentCellDirtyStateChanged, AddressOf RoleFieldsGrid_CurrentCellDirtyStateChanged
            AddHandler roleFieldsGrid.CellValueChanged, AddressOf RoleFieldsGrid_CellValueChanged
            AddHandler roleFieldsGrid.EditingControlShowing, AddressOf RoleFieldsGrid_EditingControlShowing
            AddHandler Me.FormClosing, AddressOf Roles_U_FormClosing
            Me.Controls.Add(roleFieldsGrid)
        End Sub

        Private Sub LoadLeftGrid()
            Try
                Dim table = DataAccess.GetRoleSchemaTable()
                leftGrid.DataSource = table
            Catch ex As Exception
                MessageBox.Show("Error loading available tables: " & ex.Message, "Error")
            End Try
        End Sub

        Private Sub LoadRightGrid()
            Try
                Dim table = DataAccess.GetRoleDetailsForRole(_roleId, _registrationId)
                rightGrid.DataSource = table
            Catch ex As Exception
                MessageBox.Show("Error loading role permissions: " & ex.Message, "Error")
            End Try
        End Sub

        Private Sub AddTableButton_Click(sender As Object, e As EventArgs)
            If leftGrid.SelectedRows.Count = 0 Then
                MessageBox.Show("Please select a table from the left grid.", "Selection Required")
                Return
            End If

            Try
                Dim selectedRowIndex = leftGrid.SelectedRows(0).Index
                Dim dt = CType(leftGrid.DataSource, DataTable)
                Dim sourceRow = dt.Rows(selectedRowIndex)
                Dim roleSchemaId = CInt(sourceRow("ID"))
                Dim dbTable = CStr(sourceRow("DB_Table"))
                Dim tableAlias = If(sourceRow("Table_Alias") Is DBNull.Value,
                                    String.Empty,
                                    Convert.ToString(sourceRow("Table_Alias"), Globalization.CultureInfo.InvariantCulture)).Trim()
                If String.IsNullOrWhiteSpace(tableAlias) Then
                    tableAlias = DataAccess.FormatTableNameAsAlias(dbTable)
                End If

                ' Check if this table already exists in the right grid
                Dim isNewTable = True
                Dim rightGridDt = TryCast(rightGrid.DataSource, DataTable)
                If rightGridDt IsNot Nothing Then
                    For Each row As DataRow In rightGridDt.Rows
                        If CInt(row("SchemaID")) = roleSchemaId Then
                            isNewTable = False
                            Exit For
                        End If
                    Next
                End If

                If isNewTable Then
                    ' Add the detail and initial fields atomically.
                    Dim newId = DataAccess.AddRoleTableWithFields(_roleId,
                                                                  _registrationId,
                                                                  roleSchemaId,
                                                                  dbTable,
                                                                  tableAlias,
                                                                  tableAlias)
                    If newId <= 0 Then
                        MessageBox.Show("Failed to add table permission.", "Error")
                        Return
                    End If
                    LoadRightGrid()
                    
                    ' Reload the role fields table so the selection change handler can filter properly
                    LoadRoleFieldsGrid()
                    
                    ' Select the row for this table in rightGrid to display its fields
                    Dim rightGridDtAfter = TryCast(rightGrid.DataSource, DataTable)
                    If rightGridDtAfter IsNot Nothing Then
                        For i = 0 To rightGrid.Rows.Count - 1
                            If CInt(rightGridDtAfter.Rows(i)("SchemaID")) = roleSchemaId Then
                                rightGrid.ClearSelection()
                                rightGrid.Rows(i).Selected = True
                                Exit For
                            End If
                        Next
                    End If
                    
                    MessageBox.Show("Table added and initial fields inserted.", "Success")
                Else
                    ' Table already exists - just select it and sync schema
                    Dim rightGridDtAfter = TryCast(rightGrid.DataSource, DataTable)
                    If rightGridDtAfter IsNot Nothing Then
                        For i = 0 To rightGrid.Rows.Count - 1
                            If CInt(rightGridDtAfter.Rows(i)("SchemaID")) = roleSchemaId Then
                                rightGrid.ClearSelection()
                                rightGrid.Rows(i).Selected = True
                                Exit For
                            End If
                        Next
                    End If
                    
                    ' Refresh schema to sync any new columns (does LoadRoleFieldsGrid internally)
                    Dim syncCounts = RefreshSelectedRowSchema()
                    
                    MessageBox.Show($"Table Synced:{vbCrLf}{vbCrLf}Added:   {syncCounts.Item1}{vbCrLf}Deleted: {syncCounts.Item2}", "Success")
                End If
            Catch ex As Exception
                MessageBox.Show("Error adding table permission: " & ex.Message, "Error")
            End Try
        End Sub

        Private Sub RightGrid_CellEndEdit(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then
                Return
            End If

            Dim propagateCaptionOverride = String.Equals(rightGrid.Columns(e.ColumnIndex).Name, "OverrideCaption", StringComparison.OrdinalIgnoreCase)
            PersistRightGridRow(e.RowIndex, False, propagateCaptionOverride)
        End Sub

        Private Sub RightGrid_RowValidated(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then
                Return
            End If

            PersistRightGridRow(e.RowIndex, True, False)
        End Sub

        Private Sub RightGrid_CurrentCellDirtyStateChanged(sender As Object, e As EventArgs)
            If Not rightGrid.IsCurrentCellDirty OrElse rightGrid.CurrentCell Is Nothing Then
                Return
            End If

            If TypeOf rightGrid.CurrentCell.OwningColumn Is DataGridViewCheckBoxColumn Then
                rightGrid.CommitEdit(DataGridViewDataErrorContexts.Commit)
            End If
        End Sub

        Private Sub RightGrid_CellValueChanged(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then
                Return
            End If

            If TypeOf rightGrid.Columns(e.ColumnIndex) Is DataGridViewCheckBoxColumn Then
                If suppressRightGridPermissionRules Then
                    Return
                End If

                Dim changedColumnName = rightGrid.Columns(e.ColumnIndex).Name
                NormalizeRightGridPermissionRules(rightGrid.Rows(e.RowIndex), changedColumnName)
                PersistRightGridRow(e.RowIndex, False)
            End If
        End Sub

        Private Sub NormalizeRightGridPermissionRules(row As DataGridViewRow, changedColumnName As String)
            If row Is Nothing OrElse row.IsNewRow Then
                Return
            End If

            suppressRightGridPermissionRules = True
            Try
                If String.Equals(changedColumnName, "Can_Read", StringComparison.OrdinalIgnoreCase) AndAlso
                   GetGridBoolean(row, "Can_Read") Then
                    row.Cells("Can_Create").Value = False
                    row.Cells("Can_Update").Value = False
                  ElseIf (String.Equals(changedColumnName, "Can_Create", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(changedColumnName, "Can_Update", StringComparison.OrdinalIgnoreCase)) AndAlso
                      (GetGridBoolean(row, "Can_Create") OrElse GetGridBoolean(row, "Can_Update")) Then
                    row.Cells("Can_Read").Value = False
                End If
            Finally
                suppressRightGridPermissionRules = False
            End Try
        End Sub

        Private Sub PersistRightGridRow(rowIndex As Integer, applyDefaultOverrideCaption As Boolean, Optional propagateCaptionOverride As Boolean = False)
            If suppressRightGridPersistence Then
                Return
            End If

            Try
                If rowIndex < 0 OrElse rowIndex >= rightGrid.Rows.Count Then
                    Return
                End If

                Dim row = rightGrid.Rows(rowIndex)
                If row Is Nothing OrElse row.IsNewRow Then
                    Return
                End If

                Dim detailId = CInt(row.Cells("ID").Value)
                Dim tableAlias = If(row.Cells("Table_Alias").Value IsNot Nothing, row.Cells("Table_Alias").Value.ToString(), "")
                Dim tableCaption = If(row.Cells("OverrideCaption").Value IsNot Nothing, row.Cells("OverrideCaption").Value.ToString(), "")

                If applyDefaultOverrideCaption AndAlso String.IsNullOrWhiteSpace(tableCaption) Then
                    tableCaption = tableAlias
                    row.Cells("OverrideCaption").Value = tableCaption
                End If

                Dim canCreate = GetGridBoolean(row, "Can_Create")
                Dim canRead = GetGridBoolean(row, "Can_Read")
                Dim canUpdate = GetGridBoolean(row, "Can_Update")
                Dim canDelete = GetGridBoolean(row, "Can_Delete")
                Dim canViewAllRecords = GetGridBoolean(row, "Can_ViewAllRecords")
                Dim canViewOnlyMyRecords = GetGridBoolean(row, "Can_ViewOnlyMyRecords")
                Dim canUseQBE = GetGridBoolean(row, "Can_UseQBE")

                DataAccess.UpdateRoleDetails(detailId, tableAlias, tableCaption, canCreate, canRead, canUpdate, canDelete, canViewAllRecords, canViewOnlyMyRecords, canUseQBE, propagateCaptionOverride)
            Catch ex As Exception
                MessageBox.Show("Error updating permission: " & ex.Message, "Error")
            End Try
        End Sub

        Private Sub RemoveTableButton_Click(sender As Object, e As EventArgs)
            If rightGrid.SelectedRows.Count = 0 Then
                MessageBox.Show("Please select a row from the right grid to remove.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Try
                Dim selectedRowIndex = rightGrid.SelectedRows(0).Index
                Dim dt = TryCast(rightGrid.DataSource, DataTable)
                If dt Is Nothing OrElse selectedRowIndex >= dt.Rows.Count Then Return

                Dim detailId = CInt(dt.Rows(selectedRowIndex)("ID"))

                If MessageBox.Show("Are you sure you want to remove this permission?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                    suppressRightGridPersistence = True
                    suppressRoleFieldPersistence = True
                    Try
                        DataAccess.DeleteRoleDetails(detailId)
                        LoadRightGrid()
                        LoadRoleFieldsGrid()
                    Finally
                        suppressRightGridPersistence = False
                        suppressRoleFieldPersistence = False
                    End Try
                    MessageBox.Show("Permission removed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
            Catch ex As Exception
                MessageBox.Show($"Error: {ex.Message}", "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Function RefreshSelectedRowSchema() As Tuple(Of Integer, Integer)
            If rightGrid.SelectedRows.Count = 0 Then
                Return New Tuple(Of Integer, Integer)(0, 0)  ' Return zeros if no row selected
            End If

            Try
                Dim selectedRowIndex = rightGrid.SelectedRows(0).Index
                Dim dt = TryCast(rightGrid.DataSource, DataTable)
                If dt Is Nothing OrElse selectedRowIndex >= dt.Rows.Count Then
                    Return New Tuple(Of Integer, Integer)(0, 0)
                End If

                Dim schemaId = CInt(dt.Rows(selectedRowIndex)("SchemaID"))
                Dim dbTable = dt.Rows(selectedRowIndex)("DB_Table").ToString()
                Dim updatedBy = If(SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)

                Dim inserted, deleted As Integer
                Dim syncResult = DataAccess.SyncRoleFieldsWithSchema(schemaId, dbTable, _registrationId, _roleId, updatedBy, inserted, deleted)

                ' Always refresh the role fields grid to reflect changes
                LoadRoleFieldsGrid()
                
                ' Restore selection to the same row
                If selectedRowIndex < rightGrid.Rows.Count Then
                    rightGrid.ClearSelection()
                    rightGrid.Rows(selectedRowIndex).Selected = True
                    rightGrid.FirstDisplayedScrollingRowIndex = selectedRowIndex
                End If
                
                Return New Tuple(Of Integer, Integer)(inserted, deleted)
            Catch ex As Exception
                ' Silent catch - don't show errors during Add flow
                Return New Tuple(Of Integer, Integer)(0, 0)
            End Try
        End Function

        Private Sub LoadRoleSelector()
            Try
                suppressRoleSelectionChange = True
                Dim roles = DataAccess.GetRolesByRegistration(_registrationId)
                roleNameComboBox.DataSource = roles
                roleNameComboBox.DisplayMember = "RoleName"
                roleNameComboBox.ValueMember = "ID"

                If roles.Rows.Count > 0 Then
                    For i = 0 To roles.Rows.Count - 1
                        If Convert.ToInt32(roles.Rows(i)("ID")) = _roleId Then
                            roleNameComboBox.SelectedIndex = i
                            Exit For
                        End If
                    Next
                End If
            Catch ex As Exception
                MessageBox.Show("Error loading roles: " & ex.Message, "Error")
            Finally
                suppressRoleSelectionChange = False
            End Try
        End Sub

        Private Sub RoleNameComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If suppressRoleSelectionChange OrElse roleNameComboBox.SelectedValue Is Nothing Then
                Return
            End If

            Dim selectedRoleId As Integer
            If Not Integer.TryParse(roleNameComboBox.SelectedValue.ToString(), selectedRoleId) OrElse selectedRoleId <= 0 OrElse selectedRoleId = _roleId Then
                Return
            End If

            _roleId = selectedRoleId
            _roleName = roleNameComboBox.Text
            Me.Text = BuildRolesTitle()
            LoadRightGrid()
            LoadDisplayOrder()
            LoadRoleFieldsGrid()
        End Sub

        Private Sub DisplayOrderTextBox_TextChanged(sender As Object, e As EventArgs)
            Dim displayOrder As Integer = 0
            If String.IsNullOrWhiteSpace(displayOrderTextBox.Text) OrElse Not Integer.TryParse(displayOrderTextBox.Text, displayOrder) OrElse displayOrder = 0 Then
                displayOrderTextBox.BackColor = Color.FromArgb(255, 255, 200)
            Else
                displayOrderTextBox.BackColor = Color.White
            End If
        End Sub

        Private Sub LoadDisplayOrder()
            Try
                Dim displayOrder = DataAccess.GetRoleDisplayOrder(_roleId)
                displayOrderTextBox.Text = If(displayOrder = 0, "", displayOrder.ToString())
                displayOrderTextBox.BackColor = If(displayOrder = 0, Color.FromArgb(255, 255, 200), Color.White)
                isActiveCheckBox.Checked = DataAccess.GetRoleIsActive(_roleId)
                typApplicationAdminCheckBox.Checked = DataAccess.GetRoleTypAppAdmin(_roleId)
                typCompanyAdminCheckBox.Checked = DataAccess.GetRoleTypCompanyAdmin(_roleId)
            Catch ex As Exception
                displayOrderTextBox.Text = ""
                displayOrderTextBox.BackColor = Color.FromArgb(255, 255, 200)
                isActiveCheckBox.Checked = False
                typApplicationAdminCheckBox.Checked = False
                typCompanyAdminCheckBox.Checked = False
                MessageBox.Show("Error loading role settings: " & ex.Message, "Error")
            End Try
        End Sub

        Private Sub SaveRoleSettings()
            Try
                Dim newRoleName = roleNameComboBox.Text.Trim()
                Dim newDisplayOrder As Integer = 0

                If Not Integer.TryParse(displayOrderTextBox.Text, newDisplayOrder) Then
                    newDisplayOrder = 0
                End If

                If DataAccess.RoleNameExists(_registrationId, newRoleName, _roleId) Then
                    MessageBox.Show($"A role named '{newRoleName}' already exists.", "Duplicate Role Name", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                ' Update role name, display order, active status and type flags
                DataAccess.UpdateRoleNameAndDisplayOrder(_roleId, newRoleName, newDisplayOrder, isActiveCheckBox.Checked,
                    typApplicationAdminCheckBox.Checked, typCompanyAdminCheckBox.Checked)
                _roleName = newRoleName
                Me.Text = BuildRolesTitle()
            Catch ex As Exception
                MessageBox.Show($"Error saving role settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub OkButton_Click(sender As Object, e As EventArgs)
            CommitCurrentRightGridEdit()

            Dim validationErrors = ValidateRequiredFields()
            If validationErrors.Count > 0 Then
                Dim errorLines As New List(Of String)
                errorLines.Add("Please fill in the following required fields:")
                errorLines.Add("")
                For Each errorField In validationErrors
                    errorLines.Add("- " & errorField)
                Next
                MessageBox.Show(String.Join(vbCrLf, errorLines), "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            SaveRoleSettings()
            Me.DialogResult = DialogResult.OK
            Me.Close()
        End Sub

        Private Function ValidateRequiredFields() As List(Of String)
            Dim errors As New List(Of String)

            If String.IsNullOrWhiteSpace(roleNameComboBox.Text) Then
                errors.Add("Role Name")
            End If

            Dim displayOrder As Integer = 0
            If String.IsNullOrWhiteSpace(displayOrderTextBox.Text) OrElse Not Integer.TryParse(displayOrderTextBox.Text, displayOrder) OrElse displayOrder = 0 Then
                errors.Add("Display Order (must be a number greater than 0)")
            End If

            Return errors
        End Function

        Private Sub LoadRoleFieldsGrid()
            Try
                roleFieldsGrid.DataSource = Nothing
                _roleFieldsTable = DataAccess.GetRoleFieldsForRole(_roleId)
                _roleFieldsTable.DefaultView.RowFilter = String.Empty
                ApplySelectedRoleDetailFieldFilter()
            Catch ex As Exception
                MessageBox.Show("Error loading role fields: " & ex.Message, "Error")
            End Try
        End Sub

        Private Sub ApplySelectedRoleDetailFieldFilter()
            If _roleFieldsTable Is Nothing Then
                Return
            End If

            Dim selectedRow = If(rightGrid.SelectedRows.Count > 0, rightGrid.SelectedRows(0), Nothing)
            Dim rightTable = TryCast(rightGrid.DataSource, DataTable)
            If selectedRow Is Nothing OrElse rightTable Is Nothing OrElse selectedRow.Index < 0 OrElse selectedRow.Index >= rightTable.Rows.Count Then
                roleFieldsGrid.DataSource = _roleFieldsTable
                Return
            End If

            ' A field belongs to one role detail. Match on that key rather than on schema and table
            ' name: the name match cannot tell two rows apart when the same field exists on more
            ' than one table, and it is at the mercy of how the table name happens to be spelled.
            Dim selectedDetail = rightTable.Rows(selectedRow.Index)
            Dim detailId = Convert.ToInt32(selectedDetail("ID"))
            _roleFieldsTable.DefaultView.RowFilter =
                "RoleDetailID = " & detailId.ToString(Globalization.CultureInfo.InvariantCulture)
            roleFieldsGrid.DataSource = _roleFieldsTable.DefaultView
        End Sub

        Private Sub RightGrid_SelectionChanged(sender As Object, e As EventArgs)
            If rightGrid.SelectedRows.Count = 0 OrElse _roleFieldsTable Is Nothing Then Return
            Try
                roleFieldsGrid.DataSource = Nothing
                _roleFieldsTable.DefaultView.RowFilter = String.Empty
                ApplySelectedRoleDetailFieldFilter()
            Catch ex As Exception
                MessageBox.Show("Error filtering role fields: " & ex.Message, "Error")
            End Try
        End Sub

        Private Function ShouldFieldBeActive(row As DataGridViewRow) As Boolean
            Dim isRequired = GetCheckBoxValue(row, "IsRequired")
            Dim isUnique = GetCheckBoxValue(row, "IsUnique")
            Dim makeInvisible = GetCheckBoxValue(row, "Make_Invisible")
            Dim canCreate = GetCheckBoxValue(row, "Can_Create")
            Dim canRead = GetCheckBoxValue(row, "Can_Read")
            Dim canUpdate = GetCheckBoxValue(row, "Can_Update")
            Dim overrideCaption = If(row.Cells("OverrideCaption").Value IsNot Nothing, row.Cells("OverrideCaption").Value.ToString().Trim(), "")
            ' Can_ checkboxes are "permission granted" — unchecked means restricted → IsActive = True
            Return isRequired OrElse isUnique OrElse makeInvisible OrElse overrideCaption.Length > 0 OrElse Not canCreate OrElse Not canRead OrElse Not canUpdate
        End Function

        Private Function GetCheckBoxValue(row As DataGridViewRow, colName As String) As Boolean
            Dim cell = TryCast(row.Cells(colName), DataGridViewCheckBoxCell)
            If cell Is Nothing Then Return False
            ' Use EditedFormattedValue when editing, fall back to Value
            Dim val = If(cell.IsInEditMode, cell.EditedFormattedValue, cell.Value)
            Return ToBooleanValue(val)
        End Function

        Private Function GetGridBoolean(row As DataGridViewRow, columnName As String) As Boolean
            If row Is Nothing OrElse Not row.DataGridView.Columns.Contains(columnName) Then
                Return False
            End If

            Return ToBooleanValue(row.Cells(columnName).Value)
        End Function

        Private Shared Function ToBooleanValue(value As Object) As Boolean
            If value Is Nothing OrElse Convert.IsDBNull(value) Then
                Return False
            End If

            If TypeOf value Is Boolean Then
                Return DirectCast(value, Boolean)
            End If

            Dim parsed As Boolean
            If Boolean.TryParse(value.ToString(), parsed) Then
                Return parsed
            End If

            Return False
        End Function

        Private Sub RoleFieldsGrid_EditingControlShowing(sender As Object, e As DataGridViewEditingControlShowingEventArgs)
            Dim colName = roleFieldsGrid.CurrentCell?.OwningColumn?.Name
            If colName = "OverrideCaption" Then
                Dim tb = TryCast(e.Control, TextBox)
                If tb IsNot Nothing Then
                    RemoveHandler tb.TextChanged, AddressOf OverrideCaptionTextBox_TextChanged
                    AddHandler tb.TextChanged, AddressOf OverrideCaptionTextBox_TextChanged
                End If
            End If
        End Sub

        Private Sub OverrideCaptionTextBox_TextChanged(sender As Object, e As EventArgs)
            Dim rowIndex = roleFieldsGrid.CurrentCell?.RowIndex
            If rowIndex Is Nothing OrElse rowIndex < 0 Then Return
            Dim row = roleFieldsGrid.Rows(rowIndex.Value)
            Dim tb = TryCast(sender, TextBox)
            Dim hasText = tb IsNot Nothing AndAlso tb.Text.Trim().Length > 0
            Dim canCreate = GetCheckBoxValue(row, "Can_Create")
            Dim canRead = GetCheckBoxValue(row, "Can_Read")
            Dim canUpdate = GetCheckBoxValue(row, "Can_Update")
            Dim isRequired = GetCheckBoxValue(row, "IsRequired")
            Dim isUnique = GetCheckBoxValue(row, "IsUnique")
            Dim makeInvisible = GetCheckBoxValue(row, "Make_Invisible")
            row.Cells("IsActive").Value = hasText OrElse isRequired OrElse isUnique OrElse makeInvisible OrElse Not canCreate OrElse Not canRead OrElse Not canUpdate
            roleFieldsGrid.InvalidateCell(row.Cells("IsActive"))
        End Sub

        Private Sub RoleFieldsGrid_CurrentCellDirtyStateChanged(sender As Object, e As EventArgs)
            If roleFieldsGrid.IsCurrentCellDirty Then
                Dim colName = roleFieldsGrid.CurrentCell?.OwningColumn?.Name
                Dim triggerColumns = New HashSet(Of String) From {"IsRequired", "IsUnique", "Make_Invisible", "Can_Create", "Can_Read", "Can_Update"}
                If colName IsNot Nothing AndAlso triggerColumns.Contains(colName) Then
                    roleFieldsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit)
                End If
            End If
        End Sub

        Private Sub RoleFieldsGrid_CellValueChanged(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
            Dim colName = roleFieldsGrid.Columns(e.ColumnIndex).Name
            Dim triggerColumns = New HashSet(Of String) From {"IsRequired", "IsUnique", "Make_Invisible", "Can_Create", "Can_Read", "Can_Update", "OverrideCaption"}
            If triggerColumns.Contains(colName) Then
                Dim row = roleFieldsGrid.Rows(e.RowIndex)
                row.Cells("IsActive").Value = ShouldFieldBeActive(row)
                roleFieldsGrid.InvalidateCell(row.Cells("IsActive"))
            End If
        End Sub

        Private Sub RoleFieldsGrid_CellEndEdit(sender As Object, e As DataGridViewCellEventArgs)
            If suppressRoleFieldPersistence Then
                Return
            End If

            Try
                Dim row = roleFieldsGrid.Rows(e.RowIndex)
                Dim view = TryCast(roleFieldsGrid.DataSource, DataView)
                Dim dataRow = If(view IsNot Nothing, view(e.RowIndex).Row, TryCast(_roleFieldsTable.Rows(e.RowIndex), DataRow))
                Dim id = CInt(dataRow("ID"))
                Dim caCanChange = row.Cells("CA_CanChange").Value
                Dim canCreate = GetGridBoolean(row, "Can_Create")
                Dim canRead = GetGridBoolean(row, "Can_Read")
                Dim canUpdate = GetGridBoolean(row, "Can_Update")
                Dim isActive = GetGridBoolean(row, "IsActive")
                Dim isRequired = GetGridBoolean(row, "IsRequired")
                Dim isUnique = GetGridBoolean(row, "IsUnique")
                Dim makeInvisible = GetGridBoolean(row, "Make_Invisible")
                Dim orderByVal = row.Cells("OrderBy").Value
                Dim overrideCaption = If(row.Cells("OverrideCaption").Value IsNot Nothing, row.Cells("OverrideCaption").Value.ToString(), "")
                ' FriendlyFieldName is hidden in DataTable, read from dataRow not grid cells
                Dim friendlyFieldName = If(dataRow("FriendlyFieldName") IsNot Nothing, dataRow("FriendlyFieldName").ToString(), "")
                Dim updatedBy = If(SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)

                ' Auto-set IsActive when editing one of the trigger columns
                Dim editedColName = roleFieldsGrid.Columns(e.ColumnIndex).Name
                Dim triggerColumns = New HashSet(Of String) From {"IsRequired", "IsUnique", "Make_Invisible", "OverrideCaption"}
                If triggerColumns.Contains(editedColName) Then
                    isActive = ShouldFieldBeActive(row)
                    row.Cells("IsActive").Value = isActive
                End If

                Dim propagateCaptionOverride = String.Equals(editedColName, "OverrideCaption", StringComparison.OrdinalIgnoreCase)
                DataAccess.UpdateRoleField(id, caCanChange, canCreate, canRead, canUpdate, isActive, isRequired, isUnique, makeInvisible, orderByVal, overrideCaption, friendlyFieldName, updatedBy, propagateCaptionOverride)
            Catch ex As Exception
                MessageBox.Show("Error updating field setting: " & ex.Message, "Error")
            End Try
        End Sub

        Private Sub RoleFieldsGrid_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
            ' Format the FieldName column display value
            If e.ColumnIndex = 0 AndAlso e.Value IsNot Nothing Then
                e.Value = DataAccess.FormatFieldName(e.Value.ToString())
                e.FormattingApplied = True
            End If
        End Sub

        Private Sub CancelButton_Click(sender As Object, e As EventArgs)
            CommitCurrentRightGridEdit()
            Me.Close()
        End Sub

        Private Sub Roles_U_Shown(sender As Object, e As EventArgs)
            WireFocusIndicators(Me)
            Dim firstField = FindFirstFocusableField(Me)
            If firstField IsNot Nothing Then
                firstField.Focus()
                Dim textControl = TryCast(firstField, TextBoxBase)
                If textControl IsNot Nothing Then
                    textControl.Select(0, 0)
                End If
            End If
        End Sub

        Private Sub WireFocusIndicators(container As Control)
            For Each control As Control In container.Controls
                If IsFocusIndicatorControl(control) AndAlso Not focusOriginalBackColors.ContainsKey(control) Then
                    focusOriginalBackColors(control) = control.BackColor
                    AddHandler control.Enter, AddressOf FocusIndicator_Enter
                    AddHandler control.Leave, AddressOf FocusIndicator_Leave
                End If

                If control.HasChildren Then
                    WireFocusIndicators(control)
                End If
            Next
        End Sub

        Private Shared Function IsFocusIndicatorControl(control As Control) As Boolean
            Return TypeOf control Is TextBoxBase OrElse TypeOf control Is ComboBox OrElse TypeOf control Is CheckBox
        End Function

        Private Sub FocusIndicator_Enter(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control IsNot Nothing Then
                control.BackColor = Color.FromArgb(226, 245, 232)
            End If
        End Sub

        Private Sub FocusIndicator_Leave(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return

            Dim originalColor As Color
            If focusOriginalBackColors.TryGetValue(control, originalColor) Then
                control.BackColor = originalColor
            End If
        End Sub

        Private Shared Function FindFirstFocusableField(container As Control) As Control
            For Each control As Control In container.Controls
                If control.Visible AndAlso control.Enabled AndAlso control.TabStop AndAlso
                   (TypeOf control Is TextBoxBase OrElse TypeOf control Is ComboBox OrElse TypeOf control Is CheckBox) Then
                    Return control
                End If

                If control.HasChildren Then
                    Dim nestedField = FindFirstFocusableField(control)
                    If nestedField IsNot Nothing Then Return nestedField
                End If
            Next

            Return Nothing
        End Function

        Private Sub Roles_U_FormClosing(sender As Object, e As FormClosingEventArgs)
            CommitCurrentRightGridEdit()
            If e.CloseReason = CloseReason.UserClosing Then
                Me.DialogResult = DialogResult.Cancel
            End If
        End Sub

        Private Sub CommitCurrentRightGridEdit()
            If rightGrid Is Nothing OrElse rightGrid.CurrentCell Is Nothing Then
                Return
            End If

            Dim rowIndex = rightGrid.CurrentCell.RowIndex
            If rightGrid.IsCurrentCellDirty Then
                rightGrid.CommitEdit(DataGridViewDataErrorContexts.Commit)
            End If
            rightGrid.EndEdit()
            If rowIndex >= 0 AndAlso rowIndex < rightGrid.Rows.Count Then
                PersistRightGridRow(rowIndex, True)
            End If
        End Sub
    End Class
End Namespace
