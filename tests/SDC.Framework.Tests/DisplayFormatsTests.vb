Option Strict On
Option Explicit On

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' How the Registration page lists its date and time formats: the pattern in plain letters,
    ''' then what it produces. Every stored pattern is checked, so a row added later that the
    ''' translation mangles fails here rather than on somebody's screen.
    ''' </summary>
    <TestClass>
    Public Class DisplayFormatsTests

        <DataTestMethod>
        <DataRow("MM/dd/yyyy", "MM/DD/YYYY")>
        <DataRow("MM/dd/yy", "MM/DD/YY")>
        <DataRow("dd/MM/yyyy", "DD/MM/YYYY")>
        <DataRow("yyyy-MM-dd", "YYYY-MM-DD")>
        <DataRow("MMM d, yyyy", "Mmm D, YYYY")>
        <DataRow("d MMM yyyy", "D Mmm YYYY")>
        <DataRow("MMMM d, yyyy", "Mmmm D, YYYY")>
        <DataRow("dddd dd MMMM yyyy", "Dddd DD Mmmm YYYY")>
        Public Sub DatePatternsReadAsTheyAreWritten(pattern As String, expected As String)
            Assert.AreEqual(expected, DisplayFormats.ReadablePattern(pattern))
        End Sub

        <DataTestMethod>
        <DataRow("hh:mm tt", "hh:mm AM")>
        <DataRow("h:mm tt", "h:mm AM")>
        <DataRow("hh:mm:ss tt", "hh:mm:ss AM")>
        <DataRow("h:mm:ss tt", "h:mm:ss AM")>
        <DataRow("HH:mm", "HH:mm")>
        <DataRow("HH:mm:ss", "HH:mm:ss")>
        Public Sub TimePatternsKeepMinutesApartFromMonths(pattern As String, expected As String)
            Assert.AreEqual(expected, DisplayFormats.ReadablePattern(pattern))
        End Sub

        <DataTestMethod>
        <DataRow("hh:mm tt", "09:05 AM      02:30 PM")>
        <DataRow("h:mm tt", "9:05 AM      2:30 PM")>
        <DataRow("HH:mm", "09:05      14:30")>
        Public Sub TimeSamplesShowTheLeadingZeroAndTheClock(pattern As String, expected As String)
            Assert.AreEqual(expected, DisplayFormats.TimeSamplesOf(pattern, DisplayFormats.DefaultTimePattern))
        End Sub

    End Class

End Namespace
