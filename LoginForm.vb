Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class LoginForm
        Inherits Form

        Private Const MaxAttempts As Integer = 3

        Private ReadOnly emailLabel As Label
        Private ReadOnly passwordLabel As Label
        Private ReadOnly emailTextBox As TextBox
        Private ReadOnly passwordTextBox As TextBox
        Private ReadOnly loginButton As Button
        Private ReadOnly cancelActionButton As Button
        Private ReadOnly statusLabel As Label
        Private ReadOnly btnSLitaker As Button
        Private ReadOnly btnGRathke As Button
        Private ReadOnly btnASawyer As Button
        Private ReadOnly devHotspotPanel As Panel
        Private hiddenRevealClickCount As Integer = 0
        Private failedAttempts As Integer = 0

        Public Sub New()
            Me.Text = "Contacts Login"
            Me.StartPosition = FormStartPosition.CenterScreen
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New Size(420, 230)
            Me.BackColor = Color.White

            btnSLitaker = New Button() With {
                .Name = "BTN_SLitaker",
                .Text = "S Litaker",
                .Location = New Point(20, 6),
                .Size = New Size(115, 24),
                .Visible = False
            }

            btnGRathke = New Button() With {
                .Name = "BTN_GRathke",
                .Text = "G Rathke",
                .Location = New Point(145, 6),
                .Size = New Size(115, 24),
                .Visible = False
            }

            btnASawyer = New Button() With {
                .Name = "BTN_ASawyer",
                .Text = "A Sawyer",
                .Location = New Point(270, 6),
                .Size = New Size(115, 24),
                .Visible = False
            }

            emailLabel = New Label() With {
                .Text = "Email",
                .AutoSize = True,
                .Location = New Point(30, 54),
                .ForeColor = Color.FromArgb(60, 60, 60)
            }

            emailTextBox = New TextBox() With {
                .Location = New Point(130, 49),
                .Size = New Size(250, 26),
                .BorderStyle = BorderStyle.FixedSingle
            }

            passwordLabel = New Label() With {
                .Text = "Password",
                .AutoSize = True,
                .Location = New Point(30, 99),
                .ForeColor = Color.FromArgb(60, 60, 60)
            }

            passwordTextBox = New TextBox() With {
                .Location = New Point(130, 94),
                .Size = New Size(250, 26),
                .BorderStyle = BorderStyle.FixedSingle,
                .UseSystemPasswordChar = True
            }

            loginButton = New Button() With {
                .Text = "Login",
                .Location = New Point(130, 144),
                .Size = New Size(110, 36),
                .FlatStyle = FlatStyle.Flat,
                .BackColor = Color.White,
                .ForeColor = Color.FromArgb(45, 45, 45)
            }
            loginButton.FlatAppearance.BorderColor = Color.FromArgb(170, 170, 170)
            loginButton.FlatAppearance.BorderSize = 1

            cancelActionButton = New Button() With {
                .Text = "Cancel",
                .Location = New Point(270, 144),
                .Size = New Size(110, 36),
                .FlatStyle = FlatStyle.Flat,
                .BackColor = Color.White,
                .ForeColor = Color.FromArgb(45, 45, 45)
            }
            cancelActionButton.FlatAppearance.BorderColor = Color.FromArgb(170, 170, 170)
            cancelActionButton.FlatAppearance.BorderSize = 1

            statusLabel = New Label() With {
                .Text = String.Empty,
                .AutoSize = False,
                .Size = New Size(350, 30),
                .Location = New Point(30, 186),
                .ForeColor = Color.Firebrick
            }

            devHotspotPanel = New Panel() With {
                .Name = "DEV_REVEAL_HOTSPOT",
                .Location = New Point(Me.ClientSize.Width - 54, Me.ClientSize.Height - 54),
                .Size = New Size(48, 48),
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
            AddHandler devHotspotPanel.Click, AddressOf DevHotspotPanel_Click

            Me.Controls.Add(btnSLitaker)
            Me.Controls.Add(btnGRathke)
            Me.Controls.Add(btnASawyer)
            Me.Controls.Add(emailLabel)
            Me.Controls.Add(emailTextBox)
            Me.Controls.Add(passwordLabel)
            Me.Controls.Add(passwordTextBox)
            Me.Controls.Add(loginButton)
            Me.Controls.Add(cancelActionButton)
            Me.Controls.Add(statusLabel)
            Me.Controls.Add(devHotspotPanel)
        End Sub

        Private Sub DevHotspotPanel_Click(sender As Object, e As EventArgs)
            hiddenRevealClickCount += 1
            If hiddenRevealClickCount >= 3 Then
                btnSLitaker.Visible = True
                btnGRathke.Visible = True
                btnASawyer.Visible = True
                hiddenRevealClickCount = 0
            End If
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

            statusLabel.Text = "Logging in..."
            statusLabel.ForeColor = Color.Firebrick
            statusLabel.Refresh()
            emailTextBox.Refresh()
            passwordTextBox.Refresh()

            Dim sessionRegistrationId As Integer = user.UserId
            Dim sessionRegistrationName As String = String.Empty
            Dim licenseEndDate As Nullable(Of Date) = Nothing
            Dim assignedRoles As List(Of UserRoleOption) = Nothing
            Dim hdUserSupport As Integer = 0
            Dim hdApplicationSupport As Integer = 0
            Dim loginWarnings As New List(Of String)()

            Dim loadedLoginContext = DataAccess.TryGetLoginRoleSelectionData(user.UserId,
                                                                             sessionRegistrationId,
                                                                             sessionRegistrationName,
                                                                             licenseEndDate,
                                                                             assignedRoles)

            If Not loadedLoginContext Then
                If Not DataAccess.TryGetRegistrationContextForUser(user.UserId, sessionRegistrationId, sessionRegistrationName) Then
                    sessionRegistrationId = user.UserId
                    sessionRegistrationName = String.Empty
                End If

                licenseEndDate = DataAccess.GetLicenseEndDate(sessionRegistrationId)
                assignedRoles = DataAccess.GetAssignedRolesForUser(user.UserId, sessionRegistrationId)
            End If

            ' License expiration check
            If Not licenseEndDate.HasValue Then
                MessageBox.Show(
                    "LICENSE DATE IS NOT SET FOR THIS REGISTRATION." & vbCrLf & vbCrLf &
                    "PLEASE CONTACT YOUR ADMINISTRATOR.",
                    "License Date Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                SessionState.ClearSession()
                Application.Exit()
                Return
            End If

            Dim today As Date = Date.Today
            Dim endDate As Date = licenseEndDate.Value.Date

            If today >= endDate Then
                If today = endDate Then
                    MessageBox.Show(
                        "YOUR LICENSE EXPIRES TODAY, " & endDate.ToString("MMMM d, yyyy").ToUpper() & "." & vbCrLf & vbCrLf &
                        "PLEASE CONTACT YOUR ADMINISTRATOR.",
                        "License Expires Today", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Else
                    MessageBox.Show(
                        "YOUR LICENSE EXPIRED ON " & endDate.ToString("MMMM d, yyyy").ToUpper() & "." & vbCrLf & vbCrLf &
                        "PLEASE CONTACT YOUR ADMINISTRATOR.",
                        "License Expired", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If

                SessionState.ClearSession()
                Application.Exit()
                Return
            End If

            Dim daysRemaining As Integer = CInt((endDate - today).TotalDays)
            If daysRemaining <= 10 Then
                Dim dayLabel = If(daysRemaining = 1, "Day", "Days")
                loginWarnings.Add(("WARNING: YOUR LICENSE WILL EXPIRE ON " & endDate.ToString("MMMM d, yyyy") & "." & vbCrLf & vbCrLf &
                                   daysRemaining.ToString() & " " & dayLabel & " UNTIL EXPIRATION." & vbCrLf & vbCrLf &
                                   "PLEASE CONTACT YOUR ADMINISTRATOR TO RENEW.").ToUpperInvariant())
            End If

            If assignedRoles Is Nothing OrElse assignedRoles.Count = 0 Then
                MessageBox.Show(
                    "NO ROLES ARE ASSIGNED TO THIS USER FOR THE SELECTED REGISTRATION." & vbCrLf & vbCrLf &
                    "PLEASE CONTACT YOUR ADMINISTRATOR.",
                    "No Roles Assigned",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning)
                SessionState.ClearSession()
                Application.Exit()
                Return
            End If

            Dim selectedRole As UserRoleOption = Nothing
            If assignedRoles.Count = 1 Then
                selectedRole = assignedRoles(0)
            Else
                Using roleSelector As New FW_RoleSelection(assignedRoles)
                    Dim roleResult = roleSelector.ShowDialog(Me)
                    If roleResult <> DialogResult.OK OrElse roleSelector.SelectedRole Is Nothing Then
                        statusLabel.Text = "Role selection cancelled."
                        Return
                    End If

                    selectedRole = roleSelector.SelectedRole
                End Using
            End If

            Dim companyAdminRoleId = DataAccess.GetCompanyAdminRoleId(sessionRegistrationId)
            Dim companyAdminUserId = DataAccess.GetCompanyAdminUserId(sessionRegistrationId)
            Dim registrationRecord = DataAccess.GetRegistrationById(sessionRegistrationId)
            Dim smartyAuthId = If(registrationRecord Is Nothing, String.Empty, registrationRecord.Smarty_AuthID)
            Dim smartyAuthToken = If(registrationRecord Is Nothing, String.Empty, registrationRecord.Smarty_AuthToken)
            Dim smartyEmbeddedKey = If(registrationRecord Is Nothing, String.Empty, registrationRecord.Smarty_EmbeddedKey)
            Dim smartyUseEmbeddedKey = registrationRecord IsNot Nothing AndAlso registrationRecord.Smarty_UseEmbeddedKey
            Dim businessRuleType = DataAccess.GetRegistrationBusinessRuleType(sessionRegistrationId)
            Dim maxRecordsNoQBE = DataAccess.GetMaxRecordsNoQBE(sessionRegistrationId)
            DataAccess.GetHelpDeskRouting(sessionRegistrationId, hdUserSupport, hdApplicationSupport)

            If hdUserSupport <= 0 Then
                loginWarnings.Add("HELP DESK USER SUPPORT IS NOT CONFIGURED FOR THIS REGISTRATION.")
            End If
            If hdApplicationSupport <= 0 Then
                loginWarnings.Add("HELP DESK APPLICATION SUPPORT IS NOT CONFIGURED FOR THIS REGISTRATION.")
            End If

            SessionState.StartSession(user,
                                      sessionRegistrationId,
                                      sessionRegistrationName,
                                      smartyAuthId,
                                      smartyAuthToken,
                                      smartyEmbeddedKey,
                                      smartyUseEmbeddedKey,
                                      businessRuleType,
                                      selectedRole.RoleID,
                                      companyAdminRoleId,
                                      companyAdminUserId,
                                      hdUserSupport,
                                      hdApplicationSupport,
                                      selectedRole.RoleName,
                                      selectedRole.RoleType,
                                      selectedRole.IsApplicationAdmin,
                                      selectedRole.IsCompanyAdmin,
                                      maxRecordsNoQBE)

            If loginWarnings.Count > 0 Then
                MessageBox.Show(String.Join(vbCrLf & vbCrLf, loginWarnings).ToUpperInvariant(),
                                "Login Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If

            Dim menuDialogResult As DialogResult = DialogResult.None

            Using menu As New FW_MainMenu(user, AddressOf MenuFormInitializer.Configure)
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
