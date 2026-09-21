Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Text
Imports System.Text.RegularExpressions

Namespace SDC.Framework

    ''' <summary>
    ''' Puts a browse page's own SQL inside a wrapper, so the criteria and the row cap can be
    ''' applied by SQL Server instead of by this machine.
    '''
    ''' **Why a wrapper and not an appended WHERE.** A page selects aliases -
    ''' G.[GenderDescription] AS [GenderID] - and SQL Server will not accept an alias in the WHERE
    ''' of the statement that defines it. Inside a wrapper every selected column is addressable as
    ''' q.[name], which is the name the search grid already knows it by.
    '''
    ''' **It declines far more readily than it tries.** Anything it does not fully understand comes
    ''' back as a decline with a reason, and the caller runs exactly what it ran before. A wrong
    ''' wrap is a page that will not open or, worse, a page that quietly returns the wrong rows;
    ''' a decline costs nothing but the optimisation. Every rule below is written to fail that way.
    '''
    ''' **It rewrites nothing except the ORDER BY, and only when it can prove the mapping.** An
    ''' ORDER BY inside a derived table is illegal, so it has to move outside - and outside,
    ''' ORDER BY E.[EmployeeID] is meaningless, because E does not exist there. Each term is
    ''' matched against the select list and rewritten to the output name it produced. A term that
    ''' cannot be matched declines the whole wrap rather than guessing.
    ''' </summary>
    Public Module BrowseSqlWrapper

        ''' <summary>The alias the wrapper gives the page's own query.</summary>
        Public Const InnerAlias As String = "q"

        Public Class WrapResult
            ''' <summary>True when <see cref="Sql"/> can be run in place of the original.</summary>
            Public Property Wrapped As Boolean
            Public Property Sql As String = String.Empty

            ''' <summary>Why it declined, for FW_FallbackUsageLog. Empty when it did not.</summary>
            Public Property DeclineReason As String = String.Empty

            ''' <summary>
            ''' The names the inner query produces, in order. The caller needs these to know
            ''' which predicates it may even ask for - there is no point asking to filter on
            ''' DeletedFlag if the page does not select it.
            ''' </summary>
            Public Property OutputNames As New List(Of String)()
        End Class

        ''' <summary>
        ''' Reads the select list without running anything, so a caller can decide which
        ''' predicates are available before it builds them.
        ''' </summary>
        Public Function TryReadOutputNames(innerSql As String, ByRef names As List(Of String)) As Boolean
            names = New List(Of String)()

            Dim body = If(innerSql, String.Empty).Trim()
            If body = String.Empty Then Return False

            Dim selectListEnd = IndexOfKeywordAtDepthZero(body, "FROM")
            If selectListEnd < 0 Then Return False

            Dim afterSelect = SelectListStart(body)
            If afterSelect < 0 OrElse afterSelect >= selectListEnd Then Return False

            Dim selectList = body.Substring(afterSelect, selectListEnd - afterSelect)

            For Each item In SplitAtDepthZero(selectList, ","c)
                Dim trimmed = item.Trim()
                If trimmed = String.Empty Then Return False

                Dim outputName = OutputNameOf(trimmed)
                If outputName = String.Empty Then Return False

                names.Add(outputName)
            Next

            Return names.Count > 0
        End Function

        ''' <summary>
        ''' Wraps the page SQL, or explains why it will not.
        ''' </summary>
        ''' <param name="topRows">
        ''' The row cap, or zero for none. Ask for one more than is to be shown: the caller knows
        ''' the list is longer than the cap only because it got one more row than it asked for,
        ''' and without that it can show ten rows and be unable to say whether there are eleven or
        ''' eleven thousand.
        ''' </param>
        ''' <param name="predicates">
        ''' Already-built predicate text, each referring to columns as q.[name] and carrying
        ''' parameter placeholders rather than values. This never sees a user's value.
        ''' </param>
        Public Function TryWrap(innerSql As String,
                                topRows As Integer,
                                predicates As IEnumerable(Of String)) As WrapResult
            Dim result As New WrapResult()

            Dim body = If(innerSql, String.Empty).Trim()
            If body = String.Empty Then
                result.DeclineReason = "no SQL"
                Return result
            End If

            ' One statement only. A batch cannot be made into a derived table, and anything
            ' carrying a semicolon may be two statements or a CTE terminator.
            If body.Contains(";") Then
                result.DeclineReason = "contains a statement separator"
                Return result
            End If

            ' A CTE has to sit above the outer SELECT, not inside a derived table. Solvable, and
            ' deliberately not solved: no page generates one today.
            If Regex.IsMatch(body, "^\s*WITH\b", RegexOptions.IgnoreCase) Then
                result.DeclineReason = "starts with a common table expression"
                Return result
            End If

            If Not Regex.IsMatch(body, "^\s*SELECT\b", RegexOptions.IgnoreCase) Then
                result.DeclineReason = "does not begin with SELECT"
                Return result
            End If

            ' Its own TOP would fight the wrapper's, and which one won would depend on the plan.
            If Regex.IsMatch(body, "^\s*SELECT\s+(DISTINCT\s+)?TOP\b", RegexOptions.IgnoreCase) Then
                result.DeclineReason = "already uses TOP"
                Return result
            End If

            Dim names As List(Of String) = Nothing
            If Not TryReadOutputNames(body, names) Then
                result.DeclineReason = "the select list could not be read"
                Return result
            End If

            ' A derived table may not have two columns of the same name. The unwrapped query can -
            ' a DataTable renames the second - so this is a case that works today and would fail
            ' only once wrapped.
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each name In names
                If Not seen.Add(name) Then
                    result.DeclineReason = "two selected columns are both named " & name
                    Return result
                End If
            Next

            result.OutputNames = names

            Dim orderByIndex = IndexOfKeywordAtDepthZero(body, "ORDER BY")
            Dim inner = body
            Dim outerOrderBy = String.Empty

            If orderByIndex >= 0 Then
                inner = body.Substring(0, orderByIndex).TrimEnd()
                Dim originalOrderBy = body.Substring(orderByIndex + "ORDER BY".Length).Trim()

                If Not TryRewriteOrderBy(originalOrderBy, body, names, outerOrderBy) Then
                    result.DeclineReason = "the ORDER BY could not be matched to the select list"
                    Return result
                End If
            ElseIf topRows > 0 Then
                ' TOP without an order is whichever rows the engine finds first, and that can
                ' differ between runs of the same query. The old path is no better - it keeps the
                ' first N of an arbitrary order - but it is at least the arbitrary order the page
                ' has always had, and changing which rows appear is not an optimisation.
                result.DeclineReason = "no ORDER BY, so TOP would not be deterministic"
                Return result
            End If

            Dim sql As New StringBuilder()
            sql.Append("SELECT ")
            If topRows > 0 Then
                sql.Append("TOP (").Append(topRows.ToString(Globalization.CultureInfo.InvariantCulture)).Append(") ")
            End If
            sql.Append(InnerAlias).Append(".* FROM (").AppendLine()
            sql.AppendLine(inner)
            sql.Append(") AS ").Append(InnerAlias)

            Dim wherePredicates = New List(Of String)()
            If predicates IsNot Nothing Then
                For Each predicate In predicates
                    If Not String.IsNullOrWhiteSpace(predicate) Then wherePredicates.Add(predicate.Trim())
                Next
            End If

            If wherePredicates.Count > 0 Then
                sql.AppendLine()
                sql.Append("WHERE ").Append(String.Join(" AND ", wherePredicates))
            End If

            If outerOrderBy <> String.Empty Then
                sql.AppendLine()
                sql.Append("ORDER BY ").Append(outerOrderBy)
            End If

            result.Wrapped = True
            result.Sql = sql.ToString()
            Return result
        End Function

        ''' <summary>
        ''' Turns ORDER BY E.[EmployeeID] ASC into ORDER BY q.[PK] ASC, or fails.
        '''
        ''' Matching is by the expression's text against the select list, because that is the only
        ''' evidence available without a parser: the item that produced an output name knows what
        ''' expression it came from. A term naming an output column directly is taken as itself.
        ''' </summary>
        Private Function TryRewriteOrderBy(originalOrderBy As String,
                                           body As String,
                                           names As List(Of String),
                                           ByRef rewritten As String) As Boolean
            rewritten = String.Empty
            If String.IsNullOrWhiteSpace(originalOrderBy) Then Return False

            Dim expressionToName = BuildExpressionMap(body, names)
            If expressionToName Is Nothing Then Return False

            Dim rebuilt As New List(Of String)()

            For Each term In SplitAtDepthZero(originalOrderBy, ","c)
                Dim trimmed = term.Trim()
                If trimmed = String.Empty Then Return False

                Dim direction = String.Empty
                Dim directionMatch = Regex.Match(trimmed, "\s+(ASC|DESC)\s*$", RegexOptions.IgnoreCase)
                If directionMatch.Success Then
                    direction = " " & directionMatch.Groups(1).Value.ToUpperInvariant()
                    trimmed = trimmed.Substring(0, directionMatch.Index).Trim()
                End If

                ' Anything past a plain column reference - an expression, a CASE, a function - is
                ' not matched by text with any confidence.
                If Not Regex.IsMatch(trimmed, "^(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)(\.(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*))?$") Then
                    Return False
                End If

                Dim key = NormalizeExpression(trimmed)
                Dim outputName As String = Nothing

                If expressionToName.TryGetValue(key, outputName) Then
                    rebuilt.Add(InnerAlias & ".[" & outputName & "]" & direction)
                    Continue For
                End If

                ' A term that is already an output name, such as ORDER BY LastName where the
                ' select list produced LastName.
                Dim bare = StripBrackets(trimmed)
                Dim matchedName = names.FirstOrDefault(Function(n) String.Equals(n, bare, StringComparison.OrdinalIgnoreCase))
                If matchedName IsNot Nothing Then
                    rebuilt.Add(InnerAlias & ".[" & matchedName & "]" & direction)
                    Continue For
                End If

                Return False
            Next

            If rebuilt.Count = 0 Then Return False

            rewritten = String.Join(", ", rebuilt)
            Return True
        End Function

        ''' <summary>
        ''' Each select item's source expression against the name it produces, so an ORDER BY term
        ''' written the way the select list writes it can be found.
        ''' </summary>
        Private Function BuildExpressionMap(body As String, names As List(Of String)) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            Dim selectListEnd = IndexOfKeywordAtDepthZero(body, "FROM")
            Dim afterSelect = SelectListStart(body)
            If selectListEnd < 0 OrElse afterSelect < 0 OrElse afterSelect >= selectListEnd Then Return Nothing

            Dim items = SplitAtDepthZero(body.Substring(afterSelect, selectListEnd - afterSelect), ","c)
            If items.Count <> names.Count Then Return Nothing

            For index = 0 To items.Count - 1
                Dim expression = ExpressionOf(items(index).Trim())
                If expression = String.Empty Then Continue For

                Dim key = NormalizeExpression(expression)
                If Not map.ContainsKey(key) Then map(key) = names(index)
            Next

            Return map
        End Function

        ''' <summary>The expression part of a select item, with any AS alias removed.</summary>
        Private Function ExpressionOf(item As String) As String
            Dim aliasMatch = Regex.Match(item, "\s+AS\s+(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.IgnoreCase)
            If aliasMatch.Success Then
                Return item.Substring(0, aliasMatch.Index).Trim()
            End If
            Return item.Trim()
        End Function

        ''' <summary>The name a select item produces, or empty when it cannot be determined.</summary>
        Private Function OutputNameOf(item As String) As String
            Dim aliasMatch = Regex.Match(item, "\s+AS\s+(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\s*$", RegexOptions.IgnoreCase)
            If aliasMatch.Success Then
                Return StripBrackets(aliasMatch.Groups(1).Value)
            End If

            ' No alias, so the name is the column's own - and only a plain column reference has
            ' one. SELECT a + b with no alias produces an unnamed column, which a derived table
            ' will not accept.
            Dim plain = Regex.Match(item.Trim(),
                                    "^(?:(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)\.)?(\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)$")
            If plain.Success Then
                Return StripBrackets(plain.Groups(2).Value)
            End If

            Return String.Empty
        End Function

        Private Function StripBrackets(text As String) As String
            Dim trimmed = If(text, String.Empty).Trim()
            If trimmed.StartsWith("[") AndAlso trimmed.EndsWith("]") AndAlso trimmed.Length >= 2 Then
                Return trimmed.Substring(1, trimmed.Length - 2)
            End If
            Return trimmed
        End Function

        ''' <summary>Brackets and spacing removed, so E.[EmployeeID] and E.EmployeeID are one key.</summary>
        Private Function NormalizeExpression(expression As String) As String
            Return Regex.Replace(If(expression, String.Empty), "[\[\]\s]", String.Empty)
        End Function

        ''' <summary>Where the select list begins, past SELECT and any DISTINCT.</summary>
        Private Function SelectListStart(body As String) As Integer
            Dim match = Regex.Match(body, "^\s*SELECT\s+(DISTINCT\s+)?", RegexOptions.IgnoreCase)
            If Not match.Success Then Return -1
            Return match.Index + match.Length
        End Function

        ''' <summary>
        ''' The first position of a keyword that is not inside brackets, parentheses or a string.
        '''
        ''' A scanner rather than a regular expression, because ORDER BY appears inside subqueries
        ''' and inside quoted text, and cutting the statement at the wrong one produces SQL that
        ''' still runs and returns different rows. That is the failure this whole class is written
        ''' to avoid.
        ''' </summary>
        Private Function IndexOfKeywordAtDepthZero(body As String, keyword As String) As Integer
            Dim depth = 0
            Dim inString = False
            Dim inBracket = False
            Dim index = 0

            While index < body.Length
                Dim ch = body(index)

                If inString Then
                    If ch = "'"c Then inString = False
                    index += 1
                    Continue While
                End If

                If inBracket Then
                    If ch = "]"c Then inBracket = False
                    index += 1
                    Continue While
                End If

                Select Case ch
                    Case "'"c
                        inString = True
                    Case "["c
                        inBracket = True
                    Case "("c
                        depth += 1
                    Case ")"c
                        depth -= 1
                    Case Else
                        If depth = 0 AndAlso StartsWithKeywordAt(body, index, keyword) Then
                            Return index
                        End If
                End Select

                index += 1
            End While

            Return -1
        End Function

        ''' <summary>
        ''' Whether the keyword starts here, as a whole word, allowing any run of whitespace
        ''' between the words of a two-word keyword.
        ''' </summary>
        Private Function StartsWithKeywordAt(body As String, index As Integer, keyword As String) As Boolean
            If index > 0 Then
                Dim before = body(index - 1)
                If Char.IsLetterOrDigit(before) OrElse before = "_"c Then Return False
            End If

            Dim position = index
            Dim words = keyword.Split(" "c)

            For wordIndex = 0 To words.Length - 1
                Dim word = words(wordIndex)
                If position + word.Length > body.Length Then Return False
                If String.Compare(body, position, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) <> 0 Then
                    Return False
                End If

                position += word.Length

                If wordIndex < words.Length - 1 Then
                    Dim spaces = 0
                    While position < body.Length AndAlso Char.IsWhiteSpace(body(position))
                        position += 1
                        spaces += 1
                    End While
                    If spaces = 0 Then Return False
                End If
            Next

            If position < body.Length Then
                Dim after = body(position)
                If Char.IsLetterOrDigit(after) OrElse after = "_"c Then Return False
            End If

            Return True
        End Function

        ''' <summary>Splits on a separator that is not inside parentheses, brackets or a string.</summary>
        Private Function SplitAtDepthZero(text As String, separator As Char) As List(Of String)
            Dim parts As New List(Of String)()
            Dim current As New StringBuilder()
            Dim depth = 0
            Dim inString = False
            Dim inBracket = False

            For Each ch In If(text, String.Empty)
                If inString Then
                    current.Append(ch)
                    If ch = "'"c Then inString = False
                    Continue For
                End If

                If inBracket Then
                    current.Append(ch)
                    If ch = "]"c Then inBracket = False
                    Continue For
                End If

                Select Case ch
                    Case "'"c
                        inString = True
                        current.Append(ch)
                    Case "["c
                        inBracket = True
                        current.Append(ch)
                    Case "("c
                        depth += 1
                        current.Append(ch)
                    Case ")"c
                        depth -= 1
                        current.Append(ch)
                    Case separator
                        If depth = 0 Then
                            parts.Add(current.ToString())
                            current.Clear()
                        Else
                            current.Append(ch)
                        End If
                    Case Else
                        current.Append(ch)
                End Select
            Next

            parts.Add(current.ToString())
            Return parts
        End Function

    End Module

End Namespace
