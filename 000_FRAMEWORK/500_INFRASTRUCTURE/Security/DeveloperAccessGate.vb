Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework
    ''' <summary>
    ''' A confirmation prompt in front of developer-only actions reached from the Application
    ''' dashboard, so one is never opened by a stray click.
    '''
    ''' This is a deliberate speed bump, not a security control. The accepted values are compiled
    ''' into the application, and a .NET assembly can be decompiled, so anyone holding the
    ''' executable can recover them. Real protection comes from the dashboard being reachable only
    ''' by an Application Admin, and from the credentials themselves being DPAPI-encrypted against
    ''' the Windows account. Never let this gate be the only thing protecting an operation.
    '''
    ''' Values are held as hashes rather than plain text, using the same keyed HMAC-SHA512 the user
    ''' password path uses (DataAccess.ComputeKeyedHashHex). Comparison is exact, including case,
    ''' and spaces are stripped exactly as they are for a user password.
    ''' </summary>
    Public NotInheritable Class DeveloperAccessGate

        ''' <summary>Key for the gate hash. Changing it invalidates every value below.</summary>
        Private Const GateKey As String = "WXFramework.DeveloperAccessGate.v1"

        Private Shared ReadOnly AcceptedHashes As String() = {
            "0a3949e7e3abe967041c38b2cdf56f82708bfce71e2b3934e7689f19bbfda96cfe9e7d38b87c53ae3555332cead86f299157d4e19b0e3565d1e17801f537e1e7",
            "918eeb4a061f388254b481e2e211bf09aa73628b50adb77dcc78cd89a1f44d20ca3d939100aecbe9f122a3c405c7fcf8c13655d53989632ca2eeb2538eb79390",
            "7e79eab6000281fb6dcc6778f5593881accf41762f965a2358832192524b80c5fc79241750117ab5c9efb278038a206e2818ef2fe79e14afd007cf13db7c0586",
            "fc124be2f12d1de496c2e1dc0867a20d8f34a7584ce9b2ff0d9a6632310c76677134cff9cf7a71ee2d16cb03f4fe0e29fe991f1cbdfeb29084f6306631f1c644",
            "aaa5590ccbdcd3ff092f5dcb47b47751fb5534bc04d78551c0050be3c9966d8490be40c9c1629223137c63cd76c3486adb0c6168abd79ef4f4d0ed5c4b143a17",
            "ad2afb829e771dccaa5ab1e601939be297b54affd83b9334dffd6b5a8d88549cc3a1d8388bd4ac0c21b4ce42a6b42764d179254765fe76af4304059dbbb64b04"
        }

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Prompts for the developer password. Returns True only when an accepted value is
        ''' entered; cancelling or closing the prompt returns False.
        ''' </summary>
        Public Shared Function Prompt(owner As IWin32Window, actionDescription As String) As Boolean
            Using promptForm As New DeveloperPasswordForm(actionDescription)
                Return promptForm.ShowDialog(owner) = DialogResult.OK
            End Using
        End Function

        Private Shared Function IsAccepted(candidate As String) As Boolean
            If String.IsNullOrEmpty(candidate) Then Return False

            Dim hash = DataAccess.ComputeKeyedHashHex(candidate, GateKey)
            Return AcceptedHashes.Any(Function(accepted) String.Equals(accepted, hash, StringComparison.Ordinal))
        End Function

        Private Class DeveloperPasswordForm
            Inherits Form

            Private ReadOnly passwordTextBox As TextBox
            Private ReadOnly messageLabel As Label

            Public Sub New(actionDescription As String)
                Text = "Developer Access"
                ClientSize = New Size(420, 190)
                FormBorderStyle = FormBorderStyle.FixedDialog
                StartPosition = FormStartPosition.CenterParent
                MaximizeBox = False
                MinimizeBox = False

                Dim titleLabel As New Label() With {
                    .Text = "Developer Access Required",
                    .Font = New Font("Segoe UI", 12.0F, FontStyle.Bold),
                    .Location = New Point(20, 15),
                    .AutoSize = True
                }

                Dim descriptionLabel As New Label() With {
                    .Text = actionDescription,
                    .Location = New Point(20, 45),
                    .Size = New Size(380, 32),
                    .ForeColor = SystemColors.GrayText
                }

                Dim passwordLabel As New Label() With {
                    .Text = "Password",
                    .Location = New Point(20, 88),
                    .Size = New Size(80, 24)
                }

                passwordTextBox = New TextBox() With {
                    .Name = "TextBox_DeveloperPassword",
                    .Location = New Point(105, 84),
                    .Size = New Size(295, 26),
                    .UseSystemPasswordChar = True,
                    .BorderStyle = BorderStyle.FixedSingle
                }

                messageLabel = New Label() With {
                    .Location = New Point(20, 116),
                    .Size = New Size(380, 20),
                    .ForeColor = Color.Firebrick
                }

                Dim okButton As New Button() With {.Text = "OK", .Size = New Size(90, 30), .Location = New Point(214, 146)}
                Dim cancelActionButton As New Button() With {.Text = "Cancel", .Size = New Size(90, 30), .Location = New Point(310, 146), .DialogResult = DialogResult.Cancel}

                AddHandler okButton.Click, AddressOf OkButton_Click
                AddHandler passwordTextBox.TextChanged, Sub(s, e) messageLabel.Text = String.Empty

                Controls.AddRange({titleLabel, descriptionLabel, passwordLabel, passwordTextBox, messageLabel, okButton, cancelActionButton})
                AcceptButton = okButton
                CancelButton = cancelActionButton
            End Sub

            Private Sub OkButton_Click(sender As Object, e As EventArgs)
                If Not IsAccepted(passwordTextBox.Text) Then
                    messageLabel.Text = "Incorrect password."
                    passwordTextBox.SelectAll()
                    passwordTextBox.Focus()
                    Return
                End If

                DialogResult = DialogResult.OK
                Close()
            End Sub
        End Class
    End Class
End Namespace
