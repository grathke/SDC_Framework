Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
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

            If grid.CurrentCell Is Nothing AndAlso grid.Rows.Count > 0 AndAlso grid.Columns.Count > 0 Then
                grid.CurrentCell = grid.Rows(0).Cells(0)
            End If

            grid.Focus()
        End Sub
    End Module
End Namespace
