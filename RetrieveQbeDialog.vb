Option Strict On
Option Explicit On

Imports System.ComponentModel
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class RetrieveQbeDialog
        Inherits Form

        Private ReadOnly savedList As List(Of SavedQbeRecord)
        Private ReadOnly currentUserId As Integer
        Private ReadOnly listBox As ListBox
        Private ReadOnly applyButton As Button
        Private ReadOnly deleteButton As Button

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SelectedRecord As SavedQbeRecord

        Public Sub New(saved As List(Of SavedQbeRecord), userId As Integer)
            savedList = saved
            currentUserId = userId

            Me.Text = "Retrieve Saved QBE"
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.StartPosition = FormStartPosition.CenterParent
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New System.Drawing.Size(380, 300)
            Me.BackColor = System.Drawing.Color.White

            Dim titleLabel As New Label() With {
                .Text = "Select a saved QBE to apply:",
                .Location = New System.Drawing.Point(12, 12),
                .AutoSize = True
            }

            listBox = New ListBox() With {
                .Location = New System.Drawing.Point(12, 36),
                .Size = New System.Drawing.Size(356, 210),
                .SelectionMode = SelectionMode.One
            }

            For Each rec In savedList
                Dim display = If(rec.IsCompanyWide, "[Company] " & rec.QbeName, rec.QbeName)
                listBox.Items.Add(display)
            Next

            If listBox.Items.Count > 0 Then
                listBox.SelectedIndex = 0
            End If

            applyButton = New Button() With {
                .Text = "Apply",
                .DialogResult = DialogResult.OK,
                .Location = New System.Drawing.Point(12, 260),
                .Size = New System.Drawing.Size(80, 30)
            }

            deleteButton = New Button() With {
                .Text = "Delete",
                .Location = New System.Drawing.Point(102, 260),
                .Size = New System.Drawing.Size(80, 30)
            }

            Dim cancelButton As New Button() With {
                .Text = "Cancel",
                .DialogResult = DialogResult.Cancel,
                .Location = New System.Drawing.Point(292, 260),
                .Size = New System.Drawing.Size(80, 30)
            }

            Me.AcceptButton = applyButton
            Me.CancelButton = cancelButton

            AddHandler applyButton.Click, AddressOf ApplyButton_Click
            AddHandler deleteButton.Click, AddressOf DeleteButton_Click
            AddHandler listBox.SelectedIndexChanged, AddressOf ListBox_SelectedIndexChanged

            Me.Controls.Add(titleLabel)
            Me.Controls.Add(listBox)
            Me.Controls.Add(applyButton)
            Me.Controls.Add(deleteButton)
            Me.Controls.Add(cancelButton)

            UpdateButtonStates()
        End Sub

        Private Sub UpdateButtonStates()
            Dim idx = listBox.SelectedIndex
            applyButton.Enabled = (idx >= 0)
            If idx >= 0 Then
                Dim rec = savedList(idx)
                ' Only allow delete for records owned by the current user
                deleteButton.Enabled = (rec.UserID = currentUserId)
            Else
                deleteButton.Enabled = False
            End If
        End Sub

        Private Sub ListBox_SelectedIndexChanged(sender As Object, e As System.EventArgs)
            UpdateButtonStates()
        End Sub

        Private Sub ApplyButton_Click(sender As Object, e As System.EventArgs)
            Dim idx = listBox.SelectedIndex
            If idx < 0 Then
                Me.DialogResult = DialogResult.None
                Return
            End If
            SelectedRecord = savedList(idx)
        End Sub

        Private Sub DeleteButton_Click(sender As Object, e As System.EventArgs)
            Dim idx = listBox.SelectedIndex
            If idx < 0 Then Return

            Dim rec = savedList(idx)
            If rec.UserID <> currentUserId Then
                MessageBox.Show("You can only delete QBE filters you created.", "Delete",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Dim confirm = MessageBox.Show(
                "Delete saved QBE """ & rec.QbeName & """?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question)

            If confirm <> DialogResult.Yes Then Return

            Try
                DataAccess.DeleteSavedQbe(rec.SavedQbeID, currentUserId)
                savedList.RemoveAt(idx)
                listBox.Items.RemoveAt(idx)
                If listBox.Items.Count > 0 Then
                    listBox.SelectedIndex = Math.Min(idx, listBox.Items.Count - 1)
                End If
                UpdateButtonStates()
            Catch ex As Exception
                MessageBox.Show("Delete failed: " & ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub
    End Class
End Namespace
