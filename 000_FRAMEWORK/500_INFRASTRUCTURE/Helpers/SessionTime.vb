Option Strict On
Option Explicit On

Imports System.Data
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

        ''' <summary>
        ''' The viewer's wall clock now. Not DateTime.Now, which is the clock of whatever machine
        ''' runs the application - under Thinfinity, the server's, whoever is looking.
        ''' </summary>
        Public Shared Function Now() As DateTime
            Return ToSessionZone(DateTime.UtcNow)
        End Function

        ''' <summary>
        ''' The viewer's today. Near midnight it can differ from the server's, and a licence that
        ''' expires today, or a date picker's default, should answer for the person looking.
        ''' </summary>
        Public Shared Function Today() As Date
            Return Now().Date
        End Function

        ''' <summary>
        ''' A time the viewer typed, in their zone, as the UTC instant to store. The inverse of
        ''' ToSessionZone; with no zone on the session it is returned unchanged.
        ''' </summary>
        Public Shared Function ToUtc(sessionLocal As DateTime) As DateTime
            Dim zone = ResolveZone()
            If zone Is Nothing Then Return sessionLocal

            Return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(sessionLocal, DateTimeKind.Unspecified), zone)
        End Function

        ''' <summary>
        ''' Whether a column holds an instant - stored UTC, shown in the viewer's zone.
        '''
        ''' ONE_CLOCK_SPEC.md section 4: a column ending in Date is a calendar date and a column
        ''' ending in Local is a wall-clock time, and neither is ever converted. By name, because a
        ''' result set cannot say which it is - date, datetime and datetime2 all arrive as DateTime.
        ''' </summary>
        Public Shared Function IsInstantColumn(columnName As String) As Boolean
            Dim name = If(columnName, String.Empty).Trim()
            If name = String.Empty Then Return False

            Return Not name.EndsWith("Date", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not name.EndsWith("Local", StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Converts every instant column of a fetched table to the viewer's zone, in place. Date and
        ''' wall-clock columns are left exactly as stored. The one conversion the browse grid and Hot
        ''' Fields share, so a record cannot read one time in the grid and another beside it.
        ''' </summary>
        Public Shared Sub ConvertInstantColumns(table As DataTable)
            If table Is Nothing OrElse table.Rows.Count = 0 Then Return
            If ResolveZone() Is Nothing Then Return

            Dim converted = False

            For Each column As DataColumn In table.Columns
                If column.DataType IsNot GetType(DateTime) OrElse Not IsInstantColumn(column.ColumnName) Then Continue For

                Dim wasReadOnly = column.ReadOnly
                column.ReadOnly = False

                For Each row As DataRow In table.Rows
                    If row.IsNull(column) Then Continue For
                    row(column) = ToSessionZone(CDate(row(column)))
                    converted = True
                Next

                column.ReadOnly = wasReadOnly
            Next

            ' A table with nothing to convert is left exactly as fetched.
            If converted Then table.AcceptChanges()
        End Sub

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
