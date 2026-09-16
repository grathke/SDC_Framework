Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Asks for the first administrator of a new registration, just before it is saved.
    '''
    ''' Cancelling cancels the registration. A registration saved without this has no roles and
    ''' nobody in it, and can only be finished by hand in SQL.
    ''' </summary>
    Public NotInheritable Class RegistrationAdminPrompt

        Private Sub New()
        End Sub

        Public Shared Function Ask(owner As IWin32Window, registrationName As String) As RegistrationAdminRequest
            Dim result As New RegistrationAdminRequest()

            Using dialog As New Form()
                dialog.Text = "First Administrator"
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog
                dialog.StartPosition = FormStartPosition.CenterParent
                dialog.MinimizeBox = False
                dialog.MaximizeBox = False
                dialog.ClientSize = New Size(440, 250)

                Dim intro As New Label() With {
                    .Text = "Who administers " & If(String.IsNullOrWhiteSpace(registrationName), "this registration", registrationName.Trim()) & "?",
                    .Location = New Point(16, 14),
                    .Size = New Size(408, 20),
                    .ForeColor = Color.FromArgb(52, 60, 70)
                }

                Dim firstBox = AddRow(dialog, "First Name", 46)
                Dim lastBox = AddRow(dialog, "Last Name", 82)
                Dim userBox = AddRow(dialog, "User Name", 118)
                Dim passwordBox = AddRow(dialog, "Temporary Password", 154)
                passwordBox.UseSystemPasswordChar = True

                Dim okButton As New Button() With {
                    .Text = "OK", .DialogResult = DialogResult.OK,
                    .Location = New Point(180, 200), .Size = New Size(110, 30)
                }
                Dim cancelButton As New Button() With {
                    .Text = "Cancel", .DialogResult = DialogResult.Cancel,
                    .Location = New Point(300, 200), .Size = New Size(110, 30)
                }

                AddHandler okButton.Click,
                    Sub(sender, e)
                        result.FirstName = firstBox.Text
                        result.LastName = lastBox.Text
                        result.UserName = userBox.Text
                        result.TemporaryPassword = passwordBox.Text

                        If Not result.IsComplete Then
                            MessageBox.Show(dialog, "All four are required.", "First Administrator",
                                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
                            dialog.DialogResult = DialogResult.None
                        End If
                    End Sub

                dialog.Controls.Add(intro)
                dialog.Controls.Add(okButton)
                dialog.Controls.Add(cancelButton)
                dialog.AcceptButton = okButton
                dialog.CancelButton = cancelButton

                If dialog.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
            End Using

            Return result
        End Function

        Private Shared Function AddRow(dialog As Form, caption As String, top As Integer) As TextBox
            dialog.Controls.Add(New Label() With {
                .Text = caption,
                .Location = New Point(16, top + 4),
                .Size = New Size(150, 20)
            })

            Dim box As New TextBox() With {
                .Location = New Point(176, top),
                .Size = New Size(234, 26),
                .BorderStyle = BorderStyle.FixedSingle
            }
            dialog.Controls.Add(box)
            Return box
        End Function

    End Class

End Namespace
