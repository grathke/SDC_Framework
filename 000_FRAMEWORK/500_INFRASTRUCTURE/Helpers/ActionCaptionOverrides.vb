Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
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
        ''' Re-captions each button from the role's alias for its page's table.
        '''
        ''' A button whose page has no rename recorded is left exactly as it was, so the caller's
        ''' coded caption stands. Pass only buttons that open a table-backed page: a dashboard tile
        ''' that opens another dashboard, or a configuration dialog, is not a table under another
        ''' name and has nothing to resolve.
        ''' </summary>
        Public Sub Apply(buttonPages As IEnumerable(Of KeyValuePair(Of ButtonBase, String)))
            If buttonPages Is Nothing Then
                Return
            End If

            Dim session = SessionState.Current
            Dim registrationId = If(session.HasValue, session.Value.RegistrationID, 0)
            If registrationId <= 0 Then
                Return
            End If

            For Each pair In buttonPages
                If pair.Key Is Nothing OrElse String.IsNullOrWhiteSpace(pair.Value) Then
                    Continue For
                End If

                Try
                    Dim renamed = PageTitleHelper.ResolvePageRename(registrationId, pair.Value)
                    If Not String.IsNullOrWhiteSpace(renamed) Then
                        pair.Key.Text = renamed
                    End If
                Catch
                    ' A caption is not worth a broken dashboard. The button keeps the wording it was
                    ' given, which is what every session saw before this existed.
                End Try
            Next
        End Sub

    End Module

End Namespace
