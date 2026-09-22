Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class LoginForm
        Inherits Form

        Private Const MaxAttempts As Integer = 3

        Private ReadOnly emailLabel As Label
        Private ReadOnly passwordLabel As Label
        Private ReadOnly emailTextBox As TextBox
        Private ReadOnly passwordTextBox As TextBox
        Private ReadOnly loginButton As Button
        Private ReadOnly cancelActionButton As Button

        ''' The white panel the fields sit on. Everything goes on this rather than the form, so the
        ''' gradient behind it stays visible.
        Private ReadOnly card As Panel
        Private ReadOnly statusLabel As Label
        ''' The card is its normal height until the hotspot is triple-clicked, then it grows
        ''' downward to uncover the two rows. Growing rather than re-centring: re-centring moves
        ''' the whole card up by 20 and the sign-in box jumps under the cursor.
        Private Const CardNormalHeight As Integer = 300
        Private Const CardRevealedHeight As Integer = 340
        Private Const DevRowOneTop As Integer = 276
        Private Const DevRowTwoTop As Integer = 306

        ''' The Saraland row holds four, so it is laid out on its own pitch. 16 + 4 x 106 ends at
        ''' 434 inside a 440 card.
        Private Const SarRowLeft As Integer = 16
        Private Const SarButtonWidth As Integer = 100
        Private Const SarButtonPitch As Integer = 106

        Private ReadOnly btnSLitaker As Button
        Private ReadOnly btnGRathke As Button
        Private ReadOnly btnASawyer As Button
        Private ReadOnly btnSarCompanyAdmin As Button
        Private ReadOnly btnSarUser1 As Button
        Private ReadOnly btnSarUser2 As Button
        Private ReadOnly btnSarUser3 As Button
        Private ReadOnly devHotspotPanel As Panel
        Private hiddenRevealClickCount As Integer = 0
        Private failedAttempts As Integer = 0

        Public Sub New()
            Me.Text = "User Logon"
            Me.StartPosition = FormStartPosition.CenterScreen
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False

            ' Larger than the card it holds, because the card sits on a backdrop rather than
            ' filling the window. The first screen of the product used to be a grey box with system
            ' buttons; this is the shell's own title band and accent, so login, role selection and
            ' the menu read as one application.
            ' A little larger than the card, so the shell drawn behind shows as a frame around it
            ' rather than a full-size backdrop. Tried at the menu's own size on 2026-09-12 and it
            ' was too much window for a login box.
            Me.ClientSize = New Size(560, 420)

            card = ShellChrome.BuildCard(Me, New Size(440, CardNormalHeight), "Sign in to continue")

            ' Both rows sit below the status label, never over it - revealed at y=240 they covered
            ' the login error that had just been shown.
            btnSLitaker = New Button() With {
                .Name = "BTN_SLitaker",
                .Text = "S Litaker",
                .Location = New Point(16, DevRowOneTop),
                .Size = New Size(130, 26),
                .Visible = False
            }

            btnGRathke = New Button() With {
                .Name = "BTN_GRathke",
                .Text = "G Rathke",
                .Location = New Point(154, DevRowOneTop),
                .Size = New Size(130, 26),
                .Visible = False
            }

            btnASawyer = New Button() With {
                .Name = "BTN_ASawyer",
                .Text = "A Sawyer",
                .Location = New Point(292, DevRowOneTop),
                .Size = New Size(130, 26),
                .Visible = False
            }

            ' Four to a row rather than three, so they are narrower and set on their own pitch.
            ' Captioned for the login - CA, user1, User2, User3 - each of which holds exactly one
            ' Saraland role.
            btnSarCompanyAdmin = New Button() With {
                .Name = "BTN_SAR_CompanyAdmin",
                .Text = "s Cty Admin",
                .Location = New Point(SarRowLeft, DevRowTwoTop),
                .Size = New Size(SarButtonWidth, 26),
                .Visible = False
            }

            btnSarUser1 = New Button() With {
                .Name = "BTN_SAR_User1",
                .Text = "s User 1",
                .Location = New Point(SarRowLeft + SarButtonPitch, DevRowTwoTop),
                .Size = New Size(SarButtonWidth, 26),
                .Visible = False
            }

            btnSarUser2 = New Button() With {
                .Name = "BTN_SAR_User2",
                .Text = "s User 2",
                .Location = New Point(SarRowLeft + SarButtonPitch * 2, DevRowTwoTop),
                .Size = New Size(SarButtonWidth, 26),
                .Visible = False
            }

            btnSarUser3 = New Button() With {
                .Name = "BTN_SAR_User3",
                .Text = "s User 3",
                .Location = New Point(SarRowLeft + SarButtonPitch * 3, DevRowTwoTop),
                .Size = New Size(SarButtonWidth, 26),
                .Visible = False
            }

            emailLabel = New Label() With {
                .Text = "User Name",
                .AutoSize = True,
                .Location = New Point(40, 103),
                .ForeColor = ShellChrome.BodyInk
            }

            emailTextBox = New TextBox() With {
                .Location = New Point(130, 99),
                .Size = New Size(270, 26),
                .BorderStyle = BorderStyle.FixedSingle
            }

            passwordLabel = New Label() With {
                .Text = "Password",
                .AutoSize = True,
                .Location = New Point(40, 145),
                .ForeColor = ShellChrome.BodyInk
            }

            passwordTextBox = New TextBox() With {
                .Location = New Point(130, 141),
                .Size = New Size(270, 26),
                .BorderStyle = BorderStyle.FixedSingle,
                .UseSystemPasswordChar = True
            }

            loginButton = New Button() With {
                .Text = "Login",
                .Location = New Point(130, 190),
                .Size = New Size(130, 38)
            }
            ShellChrome.StylePrimary(loginButton)

            ' Exit, not Cancel. This button ends the application - there is nothing to cancel,
            ' and the login screen is also where the main menu returns to on the way out, where
            ' "Cancel" reads as though it would undo something.
            cancelActionButton = New Button() With {
                .Text = "Exit",
                .Location = New Point(270, 190),
                .Size = New Size(130, 38)
            }
            ShellChrome.StyleSecondary(cancelActionButton)

            statusLabel = New Label() With {
                .Text = String.Empty,
                .AutoSize = False,
                .Size = New Size(370, 34),
                .Location = New Point(40, 234),
                .ForeColor = Color.Firebrick
            }

            ' Seventy-two square rather than forty-eight. A click travelling over the network to a
            ' small corner target is easy to miss, and a miss costs nothing visible - there is no
            ' cursor change and no hover to say the pointer is in the right place. The count needs
            ' three clicks but no timer, so neither speed nor pauses between them matter.
            devHotspotPanel = New Panel() With {
                .Name = "DEV_REVEAL_HOTSPOT",
                .Location = New Point(card.Width - 78, card.Height - 78),
                .Size = New Size(72, 72),
                .BackColor = Color.White,
                .BorderStyle = BorderStyle.None,
                .Anchor = AnchorStyles.Right Or AnchorStyles.Bottom,
                .TabStop = False
            }

            Me.AcceptButton = loginButton
            Me.CancelButton = cancelActionButton

            AddHandler loginButton.Click, AddressOf LoginButton_Click
            AddHandler cancelActionButton.Click, AddressOf CancelButton_Click
            AddHandler btnSLitaker.Click, AddressOf BtnSLitaker_Click
            AddHandler btnGRathke.Click, AddressOf BtnGRathke_Click
            AddHandler btnASawyer.Click, AddressOf BtnASawyer_Click
            AddHandler btnSarCompanyAdmin.Click, AddressOf BtnSarCompanyAdmin_Click
            AddHandler btnSarUser1.Click, AddressOf BtnSarUser1_Click
            AddHandler btnSarUser2.Click, AddressOf BtnSarUser2_Click
            AddHandler btnSarUser3.Click, AddressOf BtnSarUser3_Click
            AddHandler devHotspotPanel.Click, AddressOf DevHotspotPanel_Click

            ' Onto the card, not the form. Anything added to the form would land behind the card
            ' or beside it on the backdrop.
            card.Controls.Add(btnSLitaker)
            card.Controls.Add(btnGRathke)
            card.Controls.Add(btnASawyer)
            card.Controls.Add(btnSarCompanyAdmin)
            card.Controls.Add(btnSarUser1)
            card.Controls.Add(btnSarUser2)
            card.Controls.Add(btnSarUser3)
            card.Controls.Add(emailLabel)
            card.Controls.Add(emailTextBox)
            card.Controls.Add(passwordLabel)
            card.Controls.Add(passwordTextBox)
            card.Controls.Add(loginButton)
            card.Controls.Add(cancelActionButton)
            card.Controls.Add(statusLabel)
            card.Controls.Add(devHotspotPanel)
        End Sub

        Private Sub DevHotspotPanel_Click(sender As Object, e As EventArgs)
            hiddenRevealClickCount += 1
            If hiddenRevealClickCount < 3 Then Return

            hiddenRevealClickCount = 0
            card.Height = CardRevealedHeight

            For Each quickLogin As Button In New Button() {btnSLitaker, btnGRathke, btnASawyer,
                                                           btnSarCompanyAdmin, btnSarUser1, btnSarUser2, btnSarUser3}
                quickLogin.Visible = True
            Next
        End Sub

        Private Sub BtnSarCompanyAdmin_Click(sender As Object, e As EventArgs)
            QuickFillAndLogin("CA", "1234")
        End Sub

        Private Sub BtnSarUser1_Click(sender As Object, e As EventArgs)
            QuickFillAndLogin("user1", "1234")
        End Sub

        Private Sub BtnSarUser2_Click(sender As Object, e As EventArgs)
            QuickFillAndLogin("User2", "1234")
        End Sub

        Private Sub BtnSarUser3_Click(sender As Object, e As EventArgs)
            QuickFillAndLogin("User3", "1234")
        End Sub

        Private Sub BtnSLitaker_Click(sender As Object, e As EventArgs)
            QuickFillAndLogin("sandy@gowisenow.com", "1234")
        End Sub

        Private Sub BtnGRathke_Click(sender As Object, e As EventArgs)
            QuickFillAndLogin("grathke@sdcdev.net", "1234")
        End Sub

        Private Sub BtnASawyer_Click(sender As Object, e As EventArgs)
            QuickFillAndLogin("asawyer@asboinc.com", "1234")
        End Sub

        Private Sub QuickFillAndLogin(email As String, password As String)
            emailTextBox.Text = email
            passwordTextBox.Text = password
            statusLabel.Text = "Logging in..."
            statusLabel.Refresh()
            emailTextBox.Refresh()
            passwordTextBox.Refresh()
            Application.DoEvents()
            LoginButton_Click(loginButton, EventArgs.Empty)
        End Sub

        ''' <summary>Carries an authentication result back from the worker thread.</summary>
        Private Class AuthenticationAttempt
            Public Property Succeeded As Boolean
            Public Property User As UserContext
            Public Property ErrorMessage As String = String.Empty
        End Class

        ''' <summary>
        ''' Reports a login that failed for a reason other than the credentials, and separates the
        ''' causes: an unreachable server is fixed by starting it, while refused credentials or a
        ''' missing database are fixed by changing the settings. Only the latter offers the
        ''' configuration dialog.
        ''' </summary>
        Private Sub HandleLoginDatabaseError(errorMessage As String)
            Dim statusTask = Task.Run(Function() DataAccess.GetConnectionStatus(10))
            FW_BusyDialog.WaitFor(Me, statusTask, "CHECKING DATABASE", "Working out why the sign-in failed...")
            Dim status = statusTask.Result

            If status.Succeeded Then
                ' The database answers, so this was something else and the settings are not at
                ' fault. Offering to change them would send the user down the wrong path.
                statusLabel.Text = errorMessage
                MessageBox.Show(Me, errorMessage, "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            statusLabel.Text = status.Describe()

            ' An unreachable server is not a settings problem. Retrying once it is up is the fix,
            ' and inviting the user to edit a correct connection would only risk breaking it.
            If Not status.SettingsMightFixIt Then
                MessageBox.Show(Me,
                                status.Describe() & Environment.NewLine & Environment.NewLine &
                                status.Message & Environment.NewLine & Environment.NewLine &
                                "Start the database, then sign in again.",
                                "Database Unavailable",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error)
                Return
            End If

            If MessageBox.Show(Me,
                               status.Describe() & Environment.NewLine & Environment.NewLine &
                               status.Message & Environment.NewLine & Environment.NewLine &
                               "Open the database connection settings?",
                               "Database Connection Error",
                               MessageBoxButtons.YesNo,
                               MessageBoxIcon.Error) <> DialogResult.Yes Then
                Return
            End If

            Using configForm As New FW_DatabaseConfig(status.Describe() & " Check the connection settings below.")
                If configForm.ShowDialog(Me) = DialogResult.OK Then
                    statusLabel.Text = "Connection settings saved. Sign in again."
                End If
            End Using
        End Sub

        Private Sub LoginButton_Click(sender As Object, e As EventArgs)
            statusLabel.Text = String.Empty

            ' Authenticated off the UI thread so the window can paint. Against an unreachable
            ' server the connection attempt can take tens of seconds, and doing it inline made the
            ' application look frozen with no indication anything was happening.
            Dim email = emailTextBox.Text
            Dim password = passwordTextBox.Text
            Dim attemptTask = Task.Run(Function()
                                           Dim attemptUser As UserContext = Nothing
                                           Dim attemptError As String = String.Empty
                                           Dim attemptOk = DataAccess.TryAuthenticate(email, password, attemptUser, attemptError)
                                           Return New AuthenticationAttempt With {
                                               .Succeeded = attemptOk,
                                               .User = attemptUser,
                                               .ErrorMessage = attemptError
                                           }
                                       End Function)

            FW_BusyDialog.WaitFor(Me, attemptTask, "SIGNING IN", "Contacting the database...")

            Dim attempt = attemptTask.Result
            Dim user As UserContext = attempt.User
            Dim errorMessage As String = attempt.ErrorMessage
            Dim ok = attempt.Succeeded

            If Not ok Then
                ' Only a thrown error reaches here. A wrong password or unknown email produces a
                ' plain message and falls through to the attempt counter below, and must never
                ' offer to reconfigure the database.
                If errorMessage.StartsWith("Login failed:", StringComparison.OrdinalIgnoreCase) Then
                    statusLabel.Text = errorMessage
                    HandleLoginDatabaseError(errorMessage)
                    Return
                End If

                failedAttempts += 1
                Dim attemptsLeft = MaxAttempts - failedAttempts

                If attemptsLeft > 0 Then
                    statusLabel.Text = errorMessage & " Attempts left: " & attemptsLeft.ToString() & "."
                Else
                    statusLabel.Text = "Maximum login attempts reached. Application will close."
                    MessageBox.Show("Too many failed login attempts.", "Login Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Me.Close()
                End If

                Return
            End If

            ' The form stays alive behind the main menu, so without this a signed-out user comes
            ' back carrying the failures from before they got in.
            failedAttempts = 0

            statusLabel.Text = "Logging in..."
            statusLabel.ForeColor = Color.Firebrick
            statusLabel.Refresh()
            emailTextBox.Refresh()
            passwordTextBox.Refresh()

            ' Everything from here to the menu - registration, licence, roles, the session - is
            ' SessionStarter's, shared with Switch User so the two cannot come apart. The password
            ' check above is the only part that belongs to this form.
            Dim roleSelectionCancelled As Boolean
            If Not SessionStarter.Begin(Me, user, SessionStarter.Purpose.Login, roleSelectionCancelled) Then
                If roleSelectionCancelled Then statusLabel.Text = "Role selection cancelled."
                Return
            End If

            Dim menuDialogResult As DialogResult = DialogResult.None

            Using menu As New FW_MainMenu(user, AddressOf MenuFormInitializer.Configure)
                ' The browse path's one-time startup cost, paid on a background thread once the menu
                ' is on screen - see BrowseWarmUp for the measurement. Hooked here rather than added
                ' to FW_MainMenu: the menu is a protected area, and nothing about this is the menu's
                ' business beyond the moment it picks to start.
                AddHandler menu.Shown, Sub(warmSender As Object, warmArgs As EventArgs) BrowseWarmUp.Start()

                Me.Hide()
                menuDialogResult = menu.ShowDialog(Me)
            End Using

            If menuDialogResult = DialogResult.Cancel Then
                SessionState.ClearSession()
                Me.Show()
                Me.Activate()
                statusLabel.Text = String.Empty
                emailTextBox.Clear()
                passwordTextBox.Clear()
                Return
            End If

            Me.Close()
        End Sub

        Private Sub CancelButton_Click(sender As Object, e As EventArgs)
            SessionState.ClearSession()
            Me.Close()
        End Sub
    End Class
End Namespace
