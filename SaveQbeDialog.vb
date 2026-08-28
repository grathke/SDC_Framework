Option Strict On
Option Explicit On

Imports System.Windows.Forms

Namespace HelloWorld
    Public Class SaveQbeDialog
        Inherits Form

        Private ReadOnly nameTextBox As TextBox
        Private ReadOnly companyWideCheckBox As CheckBox

        Public ReadOnly Property QbeName As String
            Get
                Return If(nameTextBox.Text, String.Empty).Trim()
            End Get
        End Property

        Public ReadOnly Property IsCompanyWide As Boolean
            Get
                Return companyWideCheckBox.Checked
            End Get
        End Property

        Public Sub New()
            Me.Text = "Save QBE"
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.StartPosition = FormStartPosition.CenterParent
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New System.Drawing.Size(360, 145)
            Me.BackColor = System.Drawing.Color.White

            Dim nameLabel As New Label() With {
                .Text = "Name of Saved QBE",
                .Location = New System.Drawing.Point(16, 20),
                .AutoSize = True
            }

            nameTextBox = New TextBox() With {
                .Location = New System.Drawing.Point(160, 16),
                .Size = New System.Drawing.Size(184, 26),
                .BackColor = System.Drawing.Color.FromArgb(200, 240, 248)
            }

            companyWideCheckBox = New CheckBox() With {
                .Text = "CompanyWide",
                .Location = New System.Drawing.Point(160, 54),
                .AutoSize = True,
                .Checked = False
            }

            Dim okButton As New Button() With {
                .Text = "Ok",
                .DialogResult = DialogResult.OK,
                .Location = New System.Drawing.Point(188, 104),
                .Size = New System.Drawing.Size(75, 30)
            }

            Dim cancelButton As New Button() With {
                .Text = "Cancel",
                .DialogResult = DialogResult.Cancel,
                .Location = New System.Drawing.Point(272, 104),
                .Size = New System.Drawing.Size(75, 30)
            }

            Me.AcceptButton = okButton
            Me.CancelButton = cancelButton

            AddHandler okButton.Click, AddressOf OkButton_Click

            Me.Controls.Add(nameLabel)
            Me.Controls.Add(nameTextBox)
            Me.Controls.Add(companyWideCheckBox)
            Me.Controls.Add(okButton)
            Me.Controls.Add(cancelButton)
        End Sub

        Private Sub OkButton_Click(sender As Object, e As System.EventArgs)
            If String.IsNullOrWhiteSpace(nameTextBox.Text) Then
                MessageBox.Show("Please enter a name for the saved QBE.", "Name Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                nameTextBox.Focus()
                Me.DialogResult = DialogResult.None
            End If
        End Sub
    End Class
End Namespace
