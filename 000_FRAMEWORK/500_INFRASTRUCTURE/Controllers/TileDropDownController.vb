Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Drops a menu below a tile, and owns how that menu behaves: click to open, and close on its
    ''' own once the pointer is over neither the menu nor the tile. Choosing an item closes it, and
    ''' so does clicking anywhere else.
    '''
    ''' Clicking the tile a second time does **not** close it, and that was decided rather than
    ''' overlooked. An open ContextMenuStrip holds the mouse, so such a click is a dismissal before
    ''' it is ever a button click: the menu is already gone by the time the tile's Click runs, which
    ''' reopens it in the same frame and looks like nothing happened. Suppressing that reopen from
    ''' the Closed event was tried on 2026-09-03 and did not work - Closed does not appear to run
    ''' before Click - and with pointer-away closing already covering the case, a second attempt was
    ''' not judged worth it. See MENU-22 in TEST_CASES.md before trying again.
    '''
    ''' A controller rather than handlers on each tile, for the reason IconImageController is one:
    ''' every tile that grows a menu wants the same behaviour, and the second copy is how the pair
    ''' of them drift apart. A caller supplies what its menu contains. It does not get to say how
    ''' the menu opens or closes, because a flag for that is how two menus end up behaving
    ''' differently for no reason anybody remembers.
    '''
    ''' A ContextMenuStrip and not a panel, because it is its own top-level window and so drops over
    ''' the regions below. A child panel would be clipped at the ribbon's edge, which is the whole
    ''' difficulty this answers. It is shown explicitly rather than assigned to the tile's
    ''' ContextMenuStrip property: that property answers the right button, and on every tile here it
    ''' is already taken by the App Admin icon picker.
    ''' </summary>
    Public NotInheritable Class TileDropDownController

        ''' <summary>
        ''' How far outside the menu and the tile still counts as being on them, so that a diagonal
        ''' move across the seam between the two does not clip a corner and register as having left.
        ''' </summary>
        Private Const SeamPadding As Integer = 6

        Private Const PointerPollMs As Integer = 200

        ''' <summary>
        ''' One menu per tile, built on first use and kept. Keyed by the control itself, which
        ''' compares by reference - two tiles are never the same key.
        ''' </summary>
        Private ReadOnly menusByTile As New Dictionary(Of Control, ContextMenuStrip)()

        Private openMenu As ContextMenuStrip
        Private openTile As Control
        Private pointerWatcher As Timer

        ''' <summary>
        ''' Opens the tile's menu below it.
        '''
        ''' Call from the tile's own Click. The items are asked for once, the first time a given
        ''' tile is opened, so a caller may build them from state that does not exist yet when the
        ''' tile is created.
        ''' </summary>
        Public Sub Open(tile As Control, buildItems As Func(Of IEnumerable(Of ToolStripItem)))
            If tile Is Nothing OrElse buildItems Is Nothing Then
                Return
            End If

            Dim menu = MenuFor(tile, buildItems)
            If menu Is Nothing OrElse menu.Items.Count = 0 Then
                Return
            End If

            openMenu = menu
            openTile = tile

            ' The same font as the tile's caption. The menu took the system's 9pt default, which read
            ' as a footnote under a 12pt caption. Set on every opening rather than once when the menu
            ' is built, because the menu is kept and reused, and F9 changes the tile's font in between.
            If tile.Font IsNot Nothing AndAlso Not Equals(menu.Font, tile.Font) Then
                menu.Font = tile.Font
            End If

            ' Anchored to the tile's bottom-left corner.
            menu.Show(tile, New Point(0, tile.Height))
            StartPointerWatcher()
        End Sub

        Private Function MenuFor(tile As Control,
                                 buildItems As Func(Of IEnumerable(Of ToolStripItem))) As ContextMenuStrip
            Dim menu As ContextMenuStrip = Nothing
            If menusByTile.TryGetValue(tile, menu) Then
                Return menu
            End If

            menu = New ContextMenuStrip()

            Dim items = buildItems()
            If items IsNot Nothing Then
                For Each item In items
                    If item IsNot Nothing Then
                        menu.Items.Add(item)
                    End If
                Next
            End If

            AddHandler menu.Closed, AddressOf Menu_Closed
            menusByTile(tile) = menu
            Return menu
        End Function

        ''' <summary>
        ''' Lets go of the menu that has closed, however it closed, so the watcher stops and the
        ''' next open starts from a clean slate.
        ''' </summary>
        Private Sub Menu_Closed(sender As Object, e As ToolStripDropDownClosedEventArgs)
            If sender Is openMenu Then
                openMenu = Nothing
                openTile = Nothing
                StopPointerWatcher()
            End If
        End Sub

        ''' <summary>
        ''' Closes the menu once the pointer is over neither the menu nor the tile that opened it.
        '''
        ''' Polled rather than driven by MouseLeave. The pointer crosses from the tile to the menu
        ''' and back between two separate top-level windows, and each crossing raises a leave on one
        ''' of them - so closing on leave would shut the menu the instant somebody moved towards it.
        ''' Asking where the pointer actually is answers the question once, for both.
        ''' </summary>
        Private Sub StartPointerWatcher()
            If pointerWatcher Is Nothing Then
                pointerWatcher = New Timer() With {.Interval = PointerPollMs}
                AddHandler pointerWatcher.Tick, AddressOf PointerWatcher_Tick
            End If

            pointerWatcher.Start()
        End Sub

        Private Sub StopPointerWatcher()
            If pointerWatcher IsNot Nothing Then
                pointerWatcher.Stop()
            End If
        End Sub

        Private Sub PointerWatcher_Tick(sender As Object, e As EventArgs)
            If openMenu Is Nothing OrElse Not openMenu.Visible Then
                StopPointerWatcher()
                Return
            End If

            Dim overMenu = Rectangle.Inflate(openMenu.Bounds, SeamPadding, SeamPadding).Contains(Cursor.Position)
            If overMenu OrElse PointerIsOver(openTile) Then
                Return
            End If

            openMenu.Close()
        End Sub

        Private Shared Function PointerIsOver(control As Control) As Boolean
            If control Is Nothing OrElse Not control.IsHandleCreated Then
                Return False
            End If

            Dim bounds = control.RectangleToScreen(control.ClientRectangle)
            Return Rectangle.Inflate(bounds, SeamPadding, SeamPadding).Contains(Cursor.Position)
        End Function
    End Class
End Namespace
