Option Strict On
Option Explicit On

Imports System.Collections.Generic

Namespace SDC.Framework

    ''' <summary>
    ''' Holds one login's page zooms for the life of the session.
    '''
    ''' Keyed on the user, not the employee: a contractor has a login and no employee row, and
    ''' keying on FW_Employees left them with nowhere to store anything.
    '''
    ''' Read once, at login: every page opened afterwards answers from memory rather than asking
    ''' the database again. A page open is the wrong place for a round trip - there are dozens in a
    ''' session and the answer does not change between them.
    '''
    ''' Written only when somebody actually changed the zoom, and only as the page closes. Not on
    ''' Save: the zoom is not part of the record, so cancelling an edit must not discard how the
    ''' page was set up to be read, and a browse page has no Save to hang it on.
    '''
    ''' Nothing here is allowed to fail loudly. A remembered zoom is a convenience, and losing one
    ''' is never worth stopping a page from opening or closing.
    ''' </summary>
    Public NotInheritable Class PageZoomStore

        Private Sub New()
        End Sub

        Private Shared saved As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
        Private Shared currentUserId As Integer

        ''' <summary>Loads the whole of one login's zooms. Called once, after login.</summary>
        Public Shared Sub LoadForUser(userId As Integer)
            saved = New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
            currentUserId = userId

            Try
                saved = DataAccess.GetPageZooms(userId)
            Catch
                saved = New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
            End Try
        End Sub

        ''' <summary>The zoom stored for a page, or 1.0 where nothing was chosen.</summary>
        Public Shared Function FactorFor(pageName As String) As Single
            Dim factor As Single

            If String.IsNullOrWhiteSpace(pageName) Then Return 1.0F
            If Not saved.TryGetValue(pageName.Trim(), factor) Then Return 1.0F

            Return factor
        End Function

        ''' <summary>
        ''' Records a changed zoom. Does nothing when it matches what is already held, so a page
        ''' opened and closed without touching the keyboard costs no write at all.
        ''' </summary>
        Public Shared Sub Remember(pageName As String, factor As Single)
            If currentUserId <= 0 OrElse String.IsNullOrWhiteSpace(pageName) Then Return

            Dim page = pageName.Trim()
            If Math.Abs(FactorFor(page) - factor) < 0.001F Then Return

            Dim registrationId = 0
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                registrationId = SessionState.Current.Value.RegistrationID
            End If

            Try
                DataAccess.SavePageZoom(currentUserId, page, factor, registrationId)
                saved(page) = factor
            Catch telemetryEx As Exception
                Telemetry.Error(telemetryEx, "PageZoomStore.Remember")
            End Try
        End Sub

    End Class

End Namespace
