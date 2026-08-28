Option Strict On
Option Explicit On

Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class FW_AuditTrail_B
        Inherits Form

        Private ReadOnly pageLabel As Label
        Private ReadOnly pageComboBox As ComboBox
        Private ReadOnly tableLabel As Label
        Private ReadOnly tableComboBox As ComboBox
        Private ReadOnly operationLabel As Label
        Private ReadOnly operationComboBox As ComboBox
        Private ReadOnly userLabel As Label
        Private ReadOnly userComboBox As ComboBox
        Private ReadOnly registrationLabel As Label
        Private ReadOnly registrationComboBox As ComboBox
        Private ReadOnly fromDateLabel As Label
        Private ReadOnly fromDatePicker As DateTimePicker
        Private ReadOnly toDateLabel As Label
        Private ReadOnly toDatePicker As DateTimePicker
        Private ReadOnly changedOnlyCheckBox As CheckBox
        Private ReadOnly applyFilterButton As Button
        Private ReadOnly deleteButton As Button
        Private ReadOnly restoreButton As Button
        Private ReadOnly showDeletedButton As Button
        Private ReadOnly closeButton As Button
        Private ReadOnly auditGrid As DataGridView
        Private ReadOnly beforeLabel As Label
        Private ReadOnly beforeTextBox As TextBox
        Private ReadOnly afterLabel As Label
        Private ReadOnly afterTextBox As TextBox
        Private ReadOnly deltaLabel As Label
        Private ReadOnly deltaGrid As DataGridView
        Private showDeletedRecordsOnly As Boolean = False

        Private Shared ReadOnly ShowDeletedText As String = "Show Deleted"
        Private Shared ReadOnly ShowNormalText As String = "Show Normal"

        Private Class FilterOption
            Public Property Value As String
            Public Property Display As String

            Public Overrides Function ToString() As String
                Return Display
            End Function
        End Class

        Public Sub New()
            Me.Text = "Update Audit History"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.MinimumSize = New Size(1200, 760)
            Me.ClientSize = New Size(1280, 820)
            Me.BackColor = Color.White

            registrationLabel = New Label() With {.Text = "Registration:", .Location = New Point(16, 16), .AutoSize = True}
            registrationComboBox = New ComboBox() With {.Location = New Point(98, 12), .Size = New Size(220, 26), .DropDownStyle = ComboBoxStyle.DropDownList}

            pageLabel = New Label() With {.Text = "Page:", .Location = New Point(332, 16), .AutoSize = True}
            pageComboBox = New ComboBox() With {.Location = New Point(378, 12), .Size = New Size(180, 26), .DropDownStyle = ComboBoxStyle.DropDownList}

            tableLabel = New Label() With {.Text = "Table:", .Location = New Point(570, 16), .AutoSize = True}
            tableComboBox = New ComboBox() With {.Location = New Point(616, 12), .Size = New Size(180, 26), .DropDownStyle = ComboBoxStyle.DropDownList}

            operationLabel = New Label() With {.Text = "Operation:", .Location = New Point(808, 16), .AutoSize = True}
            operationComboBox = New ComboBox() With {.Location = New Point(877, 12), .Size = New Size(118, 26), .DropDownStyle = ComboBoxStyle.DropDownList}

            userLabel = New Label() With {.Text = "User:", .Location = New Point(1004, 16), .AutoSize = True}
            userComboBox = New ComboBox() With {.Location = New Point(1042, 12), .Size = New Size(220, 26), .DropDownStyle = ComboBoxStyle.DropDownList}

            fromDateLabel = New Label() With {.Text = "From:", .Location = New Point(16, 49), .AutoSize = True}
            fromDatePicker = New DateTimePicker() With {.Location = New Point(62, 44), .Size = New Size(180, 26), .Format = DateTimePickerFormat.Short, .ShowCheckBox = True}

            toDateLabel = New Label() With {.Text = "To:", .Location = New Point(262, 49), .AutoSize = True}
            toDatePicker = New DateTimePicker() With {.Location = New Point(289, 44), .Size = New Size(180, 26), .Format = DateTimePickerFormat.Short, .ShowCheckBox = True}

            changedOnlyCheckBox = New CheckBox() With {
                .Text = "Show Changed Fields Only",
                .Location = New Point(508, 47),
                .AutoSize = True,
                .Checked = True
            }

            applyFilterButton = New Button() With {.Text = "Apply", .Location = New Point(740, 42), .Size = New Size(88, 30)}
            deleteButton = New Button() With {.Text = "Delete", .Location = New Point(835, 42), .Size = New Size(88, 30)}
            restoreButton = New Button() With {.Text = "Restore", .Location = New Point(928, 42), .Size = New Size(88, 30), .Visible = False}
            showDeletedButton = New Button() With {.Text = ShowDeletedText, .Location = New Point(1021, 42), .Size = New Size(110, 30)}
            closeButton = New Button() With {.Text = "Close", .Location = New Point(1138, 42), .Size = New Size(88, 30)}

            auditGrid = New DataGridView() With {
                .Location = New Point(16, 82),
                .Size = New Size(Me.ClientSize.Width - 32, 300),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .MultiSelect = False,
                .RowHeadersVisible = False,
                .AutoGenerateColumns = True,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            }
            ApplyBrowseGridStandard(auditGrid)
            auditGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill

            beforeLabel = New Label() With {.Text = "Before", .Location = New Point(16, 390), .AutoSize = True, .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)}
            beforeTextBox = New TextBox() With {
                .Location = New Point(16, 410),
                .Size = New Size((Me.ClientSize.Width - 48) \ 2, 160),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Multiline = True,
                .ReadOnly = True,
                .ScrollBars = ScrollBars.Both,
                .Font = New Font("Consolas", 9.0F, FontStyle.Regular)
            }

            afterLabel = New Label() With {.Text = "After", .Location = New Point((Me.ClientSize.Width \ 2) + 8, 390), .AutoSize = True, .Anchor = AnchorStyles.Top Or AnchorStyles.Right, .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)}
            afterTextBox = New TextBox() With {
                .Location = New Point((Me.ClientSize.Width \ 2) + 8, 410),
                .Size = New Size((Me.ClientSize.Width - 48) \ 2, 160),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Multiline = True,
                .ReadOnly = True,
                .ScrollBars = ScrollBars.Both,
                .Font = New Font("Consolas", 9.0F, FontStyle.Regular)
            }

            deltaLabel = New Label() With {.Text = "Delta (Changed Fields)", .Location = New Point(16, 580), .AutoSize = True, .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)}
            deltaGrid = New DataGridView() With {
                .Location = New Point(16, 602),
                .Size = New Size(Me.ClientSize.Width - 32, 200),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right,
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .MultiSelect = False,
                .RowHeadersVisible = False,
                .AutoGenerateColumns = True,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            }
            ApplyBrowseGridStandard(deltaGrid)
            deltaGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill

            AddHandler applyFilterButton.Click, AddressOf ApplyFilterButton_Click
            AddHandler deleteButton.Click, AddressOf DeleteButton_Click
            AddHandler restoreButton.Click, AddressOf RestoreButton_Click
            AddHandler showDeletedButton.Click, AddressOf ShowDeletedButton_Click
            AddHandler closeButton.Click, AddressOf CloseButton_Click
            AddHandler auditGrid.SelectionChanged, AddressOf AuditGrid_SelectionChanged
            AddHandler Me.Resize, AddressOf FW_AuditTrail_B_Resize
            AddHandler Me.Load, AddressOf FW_AuditTrail_B_Load

            Me.Controls.AddRange({
                pageLabel, pageComboBox,
                tableLabel, tableComboBox,
                operationLabel, operationComboBox,
                userLabel, userComboBox,
                registrationLabel, registrationComboBox,
                fromDateLabel, fromDatePicker,
                toDateLabel, toDatePicker,
                changedOnlyCheckBox,
                applyFilterButton, deleteButton, restoreButton, showDeletedButton, closeButton,
                auditGrid,
                beforeLabel, beforeTextBox,
                afterLabel, afterTextBox,
                deltaLabel, deltaGrid
            })
        End Sub

        Private Sub FW_AuditTrail_B_Load(sender As Object, e As EventArgs)
            UpdateDeletedUiState()
            LoadFilterOptions()
            LoadGrid()
        End Sub

        Private Sub FW_AuditTrail_B_Resize(sender As Object, e As EventArgs)
            auditGrid.Width = Me.ClientSize.Width - 32

            Dim halfWidth As Integer = (Me.ClientSize.Width - 48) \ 2
            beforeTextBox.Width = halfWidth
            afterLabel.Left = beforeTextBox.Right + 16
            afterTextBox.Left = beforeTextBox.Right + 16
            afterTextBox.Width = halfWidth

            deltaGrid.Width = Me.ClientSize.Width - 32
            deltaGrid.Height = Math.Max(140, Me.ClientSize.Height - deltaGrid.Top - 16)
        End Sub

        Private Sub ApplyFilterButton_Click(sender As Object, e As EventArgs)
            LoadGrid()
        End Sub

        Private Sub ShowDeletedButton_Click(sender As Object, e As EventArgs)
            showDeletedRecordsOnly = Not showDeletedRecordsOnly
            UpdateDeletedUiState()
            LoadFilterOptions()
            LoadGrid()
        End Sub

        Private Sub DeleteButton_Click(sender As Object, e As EventArgs)
            Dim auditId = GetSelectedAuditId()
            If Not auditId.HasValue Then
                MessageBox.Show("Select an audit row first.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If MessageBox.Show("Soft delete this audit row?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            Try
                Dim updatedBy = If(SessionState.IsActive AndAlso SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)
                DataAccess.SoftDeleteAuditTrailEntry(auditId.Value, updatedBy)
                LoadFilterOptions()
                LoadGrid()
                FW_EntityCrudAdapter.ShowAutoClosingMessage(Me, "Audit row deleted.", "Delete", MessageBoxIcon.Information, 1000)
            Catch ex As Exception
                MessageBox.Show("Delete failed: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub RestoreButton_Click(sender As Object, e As EventArgs)
            Dim auditId = GetSelectedAuditId()
            If Not auditId.HasValue Then
                MessageBox.Show("Select an audit row first.", "Restore", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If MessageBox.Show("Restore this audit row?", "Confirm Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            Try
                Dim updatedBy = If(SessionState.IsActive AndAlso SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)
                DataAccess.RestoreAuditTrailEntry(auditId.Value, updatedBy)
                LoadFilterOptions()
                LoadGrid()
                FW_EntityCrudAdapter.ShowAutoClosingMessage(Me, "Audit row restored.", "Restore", MessageBoxIcon.Information, 1000)
            Catch ex As Exception
                MessageBox.Show("Restore failed: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub

        Private Function GetSelectedAuditId() As Integer?
            If auditGrid Is Nothing OrElse auditGrid.SelectedRows.Count = 0 Then
                Return Nothing
            End If

            If auditGrid.Columns Is Nothing OrElse Not auditGrid.Columns.Contains("AuditID") Then
                Return Nothing
            End If

            Dim row = auditGrid.SelectedRows(0)
            If row Is Nothing Then
                Return Nothing
            End If

            Dim idValue = If(row.Cells("AuditID").Value, Nothing)
            If idValue Is Nothing OrElse idValue Is DBNull.Value Then
                Return Nothing
            End If

            Dim parsed As Integer
            If Integer.TryParse(idValue.ToString(), parsed) AndAlso parsed > 0 Then
                Return parsed
            End If

            Return Nothing
        End Function

        Private Sub UpdateDeletedUiState()
            showDeletedButton.Text = If(showDeletedRecordsOnly, ShowNormalText, ShowDeletedText)
            deleteButton.Visible = Not showDeletedRecordsOnly
            restoreButton.Visible = showDeletedRecordsOnly
        End Sub

        Private Sub LoadFilterOptions()
            Dim source = DataAccess.GetUpdateAuditFilterValues(showDeletedRecordsOnly)

            PopulateSimpleCombo(pageComboBox, source, "PageName")
            PopulateSimpleCombo(tableComboBox, source, "TableName")
            PopulateSimpleCombo(operationComboBox, source, "OperationType")
            PopulateUserCombo(userComboBox, source)
            PopulateRegistrationCombo(registrationComboBox, source)
        End Sub

        Private Sub PopulateSimpleCombo(combo As ComboBox, source As DataTable, columnName As String)
            combo.Items.Clear()
            combo.Items.Add(New FilterOption With {.Display = "(All)", .Value = String.Empty})

            If source IsNot Nothing AndAlso source.Columns.Contains(columnName) Then
                Dim values = source.AsEnumerable().Select(Function(r) If(r(columnName), String.Empty).ToString().Trim()).
                                                  Where(Function(v) v <> String.Empty).
                                                  Distinct(StringComparer.OrdinalIgnoreCase).
                                                  OrderBy(Function(v) v, StringComparer.OrdinalIgnoreCase)
                For Each value In values
                    combo.Items.Add(New FilterOption With {.Display = value, .Value = value})
                Next
            End If

            combo.SelectedIndex = 0
        End Sub

        Private Sub PopulateUserCombo(combo As ComboBox, source As DataTable)
            combo.Items.Clear()
            combo.Items.Add(New FilterOption With {.Display = "(All)", .Value = String.Empty})

            If source IsNot Nothing AndAlso source.Columns.Contains("UserID") AndAlso source.Columns.Contains("UserDisplay") Then
                Dim users = source.AsEnumerable().
                    Select(Function(r) New With {
                        .UserID = Convert.ToInt32(If(r("UserID") Is DBNull.Value, 0, r("UserID")), CultureInfo.InvariantCulture),
                        .UserDisplay = If(r("UserDisplay"), String.Empty).ToString().Trim()
                    }).
                    Where(Function(u) u.UserID > 0).
                    GroupBy(Function(u) u.UserID).
                    Select(Function(g) g.First()).
                    OrderBy(Function(u) u.UserDisplay, StringComparer.OrdinalIgnoreCase).
                    ThenBy(Function(u) u.UserID)

                For Each user In users
                    Dim caption = If(user.UserDisplay = String.Empty, user.UserID.ToString(CultureInfo.InvariantCulture), user.UserDisplay)
                    combo.Items.Add(New FilterOption With {
                        .Display = user.UserID.ToString(CultureInfo.InvariantCulture) & " - " & caption,
                        .Value = user.UserID.ToString(CultureInfo.InvariantCulture)
                    })
                Next
            End If

            combo.SelectedIndex = 0
        End Sub

        Private Sub PopulateRegistrationCombo(combo As ComboBox, source As DataTable)
            combo.Items.Clear()
            combo.Items.Add(New FilterOption With {.Display = "(All)", .Value = String.Empty})

            If source IsNot Nothing AndAlso source.Columns.Contains("RegistrationID") Then
                Dim regs = source.AsEnumerable().
                    Select(Function(r) New With {
                        .RegistrationID = Convert.ToInt32(If(r("RegistrationID") Is DBNull.Value, 0, r("RegistrationID")), CultureInfo.InvariantCulture),
                        .RegistrationDisplay = If(r("RegistrationDisplay"), String.Empty).ToString().Trim()
                    }).
                    Where(Function(x) x.RegistrationID > 0).
                    GroupBy(Function(x) x.RegistrationID).
                    Select(Function(g) g.First()).
                    OrderBy(Function(x) x.RegistrationDisplay, StringComparer.OrdinalIgnoreCase).
                    ThenBy(Function(x) x.RegistrationID)

                For Each reg In regs
                    Dim caption = If(reg.RegistrationDisplay = String.Empty,
                                     reg.RegistrationID.ToString(CultureInfo.InvariantCulture),
                                     reg.RegistrationDisplay)
                    combo.Items.Add(New FilterOption With {
                        .Display = reg.RegistrationID.ToString(CultureInfo.InvariantCulture) & " - " & caption,
                        .Value = reg.RegistrationID.ToString(CultureInfo.InvariantCulture)
                    })
                Next
            End If

            combo.SelectedIndex = 0
        End Sub

        Private Sub LoadGrid()
            Dim selectedPage = TryCast(pageComboBox.SelectedItem, FilterOption)
            Dim selectedTable = TryCast(tableComboBox.SelectedItem, FilterOption)
            Dim selectedOperation = TryCast(operationComboBox.SelectedItem, FilterOption)
            Dim selectedUser = TryCast(userComboBox.SelectedItem, FilterOption)
            Dim selectedRegistration = TryCast(registrationComboBox.SelectedItem, FilterOption)

            Dim userId As Integer? = Nothing
            Dim parsedUserId As Integer
            If selectedUser IsNot Nothing AndAlso Integer.TryParse(selectedUser.Value, parsedUserId) AndAlso parsedUserId > 0 Then
                userId = parsedUserId
            End If

            Dim registrationId As Integer? = Nothing
            Dim parsedRegistrationId As Integer
            If selectedRegistration IsNot Nothing AndAlso Integer.TryParse(selectedRegistration.Value, parsedRegistrationId) AndAlso parsedRegistrationId > 0 Then
                registrationId = parsedRegistrationId
            End If

            Dim fromDate As DateTime? = If(fromDatePicker.Checked, CType(fromDatePicker.Value.Date, DateTime?), Nothing)
            Dim toDate As DateTime? = If(toDatePicker.Checked, CType(toDatePicker.Value.Date, DateTime?), Nothing)

            Dim data = DataAccess.GetUpdateAuditEntries(
                registrationId:=registrationId,
                userId:=userId,
                pageName:=If(selectedPage IsNot Nothing, selectedPage.Value, String.Empty),
                tableName:=If(selectedTable IsNot Nothing, selectedTable.Value, String.Empty),
                operationType:=If(selectedOperation IsNot Nothing, selectedOperation.Value, String.Empty),
                fromDate:=fromDate,
                toDate:=toDate,
                changedOnly:=changedOnlyCheckBox.Checked,
                showDeletedOnly:=showDeletedRecordsOnly)

            auditGrid.DataSource = data
            ConfigureAuditGridColumns()
            BindSelectedRowDetails()
        End Sub

        Private Sub ConfigureAuditGridColumns()
            If auditGrid.Columns Is Nothing OrElse auditGrid.Columns.Count = 0 Then
                Return
            End If

            Dim visibleColumns = New String() {
                "CreatedOn", "RegistrationID", "UserID", "UserDisplay", "PageName", "TableName", "OperationType", "RecordKey", "ChangeCount", "ChangedFields", "SaveSucceeded"
            }

            For Each col As DataGridViewColumn In auditGrid.Columns
                col.Visible = visibleColumns.Contains(col.Name, StringComparer.OrdinalIgnoreCase)
            Next

            If auditGrid.Columns.Contains("CreatedOn") Then auditGrid.Columns("CreatedOn").HeaderText = "When"
            If auditGrid.Columns.Contains("UserDisplay") Then auditGrid.Columns("UserDisplay").HeaderText = "User"
            If auditGrid.Columns.Contains("ChangeCount") Then auditGrid.Columns("ChangeCount").HeaderText = "# Changes"
            If auditGrid.Columns.Contains("ChangedFields") Then auditGrid.Columns("ChangedFields").HeaderText = "Changed Fields"
        End Sub

        Private Sub AuditGrid_SelectionChanged(sender As Object, e As EventArgs)
            BindSelectedRowDetails()
        End Sub

        Private Sub BindSelectedRowDetails()
            beforeTextBox.Clear()
            afterTextBox.Clear()
            deltaGrid.DataSource = Nothing

            If auditGrid.SelectedRows.Count = 0 Then
                Return
            End If

            Dim row = auditGrid.SelectedRows(0)
            Dim beforeJson = If(row.Cells("BeforeSnapshotJson").Value, String.Empty).ToString()
            Dim afterJson = If(row.Cells("AfterSnapshotJson").Value, String.Empty).ToString()
            Dim deltaJson = If(row.Cells("DeltaJson").Value, String.Empty).ToString()

            beforeTextBox.Text = PrettyPrintJson(beforeJson)
            afterTextBox.Text = PrettyPrintJson(afterJson)
            deltaGrid.DataSource = ParseDeltaJsonToTable(deltaJson)
            ConfigureDeltaGridColumns()
        End Sub

        Private Sub ConfigureDeltaGridColumns()
            If deltaGrid.Columns Is Nothing OrElse deltaGrid.Columns.Count = 0 Then
                Return
            End If

            If deltaGrid.Columns.Contains("Field") Then deltaGrid.Columns("Field").HeaderText = "Field"
            If deltaGrid.Columns.Contains("Before") Then deltaGrid.Columns("Before").HeaderText = "Before"
            If deltaGrid.Columns.Contains("After") Then deltaGrid.Columns("After").HeaderText = "After"
        End Sub

        Private Function PrettyPrintJson(json As String) As String
            If String.IsNullOrWhiteSpace(json) Then
                Return String.Empty
            End If

            Try
                Using document = JsonDocument.Parse(json)
                    Return JsonSerializer.Serialize(document.RootElement, New JsonSerializerOptions With {.WriteIndented = True})
                End Using
            Catch
                Return json
            End Try
        End Function

        Private Function ParseDeltaJsonToTable(deltaJson As String) As DataTable
            Dim table As New DataTable("Delta")
            table.Columns.Add("Field", GetType(String))
            table.Columns.Add("Before", GetType(String))
            table.Columns.Add("After", GetType(String))

            If String.IsNullOrWhiteSpace(deltaJson) Then
                Return table
            End If

            Try
                Dim rows = JsonSerializer.Deserialize(Of List(Of Dictionary(Of String, String)))(deltaJson)
                If rows Is Nothing Then
                    Return table
                End If

                For Each entry In rows
                    Dim row = table.NewRow()
                    Dim fieldName = If(entry.ContainsKey("Field"), entry("Field"), String.Empty)
                    Dim beforeValue = If(entry.ContainsKey("Before"), entry("Before"), String.Empty)
                    Dim afterValue = If(entry.ContainsKey("After"), entry("After"), String.Empty)

                    row("Field") = fieldName
                    row("Before") = ToFriendlyDeltaValue(fieldName, beforeValue)
                    row("After") = ToFriendlyDeltaValue(fieldName, afterValue)
                    table.Rows.Add(row)
                Next
            Catch
                ' Ignore malformed delta payload and return an empty details table.
            End Try

            Return table
        End Function

        Private Shared Function ToFriendlyDeltaValue(fieldName As String, value As String) As String
            Dim raw = If(value, String.Empty)
            If String.IsNullOrWhiteSpace(raw) Then
                Return raw
            End If

            If String.IsNullOrWhiteSpace(fieldName) OrElse
               Not fieldName.StartsWith("ComboBox_", StringComparison.OrdinalIgnoreCase) Then
                Return raw
            End If

            Dim text = raw.Trim()
            Dim openParenIndex = text.LastIndexOf(" (", StringComparison.Ordinal)
            If openParenIndex <= 0 OrElse Not text.EndsWith(")", StringComparison.Ordinal) Then
                Return text
            End If

            Return text.Substring(0, openParenIndex).Trim()
        End Function
    End Class
End Namespace
