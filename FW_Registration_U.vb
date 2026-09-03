Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class FW_Registration_U
        Inherits FW_Base_U

        Private registrationTextBox As TextBox
        Private businessRuleTypeComboBox As ComboBox
        Private registrationTypeComboBox As ComboBox
        Private addressTextBox As TextBox
        Private address2TextBox As TextBox
        Private cityTextBox As TextBox
        Private stateTextBox As TextBox
        Private zipTextBox As TextBox
        Private mainFaxTextBox As TextBox
        Private mainPhoneTextBox As TextBox
        Private emailTextBox As TextBox
        Private webLandingPageTextBox As TextBox
        Private smartyAuthIdTextBox As TextBox
        Private smartyAuthTokenTextBox As TextBox
        Private smartyEmbeddedKeyTextBox As TextBox
        Private smartyUseEmbeddedKeyCheckBox As CheckBox
        Private smartyAddressLookupController As SmartyAddressLookupController
        Private zipCoderController As ZipCoderController

        Private displayDashboardCheckBox As CheckBox
        Private allowMessagingCheckBox As CheckBox
        Private allowMultipleRolesCheckBox As CheckBox
        Private allowPasswordChangeCheckBox As CheckBox
        Private allowUpdateProfileCheckBox As CheckBox
        Private allowUpdateEmailCheckBox As CheckBox
        Private twoFactorCheckBox As CheckBox
        Private activeRegistrationId As Integer
        Private currentRecord As RegistrationRecord

        Protected Overrides Function BuildMaintenanceTitle() As String
            Return "Registration Maintenance"
        End Function

        Public Sub New(Optional registrationId As Integer = 0)
            Me.ClientSize = New Size(980, 720)
            activeRegistrationId = ResolveInitialRegistrationId(registrationId)

            BuildLayout()

            okButton.Location = New Point(700, 660)
            cancelActionButton.Location = New Point(835, 660)

            ApplyMode()
            BindToForm()
            smartyAddressLookupController = New SmartyAddressLookupController(
                Me,
                addressTextBox,
                cityTextBox,
                stateTextBox,
                zipTextBox,
                Function() SmartyAddressLookupController.IsSessionLookupEnabled(),
                Function() SmartyAddressLookupController.GetSessionEmbeddedKey())
            zipCoderController = New ZipCoderController(Me, cityTextBox, stateTextBox, zipTextBox)
        End Sub

        Protected Overrides Function OkButtonText() As String
            Return "Save"
        End Function

        Protected Overrides Function GetPageName() As String
            Return "FW_Registration_U"
        End Function

        Protected Overrides Function GetTableNameOverride() As String
            Return "FW_Registration"
        End Function

        Protected Overrides Sub BindToFormInternal()
            BindBusinessRuleTypes()
            BindRegistrationTypes()
            stateTextBox.MaxLength = 2
            zipTextBox.MaxLength = 10

            If activeRegistrationId > 0 Then
                currentRecord = DataAccess.GetRegistrationById(activeRegistrationId)
            End If

            If currentRecord Is Nothing Then
                currentRecord = BuildDefaultRecord()
            End If

            CaptureOriginalRowVersion(currentRecord.RowVersion)

            ApplyRecordToForm(currentRecord)
        End Sub

        Protected Overrides Sub ApplyMode()
            registrationTextBox.ReadOnly = False
            registrationTypeComboBox.Enabled = True
            addressTextBox.ReadOnly = False
            address2TextBox.ReadOnly = False
            cityTextBox.ReadOnly = False
            stateTextBox.ReadOnly = False
            zipTextBox.ReadOnly = False
            mainFaxTextBox.ReadOnly = False
            mainPhoneTextBox.ReadOnly = False
            emailTextBox.ReadOnly = False
            webLandingPageTextBox.ReadOnly = False
        End Sub

        Protected Overrides Function TryBuildRecord() As Boolean
            If businessRuleTypeComboBox.SelectedValue Is Nothing OrElse String.IsNullOrWhiteSpace(businessRuleTypeComboBox.SelectedValue.ToString()) Then
                MessageBox.Show("Business Rule Type is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                businessRuleTypeComboBox.Focus()
                Return False
            End If

            currentRecord = BuildRecordFromForm()
            Return True
        End Function

        Protected Overrides Function SaveRecord() As Boolean
            If currentRecord Is Nothing Then
                currentRecord = BuildRecordFromForm()
            End If

            Dim currentUserId = 0
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                currentUserId = SessionState.Current.Value.UserID
            End If

            Try
                If currentRecord.ID > 0 Then
                    Dim saveResult = DataAccess.UpdateRegistration(currentRecord, currentUserId)
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

                        Dim latestRecord = DataAccess.GetRegistrationById(currentRecord.ID)
                        If latestRecord Is Nothing OrElse latestRecord.RowVersion Is Nothing Then
                            MessageBox.Show(Me, "The record no longer exists.", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            Return False
                        End If

                        currentRecord.RowVersion = latestRecord.RowVersion
                        saveResult = DataAccess.UpdateRegistration(currentRecord, currentUserId)
                    End If

                    If saveResult <> SaveResult.Succeeded Then
                        MessageBox.Show(Me, "The registration could not be saved.", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return False
                    End If
                    activeRegistrationId = currentRecord.ID
                Else
                    Dim newId = DataAccess.CreateRegistration(currentRecord, currentUserId)
                    currentRecord.ID = newId
                    activeRegistrationId = newId
                End If

                Return True
            Catch ex As Exception
                MessageBox.Show("Error saving registration: " & ex.Message, "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End Try
        End Function

        Protected Overrides Function ResolveAuditRecordKey() As String
            If currentRecord IsNot Nothing AndAlso currentRecord.ID > 0 Then
                Return currentRecord.ID.ToString(CultureInfo.InvariantCulture)
            End If

            If activeRegistrationId > 0 Then
                Return activeRegistrationId.ToString(CultureInfo.InvariantCulture)
            End If

            Return String.Empty
        End Function

        Protected Overrides Function ResolveAuditOperationType() As String
            If currentRecord IsNot Nothing AndAlso currentRecord.ID > 0 Then
                Return "Modify"
            End If

            If activeRegistrationId > 0 Then
                Return "Modify"
            End If

            Return "Create"
        End Function

        Private Sub BuildLayout()
            Dim y As Integer = 20
            Dim rowGap As Integer = 42
            Dim optionsX As Integer = 500

            registrationTextBox = AddField("RegName", y, False, False)
            SetFieldLabelText("RegName", "Registration")
            displayDashboardCheckBox = AddOptionCheckBox("CheckBox_DisplayDashboardOnStartup", "Display Dashboard on Startup", optionsX, y + 2)
            allowMessagingCheckBox = AddOptionCheckBox("CheckBox_AllowMessaging", "Allow Messaging", optionsX + 250, y + 2)

            y += rowGap
            businessRuleTypeComboBox = AddLabeledComboBoxSharedStyle("BusinessRuleType", "Business Rule Type", y)
            allowMultipleRolesCheckBox = AddOptionCheckBox("CheckBox_AllowMultipleRoles", "Allow Multiple Roles per User", optionsX, y + 2)

            y += rowGap
            registrationTypeComboBox = AddLabeledComboBoxSharedStyle("RegistrationTypeID", "Registration Type", y)
            allowPasswordChangeCheckBox = AddOptionCheckBox("CheckBox_AllowPasswordChangeAtLogin", "Allow Password Change At Login", optionsX, y + 2)

            y += rowGap
            addressTextBox = AddField("Address1", y, False, False)
            allowUpdateProfileCheckBox = AddOptionCheckBox("CheckBox_AllowUpdateMyProfile", "Allow Update My Profile at Main Menu", optionsX, y + 2)

            y += rowGap
            address2TextBox = AddField("Address2", y, False, False)
            allowUpdateEmailCheckBox = AddOptionCheckBox("CheckBox_AllowUpdateMyProfileEmail", "Allow Update My Email", optionsX, y + 2)

            y += rowGap
            cityTextBox = AddField("City", y, False, False)
            y += rowGap
            stateTextBox = AddField("State", y, False, False)
            stateTextBox.Width = 80
            twoFactorCheckBox = AddOptionCheckBox("CheckBox_TwoFactorAuthentication", "Two-Factor Authentication (2FA)", optionsX, y + 2)

            y += rowGap
            zipTextBox = AddField("Zip", y, False, False)
            zipTextBox.Width = 100

            y += rowGap
            mainFaxTextBox = AddField("MainFax", y, False, False)
            mainFaxTextBox.Width = 180

            y += rowGap
            mainPhoneTextBox = AddField("MainPhone", y, False, False)
            mainPhoneTextBox.Width = 180

            y += rowGap
            emailTextBox = AddField("MainEMail", y, False, False)
            SetFieldLabelText("MainEMail", "Email")
            emailTextBox.Width = 240

            y += rowGap
            webLandingPageTextBox = AddField("WebLandingPage", y, False, False)
            y += rowGap
            smartyAuthIdTextBox = AddField("Smarty_AuthID", y, False, False)
            SetFieldLabelText("Smarty_AuthID", "Smarty Auth ID")

            y += rowGap
            smartyAuthTokenTextBox = AddField("Smarty_AuthToken", y, False, False)
            SetFieldLabelText("Smarty_AuthToken", "Smarty Auth Token")

            y += rowGap
            smartyEmbeddedKeyTextBox = AddField("Smarty_EmbeddedKey", y, False, False)
            SetFieldLabelText("Smarty_EmbeddedKey", "Smarty Embedded Key")

            smartyUseEmbeddedKeyCheckBox = AddOptionCheckBox("CheckBox_Smarty_UseEmbeddedKey",
                                                             "Use Smarty Embedded Key",
                                                             500,
                                                             y + 2)
        End Sub

        Private Function AddLabeledComboBoxSharedStyle(fieldName As String,
                                                       labelText As String,
                                                       y As Integer) As ComboBox
            Dim lbl As New Label() With {
                .Name = "Label_" & fieldName,
                .Text = labelText,
                .Location = New Point(20, y),
                .Size = New Size(120, 26),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Me.Controls.Add(lbl)

            Dim combo As New ComboBox() With {
                .Name = "ComboBox_" & fieldName,
                .Location = New Point(150, y),
                .Size = New Size(320, 26),
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .BackColor = SystemColors.Window
            }
            Me.Controls.Add(combo)
            Return combo
        End Function

        Private Function AddOptionCheckBox(name As String,
                                           text As String,
                                           x As Integer,
                                           y As Integer) As CheckBox
            Dim chk As New CheckBox() With {
                .Name = name,
                .Text = text,
                .Location = New Point(x, y),
                .AutoCheck = True,
                .AutoSize = True,
                .BackColor = SystemColors.Control
            }
            Me.Controls.Add(chk)
            Return chk
        End Function

        Private Sub SetFieldLabelText(fieldName As String, caption As String)
            Dim labelName = "Label_" & fieldName
            Dim matches = Me.Controls.Find(labelName, True)
            If matches Is Nothing OrElse matches.Length = 0 Then
                Return
            End If

            matches(0).Text = caption
        End Sub

        Private Sub BindRegistrationTypes()
            Dim source = DataAccess.GetRegistrationTypes()
            Dim bindTable As New DataTable("RegistrationTypes")
            bindTable.Columns.Add("Value", GetType(Integer))
            bindTable.Columns.Add("Display", GetType(String))

            bindTable.Rows.Add(0, "Make a Selection")

            If source IsNot Nothing AndAlso source.Columns.Count > 0 Then
                Dim valueColumn = ResolveRegistrationTypeValueColumn(source)
                Dim displayColumn = ResolveRegistrationTypeDisplayColumn(source)

                For Each row As DataRow In source.Rows
                    Dim value = 0
                    If valueColumn IsNot Nothing AndAlso Not row.IsNull(valueColumn) Then
                        Integer.TryParse(row(valueColumn).ToString(), value)
                    End If

                    Dim displayText As String = String.Empty
                    If displayColumn IsNot Nothing AndAlso Not row.IsNull(displayColumn) Then
                        displayText = row(displayColumn).ToString().Trim()
                    End If

                    If String.IsNullOrWhiteSpace(displayText) Then
                        Continue For
                    End If

                    bindTable.Rows.Add(value, displayText)
                Next
            End If

            registrationTypeComboBox.DataSource = bindTable
            registrationTypeComboBox.DisplayMember = "Display"
            registrationTypeComboBox.ValueMember = "Value"
            registrationTypeComboBox.SelectedIndex = 0
        End Sub

        Private Sub BindBusinessRuleTypes()
            Dim bindTable As New DataTable("BusinessRuleTypes")
            bindTable.Columns.Add("Value", GetType(String))
            bindTable.Columns.Add("Display", GetType(String))

            bindTable.Rows.Add(String.Empty, "Make a Selection")
            bindTable.Rows.Add(BR_RegIDBased, "Registration-Based (Legacy)")
            bindTable.Rows.Add(BR_RoleBased, "Role-Based")

            businessRuleTypeComboBox.DataSource = bindTable
            businessRuleTypeComboBox.DisplayMember = "Display"
            businessRuleTypeComboBox.ValueMember = "Value"
            businessRuleTypeComboBox.SelectedIndex = 0
        End Sub

        Private Sub ApplyRecordToForm(record As RegistrationRecord)
            registrationTextBox.Text = SafeText(record.RegName)
            addressTextBox.Text = SafeText(record.Address1)
            address2TextBox.Text = SafeText(record.Address2)
            cityTextBox.Text = SafeText(record.City)
            stateTextBox.Text = SafeText(record.State)
            zipTextBox.Text = SafeText(record.Zip)
            mainFaxTextBox.Text = SafeText(record.MainFax)
            mainPhoneTextBox.Text = SafeText(record.MainPhone)
            emailTextBox.Text = SafeText(record.MainEMail)
            webLandingPageTextBox.Text = SafeText(record.WebLandingPage)
            smartyAuthIdTextBox.Text = SafeText(record.Smarty_AuthID)
            smartyAuthTokenTextBox.Text = SafeText(record.Smarty_AuthToken)
            smartyEmbeddedKeyTextBox.Text = SafeText(record.Smarty_EmbeddedKey)
            smartyUseEmbeddedKeyCheckBox.Checked = record.Smarty_UseEmbeddedKey

            Dim normalizedBrType = NormalizeBusinessRuleType(record.BusinessRuleType)
            If String.IsNullOrWhiteSpace(record.BusinessRuleType) Then
                businessRuleTypeComboBox.SelectedIndex = 0
            Else
                businessRuleTypeComboBox.SelectedValue = normalizedBrType
            End If

            If businessRuleTypeComboBox.SelectedIndex < 0 Then
                businessRuleTypeComboBox.SelectedIndex = 0
            End If

            displayDashboardCheckBox.Checked = record.DisplayDashboardOnStartUp
            allowMessagingCheckBox.Checked = record.AllowMessaging
            allowMultipleRolesCheckBox.Checked = record.AllowMultipleRoles
            allowPasswordChangeCheckBox.Checked = record.AllowPasswordChangeAtLogin
            allowUpdateProfileCheckBox.Checked = record.AllowUpdateMyProfile
            allowUpdateEmailCheckBox.Checked = record.AllowUpdateMyProfileEmail
            twoFactorCheckBox.Checked = record.TwoFactorAuthentication

            smartyAuthIdTextBox.DataBindings.Clear()
            smartyAuthIdTextBox.DataBindings.Add("Text", record, "Smarty_AuthID", True)
            smartyAuthTokenTextBox.DataBindings.Clear()
            smartyAuthTokenTextBox.DataBindings.Add("Text", record, "Smarty_AuthToken", True)
            smartyEmbeddedKeyTextBox.DataBindings.Clear()
            smartyEmbeddedKeyTextBox.DataBindings.Add("Text", record, "Smarty_EmbeddedKey", True)
            smartyUseEmbeddedKeyCheckBox.DataBindings.Clear()
            smartyUseEmbeddedKeyCheckBox.DataBindings.Add("Checked", record, "Smarty_UseEmbeddedKey", True)

            If record.RegistrationTypeID > 0 Then
                registrationTypeComboBox.SelectedValue = record.RegistrationTypeID
                If registrationTypeComboBox.SelectedIndex < 0 Then
                    registrationTypeComboBox.SelectedIndex = 0
                End If
            Else
                registrationTypeComboBox.SelectedIndex = 0
            End If
        End Sub

        Private Function BuildRecordFromForm() As RegistrationRecord
            Dim regTypeId = 0
            If registrationTypeComboBox.SelectedValue IsNot Nothing Then
                Integer.TryParse(registrationTypeComboBox.SelectedValue.ToString(), regTypeId)
            End If

            Dim businessRuleType = String.Empty
            If businessRuleTypeComboBox.SelectedValue IsNot Nothing Then
                Dim selectedBrType = businessRuleTypeComboBox.SelectedValue.ToString().Trim()
                If selectedBrType <> String.Empty Then
                    businessRuleType = NormalizeBusinessRuleType(selectedBrType)
                End If
            End If

            Dim recordId = activeRegistrationId
            If currentRecord IsNot Nothing AndAlso currentRecord.ID > 0 Then
                recordId = currentRecord.ID
            End If

            Return New RegistrationRecord With {
                .ID = recordId,
                .RegName = registrationTextBox.Text.Trim(),
                .BusinessRuleType = businessRuleType,
                .RegistrationTypeID = regTypeId,
                .Address1 = addressTextBox.Text.Trim(),
                .Address2 = address2TextBox.Text.Trim(),
                .City = cityTextBox.Text.Trim(),
                .State = stateTextBox.Text.Trim(),
                .Zip = zipTextBox.Text.Trim(),
                .MainFax = mainFaxTextBox.Text.Trim(),
                .MainPhone = mainPhoneTextBox.Text.Trim(),
                .MainEMail = emailTextBox.Text.Trim(),
                .WebLandingPage = webLandingPageTextBox.Text.Trim(),
                .Smarty_AuthID = smartyAuthIdTextBox.Text.Trim(),
                .Smarty_AuthToken = smartyAuthTokenTextBox.Text.Trim(),
                .Smarty_EmbeddedKey = smartyEmbeddedKeyTextBox.Text.Trim(),
                .Smarty_UseEmbeddedKey = smartyUseEmbeddedKeyCheckBox.Checked,
                .DisplayDashboardOnStartUp = displayDashboardCheckBox.Checked,
                .AllowMessaging = allowMessagingCheckBox.Checked,
                .AllowMultipleRoles = allowMultipleRolesCheckBox.Checked,
                .AllowPasswordChangeAtLogin = allowPasswordChangeCheckBox.Checked,
                .AllowUpdateMyProfile = allowUpdateProfileCheckBox.Checked,
                .AllowUpdateMyProfileEmail = allowUpdateEmailCheckBox.Checked,
                .Ribbonbar_InvisibleIcons = If(currentRecord Is Nothing, False, currentRecord.Ribbonbar_InvisibleIcons),
                .TwoFactorAuthentication = twoFactorCheckBox.Checked,
                .IsActive = True,
                .RowVersion = CopyOriginalRowVersion()
            }
        End Function

        Private Shared Function BuildDefaultRecord() As RegistrationRecord
            Return New RegistrationRecord With {
                .ID = 0,
                .RegName = String.Empty,
                .BusinessRuleType = String.Empty,
                .RegistrationTypeID = 0,
                .Address1 = String.Empty,
                .Address2 = String.Empty,
                .City = String.Empty,
                .State = String.Empty,
                .Zip = String.Empty,
                .MainFax = String.Empty,
                .MainPhone = String.Empty,
                .MainEMail = String.Empty,
                .WebLandingPage = String.Empty,
                .Smarty_AuthID = String.Empty,
                .Smarty_AuthToken = String.Empty,
                .Smarty_EmbeddedKey = String.Empty,
                .Smarty_UseEmbeddedKey = False,
                .DisplayDashboardOnStartUp = True,
                .AllowMessaging = True,
                .AllowMultipleRoles = True,
                .AllowPasswordChangeAtLogin = True,
                .AllowUpdateMyProfile = True,
                .AllowUpdateMyProfileEmail = True,
                .Ribbonbar_InvisibleIcons = True,
                .TwoFactorAuthentication = False,
                .IsActive = True
            }
        End Function

        Private Shared Function SafeText(value As String) As String
            Return If(value, String.Empty)
        End Function

        Private Shared Function ResolveInitialRegistrationId(requestedRegistrationId As Integer) As Integer
            If requestedRegistrationId > 0 Then
                Return requestedRegistrationId
            End If

            If SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso SessionState.Current.Value.RegistrationID > 0 Then
                Return SessionState.Current.Value.RegistrationID
            End If

            Return 0
        End Function

        Private Shared Function ResolveRegistrationTypeValueColumn(source As DataTable) As DataColumn
            If source.Columns.Contains("ID") Then
                Return source.Columns("ID")
            End If

            For Each col As DataColumn In source.Columns
                If col.ColumnName.EndsWith("ID", StringComparison.OrdinalIgnoreCase) Then
                    Return col
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function ResolveRegistrationTypeDisplayColumn(source As DataTable) As DataColumn
            Dim candidates = New String() {"RegistrationType", "TypeDescription", "Description", "Name"}
            For Each candidate In candidates
                If source.Columns.Contains(candidate) Then
                    Return source.Columns(candidate)
                End If
            Next

            For Each col As DataColumn In source.Columns
                If col.DataType Is GetType(String) Then
                    Return col
                End If
            Next

            Return Nothing
        End Function
    End Class
End Namespace
