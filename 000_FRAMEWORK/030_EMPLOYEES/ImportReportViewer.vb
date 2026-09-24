Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Shows an import's page - its problems, or its results - inside the application, with Open In Browser to take it
    ''' out into a browser tab of its own.
    '''
    ''' Shown in a window first rather than handed to the browser as a download: a download lands
    ''' in somebody's Downloads folder and has to be found and opened, and the point of the page is
    ''' to be read now, beside the import that produced it. The browser tab is for keeping it,
    ''' printing it, or sending it to whoever owns the file - see BrowserDocument.
    '''
    ''' The embedded WebBrowser renders the page on the server like any other control, so the
    ''' browser session sees pixels and needs nothing of its own.
    ''' </summary>
    Friend Class ImportReportViewer
        Inherits Form

        Private ReadOnly html As String
        Private ReadOnly saveName As String
        Private ReadOnly linkCaption As String

        Friend Sub New(title As String, pageHtml As String, fileName As String, Optional browserLinkCaption As String = "Open the page")
            html = pageHtml
            saveName = fileName
            linkCaption = browserLinkCaption

            Text = title
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            StartPosition = FormStartPosition.CenterParent
            ClientSize = New Size(1080, 680)
            Font = New Font("Segoe UI", 9.0F)

            Dim browser As New WebBrowser() With {
                .Location = New Point(12, 12),
                .Size = New Size(ClientSize.Width - 24, ClientSize.Height - 66),
                .AllowWebBrowserDrop = False,
                .IsWebBrowserContextMenuEnabled = False,
                .ScriptErrorsSuppressed = True,
                .WebBrowserShortcutsEnabled = True
            }

            Dim saveButton As New Button() With {
                .Name = "Button_SaveReport",
                .Text = "Open In Browser",
                .Size = New Size(140, 30),
                .Location = New Point(ClientSize.Width - 264, ClientSize.Height - 44)
            }
            AddHandler saveButton.Click, AddressOf SaveButton_Click

            Dim closeButton As New Button() With {
                .Name = "Button_Close",
                .Text = "Close",
                .Size = New Size(100, 30),
                .Location = New Point(ClientSize.Width - 112, ClientSize.Height - 44),
                .DialogResult = DialogResult.Cancel
            }

            Controls.AddRange(New Control() {browser, saveButton, closeButton})
            CancelButton = closeButton

            browser.DocumentText = html
        End Sub

        ''' <summary>
        ''' Opens the page in a browser tab of its own - through a link in a browser session, in
        ''' the default browser on the desktop - where it can be kept, printed or sent on.
        ''' </summary>
        Private Sub SaveButton_Click(sender As Object, e As EventArgs)
            Try
                Dim encoding As New UTF8Encoding(True)
                Dim data = encoding.GetPreamble().Concat(encoding.GetBytes(html)).ToArray()
                BrowserDocument.Show(Me, BrowserDocument.ImportsArea, saveName, data, linkCaption)
            Catch ex As Exception
                Telemetry.Error(ex, "ImportReportViewer.SaveButton_Click")
                MessageBox.Show(Me, "The page could not be opened: " & ex.Message,
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub
    End Class
End Namespace
