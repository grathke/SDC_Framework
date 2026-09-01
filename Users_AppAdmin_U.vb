Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld

    Public Class Users_AppAdmin_U
        Inherits FW_Base_U

        Private ReadOnly mode As UserAdminEditMode
        Private ReadOnly userIdTextBox As TextBox
        Private ReadOnly firstNameTextBox As TextBox
        Private ReadOnly lastNameTextBox As TextBox
        Private ReadOnly emailTextBox As TextBox
        Private ReadOnly phoneTextBox As TextBox
        Private ReadOnly addressTextBox As TextBox
        Private ReadOnly address2TextBox As TextBox
        Private ReadOnly cityTextBox As TextBox
        Private ReadOnly stateTextBox As TextBox
        Private ReadOnly zipTextBox As TextBox
        Private smartyAddressLookupController As SmartyAddressLookupController
        Private zipCoderController As ZipCoderController
        Private ReadOnly passwordTextBox As TextBox
        Private ReadOnly activeCheckBox As CheckBox
        Private ReadOnly superAdminCheckBox As CheckBox
        Private ReadOnly availableRolesGrid As DataGridView
        Private ReadOnly assignedRolesGrid As DataGridView
        Private ReadOnly assignButton As Button
        Private ReadOnly removeButton As Button
        Private ReadOnly registrationLabel As Label
        Private ReadOnly registrationIdTextBox As TextBox

        <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
        Public Property UserData As UserAdminRecord

        Public Sub New(editMode As UserAdminEditMode, data As UserAdminRecord)
            mode = editMode
            UserData = CloneRecord(data)

            Me.ClientSize = New Size(860, 664)

            ' Hidden key field used by shared audit operation detection.
            userIdTextBox = New TextBox() With {
                .Name = "TextBox_UserID",
                .Location = New Point(-10000, -10000),
                .Size = New Size(1, 1),
                .Visible = False,
                .ReadOnly = True,
                .TabStop = False
            }
            Me.Controls.Add(userIdTextBox)

            Dim userTitleLabel = New Label() With {
                .Name = "Label_UserTitle",
                .Text = DialogTitle(),
                .Font = New Font("Segoe UI", 14.0F, FontStyle.Bold),
                .AutoSize = True,
                .Location = New Point(20, 15)
            }
            Me.Controls.Add(userTitleLabel)

            ' User fields (left column)
            Dim y = 62
            firstNameTextBox = AddField("FirstName", y, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode, True)
            y += 42
            lastNameTextBox = AddField("LastName", y, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode, True)
            y += 42
            emailTextBox = AddField("Email", y, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode, True)
            y += 42
            phoneTextBox = AddField("Phone", y, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode)
            y += 42
            passwordTextBox = AddField("Password", y, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode)
            passwordTextBox.UseSystemPasswordChar = False
            y += 42

            addressTextBox = AddField("Address1", 62, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode, False, 500)
            address2TextBox = AddField("Address2", 104, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode, False, 500)
            cityTextBox = AddField("City", 104, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode, False, 500)
            stateTextBox = AddField("State", 146, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode, False, 500)
            zipTextBox = AddField("Zip", 188, mode = UserAdminEditMode.ReadMode OrElse mode = UserAdminEditMode.DeleteMode, False, 500)
            EnsureAddressFieldLabels()
            ArrangeUserFields()

            activeCheckBox = New CheckBox() With {
                .Name = "CheckBox_IsActive",
                .Text = "Is Active",
                .Location = New Point(150, y + 2),
                .AutoSize = True,
                .TabStop = True,
                .Enabled = mode <> UserAdminEditMode.ReadMode AndAlso mode <> UserAdminEditMode.DeleteMode
            }
            Me.Controls.Add(activeCheckBox)
            y += 36

            superAdminCheckBox = New CheckBox() With {
                .Name = "CheckBox_SuperAdmin",
                .Text = "Super Admin",
                .Location = New Point(150, y + 2),
                .AutoSize = True,
                .Enabled = mode <> UserAdminEditMode.ReadMode AndAlso mode <> UserAdminEditMode.DeleteMode
            }
            Me.Controls.Add(superAdminCheckBox)

            ' Role assignment section
            Dim leftGridLeft As Integer = 20
            Dim gridTop As Integer = 364
            Dim gridWidth As Integer = 340
            Dim gridHeight As Integer = 220
            Dim gapBetweenGrids As Integer = 140
            Dim rightGridLeft As Integer = leftGridLeft + gridWidth + gapBetweenGrids
            Dim transferButtonWidth As Integer = 60
            Dim centerBetweenGridsX As Integer = leftGridLeft + gridWidth + (gapBetweenGrids \ 2)

            Dim rolesLabel = New Label() With {
                .Text = "Available Roles",
                .Location = New Point(leftGridLeft, 344),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
            }
            Dim assignedLabel = New Label() With {
                .Text = "Assigned Roles",
                .Location = New Point(rightGridLeft, 344),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
            }

            availableRolesGrid = New DataGridView() With {
                .Name = "availableRolesGrid",
                .Location = New Point(leftGridLeft, gridTop),
                .Size = New Size(gridWidth, gridHeight),
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                .BackgroundColor = Color.White,
                .RowHeadersVisible = False
            }
            ApplyLightBlueHeaderStyle(availableRolesGrid)

            assignedRolesGrid = New DataGridView() With {
                .Name = "assignedRolesGrid",
                .Location = New Point(rightGridLeft, gridTop),
                .Size = New Size(gridWidth, gridHeight),
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                .BackgroundColor = Color.White,
                .RowHeadersVisible = False
            }
            ApplyLightBlueHeaderStyle(assignedRolesGrid)

            assignButton = New Button() With {
                .Text = ">>",
                .Location = New Point(centerBetweenGridsX - (transferButtonWidth \ 2), 444),
                .Size = New Size(transferButtonWidth, 36),
                .Enabled = mode <> UserAdminEditMode.ReadMode AndAlso mode <> UserAdminEditMode.DeleteMode
            }
            removeButton = New Button() With {
                .Text = "<<",
                .Location = New Point(centerBetweenGridsX - (transferButtonWidth \ 2), 494),
                .Size = New Size(transferButtonWidth, 36),
                .Enabled = mode <> UserAdminEditMode.ReadMode AndAlso mode <> UserAdminEditMode.DeleteMode
            }

            AddHandler assignButton.Click, AddressOf AssignButton_Click
            AddHandler removeButton.Click, AddressOf RemoveButton_Click

            Me.Controls.AddRange({rolesLabel, assignedLabel, availableRolesGrid,
                                   assignedRolesGrid, assignButton, removeButton})

            ' Move base buttons down
            okButton.Location = New Point(580, 614)
            cancelActionButton.Location = New Point(720, 614)
            enumButton.Location = New Point(20, 614)

            registrationLabel = New Label() With {
                .Name = "Label_RegistrationID",
                .Text = "Reg ID",
                .Location = New Point(100, 619),
                .Size = New Size(55, 24),
                .Visible = IsApplicationAdminSession()
            }
            registrationIdTextBox = New TextBox() With {
                .Name = "TextBox_RegistrationID",
                .Location = New Point(160, 614),
                .Size = New Size(70, 26),
                .ReadOnly = True,
                .TabStop = False,
                .Visible = registrationLabel.Visible
            }
            Me.Controls.Add(registrationLabel)
            Me.Controls.Add(registrationIdTextBox)

            HookDirtyEvents()
            BindToForm()
            If mode <> UserAdminEditMode.ReadMode AndAlso mode <> UserAdminEditMode.DeleteMode Then
                smartyAddressLookupController = New SmartyAddressLookupController(
                    Me,
                    addressTextBox,
                    cityTextBox,
                    stateTextBox,
                    zipTextBox,
                    Function() SmartyAddressLookupController.IsSessionLookupEnabled(),
                    Function() SmartyAddressLookupController.GetSessionEmbeddedKey())
                zipCoderController = New ZipCoderController(Me, cityTextBox, stateTextBox, zipTextBox)
            End If
            ApplyRequiredLabelStyle()
            SetManualTabOrder(firstNameTextBox,
                              lastNameTextBox,
                              addressTextBox,
                              address2TextBox,
                              cityTextBox,
                              stateTextBox,
                              zipTextBox,
                              If(zipCoderController Is Nothing, Nothing, zipCoderController.ZipCoderButton_Control),
                              emailTextBox,
                              phoneTextBox,
                              passwordTextBox,
                              activeCheckBox,
                              superAdminCheckBox,
                              enumButton,
                              okButton,
                              cancelActionButton)
            availableRolesGrid.TabStop = False
            assignButton.TabStop = False
            removeButton.TabStop = False
            assignedRolesGrid.TabStop = False
        End Sub

        ''' <summary>
        ''' This page is laid out in two columns: name and address on the left from x=20, contact
        ''' details on the right from x=500. Declaring the split lets a hidden left-hand field pull
        ''' the fields under it up without disturbing Email, Phone and Password.
        ''' </summary>
        Protected Overrides Function GetLayoutColumnLefts() As Integer()
            Return New Integer() {0, 490}
        End Function

        Private Shared Function IsApplicationAdminSession() As Boolean
            Return SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso
                   SessionState.Current.Value.IsApplicationAdminRole
        End Function
        Private Sub EnsureAddressFieldLabels()
            SetAddressFieldLabel("Label_Address", "Address")
            SetAddressFieldLabel("Label_City", "City")
            SetAddressFieldLabel("Label_State", "State")
            SetAddressFieldLabel("Label_Zip", "Zip")
        End Sub

        Private Sub ArrangeUserFields()
            firstNameTextBox.MaxLength = 50
            lastNameTextBox.MaxLength = 50
            emailTextBox.MaxLength = 128
            phoneTextBox.MaxLength = 25
            passwordTextBox.MaxLength = 50
            PositionUserField("FirstName", New Point(20, 62), New Point(150, 62), 220, 120)
            PositionUserField("LastName", New Point(20, 104), New Point(150, 104), 220, 120)
            PositionUserField("Address", New Point(20, 146), New Point(150, 146), 250, 120)
            PositionUserField("Address2", New Point(20, 188), New Point(150, 188), 250, 120)
            PositionUserField("City", New Point(20, 230), New Point(150, 230), 145, 120)
            PositionUserField("State", New Point(305, 230), New Point(350, 230), 45, 40)
            PositionUserField("Zip", New Point(410, 230), New Point(445, 230), 75, 35)

            PositionUserField("Email", New Point(500, 62), New Point(620, 62), 220, 110)
            PositionUserField("Phone", New Point(500, 104), New Point(620, 104), 135, 110)
            PositionUserField("Password", New Point(500, 146), New Point(620, 146), 150, 110)

            addressTextBox.MaxLength = 75
            address2TextBox.MaxLength = 75
            cityTextBox.MaxLength = 75
            stateTextBox.MaxLength = 2
            zipTextBox.MaxLength = 10
        End Sub

        Private Sub PositionUserField(fieldName As String, labelLocation As Point,
                                      textBoxLocation As Point, textBoxWidth As Integer,
                                      labelWidth As Integer)
            Dim labels = Controls.Find("Label_" & fieldName, True)
            If labels.Length > 0 Then
                Dim label = TryCast(labels(0), Label)
                If label IsNot Nothing Then
                    label.Location = labelLocation
                    label.Size = New Size(labelWidth, 26)
                End If
            End If

            Dim textBoxes = Controls.Find("TextBox_" & fieldName, True)
            If textBoxes.Length > 0 Then
                Dim textBox = TryCast(textBoxes(0), TextBox)
                If textBox IsNot Nothing Then
                    textBox.Location = textBoxLocation
                    textBox.Width = textBoxWidth
                End If
            End If
        End Sub

        Private Sub SetAddressFieldLabel(labelName As String, caption As String)
            Dim labels = Controls.Find(labelName, True)
            If labels.Length = 0 Then Return

            Dim label = TryCast(labels(0), Label)
            If label Is Nothing Then Return

            label.Text = caption
            label.Visible = True
            label.BringToFront()
        End Sub

        Protected Overrides Sub BindToFormInternal()
            CaptureOriginalRowVersion(UserData.RowVersion)
            userIdTextBox.Text = UserData.UserID.ToString(CultureInfo.InvariantCulture)
            firstNameTextBox.Text = If(UserData.FirstName, String.Empty)
            lastNameTextBox.Text = If(UserData.LastName, String.Empty)
            emailTextBox.Text = If(UserData.Email, String.Empty)
            phoneTextBox.Text = If(UserData.Phone, String.Empty)
            addressTextBox.Text = If(UserData.Address1, String.Empty)
            address2TextBox.Text = If(UserData.Address2, String.Empty)
            cityTextBox.Text = If(UserData.City, String.Empty)
            stateTextBox.Text = If(UserData.State, String.Empty)
            zipTextBox.Text = If(UserData.Zip, String.Empty)
            activeCheckBox.Checked = UserData.IsActive
            superAdminCheckBox.Checked = UserData.SuperAdmin

            ' DataBindings for enumeration discovery
            firstNameTextBox.DataBindings.Clear()
            firstNameTextBox.DataBindings.Add("Text", UserData, "FirstName", True)
            lastNameTextBox.DataBindings.Clear()
            lastNameTextBox.DataBindings.Add("Text", UserData, "LastName", True)
            emailTextBox.DataBindings.Clear()
            emailTextBox.DataBindings.Add("Text", UserData, "Email", True)
            phoneTextBox.DataBindings.Clear()
            phoneTextBox.DataBindings.Add("Text", UserData, "Phone", True)
            addressTextBox.DataBindings.Clear()
            addressTextBox.DataBindings.Add("Text", UserData, "Address1", True)
            address2TextBox.DataBindings.Clear()
            address2TextBox.DataBindings.Add("Text", UserData, "Address2", True)
            cityTextBox.DataBindings.Clear()
            cityTextBox.DataBindings.Add("Text", UserData, "City", True)
            stateTextBox.DataBindings.Clear()
            stateTextBox.DataBindings.Add("Text", UserData, "State", True)
            zipTextBox.DataBindings.Clear()
            zipTextBox.DataBindings.Add("Text", UserData, "Zip", True)
            ConfigurePasswordEditorForCurrentUser()
            activeCheckBox.DataBindings.Clear()
            activeCheckBox.DataBindings.Add("Checked", UserData, "IsActive", True)
            superAdminCheckBox.DataBindings.Clear()
            superAdminCheckBox.DataBindings.Add("Checked", UserData, "SuperAdmin", True)

            LoadAvailableRoles()
            If UserData.UserID > 0 Then LoadAssignedRoles()

            registrationIdTextBox.Text = UserData.RegistrationID.ToString(CultureInfo.InvariantCulture)
            registrationIdTextBox.DataBindings.Clear()
            registrationIdTextBox.DataBindings.Add("Text", UserData, "RegistrationID", True, DataSourceUpdateMode.Never)
        End Sub

        Private Sub ApplyRequiredLabelStyle()
            ApplyRequiredLabelStyle(Me)
        End Sub

        Private Sub ApplyRequiredLabelStyle(container As Control)
            For Each control As Control In container.Controls
                If IsAppAdminRequiredControl(control) Then
                    Dim labelName = GetRequiredLabelName(control.Name)
                    If Not String.IsNullOrWhiteSpace(labelName) Then
                        Dim labels = Me.Controls.Find(labelName, True)
                        If labels.Length > 0 Then
                            Dim label = TryCast(labels(0), Label)
                            If label IsNot Nothing Then
                                label.Text = EnsureSingleRequiredMarker(label.Text)
                                label.BackColor = Color.FromArgb(221, 235, 247)
                            End If
                        End If
                    End If
                End If

                If control.Controls.Count > 0 Then
                    ApplyRequiredLabelStyle(control)
                End If
            Next
        End Sub

        Private Shared Function IsAppAdminRequiredControl(control As Control) As Boolean
            If control Is Nothing OrElse Not String.Equals(If(control.Tag, String.Empty).ToString(), "Required", StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            Return String.Equals(control.Name, "TextBox_FirstName", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(control.Name, "TextBox_LastName", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(control.Name, "TextBox_Email", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function GetRequiredLabelName(controlName As String) As String
            If String.IsNullOrWhiteSpace(controlName) Then Return String.Empty

            If controlName.StartsWith("TextBox_", StringComparison.OrdinalIgnoreCase) Then
                Return "Label_" & controlName.Substring(8)
            End If

            If controlName.StartsWith("ComboBox_", StringComparison.OrdinalIgnoreCase) Then
                Return "Label_" & controlName.Substring(9)
            End If

            If controlName.StartsWith("CheckBox_", StringComparison.OrdinalIgnoreCase) Then
                Return "Label_" & controlName.Substring(9)
            End If

            Return String.Empty
        End Function

        Private Sub LoadAvailableRoles()
            Try
                Dim registrationId = GetEffectiveRegistrationId()
                Dim roles = DataAccess.GetRolesForUserAssignment(registrationId, UserData.UserID)
                availableRolesGrid.DataSource = roles
                FormatAvailableRolesGrid()
            Catch ex As Exception
                ' Silently skip
            End Try
        End Sub

        Private Sub FormatAvailableRolesGrid()
            If availableRolesGrid.Columns Is Nothing OrElse availableRolesGrid.Columns.Count = 0 Then
                Return
            End If

            For Each col As DataGridViewColumn In availableRolesGrid.Columns
                Dim columnName = If(col.DataPropertyName, col.Name)
                col.Visible = String.Equals(columnName, "RoleName", StringComparison.OrdinalIgnoreCase)
                col.HeaderText = ToPascalCaseDisplay(columnName)
            Next
        End Sub

        Private Sub LoadAssignedRoles()
            Try
                Dim roles = DataAccess.GetUserRoles(UserData.UserID)
                assignedRolesGrid.DataSource = roles
                FormatAssignedRolesGrid()
            Catch ex As Exception
                ' Silently skip
            End Try
        End Sub

        Private Sub FormatAssignedRolesGrid()
            If assignedRolesGrid.Columns Is Nothing OrElse assignedRolesGrid.Columns.Count = 0 Then
                Return
            End If

            For Each col As DataGridViewColumn In assignedRolesGrid.Columns
                col.Visible = String.Equals(col.Name, "RoleName", StringComparison.OrdinalIgnoreCase)
            Next

            If assignedRolesGrid.Columns.Contains("RoleName") Then
                assignedRolesGrid.Columns("RoleName").HeaderText = "Role Name"
                assignedRolesGrid.Columns("RoleName").AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            End If
        End Sub

        Private Sub AssignButton_Click(sender As Object, e As EventArgs)
            If availableRolesGrid.SelectedRows.Count = 0 Then Return
            Try
                If Not ValidateAndSaveParentForRoleAssignment() Then
                    Return
                End If

                Dim roleId As Integer = 0
                Integer.TryParse(availableRolesGrid.SelectedRows(0).Cells("ID").Value?.ToString(), roleId)
                Dim displayOrder As Integer = 0
                Integer.TryParse(availableRolesGrid.SelectedRows(0).Cells("DisplayOrder").Value?.ToString(), displayOrder)
                If roleId > 0 Then
                    If displayOrder <= 0 Then
                        MessageBox.Show("DisplayOrder is missing from the selected role.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If

                    Dim createdBy = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)
                    DataAccess.AssignRoleToUser(UserData.UserID, UserData.RegistrationID, roleId, displayOrder, createdBy)
                    LoadAvailableRoles()
                    LoadAssignedRoles()
                Else
                    MessageBox.Show("No role selected.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End If
            Catch ex As Exception
                MessageBox.Show("Error assigning role: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Function ValidateAndSaveParentForRoleAssignment() As Boolean
            If Not ValidateAndBuildForSave() Then
                Return False
            End If

            If UserData.RegistrationID <= 0 Then
                UserData.RegistrationID = GetEffectiveRegistrationId()
            End If

            If UserData.RegistrationID <= 0 Then
                MessageBox.Show("RegistrationID is missing from the user record.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If

            If UserData.UserID = 0 Then
                If Not SaveRecordWithAudit() Then
                    Return False
                End If

                If UserData.UserID = 0 Then
                    MessageBox.Show("User was not saved. UserID is still 0.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return False
                End If
            End If

            Return True
        End Function

        Private Function GetEffectiveRegistrationId() As Integer
            If UserData IsNot Nothing AndAlso UserData.RegistrationID > 0 Then
                Return UserData.RegistrationID
            End If

            If SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso SessionState.Current.Value.RegistrationID > 0 Then
                Return SessionState.Current.Value.RegistrationID
            End If

            Return 0
        End Function

        Private Shared Function NormalizeLoginEmailForValidation(value As String) As String
            If value Is Nothing Then
                Return String.Empty
            End If

            Return value.Replace(" ", String.Empty).Trim().ToLowerInvariant()
        End Function

        Private Sub RemoveButton_Click(sender As Object, e As EventArgs)
            If assignedRolesGrid.SelectedRows.Count = 0 Then Return
            Try
                Dim roleId As Integer = 0
                Integer.TryParse(assignedRolesGrid.SelectedRows(0).Cells("RoleID").Value?.ToString(), roleId)
                If roleId > 0 Then
                    DataAccess.RemoveRoleFromUser(UserData.UserID, roleId)
                    LoadAvailableRoles()
                    LoadAssignedRoles()
                End If
            Catch ex As Exception
                MessageBox.Show("Error removing role: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Protected Overrides Function TryBuildRecord() As Boolean
            Dim rawEmail = If(emailTextBox.Text, String.Empty).Trim()
            Dim normalizedEmail = NormalizeLoginEmailForValidation(rawEmail)

            If String.IsNullOrWhiteSpace(normalizedEmail) Then
                MessageBox.Show("Email is required because it is used for login.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                emailTextBox.Focus()
                Return False
            End If

            Dim currentUserId = If(UserData IsNot Nothing, UserData.UserID, 0)
            If Not DataAccess.IsUserLoginUnique(rawEmail, currentUserId) Then
                MessageBox.Show("That login email already belongs to another user. Enter a unique email.", "Duplicate Login", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                emailTextBox.Focus()
                Return False
            End If

            Dim record = New UserAdminRecord() With {
                .UserID = UserData.UserID,
                .RegistrationID = UserData.RegistrationID,
                .FirstName = firstNameTextBox.Text.Trim(),
                .LastName = lastNameTextBox.Text.Trim(),
                .Email = rawEmail,
                .Phone = phoneTextBox.Text.Trim(),
                .Address1 = addressTextBox.Text.Trim(),
                .Address2 = address2TextBox.Text.Trim(),
                .City = cityTextBox.Text.Trim(),
                .State = stateTextBox.Text.Trim(),
                .Zip = zipTextBox.Text.Trim(),
                .IsActive = activeCheckBox.Checked,
                .SuperAdmin = superAdminCheckBox.Checked,
                .RowVersion = CopyOriginalRowVersion()
            }
            UserData = record
            Return True
        End Function

        Protected Overrides Function SaveRecord() As Boolean
            ' Persist new records (UserID = 0)
            If UserData.UserID = 0 Then
                Try
                    Dim createdBy = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)
                    Dim newUserId = DataAccess.CreateUser(UserData, createdBy)
                    If newUserId > 0 Then
                        UserData.UserID = newUserId
                        If Not PersistPasswordIfChanged(createdBy) Then
                            Return False
                        End If
                        Return True
                    End If
                    MessageBox.Show("Failed to create user record.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return False
                Catch ex As Exception
                    MessageBox.Show("Error saving user: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return False
                End Try
            End If

            ' Persist existing records (UserID > 0)
            If UserData.UserID > 0 Then
                Try
                    Dim updatedBy = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)
                    Dim saveResult = DataAccess.UpdateUser(UserData, updatedBy)
                    If saveResult = SaveResult.ConcurrencyUnavailable Then
                        ShowConcurrencyUnavailable()
                        Return False
                    End If

                    If saveResult = SaveResult.RecordChanged Then
                        ' A deleted record is not a conflict to overwrite - saying so first stops
                        ' the edits being written onto a record nobody can see any more.
                        If HandleRecordDeletedDuringSave() Then Return False

                        If Not ConfirmConcurrencyOverwrite() Then
                            Return False
                        End If

                        Dim latestRecord = DataAccess.GetUserByID(UserData.UserID)
                        If latestRecord Is Nothing OrElse latestRecord.RowVersion Is Nothing Then
                            MessageBox.Show(Me, "The record no longer exists.", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            Return False
                        End If

                        UserData.RowVersion = latestRecord.RowVersion
                        saveResult = DataAccess.UpdateUser(UserData, updatedBy)
                    End If

                    If saveResult <> SaveResult.Succeeded Then
                        MessageBox.Show(Me, "The user could not be saved.", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return False
                    End If
                    If Not PersistPasswordIfChanged(updatedBy) Then
                        Return False
                    End If
                    Return True
                Catch ex As Exception
                    MessageBox.Show("Error saving user: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return False
                End Try
            End If

            Return True
        End Function

        Protected Overrides Sub ApplyMode()
            ' Mode already applied in constructor
        End Sub

        Protected Overrides Function GetPageName() As String
            Return "Users_AppAdmin_U"
        End Function

        Protected Overrides Function GetTableNameOverride() As String
            Return "FW_Users"
        End Function

        Protected Overrides Function IsViewOnly() As Boolean
            Return mode = UserAdminEditMode.ReadMode
        End Function

        Protected Overrides Function IsCreatingNewRecord() As Boolean
            Return mode = UserAdminEditMode.CreateMode
        End Function

        Protected Overrides Function ShouldWarnOnCancel() As Boolean
            Return mode = UserAdminEditMode.CreateMode OrElse mode = UserAdminEditMode.UpdateMode
        End Function

        Protected Overrides Function ResolveAuditRecordKey() As String
            If UserData IsNot Nothing AndAlso UserData.UserID > 0 Then
                Return UserData.UserID.ToString(CultureInfo.InvariantCulture)
            End If

            Return String.Empty
        End Function

        Protected Overrides Function ResolveAuditOperationType() As String
            Select Case mode
                Case UserAdminEditMode.CreateMode
                    Return "Create"
                Case UserAdminEditMode.UpdateMode
                    Return "Modify"
                Case UserAdminEditMode.DeleteMode
                    Return "Delete"
                Case Else
                    Return String.Empty
            End Select
        End Function

        Protected Overrides Function OkButtonText() As String
            If mode = UserAdminEditMode.DeleteMode Then Return "Delete"
            Return "OK"
        End Function

        Protected Overrides Function BuildMaintenanceTitle() As String
            Return DialogTitle()
        End Function

        Private Function DialogTitle() As String
            Select Case mode
                Case UserAdminEditMode.CreateMode : Return "User Maintenance - Create"
                Case UserAdminEditMode.ReadMode : Return "User Maintenance - Read"
                Case UserAdminEditMode.UpdateMode : Return "User Maintenance - Update"
                Case UserAdminEditMode.DeleteMode : Return "User Maintenance - Delete"
                Case Else : Return "User Maintenance"
            End Select
        End Function

        Private Sub HookDirtyEvents()
            AddHandler firstNameTextBox.TextChanged, AddressOf MarkDirty
            AddHandler lastNameTextBox.TextChanged, AddressOf MarkDirty
            AddHandler emailTextBox.TextChanged, AddressOf MarkDirty
            AddHandler phoneTextBox.TextChanged, AddressOf MarkDirty
            AddHandler passwordTextBox.TextChanged, AddressOf MarkDirty
            AddHandler activeCheckBox.CheckedChanged, AddressOf MarkDirty
        End Sub

        Protected Overrides Function GetAdditionalValidationMessageLines() As IEnumerable(Of String)
            Return Array.Empty(Of String)()
        End Function

        Private Function GetEmailCaptionForValidation() As String
            Dim emailLabel = TryCast(Me.Controls.Find("Label_Email", True).FirstOrDefault(), Label)
            Dim caption = If(emailLabel IsNot Nothing, emailLabel.Text, "Email")
            Return StripRequiredMarker(caption)
        End Function

        Private Shared Function StripRequiredMarker(caption As String) As String
            Dim result = If(caption, String.Empty).Trim()
            While result.EndsWith("*", StringComparison.Ordinal)
                result = result.Substring(0, result.Length - 1).TrimEnd()
            End While

            Return If(String.IsNullOrWhiteSpace(result), "Email", result)
        End Function

        Private Shared Function EnsureSingleRequiredMarker(caption As String) As String
            Dim result = If(caption, String.Empty).Trim()
            While result.EndsWith("*", StringComparison.Ordinal)
                result = result.Substring(0, result.Length - 1).TrimEnd()
            End While

            If String.IsNullOrWhiteSpace(result) Then
                Return "*"
            End If

            Return result & " *"
        End Function

        ''' <summary>
        ''' An existing user's password is already hashed, so the box shows the same sentinel the
        ''' database holds in [Password]. Typing over it is what sets a new password; leaving it
        ''' alone leaves the password untouched. The value is never swapped out on focus, so it
        ''' survives a tab through the field and the unsaved-changes check stays honest.
        ''' </summary>
        Private Sub ConfigurePasswordEditorForCurrentUser()
            loading = True
            passwordTextBox.UseSystemPasswordChar = False
            passwordTextBox.ForeColor = SystemColors.WindowText
            passwordTextBox.Text = If(UserData IsNot Nothing AndAlso UserData.UserID > 0,
                                      DataAccess.StoredPasswordMask,
                                      String.Empty)
            loading = False
        End Sub

        Private Function PersistPasswordIfChanged(updatedBy As Integer) As Boolean
            If UserData Is Nothing OrElse UserData.UserID <= 0 Then
                Return True
            End If

            ' Still holding the sentinel means the password was not retyped. Nothing to hash.
            Dim pendingPassword = If(passwordTextBox.Text, String.Empty).Trim()
            If pendingPassword = String.Empty OrElse
               String.Equals(pendingPassword, DataAccess.StoredPasswordMask, StringComparison.Ordinal) Then
                Return True
            End If

            If Not DataAccess.UpdateUserPasswordHash(UserData.UserID, pendingPassword, updatedBy) Then
                MessageBox.Show("Password could not be updated.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End If

            ConfigurePasswordEditorForCurrentUser()
            Return True
        End Function

        Private Function CloneRecord(record As UserAdminRecord) As UserAdminRecord
            If record Is Nothing Then Return New UserAdminRecord()
            Return New UserAdminRecord() With {
                .UserID = record.UserID,
                .RegistrationID = record.RegistrationID,
                .FirstName = record.FirstName,
                .LastName = record.LastName,
                .FirstLast = record.FirstLast,
                .LastFirst = record.LastFirst,
                .Email = record.Email,
                .Phone = record.Phone,
                .Address1 = record.Address1,
                .Address2 = record.Address2,
                .City = record.City,
                .State = record.State,
                .Zip = record.Zip,
                .IsActive = record.IsActive,
                .SuperAdmin = record.SuperAdmin,
                .RowVersion = If(record.RowVersion Is Nothing, Nothing, CType(record.RowVersion.Clone(), Byte()))
            }
        End Function
    End Class

End Namespace
