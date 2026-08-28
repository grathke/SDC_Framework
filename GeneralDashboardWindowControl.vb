Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class GeneralDashboardWindowControl
        Inherits UserControl
        Implements IAccessControlledControl

        Private ReadOnly pendingLabel As Label
        Private ReadOnly errorLabel As Label
        Private ReadOnly lastSendingLabel As Label
        Private ReadOnly nextSendingLabel As Label
        Private ReadOnly enableButton As Button
        Private ReadOnly pendingProductsButton As Button

        Public Sub New()
            Me.Dock = DockStyle.Fill
            Me.BackColor = Color.White
            Me.Padding = New Padding(8)

            Dim statusIconPanel As New Panel() With {
                .Location = New Point(8, 20),
                .Size = New Size(94, 94),
                .BackColor = Color.White
            }
            AddHandler statusIconPanel.Paint, AddressOf StatusIconPanel_Paint

            pendingLabel = New Label() With {
                .Text = "0000 pending",
                .Location = New Point(116, 20),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            errorLabel = New Label() With {
                .Text = "0000 in error",
                .Location = New Point(116, 64),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            lastSendingLabel = New Label() With {
                .Text = "Last sending: yesterday, 22:00",
                .Location = New Point(8, 128),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            nextSendingLabel = New Label() With {
                .Text = "Next sending: tomorrow, 15:00",
                .Location = New Point(8, 152),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            Dim loopIconPanel As New Panel() With {
                .Location = New Point(8, 188),
                .Size = New Size(54, 54),
                .BackColor = Color.White
            }
            AddHandler loopIconPanel.Paint, AddressOf LoopIconPanel_Paint

            Dim outgoingLoopLabel As New Label() With {
                .Text = "Outgoing loop:",
                .Location = New Point(68, 196),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            Dim enableLabel As New Label() With {
                .Text = "Enables",
                .Location = New Point(194, 196),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 18.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            enableButton = New Button() With {
                .Text = "Enable",
                .Location = New Point(72, 224),
                .Size = New Size(92, 32),
                .FlatStyle = FlatStyle.Flat,
                .BackColor = Color.FromArgb(30, 145, 190),
                .ForeColor = Color.White
            }
            enableButton.FlatAppearance.BorderSize = 0

            pendingProductsButton = New Button() With {
                .Text = "View the pending Acme products",
                .Location = New Point(8, 264),
                .Size = New Size(260, 34),
                .FlatStyle = FlatStyle.Flat,
                .BackColor = Color.FromArgb(30, 145, 190),
                .ForeColor = Color.White
            }
            pendingProductsButton.FlatAppearance.BorderSize = 0

            Me.Controls.Add(statusIconPanel)
            Me.Controls.Add(pendingLabel)
            Me.Controls.Add(errorLabel)
            Me.Controls.Add(lastSendingLabel)
            Me.Controls.Add(nextSendingLabel)
            Me.Controls.Add(loopIconPanel)
            Me.Controls.Add(outgoingLoopLabel)
            Me.Controls.Add(enableLabel)
            Me.Controls.Add(enableButton)
            Me.Controls.Add(pendingProductsButton)
        End Sub

        Public Sub ApplyAccess(profile As AccessProfile, tableName As String) Implements IAccessControlledControl.ApplyAccess
            If profile Is Nothing Then
                Return
            End If

            Dim canRead = profile.Can(tableName, AccessCapability.Read)
            Dim canUpdate = profile.Can(tableName, AccessCapability.Update)
            Dim canExecute = profile.Can(tableName, AccessCapability.Execute)

            Me.Enabled = canRead
            enableButton.Enabled = canUpdate OrElse canExecute
            pendingProductsButton.Enabled = canRead
        End Sub

        Public Sub SetCounters(pending As Integer, [error] As Integer)
            pendingLabel.Text = pending.ToString("0000") & " pending"
            errorLabel.Text = [error].ToString("0000") & " in error"
        End Sub

        Public Sub SetSchedule(lastSending As String, nextSending As String)
            lastSendingLabel.Text = "Last sending: " & lastSending
            nextSendingLabel.Text = "Next sending: " & nextSending
        End Sub

        Private Sub StatusIconPanel_Paint(sender As Object, e As PaintEventArgs)
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Dim bounds As New Rectangle(6, 6, 80, 80)

            Using fillBrush As New SolidBrush(Color.FromArgb(247, 58, 40)),
                borderPen As New Pen(Color.FromArgb(198, 34, 20), 2.0F)
                e.Graphics.FillEllipse(fillBrush, bounds)
                e.Graphics.DrawEllipse(borderPen, bounds)
            End Using

            Using toolPen As New Pen(Color.Silver, 6.0F),
                toolPenDark As New Pen(Color.Gray, 3.0F)
                e.Graphics.DrawLine(toolPen, 20, 72, 72, 20)
                e.Graphics.DrawLine(toolPenDark, 18, 70, 70, 18)
                e.Graphics.DrawLine(toolPen, 24, 30, 52, 58)
                e.Graphics.DrawLine(toolPenDark, 24, 32, 50, 58)
            End Using
        End Sub

        Private Sub LoopIconPanel_Paint(sender As Object, e As PaintEventArgs)
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Dim bounds As New Rectangle(2, 2, 48, 48)

            Using fillBrush As New SolidBrush(Color.FromArgb(121, 195, 73)),
                borderPen As New Pen(Color.FromArgb(78, 153, 41), 2.0F)
                e.Graphics.FillEllipse(fillBrush, bounds)
                e.Graphics.DrawEllipse(borderPen, bounds)
            End Using

            Using glyphPen As New Pen(Color.White, 4.0F)
                e.Graphics.DrawArc(glyphPen, 14, 14, 24, 24, 30, 290)
            End Using
        End Sub
    End Class
End Namespace
