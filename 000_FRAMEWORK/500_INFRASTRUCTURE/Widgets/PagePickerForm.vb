Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class PagePickerForm
        Inherits Form

        Public Sub New()
            Me.Text = "Preview"
            Me.StartPosition = FormStartPosition.CenterScreen
            Me.ClientSize = New Size(420, 330)
            Me.MinimumSize = New Size(420, 330)
            ' Not resizable by dragging. Nearly every run of this application is a VirtualUI
            ' session, where a window has no business growing past the canvas it is drawn on, and
            ' a page that can be dragged wider fights both the zoom and its own layout. The
            ' application still sizes the window itself - F9, and Hot Fields widening a browse page -
            ' because a fixed border only stops the drag handles, not code.
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.BackColor = Color.WhiteSmoke

            Dim titleLabel As New Label() With {
                .Text = "Choose a page to open",
                .AutoSize = True,
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Bold),
                .Location = New Point(16, 16)
            }
            Me.Controls.Add(titleLabel)

            Dim descriptionLabel As New Label() With {
                .Text = "Development launcher (bypasses login/menu flow)",
                .AutoSize = True,
                .ForeColor = Color.DimGray,
                .Location = New Point(18, 46)
            }
            Me.Controls.Add(descriptionLabel)

            Dim registrationBrowseButton = CreateLaunchButton("Open FW_Registration_B", 18, 84)
            AddHandler registrationBrowseButton.Click, Sub(sender, e) OpenPageDialog(Function() New FW_Registration_B())
            Me.Controls.Add(registrationBrowseButton)

            Dim registrationUpdateButton = CreateLaunchButton("Open FW_Registration_U", 18, 126)
            AddHandler registrationUpdateButton.Click, Sub(sender, e) OpenPageDialog(Function() New FW_Registration_U())
            Me.Controls.Add(registrationUpdateButton)

            Dim closeButton = CreateLaunchButton("Close", 18, 168)
            AddHandler closeButton.Click, Sub(sender, e) Me.Close()
            Me.Controls.Add(closeButton)
        End Sub

        Private Shared Function CreateLaunchButton(caption As String, x As Integer, y As Integer) As Button
            Return New Button() With {
                .Text = caption,
                .Location = New Point(x, y),
                .Size = New Size(220, 34)
            }
        End Function

        Private Sub OpenPageDialog(factory As Func(Of Form))
            Try
                Using frm = factory.Invoke()
                    frm.StartPosition = FormStartPosition.CenterParent
                    frm.ShowDialog(Me)
                End Using
            Catch ex As Exception
                MessageBox.Show(Me,
                                "Could not open page: " & ex.Message,
                                "Preview",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error)
            End Try
        End Sub
    End Class
End Namespace
