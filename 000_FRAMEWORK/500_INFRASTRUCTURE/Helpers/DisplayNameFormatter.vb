Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text.RegularExpressions

Namespace SDC.Framework
    ''' <summary>
    ''' Single owner for turning a database name into user-facing display text.
    ''' Used by control labels (FW_Base_U), grid column headers and page titles
    ''' (FW_Base_B), and entity aliases (EntityDisplayNameHelper). Do not add a
    ''' second implementation; extend this one.
    ''' </summary>
    Public Module DisplayNameFormatter

        ''' <summary>
        ''' Words that must stay upper case after title casing. Compared case-insensitively
        ''' against each produced word. Add domain acronyms here rather than special-casing
        ''' them at a call site.
        ''' </summary>
        Private ReadOnly Acronyms As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "ID", "PK", "SSN", "DOB", "QBE", "SQL", "HD", "US", "UI", "URL", "PIN", "FAX"
        }

        ''' <summary>
        ''' Converts a database or control name into display text.
        ''' Examples: FirstName -> First Name, First_Name -> First Name,
        ''' UserID -> User ID, SSN -> SSN, FW_HD_Issues -> HD Issues, Address2 -> Address 2.
        ''' </summary>
        ''' <param name="name">Source name, typically a column or control caption.</param>
        ''' <param name="stripFrameworkPrefix">
        ''' True for table-derived text (column headers, page titles, entity aliases) where the
        ''' FW_ prefix is noise. False for control captions, which are already field names.
        ''' </param>
        ''' <summary>
        ''' The name a user sees for a page. `_B` and `_U` are a developer naming convention, not
        ''' something to put on screen, so the suffix is dropped before formatting: EntityX_B and
        ''' EntityX_U both become "Entity X". Callers add the word for what the page is doing.
        ''' </summary>
        Public Function ToPageDisplayName(pageName As String) As String
            Dim text = If(pageName, String.Empty).Trim()
            If text.EndsWith("_B", StringComparison.OrdinalIgnoreCase) OrElse
               text.EndsWith("_U", StringComparison.OrdinalIgnoreCase) Then
                text = text.Substring(0, text.Length - 2)
            End If
            Return ToDisplayName(text, stripFrameworkPrefix:=True)
        End Function

        Public Function ToDisplayName(name As String,
                                      Optional stripFrameworkPrefix As Boolean = True) As String
            If String.IsNullOrWhiteSpace(name) Then
                Return If(name, String.Empty)
            End If

            Dim text = name.Trim()

            If stripFrameworkPrefix AndAlso text.StartsWith("FW_", StringComparison.OrdinalIgnoreCase) Then
                text = text.Substring(3)
            End If

            text = text.Replace("_"c, " "c)

            ' Split camel case, acronym-to-word boundaries, and letter/digit boundaries.
            text = Regex.Replace(text, "([a-z0-9])([A-Z])", "$1 $2")
            text = Regex.Replace(text, "([A-Z]+)([A-Z][a-z])", "$1 $2")
            text = Regex.Replace(text, "([A-Za-z])([0-9])", "$1 $2")
            text = Regex.Replace(text, "([0-9])([A-Za-z])", "$1 $2")
            text = Regex.Replace(text, "\s+", " ").Trim()

            If text = String.Empty Then
                Return String.Empty
            End If

            text = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant())

            ' Restore acronyms that title casing flattened, for example Ssn -> SSN.
            Dim words = text.Split(" "c)
            For index = 0 To words.Length - 1
                If Acronyms.Contains(words(index)) Then
                    words(index) = words(index).ToUpperInvariant()
                End If
            Next

            text = String.Join(" ", words)

            ' Splitting leaves "EMail" as "E Mail"; the intended display is one word.
            text = text.Replace("E Mail", "Email")

            Return text
        End Function

    End Module
End Namespace
