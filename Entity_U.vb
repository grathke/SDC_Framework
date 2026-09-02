Option Strict On
Option Explicit On

Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class Entity_U
        Inherits FW_Base_U

        Private ReadOnly mode As EntityEditMode
        Private ReadOnly idTextBox As TextBox
        Private ReadOnly registrationIdTextBox As TextBox
        Private ReadOnly assignedManagerComboBox As ComboBox
        Private ReadOnly genderComboBox As ComboBox
        Private ReadOnly firstNameTextBox As TextBox
        Private ReadOnly middleNameTextBox As TextBox
        Private ReadOnly lastNameTextBox As TextBox
        Private ReadOnly firstLastTextBox As TextBox
        Private ReadOnly lastFirstTextBox As TextBox
        Private ReadOnly emailTextBox As TextBox
        Private ReadOnly phoneTextBox As TextBox
        Private ReadOnly activeCheckBox As CheckBox
        Private ReadOnly entityTableName As String

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property EntityData As EntityRecord

        Public Sub New(editMode As EntityEditMode, data As EntityRecord, Optional tableName As String = "FW_Entity")
            MyBase.New()
            mode = editMode
            EntityData = CloneRecord(data)
            entityTableName = If(String.IsNullOrWhiteSpace(tableName), "FW_Entity", tableName.Trim())

            Me.ClientSize = New Size(600, 550)

            Dim y = 20
            idTextBox = AddField("EntityID", y, True, False)
            y += 42
            
            ' Create AssignedManagerID ComboBox
            Dim assignedManagerLabel As New Label() With {
                .Name = "Label_AssignedManagerID",
                .Text = "AssignedManagerID",
                .Location = New Point(20, y),
                .Size = New Size(120, 26),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Me.Controls.Add(assignedManagerLabel)
            
            assignedManagerComboBox = New ComboBox() With {
                .Name = "ComboBox_AssignedManagerID",
                .Location = New Point(150, y),
                .Size = New Size(320, 26),
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .TabIndex = 0
            }
            Me.Controls.Add(assignedManagerComboBox)
            y += 42

            Dim genderLabel As New Label() With {
                .Name = "Label_GenderID",
                .Text = "Gender",
                .Location = New Point(20, y),
                .Size = New Size(120, 26),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Me.Controls.Add(genderLabel)

            genderComboBox = New ComboBox() With {
                .Name = "ComboBox_GenderID",
                .Location = New Point(150, y),
                .Size = New Size(320, 26),
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .TabIndex = 1
            }
            Me.Controls.Add(genderComboBox)
            y += 42
            
            firstNameTextBox = AddField("FirstName", y, False, True)
            y += 42
            middleNameTextBox = AddField("MiddleName", y, False, False)
            y += 42
            lastNameTextBox = AddField("LastName", y, False, True)
            y += 42
            firstLastTextBox = AddField("FirstLast", y, True, False)
            y += 42
            lastFirstTextBox = AddField("LastFirst", y, True, False)
            y += 42
            emailTextBox = AddField("EMail1", y, False, True)
            y += 42
            phoneTextBox = AddField("Phone1", y, False, False)

            firstNameTextBox.MaxLength = 50
            middleNameTextBox.MaxLength = 30
            lastNameTextBox.MaxLength = 50
            firstLastTextBox.MaxLength = 101
            lastFirstTextBox.MaxLength = 102
            emailTextBox.MaxLength = 72
            phoneTextBox.MaxLength = 20

            activeCheckBox = New CheckBox() With {
                .Name = "CheckBox_IsActive",
                .Text = "IsActive",
                .Location = New Point(490, 20),
                .AutoSize = True,
                .TabStop = False
            }
            Me.Controls.Add(activeCheckBox)

            registrationIdTextBox = AddField("RegistrationID", 500, True, False, 100)
            Dim registrationIdLabels = Me.Controls.Find("Label_RegistrationID", True)
            If registrationIdLabels.Length > 0 Then
                registrationIdLabels(0).Text = "Reg ID"
            End If
            registrationIdTextBox.ReadOnly = True
            registrationIdTextBox.TabStop = False
            registrationIdTextBox.Width = 70
            registrationIdTextBox.Visible = IsApplicationAdminSession()
            If registrationIdLabels.Length > 0 Then
                registrationIdLabels(0).Width = 55
                registrationIdLabels(0).Location = New Point(100, 505)
                registrationIdLabels(0).Visible = registrationIdTextBox.Visible
            End If
            registrationIdTextBox.Location = New Point(160, 500)

            HookDirtyEvents()
            BindToForm()
            ApplyMode()
            ApplyRegistrationIdPresentation()
            ApplyEntityTabOrderTest()
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            BeginInvoke(New Action(Sub()
                                       ApplyEntityTabOrderTest()
                                       FocusFirstEditableControlForTest()
                                   End Sub))
        End Sub

        Private Sub ApplyEntityTabOrderTest()
            SetManualTabOrder(assignedManagerComboBox,
                              genderComboBox,
                              firstNameTextBox,
                              middleNameTextBox,
                              lastNameTextBox,
                              emailTextBox,
                              phoneTextBox,
                              okButton,
                              cancelActionButton)
            okButton.TabStop = False
            cancelActionButton.TabStop = False
        End Sub

        Private Sub FocusFirstEditableControlForTest()
            ActiveControl = assignedManagerComboBox
            assignedManagerComboBox.Focus()
        End Sub

        Private Sub HookDirtyEvents()
            AddHandler registrationIdTextBox.TextChanged, AddressOf MarkDirty
            AddHandler assignedManagerComboBox.SelectionChangeCommitted, AddressOf MarkDirty
            AddHandler genderComboBox.SelectionChangeCommitted, AddressOf MarkDirty
            AddHandler firstNameTextBox.TextChanged, AddressOf MarkDirty
            AddHandler middleNameTextBox.TextChanged, AddressOf MarkDirty
            AddHandler lastNameTextBox.TextChanged, AddressOf MarkDirty
            AddHandler emailTextBox.TextChanged, AddressOf MarkDirty
            AddHandler phoneTextBox.TextChanged, AddressOf MarkDirty
            AddHandler activeCheckBox.CheckedChanged, AddressOf MarkDirty
        End Sub

        Protected Overrides Sub BindToFormInternal()
            CaptureOriginalRowVersion(EntityData.RowVersion)
            idTextBox.Text = If(EntityData.ID <= 0, String.Empty, EntityData.ID.ToString())
            registrationIdTextBox.Text = EntityData.RegistrationID.ToString()
            
            ' Set up data bindings for enumeration
            idTextBox.DataBindings.Clear()
            idTextBox.DataBindings.Add("Text", EntityData, "ID", True)
            
            registrationIdTextBox.DataBindings.Clear()
            registrationIdTextBox.DataBindings.Add("Text", EntityData, "RegistrationID", True)
            
            ' Load users for AssignedManagerID ComboBox
            Dim activeSession = SessionState.Current
            If activeSession.HasValue Then
                Try
                    Dim users = DataAccess.GetUsersByRegistration(activeSession.Value.RegistrationID)
                    If users IsNot Nothing Then
                        ConfigureLookupCombo(assignedManagerComboBox,
                                             users,
                                             "ID",
                                             "FirstLast",
                                             EntityData.AssignedManagerID,
                                             "Make a Selection")
                        
                        ' Set up data binding for enumeration
                        assignedManagerComboBox.DataBindings.Clear()
                        assignedManagerComboBox.DataBindings.Add("SelectedValue", EntityData, "AssignedManagerID", True, DataSourceUpdateMode.Never)
                        If EntityData.AssignedManagerID <= 0 OrElse assignedManagerComboBox.SelectedIndex < 0 Then
                            assignedManagerComboBox.SelectedIndex = 0
                        End If
                    Else
                        ' No users available, clear combo
                        assignedManagerComboBox.DataSource = Nothing
                    End If
                Catch ex As Exception
                    ' If FW_Users query fails, just skip loading the combo
                    MessageBox.Show("Warning: Could not load users list: " & ex.Message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    assignedManagerComboBox.DataSource = Nothing
                End Try

                Try
                    Dim genders = DataAccess.GetGendersByRegistration(activeSession.Value.RegistrationID)
                    If genders IsNot Nothing Then
                        ConfigureLookupCombo(genderComboBox,
                                             genders,
                                             "ID",
                                             "GenderDescription",
                                             EntityData.GenderID,
                                             "Make a Selection")

                        genderComboBox.DataBindings.Clear()
                        genderComboBox.DataBindings.Add("SelectedValue", EntityData, "GenderID", True, DataSourceUpdateMode.Never)
                        If EntityData.GenderID <= 0 OrElse genderComboBox.SelectedIndex < 0 Then
                            genderComboBox.SelectedIndex = 0
                        End If
                    Else
                        genderComboBox.DataSource = Nothing
                    End If
                Catch ex As Exception
                    MessageBox.Show("Warning: Could not load gender list: " & ex.Message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    genderComboBox.DataSource = Nothing
                End Try
            End If
            
            firstNameTextBox.Text = SafeString(EntityData.FirstName)
            middleNameTextBox.Text = SafeString(EntityData.MiddleName)
            lastNameTextBox.Text = SafeString(EntityData.LastName)
            firstLastTextBox.Text = SafeString(EntityData.FirstLast)
            lastFirstTextBox.Text = SafeString(EntityData.LastFirst)
            emailTextBox.Text = SafeString(EntityData.EMail1)
            phoneTextBox.Text = SafeString(EntityData.Phone1)
            activeCheckBox.Checked = EntityData.IsActive
            
            ' Set up data bindings for enumeration
            firstNameTextBox.DataBindings.Clear()
            firstNameTextBox.DataBindings.Add("Text", EntityData, "FirstName", True)
            
            middleNameTextBox.DataBindings.Clear()
            middleNameTextBox.DataBindings.Add("Text", EntityData, "MiddleName", True)
            
            lastNameTextBox.DataBindings.Clear()
            lastNameTextBox.DataBindings.Add("Text", EntityData, "LastName", True)
            
            firstLastTextBox.DataBindings.Clear()
            firstLastTextBox.DataBindings.Add("Text", EntityData, "FirstLast", True)
            
            lastFirstTextBox.DataBindings.Clear()
            lastFirstTextBox.DataBindings.Add("Text", EntityData, "LastFirst", True)
            
            emailTextBox.DataBindings.Clear()
            emailTextBox.DataBindings.Add("Text", EntityData, "EMail1", True)
            
            phoneTextBox.DataBindings.Clear()
            phoneTextBox.DataBindings.Add("Text", EntityData, "Phone1", True)
            
            activeCheckBox.DataBindings.Clear()
            activeCheckBox.DataBindings.Add("Checked", EntityData, "IsActive", True)

            If assignedManagerComboBox.DataSource IsNot Nothing AndAlso (EntityData.AssignedManagerID <= 0 OrElse assignedManagerComboBox.SelectedIndex < 0) Then
                assignedManagerComboBox.SelectedIndex = 0
            End If

            If genderComboBox.DataSource IsNot Nothing AndAlso (EntityData.GenderID <= 0 OrElse genderComboBox.SelectedIndex < 0) Then
                genderComboBox.SelectedIndex = 0
            End If

            loading = False
            hasUnsavedChanges = False
        End Sub

        Protected Overrides Sub ApplyMode()
            Dim isReadOnlyMode = (mode = EntityEditMode.ReadMode) OrElse (mode = EntityEditMode.DeleteMode)

            registrationIdTextBox.ReadOnly = True
            registrationIdTextBox.TabStop = False
            registrationIdTextBox.Visible = IsApplicationAdminSession()
            assignedManagerComboBox.Enabled = Not isReadOnlyMode
            genderComboBox.Enabled = Not isReadOnlyMode
            firstNameTextBox.ReadOnly = isReadOnlyMode
            middleNameTextBox.ReadOnly = isReadOnlyMode
            lastNameTextBox.ReadOnly = isReadOnlyMode
            emailTextBox.ReadOnly = isReadOnlyMode
            phoneTextBox.ReadOnly = isReadOnlyMode
            activeCheckBox.Enabled = Not isReadOnlyMode

            If mode = EntityEditMode.ReadMode Then
                cancelActionButton.Text = "Close"
            End If
        End Sub

        Private Sub ApplyRegistrationIdPresentation()
            Dim registrationIdLabels = Me.Controls.Find("Label_RegistrationID", True)
            If registrationIdLabels.Length = 0 Then Return

            Dim showRegistrationId = IsApplicationAdminSession()
            Dim label = registrationIdLabels(0)
            label.Text = "Reg ID"
            label.Location = New Point(100, 505)
            label.Size = New Size(55, 26)
            label.Visible = showRegistrationId
            label.BringToFront()

            registrationIdTextBox.Location = New Point(160, 500)
            registrationIdTextBox.Size = New Size(70, 26)
            registrationIdTextBox.ReadOnly = True
            registrationIdTextBox.TabStop = False
            registrationIdTextBox.Visible = showRegistrationId
            registrationIdTextBox.BringToFront()
        End Sub

        Protected Overrides Function TryBuildRecord() As Boolean
            Dim parsedRegistrationId As Integer
            If Not Integer.TryParse(registrationIdTextBox.Text.Trim(), parsedRegistrationId) Then
                MessageBox.Show("RegistrationID must be a whole number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If

            Dim parsedAssignedManager As Integer = GetComboSelectedIdOrZero(assignedManagerComboBox)
            Dim parsedGenderId As Integer = GetComboSelectedIdOrZero(genderComboBox)
            Dim record = New EntityRecord With {
                .ID = ParseIntOrZero(idTextBox.Text),
                .RegistrationID = parsedRegistrationId,
                .AssignedManagerID = parsedAssignedManager,
                .GenderID = parsedGenderId,
                .FirstName = firstNameTextBox.Text.Trim(),
                .MiddleName = middleNameTextBox.Text.Trim(),
                .LastName = lastNameTextBox.Text.Trim(),
                .FirstLast = firstLastTextBox.Text,
                .LastFirst = lastFirstTextBox.Text,
                .EMail1 = emailTextBox.Text.Trim(),
                .Phone1 = phoneTextBox.Text.Trim(),
                .IsActive = activeCheckBox.Checked,
                .RowVersion = CopyOriginalRowVersion()
            }

            EntityData = record
            Return True
        End Function

        Protected Overrides Function IsViewOnly() As Boolean
            Return mode = EntityEditMode.ReadMode
        End Function

        Protected Overrides Function IsCreatingNewRecord() As Boolean
            Return mode = EntityEditMode.CreateMode
        End Function

        Private Shared Function IsApplicationAdminSession() As Boolean
            Return SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso
                   SessionState.Current.Value.IsApplicationAdminRole
        End Function

        Protected Overrides Function ShouldWarnOnCancel() As Boolean
            Return mode = EntityEditMode.CreateMode OrElse mode = EntityEditMode.UpdateMode
        End Function

        Protected Overrides Function OkButtonText() As String
            If mode = EntityEditMode.DeleteMode Then Return "Delete"
            Return "OK"
        End Function

        Protected Overrides Function ResolveAuditOperationType() As String
            Select Case mode
                Case EntityEditMode.CreateMode
                    Return "Create"
                Case EntityEditMode.UpdateMode
                    Return "Modify"
                Case EntityEditMode.DeleteMode
                    Return "Delete"
                Case Else
                    Return String.Empty
            End Select
        End Function

        ''' The Entity table can be renamed per registration, so the title comes from the
        ''' registration display name rather than the class name.
        Protected Overrides Function BuildMaintenanceTitle() As String
            Return DialogTitle()
        End Function

        Private Function DialogTitle() As String
            Dim registrationId As Integer = If(EntityData Is Nothing, 0, EntityData.RegistrationID)
            Return EntityDisplayNameHelper.BuildEntityMaintenanceTitle(registrationId, entityTableName, mode)
        End Function

        Private Function CloneRecord(record As EntityRecord) As EntityRecord
            If record Is Nothing Then
                Return New EntityRecord()
            End If

            Return New EntityRecord With {
                .ID = record.ID,
                .RegistrationID = record.RegistrationID,
                .AssignedManagerID = record.AssignedManagerID,
                .GenderID = record.GenderID,
                .FirstName = record.FirstName,
                .MiddleName = record.MiddleName,
                .LastName = record.LastName,
                .FirstLast = record.FirstLast,
                .LastFirst = record.LastFirst,
                .EMail1 = record.EMail1,
                .Phone1 = record.Phone1,
                .IsActive = record.IsActive,
                .RowVersion = If(record.RowVersion Is Nothing, Nothing, CType(record.RowVersion.Clone(), Byte()))
            }
        End Function

        Private Function SafeString(value As String) As String
            If value Is Nothing Then
                Return String.Empty
            End If

            Return value
        End Function
    End Class
End Namespace
