Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' The wrapper rewrites SQL nobody in this repository wrote by hand, and a wrong rewrite is
    ''' either a page that will not open or - far worse - a page that returns the wrong rows and
    ''' says nothing. These are the cases that decide which.
    '''
    ''' The first test uses the real FW_Employees_B SQL, character for character as FW_Pages holds
    ''' it, because a wrapper that works on a tidied-up example is worth nothing.
    ''' </summary>
    <TestClass>
    Public Class BrowseSqlWrapperTests

        ''' <summary>The Employees page SQL, exactly as stored on 2026-09-21.</summary>
        Private Const EmployeesSql As String =
            "SELECT" & vbCrLf &
            "    E.[EmployeeID] AS PK," & vbCrLf &
            "    E.[FirstLast]," & vbCrLf &
            "    E.[Address1]," & vbCrLf &
            "    E.[City]," & vbCrLf &
            "    E.[State]," & vbCrLf &
            "    E.[Zip]," & vbCrLf &
            "    G.[GenderDescription] AS [GenderID]," & vbCrLf &
            "    E2.[FirstLast] AS [AssignedManagerID]," & vbCrLf &
            "    E.[UserName]," & vbCrLf &
            "    E.[HireDate]," & vbCrLf &
            "    E.[CreatedOn]" & vbCrLf &
            "FROM dbo.[FW_Employees] E" & vbCrLf &
            "LEFT JOIN dbo.[FW_Gender] G ON E.[GenderID] = G.[GenderID]" & vbCrLf &
            "LEFT JOIN dbo.[FW_Employees] E2 ON E.[AssignedManagerID] = E2.[EmployeeID]" & vbCrLf &
            "WHERE E.[RegistrationID] = @RegistrationID" & vbCrLf &
            "ORDER BY E.[EmployeeID] ASC"

        ' The other six browse pages, as FW_Pages held them on 2026-09-21. Read out of the database
        ' rather than typed, because the whole point of the test above is that a tidied-up example
        ' proves nothing - and two of these decline for reasons no example would have suggested.
        '
        ' Line breaks are normalised to vbCrLf. Nothing in the wrapper reads whitespace beyond
        ' "is there any", so that changes no outcome.

        ''' <summary>No ORDER BY at all, and a ? for the registration value.</summary>
        Private Const HdIssuesSql As String =
            "SELECT" & vbCrLf &
            "    i.IssueID AS PK," & vbCrLf &
            "    i.IssueNumber," & vbCrLf &
            "    i.Subject," & vbCrLf &
            "    c.CategoryName," & vbCrLf &
            "    i.Status," & vbCrLf &
            "    i.CreatedOn," & vbCrLf &
            "    i.UpdatedOn," & vbCrLf &
            "    i.ConversationEntryCount AS Messages" & vbCrLf &
            "FROM dbo.FW_HD_Issues AS i" & vbCrLf &
            "LEFT JOIN dbo.FW_HD_IssueCategories AS c" & vbCrLf &
            "    ON c.CategoryID = i.CategoryID" & vbCrLf &
            "WHERE i.RegistrationID = ?" & vbCrLf &
            "  AND ISNULL(i.DeletedFlag, 0) = 0"

        ''' <summary>Descending order, and two joins.</summary>
        Private Const HdIssuesSupportSql As String =
            "SELECT" & vbCrLf &
            "    i.IssueID AS PK," & vbCrLf &
            "    i.IssueNumber," & vbCrLf &
            "    i.Subject," & vbCrLf &
            "    c.CategoryName," & vbCrLf &
            "    i.Status," & vbCrLf &
            "    u.FirstLast AS RequesterName," & vbCrLf &
            "    i.CreatedOn," & vbCrLf &
            "    i.UpdatedOn" & vbCrLf &
            "FROM dbo.FW_HD_Issues AS i" & vbCrLf &
            "LEFT JOIN dbo.FW_HD_IssueCategories AS c" & vbCrLf &
            "    ON c.CategoryID = i.CategoryID" & vbCrLf &
            "LEFT JOIN dbo.FW_Users AS u" & vbCrLf &
            "    ON u.UserID = i.ReporterUserID" & vbCrLf &
            "    AND u.RegistrationID = i.RegistrationID" & vbCrLf &
            "WHERE ISNULL(i.DeletedFlag, 0) = 0 AND i.RegistrationID = @RegistrationID" & vbCrLf &
            "ORDER BY i.IssueID DESC"

        ''' <summary>The one page that selects DeletedFlag, and orders by an output name.</summary>
        Private Const PageGenerationSql As String =
            "SELECT GeneratedPageID AS PK, RequestName, PageBaseName, BrowsePageName, " &
            "MaintenancePageName, DeletedFlag FROM FW_GeneratedPages ORDER BY RequestName"

        ''' <summary>Two columns, no ORDER BY, no alias prefixes, and a lower-case "as".</summary>
        Private Const RegistrationSql As String =
            "SELECT RegistrationID as PK, RegName FROM FW_Registration"

        ''' <summary>Two ORDER BY terms, both needing rewriting.</summary>
        Private Const SwitchUserSql As String =
            "SELECT" & vbCrLf &
            "    s.[SwitchUserID] AS PK," & vbCrLf &
            "    s.[LastName]," & vbCrLf &
            "    s.[FirstName]," & vbCrLf &
            "    s.[UserName]," & vbCrLf &
            "    s.[Email]," & vbCrLf &
            "    s.[PersonType]," & vbCrLf &
            "    s.[CompanyName]," & vbCrLf &
            "    s.[IsActive]," & vbCrLf &
            "    s.[UserId]" & vbCrLf &
            "FROM dbo.[FW_SwitchUser] s" & vbCrLf &
            "WHERE s.[RegistrationID] = @RegistrationID" & vbCrLf &
            "ORDER BY s.[LastName] ASC, s.[FirstName] ASC"

        ''' <summary>Ordered by a column from the joined table, not the base one.</summary>
        Private Const UserAccessDiagnosticSql As String =
            "SELECT" & vbCrLf &
            "    U.[UserId] AS PK," & vbCrLf &
            "    E.[FirstName]," & vbCrLf &
            "    E.[LastName]," & vbCrLf &
            "    E.[FirstLast]," & vbCrLf &
            "    U.[UserName]" & vbCrLf &
            "FROM dbo.[FW_Users] U" & vbCrLf &
            "INNER JOIN dbo.[FW_Employees] E ON E.[UserId] = U.[UserId]" & vbCrLf &
            "WHERE U.[RegistrationID] = @RegistrationID" & vbCrLf &
            "ORDER BY E.[FirstLast] ASC"

        ''' <summary>Three-part column names, which is what makes this one decline.</summary>
        Private Const RolesSql As String =
            "SELECT dbo.FW_Roles.RoleID AS PK, dbo.FW_Roles.DisplayOrder, dbo.FW_Roles.RoleName " &
            "From dbo.FW_Roles WHERE RegistrationID = ? Order By DisplayOrder"

        <TestMethod>
        Public Sub TheRealEmployeesPage_Wraps()
            Dim result = BrowseSqlWrapper.TryWrap(EmployeesSql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "SELECT TOP (12) q.* FROM (")
            StringAssert.Contains(result.Sql, ") AS q")

            ' The heart of it: E does not exist outside the derived table, and EmployeeID is
            ' selected as PK, so the order has to be by that name.
            StringAssert.Contains(result.Sql, "ORDER BY q.[PK] ASC")
            Assert.IsFalse(result.Sql.Contains("ORDER BY E."), "The inner alias escaped into the outer ORDER BY.")
        End Sub

        <TestMethod>
        Public Sub TheRealEmployeesPage_ReportsItsOutputNames()
            Dim names As List(Of String) = Nothing
            Assert.IsTrue(BrowseSqlWrapper.TryReadOutputNames(EmployeesSql, names))

            CollectionAssert.AreEqual(
                New List(Of String) From {
                    "PK", "FirstLast", "Address1", "City", "State", "Zip",
                    "GenderID", "AssignedManagerID", "UserName", "HireDate", "CreatedOn"
                }, names)
        End Sub

        <TestMethod>
        Public Sub Predicates_GoIntoTheOuterWhere()
            Dim result = BrowseSqlWrapper.TryWrap(EmployeesSql, 201,
                                                  {"q.[LastName] LIKE @p0", "ISNULL(q.[DeletedFlag], 0) = 0"})

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "WHERE q.[LastName] LIKE @p0 AND ISNULL(q.[DeletedFlag], 0) = 0")

            ' The page's own WHERE stays where it was, inside.
            StringAssert.Contains(result.Sql, "WHERE E.[RegistrationID] = @RegistrationID")
        End Sub

        <TestMethod>
        Public Sub NoCap_StillWrapsForTheFilter()
            Dim result = BrowseSqlWrapper.TryWrap(EmployeesSql, 0, {"q.[City] = @p0"})

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            Assert.IsFalse(result.Sql.Contains("TOP ("), "A cap of zero must not produce a TOP.")
        End Sub

        <TestMethod>
        Public Sub OrderByAnOutputNameDirectly_Works()
            Dim sql = "SELECT E.[LastName], E.[FirstName] FROM dbo.[T] E ORDER BY LastName DESC"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[LastName] DESC")
        End Sub

        <TestMethod>
        Public Sub SeveralOrderByTerms_AreAllRewritten()
            Dim sql = "SELECT E.[A] AS PK, E.[B], E.[C] FROM dbo.[T] E ORDER BY E.[B] DESC, E.[A] ASC"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[B] DESC, q.[PK] ASC")
        End Sub

        <TestMethod>
        Public Sub OrderByInsideASubquery_IsNotMistakenForTheOuterOne()
            ' The scanner exists for this. Cutting the statement at the inner ORDER BY produces
            ' SQL that still runs and returns different rows, which is the failure that would
            ' never be noticed.
            Dim sql = "SELECT E.[A] AS PK, (SELECT TOP 1 X.[V] FROM dbo.[X] X ORDER BY X.[V]) AS [Latest] " &
                      "FROM dbo.[T] E ORDER BY E.[A] ASC"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[PK] ASC")
            StringAssert.Contains(result.Sql, "ORDER BY X.[V]")
        End Sub

        <TestMethod>
        Public Sub OrderByInsideAStringLiteral_IsNotAKeyword()
            Dim sql = "SELECT E.[A] AS PK, 'order by nothing' AS [Note] FROM dbo.[T] E ORDER BY E.[A]"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[PK]")
            StringAssert.Contains(result.Sql, "'order by nothing'")
        End Sub

        <TestMethod>
        Public Sub AnExpressionInTheOrderBy_Declines()
            Dim sql = "SELECT E.[A] AS PK, E.[B] FROM dbo.[T] E ORDER BY ISNULL(E.[B], 0) ASC"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsFalse(result.Wrapped)
            StringAssert.Contains(result.DeclineReason, "ORDER BY")
        End Sub

        <TestMethod>
        Public Sub AnOrderByColumnThatIsNotSelected_Declines()
            ' Legal today, because the inner query can order by anything in its own FROM. Outside
            ' the wrapper that column does not exist, and guessing would reorder the page.
            Dim sql = "SELECT E.[A] AS PK FROM dbo.[T] E ORDER BY E.[Hidden] ASC"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsFalse(result.Wrapped)
        End Sub

        <TestMethod>
        Public Sub NoOrderByAndACap_Declines()
            Dim sql = "SELECT E.[A] AS PK FROM dbo.[T] E"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsFalse(result.Wrapped)
            StringAssert.Contains(result.DeclineReason, "deterministic")
        End Sub

        <TestMethod>
        Public Sub NoOrderByAndNoCap_IsFine()
            Dim sql = "SELECT E.[A] AS PK FROM dbo.[T] E"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 0, {"q.[A] = @p0"})

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
        End Sub

        <TestMethod>
        Public Sub TwoColumnsWithTheSameName_Decline()
            ' This runs today - a DataTable renames the second - and a derived table refuses it.
            Dim sql = "SELECT E.[FirstLast], E2.[FirstLast] FROM dbo.[T] E JOIN dbo.[T] E2 ON 1=1 ORDER BY E.[FirstLast]"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsFalse(result.Wrapped)
            StringAssert.Contains(result.DeclineReason, "FirstLast")
        End Sub

        <TestMethod>
        Public Sub AnUnnamedExpressionColumn_Declines()
            Dim sql = "SELECT E.[A] AS PK, E.[B] + E.[C] FROM dbo.[T] E ORDER BY E.[A]"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsFalse(result.Wrapped)
        End Sub

        <TestMethod>
        Public Sub SqlThatIsNotASingleSelect_Declines()
            Assert.IsFalse(BrowseSqlWrapper.TryWrap("SELECT 1; SELECT 2", 12, Nothing).Wrapped)
            Assert.IsFalse(BrowseSqlWrapper.TryWrap("WITH x AS (SELECT 1 AS A) SELECT A FROM x ORDER BY A", 12, Nothing).Wrapped)
            Assert.IsFalse(BrowseSqlWrapper.TryWrap("EXEC dbo.SomeProc", 12, Nothing).Wrapped)
            Assert.IsFalse(BrowseSqlWrapper.TryWrap("SELECT TOP 10 E.[A] AS PK FROM dbo.[T] E ORDER BY E.[A]", 12, Nothing).Wrapped)
            Assert.IsFalse(BrowseSqlWrapper.TryWrap("", 12, Nothing).Wrapped)
            Assert.IsFalse(BrowseSqlWrapper.TryWrap(Nothing, 12, Nothing).Wrapped)
        End Sub

        <TestMethod>
        Public Sub TheSupportQueue_KeepsItsDescendingOrder()
            Dim result = BrowseSqlWrapper.TryWrap(HdIssuesSupportSql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[PK] DESC")
        End Sub

        <TestMethod>
        Public Sub ThePageGenerationPage_SelectsDeletedFlagAndOrdersByAnOutputName()
            Dim names As List(Of String) = Nothing
            Assert.IsTrue(BrowseSqlWrapper.TryReadOutputNames(PageGenerationSql, names))
            CollectionAssert.Contains(names, "DeletedFlag")

            Dim result = BrowseSqlWrapper.TryWrap(PageGenerationSql, 12, {"ISNULL(q.[DeletedFlag], 0) = 0"})

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[RequestName]")
            StringAssert.Contains(result.Sql, "WHERE ISNULL(q.[DeletedFlag], 0) = 0")
        End Sub

        <TestMethod>
        Public Sub TheSwitchUserPage_RewritesBothOrderTerms()
            Dim result = BrowseSqlWrapper.TryWrap(SwitchUserSql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[LastName] ASC, q.[FirstName] ASC")
        End Sub

        <TestMethod>
        Public Sub TheAccessDiagnostic_OrdersByAColumnFromTheJoinedTable()
            ' E.[FirstLast] is selected without an alias, so its output name is FirstLast and the
            ' order has to find it through the select list rather than through the table prefix.
            Dim result = BrowseSqlWrapper.TryWrap(UserAccessDiagnosticSql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[FirstLast] ASC")
        End Sub

        <TestMethod>
        Public Sub TheRolesPage_Declines_BecauseItsColumnsAreThreePart()
            ' dbo.FW_Roles.DisplayOrder. A select item is read as at most one dot, so the name this
            ' produces cannot be determined and the whole wrap is refused. This is the page that
            ' exercises the fallback, and it is why the fallback had to exist.
            Dim result = BrowseSqlWrapper.TryWrap(RolesSql, 12, Nothing, "PK")

            Assert.IsFalse(result.Wrapped)
            StringAssert.Contains(result.DeclineReason, "select list")
        End Sub

        <TestMethod>
        Public Sub APageWithNoOrderBy_TakesTheDefaultColumn()
            ' FW_HD_Issues_B and FW_Registration_B both have no ORDER BY, and both alias their key
            ' AS PK. Without this they could not be capped at all.
            Dim issues = BrowseSqlWrapper.TryWrap(HdIssuesSql, 12, Nothing, "PK")
            Assert.IsTrue(issues.Wrapped, issues.DeclineReason)
            StringAssert.Contains(issues.Sql, "ORDER BY q.[PK]")

            Dim registration = BrowseSqlWrapper.TryWrap(RegistrationSql, 12, Nothing, "PK")
            Assert.IsTrue(registration.Wrapped, registration.DeclineReason)
            StringAssert.Contains(registration.Sql, "ORDER BY q.[PK]")
        End Sub

        <TestMethod>
        Public Sub ADefaultOrderColumnThatIsNotSelected_Declines()
            ' Roles_B's code-level DefaultSelectSql aliases nothing AS PK. Ordering by a column that
            ' is not in the derived table is a page that will not open, so it declines instead.
            Dim sql = "SELECT RoleID, RegistrationID, RoleName, IsActive, UpdatedOn " &
                      "FROM dbo.FW_Roles WHERE RegistrationID = @RegistrationID"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing, "PK")

            Assert.IsFalse(result.Wrapped)
            StringAssert.Contains(result.DeclineReason, "PK")
        End Sub

        <TestMethod>
        Public Sub ADefaultOrderColumn_IsNeverRawSql()
            ' The parameter is an output name, matched and quoted here. Anything else declines, so
            ' it cannot become a way to put text into the ORDER BY.
            Dim result = BrowseSqlWrapper.TryWrap(RegistrationSql, 12, Nothing, "PK DESC; DROP TABLE x")

            Assert.IsFalse(result.Wrapped)
        End Sub

        <TestMethod>
        Public Sub APageWithAnOrderBy_IgnoresTheDefaultColumn()
            Dim result = BrowseSqlWrapper.TryWrap(SwitchUserSql, 12, Nothing, "PK")

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "ORDER BY q.[LastName] ASC, q.[FirstName] ASC")
        End Sub

        ''' <summary>
        ''' Prints what the wrapper makes of all eight pages, so the statements can be run against a
        ''' real database and compared to what the pages return today. Asserts nothing about the
        ''' text - the tests above do that - and exists because the semantic differences between a
        ''' DataView filter and a WHERE clause will not be found by reading.
        ''' </summary>
        <TestMethod>
        Public Sub PrintEveryRealPageWrapped()
            Dim pages = New List(Of KeyValuePair(Of String, String)) From {
                New KeyValuePair(Of String, String)("FW_Employees_B", EmployeesSql),
                New KeyValuePair(Of String, String)("FW_HD_Issues_B", HdIssuesSql),
                New KeyValuePair(Of String, String)("FW_HD_Issues_Support_B", HdIssuesSupportSql),
                New KeyValuePair(Of String, String)("FW_PageGeneration_B", PageGenerationSql),
                New KeyValuePair(Of String, String)("FW_Registration_B", RegistrationSql),
                New KeyValuePair(Of String, String)("FW_SwitchUser_B", SwitchUserSql),
                New KeyValuePair(Of String, String)("FW_UserAccessDiagnostic_B", UserAccessDiagnosticSql),
                New KeyValuePair(Of String, String)("Roles_B", RolesSql)
            }

            Dim report As New System.Text.StringBuilder()

            For Each page In pages
                Dim result = BrowseSqlWrapper.TryWrap(page.Value, 12, Nothing, "PK")
                report.AppendLine("===== " & page.Key & " =====")
                If result.Wrapped Then
                    report.AppendLine(result.Sql)
                Else
                    report.AppendLine("DECLINED: " & result.DeclineReason)
                End If
                report.AppendLine()
            Next

            Console.WriteLine(report.ToString())
        End Sub

        <TestMethod>
        Public Sub DistinctIsUnderstood()
            Dim sql = "SELECT DISTINCT E.[A] AS PK FROM dbo.[T] E ORDER BY E.[A]"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "SELECT DISTINCT E.[A] AS PK")
        End Sub

    End Class

End Namespace
