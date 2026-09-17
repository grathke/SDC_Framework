Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Text.Json
Imports System.Windows.Forms

Namespace SDC.Framework
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
            ' The page has two vertical edges and no more: the left list at x=10, and everything
            ' on the right starting at x=260 and ending at x=1150 - the permissions grid, the field
            ' grid below it, and the buttons below that. The left list runs the full height beside
            ' all of them, to 783, which is where the field grid ends.
            leftGrid = New DataGridView With {
                .Location = New Point(10, 65),
                .Size = New Size(160, 718),
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
                .Width = 240,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                .ReadOnly = True
            })
            ' Can_Read is captioned "Read", not "Read Only" as it was until 2026-09-10. "Read
            ' Only" described a mode rather than the column: it grants read access, and a role can
            ' hold it alongside Create, Update and Delete, so the caption promised an exclusivity
            ' that nothing enforces. The field grid below has always called it "Read".
            For Each def In New (String, String)() {
                ("Can_Create", "Create"),
                ("Can_Read", "Read"),
                ("Can_Update", "Update"),
                ("Can_Delete", "Delete"),
                ("Can_ViewAllRecords", "View All"),
                ("Can_ViewOnlyMyRecords", "View Mine"),
                ("Can_UseQBE", "Use QBE")
            }
                Dim col As New DataGridViewCheckBoxColumn() With {
                    .Name = def.Item1,
                    .DataPropertyName = def.Item1,
                    .HeaderText = def.Item2,
                    .Width = TickColumnWidth,
                    .AutoSizeMode = DataGridViewAutoSizeColumnMode.None
                }
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                rightGrid.Columns.Add(col)
            Next

            ' Can_ViewAllRecords was captioned "By RegID" and briefly hidden on that account. The
            ' caption was the problem, not the permission: it named a column instead of a
            ' capability, and it reads almost the same as Can_ViewOnlyMyRecords next to it while
            ' meaning the opposite. "View All" against "View Mine" says which is which.
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
            AddHandler rightGrid.DataBindingComplete, AddressOf RightGrid_DataBindingComplete
            Me.Controls.Add(rightGrid)

            ' OK Button
            okButton = New Button With {
                .Text = "OK",
                .Location = New Point(980, 800),
                .Size = New Size(80, 35)
            }
            AddHandler okButton.Click, AddressOf OkButton_Click
            Me.Controls.Add(okButton)

            ' Cancel Button
            cancelButton = New Button With {
                .Text = "Cancel",
                .Location = New Point(1070, 800),
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
                .Location = New Point(260, 328),
                .Size = New Size(400, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(roleFieldsLabel)

            ' Role Fields grid
            roleFieldsGrid = New DataGridView With {
                .Location = New Point(260, 353),
                .Size = New Size(890, 430),
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
                .Width = 240,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            })
            For Each rfDef In New (String, String, Integer, Boolean)() {
                ("CA_CanChange", "CA Can Change", 110, False),
                ("Can_Create", "Create", TickColumnWidth, False),
                ("Can_Read", "Read", TickColumnWidth, False),
                ("Can_Update", "Update", TickColumnWidth, False),
                ("IsActive", "Active", TickColumnWidth, False),
                ("IsRequired", "Required", TickColumnWidth, False),
                ("IsUnique", "Unique", TickColumnWidth, False),
                ("Make_Invisible", "Hide", TickColumnWidth, False)
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

            ' Hidden rather than removed: the save path reads this cell, and the value still
            ' matters - it is only the column that is not wanted on screen. Its 110px goes back to
            ' the grid, where the Field column takes part of it and the Fill caption column the rest.
            If roleFieldsGrid.Columns.Contains("CA_CanChange") Then
                roleFieldsGrid.Columns("CA_CanChange").Visible = False
            End If

            ' IsActive goes the same way, for a stronger reason: nothing sets it by hand. It is
            ' derived - ShouldFieldBeActive recomputes it from the other ticks on the row, so a
            ' field switches itself on the moment anything is configured on it. A checkbox that
            ' overwrites itself is worse than no checkbox, and its 60px joins the Fill caption
            ' column. The cell stays, because the save path and ShouldFieldBeActive both read it.
            If roleFieldsGrid.Columns.Contains("IsActive") Then
                roleFieldsGrid.Columns("IsActive").Visible = False
            End If
            roleFieldsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "OrderBy",
                .DataPropertyName = "OrderBy",
                .HeaderText = "Order",
                .Width = TickColumnWidth,
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

        ' --- Role change auditing -------------------------------------------------------------
        ' This page writes on every grid edit rather than on a Save button, so it cannot use
        ' FW_Base_U's SaveRecordWithAudit. It writes the same BeforeSave/AfterSave pair through the
        ' same shared writer, DataAccess.LogUpdateAudit, so the audit page's Before, After and
        ' Delta panes work here exactly as they do for a standard maintenance page.
        '
        ' The "before" state is the snapshot last loaded or last written for that row, held in
        ' memory and keyed by its id. Re-reading the row from the database would be the obvious
        ' alternative and would cost a round trip on every checkbox click.
        '
        ' LogUpdateAudit swallows its own errors, so a failure to audit can never interrupt or
        ' roll back the permission change it is recording.
        Private ReadOnly roleDetailSnapshots As New Dictionary(Of Integer, String)()
        Private ReadOnly roleFieldSnapshots As New Dictionary(Of Integer, String)()
        Private roleSettingsSnapshot As String = String.Empty

        ''' <summary>
        ''' One width for every tick column on the page, and for Order.
        '''
        ''' The two grids are read as one, so they are built to the same measurements: a 240 name
        ''' column, then seven columns of this width. Above they are the seven table permissions;
        ''' below they are six field permissions and Order, which lands under the seventh. The Fill
        ''' caption column then comes out the same width in each, and every vertical edge lines up
        ''' down the page. A hidden column costs nothing, so IsActive and CA_CanChange do not
        ''' disturb it.
        ''' </summary>
        Private Const TickColumnWidth As Integer = 70

        Private Const AuditPageName As String = "Roles_U"
        Private Const RoleDetailsTableName As String = "FW_RoleDetails"
        Private Const RoleFieldsTableName As String = "FW_RoleFields"
        Private Const RolesTableName As String = "FW_Roles"

        Private Shared ReadOnly RoleDetailAuditColumns As String() = {
            "DB_Table", "Table_Alias", "OverrideCaption", "Can_Create", "Can_Read", "Can_Update",
            "Can_Delete", "Can_ViewAllRecords", "Can_ViewOnlyMyRecords", "Can_UseQBE"}

        ' RoleDetailID and TableName come first because FW_RoleFields is the child of
        ' FW_RoleDetails. Without them an audit row says Address1 changed without saying which
        ' table's Address1, and the same field name exists on more than one table.
        Private Shared ReadOnly RoleFieldAuditColumns As String() = {
            "RoleDetailID", "TableName", "FieldName", "FriendlyFieldName", "OverrideCaption",
            "CA_CanChange", "Can_Create", "Can_Read", "Can_Update", "IsActive", "IsRequired",
            "IsUnique", "Make_Invisible", "OrderBy"}

        ''' <summary>Serialises a flat map the same way FW_Base_U does, so the Delta pane can diff it.</summary>
        Private Shared Function BuildAuditSnapshot(values As Dictionary(Of String, String)) As String
            Dim snapshot As New SortedDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each pair In values
                snapshot(pair.Key) = If(pair.Value, String.Empty)
            Next
            Return JsonSerializer.Serialize(snapshot)
        End Function

        Private Shared Function AuditText(value As Object) As String
            If value Is Nothing OrElse value Is DBNull.Value Then
                Return String.Empty
            End If
            Return Convert.ToString(value, Globalization.CultureInfo.InvariantCulture)
        End Function

        ''' <summary>Snapshot of a loaded row, used to seed the before state after a grid load.</summary>
        Private Shared Function SnapshotFromRow(row As DataRow, columns As String()) As String
            Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If row IsNot Nothing Then
                For Each columnName In columns
                    If row.Table.Columns.Contains(columnName) Then
                        values(columnName) = AuditText(row(columnName))
                    End If
                Next
            End If
            Return BuildAuditSnapshot(values)
        End Function

        Private Shared Function SnapshotRoleDetailValues(dbTable As String, tableAlias As String, overrideCaption As String,
                                                         canCreate As Boolean, canRead As Boolean, canUpdate As Boolean,
                                                         canDelete As Boolean, canViewAllRecords As Boolean,
                                                         canViewOnlyMyRecords As Boolean, canUseQbe As Boolean) As String
            Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            values("DB_Table") = If(dbTable, String.Empty)
            values("Table_Alias") = If(tableAlias, String.Empty)
            values("OverrideCaption") = If(overrideCaption, String.Empty)
            values("Can_Create") = canCreate.ToString()
            values("Can_Read") = canRead.ToString()
            values("Can_Update") = canUpdate.ToString()
            values("Can_Delete") = canDelete.ToString()
            values("Can_ViewAllRecords") = canViewAllRecords.ToString()
            values("Can_ViewOnlyMyRecords") = canViewOnlyMyRecords.ToString()
            values("Can_UseQBE") = canUseQbe.ToString()
            Return BuildAuditSnapshot(values)
        End Function

        ''' <summary>
        ''' The after state of a role field. FieldName is included because the before state - read
        ''' from the loaded row - carries it, and a key present on one side only reads as a change.
        ''' </summary>
        Private Shared Function SnapshotRoleFieldValues(roleDetailId As String, tableName As String,
                                                        fieldName As String, friendlyFieldName As String, overrideCaption As String,
                                                        caCanChange As Object, canCreate As Boolean, canRead As Boolean,
                                                        canUpdate As Boolean, isActive As Boolean, isRequired As Boolean,
                                                        isUnique As Boolean, makeInvisible As Boolean,
                                                        orderByValue As Object) As String
            Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            values("RoleDetailID") = If(roleDetailId, String.Empty)
            values("TableName") = If(tableName, String.Empty)
            values("FieldName") = If(fieldName, String.Empty)
            values("FriendlyFieldName") = If(friendlyFieldName, String.Empty)
            values("OverrideCaption") = If(overrideCaption, String.Empty)
            values("CA_CanChange") = AuditText(caCanChange)
            values("Can_Create") = canCreate.ToString()
            values("Can_Read") = canRead.ToString()
            values("Can_Update") = canUpdate.ToString()
            values("IsActive") = isActive.ToString()
            values("IsRequired") = isRequired.ToString()
            values("IsUnique") = isUnique.ToString()
            values("Make_Invisible") = makeInvisible.ToString()
            values("OrderBy") = AuditText(orderByValue)
            Return BuildAuditSnapshot(values)
        End Function

        ''' <summary>Snapshot of the role's own settings.</summary>
        ''' <param name="roleName">
        ''' Passed in rather than read from roleNameComboBox. That combo is bound, and until its
        ''' DisplayMember has resolved its .Text returns the bound row object's type name - so
        ''' seeding the before state from it recorded "System.Data.DataRowView" as the old role
        ''' name, and the Delta pane then reported a rename that never happened.
        ''' </param>
        Private Function CaptureRoleSettingsSnapshot(roleName As String) As String
            Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            values("RoleName") = If(roleName, String.Empty).Trim()
            values("DisplayOrder") = displayOrderTextBox.Text.Trim()
            values("IsActive") = isActiveCheckBox.Checked.ToString()
            values("Typ_ApplicationAdmin") = typApplicationAdminCheckBox.Checked.ToString()
            values("Typ_CompanyAdmin") = typCompanyAdminCheckBox.Checked.ToString()
            Return BuildAuditSnapshot(values)
        End Function

        Private Sub LogRoleAudit(tableName As String, operationType As String, phase As String,
                                 recordKey As String, snapshotJson As String,
                                 Optional saveSucceeded As Boolean? = Nothing)
            DataAccess.LogUpdateAudit(AuditPageName,
                                      tableName,
                                      operationType,
                                      phase,
                                      recordKey,
                                      snapshotJson,
                                      saveSucceeded,
                                      _registrationId)
        End Sub

        ''' <summary>
        ''' True when the row is already in the state being saved, so both the write and its audit
        ''' pair can be skipped.
        ''' </summary>
        ''' <remarks>
        ''' This page persists a row on selection and on commit whether or not anything changed,
        ''' which produced database updates that set a row to what it already held, and audit pairs
        ''' whose Before and After were byte for byte identical.
        '''
        ''' An empty before state is never treated as unchanged: it means the row was not seeded
        ''' rather than that it matches, and the write must go ahead.
        ''' </remarks>
        Private Shared Function NothingChanged(beforeSnapshot As String, afterSnapshot As String) As Boolean
            If String.IsNullOrEmpty(beforeSnapshot) Then
                Return False
            End If
            Return String.Equals(beforeSnapshot, afterSnapshot, StringComparison.Ordinal)
        End Function

        Private Function CachedSnapshot(cache As Dictionary(Of Integer, String), id As Integer) As String
            Dim snapshot As String = Nothing
            If cache.TryGetValue(id, snapshot) Then
                Return snapshot
            End If
            Return String.Empty
        End Function

        ''' <summary>
        ''' A table whose permission exists only to switch a feature on, where Read is the whole
        ''' question and nothing else means anything.
        '''
        ''' Two kinds qualify. **FW_Perm_ by prefix**, because that prefix means precisely this -
        ''' a table with no data that exists to be permitted or not - so every one of them is a
        ''' gate by definition and none has to be listed.
        '''
        ''' **FW_Messages by name**, deliberately not by prefix: it is a real table with real rows,
        ''' and Create or Delete on it could one day mean "may compose" or "may delete a message".
        ''' Naming it is a decision reversed by deleting a line.
        ''' </summary>
        Private Shared Function IsReadOnlyGateTable(dbTable As String) As Boolean
            If String.IsNullOrWhiteSpace(dbTable) Then Return False

            Dim name = dbTable.Trim()
            If name.StartsWith("FW_Perm_", StringComparison.OrdinalIgnoreCase) Then Return True

            Return String.Equals(name, "FW_Messages", StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Tables that are only ever browsed, where Read and Use QBE are the two ticks that mean
        ''' anything and the rest are greyed out.
        '''
        ''' Not the same as a gate table, though it looks alike on screen: a gate table permits one
        ''' tick, Read, and nothing else - Use QBE means nothing on a table with no rows to search.
        ''' A browse-only table has rows, and searching them is the whole page, so QBE stays
        ''' tickable.
        '''
        ''' FW_SwitchUser is the first: a snapshot of who each login belongs to, rewritten by a
        ''' stored procedure and never edited by anybody. Create, Update and Delete on it would be
        ''' ticks that grant nothing. The next page that only browses is one line here.
        ''' </summary>
        Private Shared Function IsBrowseOnlyTable(dbTable As String) As Boolean
            If String.IsNullOrWhiteSpace(dbTable) Then Return False

            Return String.Equals(dbTable.Trim(), "FW_SwitchUser", StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' What a table is granted the first time a role gains it, from the same two tests that
        ''' decide which of its ticks are greyed out afterwards. One answer, so what a row arrives
        ''' as and what it may be changed to cannot disagree.
        ''' </summary>
        Private Shared Function ResolveRoleTableGrant(dbTable As String) As DataAccess.RoleTableGrant
            If IsReadOnlyGateTable(dbTable) Then Return DataAccess.RoleTableGrant.ReadOnlyGate
            If IsBrowseOnlyTable(dbTable) Then Return DataAccess.RoleTableGrant.BrowseOnly

            Return DataAccess.RoleTableGrant.FullAccess
        End Function

        ''' <summary>
        ''' Greys out the permissions that mean nothing on a gate table, so the only tick that has
        ''' an effect is the only tick that can be made.
        '''
        ''' On binding rather than on paint: the cells are read-only for good, not styled per
        ''' redraw, so an edit cannot start in the first place. The Read column is deliberately
        ''' left alone.
        ''' </summary>
        Private Sub RightGrid_DataBindingComplete(sender As Object, e As DataGridViewBindingCompleteEventArgs)
            If rightGrid Is Nothing OrElse Not rightGrid.Columns.Contains("Can_Read") Then Return

            Dim gateLocked = New String() {"Can_Create", "Can_Update", "Can_Delete",
                                           "Can_ViewAllRecords", "Can_ViewOnlyMyRecords", "Can_UseQBE"}

            ' Everything the gate list locks except two. Use QBE is how a browse-only page is
            ' searched at all. View All is what shows the registration selector, and on a page
            ' that may look across companies it is the tick that widens the scope - the decision
            ' an administrator should be able to make and to take back.
            Dim browseOnlyLocked = New String() {"Can_Create", "Can_Update", "Can_Delete",
                                                 "Can_ViewOnlyMyRecords"}

            For Each row As DataGridViewRow In rightGrid.Rows
                If row.IsNewRow Then Continue For

                Dim boundRow = TryCast(row.DataBoundItem, DataRowView)
                If boundRow Is Nothing OrElse Not boundRow.Row.Table.Columns.Contains("DB_Table") Then Continue For

                Dim dbTable = Convert.ToString(boundRow("DB_Table"))
                Dim locked As String()
                If IsReadOnlyGateTable(dbTable) Then
                    locked = gateLocked
                ElseIf IsBrowseOnlyTable(dbTable) Then
                    locked = browseOnlyLocked
                Else
                    Continue For
                End If

                For Each columnName In locked
                    If Not rightGrid.Columns.Contains(columnName) Then Continue For
                    Dim cell = row.Cells(columnName)
                    cell.ReadOnly = True
                    cell.Style.BackColor = Color.FromArgb(238, 240, 242)
                    cell.Style.ForeColor = Color.FromArgb(170, 176, 183)
                Next
            Next
        End Sub

        Private Sub LoadRightGrid()
            Try
                Dim table = DataAccess.GetRoleDetailsForRole(_roleId, _registrationId)
                rightGrid.DataSource = table

                ' A freshly loaded row is the before state for whatever is edited next.
                roleDetailSnapshots.Clear()
                If table IsNot Nothing AndAlso table.Columns.Contains("ID") Then
                    For Each detailRow As DataRow In table.Rows
                        If detailRow("ID") IsNot DBNull.Value Then
                            roleDetailSnapshots(CInt(detailRow("ID"))) = SnapshotFromRow(detailRow, RoleDetailAuditColumns)
                        End If
                    Next
                End If
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
                    ' The before state is deliberately empty: the role had no permission row for
                    ' this table at all, so there is nothing to show on the Before pane.
                    LogRoleAudit(RoleDetailsTableName, "Create", "BeforeSave", dbTable, String.Empty)

                    ' Add the detail and initial fields atomically.
                    Dim newId = DataAccess.AddRoleTableWithFields(_roleId,
                                                                  _registrationId,
                                                                  roleSchemaId,
                                                                  dbTable,
                                                                  tableAlias,
                                                                  tableAlias,
                                                                  ResolveRoleTableGrant(dbTable))
                    If newId <= 0 Then
                        MessageBox.Show("Failed to add table permission.", "Error")
                        Return
                    End If
                    LoadRightGrid()
                    
                    ' Logged after the reload so the after state is the row as the database now
                    ' holds it, rather than what this page asked for.
                    Dim grantKey = newId.ToString(Globalization.CultureInfo.InvariantCulture)
                    LogRoleAudit(RoleDetailsTableName, "Create", "AfterSave", grantKey,
                                 CachedSnapshot(roleDetailSnapshots, newId), True)

                    ' Reload the role fields table so the selection change handler can filter properly
                    LoadRoleFieldsGrid()
                    
                    ' Select the row for this table in rightGrid to display its fields
                    SelectRightGridRowForSchema(roleSchemaId)
                    
                    MessageBox.Show("Table added and initial fields inserted.", "Success")
                Else
                    ' Table already exists - just select it and sync schema
                    SelectRightGridRowForSchema(roleSchemaId)
                    
                    ' Refresh schema to sync any new columns (does LoadRoleFieldsGrid internally)
                    Dim syncCounts = RefreshSelectedRowSchema()
                    
                    MessageBox.Show($"Table Synced:{vbCrLf}{vbCrLf}Added:    {syncCounts.Item1}{vbCrLf}Deleted:  {syncCounts.Item2}{vbCrLf}Repaired: {syncCounts.Item3}", "Success")
                End If
            Catch ex As Exception
                MessageBox.Show("Error adding table permission: " & ex.Message, "Error")
            End Try
        End Sub

        ''' <summary>
        ''' Selects the row for a table and scrolls it into view.
        '''
        ''' Selecting alone was not enough: a role with fifty tables puts a newly added one wherever
        ''' the sort lands it, and a row selected off-screen reads as nothing having happened - the
        ''' administrator scrolls looking for what they just added. The current cell is set as well
        ''' as the selection, because that is what the grid scrolls to and what the field list below
        ''' follows.
        '''
        ''' One method for both callers. Adding a table and re-picking one already there were the
        ''' same nine lines twice, and only one of them would have been fixed.
        ''' </summary>
        Private Sub SelectRightGridRowForSchema(roleSchemaId As Integer)
            Dim bound = TryCast(rightGrid.DataSource, DataTable)
            If bound Is Nothing Then Return

            For i = 0 To rightGrid.Rows.Count - 1
                If i >= bound.Rows.Count Then Exit For
                If CInt(bound.Rows(i)("SchemaID")) <> roleSchemaId Then Continue For

                rightGrid.ClearSelection()
                rightGrid.Rows(i).Selected = True

                ' The first visible column, because the key one may be hidden and a current cell
                ' cannot sit on a column nobody can see.
                For Each column As DataGridViewColumn In rightGrid.Columns
                    If column.Visible Then
                        rightGrid.CurrentCell = rightGrid.Rows(i).Cells(column.Index)
                        Exit For
                    End If
                Next

                ' Setting the current cell scrolls the row into view already; this puts it near the
                ' top instead of just inside the edge, where it is easy to miss.
                Dim firstVisible = Math.Max(0, i - 2)
                If rightGrid.Rows.Count > firstVisible Then
                    rightGrid.FirstDisplayedScrollingRowIndex = firstVisible
                End If

                Return
            Next
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

                ' CommitEdit alone does not leave edit mode, so CellEndEdit - which owns the write -
                ' did not run until the cell lost focus. A tick is meant to save immediately.
                rightGrid.EndEdit()
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

                ' Read from the bound row rather than a grid cell. DB_Table is in the DataTable but
                ' has no column in the grid, so the cell lookup returned nothing and every After
                ' snapshot recorded an empty table name - which the Delta pane then reported as the
                ' table having been cleared.
                Dim dbTableName As String = String.Empty
                Dim boundDetail = TryCast(row.DataBoundItem, DataRowView)
                If boundDetail IsNot Nothing AndAlso boundDetail.Row IsNot Nothing AndAlso
                   boundDetail.Row.Table.Columns.Contains("DB_Table") Then
                    dbTableName = AuditText(boundDetail("DB_Table"))
                End If

                Dim detailKey = detailId.ToString(Globalization.CultureInfo.InvariantCulture)
                Dim detailAfter = SnapshotRoleDetailValues(dbTableName, tableAlias, tableCaption,
                                                           canCreate, canRead, canUpdate, canDelete,
                                                           canViewAllRecords, canViewOnlyMyRecords, canUseQBE)
                Dim detailBefore = CachedSnapshot(roleDetailSnapshots, detailId)
                If NothingChanged(detailBefore, detailAfter) Then
                    Return
                End If

                LogRoleAudit(RoleDetailsTableName, "Update", "BeforeSave", detailKey, detailBefore)

                Dim detailSaved = False
                Try
                    DataAccess.UpdateRoleDetails(detailId, tableAlias, tableCaption, canCreate, canRead, canUpdate, canDelete, canViewAllRecords, canViewOnlyMyRecords, canUseQBE, propagateCaptionOverride)
                    detailSaved = True
                Finally
                    LogRoleAudit(RoleDetailsTableName, "Update", "AfterSave", detailKey, detailAfter, detailSaved)
                    If detailSaved Then
                        roleDetailSnapshots(detailId) = detailAfter
                    End If
                End Try
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
                        Dim removedKey = detailId.ToString(Globalization.CultureInfo.InvariantCulture)
                        LogRoleAudit(RoleDetailsTableName, "Delete", "BeforeSave", removedKey,
                                     CachedSnapshot(roleDetailSnapshots, detailId))

                        Dim removeSaved = False
                        Try
                            DataAccess.DeleteRoleDetails(detailId)
                            removeSaved = True
                        Finally
                            ' Nothing is left, so the after state is empty by design.
                            LogRoleAudit(RoleDetailsTableName, "Delete", "AfterSave", removedKey, String.Empty, removeSaved)
                        End Try

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

        Private Function RefreshSelectedRowSchema() As Tuple(Of Integer, Integer, Integer)
            If rightGrid.SelectedRows.Count = 0 Then
                Return New Tuple(Of Integer, Integer, Integer)(0, 0, 0)  ' Return zeros if no row selected
            End If

            Try
                Dim selectedRowIndex = rightGrid.SelectedRows(0).Index
                Dim dt = TryCast(rightGrid.DataSource, DataTable)
                If dt Is Nothing OrElse selectedRowIndex >= dt.Rows.Count Then
                    Return New Tuple(Of Integer, Integer, Integer)(0, 0, 0)
                End If

                Dim schemaId = CInt(dt.Rows(selectedRowIndex)("SchemaID"))
                Dim dbTable = dt.Rows(selectedRowIndex)("DB_Table").ToString()
                Dim updatedBy = If(SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)

                Dim inserted, deleted, repaired As Integer

                ' A sync inserts and deletes FW_RoleFields rows in bulk, so what is worth recording
                ' is how many of each, not the state of any one row.
                Dim syncKey = schemaId.ToString(Globalization.CultureInfo.InvariantCulture)
                Dim syncBefore As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                syncBefore("DB_Table") = dbTable
                syncBefore("SchemaID") = syncKey
                LogRoleAudit(RoleFieldsTableName, "Sync", "BeforeSave", syncKey, BuildAuditSnapshot(syncBefore))

                Dim syncResult = False
                Try
                    syncResult = DataAccess.SyncRoleFieldsWithSchema(schemaId, dbTable, _registrationId, _roleId, updatedBy, inserted, deleted, repaired)
                Finally
                    Dim syncAfter As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                    syncAfter("DB_Table") = dbTable
                    syncAfter("SchemaID") = syncKey
                    syncAfter("FieldsInserted") = inserted.ToString(Globalization.CultureInfo.InvariantCulture)
                    syncAfter("FieldsDeleted") = deleted.ToString(Globalization.CultureInfo.InvariantCulture)
                    syncAfter("LinksRepaired") = repaired.ToString(Globalization.CultureInfo.InvariantCulture)
                    LogRoleAudit(RoleFieldsTableName, "Sync", "AfterSave", syncKey, BuildAuditSnapshot(syncAfter), syncResult)
                End Try

                ' Always refresh the role fields grid to reflect changes
                LoadRoleFieldsGrid()
                
                ' Restore selection to the same row
                If selectedRowIndex < rightGrid.Rows.Count Then
                    rightGrid.ClearSelection()
                    rightGrid.Rows(selectedRowIndex).Selected = True
                    rightGrid.FirstDisplayedScrollingRowIndex = selectedRowIndex
                End If
                
                Return New Tuple(Of Integer, Integer, Integer)(inserted, deleted, repaired)
            Catch ex As Exception
                ' Silent catch - don't show errors during Add flow
                Return New Tuple(Of Integer, Integer, Integer)(0, 0, 0)
            End Try
        End Function

        Private Sub LoadRoleSelector()
            Try
                suppressRoleSelectionChange = True
                Dim roles = DataAccess.GetRolesByRegistration(_registrationId)
                roleNameComboBox.DataSource = roles
                roleNameComboBox.DisplayMember = "RoleName"
                roleNameComboBox.ValueMember = "ID"
                ComboWidth.FitToContent(roleNameComboBox)

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
                roleSettingsSnapshot = CaptureRoleSettingsSnapshot(_roleName)
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

                Dim roleKey = _roleId.ToString(Globalization.CultureInfo.InvariantCulture)
                Dim roleAfter = CaptureRoleSettingsSnapshot(newRoleName)
                If NothingChanged(roleSettingsSnapshot, roleAfter) Then
                    _roleName = newRoleName
                    Return
                End If

                LogRoleAudit(RolesTableName, "Update", "BeforeSave", roleKey, roleSettingsSnapshot)

                Dim roleSaved = False
                Try
                    ' Update role name, display order, active status and type flags
                    DataAccess.UpdateRoleNameAndDisplayOrder(_roleId, newRoleName, newDisplayOrder, isActiveCheckBox.Checked,
                        typApplicationAdminCheckBox.Checked, typCompanyAdminCheckBox.Checked)
                    roleSaved = True
                Finally
                    LogRoleAudit(RolesTableName, "Update", "AfterSave", roleKey, roleAfter, roleSaved)
                    If roleSaved Then
                        roleSettingsSnapshot = roleAfter
                    End If
                End Try

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

                roleFieldSnapshots.Clear()
                If _roleFieldsTable.Columns.Contains("ID") Then
                    For Each fieldRow As DataRow In _roleFieldsTable.Rows
                        If fieldRow("ID") IsNot DBNull.Value Then
                            roleFieldSnapshots(CInt(fieldRow("ID"))) = SnapshotFromRow(fieldRow, RoleFieldAuditColumns)
                        End If
                    Next
                End If
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

        ''' <summary>
        ''' Commits a checkbox the moment it is clicked, so the row is written immediately.
        ''' </summary>
        ''' <remarks>
        ''' Any checkbox column qualifies, which is how the right grid has always decided this. The
        ''' named list this replaced covered six of the grid's eight checkbox columns and left
        ''' IsActive out, so ticking Active did not save until the cell lost focus. A list of column
        ''' names has to be updated every time a column is added; asking what kind of column it is
        ''' does not.
        '''
        ''' CommitEdit pushes the value into the data source but stays in edit mode, so CellEndEdit -
        ''' which owns the database write - would not run. EndEdit is what closes that gap.
        ''' </remarks>
        Private Sub RoleFieldsGrid_CurrentCellDirtyStateChanged(sender As Object, e As EventArgs)
            If Not roleFieldsGrid.IsCurrentCellDirty OrElse roleFieldsGrid.CurrentCell Is Nothing Then
                Return
            End If

            If TypeOf roleFieldsGrid.CurrentCell.OwningColumn Is DataGridViewCheckBoxColumn Then
                roleFieldsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit)
                roleFieldsGrid.EndEdit()
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
                Dim fieldKey = id.ToString(Globalization.CultureInfo.InvariantCulture)
                Dim auditFieldName As String = String.Empty
                Dim auditRoleDetailId As String = String.Empty
                Dim auditFieldTable As String = String.Empty
                If dataRow IsNot Nothing Then
                    If dataRow.Table.Columns.Contains("FieldName") Then auditFieldName = AuditText(dataRow("FieldName"))
                    If dataRow.Table.Columns.Contains("RoleDetailID") Then auditRoleDetailId = AuditText(dataRow("RoleDetailID"))
                    If dataRow.Table.Columns.Contains("TableName") Then auditFieldTable = AuditText(dataRow("TableName"))
                End If
                Dim fieldAfter = SnapshotRoleFieldValues(auditRoleDetailId, auditFieldTable,
                                                         auditFieldName, friendlyFieldName, overrideCaption, caCanChange,
                                                         canCreate, canRead, canUpdate, isActive,
                                                         isRequired, isUnique, makeInvisible, orderByVal)
                Dim fieldBefore = CachedSnapshot(roleFieldSnapshots, id)
                If NothingChanged(fieldBefore, fieldAfter) Then
                    Return
                End If

                LogRoleAudit(RoleFieldsTableName, "Update", "BeforeSave", fieldKey, fieldBefore)

                Dim fieldSaved = False
                Try
                    DataAccess.UpdateRoleField(id, caCanChange, canCreate, canRead, canUpdate, isActive, isRequired, isUnique, makeInvisible, orderByVal, overrideCaption, friendlyFieldName, updatedBy, propagateCaptionOverride)
                    fieldSaved = True
                Finally
                    LogRoleAudit(RoleFieldsTableName, "Update", "AfterSave", fieldKey, fieldAfter, fieldSaved)
                    If fieldSaved Then
                        roleFieldSnapshots(id) = fieldAfter
                    End If
                End Try
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
