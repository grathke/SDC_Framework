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
            Return Date.Now.ToString(SafePattern(pattern, fallback), CultureInfo.InvariantCulture)
        End Function

    End Module

End Namespace
