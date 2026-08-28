Option Strict On
Option Explicit On

Imports System.Drawing

Namespace HelloWorld
    Public NotInheritable Class DashboardGridLayout
        Private Sub New()
        End Sub

        Public Const FirstColumnLeft As Integer = 70
        Public Const ColumnGap As Integer = 150
        Public Const FirstRowTop As Integer = 105
        Public Const RowGap As Integer = 130
        Public Const StandardClientWidth As Integer = 820
        Public Const StandardClientHeight As Integer = 560

        Public Shared Function CellLeft(column As Integer) As Integer
            If column < 1 Then Throw New ArgumentOutOfRangeException(NameOf(column))
            Return FirstColumnLeft + ((column - 1) * ColumnGap)
        End Function

        Public Shared Function CellTop(row As Integer, Optional firstRowTop As Integer = FirstRowTop) As Integer
            If row < 1 Then Throw New ArgumentOutOfRangeException(NameOf(row))
            Return firstRowTop + ((row - 1) * RowGap)
        End Function

        Public Shared Function CellLocation(row As Integer, column As Integer, Optional firstRowTop As Integer = FirstRowTop) As Point
            Return New Point(CellLeft(column), CellTop(row, firstRowTop))
        End Function
    End Class
End Namespace
