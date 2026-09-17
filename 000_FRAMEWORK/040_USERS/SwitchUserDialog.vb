Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Finds the account an administrator wants to become.
    '''
    ''' One text box, searched against the user name and both places an email can live. One match
    ''' is taken straight away - there is nothing to choose between. Several are listed. The
    ''' administrator's own account is never offered: becoming yourself is not a switch.
    '''
    ''' It only finds. Checking that the caller may switch, and starting the session, belong to the
    ''' menu's Switch User action and to SessionStarter.
    ''' </summary>
    Public Class SwitchUserDialog
        Inherits Form

        Private ReadOnly searchBox As TextBox
        Private ReadOnly findButton As Button
        Private ReadOnly matchesList As ListBox
        Private ReadOnly statusLabel As Label
        Private ReadOnly okButton As Button
        Private ReadOnly excludeUserId As Integer

        ''' <summary>The account chosen, once the dialog closes with OK.</summary>
        Public ReadOnly Property ChosenUser As UserContext
            Get
                Return chosen
            End Get
        End Property

        Private chosen As UserContext

        Private NotInheritable Class Match
            Public Property Account As UserContext
            Public Property UserName As String
            Public Property Email As String

            Public Overrides Function ToString() As String
                Dim contact = If(String.IsNullOrWhiteSpace(Email) OrElse String.Equals(Email, UserName, StringComparison.OrdinalIgnoreCase),
                                 UserName, UserName & "  -  " & Email)
                Return Account.DisplayName & "   (" & contact & ")"
            End Function
        End Class

        Public Sub New(excludeUserId As Integer)
            Me.excludeUserId = excludeUserId

            Text = "Switch User"
            StartPosition = FormStartPosition.CenterParent
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ShowInTaskbar = False
            ClientSize = New Size(520, 300)
            Font = New Font("Segoe UI", 9.5F, FontStyle.Regular)

            Controls.Add(New Label() With {
                .Text = "User name or email",
                .Location = New Point(16, 16),
                .AutoSize = True
            })

            searchBox = New TextBox() With {
                .Location = New Point(16, 40),
                .Size = New Size(380, 26)
            }

            findButton = New Button() With {
                .Text = "Find",
                .Location = New Point(406, 38),
                .Size = New Size(98, 30)
            }

            matchesList = New ListBox() With {
                .Location = New Point(16, 80),
                .Size = New Size(488, 150),
                .IntegralHeight = False,
                .Visible = False
            }

            statusLabel = New Label() With {
                .Location = New Point(16, 82),
                .Size = New Size(488, 44),
                .ForeColor = Color.Firebrick
            }

            okButton = New Button() With {
                .Text = "Switch",
                .Location = New Point(290, 248),
                .Size = New Size(104, 34),
                .Enabled = False
            }

            Dim cancelButton As New Button() With {
                .Text = "Cancel",
                .Location = New Point(400, 248),
                .Size = New Size(104, 34),
                .DialogResult = DialogResult.Cancel
            }

            Controls.AddRange(New Control() {searchBox, findButton, matchesList, statusLabel, okButton, cancelButton})
            AcceptButton = findButton
            CancelButton = cancelButton

            AddHandler findButton.Click, Sub(sender, e) Search()
            AddHandler okButton.Click, Sub(sender, e) Choose(TryCast(matchesList.SelectedItem, Match))
            AddHandler matchesList.SelectedIndexChanged, Sub(sender, e) okButton.Enabled = matchesList.SelectedItem IsNot Nothing
            AddHandler matchesList.DoubleClick, Sub(sender, e) Choose(TryCast(matchesList.SelectedItem, Match))
        End Sub

        Private Sub Search()
            statusLabel.Text = String.Empty
            matchesList.Items.Clear()
            matchesList.Visible = False
            okButton.Enabled = False
            AcceptButton = findButton

            If String.IsNullOrWhiteSpace(searchBox.Text) Then
                statusLabel.Text = "Enter a user name or an email."
                searchBox.Focus()
                Return
            End If

            Dim results As List(Of (User As UserContext, UserName As String, Email As String))
            Try
                results = DataAccess.FindUsersForSwitch(searchBox.Text)
            Catch ex As Exception
                statusLabel.Text = "The search could not run: " & ex.Message
                Return
            End Try

            Dim matches = results.
                Where(Function(result) result.User.UserId <> excludeUserId).
                Select(Function(result) New Match With {.Account = result.User, .UserName = result.UserName, .Email = result.Email}).
                ToList()

            ' The only match was the administrator themselves. Saying "no account matches" read as
            ' though their own account did not exist.
            If matches.Count = 0 AndAlso results.Count > 0 Then
                statusLabel.Text = "That is your own account - you are already signed in as it."
                searchBox.SelectAll()
                searchBox.Focus()
                Return
            End If

            If matches.Count = 0 Then
                statusLabel.Text = "No active account matches """ & searchBox.Text.Trim() & """."
                searchBox.SelectAll()
                searchBox.Focus()
                Return
            End If

            ' One match is the answer, not a question.
            If matches.Count = 1 Then
                Choose(matches(0))
                Return
            End If

            For Each candidate In matches
                matchesList.Items.Add(candidate)
            Next

            matchesList.Visible = True
            matchesList.SelectedIndex = 0
            AcceptButton = okButton
            matchesList.Focus()
        End Sub

        Private Sub Choose(candidate As Match)
            If candidate Is Nothing Then Return

            chosen = candidate.Account
            DialogResult = DialogResult.OK
            Close()
        End Sub

    End Class

End Namespace
