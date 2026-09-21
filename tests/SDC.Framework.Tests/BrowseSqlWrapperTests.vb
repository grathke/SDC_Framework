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
        Public Sub DistinctIsUnderstood()
            Dim sql = "SELECT DISTINCT E.[A] AS PK FROM dbo.[T] E ORDER BY E.[A]"
            Dim result = BrowseSqlWrapper.TryWrap(sql, 12, Nothing)

            Assert.IsTrue(result.Wrapped, result.DeclineReason)
            StringAssert.Contains(result.Sql, "SELECT DISTINCT E.[A] AS PK")
        End Sub

    End Class

End Namespace
