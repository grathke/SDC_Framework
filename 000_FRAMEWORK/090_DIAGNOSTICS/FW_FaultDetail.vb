Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Globalization
Imports System.Text
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' One fault, in full, written so it can be handed straight to somebody who will fix it.
    '''
    ''' The health page's Needs Attention list shows a headline and a count. Everything else -
    ''' the message, the stack trace, the fingerprint, which machine and which build - is recorded
    ''' and was until now displayed nowhere. This is where it is read.
    '''
    ''' **The text is the point, not the layout.** It is laid out as a report with labelled
    ''' sections in a fixed-pitch face, so selecting the lot and pasting it into a conversation
    ''' with a developer - or into Claude Code - produces something that reads correctly at the
    ''' other end. That is the whole reason the window exists rather than a tooltip.
    '''
    ''' **Copy works on the desktop and is unreliable in a browser session.** Clipboard.SetText
    ''' puts text on the *server's* clipboard, and over Thinfinity the server is not the machine
    ''' the person is sitting at. So the text box is selectable and focused with everything
    ''' selected on open: in a session, Ctrl+C in the browser is the path that works, and the
    ''' button is the convenience for a desktop run. The button says which.
    ''' </summary>
    Public Class FW_FaultDetail
        Inherits Form

        Private ReadOnly detail As HealthDataAccess.FaultDetail
        Private ReadOnly reportBox As TextBox

        Private Const DialogWidth As Integer = 900
        Private Const DialogHeight As Integer = 640

        Public Sub New(faultDetail As HealthDataAccess.FaultDetail)
            detail = faultDetail

            Text = "Fault Detail"
            StartPosition = FormStartPosition.CenterParent
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ClientSize = New Size(DialogWidth, DialogHeight)
            BackColor = Color.White

            Dim heading As New Label() With {
                .Text = If(detail Is Nothing OrElse Not detail.Found,
                           "Fault not found",
                           detail.ExceptionType),
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(45, 48, 52),
                .Location = New Point(20, 16),
                .Size = New Size(DialogWidth - 40, 28),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(heading)

            Dim subheading As New Label() With {
                .Text = "Select all and copy, then hand this to whoever is fixing it.",
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(110, 118, 126),
                .Location = New Point(22, 44),
                .Size = New Size(DialogWidth - 44, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(subheading)

            ' Fixed pitch, because a stack trace read in a proportional face is much harder to
            ' follow - the frames stop lining up.
            reportBox = New TextBox() With {
                .Multiline = True,
                .ReadOnly = True,
                .ScrollBars = ScrollBars.Both,
                .WordWrap = False,
                .Font = New Font("Consolas", 9.5F),
                .BackColor = Color.FromArgb(250, 250, 251),
                .Location = New Point(20, 70),
                .Size = New Size(DialogWidth - 40, DialogHeight - 130),
                .Text = BuildReport()
            }
            Controls.Add(reportBox)

            Dim copyButton As New Button() With {
                .Text = "Copy to clipboard",
                .Font = New Font("Segoe UI", 10.0F),
                .Location = New Point(20, DialogHeight - 48),
                .Size = New Size(160, 30)
            }
            AddHandler copyButton.Click, AddressOf CopyButton_Click
            Controls.Add(copyButton)

            Dim sessionNote As New Label() With {
                .Text = "In a browser session, select the text above and press Ctrl+C instead.",
                .Font = New Font("Segoe UI", 8.5F, FontStyle.Italic),
                .ForeColor = Color.FromArgb(110, 118, 126),
                .Location = New Point(190, DialogHeight - 42),
                .Size = New Size(470, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(sessionNote)

            Dim closeButton As New Button() With {
                .Text = "Close",
                .Font = New Font("Segoe UI", 10.0F),
                .Location = New Point(DialogWidth - 120, DialogHeight - 48),
                .Size = New Size(100, 30)
            }
            AddHandler closeButton.Click, Sub(s, e) Close()
            Controls.Add(closeButton)

            CancelButton = closeButton

            ' Everything selected on open, so the browser path is one keystroke rather than a drag
            ' down a stack trace that scrolls.
            AddHandler Shown, Sub(s, e)
                                  reportBox.Focus()
                                  reportBox.SelectAll()
                              End Sub
        End Sub

        ''' <summary>
        ''' The fault as a report.
        '''
        ''' Ordered by what a reader needs first: what broke and where, then how often and since
        ''' when, then the stack. The fingerprint is included because it is the identity of the
        ''' fault - two reports carrying the same one are the same problem, however differently
        ''' somebody describes them.
        ''' </summary>
        Private Function BuildReport() As String
            If detail Is Nothing OrElse Not detail.Found Then
                Return "This fault could not be read from FW_ErrorLog." & Environment.NewLine &
                       "It may have been removed since the page was last refreshed."
            End If

            Dim report As New StringBuilder()

            report.AppendLine("FAULT")
            report.AppendLine("  Type          " & detail.ExceptionType)
            report.AppendLine("  Where caught  " & Blank(detail.Context))
            report.AppendLine("  Page          " & Blank(detail.PageName))
            report.AppendLine("  Origin        " & Blank(detail.Origin))
            report.AppendLine()

            report.AppendLine("HISTORY")
            report.AppendLine("  Occurrences   " & detail.OccurrenceCount.ToString("N0", CultureInfo.CurrentCulture))
            report.AppendLine("  First seen    " & Stamp(detail.FirstSeen))
            report.AppendLine("  Last seen     " & Stamp(detail.LastSeen))
            report.AppendLine()

            report.AppendLine("WHERE IT RAN")
            report.AppendLine("  Session       " & Blank(detail.SessionKind))
            report.AppendLine("  Machine       " & Blank(detail.MachineName))
            report.AppendLine("  Build         " & Blank(detail.AppVersion))
            report.AppendLine("  Fingerprint   " & detail.Fingerprint)
            report.AppendLine()

            report.AppendLine("MESSAGE")
            report.AppendLine(Indent(Blank(detail.Message)))
            report.AppendLine()

            report.AppendLine("STACK TRACE")

            If String.IsNullOrWhiteSpace(detail.StackTrace) Then
                ' A swallowed fault often has none. Saying so beats an empty heading, which reads
                ' as the report having been truncated.
                report.AppendLine("  (none recorded - this fault was caught without one)")
            Else
                report.AppendLine(Indent(detail.StackTrace))
            End If

            Return report.ToString()
        End Function

        Private Shared Function Blank(value As String) As String
            Return If(String.IsNullOrWhiteSpace(value), "(not recorded)", value.Trim())
        End Function

        Private Shared Function Stamp(value As Date) As String
            ' Shown in local time with the zone named. These are stored in UTC, and a time with no
            ' zone on it is the kind of detail that wastes an hour later.
            Return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) &
                   " (" & TimeZoneInfo.Local.StandardName & ")"
        End Function

        Private Shared Function Indent(value As String) As String
            If String.IsNullOrEmpty(value) Then Return String.Empty

            Dim lines = value.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            Dim builder As New StringBuilder()

            For Each line In lines
                builder.AppendLine("  " & line.TrimEnd())
            Next

            Return builder.ToString().TrimEnd()
        End Function

        Private Sub CopyButton_Click(sender As Object, e As EventArgs)
            Try
                Clipboard.SetText(reportBox.Text)

                MessageBox.Show(Me,
                                "COPIED." & Environment.NewLine & Environment.NewLine &
                                "IN A BROWSER SESSION THIS REACHES THE SERVER'S CLIPBOARD RATHER " &
                                "THAN YOURS - SELECT THE TEXT AND PRESS CTRL+C INSTEAD.",
                                "COPY",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information)

            Catch ex As Exception
                ' The clipboard is genuinely unavailable sometimes - another process holding it,
                ' or a session with none at all. The text is on screen and selectable either way.
                Telemetry.Error(ex, "FW_FaultDetail.CopyButton_Click", Telemetry.FaultOrigin.Swallowed)

                MessageBox.Show(Me,
                                "THE CLIPBOARD COULD NOT BE USED." & Environment.NewLine & Environment.NewLine &
                                "SELECT THE TEXT ABOVE AND PRESS CTRL+C.",
                                "COPY",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
            End Try
        End Sub

    End Class
End Namespace
