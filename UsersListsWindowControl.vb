Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class UsersListsWindowControl
        Inherits UserControl
        Implements IAccessControlledControl

        Private ReadOnly registeredCountLabel As Label
        Private ReadOnly listBoxesCountLabel As Label
        Private ReadOnly lastRegistrationLabel As Label

        Public Sub New()
            Me.Dock = DockStyle.Fill
            Me.BackColor = Color.White
            Me.Padding = New Padding(8)

            Dim usersIconPanel As New Panel() With {
                .Location = New Point(10, 24),
                .Size = New Size(96, 74),
                .BackColor = Color.White
            }
            AddHandler usersIconPanel.Paint, AddressOf UsersIconPanel_Paint

            registeredCountLabel = New Label() With {
                .Text = "0000 registered",
                .Location = New Point(118, 28),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            listBoxesCountLabel = New Label() With {
                .Text = "0000 list boxes",
                .Location = New Point(118, 72),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            lastRegistrationLabel = New Label() With {
                .Text = "Last registration:" & Environment.NewLine & "yesterday, 21:35",
                .Location = New Point(10, 136),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 53, 63)
            }

            Me.Controls.Add(usersIconPanel)
            Me.Controls.Add(registeredCountLabel)
            Me.Controls.Add(listBoxesCountLabel)
            Me.Controls.Add(lastRegistrationLabel)
        End Sub

        Public Sub ApplyAccess(profile As AccessProfile, tableName As String) Implements IAccessControlledControl.ApplyAccess
            If profile Is Nothing Then
                Return
            End If

            Me.Enabled = profile.Can(tableName, AccessCapability.Read)
        End Sub

        Public Sub SetCounts(registeredCount As Integer, listBoxCount As Integer)
            registeredCountLabel.Text = registeredCount.ToString("0000") & " registered"
            listBoxesCountLabel.Text = listBoxCount.ToString("0000") & " list boxes"
        End Sub

        Public Sub SetLastRegistrationText(value As String)
            lastRegistrationLabel.Text = "Last registration:" & Environment.NewLine & value
        End Sub

        Private Sub UsersIconPanel_Paint(sender As Object, e As PaintEventArgs)
            e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Using userBrush As New SolidBrush(Color.FromArgb(165, 165, 165))
                e.Graphics.FillEllipse(userBrush, 10, 8, 28, 28)
                e.Graphics.FillEllipse(userBrush, 36, 4, 32, 32)
                e.Graphics.FillEllipse(userBrush, 6, 38, 40, 28)
                e.Graphics.FillEllipse(userBrush, 30, 34, 50, 30)
            End Using
        End Sub
    End Class
End Namespace
