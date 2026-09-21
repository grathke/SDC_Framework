Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Everything signing in does once it knows who the person is.
    '''
    ''' Registration, licence, roles, the role prompt, the session itself, the page zooms, the login
    ''' warnings. It lived inline in LoginForm's button handler after the password check, and
    ''' Switch User needs exactly the same thing without the password - a second copy would have
    ''' been right on the day it was written and wrong the first time login changed.
    '''
    ''' The password is not here and must never be. Authenticating is the login form's job; this
    ''' starts a session for somebody already established, which is why Switch User checks the
    ''' caller is an App Admin before it gets this far.
    ''' </summary>
    Public NotInheritable Class SessionStarter

        Private Sub New()
        End Sub

        ''' <summary>Why a session is being started, which decides what a refusal costs.</summary>
        Public Enum Purpose
            ''' <summary>A real sign-in. A lapsed licence or no roles ends the application, as it always has.</summary>
            Login

            ''' <summary>
            ''' An administrator becoming somebody, or returning to themselves. A refusal leaves the
            ''' current session exactly as it was: ending the application would throw out the
            ''' administrator, who has done nothing wrong.
            ''' </summary>
            SwitchUser
        End Enum

        ''' <summary>
        ''' Starts a session for <paramref name="user"/>.
        '''
        ''' Returns True once the session is running. <paramref name="cancelled"/> is True when the
        ''' person declined the role prompt, which is a choice rather than a failure and needs no
        ''' message.
        '''
        ''' <paramref name="preferredRoleId"/> skips the role prompt when that role is among the
        ''' person's own - returning an administrator to the role they were in, rather than asking
        ''' them to choose it again.
        '''
        ''' Nothing about the current session changes until every check has passed. A switch that is
        ''' refused leaves the administrator exactly where they were.
        ''' </summary>
        Public Shared Function Begin(owner As IWin32Window,
                                     user As UserContext,
                                     reason As Purpose,
                                     ByRef cancelled As Boolean,
                                     Optional preferredRoleId As Integer = 0) As Boolean
            cancelled = False
            If user Is Nothing OrElse user.UserId <= 0 Then Return False

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
                Refuse(owner, reason, user,
                       "LICENSE DATE IS NOT SET FOR THIS REGISTRATION.",
                       "License Date Missing")
                Return False
            End If

            Dim today As Date = Date.Today
            Dim endDate As Date = licenseEndDate.Value.Date

            If today >= endDate Then
                If today = endDate Then
                    Refuse(owner, reason, user,
                           If(reason = Purpose.Login, "YOUR LICENSE", "THE LICENSE") & " EXPIRES TODAY, " & endDate.ToString("MMMM d, yyyy").ToUpper() & ".",
                           "License Expires Today")
                Else
                    Refuse(owner, reason, user,
                           If(reason = Purpose.Login, "YOUR LICENSE", "THE LICENSE") & " EXPIRED ON " & endDate.ToString("MMMM d, yyyy").ToUpper() & ".",
                           "License Expired")
                End If

                Return False
            End If

            Dim daysRemaining As Integer = CInt((endDate - today).TotalDays)
            If daysRemaining <= 10 Then
                Dim dayLabel = If(daysRemaining = 1, "Day", "Days")
                loginWarnings.Add(("WARNING: YOUR LICENSE WILL EXPIRE ON " & endDate.ToString("MMMM d, yyyy") & "." & vbCrLf & vbCrLf &
                                   daysRemaining.ToString() & " " & dayLabel & " UNTIL EXPIRATION." & vbCrLf & vbCrLf &
                                   "PLEASE CONTACT YOUR ADMINISTRATOR TO RENEW.").ToUpperInvariant())
            End If

            If assignedRoles Is Nothing OrElse assignedRoles.Count = 0 Then
                ' Addressed to the person reading it. "This user" described them in the third
                ' person, and the people who sign in are not all users of the company anyway -
                ' an employee, a contractor and a portal account all land here. "The selected
                ' registration" went too: nobody selected one, it was resolved for them.
                Refuse(owner, reason, user,
                       If(reason = Purpose.Login, "YOU DO NOT HAVE ANY ROLES ASSIGNED.", "THEY DO NOT HAVE ANY ROLES ASSIGNED."),
                       "No Roles Assigned")
                Return False
            End If

            Dim selectedRole As UserRoleOption = Nothing
            If preferredRoleId > 0 Then
                selectedRole = assignedRoles.FirstOrDefault(Function(role) role.RoleID = preferredRoleId)
            End If

            If selectedRole Is Nothing Then
                If assignedRoles.Count = 1 Then
                    selectedRole = assignedRoles(0)
                Else
                    Using roleSelector As New FW_RoleSelection(assignedRoles)
                        Dim roleResult = roleSelector.ShowDialog(owner)
                        If roleResult <> DialogResult.OK OrElse roleSelector.SelectedRole Is Nothing Then
                            cancelled = True
                            Return False
                        End If

                        selectedRole = roleSelector.SelectedRole
                    End Using
                End If
            End If

            Dim companyAdminRoleId = DataAccess.GetCompanyAdminRoleId(sessionRegistrationId)
            Dim companyAdminUserId = DataAccess.GetCompanyAdminUserId(sessionRegistrationId)
            Dim registrationRecord = DataAccess.GetRegistrationById(sessionRegistrationId)
            Dim smartyAuthId = If(registrationRecord Is Nothing, String.Empty, registrationRecord.Smarty_AuthID)
            Dim smartyAuthToken = If(registrationRecord Is Nothing, String.Empty, registrationRecord.Smarty_AuthToken)
            Dim smartyEmbeddedKey = If(registrationRecord Is Nothing, String.Empty, registrationRecord.Smarty_EmbeddedKey)
            Dim smartyUseEmbeddedKey = registrationRecord IsNot Nothing AndAlso registrationRecord.Smarty_UseEmbeddedKey

            ' Off the record login already read for the Smarty keys, rather than a query of its
            ' own. Login makes six registration round trips already.
            Dim allowUpdateMyProfile = registrationRecord IsNot Nothing AndAlso registrationRecord.AllowUpdateMyProfile
            Dim homeGraphic = If(registrationRecord Is Nothing, String.Empty, If(registrationRecord.HomeGraphic, String.Empty))
            ' The employee's zone wins where they have one; otherwise the registration's.
            Dim registrationTimeZone = If(registrationRecord Is Nothing, String.Empty, If(registrationRecord.TimeZoneName, String.Empty))
            Dim employeeTimeZone = DataAccess.GetEmployeeTimeZoneName(user.UserId)
            If employeeTimeZone <> String.Empty Then registrationTimeZone = employeeTimeZone
            ' Both caps together. Two settings on one row, and asking twice would be a round trip
            ' bought for nothing.
            Dim maxRecordsNoQBE As Integer
            Dim maxRecordsWithQBE As Integer
            DataAccess.GetRecordCaps(sessionRegistrationId, maxRecordsNoQBE, maxRecordsWithQBE)
            ' Off the same registration read as the Smarty keys above, rather than a query of its own.
            Dim messageRetrievalMinutes = If(registrationRecord Is Nothing OrElse Not registrationRecord.MessageRetrievalFrequency.HasValue,
                                             0, registrationRecord.MessageRetrievalFrequency.Value)
            Dim registrationDatePattern As String = String.Empty
            Dim registrationTimePattern As String = String.Empty
            DataAccess.GetRegistrationDisplayFormats(sessionRegistrationId, registrationDatePattern, registrationTimePattern)
            DataAccess.GetHelpDeskRouting(sessionRegistrationId, hdUserSupport, hdApplicationSupport)

            If hdUserSupport <= 0 Then
                loginWarnings.Add("HELP DESK USER SUPPORT IS NOT CONFIGURED FOR THIS REGISTRATION.")
            End If
            If hdApplicationSupport <= 0 Then
                loginWarnings.Add("HELP DESK APPLICATION SUPPORT IS NOT CONFIGURED FOR THIS REGISTRATION.")
            End If

            ' Every page zoom this person has chosen, in one round trip, before any page opens.
            PageZoomStore.LoadForUser(user.UserId)

            SessionState.StartSession(user,
                                      sessionRegistrationId,
                                      sessionRegistrationName,
                                      smartyAuthId,
                                      smartyAuthToken,
                                      smartyEmbeddedKey,
                                      smartyUseEmbeddedKey,
                                      selectedRole.RoleID,
                                      companyAdminRoleId,
                                      companyAdminUserId,
                                      hdUserSupport,
                                      hdApplicationSupport,
                                      selectedRole.RoleName,
                                      selectedRole.RoleType,
                                      selectedRole.IsApplicationAdmin,
                                      selectedRole.IsCompanyAdmin,
                                      maxRecordsNoQBE,
                                      registrationDatePattern,
                                      registrationTimePattern,
                                      allowUpdateMyProfile,
                                      homeGraphic,
                                      registrationTimeZone,
                                      messageRetrievalMinutes,
                                      maxRecordsWithQBE)

            ' Recorded after the session is established rather than before, so the row carries the
            ' registration and role the person actually ended up in - a role prompt can change both
            ' - and so a sign-in that was refused above leaves no session behind.
            '
            ' Here rather than in LoginForm because Switch User comes through this same method, and
            ' a switch is a different person doing different things under different permissions.
            ' SessionTracking.Begin ends any session still open for this process before starting
            ' the new one, so a switch reads as two sessions rather than one long confusing one.
            SessionTracking.Begin(user.UserId, sessionRegistrationId, selectedRole.RoleID)

            If loginWarnings.Count > 0 Then
                MessageBox.Show(owner, String.Join(vbCrLf & vbCrLf, loginWarnings).ToUpperInvariant(),
                                "Login Warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If

            Return True
        End Function

        ''' <summary>
        ''' Says why a session could not start, and what that costs.
        '''
        ''' A real login ends the application, as it always has - a person with no licence or no roles
        ''' has nothing to sign in to. A switch only refuses, and says whose account it was, because
        ''' the administrator reading it is still signed in as themselves and nothing has changed.
        ''' </summary>
        Private Shared Sub Refuse(owner As IWin32Window, reason As Purpose, user As UserContext,
                                  problem As String, title As String)
            If reason = Purpose.Login Then
                MessageBox.Show(owner, problem & vbCrLf & vbCrLf & "PLEASE CONTACT YOUR ADMINISTRATOR.",
                                title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                SessionState.ClearSession()
                Application.Exit()
                Return
            End If

            MessageBox.Show(owner,
                            ("CANNOT SWITCH TO " & user.DisplayName & "." & vbCrLf & vbCrLf & problem).ToUpperInvariant(),
                            title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Sub

    End Class

End Namespace
