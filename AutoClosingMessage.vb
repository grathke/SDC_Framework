Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' A message that says something went well and then gets out of the way.
    '''
    ''' For the confirmations nobody needs to acknowledge - "Role deleted", "Audit row restored".
    ''' A MessageBox for those makes the user dismiss a dialog to be told the thing they just asked
    ''' for happened. Failures still use MessageBox, because those do want acknowledging.
    '''
    ''' Lived in FW_EntityCrudAdapter until 2026-09-03, which was never where it belonged: nothing
    ''' about it concerns entities, and the two pages that call it - Roles_B and FW_AuditTrail_B -
    ''' have nothing to do with that table either. It outlived the adapter for exactly that reason.
    '''
    ''' With no owner form it degrades to a plain MessageBox: there would be nothing to centre on
    ''' and nothing to keep it in front of.
    ''' </summary>
    Public Module AutoClosingMessage

        Public Sub Show(owner As Form,
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
    End Module
End Namespace
