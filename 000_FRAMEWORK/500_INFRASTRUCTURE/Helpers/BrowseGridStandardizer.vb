Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Friend Module BrowseGridStandardizer
        Private ReadOnly HeaderLightBlue As Color = Color.FromArgb(221, 235, 247)
        Private ReadOnly HeaderTextColor As Color = Color.Black
        Private ReadOnly HeaderDefaultBackground As Color = Color.White

        Friend Sub ApplyLightBlueHeaderStyle(grid As DataGridView)
            If grid Is Nothing Then
                Return
            End If

            grid.EnableHeadersVisualStyles = False
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderDefaultBackground
            grid.ColumnHeadersDefaultCellStyle.ForeColor = HeaderTextColor
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderLightBlue
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = HeaderTextColor
        End Sub

        ''' <summary>
        ''' A header that does not react to selection at all.
        '''
        ''' With EnableHeadersVisualStyles off, a DataGridView paints the header of whichever
        ''' column holds the selected cell using SelectionBackColor, and left unset that is the
        ''' system highlight - a solid blue header on the first column the moment a grid fills,
        ''' which reads as a sort order or a chosen column rather than as a side effect.
        '''
        ''' A browse grid wants the light blue: the user picked that row, and the highlight tracks
        ''' them. A reporting grid does not - nothing there acts on the selected row, so a header
        ''' changing colour is saying something that is not true. Those pass their resting colours
        ''' here and get a header that stays put.
        ''' </summary>
        Friend Sub ApplyStaticHeaderStyle(grid As DataGridView, background As Color, foreground As Color)
            If grid Is Nothing Then
                Return
            End If

            grid.EnableHeadersVisualStyles = False
            grid.ColumnHeadersDefaultCellStyle.BackColor = background
            grid.ColumnHeadersDefaultCellStyle.ForeColor = foreground
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = background
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = foreground
        End Sub

        Friend Sub ApplyBrowseGridStandard(grid As DataGridView)
            If grid Is Nothing Then
                Return
            End If

            grid.AllowUserToAddRows = False
            grid.AllowUserToDeleteRows = False
            grid.AllowUserToOrderColumns = True
            grid.ColumnHeadersVisible = True
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            grid.ColumnHeadersHeight = 34
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            grid.MultiSelect = False
            grid.RowHeadersVisible = False
            grid.ScrollBars = ScrollBars.Both

            grid.RowTemplate.Height = 28
            ApplyLightBlueHeaderStyle(grid)
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True
            grid.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        End Sub

        Friend Sub FocusGridForBrowseEntry(grid As DataGridView)
            If grid Is Nothing OrElse Not grid.Visible OrElse Not grid.Enabled Then
                Return
            End If

            ' The first *visible* cell, not the first cell. Cells(0) threw "Current cell cannot be
            ' set to an invisible cell" on any page whose first column is hidden - PK is hidden on
            ' every browse page, and a saved layout can hide others - so this failed wherever the
            ' hiding reached column zero. Rows too: a row can be invisible, and the same rule
            ' applies to it.
            If grid.CurrentCell Is Nothing Then
                Dim column = grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible)
                Dim rowIndex = grid.Rows.GetFirstRow(DataGridViewElementStates.Visible)

                If column IsNot Nothing AndAlso rowIndex >= 0 Then
                    grid.CurrentCell = grid.Rows(rowIndex).Cells(column.Index)
                End If
            End If

            grid.Focus()
        End Sub
    End Module
End Namespace
