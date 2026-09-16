Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Globalization
Imports System.Windows.Forms

Namespace SDC.Framework
    Public NotInheritable Class RegistrationComboHelper
        ''' Forces every registration label to this word, whatever the registration's real type.
        ''' Empty means off, which is how it should stay - it was left set to "Branch" and made the
        ''' label lie on every page that shows the combo.
        Private Const ForcedRegistrationTypeForTesting As String = ""

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
                    If Integer.TryParse(Convert.ToString(row("RegistrationID"), CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, rowId) AndAlso
                       rowId = selectedRegistrationId Then
                        hasSelectedRegistration = True
                        Exit For
                    End If
                Next

                If Not hasSelectedRegistration Then
                    Dim selectedRow = registrations.NewRow()
                    selectedRow("RegistrationID") = selectedRegistrationId
                    selectedRow("RegName") = "Registration " & selectedRegistrationId.ToString(CultureInfo.InvariantCulture)
                    If registrations.Columns.Contains("RegistrationType") Then
                        selectedRow("RegistrationType") = String.Empty
                    End If
                    registrations.Rows.Add(selectedRow)
                End If
            End If

            If includePlaceholder Then
                Dim placeholder = registrations.NewRow()
                placeholder("RegistrationID") = 0
                placeholder("RegName") = placeholderText
                registrations.Rows.InsertAt(placeholder, 0)
            End If

            combo.DataSource = Nothing
            combo.DisplayMember = "RegName"
            combo.ValueMember = "RegistrationID"
            combo.DataSource = registrations

            If selectedRegistrationId > 0 Then
                combo.SelectedValue = selectedRegistrationId
            ElseIf includePlaceholder Then
                combo.SelectedIndex = 0
            End If

            ComboWidth.FitToContent(combo, registrations, "RegName")
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


        ''' <summary>
        ''' Puts the caption immediately left of the combo, at whatever width the combo now is.
        '''
        ''' Two pages draw this pair - FW_Base_B for every browse page, and Roles_B, which builds
        ''' its own - and both move the combo when the window resizes. Seating the label at a fixed
        ''' x is what put the caption on top of the combo, repeatedly: the combo's width changes
        ''' when it is filled and narrowed to its content, and its left changes on every resize, so
        ''' any position worked out in advance is wrong by the time it is seen.
        ''' </summary>
        Public Shared Sub SeatLabel(label As Label, combo As ComboBox, Optional gap As Integer = 8)
            If label Is Nothing OrElse combo Is Nothing Then Return

            label.Left = Math.Max(0, combo.Left - label.PreferredWidth - gap)
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
