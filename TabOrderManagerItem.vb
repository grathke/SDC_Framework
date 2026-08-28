Option Strict On
Option Explicit On

Imports System.Windows.Forms

Namespace HelloWorld
    Friend Class TabOrderManagerItem
        Public Property Control As Control
        Public Property DisplayName As String

        Public Overrides Function ToString() As String
            Return DisplayName
        End Function
    End Class
End Namespace
