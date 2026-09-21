Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Windows.Forms

Namespace SDC.Framework
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

        ''' <summary>
        ''' The names this user has already saved for this page, compared without case.
        '''
        ''' Held here rather than asked for on OK, because the check has to happen while the
        ''' dialog is still open - somebody told their name is taken needs the box they typed it
        ''' into, not a message after the window has gone.
        ''' </summary>
        Private ReadOnly existingNames As HashSet(Of String)

        Public Sub New(takenNames As IEnumerable(Of String))
            existingNames = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            If takenNames IsNot Nothing Then
                For Each taken In takenNames
                    If Not String.IsNullOrWhiteSpace(taken) Then existingNames.Add(taken.Trim())
                Next
            End If

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

            ' Typing is the only reason this window opens, and the box is where it goes. Set on
            ' Shown as well as here: tab order alone put the caret in roughly the right place,
            ' which is not the same as putting it there.
            Me.ActiveControl = nameTextBox
            AddHandler Me.Shown, Sub(s As Object, e As EventArgs)
                                     nameTextBox.Focus()
                                     nameTextBox.SelectAll()
                                 End Sub
        End Sub

        Private Sub OkButton_Click(sender As Object, e As System.EventArgs)
            If String.IsNullOrWhiteSpace(nameTextBox.Text) Then
                MessageBox.Show("Please enter a name for the saved QBE.", "Name Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                StayOpen()
                Return
            End If

            ' Saving over a name that is taken replaces somebody's saved search, and the save
            ' itself gives no sign of it: the data layer updates the existing row and the page
            ' then reports the name as saved, exactly as it does for a new one. Asked here, with
            ' the box still on screen, No is a chance to rename rather than a lost search.
            If existingNames.Contains(QbeName) Then
                Dim answer = MessageBox.Show(Me,
                    "A saved search called """ & QbeName & """ already exists for this page." &
                    Environment.NewLine & Environment.NewLine &
                    "Replace it? Its criteria will be overwritten and cannot be recovered.",
                    "REPLACE SAVED SEARCH", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2)

                If answer <> DialogResult.Yes Then
                    StayOpen()
                    Return
                End If
            End If
        End Sub

        ''' <summary>Keeps the window up with the name selected, ready to be typed over.</summary>
        Private Sub StayOpen()
            Me.DialogResult = DialogResult.None
            nameTextBox.Focus()
            nameTextBox.SelectAll()
        End Sub
    End Class
End Namespace
