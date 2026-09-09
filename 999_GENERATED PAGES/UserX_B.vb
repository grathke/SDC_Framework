Option Strict On
Option Explicit On

Namespace SDC.Framework
    Public Class UserX_B
        Inherits FW_Base_B

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, "FW_USERS")
        End Sub


        Protected Overrides Function CreateMaintenancePage(recordId As Integer) As FW_Base_U
            Return New UserX_U(recordId, CurrentUserContext, CurrentAccessProfile)
        End Function

        Protected Overrides Function UsesStandardSoftDelete() As Boolean
            Return True
        End Function
    End Class
End Namespace
