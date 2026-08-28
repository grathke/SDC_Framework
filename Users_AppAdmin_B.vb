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
                If page.ShowDialog(Me) = System.Windows.Forms.DialogResult.OK Then
                    RefreshGridForCustomAction()
                End If
            End Using

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
                    If page.ShowDialog(Me) = System.Windows.Forms.DialogResult.OK Then
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
