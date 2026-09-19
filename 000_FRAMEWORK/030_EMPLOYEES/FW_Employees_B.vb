Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' This file is yours. The page generator writes FW_Employees_B.Generated.vb and never
    ''' reads this one, so anything added here survives the page being regenerated.
    '''
    ''' To reach into the generated half, implement OnPageBuilt - it is declared at the
    ''' end of that file and called by the constructor below.
    ''' </summary>
    Partial Public Class FW_Employees_B
        Inherits FW_Base_B

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, GeneratedTableName)
            OnPageBuilt()
        End Sub

    End Class
End Namespace
