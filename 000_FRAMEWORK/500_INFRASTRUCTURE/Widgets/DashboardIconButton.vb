Option Strict On
Option Explicit On

Imports System.ComponentModel

Namespace SDC.Framework

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

        ''' <summary>
        ''' The page this icon opens, or nothing.
        '''
        ''' Set only on a generated icon, by the generator. ActionCaptionOverrides re-captions those
        ''' from the page's caption; an icon without one keeps the caption whoever asked for the
        ''' button chose, which is the final word.
        '''
        ''' Carried on the button rather than in a list the dashboard keeps, so adding a generated
        ''' icon means writing one more property into the block the generator already writes -
        ''' instead of also extending a collection somewhere else in the file. Two places to edit is
        ''' how a generated button ends up in one of them and not the other.
        '''
        ''' A one-off icon leaves it empty and keeps the caption whoever asked for the button chose.
        '''
        ''' Hidden from designer serialization because these dashboards are built in code, not in the
        ''' designer. Without saying so the WinForms analyzer fails the build on WFO1000, which is
        ''' asking a fair question of a property it assumes a designer will have to persist.
        ''' </summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property PageName As String
    End Class
End Namespace
