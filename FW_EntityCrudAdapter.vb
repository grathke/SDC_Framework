Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public NotInheritable Class FW_EntityCrudAdapter
        Private Sub New()
        End Sub

        Public Shared Sub HandleCreate(owner As Form,
                                       registrationId As Integer,
                                       currentUserId As Integer,
                                       roleFieldTableName As String,
                                       refreshAction As Action)
            Dim model As New EntityRecord With {
                .RegistrationID = registrationId,
                .AssignedManagerID = currentUserId,
                .IsActive = True
            }

            Using dlg As New Entity_U(EntityEditMode.CreateMode, model, roleFieldTableName)
                If dlg.ShowDialog(owner) = DialogResult.OK Then
                    DataAccess.CreateEntity(dlg.EntityData, currentUserId)
                    If refreshAction IsNot Nothing Then
                        refreshAction()
                    End If
                End If
            End Using
        End Sub

        Public Shared Sub HandleRead(owner As Form,
                                     recordId As Integer,
                                     roleFieldTableName As String,
                                     refreshAction As Action)
            Dim model = DataAccess.GetEntityById(recordId)
            If model Is Nothing Then
                MessageBox.Show("Record no longer exists.", "Read", MessageBoxButtons.OK, MessageBoxIcon.Information)
                If refreshAction IsNot Nothing Then
                    refreshAction()
                End If
                Return
            End If

            Using dlg As New Entity_U(EntityEditMode.ReadMode, model, roleFieldTableName)
                dlg.ShowDialog(owner)
            End Using
        End Sub

        Public Shared Sub HandleUpdate(owner As Form,
                                       recordId As Integer,
                                       currentUserId As Integer,
                                       roleFieldTableName As String,
                                       refreshByRecordIdAction As Action(Of Integer))
            Dim model = DataAccess.GetEntityById(recordId)
            If model Is Nothing Then
                MessageBox.Show("Record no longer exists.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information)
                If refreshByRecordIdAction IsNot Nothing Then
                    refreshByRecordIdAction(recordId)
                End If
                Return
            End If

            Using dlg As New Entity_U(EntityEditMode.UpdateMode, model, roleFieldTableName)
                If dlg.ShowDialog(owner) = DialogResult.OK Then
                    Dim saveResult = DataAccess.UpdateEntity(dlg.EntityData, currentUserId)
                    If saveResult = SaveResult.ConcurrencyUnavailable Then
                        MessageBox.Show(owner,
                                        "This record cannot be saved safely because its table does not have a RowVersion column.",
                                        "Concurrency Protection Unavailable",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Warning)
                        Return
                    End If

                    If saveResult = SaveResult.RecordChanged Then
                        If MessageBox.Show(owner,
                                           "This record was changed by another user after you opened it." & Environment.NewLine & Environment.NewLine &
                                           "Do you want to overwrite that newer version with your current changes?",
                                           "Record Changed",
                                           MessageBoxButtons.YesNo,
                                           MessageBoxIcon.Warning) <> DialogResult.Yes Then
                            Return
                        End If

                        Dim latestModel = DataAccess.GetEntityById(recordId)
                        If latestModel Is Nothing OrElse latestModel.RowVersion Is Nothing Then
                            MessageBox.Show(owner, "The record no longer exists.", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            Return
                        End If

                        dlg.EntityData.RowVersion = latestModel.RowVersion
                        saveResult = DataAccess.UpdateEntity(dlg.EntityData, currentUserId)
                    End If

                    If saveResult <> SaveResult.Succeeded Then
                        MessageBox.Show(owner, "The entity could not be saved.", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If

                    If refreshByRecordIdAction IsNot Nothing Then
                        refreshByRecordIdAction(recordId)
                    End If
                End If
            End Using
        End Sub

        Public Shared Sub HandleDelete(owner As Form,
                                       recordId As Integer,
                                       roleFieldTableName As String,
                                       refreshAction As Action)
            Dim model = DataAccess.GetEntityById(recordId)
            If model Is Nothing Then
                MessageBox.Show("Record no longer exists.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information)
                If refreshAction IsNot Nothing Then
                    refreshAction()
                End If
                Return
            End If

            If MessageBox.Show("Delete this record?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            Dim updatedBy As Integer = 0
            If SessionState.IsActive Then
                updatedBy = SessionState.Current.Value.UserID
            End If

            DataAccess.DeleteEntity(model.ID, updatedBy, owner.GetType().Name)
            ShowAutoClosingMessage(owner, "Record deleted.", "Delete", MessageBoxIcon.Information, 1000)
            If refreshAction IsNot Nothing Then
                refreshAction()
            End If
        End Sub

        Friend Shared Sub ShowAutoClosingMessage(owner As Form,
                             messageText As String,
                             caption As String,
                             icon As MessageBoxIcon,
                             durationMs As Integer)
            If owner Is Nothing Then
                MessageBox.Show(messageText, caption, MessageBoxButtons.OK, icon)
                Return
            End If

            Dim notificationForm As New Form() With {
                .FormBorderStyle = FormBorderStyle.FixedToolWindow,
                .StartPosition = FormStartPosition.Manual,
                .ShowInTaskbar = False,
                .TopMost = True,
                .Text = caption,
                .ClientSize = New Size(280, 90),
                .BackColor = Color.WhiteSmoke
            }

            Dim messageLabel As New Label() With {
                .Dock = DockStyle.Fill,
                .TextAlign = ContentAlignment.MiddleCenter,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Regular),
                .Text = messageText,
                .Padding = New Padding(12)
            }

            notificationForm.Controls.Add(messageLabel)

            Dim ownerBounds = owner.Bounds
            notificationForm.Location = New Point(
                ownerBounds.Left + Math.Max(0, (ownerBounds.Width - notificationForm.Width) \ 2),
                ownerBounds.Top + Math.Max(0, (ownerBounds.Height - notificationForm.Height) \ 2))

            Dim closeTimer As New Timer() With {
                .Interval = Math.Max(250, durationMs)
            }

            AddHandler closeTimer.Tick,
                Sub(sender As Object, e As EventArgs)
                    closeTimer.Stop()
                    notificationForm.Close()
                    closeTimer.Dispose()
                    notificationForm.Dispose()
                End Sub

            AddHandler notificationForm.Shown,
                Sub(sender As Object, e As EventArgs)
                    closeTimer.Start()
                End Sub

            notificationForm.Show(owner)
        End Sub
    End Class
End Namespace
