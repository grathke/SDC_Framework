Option Strict On
Option Explicit On

Imports System.ComponentModel
Imports System.Windows.Forms

Namespace HelloWorld

    ''' <summary>
    ''' A dashboard's picture-and-caption button.
    '''
    ''' One class rather than the private copy each dashboard used to declare: the drag controller
    ''' has to reach the type to stop a drag ending in a click, and two identical private classes
    ''' could not both be that type.
    ''' </summary>
    Public Class DashboardIconButton
        Inherits Button

        Protected Overrides ReadOnly Property ShowFocusCues As Boolean
            Get
                Return False
            End Get
        End Property

        ''' <summary>
        ''' Set while an icon is being dragged, and cleared by the click it swallows.
        '''
        ''' A button raises Click from its own OnMouseUp, before the MouseUp handlers run, so by
        ''' the time a drag knows it has finished the click has already happened. The only moment
        ''' early enough is when the drag starts - hence a flag set there and honoured here, rather
        ''' than a decision made on release.
        ''' </summary>
        <Browsable(False)>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SuppressNextClick As Boolean

        Protected Overrides Sub OnClick(e As EventArgs)
            If SuppressNextClick Then
                SuppressNextClick = False
                Return
            End If

            MyBase.OnClick(e)
        End Sub
    End Class
End Namespace
