Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' The employee import's rules for single values and its two readers - the parts that fail
    ''' quietly. A user name that keeps an apostrophe, a date read the wrong way round and a quoted
    ''' comma that shifts every later column all import without an error.
    ''' </summary>
    <TestClass>
    Public Class ImportRulesTests

        ' ---- user names ----

        <TestMethod>
        Public Sub UserName_IsInitialAndSurname_WithoutPunctuationOrAccents()
            Dim taken As New HashSet(Of String)()
            Assert.AreEqual("bohare", ImportRules.GenerateUserName("Bob", "O'Hare", taken))
            Assert.AreEqual("idelacroixmoreau", ImportRules.GenerateUserName("Inès", "Delacroix-Moreau", taken))
        End Sub

        <TestMethod>
        Public Sub UserName_TakenIsNumbered_NotRefused()
            Dim taken As New HashSet(Of String) From {"gnolan"}
            Assert.AreEqual("gnolan2", ImportRules.ResolveUserName("gnolan", "Gareth", "Nolan", taken))
            Assert.AreEqual("gnolan3", ImportRules.ResolveUserName("gnolan", "Grace", "Nolan", taken))
        End Sub

        <TestMethod>
        Public Sub UserName_TakenIsCaseInsensitive()
            Dim taken As New HashSet(Of String) From {"praman"}
            Assert.AreEqual("PRaman2", ImportRules.ResolveUserName("PRaman", "Priya", "Raman", taken))
        End Sub

        <TestMethod>
        Public Sub UserName_EmailStyle_NumberGoesBeforeTheAt()
            Dim taken As New HashSet(Of String) From {"grathke@sdcdev.net"}
            Assert.AreEqual("grathke2@sdcdev.net", ImportRules.ResolveUserName("grathke@sdcdev.net", "Owen", "Fletcher", taken))
        End Sub

        <TestMethod>
        Public Sub UserName_Supplied_IsKeptWhenFree()
            Dim taken As New HashSet(Of String)()
            Assert.AreEqual("praman", ImportRules.ResolveUserName("  praman ", "Priya", "Raman", taken))
            Assert.IsTrue(taken.Contains("praman"))
        End Sub

        <TestMethod>
        Public Sub UserName_FitsTheColumnEvenWhenNumbered()
            Dim longSurname = New String("x"c, 80)
            Dim taken As New HashSet(Of String)()
            Dim first = ImportRules.GenerateUserName("A", longSurname, taken, 50)
            Dim second = ImportRules.GenerateUserName("A", longSurname, taken, 50)
            Assert.IsTrue(first.Length <= 50)
            Assert.IsTrue(second.Length <= 50)
            Assert.AreNotEqual(first, second)
        End Sub

        <TestMethod>
        Public Sub UserNamePattern_ExpandsEveryToken()
            Dim expand = Function(p As String) ImportRules.ExpandUserNamePattern(p, "Bob", "O'Hare", "Bob.OHare@Example.com")
            Assert.AreEqual("bohare", expand("{F}{Last}"))
            Assert.AreEqual("bob.ohare", expand("{First}.{Last}"))
            Assert.AreEqual("bobo", expand("{First}{L}"))
            Assert.AreEqual("bob.ohare@example.com", expand("{Email}"))
            Assert.AreEqual("bob.ohare", expand("{EmailName}"))
            Assert.AreEqual("sar-bohare", expand("sar-{f}{LAST}"))
            Assert.AreEqual("{Nope}bob", expand("{Nope}{First}"))
        End Sub

        <TestMethod>
        Public Sub UserNamePattern_AccentsAndSpacesGo_AndOnlyBracesMakeAPattern()
            Assert.AreEqual("sobriain", ImportRules.ExpandUserNamePattern("{F}{Last}", "Siobhán", "Ó Briain", ""))
            Assert.IsTrue(ImportRules.IsUserNamePattern("{First}.{Last}"))
            Assert.IsFalse(ImportRules.IsUserNamePattern("praman"))
        End Sub

        ' ---- PINs ----

        <TestMethod>
        Public Sub Pin_IsSixDigits_AndUniqueWithinTheImport()
            Dim used As New HashSet(Of String)()
            For i = 1 To 500
                Dim pin = ImportRules.GeneratePin(used)
                Assert.AreEqual(6, pin.Length)
                Assert.IsTrue(pin.All(Function(c) Char.IsDigit(c)))
            Next
            Assert.AreEqual(500, used.Count)
        End Sub

        ' ---- values ----

        <TestMethod>
        Public Sub Value_BlankIsNull()
            Assert.IsTrue(ImportRules.ConvertValue("   ", "varchar", 45).IsBlank)
            Assert.IsTrue(ImportRules.ConvertValue("", "date", 0).IsBlank)
        End Sub

        <TestMethod>
        Public Sub Value_TooLongIsReportedInCharacters()
            Dim result = ImportRules.ConvertValue(New String("a"c, 46), "varchar", 45)
            StringAssert.Contains(result.Problem, "46")
            StringAssert.Contains(result.Problem, "45")
        End Sub

        <TestMethod>
        Public Sub Value_DatesAreIsoOrMonthFirst()
            Assert.AreEqual(New Date(2026, 3, 4), ImportRules.ConvertValue("2026-03-04", "date", 0).Value)
            Assert.AreEqual(New Date(2026, 3, 4), ImportRules.ConvertValue("3/4/2026", "date", 0).Value)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("not a date", "date", 0).Problem)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("25/12/2026", "date", 0).Problem)
        End Sub

        <TestMethod>
        Public Sub Value_TodayAndNow_FollowTheColumnType()
            ' UTC. With no session signed in, the session zone is UTC too, which is what lets the
            ' date and the moment be asserted against the same clock.
            Dim clock = New DateTime(2026, 9, 24, 15, 42, 7, DateTimeKind.Utc)

            For Each word In {"Today", "today()", " TODAY () ", "Now", "now()"}
                Assert.AreEqual(clock.Date, ImportRules.ConvertValue(word, "date", 0, clock).Value, word)
            Next

            Assert.AreEqual(clock.Date, ImportRules.ConvertValue("Today", "datetime", 0, clock).Value)
            Assert.AreEqual(clock, ImportRules.ConvertValue("Now()", "datetime2", 0, clock).Value)

            Assert.AreEqual(clock.TimeOfDay, ImportRules.ConvertValue("now", "time", 0, clock).Value)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("Today", "time", 0, clock).Problem)
            Assert.AreEqual(New TimeSpan(14, 30, 0), ImportRules.ConvertValue("2:30 PM", "time", 0, clock).Value)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("noonish", "time", 0, clock).Problem)

            ' A word that merely starts the same is text, not the clock.
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("Todays", "date", 0, clock).Problem)
        End Sub

        <TestMethod>
        Public Sub Value_BitsAndNumbers()
            Assert.AreEqual(True, ImportRules.ConvertValue("Yes", "bit", 0).Value)
            Assert.AreEqual(False, ImportRules.ConvertValue("0", "bit", 0).Value)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("maybe", "bit", 0).Problem)
            Assert.AreEqual(42, ImportRules.ConvertValue("42", "int", 0).Value)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("4.2", "int", 0).Problem)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("300", "tinyint", 0).Problem)
        End Sub

        <TestMethod>
        Public Sub Csv_QuotedCommaAndLineBreakStayInOneField()
            Dim text = "Name,Address,City" & vbCrLf &
                       "Amelia,""14 Mill Lane, Apt 3"",Mobile" & vbCrLf &
                       "Sam,""101 Oak Drive" & vbCrLf & "Building B"",Theodore" & vbCrLf
            Dim source = ImportCsvReader.Read(Encoding.UTF8.GetBytes(text), True)

            Assert.IsTrue(source.Loaded, source.Problem)
            Assert.AreEqual(2, source.RowCount)
            Assert.AreEqual("14 Mill Lane, Apt 3", source.Rows.Rows(0)("Address"))
            Assert.AreEqual("Mobile", source.Rows.Rows(0)("City"))
            Assert.AreEqual("Theodore", source.Rows.Rows(1)("City"))
        End Sub

        <TestMethod>
        Public Sub Csv_SemicolonIsDetected_AndTheBomIsNotInTheFirstHeading()
            Dim encoding As New UTF8Encoding(True)
            Dim data = encoding.GetPreamble().Concat(encoding.GetBytes("First;Last" & vbLf & "Ann;Lee" & vbLf)).ToArray()
            Dim source = ImportCsvReader.Read(data, True)

            Assert.IsTrue(source.Loaded, source.Problem)
            Assert.AreEqual(";", source.Delimiter)
            Assert.AreEqual("First", source.Rows.Columns(0).ColumnName)
            Assert.AreEqual("Lee", source.Rows.Rows(0)("Last"))
        End Sub

        <TestMethod>
        Public Sub Csv_TheRealTestFileReadsFourteenRows()
            Dim path = FindRepoFile("assets\imports\employees-test.csv")
            Dim source = ImportCsvReader.Read(File.ReadAllBytes(path), True)

            Assert.IsTrue(source.Loaded, source.Problem)
            Assert.AreEqual(14, source.RowCount)
            Assert.AreEqual(12, source.Rows.Columns.Count)
        End Sub

        <TestMethod>
        Public Sub Json_ArrayOrWrappedArray_WithNestedObjectsFlattened()
            Dim json = "{""employees"": [" &
                       "{""firstName"": ""Ann"", ""address"": {""city"": ""Mobile""}, ""age"": 40, ""active"": true}," &
                       "{""firstName"": ""Bo"", ""email"": null}" &
                       "]}"
            Dim source = ImportJsonReader.Read(Encoding.UTF8.GetBytes(json))

            Assert.IsTrue(source.Loaded, source.Problem)
            Assert.AreEqual(2, source.RowCount)
            Assert.AreEqual("Mobile", source.Rows.Rows(0)("address.city"))
            Assert.AreEqual("40", source.Rows.Rows(0)("age"))
            Assert.AreEqual("true", source.Rows.Rows(0)("active"))
            Assert.AreEqual(String.Empty, source.Rows.Rows(1)("address.city"))
        End Sub

        <TestMethod>
        Public Sub Json_TheRealTestFileMatchesTheCsv()
            Dim path = FindRepoFile("assets\imports\employees-test.json")
            Dim source = ImportJsonReader.Read(File.ReadAllBytes(path))

            Assert.IsTrue(source.Loaded, source.Problem)
            Assert.AreEqual(14, source.RowCount)
        End Sub

        <TestMethod>
        Public Sub FullFiles_CsvAndJsonAgree()
            Dim csv = ImportCsvReader.Read(File.ReadAllBytes(FindRepoFile("assets\imports\employees-full.csv")), True)
            Dim json = ImportJsonReader.Read(File.ReadAllBytes(FindRepoFile("assets\imports\employees-full.json")))

            Assert.IsTrue(csv.Loaded, csv.Problem)
            Assert.IsTrue(json.Loaded, json.Problem)
            Assert.AreEqual(12, csv.RowCount)
            Assert.AreEqual(12, json.RowCount)
            Assert.AreEqual(31, csv.Rows.Columns.Count)

            ' The BOM is not in the first heading, the accent survived, and a doubled quote is one.
            Assert.AreEqual("Given Name", csv.Rows.Columns(0).ColumnName)
            Assert.AreEqual("Siobhán", csv.Rows.Rows(4)("Given Name"))
            Assert.AreEqual("Siobhán", json.Rows.Rows(4)("givenName"))
            StringAssert.Contains(CStr(csv.Rows.Rows(6)("Notes")), """Cilla""")

            ' JSON-only: the nested contact flattens, numbers and booleans keep their text.
            Assert.AreEqual("Paul Whitcombe", json.Rows.Rows(0)("emergencyContact.name"))
            Assert.AreEqual("4", json.Rows.Rows(0)("genderId"))
            Assert.AreEqual("true", json.Rows.Rows(0)("inspector"))
        End Sub

        <TestMethod>
        Public Sub FullFiles_EveryValueTheReadmePromisesParses()
            Assert.AreEqual(New Date(1985, 7, 9), ImportRules.ConvertValue("July 9, 1985", "date", 0).Value)
            Assert.AreEqual(New Date(2026, 4, 6), ImportRules.ConvertValue("4/6/2026", "date", 0).Value)
            Assert.AreEqual(True, ImportRules.ConvertValue("true", "bit", 0).Value)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("M", "int", 0).Problem)
            Assert.AreNotEqual(String.Empty, ImportRules.ConvertValue("Alabama", "varchar", 2).Problem)
            Assert.AreEqual(String.Empty, ImportRules.ConvertValue("Alabama", "nchar", 25).Problem)
            Assert.AreEqual("sobriain", ImportRules.GenerateUserName("Siobhán", "Ó Briain", New HashSet(Of String)()))
        End Sub

        <TestMethod>
        Public Sub Headings_AreRecognised_InBothTestFiles()
            For Each name In {"assets\imports\employees-test.csv", "assets\imports\employees-full.csv"}
                Dim data = File.ReadAllBytes(FindRepoFile(name))
                Assert.IsTrue(ImportCsvReader.FirstRowLooksLikeHeadings(data), name)
            Next
        End Sub

        <TestMethod>
        Public Sub Headings_AreNotSeen_WhenTheFirstRowIsAPerson()
            For Each name In {"assets\imports\employees-test.csv", "assets\imports\employees-full.csv"}
                Dim text = File.ReadAllText(FindRepoFile(name), Encoding.UTF8)
                Dim withoutHeadings = text.Substring(text.IndexOf(ControlChars.Lf) + 1)
                Assert.IsFalse(ImportCsvReader.FirstRowLooksLikeHeadings(Encoding.UTF8.GetBytes(withoutHeadings)), name)
            Next
        End Sub

        <TestMethod>
        Public Sub Headings_TextOverNumbers_CountsEvenWithABlankHeading()
            Dim text = "Name,,Zip" & vbLf & "Ann,x,36602" & vbLf & "Bo,y,36571" & vbLf
            Assert.IsTrue(ImportCsvReader.FirstRowLooksLikeHeadings(Encoding.UTF8.GetBytes(text)))
        End Sub

        <TestMethod>
        Public Sub Headings_ARepeatedTopCellIsData()
            Dim text = "Mobile,AL" & vbLf & "Mobile,AL" & vbLf & "Daphne,AL" & vbLf
            Assert.IsFalse(ImportCsvReader.FirstRowLooksLikeHeadings(Encoding.UTF8.GetBytes(text)))
        End Sub

        <TestMethod>
        Public Sub CleanFile_EveryRowPassesTheCheckRules()
            Dim source = ImportCsvReader.Read(File.ReadAllBytes(FindRepoFile("assets\imports\employees-clean.csv")), True)
            Assert.IsTrue(source.Loaded, source.Problem)
            Assert.AreEqual(14, source.RowCount)

            ' The same rules the Check tab applies to these columns: names required and within
            ' FW_Employees' 45 characters, the hire date a date, an email (where given) with an @.
            For Each row As DataRow In source.Rows.Rows
                Dim who = CStr(row("First Name")) & " " & CStr(row("Last Name"))
                For Each column In {"First Name", "Last Name"}
                    Dim converted = ImportRules.ConvertValue(CStr(row(column)), "varchar", 45)
                    Assert.AreEqual(String.Empty, converted.Problem, who & " " & column)
                    Assert.IsFalse(converted.IsBlank, who & " " & column & " is required")
                Next
                Assert.AreEqual(String.Empty, ImportRules.ConvertValue(CStr(row("Hire Date")), "date", 0).Problem, who & " Hire Date")
                Dim email = CStr(row("Email")).Trim()
                If email <> String.Empty Then Assert.IsTrue(email.IndexOf("@"c) > 0, who & " Email")
            Next
        End Sub

        <TestMethod>
        Public Sub Json_NoRecordsIsRefusedByName()
            Assert.IsFalse(ImportJsonReader.Read(Encoding.UTF8.GetBytes("{""a"": 1}")).Loaded)
            Assert.IsFalse(ImportJsonReader.Read(Encoding.UTF8.GetBytes("{not json")).Loaded)
        End Sub

        Private Shared Function FindRepoFile(relative As String) As String
            Dim dir = New DirectoryInfo(AppContext.BaseDirectory)
            While dir IsNot Nothing
                Dim candidate = Path.Combine(dir.FullName, relative)
                If File.Exists(candidate) Then Return candidate
                dir = dir.Parent
            End While
            Assert.Fail("Not found above the test output: " & relative)
            Return String.Empty
        End Function
    End Class
End Namespace
