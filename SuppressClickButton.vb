Option Strict On
Option Explicit On

Imports System.ComponentModel
Imports System.Windows.Forms

Namespace HelloWorld

    ''' <summary>
    ''' A button that can have one click swallowed, and that does not paint a focus rectangle.
    '''
    ''' Both halves are wanted wherever an icon can be dragged: the dashboards' icons and the main
    ''' menu's ribbon tiles. Written once here rather than twice, because the second copy is how the
    ''' pair of them would drift apart.
    ''' </summary>
    Public Class SuppressClickButton
        Inherits Button

        Protected Overrides ReadOnly Property ShowFocusCues As Boolean
            Get
                Return False
            End Get
        End Property

        ''' <summary>
        ''' Set while a drag begins, and cleared by the one click it swallows.
        '''
        ''' A button raises Click from its own OnMouseUp, before the MouseUp handlers run, so by the
        ''' time a drag knows it has finished the click has already happened. The only moment early
        ''' enough is when the drag starts - hence a flag set there and honoured here, rather than a
        ''' decision made on release.
        '''
        ''' Exactly one click is swallowed, not every click afterwards. A tile that has been dragged
        ''' opens its page on the next click like any other.
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
