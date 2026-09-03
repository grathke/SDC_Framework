Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Lets a dashboard's icons be dragged between grid cells, and remembers where they were put.
    '''
    ''' A controller rather than handlers on the dashboard itself: the two dashboards are separate
    ''' forms with the same grid, and this behaviour should not be written twice to be had twice.
    '''
    ''' One arrangement per dashboard, keyed on each icon's ActionKey - neither dashboard is
    ''' registration aware, so scoping the layout by one would reset it for reasons nobody could
    ''' see. Only moved icons are stored; anything absent stays where its source put it.
    ''' </summary>
    Public NotInheritable Class DashboardIconDragController

        ''' <summary>
        ''' How far the mouse must travel before a press becomes a drag rather than a click.
        '''
        ''' Without it every icon would be unopenable: a click always moves the mouse a pixel or
        ''' two, and each of those would land as a no-op drag that swallowed the click.
        ''' </summary>
        Private Const DragThreshold As Integer = 5

        ' Clear space below the lowest icon, so a caption is not flush against the window edge.
        Private Const BottomMargin As Integer = 20

        Private ReadOnly owner As Form
        Private ReadOnly dashboardName As String
        Private ReadOnly userId As Integer
        Private ReadOnly icons As New List(Of Control)()

        Private dragged As Control
        Private dragStart As Point
        Private dragOrigin As Point
        Private dragging As Boolean

        Public Sub New(owner As Form, dashboardName As String, userId As Integer)
            Me.owner = owner
            Me.dashboardName = If(dashboardName, String.Empty)
            Me.userId = userId
        End Sub

        ''' <summary>
        ''' Takes charge of the icons: applies whatever arrangement was saved, then makes them
        ''' draggable. An icon with no name is left alone - there would be nothing to record a
        ''' position against, and silently moving it would lose the move on the next visit.
        ''' </summary>
        Public Sub Attach(candidates As IEnumerable(Of Control))
            If candidates Is Nothing Then Return

            For Each candidate In candidates
                If candidate Is Nothing OrElse String.IsNullOrWhiteSpace(candidate.Name) Then Continue For

                icons.Add(candidate)
                AddHandler candidate.MouseDown, AddressOf Icon_MouseDown
                AddHandler candidate.MouseMove, AddressOf Icon_MouseMove
                AddHandler candidate.MouseUp, AddressOf Icon_MouseUp
            Next

            ApplySavedPositions()
        End Sub

        ''' <summary>
        ''' Puts every moved icon back where it was dropped. Public because the dashboard's resize
        ''' handler pins icons to the cells written in its source, so a saved arrangement has to be
        ''' laid back over the top afterwards or it would be undone by resizing the window.
        ''' </summary>
        Public Sub ApplySavedPositions()
            Dim saved = DataAccess.GetDashboardIconPositions(dashboardName)

            For Each icon In icons
                ' Every icon is placed, not only the moved ones, so one rule governs the whole grid:
                ' centred across its column and against the top of its row. Placing only the moved
                ' ones would leave them a few pixels out of line with the rest.
                Dim cell As Point
                If Not saved.TryGetValue(icon.Name, cell) Then
                    cell = DashboardGridLayout.CellFromPoint(New Point(icon.Left + (icon.Width \ 2),
                                                                       icon.Top + (icon.Height \ 2)))
                End If

                icon.Location = DashboardGridLayout.CellIconLocation(cell.Y, cell.X, icon.Size)
            Next

            FitOwnerToIcons()
        End Sub

        ''' <summary>
        ''' Grows the window until the lowest icon fits.
        '''
        ''' The dashboards are fixed dialogs whose minimum size is their standard size, so an icon
        ''' below the last visible row is not merely awkward - it cannot be reached at all. That
        ''' used to be the page generator's problem, solved by editing the height constant in
        ''' DashboardGridLayout.vb, which needed a rebuild to take effect and quietly stopped
        ''' working the moment that constant became an expression rather than a number.
        '''
        ''' Measured from the icons themselves, so it covers every way a fourth row can appear: a
        ''' generated icon, or one dragged there.
        ''' </summary>
        Private Sub FitOwnerToIcons()
            If icons.Count = 0 Then Return

            Dim required = icons.Max(Function(icon) icon.Bottom) + BottomMargin
            If owner.ClientSize.Height >= required Then Return

            owner.MinimumSize = New Size(owner.MinimumSize.Width,
                                         owner.MinimumSize.Height + (required - owner.ClientSize.Height))
            owner.ClientSize = New Size(owner.ClientSize.Width, required)
        End Sub

        Private Sub Icon_MouseDown(sender As Object, e As MouseEventArgs)
            If e.Button <> MouseButtons.Left Then Return

            dragged = TryCast(sender, Control)
            If dragged Is Nothing Then Return

            dragStart = owner.PointToClient(Control.MousePosition)
            dragOrigin = dragged.Location
            dragging = False
        End Sub

        Private Sub Icon_MouseMove(sender As Object, e As MouseEventArgs)
            If dragged Is Nothing Then Return

            Dim current = owner.PointToClient(Control.MousePosition)
            If Not dragging Then
                If Math.Abs(current.X - dragStart.X) < DragThreshold AndAlso
                   Math.Abs(current.Y - dragStart.Y) < DragThreshold Then Return

                dragging = True
                dragged.BringToFront()

                ' Marked now, not on release. A button raises its Click from OnMouseUp before the
                ' MouseUp handlers run, so a drag that only decided at the end would already have
                ' opened the page it was dragged from.
                Dim icon = TryCast(dragged, DashboardIconButton)
                If icon IsNot Nothing Then icon.SuppressNextClick = True
            End If

            dragged.Location = New Point(dragOrigin.X + (current.X - dragStart.X),
                                         dragOrigin.Y + (current.Y - dragStart.Y))
        End Sub

        Private Sub Icon_MouseUp(sender As Object, e As MouseEventArgs)
            Dim moving = dragged
            dragged = Nothing
            If moving Is Nothing Then Return

            If Not dragging Then
                ' Never passed the threshold, so this was a click. The button's own Click handler
                ' opens the page; nothing to do here but let it.
                Return
            End If

            dragging = False

            Dim centre = New Point(moving.Left + (moving.Width \ 2), moving.Top + (moving.Height \ 2))
            Dim target = DashboardGridLayout.CellFromPoint(centre)
            Dim origin = DashboardGridLayout.CellFromPoint(New Point(dragOrigin.X + (moving.Width \ 2),
                                                                     dragOrigin.Y + (moving.Height \ 2)))

            If target = origin Then
                ' Dropped back where it started. Snap it straight rather than leaving it a few
                ' pixels out, and write nothing.
                moving.Location = DashboardGridLayout.CellIconLocation(origin.Y, origin.X, moving.Size)
                Return
            End If

            ' Whatever is already there changes places with it. Swapping is what makes a full grid
            ' rearrangeable at all: no empty cell is needed, and no icon can end up underneath
            ' another or pushed off the edge.
            Dim occupant = icons.FirstOrDefault(Function(icon) icon IsNot moving AndAlso
                                                    DashboardGridLayout.CellFromPoint(
                                                        New Point(icon.Left + (icon.Width \ 2),
                                                                  icon.Top + (icon.Height \ 2))) = target)

            Dim moved As New List(Of KeyValuePair(Of String, Point))() From {
                New KeyValuePair(Of String, Point)(moving.Name, target)
            }

            moving.Location = DashboardGridLayout.CellIconLocation(target.Y, target.X, moving.Size)
            If occupant IsNot Nothing Then
                occupant.Location = DashboardGridLayout.CellIconLocation(origin.Y, origin.X, occupant.Size)
                moved.Add(New KeyValuePair(Of String, Point)(occupant.Name, origin))
            End If

            If DataAccess.SaveDashboardIconPositions(dashboardName, moved, userId) Then
                ' A drop can be the thing that creates a new bottom row.
                FitOwnerToIcons()
                Return
            End If

            ' The move did not save, so it does not stand. Putting the icons back is the honest
            ' outcome: an arrangement that looks kept and is gone tomorrow is worse than one that
            ' visibly refused.
            moving.Location = DashboardGridLayout.CellIconLocation(origin.Y, origin.X, moving.Size)
            If occupant IsNot Nothing Then
                occupant.Location = DashboardGridLayout.CellIconLocation(target.Y, target.X, occupant.Size)
            End If

            MessageBox.Show(owner,
                            "THE NEW POSITION COULD NOT BE SAVED, SO THE ICONS HAVE BEEN PUT BACK.",
                            "DASHBOARD LAYOUT",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub
    End Class
End Namespace
