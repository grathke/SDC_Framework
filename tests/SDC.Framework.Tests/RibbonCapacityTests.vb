Option Strict On
Option Explicit On

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' How many movable tiles the main menu ribbon holds.
    '''
    ''' The number was a hand-maintained constant in PageGenerator until 2026-09-05, while every
    ''' value deciding it lived on the menu form. Nothing failed when the two disagreed - the flow
    ''' panel neither wraps nor scrolls, so a tile past the end is not moved and not reachable, it
    ''' is simply not drawn, with nothing said and nothing to see.
    '''
    ''' Pinned here so a change to tile width, margin, panel inset, pinned tile count or minimum
    ''' window width fails a test instead of silently losing somebody a tile.
    '''
    ''' Shared, so no form is constructed and this runs headless like the rest of the suite.
    ''' </summary>
    <TestClass>
    Public Class RibbonCapacityTests

        <TestMethod>
        Public Sub CapacityAtMinimumWidth_IsNine()
            ' A ribbon client of 1226 at MinimumSize 1260, less the two 10px insets and the 300px
            ' pinned row, is 906 - nine tiles of 100 with 6 spare.
            '
            ' It read 7 that morning, then 8 once the minimum width went from 1180 to 1260, then 9
            ' once the pinned row stopped reserving a slot for login-as-substitute, whose button had
            ' been gone since 2026-09-04. Narrow the window minimum or re-pin a tile and this fails,
            ' rather than a tile quietly ceasing to be drawn.
            Assert.AreEqual(9, FW_MainMenu.MovableTileCapacityAtMinimumWidth())
        End Sub

        <TestMethod>
        Public Sub CapacityLeavesRoomForThePinnedRow()
            ' Without the pinned row the same width would give twelve. The subtraction is the whole
            ' reason this function exists rather than a division: drop it and every generated page
            ' would be told there is room where the pinned tiles already are.
            Assert.IsLessThan(12, FW_MainMenu.MovableTileCapacityAtMinimumWidth(),
                              "Capacity must subtract the pinned row, not just divide the ribbon width.")
        End Sub

        <TestMethod>
        Public Sub CapacityIsPositive()
            ' Guards the subtraction: geometry that made the pinned row wider than the ribbon would
            ' otherwise return a negative capacity and read as a full menu for the wrong reason.
            Assert.IsGreaterThan(0, FW_MainMenu.MovableTileCapacityAtMinimumWidth())
        End Sub

    End Class

End Namespace
