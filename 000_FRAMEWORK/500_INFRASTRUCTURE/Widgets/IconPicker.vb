Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The one place an icon is chosen, and the one place a stored choice becomes a picture.
    '''
    ''' This lived inside PageGeneration_U, private to it, while the dashboards needed the same
    ''' dialog to let an icon be re-pictured at runtime. A second picker would have been a second
    ''' answer to "what can an icon be" - the families it offers, the system: prefix, the folders it
    ''' searches - and the two would have drifted the moment either was touched.
    '''
    ''' Choose returns the value to store, or empty for "keep what you had". Cancel and a close with
    ''' nothing selected are the same answer, deliberately: neither is a choice of no icon.
    ''' </summary>
    Public NotInheritable Class IconPicker

        Private Sub New()
        End Sub

        ''' <summary>
        ''' The icon families, in the order they are offered. They are chosen from as sets - a page
        ''' takes its picture from one style or the other - so they are kept whole and in this
        ''' order rather than sorted into each other. Plain alphabetical order let the loose files
        ''' fall between them: dashboard.png sat in the gap between Color and Fluent.
        '''
        ''' A family listed here that has no files simply contributes nothing.
        ''' </summary>
        Private Shared ReadOnly IconFamilyOrder As String() = {"Color_", "Fluent_"}

        Private Shared Function IconFamilyRank(fileName As String) As Integer
            For index = 0 To IconFamilyOrder.Length - 1
                If fileName.StartsWith(IconFamilyOrder(index), StringComparison.OrdinalIgnoreCase) Then Return index
            Next

            ' Anything outside the families sorts after them, alphabetically among itself.
            Return IconFamilyOrder.Length
        End Function

        ''' <summary>
        ''' Moved to AssetImages on 2026-09-12, when the main menu's region needed the same walk
        ''' back up to the repository's images. Kept as a name here because the call sites read
        ''' better for it.
        ''' </summary>
        Private Shared Function DashboardImagesFolder() As String
            Return AssetImages.Folder()
        End Function

        ''' A choice is either a file in assets\images or one of the built-in glyphs, marked with
        ''' the system: prefix. One place resolves both to a picture.
        Public Shared Function ResolveIconImage(choice As String) As Image
            Dim wanted = If(choice, String.Empty).Trim()
            If wanted = String.Empty Then Return Nothing

            Dim systemName = PageGenerator.SystemIconName(wanted)
            If systemName.Length > 0 Then
                Select Case systemName.ToUpperInvariant()
                    Case "APPLICATION" : Return SystemIcons.Application.ToBitmap()
                    Case "ASTERISK" : Return SystemIcons.Asterisk.ToBitmap()
                    Case "ERROR" : Return SystemIcons.Error.ToBitmap()
                    Case "EXCLAMATION" : Return SystemIcons.Exclamation.ToBitmap()
                    Case "HAND" : Return SystemIcons.Hand.ToBitmap()
                    Case "INFORMATION" : Return SystemIcons.Information.ToBitmap()
                    Case "QUESTION" : Return SystemIcons.Question.ToBitmap()
                    Case "SHIELD" : Return SystemIcons.Shield.ToBitmap()
                    Case "WARNING" : Return SystemIcons.Warning.ToBitmap()
                    Case "WINLOGO" : Return SystemIcons.WinLogo.ToBitmap()
                    Case Else : Return Nothing
                End Select
            End If

            Dim folder = DashboardImagesFolder()
            If folder = String.Empty Then Return Nothing
            Dim fullPath = Path.Combine(folder, wanted)
            If Not File.Exists(fullPath) Then Return Nothing

            Try
                ' Read through a stream so the preview does not lock the file.
                Using stream As New FileStream(fullPath, FileMode.Open, FileAccess.Read)
                    Return Image.FromStream(stream)
                End Using
            Catch
                Return Nothing
            End Try
        End Function

        ''' What the user sees in the list for a stored choice.
        Public Shared Function IconChoiceDisplay(choice As String) As String
            Dim systemName = PageGenerator.SystemIconName(choice)
            Return If(systemName.Length > 0, systemName & " (system)", If(choice, String.Empty).Trim())
        End Function

        ''' What is stored for a displayed choice.
        Public Shared Function IconChoiceValue(display As String) As String
            Dim text = If(display, String.Empty).Trim()
            If text.EndsWith(" (system)", StringComparison.OrdinalIgnoreCase) Then
                Return PageGenerator.SystemIconPrefix & text.Substring(0, text.Length - " (system)".Length)
            End If
            Return text
        End Function

        Public Shared Function Choose(owner As IWin32Window, currentChoice As String) As String
            Dim folder = DashboardImagesFolder()
            Dim files As New List(Of String)()
            If folder <> String.Empty Then
                files = Directory.GetFiles(folder).
                    Where(Function(item) {".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico"}.
                        Contains(Path.GetExtension(item).ToLowerInvariant())).
                    Select(Function(item) Path.GetFileName(item)).
                    OrderBy(Function(item) IconFamilyRank(item)).
                    ThenBy(Function(item) item, StringComparer.OrdinalIgnoreCase).
                    ToList()
            End If

            Dim chosen = IconChoiceValue(currentChoice)
            Dim tiles As New List(Of Panel)()

            Using dialog As New Form With {
                .Text = "Select Dashboard Icon",
                .StartPosition = FormStartPosition.CenterParent,
                .ClientSize = New Size(780, 560),
                .MinimizeBox = False,
                .MaximizeBox = False,
                .FormBorderStyle = FormBorderStyle.FixedDialog
            }
                Dim layout As New TableLayoutPanel With {
                    .Dock = DockStyle.Fill,
                    .ColumnCount = 2,
                    .RowCount = 2,
                    .Padding = New Padding(10)
                }
                layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
                layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
                layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
                layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 48))

                Dim systemGroup As New GroupBox With {.Text = "System Icons", .Dock = DockStyle.Fill}
                Dim fileGroup As New GroupBox With {.Text = "File Graphics", .Dock = DockStyle.Fill}
                Dim systemFlow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(8)}
                Dim fileFlow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(8)}
                systemGroup.Controls.Add(systemFlow)
                fileGroup.Controls.Add(fileFlow)

                Dim applyButton As New Button With {.Text = "Select", .DialogResult = DialogResult.OK, .AutoSize = True, .Enabled = chosen <> String.Empty}

                Dim highlight = Sub()
                                    For Each tile In tiles
                                        Dim value = Convert.ToString(tile.Tag)
                                        Dim isChosen = String.Equals(value, chosen, StringComparison.OrdinalIgnoreCase)
                                        tile.BackColor = If(isChosen, Color.FromArgb(221, 235, 247), SystemColors.Control)
                                        tile.BorderStyle = If(isChosen, BorderStyle.FixedSingle, BorderStyle.None)
                                    Next
                                    applyButton.Enabled = chosen <> String.Empty
                                End Sub

                Dim addTile = Sub(host As FlowLayoutPanel, value As String, caption As String)
                                  Dim tile As New Panel With {
                                      .Size = New Size(104, 104),
                                      .Margin = New Padding(6),
                                      .Tag = value,
                                      .Cursor = Cursors.Hand
                                  }
                                  Dim picture As New PictureBox With {
                                      .Size = New Size(48, 48),
                                      .Location = New Point(28, 10),
                                      .SizeMode = PictureBoxSizeMode.Zoom,
                                      .Image = ResolveIconImage(value)
                                  }
                                  Dim captionLabel As New Label With {
                                      .Text = caption,
                                      .AutoSize = False,
                                      .Size = New Size(100, 32),
                                      .Location = New Point(2, 64),
                                      .TextAlign = ContentAlignment.TopCenter
                                  }
                                  tile.Controls.Add(picture)
                                  tile.Controls.Add(captionLabel)

                                  ' The picture and the caption fill the tile, so the click has to be
                                  ' taken on all three or half the tile would be dead.
                                  For Each clickable As Control In New Control() {tile, picture, captionLabel}
                                      AddHandler clickable.Click, Sub()
                                                                      chosen = value
                                                                      highlight()
                                                                  End Sub
                                      AddHandler clickable.DoubleClick, Sub()
                                                                            chosen = value
                                                                            dialog.DialogResult = DialogResult.OK
                                                                            dialog.Close()
                                                                        End Sub
                                  Next

                                  tiles.Add(tile)
                                  host.Controls.Add(tile)
                              End Sub

                For Each systemName In PageGenerator.SystemIconNames
                    addTile(systemFlow, PageGenerator.SystemIconPrefix & systemName, systemName)
                Next
                For Each fileName In files
                    addTile(fileFlow, fileName, fileName)
                Next

                If files.Count = 0 Then
                    fileFlow.Controls.Add(New Label With {
                        .Text = "No images found in assets\images.",
                        .AutoSize = True,
                        .ForeColor = Color.DimGray,
                        .Margin = New Padding(6)
                    })
                End If

                Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
                Dim cancelButton As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .AutoSize = True}
                actions.Controls.Add(cancelButton)
                actions.Controls.Add(applyButton)

                layout.Controls.Add(systemGroup, 0, 0)
                layout.Controls.Add(fileGroup, 1, 0)
                layout.Controls.Add(actions, 1, 1)
                dialog.Controls.Add(layout)
                dialog.AcceptButton = applyButton
                dialog.CancelButton = cancelButton

                highlight()

                If dialog.ShowDialog(owner) = DialogResult.OK AndAlso chosen <> String.Empty Then
                    Return chosen
                End If
            End Using

            ' Cancelled, or closed with nothing selected. Empty means "keep what you had", which is
            ' what every caller has to tell apart - it is not a choice of no icon.
            Return String.Empty
        End Function
    End Class
End Namespace
