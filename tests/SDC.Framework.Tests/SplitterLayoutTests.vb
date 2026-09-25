Option Strict On
Option Explicit On

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' The largest splitter distance leaves room for the bar itself. Computed without it, the bar
    ''' ran six pixels past the bottom edge and GDI+ threw from inside the layout (fault #7).
    ''' QbeSplitPanel and every SplitContainer share this one arithmetic.
    ''' </summary>
    <TestClass>
    Public Class SplitterLayoutTests

        <TestMethod>
        Public Sub MaxDistance_LeavesRoomForTheSplitter()
            Assert.AreEqual(300 - 25 - 6, SplitterLayout.MaxDistance(300, 25, 25, 6))
        End Sub

        <TestMethod>
        Public Sub MaxDistance_AtExactlyTheMinimum_IsTheFirstPanelsMinimum()
            Assert.AreEqual(25, SplitterLayout.MaxDistance(56, 25, 25, 6))
        End Sub

        <TestMethod>
        Public Sub MaxDistance_TooShortForBothPanels_HasNoLegalPosition()
            Assert.AreEqual(-1, SplitterLayout.MaxDistance(55, 25, 25, 6))
        End Sub

    End Class
End Namespace
