Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Globalization
Imports System.Windows.Forms

Namespace HelloWorld
    Public NotInheritable Class RegistrationComboHelper
        Private Const ForcedRegistrationTypeForTesting As String = "Branch"

        Private Sub New()
        End Sub

        Public Shared Sub Populate(combo As ComboBox,
                                   selectedRegistrationId As Integer,
                                   Optional includePlaceholder As Boolean = True,
                                   Optional placeholderText As String = "Make a Selection")
            If combo Is Nothing Then
                Return
            End If

            Dim registrations = DataAccess.GetAllRegistrations()
            Dim hasSelectedRegistration = False
            If selectedRegistrationId > 0 Then
                For Each row As DataRow In registrations.Rows
                    Dim rowId As Integer = 0
                    If Integer.TryParse(Convert.ToString(row("ID"), CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, rowId) AndAlso
                       rowId = selectedRegistrationId Then
                        hasSelectedRegistration = True
                        Exit For
                    End If
                Next

                If Not hasSelectedRegistration Then
                    Dim selectedRow = registrations.NewRow()
                    selectedRow("ID") = selectedRegistrationId
                    selectedRow("RegName") = "Registration " & selectedRegistrationId.ToString(CultureInfo.InvariantCulture)
                    If registrations.Columns.Contains("RegistrationType") Then
                        selectedRow("RegistrationType") = String.Empty
                    End If
                    registrations.Rows.Add(selectedRow)
                End If
            End If

            If includePlaceholder Then
                Dim placeholder = registrations.NewRow()
                placeholder("ID") = 0
                placeholder("RegName") = placeholderText
                registrations.Rows.InsertAt(placeholder, 0)
            End If

            combo.DataSource = Nothing
            combo.DisplayMember = "RegName"
            combo.ValueMember = "ID"
            combo.DataSource = registrations

            If selectedRegistrationId > 0 Then
                combo.SelectedValue = selectedRegistrationId
            ElseIf includePlaceholder Then
                combo.SelectedIndex = 0
            End If
        End Sub

        Public Shared Sub UpdateLabelForSelection(label As Label,
                                                   combo As ComboBox,
                                                   Optional fallbackText As String = "Registration")
            If label Is Nothing Then
                Return
            End If

            Dim registrationType = String.Empty
            Dim selectedRow = TryCast(combo?.SelectedItem, DataRowView)
            If selectedRow IsNot Nothing AndAlso selectedRow.DataView.Table.Columns.Contains("RegistrationType") Then
                registrationType = Convert.ToString(selectedRow("RegistrationType"), CultureInfo.InvariantCulture).Trim()
            End If

            label.Text = If(String.IsNullOrWhiteSpace(ForcedRegistrationTypeForTesting),
                           If(String.IsNullOrWhiteSpace(registrationType), fallbackText, registrationType),
                           ForcedRegistrationTypeForTesting) & ":"
        End Sub

        Public Shared Function TryGetSelectedId(combo As ComboBox, ByRef registrationId As Integer) As Boolean
            registrationId = 0
            If combo Is Nothing OrElse combo.SelectedValue Is Nothing OrElse Convert.IsDBNull(combo.SelectedValue) Then
                Return False
            End If

            Return Integer.TryParse(combo.SelectedValue.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, registrationId) AndAlso
                   registrationId > 0
        End Function
    End Class
End Namespace
