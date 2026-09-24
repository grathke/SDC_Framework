Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json

Namespace SDC.Framework

    ''' <summary>
    ''' Reads a JSON file of records into the same table of text the CSV reader returns.
    '''
    ''' Accepts the two shapes exports actually come in: an array of objects, or an object holding
    ''' one - {"employees": [...]} - where the first array of objects found is taken. Anything else
    ''' is refused by name rather than guessed at.
    '''
    ''' **Nested objects are flattened to dotted names** - {"address": {"city": "Mobile"}} becomes
    ''' a column called address.city - because the mapping grid offers columns, and a nested
    ''' object offered whole could only ever be mapped to nothing. An array inside a record is kept
    ''' as its JSON text: there is no one column it belongs in, and the pre-check will show it
    ''' beside the field it was mapped to if somebody maps it anyway.
    '''
    ''' Columns are the union across every record, in first-seen order. A record missing a key is
    ''' blank there, which is the same thing a short CSV line becomes.
    ''' </summary>
    Public Module ImportJsonReader

        Public Function Read(data As Byte()) As ImportSource
            If data Is Nothing OrElse data.Length = 0 Then
                Return ImportSource.Failed("That file is empty.")
            End If

            Try
                Using document = JsonDocument.Parse(data, New JsonDocumentOptions With {
                    .AllowTrailingCommas = True,
                    .CommentHandling = JsonCommentHandling.Skip
                })
                    Dim records = FindRecords(document.RootElement)
                    If Not records.HasValue Then
                        Return ImportSource.Failed("That file holds no list of records. " &
                                                   "Expected an array of objects, or an object containing one.")
                    End If

                    Dim flattened As New List(Of Dictionary(Of String, String))()
                    Dim columnOrder As New List(Of String)()
                    Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

                    For Each element In records.Value.EnumerateArray()
                        If element.ValueKind <> JsonValueKind.Object Then Continue For

                        Dim values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                        Flatten(element, String.Empty, values)
                        flattened.Add(values)

                        For Each key In values.Keys
                            If seen.Add(key) Then columnOrder.Add(key)
                        Next
                    Next

                    If flattened.Count = 0 Then
                        Return ImportSource.Failed("That file has no records in it.")
                    End If

                    Dim table As New DataTable("Source")
                    For Each name In columnOrder
                        table.Columns.Add(name, GetType(String))
                    Next

                    For Each values In flattened
                        Dim row = table.NewRow()
                        For Each column As DataColumn In table.Columns
                            Dim value As String = Nothing
                            row(column) = If(values.TryGetValue(column.ColumnName, value), value, String.Empty)
                        Next
                        table.Rows.Add(row)
                    Next

                    Return New ImportSource With {
                        .Rows = table,
                        .FormatName = "JSON",
                        .Delimiter = "n/a",
                        .EncodingName = "UTF-8"
                    }
                End Using
            Catch ex As JsonException
                Telemetry.Error(ex, "ImportJsonReader.Read")
                Dim where = If(ex.LineNumber.HasValue,
                               " at line " & (ex.LineNumber.Value + 1).ToString(CultureInfo.InvariantCulture),
                               String.Empty)
                Return ImportSource.Failed("That file is not valid JSON" & where & ".")
            Catch ex As Exception
                Telemetry.Error(ex, "ImportJsonReader.Read")
                Return ImportSource.Failed("The file could not be read: " & ex.Message)
            End Try
        End Function

        ''' <summary>The root when it is an array, else the first property that is an array of objects.</summary>
        Private Function FindRecords(root As JsonElement) As JsonElement?
            If root.ValueKind = JsonValueKind.Array Then Return root

            If root.ValueKind = JsonValueKind.Object Then
                For Each prop In root.EnumerateObject()
                    If prop.Value.ValueKind = JsonValueKind.Array AndAlso
                       prop.Value.EnumerateArray().Any(Function(e) e.ValueKind = JsonValueKind.Object) Then
                        Return prop.Value
                    End If
                Next
            End If

            Return Nothing
        End Function

        Private Sub Flatten(element As JsonElement, prefix As String, values As Dictionary(Of String, String))
            For Each prop In element.EnumerateObject()
                Dim name = If(prefix = String.Empty, prop.Name, prefix & "." & prop.Name)

                Select Case prop.Value.ValueKind
                    Case JsonValueKind.Object
                        Flatten(prop.Value, name, values)
                    Case JsonValueKind.Null, JsonValueKind.Undefined
                        values(name) = String.Empty
                    Case JsonValueKind.String
                        values(name) = prop.Value.GetString()
                    Case JsonValueKind.True
                        values(name) = "true"
                    Case JsonValueKind.False
                        values(name) = "false"
                    Case Else
                        ' Numbers keep their text exactly as written - 36602 stays 36602, and a
                        ' zip of 01234 written as a string stays a string. Arrays keep their JSON.
                        values(name) = prop.Value.GetRawText()
                End Select
            Next
        End Sub
    End Module
End Namespace
