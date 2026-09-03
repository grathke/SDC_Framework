Option Strict On
Option Explicit On

Imports System.Drawing

Namespace SDC.Framework
    Public NotInheritable Class DashboardGridLayout
        Private Sub New()
        End Sub

        ''' <summary>
        ''' An icon, and the cell it sits in. The cell is deliberately larger than the icon in both
        ''' directions: the gap is what stops 150-wide icons touching in a 150-wide column, and the
        ''' extra height is room for a caption that runs to two lines - "User Access Explanation"
        ''' and "HD Support Tickets" both do - without the icon above it shifting up to make room.
        ''' </summary>
        Public Const IconWidth As Integer = 150
        Public Const IconHeight As Integer = 130

        Public Const FirstColumnLeft As Integer = 70
        Public Const ColumnGap As Integer = 170
        Public Const FirstRowTop As Integer = 105
        Public Const RowGap As Integer = 155

        ' Wide enough for five columns from FirstColumnLeft plus a right margin, tall enough for
        ' three rows. Both follow from the numbers above rather than being chosen beside them, so a
        ' change to the cell cannot leave the window the wrong size for it.
        Public Const StandardClientWidth As Integer = FirstColumnLeft + (4 * ColumnGap) + IconWidth + 30
        Public Const StandardClientHeight As Integer = FirstRowTop + (3 * RowGap) + 20

        Public Shared Function CellLeft(column As Integer) As Integer
            If column < 1 Then Throw New ArgumentOutOfRangeException(NameOf(column))
            Return FirstColumnLeft + ((column - 1) * ColumnGap)
        End Function

        Public Shared Function CellTop(row As Integer, Optional firstRowTop As Integer = FirstRowTop) As Integer
            If row < 1 Then Throw New ArgumentOutOfRangeException(NameOf(row))
            Return firstRowTop + ((row - 1) * RowGap)
        End Function

        Public Shared Function CellLocation(row As Integer, column As Integer, Optional firstRowTop As Integer = FirstRowTop) As Point
            Return New Point(CellLeft(column), CellTop(row, firstRowTop))
        End Function

        ''' <summary>
        ''' How many columns a dashboard has. The generator has always assumed five when looking
        ''' for a free cell; saying so here means a dropped icon and a generated one agree on where
        ''' the grid ends.
        ''' </summary>
        Public Const ColumnCount As Integer = 5

        ''' <summary>
        ''' Where an icon sits in its cell: centred across the column, and against the top of the
        ''' row.
        '''
        ''' Top rather than centred vertically, because a caption of two lines makes its icon
        ''' taller than a caption of one. Centring each icon in its own cell would then set the
        ''' pictures at different heights along a row - the longer caption pushing its picture up -
        ''' and a row of icons that do not line up reads as a mistake. Anchored at the top, the
        ''' pictures align and the captions grow downwards into the space left for them.
        ''' </summary>
        Public Shared Function CellIconLocation(row As Integer,
                                                column As Integer,
                                                iconSize As Size,
                                                Optional firstRowTop As Integer = FirstRowTop) As Point
            Dim origin = CellLocation(row, column, firstRowTop)
            Return New Point(origin.X + ((ColumnGap - iconSize.Width) \ 2), origin.Y)
        End Function

        ''' <summary>
        ''' The cell nearest a point, as (column, row) - the same order FindNextDashboardGridCell
        ''' returns, so the generator and a drop speak of cells the same way.
        '''
        ''' Measured from the icon's centre rather than its corner, which is what makes a drop land
        ''' where it looks like it should. Rows are unbounded downwards because the dashboard grows
        ''' a row whenever the generator needs one.
        ''' </summary>
        Public Shared Function CellFromPoint(iconCenter As Point) As Point
            Dim column = CInt(Math.Round((iconCenter.X - FirstColumnLeft - (ColumnGap / 2.0)) / ColumnGap)) + 1
            Dim row = CInt(Math.Round((iconCenter.Y - FirstRowTop - (RowGap / 2.0)) / RowGap)) + 1

            Return New Point(Math.Min(ColumnCount, Math.Max(1, column)), Math.Max(1, row))
        End Function
    End Class
End Namespace
