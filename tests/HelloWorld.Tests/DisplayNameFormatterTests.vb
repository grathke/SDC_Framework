Option Strict On
Option Explicit On

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports HelloWorld.HelloWorld

Namespace HelloWorldTests

    ''' <summary>
    ''' Every browse column header, page title and field caption goes through this. It is the one
    ''' owner of the formatting, so a regression here is visible on every page at once.
    ''' </summary>
    <TestClass>
    Public Class DisplayNameFormatterTests

        <DataTestMethod>
        <DataRow("FirstName", "First Name")>
        <DataRow("First_Name", "First Name")>
        <DataRow("LastName", "Last Name")>
        <DataRow("Address2", "Address 2")>
        Public Sub SplitsCamelCaseUnderscoresAndDigits(source As String, expected As String)
            Assert.AreEqual(expected, DisplayNameFormatter.ToDisplayName(source))
        End Sub

        <TestMethod>
        Public Sub EMail_IsRejoinedAsOneWord()
            ' Splitting camel case turns EMail into "E Mail"; the formatter puts it back
            ' deliberately, so the display text is "Email".
            Assert.AreEqual("Main Email", DisplayNameFormatter.ToDisplayName("MainEMail"))
        End Sub

        <DataTestMethod>
        <DataRow("UserID", "User ID")>
        <DataRow("SSN", "SSN")>
        <DataRow("RegistrationID", "Registration ID")>
        Public Sub KeepsAcronymsUpperCase(source As String, expected As String)
            Assert.AreEqual(expected, DisplayNameFormatter.ToDisplayName(source))
        End Sub

        <TestMethod>
        Public Sub StripsFrameworkPrefix_ForTableDerivedText()
            Assert.AreEqual("HD Issues", DisplayNameFormatter.ToDisplayName("FW_HD_Issues"))
            Assert.AreEqual("Users", DisplayNameFormatter.ToDisplayName("FW_Users"))
        End Sub

        <TestMethod>
        Public Sub KeepsFrameworkPrefix_WhenAskedTo()
            ' Control captions are already field names, so the prefix is not noise there. Note it
            ' comes back as "Fw", not "FW" - title casing flattens it and FW is not in the acronym
            ' list. Recorded as current behavior rather than asserted as desirable; add "FW" to
            ' DisplayNameFormatter.Acronyms if that ever matters.
            Assert.AreEqual("Fw Users", DisplayNameFormatter.ToDisplayName("FW_Users", stripFrameworkPrefix:=False))
        End Sub

        <DataTestMethod>
        <DataRow("")>
        <DataRow("   ")>
        Public Sub BlankInput_IsReturnedUnchanged(source As String)
            Assert.AreEqual(source, DisplayNameFormatter.ToDisplayName(source))
        End Sub

        <TestMethod>
        Public Sub Nothing_DoesNotThrow()
            Assert.AreEqual(String.Empty, DisplayNameFormatter.ToDisplayName(Nothing))
        End Sub

    End Class

End Namespace
