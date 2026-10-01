Option Strict On
Option Explicit On

Imports System.IO
Imports System.Linq
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' Which prefix each owner folder gives its pages.
    '''
    ''' 100_CTY was renamed 100_CITY NEXUS on 2026-10-01. The prefix had always been derived from the
    ''' folder name, so without the PREFIX file the rename would have made every page generated
    ''' afterwards CITYNEXUS_ while its tables, pages and permission rows stayed CTY_ - with nothing
    ''' failing until somebody noticed two prefixes for one application.
    '''
    ''' Built in a temporary folder rather than read from the repository, so it pins the rule and
    ''' does not depend on where the tests happen to run from.
    ''' </summary>
    <TestClass>
    Public Class PageOwnersTests

        Private root As String

        <TestInitialize>
        Public Sub CreateWorkspace()
            root = Path.Combine(Path.GetTempPath(), "PageOwnersTests_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(Path.Combine(root, "000_FRAMEWORK"))
            Directory.CreateDirectory(Path.Combine(root, "100_CITY NEXUS"))
            Directory.CreateDirectory(Path.Combine(root, "200_NEXT PROJECT"))
            Directory.CreateDirectory(Path.Combine(root, "scripts"))
            File.WriteAllText(Path.Combine(root, "100_CITY NEXUS", PageOwners.PrefixFileName), "CTY" & Environment.NewLine)
        End Sub

        <TestCleanup>
        Public Sub RemoveWorkspace()
            If Directory.Exists(root) Then Directory.Delete(root, True)
        End Sub

        Private Function PrefixOf(folderName As String) As String
            Return PageOwners.All(root).Single(Function(owner) owner.FolderName = folderName).Prefix
        End Function

        <TestMethod>
        Public Sub PrefixFile_KeepsCtyForCityNexus()
            Assert.AreEqual("CTY", PrefixOf("100_CITY NEXUS"))
        End Sub

        <TestMethod>
        Public Sub NoPrefixFile_FallsBackToTheFolderName()
            Assert.AreEqual("NEXTPROJECT", PrefixOf("200_NEXT PROJECT"))
        End Sub

        <TestMethod>
        Public Sub Framework_IsAlwaysFw()
            Assert.AreEqual("FW", PrefixOf("000_FRAMEWORK"))
        End Sub

        <TestMethod>
        Public Sub UnnumberedFolders_AreNotOwners()
            Assert.AreEqual(3, PageOwners.All(root).Count)
        End Sub

        <TestMethod>
        Public Sub PickerShowsTheFolderNameWithTheRealPrefix()
            Dim owner = PageOwners.All(root).Single(Function(candidate) candidate.FolderName = "100_CITY NEXUS")
            Assert.AreEqual("CITY NEXUS (CTY_)", owner.DisplayName)
        End Sub

        <TestMethod>
        Public Sub AnUnusablePrefixFile_FallsBackRatherThanInventingOne()
            ' A trailing underscore typed out of habit is forgiven; anything not letters and digits
            ' is ignored, so the folder name decides.
            File.WriteAllText(Path.Combine(root, "200_NEXT PROJECT", PageOwners.PrefixFileName), "CTY_")
            Assert.AreEqual("CTY", PrefixOf("200_NEXT PROJECT"))

            File.WriteAllText(Path.Combine(root, "200_NEXT PROJECT", PageOwners.PrefixFileName), "C T-Y")
            Assert.AreEqual("NEXTPROJECT", PrefixOf("200_NEXT PROJECT"))
        End Sub

    End Class

End Namespace
