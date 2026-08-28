Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class EntityY_U
        Inherits FW_Base_U

        Private ReadOnly recordId As Integer
        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly tableName As String = "FW_ENTITY"
        Private ReadOnly primaryKey As String = "ID"
        Private record As DataRow
        Private ReadOnly formBindingSource As New BindingSource()
        Private originalRowVersion As Byte()
        Private ReadOnly firstNameTextBox As TextBox
        Private ReadOnly lastNameTextBox As TextBox
        Private ReadOnly address1TextBox As TextBox
        Private ReadOnly cityTextBox As TextBox
        Private ReadOnly stateTextBox As TextBox
        Private ReadOnly zipCodeTextBox As TextBox

        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New()
            recordId = id
            currentUser = user
            accessProfile = profile
            Text = "EntityY_U"
            ClientSize = New Size(600, 307)
            okButton.Location = New Point(ClientSize.Width - 270, ClientSize.Height - 46)
            cancelActionButton.Location = New Point(ClientSize.Width - 135, ClientSize.Height - 46)
            enumButton.Location = New Point(20, ClientSize.Height - 46)
            firstNameTextBox = AddField("FirstName", 20, False, True)
            lastNameTextBox = AddField("LastName", 62, False, True)
            address1TextBox = AddField("Address1", 104, False, False)
            cityTextBox = AddField("City", 146, False, False)
            stateTextBox = AddField("State", 188, False, False)
            zipCodeTextBox = AddField("ZipCode", 230, False, False)
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
            Return NameOf(EntityY_U)
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

        Protected Overrides Function SaveRecord() As Boolean
            Dim values As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
            values("FirstName") = firstNameTextBox.Text
            values("LastName") = lastNameTextBox.Text
            values("Address1") = address1TextBox.Text
            values("City") = cityTextBox.Text
            values("State") = stateTextBox.Text
            values("ZipCode") = zipCodeTextBox.Text
            Dim savedId As Integer = recordId
            If savedId <= 0 AndAlso record.Table.Columns.Contains(primaryKey) AndAlso Not record.IsNull(primaryKey) Then Integer.TryParse(Convert.ToString(record(primaryKey)), savedId)
            Dim savedRecordId = DataAccess.SaveGeneratedPageRecordWithId(tableName, primaryKey, savedId, values, originalRowVersion, If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))
            If savedRecordId <= 0 OrElse record Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) Then Return False
            record(primaryKey) = savedRecordId
            Return True
        End Function

        Protected Overrides Function ResolveAuditRecordKey() As String
            Return If(record Is Nothing OrElse record.Table Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) OrElse record.IsNull(primaryKey), String.Empty, Convert.ToString(record(primaryKey)))
        End Function
    End Class
End Namespace
