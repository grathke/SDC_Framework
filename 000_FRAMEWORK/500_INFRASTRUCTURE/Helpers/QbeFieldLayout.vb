Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text
Imports System.Text.Json

Namespace SDC.Framework

    ''' <summary>
    ''' The search panel's own arrangement: which fields a browse page offers in QBE, and in what
    ''' order, independently of the browse grid.
    '''
    ''' Until 2026-09-22 QBE was derived from the visible grid columns, so a field hidden to save
    ''' grid width stopped being searchable at the same time. The two are separate questions - a
    ''' page may want to search on a field it does not show - and this is the document that answers
    ''' the second one.
    '''
    ''' Stored in FW_TableLayouts beside the grid layouts, under LayoutType 'QbeDefault' with no
    ''' UserID, because the arrangement belongs to the registration rather than to whoever last
    ''' closed the page. One row per registration, page and table; an App Admin writes it and
    ''' everybody reads it.
    '''
    ''' The JSON is deliberately the same shape as a grid layout - an array of Key, DisplayIndex
    ''' and Visible - so both documents read alike and the same repair pass applies. Width has no
    ''' meaning here: the QBE grid sizes its own three columns.
    ''' </summary>
    Friend NotInheritable Class QbeFieldLayout

        Private Sub New()
        End Sub

        ''' <summary>The FW_TableLayouts LayoutType this document is stored under.</summary>
        Friend Const LayoutTypeName As String = "QbeDefault"

        ''' <summary>
        ''' The LayoutName the row carries. Not cosmetic: DataAccess.GetTableLayoutJson and
        ''' UpsertTableLayout special-case 'Default' and 'LastUsed' and require a name from every
        ''' other type, returning without touching the database when it is blank.
        ''' </summary>
        Friend Const LayoutRowName As String = "QBE Fields"

        Friend Class Entry
            Public Property FieldName As String
            Public Property Visible As Boolean
        End Class

        ''' <summary>
        ''' The saved arrangement, or an empty list when there is none, when it cannot be parsed,
        ''' or when it names no field the page still returns.
        '''
        ''' An empty list is the signal to fall back to the grid's own visible columns, which is
        ''' what every page did before this existed. A page with no saved QBE arrangement therefore
        ''' behaves exactly as it always has.
        ''' </summary>
        Friend Shared Function Parse(layoutJson As String) As List(Of Entry)
            Dim entries As New List(Of Entry)()

            Dim parseJson = DataAccess.NormalizeLayoutJsonForParsing(layoutJson)
            If String.IsNullOrWhiteSpace(parseJson) Then
                Return entries
            End If

            Try
                Using doc = JsonDocument.Parse(parseJson)
                    If doc.RootElement.ValueKind <> JsonValueKind.Array Then
                        Return New List(Of Entry)()
                    End If

                    Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

                    For Each item In doc.RootElement.EnumerateArray()
                        If item.ValueKind <> JsonValueKind.Object Then
                            Continue For
                        End If

                        Dim keyProp As JsonElement
                        If Not item.TryGetProperty("Key", keyProp) Then
                            Continue For
                        End If

                        Dim key = If(keyProp.GetString(), String.Empty).Trim()
                        If String.IsNullOrWhiteSpace(key) OrElse Not seen.Add(key) Then
                            Continue For
                        End If

                        Dim isVisible As Boolean = True
                        Dim visProp As JsonElement
                        If item.TryGetProperty("Visible", visProp) Then
                            If visProp.ValueKind = JsonValueKind.True OrElse visProp.ValueKind = JsonValueKind.False Then
                                isVisible = visProp.GetBoolean()
                            End If
                        End If

                        entries.Add(New Entry With {.FieldName = key, .Visible = isVisible})
                    Next
                End Using
            Catch
                ' A layout that cannot be read is no layout. Falling back to the grid's columns
                ' leaves a usable search panel; throwing here would close the page.
                Return New List(Of Entry)()
            End Try

            Return entries
        End Function

        Friend Shared Function Serialize(entries As IEnumerable(Of Entry)) As String
            If entries Is Nothing Then
                Return String.Empty
            End If

            Dim sb As New StringBuilder()
            sb.Append("[")

            Dim displayIndex As Integer = 0
            For Each entry In entries
                If entry Is Nothing OrElse String.IsNullOrWhiteSpace(entry.FieldName) Then
                    Continue For
                End If

                If sb.Length > 1 Then
                    sb.Append(",")
                End If

                sb.Append("{""Key"":""")
                sb.Append(Escape(entry.FieldName.Trim()))
                sb.Append(""",""DisplayIndex"":")
                sb.Append(displayIndex.ToString(Globalization.CultureInfo.InvariantCulture))
                sb.Append(",""Visible"":")
                sb.Append(If(entry.Visible, "true", "false"))
                sb.Append("}")

                displayIndex += 1
            Next

            sb.Append("]")
            Return sb.ToString()
        End Function

        ''' <summary>
        ''' The saved arrangement applied to what the page actually returns today.
        '''
        ''' Two cases the saved document cannot cover, because the page SQL can change after it was
        ''' written:
        '''
        ''' - A field in the layout that the SQL no longer returns is dropped. Nothing can be
        '''   searched on a column that is not there.
        ''' - A field the SQL returns that the layout has never heard of goes to the end, visible.
        '''   Appearing is the safe failure: a new column that silently could not be searched on
        '''   would look like a framework fault, and an App Admin can hide it in one tick.
        ''' </summary>
        ''' <param name="candidateFieldNames">
        ''' Every field the page could offer, in the order it would offer them without a layout.
        ''' </param>
        Friend Shared Function Apply(candidateFieldNames As IEnumerable(Of String),
                                     layout As IList(Of Entry)) As List(Of Entry)
            Dim candidates = If(candidateFieldNames, Enumerable.Empty(Of String)()).
                Where(Function(name) Not String.IsNullOrWhiteSpace(name)).
                ToList()

            Dim result As New List(Of Entry)()
            If candidates.Count = 0 Then
                Return result
            End If

            If layout Is Nothing OrElse layout.Count = 0 Then
                Return candidates.Select(Function(name) New Entry With {.FieldName = name, .Visible = True}).ToList()
            End If

            Dim remaining As New List(Of String)(candidates)

            For Each entry In layout
                Dim match = remaining.FirstOrDefault(Function(name) String.Equals(name, entry.FieldName, StringComparison.OrdinalIgnoreCase))
                If match Is Nothing Then
                    Continue For
                End If

                remaining.Remove(match)
                result.Add(New Entry With {.FieldName = match, .Visible = entry.Visible})
            Next

            For Each name In remaining
                result.Add(New Entry With {.FieldName = name, .Visible = True})
            Next

            Return result
        End Function

        Private Shared Function Escape(input As String) As String
            If input Is Nothing Then
                Return String.Empty
            End If

            Return input.Replace("\", "\\").Replace("""", "\" & ChrW(34))
        End Function

    End Class

End Namespace
