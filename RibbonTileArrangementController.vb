Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace HelloWorld

    ''' <summary>
    ''' Lets an App Admin rearrange the main menu's ribbon tiles, and remembers the arrangement.
    '''
    ''' Takes the FlowLayoutPanel rather than the form, so a menu shaped differently simply does not
    ''' call this. One arrangement per surface, keyed on each tile's ActionKey and shared by every
    ''' user - which is the point: the ribbon is the same ribbon for everybody.
    '''
    ''' Why an order and not a position
    ''' -------------------------------
    ''' The panel closes the gap when a permission hides a tile, for free, because a FlowLayoutPanel
    ''' skips invisible children. Nothing in the code does it, so nothing in the code can be relied
    ''' on to keep doing it - except by not fighting it. A flow child's Location belongs to the
    ''' panel, so a move here is SetChildIndex, and what is written down is a rank along the row.
    ''' Saving coordinates would leave a hole where a hidden tile used to be, which is exactly the
    ''' behaviour this is meant to preserve.
    '''
    ''' The rank is sparse. Every role sees a different subset of the tiles, and a menu swapped in
    ''' for another application may not have all of these keys at all, so a saved rank is a sort key
    ''' rather than a slot number. A tile with no saved rank keeps its source order, after the ones
    ''' that have been placed.
    ''' </summary>
    Public NotInheritable Class RibbonTileArrangementController

        ''' <summary>
        ''' How far the mouse must travel before a press becomes a drag rather than a click.
        '''
        ''' Without it every tile would be unopenable: a click always moves the mouse a pixel or
        ''' two, and each of those would land as a no-op drag that swallowed the click.
        ''' </summary>
        Private Const DragThreshold As Integer = 5

        ''' <summary>
        ''' Where an unranked tile sorts. Above any rank a drop can write, so tiles the arrangement
        ''' has never heard of fall in after the ones it has, in the order their source added them.
        ''' </summary>
        Private Const UnrankedBase As Integer = 100000

        ''' <summary>
        ''' Where an anchored tile sorts. Below any rank a drop can write, so an anchored tile leads
        ''' the row whatever the saved arrangement says about it.
        ''' </summary>
        Private Const AnchoredBase As Integer = -100000

        Private ReadOnly owner As Form
        Private ReadOnly panel As FlowLayoutPanel
        Private ReadOnly surfaceName As String
        Private ReadOnly userId As Integer
        Private ReadOnly keysByButton As New Dictionary(Of Control, String)()

        ''' Which buttons currently carry the drag handlers, so a role change can add or remove them
        ''' without wiring a second copy or removing one that was never there.
        Private ReadOnly wired As New HashSet(Of Control)()

        ''' Which buttons report mouse movement into the drag. Every tile does, not only the movable
        ''' ones - see the comment where they are wired.
        Private ReadOnly wiredMove As New HashSet(Of Control)()

        ''' <summary>
        ''' Tiles that are fixed but are not this panel's to arrange - the pinned row on the right.
        '''
        ''' They get the same "Fixed position" tooltip and the same role gating, because to an App
        ''' Admin they look exactly as draggable as the ones that are. They are deliberately not
        ''' wired for movement: they live in another panel, and feeding their coordinates into this
        ''' panel's drag would move a tile to wherever the pointer had wandered off to.
        ''' </summary>
        Private ReadOnly fixedElsewhere As New List(Of Control)()

        ''' What an anchored tile says when an App Admin hovers it. Short on purpose - it sits over
        ''' a ribbon, not a dialog, and a sentence there is noise.
        Private Const AnchoredTileTip As String = "Fixed position"

        ''' What a movable tile says. The counterpart to the anchored one, and the pair only works
        ''' as a pair: "Fixed position" alone names the tiles that cannot move but leaves the ones
        ''' that can carrying no tooltip at all, which reads as a tile nobody has got to yet rather
        ''' than as an answer.
        Private Const MovableTileTip As String = "Moveable"

        Private ReadOnly tips As New ToolTip()

        ''' <summary>
        ''' Tiles pinned to the head of the row, in this order, which cannot be dragged and which
        ''' nothing can be dropped in front of.
        '''
        ''' Supplied by the caller rather than named here: which tile leads a ribbon is the
        ''' application's decision, and this controller serves whichever menu hands it a panel.
        '''
        ''' Anchoring is not visibility. An anchored tile hidden by a permission still leaves the
        ''' row to close up behind it, because the panel skips invisible children whatever their
        ''' index.
        ''' </summary>
        Private ReadOnly anchoredKeys As List(Of String)

        ''' <summary>
        ''' How thick the drop-span bar is, and what colour.
        '''
        ''' Three pixels of the six clear ones below the row, so it reads as a deliberate mark
        ''' rather than a hairline, and still sits clear of the tiles. The colour is the ribbon's
        ''' existing hover blue - the drag is already speaking in that colour, and a second one
        ''' would only add a thing to interpret.
        ''' </summary>
        Private Const DragBarThickness As Integer = 3

        Private Shared ReadOnly DragBarColor As Color = Color.FromArgb(91, 161, 217)

        Private dragged As Control
        Private dragStart As Point
        Private dragging As Boolean

        ''' Whether the drop-span bar is currently drawn. Held rather than read back off the panel,
        ''' because the bar is painted and there is nothing on the panel to read.
        Private showingDragBounds As Boolean

        Public Sub New(owner As Form,
                       panel As FlowLayoutPanel,
                       surfaceName As String,
                       userId As Integer,
                       ParamArray anchoredKeys As String())
            Me.owner = owner
            Me.panel = panel
            Me.surfaceName = If(surfaceName, String.Empty)
            Me.userId = userId
            Me.anchoredKeys = If(anchoredKeys, New String() {}).
                Where(Function(key) Not String.IsNullOrWhiteSpace(key)).
                ToList()

            ' Wired once here rather than per role. The handler draws nothing unless a drag is under
            ' way, and only an App Admin can start one, so the role gate is already upstream of it.
            AddHandler panel.Paint, AddressOf Panel_Paint
        End Sub

        Private Function AnchorPosition(key As String) As Integer
            If String.IsNullOrWhiteSpace(key) Then Return -1
            Return anchoredKeys.FindIndex(Function(anchored) String.Equals(anchored, key, StringComparison.OrdinalIgnoreCase))
        End Function

        Private Function IsAnchored(child As Control) As Boolean
            Dim key As String = Nothing
            If child Is Nothing OrElse Not keysByButton.TryGetValue(child, key) Then Return False
            Return AnchorPosition(key) >= 0
        End Function

        ''' <summary>
        ''' Applies the saved arrangement, and - for an App Admin - makes the tiles draggable.
        '''
        ''' Nothing about the ribbon looks different until a drag is actually under way. An App
        ''' Admin who is only using the menu sees the ribbon everybody else sees. A bar marking the
        ''' span a tile can be dropped within appears while one is being moved, and goes again the
        ''' moment it is dropped.
        '''
        ''' Safe to call again. A tile already known is not re-wired, so a later pass that finds a
        ''' newly added tile picks it up without doubling the handlers on the rest.
        ''' </summary>
        Public Sub Attach(tiles As IEnumerable(Of KeyValuePair(Of String, Control)))
            If tiles IsNot Nothing Then
                For Each tile In tiles
                    Dim key = tile.Key
                    Dim button = tile.Value
                    If button Is Nothing OrElse String.IsNullOrWhiteSpace(key) Then Continue For
                    If keysByButton.ContainsKey(button) Then Continue For

                    keysByButton(button) = key
                Next
            End If

            ApplyRoleAffordances()
            ApplySavedOrder()
        End Sub

        ''' <summary>
        ''' Wires or unwires dragging to match the role the session is running under now, and takes
        ''' down any drop-span bar a role switch caught mid-move.
        '''
        ''' Run on every pass, not only the first. The menu offers a role switch and reconfigures
        ''' itself in place, so a session can become an App Admin - or stop being one - without the
        ''' form being rebuilt. Wiring once at startup left the handlers attached for a role that no
        ''' longer should have them.
        '''
        ''' An anchored tile never gets handlers whatever the role: it is not draggable at all.
        ''' Letting it be dragged and then snapping it back would read as a fault rather than as a
        ''' tile that stays put.
        ''' </summary>
        Private Sub ApplyRoleAffordances()
            Dim canArrange = SessionState.IsApplicationAdmin

            For Each pair In keysByButton
                Dim button = pair.Key
                Dim anchored = AnchorPosition(pair.Value) >= 0

                ' Every tile says which kind it is, because they all look the same. An App Admin who
                ' drags an anchored one gets silence and cannot tell whether it is fixed by design
                ' or the rearranging is broken - and naming only the fixed ones leaves the movable
                ' ones silent, which reads the same way. Only an App Admin sees either: nobody else
                ' can drag anything, so for them both would answer a question they never asked.
                tips.SetToolTip(button,
                                If(canArrange,
                                   If(anchored, AnchoredTileTip, MovableTileTip),
                                   String.Empty))

                ' MouseMove goes on every tile, anchored ones included, and does nothing unless a
                ' drag is already under way. It is a second route to the same handler: if the tile
                ' holding the drag ever loses capture, the tile the pointer is actually over reports
                ' the position instead, and the drag carries on rather than dying silently. An
                ' anchored tile still cannot start one - it gets no MouseDown.
                If canArrange AndAlso Not wiredMove.Contains(button) Then
                    AddHandler button.MouseMove, AddressOf Tile_MouseMove
                    wiredMove.Add(button)
                ElseIf Not canArrange AndAlso wiredMove.Contains(button) Then
                    RemoveHandler button.MouseMove, AddressOf Tile_MouseMove
                    wiredMove.Remove(button)
                End If

                If anchored Then Continue For

                If canArrange AndAlso Not wired.Contains(button) Then
                    AddHandler button.MouseDown, AddressOf Tile_MouseDown
                    AddHandler button.MouseUp, AddressOf Tile_MouseUp
                    wired.Add(button)
                ElseIf Not canArrange AndAlso wired.Contains(button) Then
                    RemoveHandler button.MouseDown, AddressOf Tile_MouseDown
                    RemoveHandler button.MouseUp, AddressOf Tile_MouseUp
                    wired.Remove(button)
                End If
            Next

            For Each button In fixedElsewhere
                tips.SetToolTip(button, If(canArrange, AnchoredTileTip, String.Empty))
            Next

            ' Not shown to an App Admin merely because they could drag something - only while they
            ' are. A role switch cannot land in the middle of a drag, but this clears the bar
            ' rather than assuming it: nothing else here would ever take one down.
            ShowDragBounds(False)
        End Sub

        ''' <summary>
        ''' Registers tiles that are fixed but sit outside this panel, so they carry the same
        ''' tooltip under the same rule. Safe to call again; a tile already registered is ignored.
        ''' </summary>
        Public Sub MarkFixedElsewhere(tiles As IEnumerable(Of Control))
            If tiles Is Nothing Then Return

            For Each tile In tiles
                If tile Is Nothing OrElse fixedElsewhere.Contains(tile) Then Continue For
                fixedElsewhere.Add(tile)
            Next

            ApplyRoleAffordances()
        End Sub

        ''' <summary>
        ''' Puts the tiles back in their saved order.
        '''
        ''' Ranked tiles first, in rank order; the rest after, in the order the panel already has
        ''' them. Hidden tiles are ordered along with the visible ones - they are still children,
        ''' and leaving them out would move them to the end the moment a permission hid them.
        ''' </summary>
        Public Sub ApplySavedOrder()
            Dim saved = DataAccess.GetDashboardIconPositions(surfaceName)
            If keysByButton.Count = 0 Then Return

            Dim ordered = panel.Controls.Cast(Of Control)().
                Select(Function(child, index) New With {
                    .Child = child,
                    .Sort = RankOf(child, saved, index)
                }).
                OrderBy(Function(item) item.Sort).
                Select(Function(item) item.Child).
                ToList()

            panel.SuspendLayout()
            For position = 0 To ordered.Count - 1
                panel.Controls.SetChildIndex(ordered(position), position)
            Next
            panel.ResumeLayout()
        End Sub

        Private Function RankOf(child As Control, saved As Dictionary(Of String, Point), sourceIndex As Integer) As Integer
            Dim key As String = Nothing
            If keysByButton.TryGetValue(child, key) Then
                ' An anchor beats anything written down. A rank saved before a tile was anchored, or
                ' by an older build, cannot pull it out of the head of the row.
                Dim anchor = AnchorPosition(key)
                If anchor >= 0 Then Return AnchoredBase + anchor

                Dim cell As Point
                ' The rank is the column: the ribbon is a one-row grid, so GridColumn is the
                ' position along the row and GridRow is always 1.
                If saved.TryGetValue(key, cell) Then Return cell.X
            End If

            Return UnrankedBase + sourceIndex
        End Function

        ''' <summary>
        ''' Shows or hides the bar marking the span a tile can actually be dropped within.
        '''
        ''' The panel's own border was the first attempt and was wrong twice over. It outlined the
        ''' whole panel, anchored head included - so it promised a drop zone that three tiles at the
        ''' front will not accept - and turning a BorderStyle on mid-drag costs the client area a
        ''' pixel each way, which nudged the whole row as it appeared.
        '''
        ''' A bar in the gutter has neither problem. It starts at the first tile that can move, so
        ''' the anchored head is visibly outside it, and it changes no geometry at all.
        ''' </summary>
        Private Sub ShowDragBounds(visible As Boolean)
            If showingDragBounds = visible Then Return

            showingDragBounds = visible
            panel.Invalidate()
        End Sub

        ''' <summary>
        ''' Draws the drop span: a bar under the tiles, from the first one that can move to the end
        ''' of the row.
        '''
        ''' It goes in the gutter because that is the only part of the panel nothing covers. A tile
        ''' is 96 of the panel's 102 pixels and sits flush against the top and the left edge, so a
        ''' rectangle drawn round the span would be hidden behind the tiles on three sides; the 6
        ''' pixels below the row are clear.
        '''
        ''' Read left to right it says where a drag stops: nothing before the bar will take a drop,
        ''' which is exactly what the anchored tiles do. If a permission has hidden every movable
        ''' tile there is no span to draw and nothing is drawn.
        ''' </summary>
        Private Sub Panel_Paint(sender As Object, e As PaintEventArgs)
            If Not showingDragBounds Then Return

            Dim firstMovable As Control = Nothing
            Dim lastVisible As Control = Nothing

            For Each child As Control In panel.Controls
                If Not child.Visible Then Continue For

                lastVisible = child
                If firstMovable Is Nothing AndAlso Not IsAnchored(child) Then firstMovable = child
            Next

            If firstMovable Is Nothing OrElse lastVisible Is Nothing Then Return
            If lastVisible.Right <= firstMovable.Left Then Return

            Dim top = panel.ClientSize.Height - DragBarThickness
            Using bar As New SolidBrush(DragBarColor)
                e.Graphics.FillRectangle(bar,
                                         firstMovable.Left,
                                         top,
                                         lastVisible.Right - firstMovable.Left,
                                         DragBarThickness)
            End Using
        End Sub

        Private Sub Tile_MouseDown(sender As Object, e As MouseEventArgs)
            If e.Button <> MouseButtons.Left Then Return

            dragged = TryCast(sender, Control)
            If dragged Is Nothing Then Return

            ' Taken explicitly, and not left to the button.
            '
            ' Without capture held here, the moment the pointer left the tile it was pressed on that
            ' tile stopped receiving MouseMove, so the drag could never see a position over any
            ' other tile and nothing ever swapped. The giveaway was the hover highlight appearing on
            ' the tiles being dragged across: they could only light up because the pointer messages
            ' were reaching them instead of the tile holding the drag.
            dragged.Capture = True

            dragStart = panel.PointToClient(Control.MousePosition)
            dragging = False
        End Sub

        ''' <summary>
        ''' Moves the dragged tile one slot at a time, as the pointer passes the middle of the
        ''' neighbour it is heading for.
        '''
        ''' One slot rather than a computed destination, and measured against the neighbour rather
        ''' than against the tile being dragged: the row reflows the instant an index changes, so a
        ''' rule that read the dragged tile's own position would move it back and forth under a
        ''' stationary cursor.
        ''' </summary>
        Private Sub Tile_MouseMove(sender As Object, e As MouseEventArgs)
            If dragged Is Nothing Then Return

            Dim current = panel.PointToClient(Control.MousePosition)
            If Not dragging Then
                If Math.Abs(current.X - dragStart.X) < DragThreshold AndAlso
                   Math.Abs(current.Y - dragStart.Y) < DragThreshold Then Return

                dragging = True

                ' Marked now, not on release. A button raises its Click from OnMouseUp before the
                ' MouseUp handlers run, so a drag that only decided at the end would already have
                ' opened the page it was dragged from.
                Dim tile = TryCast(dragged, SuppressClickButton)
                If tile IsNot Nothing Then tile.SuppressNextClick = True

                ShowDragBounds(True)
            End If

            Dim index = panel.Controls.GetChildIndex(dragged)
            Dim target = IndexUnderPointer(current.X)
            If target < 0 OrElse target = index Then Return

            ' Nothing goes in front of an anchored tile, so a drag towards the head of the row stops
            ' when it reaches one.
            If IsAnchored(panel.Controls(target)) Then Return

            panel.Controls.SetChildIndex(dragged, target)
        End Sub

        ''' <summary>
        ''' The visible tile the pointer is over, as a child index, or the last one it is past.
        '''
        ''' The dragged tile takes that slot as soon as the pointer enters it, which is what makes
        ''' the row react at once. The first attempt compared the pointer against the neighbour's
        ''' midpoint instead, so nothing moved until the mouse had travelled the better part of two
        ''' tiles - and since a flow panel owns its children's positions, the tile does not follow
        ''' the cursor in the meantime, so a shorter drag looked like a ribbon that would not move
        ''' at all.
        '''
        ''' Once swapped, the pointer is inside the dragged tile at its new slot, so the next move
        ''' finds the same index and does nothing. That is what stops it oscillating under a
        ''' stationary cursor.
        '''
        ''' Hidden tiles are skipped. They hold an index but occupy no space, so landing on one
        ''' would trade places with something the user cannot see.
        ''' </summary>
        Private Function IndexUnderPointer(x As Integer) As Integer
            Dim lastBefore As Integer = -1

            For position = 0 To panel.Controls.Count - 1
                Dim child = panel.Controls(position)
                If Not child.Visible Then Continue For

                If x < child.Left Then Exit For
                If x <= child.Right Then Return position

                lastBefore = position
            Next

            Return lastBefore
        End Function

        Private Sub Tile_MouseUp(sender As Object, e As MouseEventArgs)
            Dim moved = dragged
            dragged = Nothing
            If moved Is Nothing Then Return

            moved.Capture = False

            ' Unconditionally, not only on the drag path: a press that never became one never showed
            ' the bar, and ShowDragBounds is a no-op when it is already down.
            ShowDragBounds(False)

            If Not dragging Then
                ' Never passed the threshold, so this was a click. The tile's own Click handler
                ' opens the page; nothing to do here but let it.
                Return
            End If

            dragging = False
            SaveOrder()
        End Sub

        ''' <summary>
        ''' Writes a rank for every tile, not only the one that moved: an insertion shifts
        ''' everything after it, so half the row would otherwise be recorded wrong.
        '''
        ''' Hidden tiles are written too. A role that cannot see a tile must not erase where it
        ''' sits for the roles that can.
        ''' </summary>
        Private Sub SaveOrder()
            Dim ranks As New List(Of KeyValuePair(Of String, Integer))()
            Dim rank = 1

            For Each child As Control In panel.Controls
                Dim key As String = Nothing
                If Not keysByButton.TryGetValue(child, key) Then Continue For

                ' An anchored tile's place is decided by the anchor, not by the table. Writing a
                ' rank for it would leave a row that means nothing and would be read by nothing.
                If AnchorPosition(key) >= 0 Then Continue For

                ranks.Add(New KeyValuePair(Of String, Integer)(key, rank))
                rank += 1
            Next

            If DataAccess.SaveRibbonTileOrder(surfaceName, ranks, userId) Then Return

            ' The move did not save, so it does not stand. Putting the tiles back is the honest
            ' outcome: an arrangement that looks kept and is gone tomorrow is worse than one that
            ' visibly refused.
            ApplySavedOrder()

            MessageBox.Show(owner,
                            "THE NEW ARRANGEMENT COULD NOT BE SAVED, SO THE BUTTONS HAVE BEEN PUT BACK.",
                            "MENU LAYOUT",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub
    End Class
End Namespace
