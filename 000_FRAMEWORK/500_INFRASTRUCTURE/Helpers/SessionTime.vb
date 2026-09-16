Option Strict On
Option Explicit On

Imports System.Globalization

Namespace SDC.Framework

    ''' <summary>
    ''' Turns a stored UTC time into the session's time zone.
    '''
    ''' The zone is the employee's where they have one, otherwise the registration's, resolved at
    ''' login and held on the session. With none set, or an id the machine does not recognise, the
    ''' value is returned unchanged rather than guessed at.
    ''' </summary>
    Public NotInheritable Class SessionTime

        Private Sub New()
        End Sub

        Public Shared Function ToSessionZone(utc As DateTime) As DateTime
            Dim zone = ResolveZone()
            If zone Is Nothing Then Return utc

            Return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
        End Function

        ''' <summary>The zone's short name, for labelling a converted time. Empty when there is none.</summary>
        Public Shared Function ZoneAbbreviation() As String
            Dim zone = ResolveZone()
            If zone Is Nothing Then Return String.Empty

            Dim name = If(zone.IsDaylightSavingTime(DateTime.UtcNow), zone.DaylightName, zone.StandardName)
            If String.IsNullOrWhiteSpace(name) Then Return String.Empty

            ' "Eastern Standard Time" -> "EST". A name with no spaces is used as it stands.
            Dim words = name.Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
            If words.Length < 2 Then Return name

            Dim initials = String.Empty
            For Each word In words
                initials &= Char.ToUpperInvariant(word(0))
            Next
            Return initials
        End Function

        Private Shared Function ResolveZone() As TimeZoneInfo
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return Nothing

            Dim id = If(SessionState.Current.Value.TimeZoneName, String.Empty).Trim()
            If id = String.Empty Then Return Nothing

            Try
                Return TimeZoneInfo.FindSystemTimeZoneById(id)
            Catch
                Return Nothing
            End Try
        End Function

    End Class

End Namespace
