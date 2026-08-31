Option Strict On
Option Explicit On

Imports System.Windows.Forms

Namespace HelloWorld
    Public Class FW_Registration_B
        Inherits FW_Base_B

        Public Sub New()
            MyBase.New(BuildCurrentUserFromSession(),
                       MenuFormInitializer.BuildAccessProfileForCurrentSession(BuildCurrentUserFromSession(), "FW_Registration_B"),
                       "FW_Registration")
            Me.Text = "Registration Listing"
        End Sub

        Protected Overrides Function ResolveBrowsePageName() As String
            Return "FW_Registration_B"
        End Function

        Protected Overrides Function UsesRegistrationSelector() As Boolean
            Return False
        End Function

        Protected Overrides Function HandleDefaultCreateAction() As Boolean
            Using page As New FW_Registration_U()
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction()
                End If
            End Using

            Return True
        End Function

        Protected Overrides Function HandleDefaultUpdateAction(recordId As Integer) As Boolean
            If recordId <= 0 Then
                Return False
            End If

            Using page As New FW_Registration_U(recordId)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction(recordId)
                End If
            End Using

            Return True
        End Function

        Private Shared Function BuildCurrentUserFromSession() As UserContext
            Dim activeSession = SessionState.Current
            If activeSession.HasValue Then
                Return New UserContext With {
                    .UserId = activeSession.Value.UserID,
                    .Email = String.Empty,
                    .FirstName = activeSession.Value.FirstName,
                    .LastName = activeSession.Value.LastName
                }
            End If

            Return New UserContext With {
                .UserId = 0,
                .Email = String.Empty,
                .FirstName = String.Empty,
                .LastName = String.Empty
            }
        End Function

    End Class
End Namespace
