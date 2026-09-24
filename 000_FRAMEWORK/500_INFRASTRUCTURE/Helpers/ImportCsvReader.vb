Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Microsoft.VisualBasic.FileIO

Namespace SDC.Framework

    ''' <summary>
    ''' Reads a delimited file into a table of text.
    '''
    ''' **Through TextFieldParser rather than anything hand-written.** A quoted field may contain
    ''' the delimiter, and it may contain a line break, and both are ordinary CSV. A naive split
    ''' breaks on either - not loudly, but by shifting every later column one place left, which is
    ''' how a phone number ends up in the town field and nobody notices until somebody rings it.
    ''' A line break inside quotes is worse still: it turns one person into two rows, the second of
    ''' which looks like a row with no name.
    '''
    ''' It guesses the delimiter and the encoding, and reports both, because a guess the user
    ''' cannot see is one they cannot correct.
    '''
    ''' **It reads bytes, not a path.** The file arrives from the session's picker as bytes - under
    ''' Thinfinity it was uploaded from the browser and the server copy is already deleted - and
    ''' writing it back to disk only to read it again would leave a copy of somebody's staff list
    ''' on the server whenever the read failed half way.
    ''' </summary>
    Public Module ImportCsvReader

        ''' <summary>
        ''' The delimiters worth guessing between, in the order they are tried.
        '''
        ''' Comma first because it is the common case, then semicolon - which is what a spreadsheet
        ''' saves where the comma is the decimal separator, and the commonest reason a European
        ''' export opens as one enormous column.
        ''' </summary>
        Private ReadOnly CandidateDelimiters As String() = {",", ";", vbTab, "|"}

        ''' <summary>
        ''' Reads the file.
        ''' </summary>
        ''' <param name="delimiterOverride">
        ''' What the user chose, or empty to guess. A guess is right nearly always and wrong
        ''' memorably, so the form offers the override rather than insisting it knows.
        ''' </param>
        Public Function Read(data As Byte(),
                             hasHeaderRow As Boolean,
                             Optional delimiterOverride As String = "") As ImportSource

            If data Is Nothing OrElse data.Length = 0 Then
                Return ImportSource.Failed("That file is empty.")
            End If

            Try
                Dim source As New ImportSource With {.FormatName = "CSV"}
                Dim encoding = DetectEncoding(data)
                source.EncodingName = encoding.WebName.ToUpperInvariant()

                Dim text = Decode(data, encoding)

                Dim delimiter = If(String.IsNullOrEmpty(delimiterOverride),
                                   DetectDelimiter(text),
                                   delimiterOverride)
                source.Delimiter = If(delimiter = vbTab, "Tab", delimiter)

                Dim table As New DataTable("Source")

                Using parser As New TextFieldParser(New StringReader(text))
                    parser.TextFieldType = FieldType.Delimited
                    parser.SetDelimiters(delimiter)

                    ' The whole reason this class exists. Without it a quoted comma splits the row.
                    parser.HasFieldsEnclosedInQuotes = True

                    ' Left alone deliberately. Trimming belongs to validation, where a value that
                    ' was only spaces can be reported as blank rather than silently becoming so.
                    parser.TrimWhiteSpace = False

                    Dim isFirstRow = True

                    While Not parser.EndOfData
                        Dim fields = parser.ReadFields()
                        If fields Is Nothing Then Continue While

                        ' A wholly blank line - the trailing one most editors leave - is not a
                        ' person with no name.
                        If fields.All(Function(f) String.IsNullOrWhiteSpace(f)) Then Continue While

                        If isFirstRow Then
                            isFirstRow = False
                            AddColumns(table, fields, hasHeaderRow)
                            If hasHeaderRow Then Continue While
                        End If

                        AddRow(table, fields)
                    End While
                End Using

                If table.Columns.Count = 0 Then
                    Return ImportSource.Failed("That file has no rows in it.")
                End If

                source.Rows = table
                Return source

            Catch ex As MalformedLineException
                ' Named separately because it says which line, and that is the whole of what
                ' somebody needs to fix it.
                Telemetry.Error(ex, "ImportCsvReader.Read")
                Return ImportSource.Failed("Line " & ex.LineNumber.ToString(CultureInfo.InvariantCulture) &
                                           " could not be read. A quote is probably opened and not closed.")
            Catch ex As Exception
                Telemetry.Error(ex, "ImportCsvReader.Read")
                Return ImportSource.Failed("The file could not be read: " & ex.Message)
            End Try
        End Function

        ''' <summary>
        ''' A guess at whether the file's first row is headings, for the tick box to start on. The
        ''' user sees the grid and can change it; a guess only has to be right more often than not.
        ''' </summary>
        Public Function FirstRowLooksLikeHeadings(data As Byte(), Optional delimiterOverride As String = "") As Boolean
            Dim raw = Read(data, hasHeaderRow:=False, delimiterOverride:=delimiterOverride)
            Return raw.Loaded AndAlso LooksLikeHeadingRow(raw.Rows)
        End Function

        ''' <summary>
        ''' Whether row 0 of a table read without headings reads as headings.
        '''
        ''' No: any cell of it is a number or a date - "36602", "2026-01-15" - because a heading is
        ''' a name, and a person's zip code in the top row means the top row is a person. No: two
        ''' cells alike, or a cell that turns up again as a value lower in the same column.
        ''' Yes: every cell filled with distinct text. Otherwise yes only on evidence - a column
        ''' whose top cell is text while the rows below are numbers or dates.
        ''' </summary>
        Public Function LooksLikeHeadingRow(table As DataTable) As Boolean
            If table Is Nothing OrElse table.Rows.Count = 0 OrElse table.Columns.Count = 0 Then Return False

            Dim first = table.Columns.Cast(Of DataColumn)().
                                      Select(Function(c) Convert.ToString(table.Rows(0)(c), CultureInfo.InvariantCulture).Trim()).
                                      ToList()
            Dim filled = first.Where(Function(t) t <> String.Empty).ToList()
            If filled.Count = 0 Then Return False

            If filled.Any(Function(t) IsNumberOrDate(t)) Then Return False
            If filled.Distinct(StringComparer.OrdinalIgnoreCase).Count() <> filled.Count Then Return False

            Dim sample = Math.Min(table.Rows.Count - 1, 50)
            Dim textOverNumbers = False
            For c = 0 To table.Columns.Count - 1
                If first(c) = String.Empty Then Continue For

                Dim below = Enumerable.Range(1, sample).
                                       Select(Function(r) Convert.ToString(table.Rows(r)(c), CultureInfo.InvariantCulture).Trim()).
                                       Where(Function(t) t <> String.Empty).ToList()
                If below.Any(Function(t) String.Equals(t, first(c), StringComparison.OrdinalIgnoreCase)) Then Return False
                If below.Count > 0 AndAlso below.All(Function(t) IsNumberOrDate(t)) Then textOverNumbers = True
            Next

            Return filled.Count = first.Count OrElse textOverNumbers
        End Function

        Private Function IsNumberOrDate(text As String) As Boolean
            Return ImportRules.ConvertValue(text, "decimal", 0).Problem = String.Empty OrElse
                   ImportRules.ConvertValue(text, "date", 0).Problem = String.Empty
        End Function

        ''' <summary>
        ''' Column names from the header row, or Column1..ColumnN when there is none.
        '''
        ''' Blank and repeated headings both happen in real exports and both would throw when the
        ''' table is built, so a blank becomes its position and a repeat is suffixed. Renaming
        ''' visibly beats refusing to open the file: the mapping grid shows the name it ended up
        ''' with beside a sample value, so a renamed column is obvious rather than mysterious.
        ''' </summary>
        Private Sub AddColumns(table As DataTable, fields As String(), hasHeaderRow As Boolean)
            For i = 0 To fields.Length - 1
                Dim name = If(hasHeaderRow, If(fields(i), String.Empty).Trim(), String.Empty)
                If name = String.Empty Then name = "Column" & (i + 1).ToString(CultureInfo.InvariantCulture)

                Dim unique = name
                Dim suffix = 2
                While table.Columns.Contains(unique)
                    unique = name & " (" & suffix.ToString(CultureInfo.InvariantCulture) & ")"
                    suffix += 1
                End While

                table.Columns.Add(unique, GetType(String))
            Next
        End Sub

        ''' <summary>
        ''' Adds a row, tolerating a line with more or fewer fields than the header had.
        '''
        ''' A short line is padded and a long one gains columns for its extras. Refusing the file
        ''' would be defensible, but the pre-check can say what is wrong with the row far more
        ''' usefully than a parser can - and by then the row is on screen next to the message.
        ''' </summary>
        Private Sub AddRow(table As DataTable, fields As String())
            While table.Columns.Count < fields.Length
                table.Columns.Add("Column" & (table.Columns.Count + 1).ToString(CultureInfo.InvariantCulture),
                                  GetType(String))
            End While

            Dim row = table.NewRow()
            For i = 0 To table.Columns.Count - 1
                row(i) = If(i < fields.Length, If(fields(i), String.Empty), String.Empty)
            Next

            table.Rows.Add(row)
        End Sub

        ''' <summary>
        ''' Guesses the delimiter by which candidate divides the sample lines most consistently.
        '''
        ''' Consistency rather than frequency: a column of addresses full of commas would win on a
        ''' count alone. A delimiter giving the same field count on every line is doing its job;
        ''' one giving a different count each time is a character that merely appears often.
        ''' </summary>
        Private Function DetectDelimiter(text As String) As String
            Dim lines = ReadSampleLines(text, 5)
            If lines.Count = 0 Then Return ","

            Dim best = ","
            Dim bestScore = -1

            For Each candidate In CandidateDelimiters
                Dim counts = lines.Select(Function(l) CountOutsideQuotes(l, candidate)).ToList()
                If counts(0) = 0 Then Continue For

                Dim consistent = counts.All(Function(c) c = counts(0))
                Dim score = If(consistent, 1000, 0) + counts(0)

                If score > bestScore Then
                    bestScore = score
                    best = candidate
                End If
            Next

            Return best
        End Function

        ''' <summary>
        ''' Counts a delimiter only where it is not inside quotes.
        '''
        ''' Without this, "14 Mill Lane, Apt 3" votes twice for the comma in a file that is really
        ''' semicolon-separated - the guess then splits every address in half.
        ''' </summary>
        Private Function CountOutsideQuotes(line As String, delimiter As String) As Integer
            If String.IsNullOrEmpty(line) OrElse String.IsNullOrEmpty(delimiter) Then Return 0

            Dim count = 0
            Dim inQuotes = False
            Dim ch = delimiter(0)

            For i = 0 To line.Length - 1
                If line(i) = """"c Then
                    inQuotes = Not inQuotes
                ElseIf Not inQuotes AndAlso line(i) = ch Then
                    count += 1
                End If
            Next

            Return count
        End Function

        ''' <summary>
        ''' The first few non-blank lines. A line break inside quotes makes a "line" here half a
        ''' record, which is why the scoring tolerates inconsistency rather than requiring none.
        ''' </summary>
        Private Function ReadSampleLines(text As String, count As Integer) As List(Of String)
            Dim lines As New List(Of String)()

            Using reader As New StringReader(text)
                While lines.Count < count
                    Dim line = reader.ReadLine()
                    If line Is Nothing Then Exit While
                    If line.Trim() <> String.Empty Then lines.Add(line)
                End While
            End Using

            Return lines
        End Function

        ''' <summary>The text without its byte-order mark, which would otherwise open the first heading.</summary>
        Private Function Decode(data As Byte(), encoding As Encoding) As String
            Dim preambleLength = encoding.GetPreamble().Length
            If preambleLength > 0 AndAlso data.Length >= preambleLength AndAlso
               data.Take(preambleLength).SequenceEqual(encoding.GetPreamble()) Then
                Return encoding.GetString(data, preambleLength, data.Length - preambleLength)
            End If

            Return encoding.GetString(data)
        End Function

        ''' <summary>
        ''' The encoding from the byte-order mark, or UTF-8.
        '''
        ''' A file saved as ANSI by an older tool carries no mark and is not UTF-8, and the symptom
        ''' is a mangled accented name rather than an error. That is left visible on purpose: the
        ''' pre-check shows the value beside its row, so a name that came out wrong is seen before
        ''' anything is written - better than guessing at a codepage and being confidently wrong.
        ''' </summary>
        Private Function DetectEncoding(data As Byte()) As Encoding
            If data.Length >= 3 AndAlso data(0) = &HEF AndAlso data(1) = &HBB AndAlso data(2) = &HBF Then
                Return New UTF8Encoding(True)
            End If
            If data.Length >= 2 AndAlso data(0) = &HFF AndAlso data(1) = &HFE Then Return Encoding.Unicode
            If data.Length >= 2 AndAlso data(0) = &HFE AndAlso data(1) = &HFF Then Return Encoding.BigEndianUnicode

            Return New UTF8Encoding(False)
        End Function
    End Module
End Namespace
