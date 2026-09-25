Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text

Namespace SDC.Framework

    ''' <summary>A file value turned into what its target column holds, or the reason it could not be.</summary>
    Public NotInheritable Class ImportConversion
        ''' <summary>The typed value - String, Integer, Boolean, Date or Decimal - or DBNull for a blank.</summary>
        Public Property Value As Object = DBNull.Value

        ''' <summary>Empty when the value is usable.</summary>
        Public Property Problem As String = String.Empty

        Public ReadOnly Property IsBlank As Boolean
            Get
                Return Value Is Nothing OrElse Convert.IsDBNull(Value)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' The rules an import applies to a single value, without a database or a screen.
    '''
    ''' Kept apart from the page so they can be tested headlessly, which the page cannot be - and
    ''' these are exactly the rules that fail quietly: a user name that keeps an apostrophe, a
    ''' date read the wrong way round, a quoted comma that shifts a column.
    ''' </summary>
    Public Module ImportRules

        ''' <summary>Lower case, accents folded, letters and digits only - what a generated user name is built from.</summary>
        Public Function LettersAndDigits(text As String) As String
            Dim builder As New StringBuilder()
            For Each ch In FoldToAscii(If(text, String.Empty)).ToLowerInvariant()
                If Char.IsLetterOrDigit(ch) Then builder.Append(ch)
            Next
            Return builder.ToString()
        End Function

        ''' <summary>
        ''' Accents removed and letters kept: "Église" becomes "Eglise", not "glise".
        '''
        ''' Decomposing first splits an accented letter into its base and the accent, and the
        ''' accent is then a separate mark that can be dropped. Stripping non-ASCII without that
        ''' step would take the whole letter with it.
        ''' </summary>
        Public Function FoldToAscii(text As String) As String
            If String.IsNullOrEmpty(text) Then Return String.Empty

            Dim decomposed = text.Normalize(NormalizationForm.FormD)
            Dim builder As New StringBuilder(decomposed.Length)
            For Each ch In decomposed
                If CharUnicodeInfo.GetUnicodeCategory(ch) <> UnicodeCategory.NonSpacingMark Then
                    builder.Append(ch)
                End If
            Next

            Return builder.ToString().Normalize(NormalizationForm.FormC)
        End Function

        ''' <summary>
        ''' A user name made from a person's name: the first initial and the surname, lower case,
        ''' letters and digits only, numbered when taken.
        '''
        ''' Bob O'Hare is bohare, Ines Delacroix-Moreau is idelacroixmoreau, and the second Grace
        ''' Nolan is gnolan2. An apostrophe or hyphen left in a user name is one somebody has to
        ''' type exactly at every sign-in, and one that SQL written carelessly elsewhere trips on.
        '''
        ''' <paramref name="taken"/> is every name already in use - the database's and this file's
        ''' so far - lower case. The name returned is added to it, so the next row cannot pick it.
        ''' </summary>
        Public Function GenerateUserName(firstName As String,
                                         lastName As String,
                                         taken As HashSet(Of String),
                                         Optional maxLength As Integer = 50) As String
            Dim first = LettersAndDigits(firstName)
            Dim last = LettersAndDigits(lastName)

            Dim stem = If(first.Length > 0, first.Substring(0, 1), String.Empty) & last
            If stem = String.Empty Then stem = first
            If stem = String.Empty Then stem = "user"

            Return MakeUnique(stem, taken, maxLength)
        End Function

        ''' <summary>
        ''' The tokens a User Name pattern may use, in the order the page offers them, each with
        ''' what it means. One list, so the page's choices and its "more..." help cannot disagree
        ''' with what ExpandUserNamePattern actually does.
        ''' </summary>
        Public ReadOnly UserNameTokens As (Token As String, Meaning As String)() = {
            ("{F}", "first letter of the first name"),
            ("{First}", "the first name"),
            ("{L}", "first letter of the last name"),
            ("{Last}", "the last name"),
            ("{Email}", "the whole email address"),
            ("{EmailName}", "the email address before the @")
        }

        ''' <summary>Whether a User Name value is a pattern to expand rather than a name to use.</summary>
        Public Function IsUserNamePattern(value As String) As Boolean
            Return Not String.IsNullOrEmpty(value) AndAlso value.Contains("{"c) AndAlso value.Contains("}"c)
        End Function

        ''' <summary>
        ''' Expands a User Name pattern for one person: {F}{Last} is Bob O'Hare's bohare,
        ''' {First}.{Last} is bob.ohare, {EmailName} is whatever came before the @. Names lose
        ''' their accents, apostrophes and spaces exactly as a generated name does; text outside
        ''' the braces is kept as written; tokens are matched in any case. An unknown token is left
        ''' in place, so a typing mistake shows in the result rather than silently vanishing.
        ''' </summary>
        Public Function ExpandUserNamePattern(pattern As String, firstName As String, lastName As String, email As String) As String
            Dim first = LettersAndDigits(firstName)
            Dim last = LettersAndDigits(lastName)
            Dim address = If(email, String.Empty).Trim().ToLowerInvariant()
            Dim at = address.IndexOf("@"c)
            Dim emailName = If(at > 0, address.Substring(0, at), address)

            Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
                {"{F}", If(first.Length > 0, first.Substring(0, 1), String.Empty)},
                {"{First}", first},
                {"{L}", If(last.Length > 0, last.Substring(0, 1), String.Empty)},
                {"{Last}", last},
                {"{Email}", address},
                {"{EmailName}", emailName}
            }

            Dim result As New StringBuilder()
            Dim i = 0
            Dim text = If(pattern, String.Empty).Trim()
            While i < text.Length
                If text(i) = "{"c Then
                    Dim close = text.IndexOf("}"c, i)
                    If close > i Then
                        Dim token = text.Substring(i, close - i + 1)
                        Dim value As String = Nothing
                        result.Append(If(values.TryGetValue(token, value), value, token))
                        i = close + 1
                        Continue While
                    End If
                End If
                result.Append(text(i))
                i += 1
            End While

            Return result.ToString()
        End Function

        ''' <summary>
        ''' The user name a row ends up with: the one it supplied, or one made from its name - and
        ''' in either case numbered when it is already taken, rather than refused.
        '''
        ''' A supplied name is kept as written apart from its outer spaces: it may be an email
        ''' address, and the person may already know it. When it clashes, the number goes before
        ''' the @ so it still reads as the same address - grathke@sdcdev.net becomes
        ''' grathke2@sdcdev.net, not grathke@sdcdev.net2.
        ''' </summary>
        Public Function ResolveUserName(supplied As String,
                                        firstName As String,
                                        lastName As String,
                                        taken As HashSet(Of String),
                                        Optional maxLength As Integer = 50) As String
            Dim name = If(supplied, String.Empty).Trim()
            If name = String.Empty Then Return GenerateUserName(firstName, lastName, taken, maxLength)

            Return MakeUnique(name, taken, maxLength)
        End Function

        ''' <summary>
        ''' The name, or the name with 2, 3, ... added until nothing in <paramref name="taken"/>
        ''' has it. Compared lower case, because the login check is case-insensitive; returned in
        ''' the case it was given. The result is added to the set, so the next row cannot pick it.
        ''' </summary>
        Private Function MakeUnique(name As String, taken As HashSet(Of String), maxLength As Integer) As String
            Dim at = name.IndexOf("@"c)
            Dim head = If(at > 0, name.Substring(0, at), name)
            Dim tail = If(at > 0, name.Substring(at), String.Empty)

            ' Room for a number, so a long name that clashes still fits the column.
            Dim room = maxLength - tail.Length - 3
            If room > 0 AndAlso head.Length > room Then head = head.Substring(0, room)

            Dim candidate = head & tail
            Dim suffix = 2
            While taken IsNot Nothing AndAlso taken.Contains(candidate.ToLowerInvariant())
                candidate = head & suffix.ToString(CultureInfo.InvariantCulture) & tail
                suffix += 1
            End While

            taken?.Add(candidate.ToLowerInvariant())
            Return candidate
        End Function

        ''' <summary>
        ''' A six-digit PIN no other row in this import has, for a person the file gave no
        ''' password.
        '''
        ''' Unique within the import, and only there: every stored password is hashed on its own
        ''' UserId, so there is no way to ask whether somebody else already has a given PIN - and
        ''' no need to, because a password is never how an account is found.
        '''
        ''' From the cryptographic generator, and never below 100000, so it is always six digits
        ''' and survives a spreadsheet that would drop a leading zero.
        ''' </summary>
        Public Function GeneratePin(usedPins As HashSet(Of String)) As String
            Dim pin As String
            Do
                pin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture)
            Loop While usedPins IsNot Nothing AndAlso usedPins.Contains(pin)

            usedPins?.Add(pin)
            Return pin
        End Function

        ''' <summary>
        ''' Dates written the ways files actually write them. ISO first, because it cannot be
        ''' misread; then month-first, because the application is used in the United States and a
        ''' day-first file is the rarer mistake. 03/04/2026 is therefore March 4th. The pre-check
        ''' shows every date as yyyy-MM-dd once read, so a day-first file is caught by looking
        ''' rather than by luck - and a day above 12 fails outright rather than being swapped.
        ''' </summary>
        Private ReadOnly DateFormats As String() = {
            "yyyy-MM-dd", "yyyy-M-d", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ssK",
            "yyyy/MM/dd", "M/d/yyyy", "MM/dd/yyyy", "M/d/yyyy H:mm", "M-d-yyyy", "MM-dd-yyyy",
            "d MMM yyyy", "d MMMM yyyy", "MMM d, yyyy", "MMMM d, yyyy", "yyyyMMdd"
        }

        ''' <summary>Times as files write them: 24-hour, and 12-hour with AM/PM.</summary>
        Private ReadOnly TimeFormats As String() = {
            "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "h:mm tt", "h:mm:ss tt", "hh:mm tt", "h tt"
        }

        ''' <summary>"today" or "now" when the text is one of them, with or without "()", any case; empty otherwise.</summary>
        Private Function ClockWord(text As String) As String
            Dim word = text.Trim().ToLowerInvariant()
            If word.EndsWith("()", StringComparison.Ordinal) Then word = word.Substring(0, word.Length - 2).TrimEnd()
            Return If(word = "today" OrElse word = "now", word, String.Empty)
        End Function

        ''' <summary>
        ''' Converts one value for one column: blank becomes DBNull, text is length-checked, and
        ''' numbers, dates and yes/no values are parsed or reported.
        '''
        ''' Text keeps its inner spaces and loses its outer ones. A value that was only spaces is
        ''' blank - which is what it looks like on screen, and what somebody would expect a
        ''' required-field check to call it.
        '''
        ''' **Today and Now** - with or without "()", any case - follow the application's rule for
        ''' the kind of column (Glenn, 2026-09-24):
        '''
        ''' - A **date** column holds a calendar day with no zone, and nothing converts it on the way
        '''   to the screen - the day stored is the day everyone sees. Both words give the
        '''   session's day (the employee's zone, else the registration's). The server's UTC day
        '''   would date an evening import on the East Coast tomorrow, for good.
        ''' - A **date-and-time** or **time** column holds a moment, and moments are stored in
        '''   UTC (SYSUTCDATETIME(); GETDATE() was the server's local clock and is gone from the
        '''   writes - ONE_CLOCK_SPEC.md) and converted by
        '''   SessionTime.ToSessionZone when shown. Now is the UTC moment; Today is the session's
        '''   midnight expressed in UTC; a time column takes Now's UTC time and refuses Today.
        '''
        ''' Most useful as a default: Hire Date = Today.
        ''' </summary>
        ''' <param name="utcNow">The clock, in UTC. Nothing means the real one; tests pass a fixed one.</param>
        Public Function ConvertValue(text As String, sqlType As String, maxLength As Integer,
                                     Optional utcNow As DateTime? = Nothing) As ImportConversion
            Dim trimmed = If(text, String.Empty).Trim()
            If trimmed = String.Empty Then Return New ImportConversion()

            Select Case If(sqlType, String.Empty).ToLowerInvariant()
                Case "int", "smallint", "tinyint", "bigint"
                    Dim number As Long
                    If Long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, number) Then
                        Select Case sqlType.ToLowerInvariant()
                            Case "bigint" : Return New ImportConversion With {.Value = number}
                            Case "tinyint"
                                If number >= 0 AndAlso number <= 255 Then Return New ImportConversion With {.Value = CInt(number)}
                            Case "smallint"
                                If number >= Short.MinValue AndAlso number <= Short.MaxValue Then Return New ImportConversion With {.Value = CInt(number)}
                            Case Else
                                If number >= Integer.MinValue AndAlso number <= Integer.MaxValue Then Return New ImportConversion With {.Value = CInt(number)}
                        End Select
                        Return New ImportConversion With {.Problem = "'" & trimmed & "' is out of range"}
                    End If
                    Return New ImportConversion With {.Problem = "'" & trimmed & "' is not a whole number"}

                Case "decimal", "numeric", "money", "smallmoney", "float", "real"
                    Dim amount As Decimal
                    If Decimal.TryParse(trimmed, NumberStyles.Number Or NumberStyles.AllowCurrencySymbol,
                                        CultureInfo.InvariantCulture, amount) Then
                        Return New ImportConversion With {.Value = amount}
                    End If
                    Return New ImportConversion With {.Problem = "'" & trimmed & "' is not a number"}

                Case "bit"
                    Select Case trimmed.ToLowerInvariant()
                        Case "1", "true", "yes", "y", "t"
                            Return New ImportConversion With {.Value = True}
                        Case "0", "false", "no", "n", "f"
                            Return New ImportConversion With {.Value = False}
                    End Select
                    Return New ImportConversion With {.Problem = "'" & trimmed & "' is not yes or no"}

                Case "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset"
                    Dim word = ClockWord(trimmed)
                    If word <> String.Empty Then
                        Dim utc = If(utcNow, DateTime.UtcNow)
                        Dim local = SessionTime.ToSessionZone(utc)
                        If sqlType.ToLowerInvariant() = "date" Then
                            Return New ImportConversion With {.Value = local.Date}
                        End If
                        If word = "now" Then
                            Return New ImportConversion With {.Value = utc}
                        End If
                        ' The session's midnight, moved by the session's current offset from UTC.
                        Return New ImportConversion With {.Value = local.Date - (local - utc)}
                    End If

                    Dim parsed As Date
                    If Date.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture,
                                          DateTimeStyles.AllowWhiteSpaces, parsed) Then
                        Return New ImportConversion With {.Value = If(sqlType.ToLowerInvariant() = "date", parsed.Date, parsed)}
                    End If
                    Return New ImportConversion With {.Problem = "'" & trimmed & "' is not a date"}

                Case "time"
                    Dim word = ClockWord(trimmed)
                    If word = "now" Then
                        Return New ImportConversion With {.Value = If(utcNow, DateTime.UtcNow).TimeOfDay}
                    End If
                    If word = "today" Then
                        Return New ImportConversion With {.Problem = "Today is a date, and this field holds only a time - use Now"}
                    End If

                    Dim clockTime As DateTime
                    If DateTime.TryParseExact(trimmed, TimeFormats, CultureInfo.InvariantCulture,
                                              DateTimeStyles.AllowWhiteSpaces Or DateTimeStyles.NoCurrentDateDefault, clockTime) Then
                        Return New ImportConversion With {.Value = clockTime.TimeOfDay}
                    End If
                    Return New ImportConversion With {.Problem = "'" & trimmed & "' is not a time"}

                Case Else
                    If maxLength > 0 AndAlso maxLength <> Integer.MaxValue AndAlso trimmed.Length > maxLength Then
                        Return New ImportConversion With {
                            .Problem = "is " & trimmed.Length.ToString(CultureInfo.InvariantCulture) &
                                       " characters; the most it holds is " & maxLength.ToString(CultureInfo.InvariantCulture)
                        }
                    End If
                    Return New ImportConversion With {.Value = trimmed}
            End Select
        End Function
    End Module
End Namespace
