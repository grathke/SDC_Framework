Option Strict On
Option Explicit On

Imports System.Globalization

Namespace SDC.Framework

    ''' <summary>
    ''' How this company writes a date and a time.
    '''
    ''' Read once at login into the session and answered from there, never queried per control:
    ''' a maintenance page with five date fields must not make five round trips to learn the same
    ''' thing five times.
    '''
    ''' One owner, because the same two patterns have to reach three unrelated places - a
    ''' DateTimePicker's CustomFormat, a grid column's DefaultCellStyle.Format, and ToString
    ''' wherever a date is written into text. Three copies of "what is the date format" is three
    ''' chances for a page to disagree with the grid that lists it.
    '''
    ''' Nothing here knows about time zones. A format says how to write an instant down; a zone
    ''' says which instant. They are separate questions and a date column has only the first -
    ''' a birth date is the same day everywhere.
    ''' </summary>
    Public Module DisplayFormats

        ''' <summary>
        ''' What a registration that has never chosen gets, and what a bad pattern falls back to.
        '''
        ''' Not CultureInfo.CurrentCulture. Under VirtualUI the current culture is the server's,
        ''' so deriving it would show every user in every country whatever the server happened to
        ''' be installed as - a format nobody picked, with no visible reason for being what it is.
        ''' </summary>
        Public Const DefaultDatePattern As String = "MM/dd/yyyy"
        Public Const DefaultTimePattern As String = "hh:mm tt"

        ''' <summary>The company's date pattern, or the default.</summary>
        Public Function DatePattern() As String
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return DefaultDatePattern
            Return SafePattern(SessionState.Current.Value.DateFormat, DefaultDatePattern)
        End Function

        ''' <summary>The company's time pattern, or the default.</summary>
        Public Function TimePattern() As String
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return DefaultTimePattern
            Return SafePattern(SessionState.Current.Value.TimeFormat, DefaultTimePattern)
        End Function

        ''' <summary>
        ''' The two joined, for a column that carries both.
        '''
        ''' Composed rather than stored as a third preference: a company that has chosen how it
        ''' writes a date and how it writes a time has already answered this, and a separate
        ''' setting is one more thing that can contradict the other two.
        ''' </summary>
        Public Function DateTimePattern() As String
            Return DatePattern() & " " & TimePattern()
        End Function

        ''' <summary>
        ''' A pattern that is known to work, or the fallback.
        '''
        ''' A format string is data here - somebody typed it into FW_Format_Date - and an invalid
        ''' one throws at every call site that formats a date. Falling back means one bad row
        ''' makes dates look wrong for a company; not falling back means it takes the application
        ''' down for them. The first is recoverable by looking at the screen.
        ''' </summary>
        Public Function SafePattern(pattern As String, fallback As String) As String
            If String.IsNullOrWhiteSpace(pattern) Then Return fallback

            Try
                ' A pattern of one character is read as a standard specifier rather than a custom
                ' one, which is a different language - "d" is the culture's short date, not the
                ' day. Nothing in the tables is one character, and treating it as custom is what
                ' the rest of the framework expects.
                If pattern.Trim().Length < 2 Then Return fallback

                Dim rendered = Date.Now.ToString(pattern.Trim(), CultureInfo.InvariantCulture)
                If String.IsNullOrWhiteSpace(rendered) Then Return fallback

                Return pattern.Trim()
            Catch
                Return fallback
            End Try
        End Function

        ''' <summary>
        ''' A pattern shown as what it produces - "03/15/2026" rather than "MM/dd/yyyy".
        '''
        ''' For the combos that choose one. The pattern means nothing to the person picking, and
        ''' a stored sample column would be a second statement of the same fact that could drift
        ''' from the pattern it describes.
        ''' </summary>
        Public Function SampleOf(pattern As String, fallback As String) As String
            Return SessionTime.Now().ToString(SafePattern(pattern, fallback), CultureInfo.InvariantCulture)
        End Function

        ''' <summary>
        ''' A time pattern shown at two fixed moments - 9:05:07 in the morning and 2:30:07 in the
        ''' afternoon. Not the current time: at 10:53 a pattern with the leading zero and one without
        ''' read the same, so two different choices would look like the same one twice. 9:05 shows
        ''' the zero and 2:30 PM shows 12-hour against 24-hour (Glenn, 2026-09-25).
        ''' </summary>
        Public Function TimeSamplesOf(pattern As String, fallback As String) As String
            Dim safe = SafePattern(pattern, fallback)
            Dim morning = New DateTime(2000, 1, 1, 9, 5, 7)
            Dim afternoon = New DateTime(2000, 1, 1, 14, 30, 7)
            Return morning.ToString(safe, CultureInfo.InvariantCulture) & "      " &
                   afternoon.ToString(safe, CultureInfo.InvariantCulture)
        End Function

        ''' <summary>
        ''' A pattern in the letters a person reads - MM/DD/YYYY, hh:mm AM - rather than .NET's.
        '''
        ''' Days and years in capitals, Mmm and Mmmm for month names, tt as AM. Minutes stay mm
        ''' and months MM, so the two cannot be confused. Derived from the stored pattern rather
        ''' than stored beside it, so the two cannot disagree.
        ''' </summary>
        Public Function ReadablePattern(pattern As String) As String
            Dim text = If(pattern, String.Empty).Trim()
            Dim result As New Text.StringBuilder()
            Dim index = 0

            While index < text.Length
                Dim ch = text(index)
                Dim run = 1
                While index + run < text.Length AndAlso text(index + run) = ch
                    run += 1
                End While

                Select Case ch
                    Case "y"c
                        result.Append(New String("Y"c, run))
                    Case "d"c
                        result.Append(If(run >= 3, "D" & New String("d"c, run - 1), New String("D"c, run)))
                    Case "M"c
                        result.Append(If(run >= 3, "M" & New String("m"c, run - 1), New String("M"c, run)))
                    Case "t"c
                        result.Append("AM")
                    Case Else
                        result.Append(ch, run)
                End Select

                index += run
            End While

            Return result.ToString()
        End Function

    End Module

End Namespace
