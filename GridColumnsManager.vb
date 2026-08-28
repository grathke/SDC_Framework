Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows.Forms

Namespace HelloWorld
    Friend NotInheritable Class GridColumnsManager
        Private Sub New()
        End Sub

        Friend Class ManagedColumnItem
            Public Property Column As DataGridViewColumn
            Public Property DisplayName As String

            Public Overrides Function ToString() As String
                Return DisplayName
            End Function
        End Class

        Friend Shared Sub RefreshColumnsManagerFromGrid(list As CheckedListBox,
                                                        orderedColumns As IEnumerable(Of DataGridViewColumn))
            list.Items.Clear()

            For Each col In orderedColumns
                Dim text = If(String.IsNullOrWhiteSpace(col.HeaderText), col.Name, col.HeaderText.Trim())
                Dim item As New ManagedColumnItem With {
                    .Column = col,
                    .DisplayName = text
                }
                Dim idx = list.Items.Add(item)
                list.SetItemChecked(idx, col.Visible)
            Next

            If list.Items.Count > 0 Then
                list.SelectedIndex = 0
            End If
        End Sub

        Friend Shared Sub UpdateColumnsManagerButtonsState(list As CheckedListBox,
                                                           moveUpButton As Button,
                                                           moveDownButton As Button)
            Dim selectedIndex = list.SelectedIndex
            moveUpButton.Enabled = selectedIndex > 0
            moveDownButton.Enabled = selectedIndex >= 0 AndAlso selectedIndex < list.Items.Count - 1
        End Sub

        Friend Shared Sub HandleColumnsManagerListKeyDown(list As CheckedListBox,
                                                          e As KeyEventArgs)
            Dim selectedIndex = list.SelectedIndex
            If selectedIndex < 0 AndAlso list.Items.Count > 0 Then
                list.SelectedIndex = 0
                selectedIndex = 0
            End If

            If e.KeyCode = Keys.Up Then
                If selectedIndex > 0 Then
                    list.SelectedIndex = selectedIndex - 1
                End If
                e.Handled = True
                Return
            End If

            If e.KeyCode = Keys.Down Then
                If selectedIndex >= 0 AndAlso selectedIndex < list.Items.Count - 1 Then
                    list.SelectedIndex = selectedIndex + 1
                End If
                e.Handled = True
                Return
            End If

            If e.KeyCode = Keys.Space AndAlso selectedIndex >= 0 Then
                Dim currentlyChecked = list.GetItemChecked(selectedIndex)
                list.SetItemChecked(selectedIndex, Not currentlyChecked)
                e.Handled = True
                e.SuppressKeyPress = True
            End If
        End Sub

        Friend Shared Function ValidateItemCheck(list As CheckedListBox,
                                                 e As ItemCheckEventArgs) As Boolean
            If e.CurrentValue = CheckState.Checked AndAlso e.NewValue = CheckState.Unchecked Then
                If list.CheckedItems.Count <= 1 Then
                    e.NewValue = CheckState.Checked
                    MessageBox.Show("At least one column must remain visible.", "Columns", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return False
                End If
            End If

            Return True
        End Function

        Friend Shared Function MoveSelectedItem(list As CheckedListBox,
                                                delta As Integer) As Boolean
            Dim selectedIndex = list.SelectedIndex
            If selectedIndex < 0 Then
                Return False
            End If

            Dim targetIndex = selectedIndex + delta
            If targetIndex < 0 OrElse targetIndex >= list.Items.Count Then
                Return False
            End If

            Dim movingItem = list.Items(selectedIndex)
            Dim wasChecked = list.GetItemChecked(selectedIndex)

            list.Items.RemoveAt(selectedIndex)
            list.Items.Insert(targetIndex, movingItem)
            list.SetItemChecked(targetIndex, wasChecked)
            list.SelectedIndex = targetIndex
            Return True
        End Function

        Friend Shared Function ApplyColumnsManagerStateToGrid(list As CheckedListBox,
                                                              grid As DataGridView) As List(Of DataGridViewColumn)
            Dim orderedManagedColumns As New List(Of DataGridViewColumn)()

            For i As Integer = 0 To list.Items.Count - 1
                Dim item = TryCast(list.Items(i), ManagedColumnItem)
                If item Is Nothing OrElse item.Column Is Nothing Then
                    Continue For
                End If

                Dim desiredVisible = list.GetItemChecked(i)
                If item.Column.Visible <> desiredVisible Then
                    item.Column.Visible = desiredVisible
                End If

                orderedManagedColumns.Add(item.Column)
            Next

            If orderedManagedColumns.Count > 0 Then
                Dim startIndex = orderedManagedColumns.Min(Function(c) c.DisplayIndex)
                Dim maxDisplay = Math.Max(0, grid.Columns.Count - 1)
                For i As Integer = 0 To orderedManagedColumns.Count - 1
                    Dim desiredDisplay = Math.Max(0, Math.Min(maxDisplay, startIndex + i))
                    If orderedManagedColumns(i).DisplayIndex <> desiredDisplay Then
                        orderedManagedColumns(i).DisplayIndex = desiredDisplay
                    End If
                Next
            End If

            Return orderedManagedColumns
        End Function

        Friend Shared Sub FitVisibleColumnsToAvailableWidth(grid As DataGridView)
            If grid Is Nothing OrElse grid.Columns Is Nothing OrElse grid.Columns.Count = 0 Then
                Return
            End If

            Dim visibleColumns = grid.Columns.Cast(Of DataGridViewColumn)().Where(Function(col) col.Visible).ToList()
            If visibleColumns.Count = 0 Then
                Return
            End If

            Dim availableWidth = grid.DisplayRectangle.Width
            If availableWidth <= 0 Then
                Return
            End If

            Const minReadableColumnWidth As Integer = 180
            Dim canFillEvenly = (availableWidth \ visibleColumns.Count) >= minReadableColumnWidth

            If canFillEvenly Then
                For Each col In visibleColumns
                    col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
                    col.FillWeight = 1.0F
                    col.MinimumWidth = Math.Max(40, Math.Min(col.MinimumWidth, minReadableColumnWidth))
                Next
                Return
            End If

            For Each col In visibleColumns
                col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None
                Dim targetWidth = Math.Max(Math.Max(40, col.MinimumWidth), minReadableColumnWidth)
                If col.Width <> targetWidth Then
                    col.Width = targetWidth
                End If
            Next
        End Sub

        Friend Shared Sub SetColumnsPanelVisible(columnsPanel As Panel,
                                                 toggleColumnsButton As Button,
                                                 visible As Boolean,
                                                 expandedText As String,
                                                 collapsedText As String,
                                                 Optional onShown As Action = Nothing)
            If columnsPanel Is Nothing OrElse toggleColumnsButton Is Nothing Then
                Return
            End If

            columnsPanel.Visible = visible
            toggleColumnsButton.Text = If(visible, expandedText, collapsedText)

            If visible Then
                If onShown IsNot Nothing Then
                    onShown()
                End If
                columnsPanel.BringToFront()
            End If
        End Sub
    End Class
End Namespace
