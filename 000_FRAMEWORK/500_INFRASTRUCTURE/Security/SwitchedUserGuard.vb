Option Strict On
Option Explicit On

Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Viewing as somebody else is for looking, not for changing.
    '''
    ''' An administrator who switches to another user does it to see what that person sees, usually
    ''' because they have reported something. Writing while switched was allowed briefly on
    ''' 2026-09-17, with a prompt saying the change would be recorded under the administrator's own
    ''' name, and then disallowed the same day: the feature is a window, and a window that also
    ''' edits is a window nobody can be sure about afterwards.
    '''
    ''' The stamping that came with that prompt stays, and matters more now rather than less. It is
    ''' what guarantees that anything written while switched - by a path this guard does not cover,
    ''' or one written later - still names the administrator rather than the person being viewed.
    ''' This guard stops the write; SessionState.ActingUserID makes sure that a write which somehow
    ''' happens is at least honest about who made it.
    '''
    ''' One owner for the rule and one wording for the message, because a refusal that is phrased
    ''' three ways reads as three different rules.
    ''' </summary>
    Public NotInheritable Class SwitchedUserGuard

        Private Sub New()
        End Sub

        ''' <summary>
        ''' How to get back, in every message that refuses something while switched. One copy, so
        ''' a rewording reaches all of them - it was written out twice until 2026-09-25, and the
        ''' first rewording found the second only by searching.
        ''' </summary>
        Public Const ReturnInstruction As String = "RETURN AS YOURSELF FROM THE ROLE BUTTON FIRST."

        ''' <summary>
        ''' True when the caller may write. False, having said why, while viewing as another user.
        '''
        ''' <paramref name="action"/> completes the sentence "you cannot ... while viewing as
        ''' another user" - "save this record", "delete this record".
        ''' </summary>
        Public Shared Function AllowWrite(owner As IWin32Window, action As String) As Boolean
            If Not SwitchedUser.IsActive OrElse SwitchedUser.Original Is Nothing Then
                Return True
            End If

            Dim viewed = SessionState.Current
            Dim viewedName = If(viewed.HasValue, viewed.Value.FirstLast, String.Empty)

            Dim message = "YOU CANNOT " & If(String.IsNullOrWhiteSpace(action), "CHANGE ANYTHING", action.Trim().ToUpperInvariant()) &
                          " WHILE VIEWING AS " & If(String.IsNullOrWhiteSpace(viewedName), "ANOTHER USER", viewedName.ToUpperInvariant()) & "." &
                          Environment.NewLine & Environment.NewLine &
                          ReturnInstruction

            MessageBox.Show(owner, message, "Viewing As Another User", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return False
        End Function
    End Class
End Namespace
