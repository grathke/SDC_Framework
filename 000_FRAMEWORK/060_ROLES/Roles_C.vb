Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class Roles_C
        Inherits Form

        Private _registrationId As Integer

        ''' <summary>
        ''' The role this dialog created, for the grid that has to point at it.
        '''
        ''' The id was already being read back from the insert and then dropped, so Roles_B
        ''' refreshed with no record in mind: the new role appeared somewhere in the list and
        ''' nothing was selected. Named to match FW_Base_U.SavedRecordId, which is what every other
        ''' browse page reads after a save.
        '''
        ''' Roles_B needs no more than this. Its grid is not capped - ExecuteCustomQuery returns
        ''' every role in the registration - so a new role is always in the result, and finding it
        ''' is the whole of the problem there.
        ''' </summary>
        Public ReadOnly Property SavedRecordId As Integer
            Get
                Return _savedRecordId
            End Get
        End Property

        Private _savedRecordId As Integer

        ' Role settings controls
        Private roleNameLabel As Label
        Private roleNameTextBox As TextBox
        Private displayOrderLabel As Label
        Private displayOrderTextBox As TextBox

        ' Permission checkboxes
        Private caCanChangeCheckBox As CheckBox
        Private canCreateCheckBox As CheckBox
        Private canReadCheckBox As CheckBox
        Private canUpdateCheckBox As CheckBox
        Private canDeleteCheckBox As CheckBox
        Private canExportCheckBox As CheckBox
        Private canImportCheckBox As CheckBox
        Private canUseQBECheckBox As CheckBox
        Private canViewAllRecordsCheckBox As CheckBox
        Private canViewOnlyMyRecordsCheckBox As CheckBox

        ' Buttons
        Private okButton As Button
        Private Shadows cancelButton As Button

        Public Sub New(registrationId As Integer)
            _registrationId = registrationId
            InitializeComponent()
        End Sub

        Private Sub InitializeComponent()
            Me.Text = "Create New Role"
            Me.Size = New Size(600, 550)
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

            roleNameTextBox = New TextBox With {
                .Location = New Point(120, 10),
                .Size = New Size(450, 20),
                .BackColor = Color.FromArgb(255, 255, 200),
                .BorderStyle = BorderStyle.FixedSingle
            }
            AddHandler roleNameTextBox.TextChanged, AddressOf RoleNameTextBox_TextChanged
            Me.Controls.Add(roleNameTextBox)

            ' Display Order
            displayOrderLabel = New Label With {
                .Text = "Display Order:",
                .Location = New Point(10, 40),
                .Size = New Size(100, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(displayOrderLabel)

            displayOrderTextBox = New TextBox With {
                .Location = New Point(140, 40),
                .Size = New Size(100, 20),
                .BackColor = Color.FromArgb(255, 255, 200),
                .BorderStyle = BorderStyle.FixedSingle
            }
            NumericTextBoxHelper.ConfigureWholeNumberOnly(displayOrderTextBox)
            AddHandler displayOrderTextBox.TextChanged, AddressOf DisplayOrderTextBox_TextChanged
            Me.Controls.Add(displayOrderTextBox)

            ' Permission checkboxes label
            Dim permissionsLabel As New Label With {
                .Text = "Default Permissions:",
                .Location = New Point(10, 70),
                .Size = New Size(200, 20),
                .Font = New Font("Arial", 10, FontStyle.Bold)
            }
            Me.Controls.Add(permissionsLabel)

            ' Create checkbox controls
            Dim checkboxDefinitions = New (String, String, Integer, Integer)() {
                ("CA_CanChange", "CA Can Change", 10, 100),
                ("Can_Create", "Can Create", 10, 130),
                ("Can_Read", "Can Read", 10, 160),
                ("Can_Update", "Can Update", 10, 190),
                ("Can_Delete", "Can Delete", 10, 220),
                ("Can_Export", "Can Export", 300, 100),
                ("Can_Import", "Can Import", 300, 130),
                ("Can_UseQBE", "Can Use QBE", 300, 160),
                ("Can_ViewAllRecords", "Can View All Records", 300, 190),
                ("Can_ViewOnlyMyRecords", "Can View Only My Records", 300, 220)
            }

            For Each def In checkboxDefinitions
                Dim cb As New CheckBox With {
                    .Text = def.Item2,
                    .Location = New Point(def.Item3, def.Item4),
                    .Size = New Size(270, 20),
                    .Font = New Font("Arial", 10)
                }
                Me.Controls.Add(cb)

                ' Store reference by field name
                Select Case def.Item1
                    Case "CA_CanChange"
                        caCanChangeCheckBox = cb
                    Case "Can_Create"
                        canCreateCheckBox = cb
                    Case "Can_Read"
                        canReadCheckBox = cb
                    Case "Can_Update"
                        canUpdateCheckBox = cb
                    Case "Can_Delete"
                        canDeleteCheckBox = cb
                    Case "Can_Export"
                        canExportCheckBox = cb
                    Case "Can_Import"
                        canImportCheckBox = cb
                    Case "Can_UseQBE"
                        canUseQBECheckBox = cb
                    Case "Can_ViewAllRecords"
                        canViewAllRecordsCheckBox = cb
                    Case "Can_ViewOnlyMyRecords"
                        canViewOnlyMyRecordsCheckBox = cb
                End Select
            Next

            ' OK Button
            okButton = New Button With {
                .Text = "OK",
                .Location = New Point(420, 470),
                .Size = New Size(80, 35)
            }
            AddHandler okButton.Click, AddressOf OkButton_Click
            Me.Controls.Add(okButton)

            ' Cancel Button
            cancelButton = New Button With {
                .Text = "Cancel",
                .Location = New Point(500, 470),
                .Size = New Size(80, 35),
                .DialogResult = DialogResult.Cancel
            }
            AddHandler cancelButton.Click, AddressOf CancelButton_Click
            Me.Controls.Add(cancelButton)
        End Sub

        Private Sub RoleNameTextBox_TextChanged(sender As Object, e As EventArgs)
            If String.IsNullOrWhiteSpace(roleNameTextBox.Text) Then
                roleNameTextBox.BackColor = Color.FromArgb(255, 255, 200)
            Else
                roleNameTextBox.BackColor = Color.White
            End If
        End Sub

        Private Sub DisplayOrderTextBox_TextChanged(sender As Object, e As EventArgs)
            Dim displayOrder As Integer = 0
            If String.IsNullOrWhiteSpace(displayOrderTextBox.Text) OrElse Not Integer.TryParse(displayOrderTextBox.Text, displayOrder) OrElse displayOrder = 0 Then
                displayOrderTextBox.BackColor = Color.FromArgb(255, 255, 200)
            Else
                displayOrderTextBox.BackColor = Color.White
            End If
        End Sub

        Private Sub SaveRoleSettings()
            Try
                Dim newRoleName = roleNameTextBox.Text.Trim()
                Dim newDisplayOrder As Integer = 0

                If Not Integer.TryParse(displayOrderTextBox.Text, newDisplayOrder) Then
                    newDisplayOrder = 0
                End If

                If DataAccess.RoleNameExists(_registrationId, newRoleName, 0) Then
                    MessageBox.Show($"A role named '{newRoleName}' already exists.", "Duplicate Role Name", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                ' Create the new role with permissions
                Dim newRoleId = DataAccess.CreateRoleWithPermissions(
                    _registrationId,
                    newRoleName,
                    newDisplayOrder,
                    caCanChangeCheckBox.Checked,
                    canCreateCheckBox.Checked,
                    canReadCheckBox.Checked,
                    canUpdateCheckBox.Checked,
                    canDeleteCheckBox.Checked,
                    canExportCheckBox.Checked,
                    canImportCheckBox.Checked,
                    canUseQBECheckBox.Checked,
                    canViewAllRecordsCheckBox.Checked,
                    canViewOnlyMyRecordsCheckBox.Checked
                )

                If newRoleId > 0 Then
                    _savedRecordId = newRoleId
                    MessageBox.Show("Role created successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Else
                    MessageBox.Show("Failed to create role.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End If
            Catch ex As Exception
                MessageBox.Show($"Error saving role: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub OkButton_Click(sender As Object, e As EventArgs)
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

            If String.IsNullOrWhiteSpace(roleNameTextBox.Text) Then
                errors.Add("Role Name")
            End If

            Dim displayOrder As Integer = 0
            If String.IsNullOrWhiteSpace(displayOrderTextBox.Text) OrElse Not Integer.TryParse(displayOrderTextBox.Text, displayOrder) OrElse displayOrder = 0 Then
                errors.Add("Display Order (must be a number greater than 0)")
            End If

            Return errors
        End Function

        Private Sub CancelButton_Click(sender As Object, e As EventArgs)
            Me.DialogResult = DialogResult.Cancel
            Me.Close()
        End Sub
    End Class
End Namespace
