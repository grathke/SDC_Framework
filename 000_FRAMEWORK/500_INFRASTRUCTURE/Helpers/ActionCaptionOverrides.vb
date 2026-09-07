Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Puts the role's name for a page onto the button that opens it.
    '''
    ''' One override, every surface. A company that renames Users to Staff in Roles_U sees "Staff" on
    ''' the ribbon tile, "Staff Listing" on the browse page, "Edit Staff" on the maintenance page and
    ''' "Staff" on the dashboard icon - all resolved from the single FW_RoleDetails.OverrideCaption
    ''' row, per role and registration. A setting that reaches three of four places reads as broken
    ''' rather than as partly built.
    '''
    ''' Shared because there is no dashboard base class: Dashboard_Application and Dashboard_Company
    ''' both inherit Form directly, so without this the same loop would be written twice and could
    ''' drift. FW_MainMenu has its own version rather than calling this one, because a ribbon tile
    ''' carries state a dashboard icon does not - see below.
    '''
    ''' Applies but never withdraws, which is right *here* and wrong for the ribbon. A dashboard is
    ''' constructed fresh every time it is opened, so a button's Text is its coded caption when this
    ''' runs and there is nothing to restore. The ribbon persists across role changes, so FW_MainMenu
    ''' keeps each tile's DefaultCaption and puts it back when an override is withdrawn.
    ''' </summary>
    Public Module ActionCaptionOverrides

        ''' <summary>
        ''' Re-captions each button from the role's alias for the table behind it.
        '''
        ''' **Generated icons only.** The generator knows the table - it is what the page was
        ''' generated from - so it writes it in and nothing is looked up at run time. A one-off icon
        ''' is captioned by whoever asked for the button, and rewriting that would overrule a
        ''' deliberate decision with a generic one.
        '''
        ''' A button whose table has no rename recorded is left exactly as it was.
        ''' </summary>
        Public Sub Apply(owner As Control)
            If owner Is Nothing Then
                Return
            End If

            Dim session = SessionState.Current
            Dim registrationId = If(session.HasValue, session.Value.RegistrationID, 0)
            If registrationId <= 0 Then
                Return
            End If

            For Each icon In owner.Controls.OfType(Of DashboardIconButton)()
                If String.IsNullOrWhiteSpace(icon.PageName) Then
                    Continue For
                End If

                ' One resolver, so an icon and the page it opens cannot be captioned by different
                ' rules. Passing the coded caption as the fallback keeps a button with nothing
                ' recorded exactly as it was written.
                icon.Text = PageTitleHelper.ResolveCaptionForPage(registrationId, icon.PageName, icon.Text)
            Next
        End Sub

    End Module

End Namespace
