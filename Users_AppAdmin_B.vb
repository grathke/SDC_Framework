Option Strict On
Option Explicit On

Imports System
Imports System.Windows.Forms

Namespace HelloWorld

    Public Class Users_AppAdmin_B
        Inherits FW_Base_B

        Public Sub New(Optional profile As AccessProfile = Nothing)
            MyBase.New(BuildCurrentUserContext(),
                       If(profile, MenuFormInitializer.BuildAccessProfileForCurrentSession(BuildCurrentUserContext(), "Users_AppAdmin_B")),
                       "FW_Users")
        End Sub

        Protected Overrides Function ResolveCurrentRoleFieldTableName() As String
            Return "FW_Users"
        End Function

        Protected Overrides Function HandleDefaultCreateAction() As Boolean
            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                Return False
            End If

            Dim newUser = New UserAdminRecord() With {
                .RegistrationID = registrationId,
                .IsActive = True
            }

            Using page As New Users_AppAdmin_U(UserAdminEditMode.CreateMode, newUser)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction()
                End If
            End Using

            Return True
        End Function

        ''' <summary>
        ''' Soft-deletes the selected user. Mirrors Entity_B: confirm, delete through the shared
        ''' data-access call that also writes the audit row, then refresh.
        '''
        ''' The confirmation names the person, because "Delete this record?" on a grid of users is
        ''' not enough to be sure which one is selected.
        ''' </summary>
        Protected Overrides Function HandleDefaultDeleteAction() As Boolean
            Dim recordId = GetSelectedRecordIdForCustomAction()
            If Not recordId.HasValue Then Return False

            Dim userRecord = DataAccess.GetUserByID(recordId.Value)
            If userRecord Is Nothing Then
                MessageBox.Show(Me, "THIS USER NO LONGER EXISTS.", "DELETE USER", MessageBoxButtons.OK, MessageBoxIcon.Information)
                RefreshGridForCustomAction()
                Return True
            End If

            Dim who = If(String.IsNullOrWhiteSpace(userRecord.FirstLast), userRecord.Email, userRecord.FirstLast)
            If MessageBox.Show(Me,
                               ("Delete " & who & "?" & Environment.NewLine & Environment.NewLine &
                                "The user will no longer be able to sign in, and will be hidden from this list.").ToUpperInvariant(),
                               "CONFIRM DELETE",
                               MessageBoxButtons.YesNo,
                               MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return True
            End If

            DataAccess.DeleteUser(recordId.Value, If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))
            RefreshGridForCustomAction()
            Return True
        End Function

        Protected Overrides Function HandleDefaultUpdateAction(recordId As Integer) As Boolean
            Try
                Dim userRecord = DataAccess.GetUserByID(recordId)
                If userRecord Is Nothing OrElse userRecord.UserID <= 0 Then
                    MessageBox.Show(Me,
                                    "THE SELECTED USER RECORD COULD NOT BE LOADED.",
                                    "UPDATE USER",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning)
                    Return True
                End If

                Using page As New Users_AppAdmin_U(UserAdminEditMode.UpdateMode, userRecord)
                    If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                        RefreshGridForCustomAction(recordId)
                    End If
                End Using
            Catch ex As Exception
                MessageBox.Show(Me,
                                "UNABLE TO OPEN THE SELECTED USER RECORD." & Environment.NewLine & Environment.NewLine & ex.Message,
                                "UPDATE USER FAILED",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error)
            End Try

            Return True
        End Function

        Private Shared Function BuildCurrentUserContext() As UserContext
            Dim session = SessionState.Current
            If session.HasValue Then
                Return New UserContext With {
                    .UserId = session.Value.UserID,
                    .FirstName = session.Value.FirstName,
                    .LastName = session.Value.LastName,
                    .Email = String.Empty
                }
            End If

            Return New UserContext With {
                .UserId = 0,
                .FirstName = String.Empty,
                .LastName = String.Empty,
                .Email = String.Empty
            }
        End Function

    End Class

    Public Enum UserAdminEditMode
        CreateMode
        ReadMode
        UpdateMode
        DeleteMode
    End Enum

End Namespace
