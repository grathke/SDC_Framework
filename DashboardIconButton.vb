Option Strict On
Option Explicit On

Namespace HelloWorld

    ''' <summary>
    ''' A dashboard's picture-and-caption button.
    '''
    ''' One class rather than the private copy each dashboard used to declare: the drag controller
    ''' has to reach the type to stop a drag ending in a click, and two identical private classes
    ''' could not both be that type.
    '''
    ''' The suppressed click and the hidden focus rectangle now come from SuppressClickButton, which
    ''' the main menu's ribbon tiles share.
    ''' </summary>
    Public Class DashboardIconButton
        Inherits SuppressClickButton
    End Class
End Namespace
