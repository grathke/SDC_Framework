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

        Private allowMultipleRolesCheckBox As CheckBox
        Private allowPasswordChangeCheckBox As CheckBox
        Private allowUpdateProfileCheckBox As CheckBox
        Private twoFactorCheckBox As CheckBox
        Private dateFormatComboBox As ComboBox
        Private timeFormatComboBox As ComboBox
        Private timeZoneComboBox As ComboBox
        Private licenseExpirationPicker As DateTimePicker
        Private activeRegistrationId As Integer
        Private currentRecord As RegistrationRecord

        Protected Overrides Function BuildMaintenanceTitle() As String
            Return "Registration Maintenance"
        End Function

        Public Sub New(Optional registrationId As Integer = 0, Optional createNew As Boolean = False)
            Me.ClientSize = New Size(980, 720)
            activeRegistrationId = ResolveInitialRegistrationId(registrationId, createNew)

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
                    ' Asked before the write, and cancelling cancels the save. A registration with
                    ' no roles and nobody in it cannot be signed into.
                    Dim administrator = RegistrationAdminPrompt.Ask(Me, currentRecord.RegName)
                    If administrator Is Nothing Then Return False

                    Dim newId = DataAccess.CreateRegistration(currentRecord, currentUserId, administrator)
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

        ''' <summary>
        ''' Two columns that advance independently.
        '''
        ''' They shared one y until 2026-09-16, which meant removing a field from either column
        ''' left a hole in the other - the Business Rule Type combo came out of the left column
        ''' and the row below it kept its check box, so the left column had a blank row nothing
        ''' explained.
        ''' </summary>
        Private Sub BuildLayout()
            Dim y As Integer = 20
            Dim optionsY As Integer = 20
            Dim rowGap As Integer = 42
            Dim optionsX As Integer = 500

            registrationTextBox = AddField("RegName", y, False, True, labelText:="Registration Name")
            allowMultipleRolesCheckBox = AddOptionCheckBox("CheckBox_AllowMultipleRoles", "Allow Multiple Roles per User", optionsX, optionsY + 2)

            y += rowGap
            optionsY += rowGap
            registrationTypeComboBox = AddComboField("RegistrationTypeID", y, True, labelText:="Registration Type")
            allowPasswordChangeCheckBox = AddOptionCheckBox("CheckBox_AllowPasswordChangeAtLogin", "Allow Password Change At Login", optionsX, optionsY + 2)

            y += rowGap
            optionsY += rowGap
            addressTextBox = AddField("Address1", y, False, False)
            allowUpdateProfileCheckBox = AddOptionCheckBox("CheckBox_AllowUpdateMyProfile", "Allow Update My Profile at Main Menu", optionsX, optionsY + 2)

            y += rowGap
            optionsY += rowGap
            address2TextBox = AddField("Address2", y, False, False)
            ' Allow Update My Email sat here and was removed on 2026-09-14. Nothing read it - the
            ' column existed, the box was ticked, and no page ever asked.
            twoFactorCheckBox = AddOptionCheckBox("CheckBox_TwoFactorAuthentication", "Two-Factor Authentication (2FA)", optionsX, optionsY + 2)

            y += rowGap
            optionsY += rowGap
            cityTextBox = AddField("City", y, False, False)
            dateFormatComboBox = AddComboField("FormatDateID", optionsY, False, optionsX, 290, "Date Format")

            y += rowGap
            optionsY += rowGap
            stateTextBox = AddField("State", y, False, False)
            stateTextBox.Width = 80
            timeFormatComboBox = AddComboField("FormatTimeID", optionsY, False, optionsX, 290, "Time Format")

            y += rowGap
            optionsY += rowGap
            zipTextBox = AddField("Zip", y, False, False)
            zipTextBox.Width = 100
            timeZoneComboBox = AddComboField("TimeZoneID", optionsY, True, optionsX, 290, "Time Zone")

            optionsY += rowGap
            licenseExpirationPicker = AddDateField("LicenseExpiration_Date", optionsY, True, optionsX, True, False, "License Expiration")

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
                                                             optionsX,
                                                             y + 2)
        End Sub

        ''' <summary>
        ''' Fills a format combo, showing each pattern as what it produces.
        '''
        ''' No "make a selection" row: a company always writes dates some way, so there is no such
        ''' thing as not having chosen. Where a registration has never been asked, the framework
        ''' default is selected - which is the format it has been using all along, so the combo
        ''' tells the truth about the page rather than inviting an answer that changes nothing.
        ''' </summary>
        Private Shared Sub FillFormatCombo(combo As ComboBox,
                                           options As DataTable,
                                           selectedId As Integer,
                                           defaultPattern As String)
            If combo Is Nothing OrElse options Is Nothing Then Return

            options.Columns.Add("Choice", GetType(String))
            For Each row As DataRow In options.Rows
                Dim pattern = Convert.ToString(row("FormatPattern"))
                row("Choice") = DisplayFormats.SampleOf(pattern, defaultPattern) & "   -   " & Convert.ToString(row("Description"))
            Next

            combo.DataSource = options
            combo.DisplayMember = "Choice"
            combo.ValueMember = "FormatID"
            ComboWidth.FitToContent(combo, options, "Choice")

            If selectedId > 0 Then
                combo.SelectedValue = selectedId
                If combo.SelectedIndex >= 0 Then Return
            End If

            ' Nothing stored, or stored against a row that has since gone: land on the pattern the
            ' page would have used anyway rather than on whatever happens to be first.
            For index = 0 To options.Rows.Count - 1
                If String.Equals(Convert.ToString(options.Rows(index)("FormatPattern")), defaultPattern, StringComparison.Ordinal) Then
                    combo.SelectedIndex = index
                    Return
                End If
            Next

            If combo.Items.Count > 0 Then combo.SelectedIndex = 0
        End Sub

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

            ComboWidth.FitToContent(registrationTypeComboBox, bindTable, "Display")
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

            allowMultipleRolesCheckBox.Checked = record.AllowMultipleRoles
            allowPasswordChangeCheckBox.Checked = record.AllowPasswordChangeAtLogin
            allowUpdateProfileCheckBox.Checked = record.AllowUpdateMyProfile
            twoFactorCheckBox.Checked = record.TwoFactorAuthentication
            FillFormatCombo(dateFormatComboBox,
                            DataAccess.GetFormatOptions("FW_Format_Date", "FormatDateID"),
                            record.FormatDateID,
                            DisplayFormats.DefaultDatePattern)
            FillFormatCombo(timeFormatComboBox,
                            DataAccess.GetFormatOptions("FW_Format_Time", "FormatTimeID"),
                            record.FormatTimeID,
                            DisplayFormats.DefaultTimePattern)

            ConfigureLookupCombo(timeZoneComboBox,
                                 DataAccess.GetLookupTable("FW_TimeZones", "TimeZoneID", "DisplayName", False, record.TimeZoneID),
                                 "TimeZoneID",
                                 "DisplayName",
                                 record.TimeZoneID)

            ' Unticked means no expiry is stored. The picker cannot hold a null, so the tick is
            ' the value: RefreshDateFieldDisplay blanks the date while it is off.
            licenseExpirationPicker.Checked = record.LicenseExpiration.HasValue
            If record.LicenseExpiration.HasValue Then
                licenseExpirationPicker.Value = record.LicenseExpiration.Value
            End If

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

        ''' <summary>
        ''' The record as the form now describes it.
        '''
        ''' AllowUpdateMyProfileEmail is carried through from the loaded record rather than read
        ''' from a control. Its check box was removed on 2026-09-14 because nothing anywhere acts
        ''' on the setting, and writing False here instead would quietly change stored data to
        ''' suit a screen that no longer asks the question.
        '''
        ''' Ribbonbar_InvisibleIcons was carried the same way until 2026-09-15, when the column was
        ''' dropped from FW_Registration along with ImportantDOB, ImportantDaysFuture,
        ''' ImportantDaysPast, ExternalUnique, Ribbonbar_Main and Ribbonbar_Examples. Carrying a
        ''' value through is for a column that still exists and is not on screen; once the column
        ''' is gone the property has to go too, or every read and write names a column the table
        ''' does not have.
        '''
        ''' HomeGraphic is carried through as well - no control asks for it yet, and writing empty
        ''' would blank the registration's picture on every save.
        ''' </summary>
        Private Function BuildRecordFromForm() As RegistrationRecord
            Dim regTypeId = 0
            If registrationTypeComboBox.SelectedValue IsNot Nothing Then
                Integer.TryParse(registrationTypeComboBox.SelectedValue.ToString(), regTypeId)
            End If

            Dim recordId = activeRegistrationId
            If currentRecord IsNot Nothing AndAlso currentRecord.ID > 0 Then
                recordId = currentRecord.ID
            End If

            Return New RegistrationRecord With {
                .ID = recordId,
                .RegName = registrationTextBox.Text.Trim(),
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
                .AllowMultipleRoles = allowMultipleRolesCheckBox.Checked,
                .AllowPasswordChangeAtLogin = allowPasswordChangeCheckBox.Checked,
                .AllowUpdateMyProfile = allowUpdateProfileCheckBox.Checked,
                .AllowUpdateMyProfileEmail = If(currentRecord Is Nothing, False, currentRecord.AllowUpdateMyProfileEmail),
                .HomeGraphic = If(currentRecord Is Nothing, String.Empty, currentRecord.HomeGraphic),
                .TwoFactorAuthentication = twoFactorCheckBox.Checked,
                .FormatDateID = GetComboSelectedIdOrZero(dateFormatComboBox),
                .FormatTimeID = GetComboSelectedIdOrZero(timeFormatComboBox),
                .TimeZoneID = GetComboSelectedIdOrZero(timeZoneComboBox),
                .LicenseExpiration = If(licenseExpirationPicker.Checked, CType(licenseExpirationPicker.Value.Date, Date?), Nothing),
                .IsActive = True,
                .RowVersion = CopyOriginalRowVersion()
            }
        End Function

        Private Shared Function BuildDefaultRecord() As RegistrationRecord
            Return New RegistrationRecord With {
                .ID = 0,
                .RegName = String.Empty,
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
                .AllowMultipleRoles = True,
                .AllowPasswordChangeAtLogin = True,
                .AllowUpdateMyProfile = True,
                .AllowUpdateMyProfileEmail = True,
                .TwoFactorAuthentication = False,
                .IsActive = True
            }
        End Function

        Private Shared Function SafeText(value As String) As String
            Return If(value, String.Empty)
        End Function

        ''' <summary>
        ''' Which registration the page opens on.
        '''
        ''' The fallback to the session's is for the callers that mean "mine" - the standalone
        ''' entry point and the page picker. Create must not take it, or Add silently edits the
        ''' registration the user signed in under.
        ''' </summary>
        Private Shared Function ResolveInitialRegistrationId(requestedRegistrationId As Integer,
                                                             createNew As Boolean) As Integer
            If createNew Then Return 0

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
