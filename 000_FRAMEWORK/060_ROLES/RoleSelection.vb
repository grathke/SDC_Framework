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

            Me.Text = "Role Selection"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False

            ' A card on the shell's backdrop, like the login screen - these are the two windows a
            ' user meets before the menu, and they used to be the two that looked least like it.
            ' A little larger than the card, matching the login screen so the backdrop does not
            ' change size between the two.
            Me.ClientSize = New Size(700, 520)

            Dim card = ShellChrome.BuildCard(Me, New Size(580, 430), "Choose the role for this session")

            ' Sentence case, and shorter. The original was three lines of capitals, which reads as
            ' shouting and is slower to take in than the sentence it replaces.
            Dim infoLabel As New Label() With {
                .Text = "You belong to more than one role. Pick the one to use for this session - you can change it at any time from the main menu.",
                .Location = New Point(24, 92),
                .Size = New Size(532, 40),
                .AutoSize = False,
                .ForeColor = ShellChrome.BodyInk
            }

            rolesGrid = New DataGridView() With {
                .Location = New Point(24, 140),
                .Size = New Size(532, 230),
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
                .Location = New Point(336, 384),
                .Size = New Size(105, 34),
                .DialogResult = DialogResult.OK,
                .Enabled = False
            }
            ShellChrome.StylePrimary(okButton)

            cancelActionButton = New Button() With {
                .Text = "Cancel",
                .Location = New Point(451, 384),
                .Size = New Size(105, 34),
                .DialogResult = DialogResult.Cancel
            }
            ShellChrome.StyleSecondary(cancelActionButton)

            AddHandler okButton.Click, AddressOf OkButton_Click

            Me.AcceptButton = okButton
            Me.CancelButton = cancelActionButton

            ' Onto the card, not the form - the form is the backdrop.
            card.Controls.Add(infoLabel)
            card.Controls.Add(rolesGrid)
            card.Controls.Add(okButton)
            card.Controls.Add(cancelActionButton)
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
