Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Asks what the fix was before a fault is called fixed.
    '''
    ''' A tick on its own records that somebody once believed the problem was solved, which is
    ''' worth very little three months later when it comes back. The note is the point, and it is
    ''' why this is a dialog rather than a second button column.
    '''
    ''' It does not insist. Somebody who genuinely has nothing to add should not be blocked from
    ''' recording the truth, and a required field here would only produce a table full of "fixed" -
    ''' which is worse than an empty one, because it looks like an answer. The prompt asks plainly
    ''' and takes no for an answer.
    ''' </summary>
    Public Class FW_FaultResolution
        Inherits Form

        Private ReadOnly resolutionBox As TextBox

        Private Const DialogWidth As Integer = 640
        Private Const DialogHeight As Integer = 320

        ''' <summary>What was typed. Empty is allowed and means nothing was added.</summary>
        Public ReadOnly Property Resolution As String
            Get
                Return resolutionBox.Text.Trim()
            End Get
        End Property

        Public Sub New(faultHeadline As String)
            Text = "Mark As Fixed"
            StartPosition = FormStartPosition.CenterParent
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ClientSize = New Size(DialogWidth, DialogHeight)
            BackColor = Color.White

            Dim heading As New Label() With {
                .Text = "Mark this fault as fixed",
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(45, 48, 52),
                .Location = New Point(20, 16),
                .Size = New Size(DialogWidth - 40, 26),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(heading)

            Dim faultLabel As New Label() With {
                .Text = faultHeadline,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(110, 118, 126),
                .Location = New Point(22, 44),
                .Size = New Size(DialogWidth - 44, 20),
                .AutoEllipsis = True,
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(faultLabel)

            Dim prompt As New Label() With {
                .Text = "What was the fix? A commit hash is more use than a description.",
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(45, 48, 52),
                .Location = New Point(20, 78),
                .Size = New Size(DialogWidth - 40, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(prompt)

            resolutionBox = New TextBox() With {
                .Multiline = True,
                .ScrollBars = ScrollBars.Vertical,
                .Font = New Font("Consolas", 9.5F),
                .Location = New Point(20, 102),
                .Size = New Size(DialogWidth - 40, 120),
                .MaxLength = 1000
            }
            Controls.Add(resolutionBox)

            Dim note As New Label() With {
                .Text = "If this fault happens again it will reopen itself, and the page will say it recurred.",
                .Font = New Font("Segoe UI", 8.5F, FontStyle.Italic),
                .ForeColor = Color.FromArgb(110, 118, 126),
                .Location = New Point(22, 228),
                .Size = New Size(DialogWidth - 44, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(note)

            Dim okButton As New Button() With {
                .Text = "Mark Fixed",
                .Font = New Font("Segoe UI", 10.0F),
                .Location = New Point(DialogWidth - 250, DialogHeight - 48),
                .Size = New Size(110, 30),
                .DialogResult = DialogResult.OK
            }
            Controls.Add(okButton)

            Dim cancelActionButton As New Button() With {
                .Text = "Cancel",
                .Font = New Font("Segoe UI", 10.0F),
                .Location = New Point(DialogWidth - 130, DialogHeight - 48),
                .Size = New Size(110, 30),
                .DialogResult = DialogResult.Cancel
            }
            Controls.Add(cancelActionButton)

            AcceptButton = okButton
            CancelButton = cancelActionButton

            AddHandler Shown, Sub(s, e) resolutionBox.Focus()
        End Sub

    End Class
End Namespace
