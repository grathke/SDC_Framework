Option Strict On
Option Explicit On

Imports System.Data
Imports System.Globalization
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' A search row offers a day; the column may hold a time. These assert what a chosen day
    ''' means, and then prove it against a real DataView - which is where most browse pages
    ''' actually filter, and the reason a date compared as text found only the rows that happened
    ''' to sit at midnight.
    ''' </summary>
    <TestClass>
    Public Class QbeDateBoundsTests

        Private ReadOnly Day14 As Date = New Date(2026, 9, 14)
        Private ReadOnly Day15 As Date = New Date(2026, 9, 15)

        <TestMethod>
        Public Sub Equals_IsTheWholeDay()
            Dim limits = QbeDateBounds.Resolve(QbeComparisonOperator.EqualsTo, Day14)
            Assert.AreEqual(Day14, limits.Lower.Value)
            Assert.AreEqual(Day15, limits.Upper.Value)
            Assert.IsFalse(limits.Excluded)
        End Sub

        <TestMethod>
        Public Sub NotEquals_IsEverythingOutsideTheDay()
            Dim limits = QbeDateBounds.Resolve(QbeComparisonOperator.NotEquals, Day14)
            Assert.AreEqual(Day14, limits.Lower.Value)
            Assert.AreEqual(Day15, limits.Upper.Value)
            Assert.IsTrue(limits.Excluded)
        End Sub

        <TestMethod>
        Public Sub GreaterThan_StartsAtTheNextDay()
            ' Not "after midnight on the 14th", which would keep the 14th's afternoon rows.
            Dim limits = QbeDateBounds.Resolve(QbeComparisonOperator.GreaterThan, Day14)
            Assert.AreEqual(Day15, limits.Lower.Value)
            Assert.IsFalse(limits.Upper.HasValue)
        End Sub

        <TestMethod>
        Public Sub GreaterThanOrEqual_StartsAtTheDay()
            Dim limits = QbeDateBounds.Resolve(QbeComparisonOperator.GreaterThanOrEqual, Day14)
            Assert.AreEqual(Day14, limits.Lower.Value)
            Assert.IsFalse(limits.Upper.HasValue)
        End Sub

        <TestMethod>
        Public Sub LessThan_EndsAtTheDay()
            Dim limits = QbeDateBounds.Resolve(QbeComparisonOperator.LessThan, Day14)
            Assert.IsFalse(limits.Lower.HasValue)
            Assert.AreEqual(Day14, limits.Upper.Value)
        End Sub

        <TestMethod>
        Public Sub LessThanOrEqual_EndsAtTheNextDay()
            ' The one that loses a day's work when it is written as <= the day itself.
            Dim limits = QbeDateBounds.Resolve(QbeComparisonOperator.LessThanOrEqual, Day14)
            Assert.IsFalse(limits.Lower.HasValue)
            Assert.AreEqual(Day15, limits.Upper.Value)
        End Sub

        <TestMethod>
        Public Sub TimeOnTheChosenValue_IsDiscarded()
            Dim limits = QbeDateBounds.Resolve(QbeComparisonOperator.EqualsTo, New Date(2026, 9, 14, 16, 40, 0))
            Assert.AreEqual(Day14, limits.Lower.Value)
            Assert.AreEqual(Day15, limits.Upper.Value)
        End Sub

        <TestMethod>
        Public Sub FilterValue_RoundTripsAsIso()
            Assert.AreEqual("2026-09-14", QbeDateBounds.ToFilterValue(New Date(2026, 9, 14, 16, 40, 0)))

            Dim parsed As Date
            Assert.IsTrue(QbeDateBounds.TryParseFilterValue("2026-09-14", parsed))
            Assert.AreEqual(Day14, parsed)
        End Sub

        <TestMethod>
        Public Sub FilterValue_StillReadsAnOlderSavedSearch()
            ' Saved searches predate dates being typed at all, and hold whatever was keyed in.
            Dim parsed As Date
            Assert.IsTrue(QbeDateBounds.TryParseFilterValue("09/14/2026", parsed))
            Assert.AreEqual(Day14, parsed)

            Assert.IsFalse(QbeDateBounds.TryParseFilterValue("not a date", parsed))
            Assert.IsFalse(QbeDateBounds.TryParseFilterValue("", parsed))
        End Sub

        ''' <summary>
        ''' Three rows: the 14th at midnight, the 14th in the afternoon, the 15th in the morning.
        ''' The afternoon row is the one every one of these is really about.
        ''' </summary>
        Private Shared Function ThreeRows() As DataTable
            Dim table As New DataTable()
            table.Columns.Add("When", GetType(Date))
            table.Rows.Add(New Date(2026, 9, 14, 0, 0, 0))
            table.Rows.Add(New Date(2026, 9, 14, 16, 40, 0))
            table.Rows.Add(New Date(2026, 9, 15, 9, 0, 0))
            Return table
        End Function

        Private Shared Function CountMatching(comparisonOperator As QbeComparisonOperator, value As Date) As Integer
            Dim expr = QbeDateBounds.ToDataViewExpression("When", QbeDateBounds.Resolve(comparisonOperator, value))
            Using view As New DataView(ThreeRows()) With {.RowFilter = expr}
                Return view.Count
            End Using
        End Function

        <TestMethod>
        Public Sub Equals_FindsTheAfternoonRowToo()
            Assert.AreEqual(2, CountMatching(QbeComparisonOperator.EqualsTo, Day14))
        End Sub

        <TestMethod>
        Public Sub NotEquals_LeavesOnlyTheOtherDay()
            Assert.AreEqual(1, CountMatching(QbeComparisonOperator.NotEquals, Day14))
        End Sub

        <TestMethod>
        Public Sub GreaterThan_TheFourteenth_IsTheFifteenthOnly()
            Assert.AreEqual(1, CountMatching(QbeComparisonOperator.GreaterThan, Day14))
        End Sub

        <TestMethod>
        Public Sub LessThanOrEqual_TheFourteenth_KeepsItsAfternoon()
            Assert.AreEqual(2, CountMatching(QbeComparisonOperator.LessThanOrEqual, Day14))
        End Sub

        <TestMethod>
        Public Sub Between_IsTheTwoHalvesAndKeepsTheLastAfternoon()
            ' What a Between row expands into: on or after the first day, on or before the last.
            Dim lower = QbeDateBounds.ToDataViewExpression(
                "When", QbeDateBounds.Resolve(QbeComparisonOperator.GreaterThanOrEqual, Day14))
            Dim upper = QbeDateBounds.ToDataViewExpression(
                "When", QbeDateBounds.Resolve(QbeComparisonOperator.LessThanOrEqual, Day14))

            Using view As New DataView(ThreeRows()) With {.RowFilter = lower & " AND " & upper}
                Assert.AreEqual(2, view.Count)
            End Using
        End Sub

        <TestMethod>
        Public Sub TheLiteralIsReadTheSameWayInAnyCulture()
            ' Measured on 2026-09-21 rather than assumed. A browse page runs on a server whose
            ' culture nobody chose, and a filter that means one day there and another here is the
            ' kind of fault that never reproduces.
            Dim original = Threading.Thread.CurrentThread.CurrentCulture
            Try
                For Each cultureName In New String() {"en-US", "en-GB", "de-DE"}
                    Threading.Thread.CurrentThread.CurrentCulture = New CultureInfo(cultureName)
                    Assert.AreEqual(2, CountMatching(QbeComparisonOperator.EqualsTo, Day14), cultureName)
                Next
            Finally
                Threading.Thread.CurrentThread.CurrentCulture = original
            End Try
        End Sub

    End Class

End Namespace
