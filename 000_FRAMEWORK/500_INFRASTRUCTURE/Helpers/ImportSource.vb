Option Strict On
Option Explicit On

Imports System.Data

Namespace SDC.Framework

    ''' <summary>
    ''' What a source file turned out to be, and what went wrong if it did not.
    '''
    ''' Shared by the CSV and JSON readers so the form handles one result whichever it called, and
    ''' so a third format later has an obvious shape to return.
    '''
    ''' **Every value is a string.** Converting to the target's type belongs to validation, and
    ''' keeping the two apart is what lets the pre-check run again over corrected values without
    ''' re-reading the file - and what stops a date that failed to parse being quietly replaced by
    ''' a default nobody chose.
    ''' </summary>
    Public NotInheritable Class ImportSource
        Public Property Rows As DataTable

        ''' <summary>"CSV" or "JSON", shown beside the row count.</summary>
        Public Property FormatName As String = "CSV"

        ''' <summary>What the CSV reader settled on, or "n/a" for JSON. Shown so a guess is visible.</summary>
        Public Property Delimiter As String = ","

        Public Property EncodingName As String = "UTF-8"

        ''' <summary>Empty when the file read cleanly. Anything else is shown and nothing is loaded.</summary>
        Public Property Problem As String = String.Empty

        Public ReadOnly Property Loaded As Boolean
            Get
                Return Rows IsNot Nothing AndAlso Problem = String.Empty
            End Get
        End Property

        Public ReadOnly Property RowCount As Integer
            Get
                Return If(Rows Is Nothing, 0, Rows.Rows.Count)
            End Get
        End Property

        Public Shared Function Failed(problem As String) As ImportSource
            Return New ImportSource With {.Problem = problem}
        End Function
    End Class

    ''' <summary>
    ''' Which reader a file needs, decided in one place.
    '''
    ''' By extension, which is a guess - but the alternative is sniffing the content, and a JSON
    ''' file named .csv is a mistake worth reporting rather than working around silently.
    ''' </summary>
    Public Module ImportSourceFormat

        ''' <param name="fileName">
        ''' Named for what it is rather than "path": a parameter called path hides System.IO.Path
        ''' inside the function, and Path.GetExtension then fails to compile.
        ''' </param>
        Public Function IsJson(fileName As String) As Boolean
            If String.IsNullOrWhiteSpace(fileName) Then Return False
            Return System.IO.Path.GetExtension(fileName).Equals(".json", StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>Reads the bytes with whichever reader the name calls for.</summary>
        Public Function Read(fileName As String,
                             data As Byte(),
                             hasHeaderRow As Boolean,
                             Optional delimiterOverride As String = "") As ImportSource
            If IsJson(fileName) Then Return ImportJsonReader.Read(data)
            Return ImportCsvReader.Read(data, hasHeaderRow, delimiterOverride)
        End Function
    End Module
End Namespace
