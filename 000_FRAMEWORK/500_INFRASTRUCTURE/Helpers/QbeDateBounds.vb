Option Strict On
Option Explicit On

Imports System

Namespace SDC.Framework

    ''' <summary>
    ''' What a chosen day means when the column carries a time.
    '''
    ''' A search row offers a date and nothing else - no hour, no minute. A column declared
    ''' datetime holds both. A row saved at 16:40 is not equal to the day somebody picked, and
    ''' Equals on that day finds nothing at all. The fault looks like an empty result rather than
    ''' an error, and test data tends to sit at midnight, which is exactly where it does not show.
    '''
    ''' The answer is to turn every date comparison into a boundary comparison on the whole day:
    ''' Equals the 14th is "on or after the 14th and before the 15th". Nothing is truncated,
    ''' because CAST(column AS date) puts a function on the column - the same mistake as COLLATE
    ''' on a column - and because the expression language a browse page filters with has no
    ''' truncation function to call.
    '''
    ''' | Chosen                   | Means                        |
    ''' |--------------------------|------------------------------|
    ''' | Equals D                 | &gt;= D and &lt; D+1              |
    ''' | Not Equals D             | &lt; D or &gt;= D+1               |
    ''' | Greater Than D           | &gt;= D+1                      |
    ''' | Greater Than Or Equal D  | &gt;= D                        |
    ''' | Less Than D              | &lt; D                         |
    ''' | Less Than Or Equal D     | &lt; D+1                       |
    '''
    ''' Between is not here. It is expanded where the filters are built into the two comparisons
    ''' it already means - on or after the first day, on or before the last - and each half then
    ''' arrives here as an ordinary operator. Nothing below the search row learns a new word.
    '''
    ''' A column declared date rather than datetime answers all of these identically, because a
    ''' value with no time is already at midnight.
    ''' </summary>
    Public Module QbeDateBounds

        ''' <summary>
        ''' A day comparison as two boundaries: on or after <see cref="Lower"/>, and before
        ''' <see cref="Upper"/>. Either may be absent, meaning that side is open.
        '''
        ''' <see cref="Excluded"/> inverts it, which only Not Equals needs: everything outside the
        ''' chosen day rather than inside it.
        ''' </summary>
        Public Structure Bounds
            Public Property Lower As Date?
            Public Property Upper As Date?
            Public Property Excluded As Boolean
        End Structure

        ''' <summary>
        ''' The boundaries a chosen day and an operator come to.
        '''
        ''' The time on the value is discarded rather than honoured. A search row cannot express
        ''' one, and a stray time arriving from a saved search must not quietly narrow the day it
        ''' came from.
        '''
        ''' An operator that means nothing for a date - Contains, Starts With, Ends With - is read
        ''' as Equals. They are not offered on a date field, and a saved search holding one is
        ''' better answered with the day than refused.
        ''' </summary>
        Public Function Resolve(comparisonOperator As QbeComparisonOperator, value As Date) As Bounds
            Dim day = value.Date
            Dim nextDay = day.AddDays(1)

            Select Case comparisonOperator
                Case QbeComparisonOperator.NotEquals
                    Return New Bounds With {.Lower = day, .Upper = nextDay, .Excluded = True}

                Case QbeComparisonOperator.GreaterThan
                    Return New Bounds With {.Lower = nextDay, .Upper = Nothing}

                Case QbeComparisonOperator.GreaterThanOrEqual
                    Return New Bounds With {.Lower = day, .Upper = Nothing}

                Case QbeComparisonOperator.LessThan
                    Return New Bounds With {.Lower = Nothing, .Upper = day}

                Case QbeComparisonOperator.LessThanOrEqual
                    Return New Bounds With {.Lower = Nothing, .Upper = nextDay}

                Case Else
                    Return New Bounds With {.Lower = day, .Upper = nextDay}
            End Select
        End Function

        ''' <summary>
        ''' The literal a DataView row filter accepts, which is where most browse pages filter.
        '''
        ''' Sortable form, and measured rather than assumed: every candidate pattern was tried
        ''' under en-US, en-GB and de-DE on 2026-09-21 and all three agreed. The expression
        ''' parser does not read these through the current culture. This one is chosen because it
        ''' is also unambiguous to whoever reads the expression in an error message.
        ''' </summary>
        Private Const DataViewLiteralFormat As String = "yyyy-MM-ddTHH:mm:ss"

        ''' <summary>
        ''' The two boundaries as a DataView filter expression, or an empty string when there is
        ''' nothing to say.
        '''
        ''' A row holding no date matches neither side, which means Not Equals excludes it. That
        ''' matches what the text branch already does with &lt;&gt;, and what SQL would do.
        ''' </summary>
        Public Function ToDataViewExpression(fieldName As String, limits As Bounds) As String
            If String.IsNullOrWhiteSpace(fieldName) Then Return String.Empty

            Dim quoted = "[" & fieldName.Replace("]", "]]") & "]"
            Dim parts As New Collections.Generic.List(Of String)()

            If limits.Excluded Then
                If limits.Lower.HasValue Then parts.Add(quoted & " < " & Literal(limits.Lower.Value))
                If limits.Upper.HasValue Then parts.Add(quoted & " >= " & Literal(limits.Upper.Value))
                If parts.Count = 0 Then Return String.Empty
                Return "(" & String.Join(" OR ", parts) & ")"
            End If

            If limits.Lower.HasValue Then parts.Add(quoted & " >= " & Literal(limits.Lower.Value))
            If limits.Upper.HasValue Then parts.Add(quoted & " < " & Literal(limits.Upper.Value))
            If parts.Count = 0 Then Return String.Empty

            Return "(" & String.Join(" AND ", parts) & ")"
        End Function

        Private Function Literal(value As Date) As String
            Return "#" & value.ToString(DataViewLiteralFormat, Globalization.CultureInfo.InvariantCulture) & "#"
        End Function

        ''' <summary>
        ''' How a date is written into a filter value, and read back out of one.
        '''
        ''' ISO, always, whatever the company has chosen to see on screen. The pattern a
        ''' registration displays is a display decision and can be changed by an administrator;
        ''' a saved search written last year has to keep meaning the same day afterwards. It is
        ''' also the one form no language setting can read two ways, which is the whole problem
        ''' with sending a date anywhere as text.
        ''' </summary>
        Public Const FilterValueFormat As String = "yyyy-MM-dd"

        ''' <summary>Writes a date as a filter value.</summary>
        Public Function ToFilterValue(value As Date) As String
            Return value.Date.ToString(FilterValueFormat, Globalization.CultureInfo.InvariantCulture)
        End Function

        ''' <summary>
        ''' Reads a filter value back, accepting what an older saved search may hold.
        '''
        ''' ISO first, because that is what this writes. A value that is not ISO is tried against
        ''' the company's display pattern and then, last, against whatever the machine will parse -
        ''' a saved search made before dates were typed at all holds one of those, and refusing it
        ''' would lose somebody's saved search with nothing on screen to say why.
        ''' </summary>
        Public Function TryParseFilterValue(text As String, ByRef parsed As Date) As Boolean
            If String.IsNullOrWhiteSpace(text) Then Return False

            Dim raw = text.Trim()

            If Date.TryParseExact(raw, FilterValueFormat,
                                  Globalization.CultureInfo.InvariantCulture,
                                  Globalization.DateTimeStyles.None, parsed) Then
                Return True
            End If

            Dim displayPattern = DisplayFormats.DatePattern()
            If Not String.IsNullOrWhiteSpace(displayPattern) AndAlso
               Date.TryParseExact(raw, displayPattern,
                                  Globalization.CultureInfo.InvariantCulture,
                                  Globalization.DateTimeStyles.None, parsed) Then
                Return True
            End If

            Return Date.TryParse(raw, Globalization.CultureInfo.InvariantCulture,
                                 Globalization.DateTimeStyles.None, parsed)
        End Function

    End Module

End Namespace
