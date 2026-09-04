Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class FW_RoleSelection
        Inherits Form

        Private ReadOnly roleList As List(Of UserRoleOption)
        Private ReadOnly preselectedRoleId As Integer
        Private ReadOnly rolesGrid As DataGridView
        Private ReadOnly okButton As Button
        Private ReadOnly cancelActionButton As Button
        Private selectedRoleValue As UserRoleOption

        Public ReadOnly Property SelectedRole As UserRoleOption
            Get
                Return selectedRoleValue
            End Get
        End Property

        Public Sub New(roles As List(Of UserRoleOption), Optional initialSelectedRoleId As Integer = 0)
            roleList = If(roles, New List(Of UserRoleOption)())
            preselectedRoleId = initialSelectedRoleId

            Me.Text = "ROLE SELECTION"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New Size(560, 370)
            Me.BackColor = Color.White

            Dim infoLabel As New Label() With {
                .Text = "YOU APPEAR TO BELONG TO MULTIPLE ROLES. PLEASE SELECT ONE TO BECOME YOUR CURRENT ROLE FOR THIS SESSION." & Environment.NewLine & Environment.NewLine & "YOU MAY CHANGE YOUR SELECTION AT ANY TIME FROM THE MAIN MENU.",
                .Location = New Point(18, 18),
                .Size = New Size(524, 64),
                .AutoSize = False
            }

            rolesGrid = New DataGridView() With {
                .Location = New Point(18, 92),
                .Size = New Size(524, 220),
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .ReadOnly = True,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .AutoGenerateColumns = False,
                .EditMode = DataGridViewEditMode.EditProgrammatically,
                .RowHeadersVisible = False,
                .BackgroundColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle
            }
            ApplyLightBlueHeaderStyle(rolesGrid)

            Dim displayOrderColumn As New DataGridViewTextBoxColumn() With {
                .Name = "DisplayOrder",
                .HeaderText = "Order",
                .DataPropertyName = "DisplayOrder",
                .Width = 90,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            }

            Dim roleNameColumn As New DataGridViewTextBoxColumn() With {
                .Name = "RoleName",
                .HeaderText = "Role Name",
                .DataPropertyName = "RoleName",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                .FillWeight = 100.0F
            }

            rolesGrid.Columns.Add(displayOrderColumn)
            rolesGrid.Columns.Add(roleNameColumn)

            For Each role In roleList
                Dim rowIndex = rolesGrid.Rows.Add(role.DisplayOrder, role.RoleName)
                rolesGrid.Rows(rowIndex).Tag = role
            Next

            AddHandler rolesGrid.SelectionChanged, AddressOf RolesGrid_SelectionChanged
            AddHandler rolesGrid.CellDoubleClick, AddressOf RolesGrid_CellDoubleClick
            AddHandler rolesGrid.RowPrePaint, AddressOf RolesGrid_RowPrePaint
            AddHandler Me.Shown, AddressOf FW_RoleSelection_Shown

            okButton = New Button() With {
                .Text = "OK",
                .Location = New Point(386, 326),
                .Size = New Size(75, 32),
                .DialogResult = DialogResult.OK,
                .Enabled = False
            }

            cancelActionButton = New Button() With {
                .Text = "Cancel",
                .Location = New Point(467, 326),
                .Size = New Size(75, 32),
                .DialogResult = DialogResult.Cancel
            }

            AddHandler okButton.Click, AddressOf OkButton_Click

            Me.AcceptButton = okButton
            Me.CancelButton = cancelActionButton

            Me.Controls.Add(infoLabel)
            Me.Controls.Add(rolesGrid)
            Me.Controls.Add(okButton)
            Me.Controls.Add(cancelActionButton)
        End Sub

        Private Sub FW_RoleSelection_Shown(sender As Object, e As EventArgs)
            rolesGrid.ClearSelection()
            rolesGrid.CurrentCell = Nothing
            okButton.Enabled = False

            If preselectedRoleId > 0 Then
                For Each row As DataGridViewRow In rolesGrid.Rows
                    Dim role = TryCast(row.Tag, UserRoleOption)
                    If role IsNot Nothing AndAlso role.RoleID = preselectedRoleId Then
                        row.Selected = True
                        rolesGrid.CurrentCell = row.Cells(0)
                        okButton.Enabled = True
                        Return
                    End If
                Next
            End If

            For Each row As DataGridViewRow In rolesGrid.Rows
                Dim role = TryCast(row.Tag, UserRoleOption)
                If role IsNot Nothing AndAlso role.IsApplicationAdmin Then
                    row.Selected = True
                    rolesGrid.CurrentCell = row.Cells(0)
                    okButton.Enabled = True
                    Exit For
                End If
            Next
        End Sub

        ''' <summary>
        ''' Suppresses the current-cell focus rectangle. The grid is read-only and selects whole
        ''' rows, so an outline drawn around one cell only reads as that cell being edited. The
        ''' row highlight already shows what is selected.
        ''' </summary>
        Private Sub RolesGrid_RowPrePaint(sender As Object, e As DataGridViewRowPrePaintEventArgs)
            e.PaintParts = e.PaintParts And Not DataGridViewPaintParts.Focus
        End Sub

        Private Sub RolesGrid_SelectionChanged(sender As Object, e As EventArgs)
            Dim selectedRow = GetSelectedRoleRow()
            selectedRoleValue = If(selectedRow Is Nothing, Nothing, TryCast(selectedRow.Tag, UserRoleOption))
            okButton.Enabled = selectedRoleValue IsNot Nothing
        End Sub

        Private Sub RolesGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 OrElse e.RowIndex >= rolesGrid.Rows.Count Then
                Return
            End If

            rolesGrid.ClearSelection()
            rolesGrid.Rows(e.RowIndex).Selected = True
            rolesGrid.CurrentCell = rolesGrid.Rows(e.RowIndex).Cells(0)
            okButton.PerformClick()
        End Sub

        Private Sub OkButton_Click(sender As Object, e As EventArgs)
            If selectedRoleValue Is Nothing Then
                Dim selectedRow = GetSelectedRoleRow()
                If selectedRow IsNot Nothing Then
                    selectedRoleValue = TryCast(selectedRow.Tag, UserRoleOption)
                End If
            End If

            If selectedRoleValue Is Nothing Then
                MessageBox.Show("Please select a role.", "Role Required", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Me.DialogResult = DialogResult.None
                Return
            End If

            Me.DialogResult = DialogResult.OK
        End Sub

        Private Function GetSelectedRoleRow() As DataGridViewRow
            If rolesGrid.SelectedRows.Count > 0 Then
                Return rolesGrid.SelectedRows(0)
            End If

            If rolesGrid.CurrentRow IsNot Nothing AndAlso rolesGrid.CurrentRow.Index >= 0 Then
                Return rolesGrid.CurrentRow
            End If

            Return Nothing
        End Function
    End Class
End Namespace
