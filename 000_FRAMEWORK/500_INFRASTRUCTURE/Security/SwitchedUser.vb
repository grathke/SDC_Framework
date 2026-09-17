Option Strict On
Option Explicit On

Namespace SDC.Framework

    ''' <summary>
    ''' Who the administrator really is while they are viewing as somebody else.
    '''
    ''' The session itself belongs to the account being viewed - that is the point, since roles,
    ''' permissions and every page read the session. This holds the one thing the session cannot:
    ''' the account to return to, and the role they were in, so Return puts them back exactly where
    ''' they left rather than asking them to choose a role again.
    '''
    ''' One level only. Switching from inside a switch is refused rather than stacked: a chain of
    ''' borrowed identities is how nobody can say who did what.
    ''' </summary>
    Public NotInheritable Class SwitchedUser

        Private Sub New()
        End Sub

        Public Shared ReadOnly Property IsActive As Boolean
            Get
                Return Original IsNot Nothing
            End Get
        End Property

        ''' <summary>The administrator, while a switch is in force. Nothing otherwise.</summary>
        Public Shared Property Original As UserContext

        ''' <summary>The role the administrator was in when they switched.</summary>
        Public Shared Property OriginalRoleId As Integer

        Friend Shared Sub Start(administrator As UserContext, roleId As Integer)
            Original = administrator
            OriginalRoleId = roleId
        End Sub

        Friend Shared Sub Finish()
            Original = Nothing
            OriginalRoleId = 0
        End Sub

    End Class

End Namespace
