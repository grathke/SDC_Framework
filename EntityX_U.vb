Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class EntityX_U
        Inherits FW_Base_U

        Private ReadOnly recordId As Integer
        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly tableName As String = "FW_ENTITY"
        Private ReadOnly primaryKey As String = "EntityID"
        Private record As DataRow
        Private ReadOnly formBindingSource As New BindingSource()
        Private originalRowVersion As Byte()
        Private ReadOnly firstNameTextBox As TextBox
        Private ReadOnly lastNameTextBox As TextBox
        Private ReadOnly address1TextBox As TextBox
        Private ReadOnly cityTextBox As TextBox
        Private ReadOnly stateTextBox As TextBox
        Private ReadOnly zipCodeTextBox As TextBox
        Private ReadOnly genderIDComboBox As ComboBox
        Private ReadOnly assignedManagerIDComboBox As ComboBox

        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New()
            recordId = id
            currentUser = user
            accessProfile = profile
            ClientSize = New Size(600, 391)
            okButton.Location = New Point(ClientSize.Width - 270, ClientSize.Height - 46)
            cancelActionButton.Location = New Point(ClientSize.Width - 135, ClientSize.Height - 46)
            firstNameTextBox = AddField("FirstName", 20, False, False)
            lastNameTextBox = AddField("LastName", 62, False, True)
            address1TextBox = AddField("Address1", 104, False, False)
            cityTextBox = AddField("City", 146, False, False)
            stateTextBox = AddField("State", 188, False, False)
            zipCodeTextBox = AddField("ZipCode", 230, False, False)
            genderIDComboBox = AddComboField("GenderID", 272, False, 20, 320)
            assignedManagerIDComboBox = AddComboField("AssignedManagerID", 314, False, 20, 320)
            SetManualTabOrder(firstNameTextBox, lastNameTextBox, address1TextBox, cityTextBox, stateTextBox, zipCodeTextBox, genderIDComboBox, assignedManagerIDComboBox, okButton, cancelActionButton)
            BindToForm()
            ApplyMode()
        End Sub

        Public ReadOnly Property SavedRecordId As Integer
            Get
                If record Is Nothing OrElse record.Table Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) OrElse record.IsNull(primaryKey) Then Return 0
                Return Convert.ToInt32(record(primaryKey), Globalization.CultureInfo.InvariantCulture)
            End Get
        End Property

        Protected Overrides Function GetPageName() As String
            Return NameOf(EntityX_U)
        End Function

        Protected Overrides Function GetTableNameOverride() As String
            Return tableName
        End Function

        Protected Overrides Sub BindToFormInternal()
            Dim loaded = DataAccess.LoadGeneratedPageRecord(tableName, primaryKey, recordId)
            If loaded IsNot Nothing Then
                record = loaded
            Else
                Dim schema = DataAccess.GetGeneratedPageSchema(tableName)
                record = schema.NewRow()
                schema.Rows.Add(record)
            End If
            formBindingSource.DataSource = record.Table
            formBindingSource.Position = record.Table.Rows.IndexOf(record)
            For Each control In New Control() {firstNameTextBox, lastNameTextBox, address1TextBox, cityTextBox, stateTextBox, zipCodeTextBox}
                Dim fieldName = control.Name.Substring("TextBox_".Length)
                control.DataBindings.Clear()
                control.DataBindings.Add("Text", formBindingSource, fieldName, True, DataSourceUpdateMode.Never)
                If record.Table.Columns.Contains(fieldName) Then control.Text = If(record(fieldName) Is DBNull.Value, String.Empty, Convert.ToString(record(fieldName)))
            Next
            ConfigureLookupCombo(genderIDComboBox, DataAccess.GetLookupTable("FW_Gender", "GenderID", "GenderDescription", True), "GenderID", "GenderDescription", CurrentLookupId("GenderID"))
            ConfigureLookupCombo(assignedManagerIDComboBox, DataAccess.GetLookupTable("FW_USERS", "UserId", "FirstLast", True), "UserId", "FirstLast", CurrentLookupId("AssignedManagerID"))
            If record.Table.Columns.Contains("RowVersion") AndAlso Not record.IsNull("RowVersion") Then originalRowVersion = CType(DirectCast(record("RowVersion"), Byte()).Clone(), Byte())
            CaptureOriginalRowVersion(originalRowVersion)
        End Sub

        Protected Overrides Sub ApplyMode()
            For Each control In Controls.OfType(Of TextBox)()
                control.ReadOnly = String.Equals(control.Name, "TextBox_" & primaryKey, StringComparison.OrdinalIgnoreCase)
            Next
        End Sub

        Protected Overrides Function TryBuildRecord() As Boolean
            Return True
        End Function

        ''' <summary>Warns before discarding edits. Without this Cancel would discard silently.</summary>
        Protected Overrides Function ShouldWarnOnCancel() As Boolean
            Return True
        End Function

        ''' <summary>
        ''' Field-level permissions choose Can_Create over Can_Update from this. Without it
        ''' every new record would be evaluated as an update and Can_Create would never apply.
        ''' </summary>
        Protected Overrides Function IsCreatingNewRecord() As Boolean
            Return recordId <= 0
        End Function

        Private Function CurrentLookupId(fieldName As String) As Integer
            If record Is Nothing OrElse record.Table Is Nothing OrElse Not record.Table.Columns.Contains(fieldName) OrElse record.IsNull(fieldName) Then Return 0
            Dim value As Integer
            Return If(Integer.TryParse(Convert.ToString(record(fieldName)), value), value, 0)
        End Function

        Protected Overrides Function SaveRecord() As Boolean
            Dim values As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
            values("FirstName") = firstNameTextBox.Text
            values("LastName") = lastNameTextBox.Text
            values("Address1") = address1TextBox.Text
            values("City") = cityTextBox.Text
            values("State") = stateTextBox.Text
            values("ZipCode") = zipCodeTextBox.Text
            values("GenderID") = GetComboSelectedIdOrNull(genderIDComboBox)
            values("AssignedManagerID") = GetComboSelectedIdOrNull(assignedManagerIDComboBox)
            Dim savedId As Integer = recordId
            If savedId <= 0 AndAlso record.Table.Columns.Contains(primaryKey) AndAlso Not record.IsNull(primaryKey) Then Integer.TryParse(Convert.ToString(record(primaryKey)), savedId)
            Dim updatedBy = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)

            Dim outcome As SaveResult
            Dim savedRecordId = DataAccess.TrySaveGeneratedPageRecord(tableName, primaryKey, savedId, values, originalRowVersion, updatedBy, outcome)

            If outcome = SaveResult.RecordDeleted Then
                MessageBox.Show(Me, "The record no longer exists.", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If

            ' A conflict is the user's decision, never last-saved-wins. Declining keeps the
            ' page open with the edits intact.
            If outcome = SaveResult.RecordChanged Then
                If Not ConfirmConcurrencyOverwrite() Then Return False

                Dim latest = DataAccess.LoadGeneratedPageRecord(tableName, primaryKey, savedId)
                If latest Is Nothing Then
                    MessageBox.Show(Me, "The record no longer exists.", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return False
                End If

                originalRowVersion = If(latest.Table.Columns.Contains("RowVersion") AndAlso Not latest.IsNull("RowVersion"),
                                        CType(DirectCast(latest("RowVersion"), Byte()).Clone(), Byte()), Nothing)
                CaptureOriginalRowVersion(originalRowVersion)
                savedRecordId = DataAccess.TrySaveGeneratedPageRecord(tableName, primaryKey, savedId, values, originalRowVersion, updatedBy, outcome)
            End If

            If outcome <> SaveResult.Succeeded OrElse savedRecordId <= 0 Then Return False
            If record Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) Then Return False
            record(primaryKey) = savedRecordId
            Return True
        End Function

        Protected Overrides Function ResolveAuditRecordKey() As String
            Return If(record Is Nothing OrElse record.Table Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) OrElse record.IsNull(primaryKey), String.Empty, Convert.ToString(record(primaryKey)))
        End Function
    End Class
End Namespace
