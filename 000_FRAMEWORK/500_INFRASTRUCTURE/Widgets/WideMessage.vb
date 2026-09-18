Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' A message box for text that must not wrap.
    '''
    ''' MessageBox sizes itself to its longest line up to a fraction of the screen and then wraps,
    ''' and nothing in the call can change that. The page generation report is file names, one per
    ''' line, and a wrapped file name reads as two files - which is the one thing that report exists
    ''' to say clearly.
    '''
    ''' It measures the longest line and sizes to it, up to a maximum, and scrolls past that. Wide
    ''' enough to hold what it is given, never wider than the screen it is shown on.
    '''
    ''' Not a replacement for MessageBox. A two-line confirmation has nothing to gain here, and a
    ''' question needs Yes and No rather than an OK - this shows a report and is dismissed.
    ''' </summary>
    Public NotInheritable Class WideMessage

        Private Sub New()
        End Sub

        ''' <summary>The widest the dialog grows before the text starts to scroll instead.</summary>
        Private Const MaximumWidth As Integer = 900

        ''' <summary>Narrow enough looks like a fault, whatever the text measures.</summary>
        Private Const MinimumWidth As Integer = 420

        ''' <summary>The tallest it grows before the text starts to scroll instead.</summary>
        Private Const MaximumHeight As Integer = 700

        Public Shared Sub Show(owner As IWin32Window,
                               text As String,
                               title As String,
                               Optional icon As MessageBoxIcon = MessageBoxIcon.Information)
            Ask(owner, text, title, MessageBoxButtons.OK, icon)
        End Sub

        ''' <summary>
        ''' The same dialog with a question in it, for text too wide for MessageBox to ask.
        '''
        ''' The page update confirmation is six sentences naming three files, and MessageBox wrapped
        ''' the longest of them mid-sentence - so a message written to be read in lines was read in
        ''' fragments. Yes defaults to No, as the MessageBox call it replaces did.
        ''' </summary>
        Public Shared Function Ask(owner As IWin32Window,
                                   text As String,
                                   title As String,
                                   Optional buttons As MessageBoxButtons = MessageBoxButtons.OK,
                                   Optional icon As MessageBoxIcon = MessageBoxIcon.Information) As DialogResult
            If String.IsNullOrEmpty(text) Then Return DialogResult.None

            Using dialog As New Form()
                dialog.Text = If(String.IsNullOrWhiteSpace(title), String.Empty, title)
                ' Centred on the screen, not on the owner. The page update confirmation is raised
                ' while its page is still loading, so there is no owner on screen to centre over -
                ' and CenterParent falls back to wherever the unshown form happens to sit.
                dialog.StartPosition = FormStartPosition.CenterScreen
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog
                dialog.MinimizeBox = False
                dialog.MaximizeBox = False
                dialog.ShowInTaskbar = False
                dialog.BackColor = Color.White

                Dim body As New TextBox() With {
                    .Multiline = True,
                    .ReadOnly = True,
                    .WordWrap = False,
                    .ScrollBars = ScrollBars.Both,
                    .BorderStyle = BorderStyle.None,
                    .BackColor = Color.White,
                    .Font = New Font("Segoe UI", 9.75F, FontStyle.Regular),
                    .Text = text.Replace(vbLf, Environment.NewLine).Replace(vbCr & Environment.NewLine, Environment.NewLine),
                    .Dock = DockStyle.Fill
                }

                Dim bodyHost As New Panel() With {
                    .Dock = DockStyle.Fill,
                    .Padding = New Padding(16, 14, 16, 8),
                    .BackColor = Color.White
                }
                bodyHost.Controls.Add(body)

                Dim actions As New Panel() With {
                    .Dock = DockStyle.Bottom,
                    .Height = 52,
                    .BackColor = Color.White
                }
                ' Yes and No, or a single OK. Nothing else is offered, because nothing else has been
                ' needed and a third button is a third thing to lay out and default correctly.
                Dim asksAQuestion = buttons = MessageBoxButtons.YesNo

                Dim okButton As New Button() With {
                    .Text = If(asksAQuestion, "No", "OK"),
                    .Size = New Size(96, 30),
                    .DialogResult = If(asksAQuestion, DialogResult.No, DialogResult.OK),
                    .Anchor = AnchorStyles.Top Or AnchorStyles.Right
                }
                actions.Controls.Add(okButton)

                Dim yesButton As Button = Nothing
                If asksAQuestion Then
                    yesButton = New Button() With {
                        .Text = "Yes",
                        .Size = New Size(96, 30),
                        .DialogResult = DialogResult.Yes,
                        .Anchor = AnchorStyles.Top Or AnchorStyles.Right
                    }
                    actions.Controls.Add(yesButton)
                End If

                dialog.Controls.Add(bodyHost)
                dialog.Controls.Add(actions)
                dialog.AcceptButton = okButton
                dialog.CancelButton = okButton

                SizeToText(dialog, body, text)
                okButton.Location = New Point(dialog.ClientSize.Width - okButton.Width - 16, 11)
                If yesButton IsNot Nothing Then
                    yesButton.Location = New Point(okButton.Left - yesButton.Width - 8, 11)
                End If

                ' The caret sits at the end of a read-only box otherwise, scrolling a long report to
                ' its last line before anybody has seen the first.
                AddHandler dialog.Shown,
                    Sub(sender As Object, e As EventArgs)
                        body.SelectionStart = 0
                        body.SelectionLength = 0
                        okButton.Focus()
                    End Sub

                Return dialog.ShowDialog(owner)
            End Using
        End Function

        ''' <summary>
        ''' Sizes the dialog to its longest line and its line count, within the limits above.
        '''
        ''' Measured with the font the text is actually shown in, plus room for a vertical scroll
        ''' bar - which appears exactly when the height is capped, and would otherwise cover the
        ''' last few characters of every line it overlaps.
        ''' </summary>
        Private Shared Sub SizeToText(dialog As Form, body As TextBox, text As String)
            Dim lines = text.Replace(vbCrLf, vbLf).Split(ChrW(10))

            Dim widest = 0
            Dim lineHeight = 0
            Using g = dialog.CreateGraphics()
                For Each line In lines
                    If String.IsNullOrEmpty(line) Then Continue For
                    widest = Math.Max(widest, CInt(Math.Ceiling(g.MeasureString(line, body.Font).Width)))
                Next
                lineHeight = CInt(Math.Ceiling(g.MeasureString("Wy", body.Font).Height))
            End Using

            If lineHeight <= 0 Then lineHeight = 17

            Dim wanted = widest + 32 + SystemInformation.VerticalScrollBarWidth
            Dim width = Math.Max(MinimumWidth, Math.Min(MaximumWidth, wanted))

            ' Never wider than the screen it opens on. A browser session's viewport is smaller than
            ' the desktop's, and a dialog wider than the window cannot be moved back into view.
            Dim available = Screen.FromPoint(Cursor.Position).WorkingArea
            width = Math.Min(width, Math.Max(MinimumWidth, available.Width - 80))

            Dim height = (lines.Length * lineHeight) + 22 + 52 + SystemInformation.HorizontalScrollBarHeight
            height = Math.Max(160, Math.Min(MaximumHeight, height))
            height = Math.Min(height, Math.Max(160, available.Height - 80))

            dialog.ClientSize = New Size(width, height)
        End Sub
    End Class
End Namespace
