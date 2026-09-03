Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class AcmeDashboardWindowControl
        Inherits UserControl
        Implements IAccessControlledControl

        Private ReadOnly draftedLabel As Label
        Private ReadOnly prototypedLabel As Label
        Private ReadOnly testedLabel As Label
        Private ReadOnly productionLabel As Label

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

            draftedLabel = New Label() With {
                .Text = "0010 drafted",
                .Location = New Point(116, 20),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            prototypedLabel = New Label() With {
                .Text = "0005 prototyped",
                .Location = New Point(116, 64),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            testedLabel = New Label() With {
                .Text = "0003 tested",
                .Location = New Point(116, 108),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            productionLabel = New Label() With {
                .Text = "0001 production",
                .Location = New Point(116, 152),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            Me.Controls.Add(statusIconPanel)
            Me.Controls.Add(draftedLabel)
            Me.Controls.Add(prototypedLabel)
            Me.Controls.Add(testedLabel)
            Me.Controls.Add(productionLabel)
        End Sub

        Public Sub ApplyAccess(profile As AccessProfile, tableName As String) Implements IAccessControlledControl.ApplyAccess
            If profile Is Nothing Then
                Return
            End If

            Me.Enabled = profile.Can(tableName, AccessCapability.Read)
        End Sub

        Public Sub SetCounters(drafted As Integer, prototyped As Integer, tested As Integer, production As Integer)
            draftedLabel.Text = drafted.ToString("0000") & " drafted"
            prototypedLabel.Text = prototyped.ToString("0000") & " prototyped"
            testedLabel.Text = tested.ToString("0000") & " tested"
            productionLabel.Text = production.ToString("0000") & " production"
        End Sub

        Private Sub StatusIconPanel_Paint(sender As Object, e As PaintEventArgs)
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Dim bounds As New Rectangle(6, 6, 80, 80)

            Using fillBrush As New SolidBrush(Color.FromArgb(246, 214, 42)),
                borderPen As New Pen(Color.FromArgb(205, 171, 20), 2.0F)
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
    End Class
End Namespace
