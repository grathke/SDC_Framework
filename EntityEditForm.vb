Option Strict On
Option Explicit On

Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class EntityEditForm
        Inherits Form

        Private ReadOnly mode As EntityEditMode
        Private ReadOnly idTextBox As TextBox
        Private ReadOnly registrationIdTextBox As TextBox
        Private ReadOnly assignedManagerComboBox As ComboBox
        Private ReadOnly firstNameTextBox As TextBox
        Private ReadOnly middleNameTextBox As TextBox
        Private ReadOnly lastNameTextBox As TextBox
        Private ReadOnly firstLastTextBox As TextBox
        Private ReadOnly lastFirstTextBox As TextBox
        Private ReadOnly emailTextBox As TextBox
        Private ReadOnly phoneTextBox As TextBox
        Private ReadOnly activeCheckBox As CheckBox
        Private ReadOnly okButton As Button
        Private ReadOnly cancelActionButton As Button
        Private loading As Boolean
        Private hasUnsavedChanges As Boolean
        Private ReadOnly entityTableName As String

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property EntityData As EntityRecord

        Public Sub New(editMode As EntityEditMode, data As EntityRecord, Optional tableName As String = "FW_Entity")
            mode = editMode
            EntityData = CloneRecord(data)
            entityTableName = If(String.IsNullOrWhiteSpace(tableName), "FW_Entity", tableName.Trim())

            Me.Text = DialogTitle()
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New Size(500, 500)

            Dim y = 20
            idTextBox = AddField("ID", y, True, False)
            y += 42
            registrationIdTextBox = AddField("RegistrationID", y, False, False)
            y += 42
            
            ' Create AssignedManagerID ComboBox
            Dim assignedManagerLabel As New Label() With {
                .Text = "AssignedManagerID",
                .Location = New Point(20, y + 5),
                .Size = New Size(120, 24)
            }
            Me.Controls.Add(assignedManagerLabel)
            
            assignedManagerComboBox = New ComboBox() With {
                .Location = New Point(150, y),
                .Size = New Size(320, 26),
                .DropDownStyle = ComboBoxStyle.DropDownList
            }
            Me.Controls.Add(assignedManagerComboBox)
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
            y += 45

            activeCheckBox = New CheckBox() With {
                .Text = "IsActive",
                .Location = New Point(150, y),
                .AutoSize = True
            }
            Me.Controls.Add(activeCheckBox)

            okButton = New Button() With {
                .Text = OkButtonText(),
                .Location = New Point(210, 440),
                .Size = New Size(120, 36)
            }

            cancelActionButton = New Button() With {
                .Text = "Cancel",
                .Location = New Point(350, 440),
                .Size = New Size(120, 36)
            }

            AddHandler okButton.Click, AddressOf OkButton_Click
            AddHandler cancelActionButton.Click, AddressOf CancelButton_Click

            Me.Controls.Add(okButton)
            Me.Controls.Add(cancelActionButton)
            Me.AcceptButton = okButton
            Me.CancelButton = cancelActionButton

            HookDirtyEvents()
            BindToForm()
            ApplyMode()
        End Sub

        Private Function AddField(caption As String, y As Integer, [readOnly] As Boolean, Optional required As Boolean = False) As TextBox
            Dim lbl As New Label() With {
                .Text = caption,
                .Location = New Point(20, y + 5),
                .Size = New Size(120, 24)
            }
            Me.Controls.Add(lbl)

            Dim txt As New TextBox() With {
                .Location = New Point(150, y),
                .Size = New Size(320, 26),
                .ReadOnly = [readOnly],
                .BorderStyle = BorderStyle.FixedSingle,
                .BackColor = If(required, Color.FromArgb(255, 255, 200), Color.White)
            }
            Me.Controls.Add(txt)
            Return txt
        End Function

        Private Sub HookDirtyEvents()
            AddHandler registrationIdTextBox.TextChanged, AddressOf MarkDirty
            AddHandler assignedManagerComboBox.SelectedIndexChanged, AddressOf MarkDirty
            AddHandler firstNameTextBox.TextChanged, AddressOf MarkDirty
            AddHandler middleNameTextBox.TextChanged, AddressOf MarkDirty
            AddHandler lastNameTextBox.TextChanged, AddressOf MarkDirty
            AddHandler emailTextBox.TextChanged, AddressOf MarkDirty
            AddHandler phoneTextBox.TextChanged, AddressOf MarkDirty
            AddHandler activeCheckBox.CheckedChanged, AddressOf MarkDirty
        End Sub

        Private Sub MarkDirty(sender As Object, e As EventArgs)
            If Not loading Then
                hasUnsavedChanges = True
            End If
        End Sub

        Private Sub BindToForm()
            If EntityData Is Nothing Then
                EntityData = New EntityRecord()
            End If

            loading = True
            idTextBox.Text = If(EntityData.ID <= 0, String.Empty, EntityData.ID.ToString())
            registrationIdTextBox.Text = EntityData.RegistrationID.ToString()
            
            ' Load users for AssignedManagerID ComboBox
            Dim activeSession = SessionState.Current
            If activeSession.HasValue Then
                Dim users = DataAccess.GetUsersByRegistration(activeSession.Value.RegistrationID)
                If users IsNot Nothing Then
                    Dim placeholderRow = users.NewRow()
                    placeholderRow("ID") = 0
                    placeholderRow("FirstLast") = "Make a Selection"
                    users.Rows.InsertAt(placeholderRow, 0)
                End If

                assignedManagerComboBox.DataSource = users
                assignedManagerComboBox.DisplayMember = "FirstLast"
                assignedManagerComboBox.ValueMember = "ID"
                
                ' Select the appropriate user
                If EntityData.AssignedManagerID > 0 Then
                    assignedManagerComboBox.SelectedValue = EntityData.AssignedManagerID
                Else
                    assignedManagerComboBox.SelectedIndex = 0
                End If
            End If
            
            firstNameTextBox.Text = SafeString(EntityData.FirstName)
            middleNameTextBox.Text = SafeString(EntityData.MiddleName)
            lastNameTextBox.Text = SafeString(EntityData.LastName)
            firstLastTextBox.Text = SafeString(EntityData.FirstLast)
            lastFirstTextBox.Text = SafeString(EntityData.LastFirst)
            emailTextBox.Text = SafeString(EntityData.EMail1)
            phoneTextBox.Text = SafeString(EntityData.Phone1)
            activeCheckBox.Checked = EntityData.IsActive
            loading = False
            hasUnsavedChanges = False
        End Sub

        Private Sub ApplyMode()
            Dim isReadOnlyMode = (mode = EntityEditMode.ReadMode) OrElse (mode = EntityEditMode.DeleteMode)

            registrationIdTextBox.ReadOnly = isReadOnlyMode
            assignedManagerComboBox.Enabled = Not isReadOnlyMode
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

        Private Sub OkButton_Click(sender As Object, e As EventArgs)
            If mode = EntityEditMode.ReadMode Then
                Me.DialogResult = DialogResult.OK
                Me.Close()
                Return
            End If

            Dim parsed As EntityRecord = Nothing
            If Not TryBuildRecord(parsed) Then
                Return
            End If

            EntityData = parsed
            hasUnsavedChanges = False
            Me.DialogResult = DialogResult.OK
            Me.Close()
        End Sub

        Private Sub CancelButton_Click(sender As Object, e As EventArgs)
            If ShouldWarnOnCancel() AndAlso hasUnsavedChanges Then
                Dim result = MessageBox.Show("You have unsaved changes. Discard them?", "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                If result = DialogResult.No Then
                    Return
                End If
            End If

            Me.DialogResult = DialogResult.Cancel
            Me.Close()
        End Sub

        Private Function ShouldWarnOnCancel() As Boolean
            Return mode = EntityEditMode.CreateMode OrElse mode = EntityEditMode.UpdateMode
        End Function

        Private Function TryBuildRecord(ByRef record As EntityRecord) As Boolean
            Dim parsedRegistrationId As Integer
            If Not Integer.TryParse(registrationIdTextBox.Text.Trim(), parsedRegistrationId) Then
                MessageBox.Show("RegistrationID must be a whole number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If

            Dim parsedAssignedManager As Integer = 0
            If assignedManagerComboBox.SelectedValue IsNot Nothing Then
                If Not Integer.TryParse(assignedManagerComboBox.SelectedValue.ToString(), parsedAssignedManager) Then
                    MessageBox.Show("Invalid AssignedManagerID selection.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return False
                End If
            End If
            If parsedAssignedManager <= 0 Then
                parsedAssignedManager = 0
            End If

            If firstNameTextBox.Text.Trim() = String.Empty AndAlso lastNameTextBox.Text.Trim() = String.Empty Then
                MessageBox.Show("Provide at least a first or last name.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return False
            End If

            record = New EntityRecord With {
                .ID = ParseIntOrZero(idTextBox.Text),
                .RegistrationID = parsedRegistrationId,
                .AssignedManagerID = parsedAssignedManager,
                .FirstName = firstNameTextBox.Text.Trim(),
                .MiddleName = middleNameTextBox.Text.Trim(),
                .LastName = lastNameTextBox.Text.Trim(),
                .FirstLast = firstLastTextBox.Text,
                .LastFirst = lastFirstTextBox.Text,
                .EMail1 = emailTextBox.Text.Trim(),
                .Phone1 = phoneTextBox.Text.Trim(),
                .IsActive = activeCheckBox.Checked
            }

            Return True
        End Function

        Private Function ParseIntOrZero(value As String) As Integer
            Dim parsed As Integer
            If Integer.TryParse(value, parsed) Then
                Return parsed
            End If

            Return 0
        End Function

        Private Function DialogTitle() As String
            Dim registrationId As Integer = If(EntityData Is Nothing, 0, EntityData.RegistrationID)
            Return EntityDisplayNameHelper.BuildEntityMaintenanceTitle(registrationId, entityTableName, mode)
        End Function

        Private Function OkButtonText() As String
            If mode = EntityEditMode.DeleteMode Then
                Return "Delete"
            End If

            Return "OK"
        End Function

        Private Function CloneRecord(record As EntityRecord) As EntityRecord
            If record Is Nothing Then
                Return New EntityRecord()
            End If

            Return New EntityRecord With {
                .ID = record.ID,
                .RegistrationID = record.RegistrationID,
                .AssignedManagerID = record.AssignedManagerID,
                .FirstName = record.FirstName,
                .MiddleName = record.MiddleName,
                .LastName = record.LastName,
                .FirstLast = record.FirstLast,
                .LastFirst = record.LastFirst,
                .EMail1 = record.EMail1,
                .Phone1 = record.Phone1,
                .IsActive = record.IsActive
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
