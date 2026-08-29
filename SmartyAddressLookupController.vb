Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Net.Http
Imports System.Text.Json
Imports System.Windows.Forms

Namespace HelloWorld
    Public NotInheritable Class SmartyAddressLookupController
        Private Shared ReadOnly httpClient As New HttpClient()
        Private ReadOnly addressTextBox As TextBox
        Private ReadOnly cityTextBox As TextBox
        Private ReadOnly stateTextBox As TextBox
        Private ReadOnly zipTextBox As TextBox
        Private ReadOnly suggestionsList As ListBox
        Private ReadOnly lookupTimer As Timer
        Private ReadOnly isEnabled As Func(Of Boolean)
        Private ReadOnly getEmbeddedKey As Func(Of String)
        Private requestVersion As Integer
        Private suppressLookup As Boolean

        Public Sub New(owner As Form,
                       addressTextBox As TextBox,
                       cityTextBox As TextBox,
                       stateTextBox As TextBox,
                       zipTextBox As TextBox,
                       isEnabled As Func(Of Boolean),
                       getEmbeddedKey As Func(Of String))
            Me.addressTextBox = addressTextBox
            Me.cityTextBox = cityTextBox
            Me.stateTextBox = stateTextBox
            Me.zipTextBox = zipTextBox
            Me.isEnabled = isEnabled
            Me.getEmbeddedKey = getEmbeddedKey

            suggestionsList = New ListBox() With {
                .Name = "ListBox_" & addressTextBox.Name & "_SmartySuggestions",
                .Size = New Size(Math.Max(250, addressTextBox.Width), 100),
                .Visible = False,
                .IntegralHeight = False,
                .DisplayMember = "DisplayText"
            }
            owner.Controls.Add(suggestionsList)
            PositionSuggestionsList()
            suggestionsList.BringToFront()

            lookupTimer = New Timer() With {.Interval = 350}
            AddHandler lookupTimer.Tick, AddressOf LookupTimer_Tick
            AddHandler addressTextBox.TextChanged, AddressOf AddressTextBox_TextChanged
            AddHandler addressTextBox.KeyDown, AddressOf AddressTextBox_KeyDown
            AddHandler addressTextBox.Leave, AddressOf AddressTextBox_Leave
            AddHandler suggestionsList.DoubleClick, AddressOf SuggestionSelected
            AddHandler suggestionsList.KeyDown, AddressOf SuggestionsList_KeyDown
            AddHandler owner.Resize, Sub(sender, e) PositionSuggestionsList()
        End Sub

        Private Sub PositionSuggestionsList()
            If addressTextBox Is Nothing OrElse addressTextBox.Parent Is Nothing Then Return
            suggestionsList.Location = New Point(addressTextBox.Left, addressTextBox.Bottom + 2)
        End Sub

        Private Sub AddressTextBox_TextChanged(sender As Object, e As EventArgs)
            If suppressLookup Then Return
            requestVersion += 1
            lookupTimer.Stop()
            suggestionsList.Visible = False
            suggestionsList.DataSource = Nothing
            If addressTextBox.Text.Trim().Length >= 3 AndAlso IsLookupEnabled() Then lookupTimer.Start()
        End Sub

        Private Sub AddressTextBox_Leave(sender As Object, e As EventArgs)
            addressTextBox.BeginInvoke(New MethodInvoker(Sub()
                If Not addressTextBox.Focused AndAlso Not suggestionsList.Focused Then CloseSuggestions()
            End Sub))
        End Sub

        Private Sub AddressTextBox_KeyDown(sender As Object, e As KeyEventArgs)
            If Not suggestionsList.Visible OrElse suggestionsList.Items.Count = 0 Then Return
            If e.KeyCode = Keys.Down OrElse e.KeyCode = Keys.Up Then
                suggestionsList.Focus()
                If e.KeyCode = Keys.Down Then
                    suggestionsList.SelectedIndex = Math.Min(suggestionsList.SelectedIndex + 1, suggestionsList.Items.Count - 1)
                Else
                    suggestionsList.SelectedIndex = If(suggestionsList.SelectedIndex <= 0, suggestionsList.Items.Count - 1, suggestionsList.SelectedIndex - 1)
                End If
                e.SuppressKeyPress = True
            ElseIf e.KeyCode = Keys.Enter Then
                SelectSuggestion()
                e.SuppressKeyPress = True
            ElseIf e.KeyCode = Keys.Escape Then
                CloseSuggestions()
                e.SuppressKeyPress = True
            End If
        End Sub

        Private Async Sub LookupTimer_Tick(sender As Object, e As EventArgs)
            lookupTimer.Stop()
            Dim currentVersion = requestVersion
            Dim searchText = addressTextBox.Text.Trim()
            Dim embeddedKey = If(getEmbeddedKey Is Nothing, String.Empty, getEmbeddedKey())
            If searchText.Length < 3 OrElse Not IsLookupEnabled() OrElse String.IsNullOrWhiteSpace(embeddedKey) Then Return

            Try
                Dim requestUri = "https://us-autocomplete-pro.api.smarty.com/lookup?key=" & Uri.EscapeDataString(embeddedKey) & "&search=" & Uri.EscapeDataString(searchText)
                Dim responseJson = Await httpClient.GetStringAsync(requestUri)
                If currentVersion <> requestVersion OrElse addressTextBox.Text.Trim() <> searchText Then Return

                Dim suggestions As New List(Of SmartyAddressSuggestion)()
                Using document = JsonDocument.Parse(responseJson)
                    Dim root = document.RootElement
                    If root.ValueKind = JsonValueKind.Object Then
                        If Not root.TryGetProperty("suggestions", root) Then Return
                    End If
                    If root.ValueKind <> JsonValueKind.Array Then Return
                    For Each item In root.EnumerateArray()
                        Dim street = ReadJsonString(item, "street_line")
                        If String.IsNullOrWhiteSpace(street) Then Continue For
                        Dim city = ReadJsonString(item, "city")
                        Dim state = ReadJsonString(item, "state")
                        Dim zip = ReadJsonString(item, "zipcode")
                        suggestions.Add(New SmartyAddressSuggestion With {
                            .StreetLine = street,
                            .City = city,
                            .State = state,
                            .Zip = zip,
                            .DisplayText = street & ", " & city & ", " & state & " " & zip
                        })
                    Next
                End Using
                suggestionsList.DataSource = suggestions
                suggestionsList.Visible = suggestions.Count > 0
                suggestionsList.BringToFront()
            Catch
                CloseSuggestions()
            End Try
        End Sub

        Private Sub SuggestionsList_KeyDown(sender As Object, e As KeyEventArgs)
            If e.KeyCode = Keys.Enter Then
                SelectSuggestion()
                e.Handled = True
                e.SuppressKeyPress = True
            ElseIf e.KeyCode = Keys.Escape Then
                CloseSuggestions()
                addressTextBox.Focus()
                e.Handled = True
                e.SuppressKeyPress = True
            End If
        End Sub

        Private Sub SuggestionSelected(sender As Object, e As EventArgs)
            SelectSuggestion()
        End Sub

        Private Sub SelectSuggestion()
            Dim suggestion = TryCast(suggestionsList.SelectedItem, SmartyAddressSuggestion)
            If suggestion Is Nothing Then Return
            suppressLookup = True
            Try
                addressTextBox.Text = suggestion.StreetLine
                cityTextBox.Text = suggestion.City
                stateTextBox.Text = suggestion.State
                zipTextBox.Text = suggestion.Zip
                addressTextBox.SelectionStart = addressTextBox.Text.Length
            Finally
                suppressLookup = False
                CloseSuggestions()
                addressTextBox.Focus()
            End Try
        End Sub

        Private Sub CloseSuggestions()
            requestVersion += 1
            lookupTimer.Stop()
            suggestionsList.Visible = False
            suggestionsList.DataSource = Nothing
        End Sub

        ''' <summary>
        ''' True when the active session is cleared to use Smarty embedded-key lookup.
        ''' Single owner of this test; pages must not re-derive it from SessionState.
        ''' </summary>
        Public Shared Function IsSessionLookupEnabled() As Boolean
            Return SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso
                   SessionState.Current.Value.Smarty_UseEmbeddedKey
        End Function

        ''' <summary>
        ''' The active session's Smarty embedded key, trimmed, or an empty string.
        ''' </summary>
        Public Shared Function GetSessionEmbeddedKey() As String
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                Return If(SessionState.Current.Value.Smarty_EmbeddedKey, String.Empty).Trim()
            End If

            Return String.Empty
        End Function

        Private Function IsLookupEnabled() As Boolean
            Return isEnabled IsNot Nothing AndAlso isEnabled()
        End Function

        Private Shared Function ReadJsonString(element As JsonElement, propertyName As String) As String
            Dim value As JsonElement = Nothing
            If element.TryGetProperty(propertyName, value) AndAlso value.ValueKind = JsonValueKind.String Then Return If(value.GetString(), String.Empty).Trim()
            Return String.Empty
        End Function

        Private Class SmartyAddressSuggestion
            Public Property StreetLine As String
            Public Property City As String
            Public Property State As String
            Public Property Zip As String
            Public Property DisplayText As String
            Public Overrides Function ToString() As String
                Return DisplayText
            End Function
        End Class
    End Class
End Namespace
