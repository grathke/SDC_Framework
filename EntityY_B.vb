Option Strict On
Option Explicit On

Imports System.Windows.Forms

Namespace HelloWorld
    Public Class EntityY_B
        Inherits FW_Base_B

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, "FW_ENTITY")
            currentUser = user
            accessProfile = profile
        End Sub

        Protected Overrides Function HandleDefaultCreateAction() As Boolean
            Using page As New EntityY_U(0, currentUser, accessProfile)
                If page.ShowDialog(Me) = DialogResult.OK Then RefreshGridForCustomAction(page.SavedRecordId)
            End Using
            Return True
        End Function

        Protected Overrides Function HandleDefaultUpdateAction(recordId As Integer) As Boolean
            Using page As New EntityY_U(recordId, currentUser, accessProfile)
                If page.ShowDialog(Me) = DialogResult.OK Then RefreshGridForCustomAction(recordId)
            End Using
            Return True
        End Function
    End Class
End Namespace
