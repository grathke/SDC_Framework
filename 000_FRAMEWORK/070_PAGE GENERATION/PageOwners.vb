Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions

Namespace SDC.Framework

    ''' <summary>
    ''' Who a page belongs to, and everything that follows from it.
    '''
    ''' One answer decides two things that must agree: the prefix its name carries, and the folder
    ''' its files are written to. They were separate before 2026-09-18 - a checkbox set the prefix
    ''' and a constant set the folder - which allowed FW_Widget_B to be written into an
    ''' application's folder, a contradiction nothing would catch.
    '''
    ''' The owners are the numbered folders at the repository root. Adding an application means
    ''' adding a folder; nothing here lists them, so nothing here can fall behind.
    ''' </summary>
    Public NotInheritable Class PageOwners

        Private Sub New()
        End Sub

        ''' <summary>What the picker shows until somebody chooses. Never a valid owner.</summary>
        Public Const NoSelection As String = "Make a Selection"

        ''' <summary>The framework's folder, and the one owner whose prefix is not its folder name.</summary>
        Private Const FrameworkFolder As String = "000_FRAMEWORK"
        Private Const FrameworkPrefix As String = "FW"

        ''' <summary>The waiting room inside every owner, where a generated page lands unfiled.</summary>
        Public Const GeneratedFolderName As String = "999_GENERATED"

        Public NotInheritable Class Owner
            ''' <summary>The folder, as it is on disk: "000_FRAMEWORK", "100_CITY NEXUS".</summary>
            Public Property FolderName As String

            ''' <summary>The prefix its tables and pages carry, without the underscore: "FW", "CTY".</summary>
            Public Property Prefix As String

            ''' <summary>What the picker shows: "FRAMEWORK (FW_)", "CITY NEXUS (CTY_)".</summary>
            Public Property DisplayName As String

            Public Overrides Function ToString() As String
                Return DisplayName
            End Function
        End Class

        ''' <summary>
        ''' The owners, read from the root folders, in the order their numbers put them.
        '''
        ''' A folder counts when it is named NNN_something. The framework is named rather than
        ''' derived, because "FRAMEWORK" is a word and "FW" is the prefix thirty tables already
        ''' carry. Every other owner takes its prefix from a PREFIX file in its folder when it has
        ''' one - 100_CITY NEXUS holds "CTY" - and otherwise from its own name, so a folder renamed
        ''' to 200_XXX becomes XXX_ with nothing else to change.
        ''' </summary>
        Public Shared Function All(workspaceRoot As String) As List(Of Owner)
            Dim owners As New List(Of Owner)()
            If String.IsNullOrWhiteSpace(workspaceRoot) OrElse Not Directory.Exists(workspaceRoot) Then Return owners

            Dim folderNames = Directory.GetDirectories(workspaceRoot).
                Select(Function(candidate) IO.Path.GetFileName(candidate)).
                OrderBy(Function(candidate) candidate, StringComparer.Ordinal).
                ToList()

            For Each folderName In folderNames
                If Not Regex.IsMatch(folderName, "^\d{3}_.+") Then Continue For

                Dim prefix = PrefixFor(workspaceRoot, folderName)
                If prefix = String.Empty Then Continue For

                ' Described the same way as the rest. The framework showed "Framework (FW_)" from a
                ' literal while its neighbours showed their folder names - CTY, NEXT PROJECT - so
                ' one row in the list was written in a different voice from the others. Only the
                ' prefix is the framework's exception; how it reads is not.
                owners.Add(New Owner With {
                    .FolderName = folderName,
                    .Prefix = prefix,
                    .DisplayName = DescribeOwner(folderName, prefix)
                })
            Next

            Return owners
        End Function

        ''' <summary>The owner a stored folder name refers to, or nothing when it no longer exists.</summary>
        Public Shared Function ByFolder(workspaceRoot As String, folderName As String) As Owner
            If String.IsNullOrWhiteSpace(folderName) Then Return Nothing

            Return All(workspaceRoot).FirstOrDefault(
                Function(owner) String.Equals(owner.FolderName, folderName.Trim(), StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>Where a generated page for this owner is written, relative to the root.</summary>
        Public Shared Function GeneratedFolderFor(owner As Owner) As String
            If owner Is Nothing Then Return IO.Path.Combine(FrameworkFolder, GeneratedFolderName)

            Return IO.Path.Combine(owner.FolderName, GeneratedFolderName)
        End Function

        ''' <summary>
        ''' The file an application folder can hold to name its prefix, when the folder's name is
        ''' a word rather than a code. One line, the prefix without its underscore.
        ''' </summary>
        Public Const PrefixFileName As String = "PREFIX"

        Private Shared Function PrefixFor(workspaceRoot As String, folderName As String) As String
            If String.Equals(folderName, FrameworkFolder, StringComparison.OrdinalIgnoreCase) Then Return FrameworkPrefix

            ' A PREFIX file wins. 100_CITY NEXUS keeps the CTY_ its tables, pages and permission
            ' rows were created with; derived from the name it would have become CITYNEXUS_, and
            ' every page generated afterwards would have stopped matching the ones before it.
            Dim declared = ReadPrefixFile(IO.Path.Combine(workspaceRoot, folderName, PrefixFileName))
            If declared <> String.Empty Then Return declared

            ' Everything past the number, with spaces removed and upper-cased: the placeholder
            ' "200_NEXT PROJECT" is NEXTPROJECT - which is what a placeholder deserves, and stops
            ' being odd the moment it is renamed or given a PREFIX file.
            Dim underscore = folderName.IndexOf("_"c)
            If underscore < 0 OrElse underscore = folderName.Length - 1 Then Return String.Empty

            Return folderName.Substring(underscore + 1).Replace(" ", String.Empty).ToUpperInvariant()
        End Function

        ''' <summary>
        ''' The first non-blank line of a PREFIX file, upper-cased, letters and digits only.
        ''' Anything else - no file, an empty one, a trailing underscore typed out of habit - falls
        ''' back to the folder name rather than inventing a prefix nobody meant.
        ''' </summary>
        Private Shared Function ReadPrefixFile(path As String) As String
            Try
                If Not File.Exists(path) Then Return String.Empty

                Dim line = File.ReadAllLines(path).
                    Select(Function(candidate) candidate.Trim().TrimEnd("_"c)).
                    FirstOrDefault(Function(candidate) candidate <> String.Empty)
                If line Is Nothing Then Return String.Empty

                Dim prefix = line.ToUpperInvariant()
                Return If(Regex.IsMatch(prefix, "^[A-Z0-9]+$"), prefix, String.Empty)
            Catch
                Return String.Empty
            End Try
        End Function

        Private Shared Function DescribeOwner(folderName As String, prefix As String) As String
            Dim underscore = folderName.IndexOf("_"c)
            Dim readable = If(underscore >= 0, folderName.Substring(underscore + 1), folderName)

            Return readable & " (" & prefix & "_)"
        End Function
    End Class
End Namespace
