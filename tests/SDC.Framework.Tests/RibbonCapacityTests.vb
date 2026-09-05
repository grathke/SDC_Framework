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
        Public Sub CapacityAtMinimumWidth_IsSeven()
            ' A ribbon client of 1146 at MinimumSize 1180, less the two 10px insets and the 400px
            ' pinned row, is 726 - seven tiles of 100 with 26 spare.
            Assert.AreEqual(7, FW_MainMenu.MovableTileCapacityAtMinimumWidth())
        End Sub

        <TestMethod>
        Public Sub CapacityIsTheWorstCase_NotTheOrdinaryUsers()
            ' The old constant said 8, which is right only for a session that cannot see
            ' login-as-substitute - it is pinned but offered to an App Admin alone. Sizing to the
            ' session that sees the most is the whole point of the figure, so an eighth tile that no
            ' App Admin could ever see must not be allowed.
            Assert.IsLessThan(8, FW_MainMenu.MovableTileCapacityAtMinimumWidth(),
                              "Capacity must assume every pinned tile is visible, not just three of them.")
        End Sub

        <TestMethod>
        Public Sub CapacityIsPositive()
            ' Guards the subtraction: geometry that made the pinned row wider than the ribbon would
            ' otherwise return a negative capacity and read as a full menu for the wrong reason.
            Assert.IsGreaterThan(0, FW_MainMenu.MovableTileCapacityAtMinimumWidth())
        End Sub

    End Class

End Namespace
