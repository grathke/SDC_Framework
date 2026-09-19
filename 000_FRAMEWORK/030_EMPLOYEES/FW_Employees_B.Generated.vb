Option Strict On
Option Explicit On

Namespace SDC.Framework

    ''' <summary>
    ''' Written by the page generator. Every regeneration replaces this file in full,
    ''' so nothing added here survives. FW_Employees_B.vb is the half that does.
    ''' </summary>
    Partial Public Class FW_Employees_B
        Inherits FW_Base_B

        ''' <summary>The table this page browses, for the companion's constructor.</summary>
        Friend Const GeneratedTableName As String = "FW_Employees"


        Protected Overrides Function CreateMaintenancePage(recordId As Integer) As FW_Base_U
            Return New FW_Employees_U(recordId, CurrentUserContext, CurrentAccessProfile)
        End Function

        Protected Overrides Function UsesStandardSoftDelete() As Boolean
            Return True
        End Function

        ''' <summary>
        ''' The page is built and its grid is not loaded yet. Implement it in
        ''' FW_Employees_B.vb to add anything this page needs of its own.
        '''
        ''' A partial method with no implementation costs nothing: the compiler
        ''' removes the call as well as the declaration.
        ''' </summary>
        Partial Private Sub OnPageBuilt()
        End Sub
    End Class
End Namespace
