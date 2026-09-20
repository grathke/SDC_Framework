Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' What has been dealt with, when, and what was done about it.
    '''
    ''' The health page answers "what is wrong now". Nothing answered "what was wrong, and what
    ''' happened about it" - a fault left Needs Attention the moment it was fixed and took its
    ''' history with it. Every column shown here was already being written; none of it was
    ''' displayed anywhere.
    '''
    ''' **A fix that did not hold is the most useful row in the list, so it is not filtered out.**
    ''' Telemetry clears Resolved when a fault recurs while keeping the Resolution text, and that
    ''' row - "this was fixed on the 14th, here is how, and it came back" - is what somebody opens
    ''' this window to find. Filtering on Resolved would hide exactly those. ResolvedOn being set
    ''' is what makes a row history; Resolved says whether it is still true.
    '''
    ''' **Who decided matters as much as what was done.** ResolvedSource separates an
    ''' administrator accepting a fault from the code having been changed, and only the second
    ''' predicts it stops happening. A recurrence after a code change deserves far more
    ''' scepticism than one after a shrug, and the list has to let somebody tell them apart.
    '''
    ''' The window and the scope are the page's, passed in rather than chosen again here. A
    ''' history covering a different period from the number that prompted it is the kind of quiet
    ''' mismatch nobody notices for months.
    ''' </summary>
    Public Class FW_FixHistory
        Inherits Form

        Private ReadOnly windowDays As Integer
        Private ReadOnly registrationId As Integer
        Private ReadOnly grid As DataGridView
        Private ReadOnly summaryLabel As Label

        Private Const DialogWidth As Integer = 980
        Private Const DialogHeight As Integer = 560

        Private Shared ReadOnly MutedColour As Color = Color.FromArgb(110, 118, 126)
        Private Shared ReadOnly RecurredColour As Color = Color.FromArgb(192, 57, 43)

        Public Sub New(days As Integer, registration As Integer)
            windowDays = days
            registrationId = registration

            Text = "What Has Been Fixed"
            StartPosition = FormStartPosition.CenterParent
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ClientSize = New Size(DialogWidth, DialogHeight)
            BackColor = Color.White

            summaryLabel = New Label() With {
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                .ForeColor = MutedColour,
                .Location = New Point(20, 16),
                .Size = New Size(DialogWidth - 40, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(summaryLabel)

            grid = New DataGridView() With {
                .Location = New Point(20, 44),
                .Size = New Size(DialogWidth - 40, DialogHeight - 108),
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .ReadOnly = True,
                .RowHeadersVisible = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .MultiSelect = False,
                .BackgroundColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle,
                .Font = New Font("Segoe UI", 9.5F),
                .EnableHeadersVisualStyles = False
            }

            grid.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 250)

            ' A selected row keeps a readable foreground. Left unset it takes the system's white,
            ' which over this tint disappears.
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(45, 48, 52)

            ' A header that stays put. Nothing here acts on the selected row, so a header lighting
            ' up with the selection would read as a chosen column or a sort order.
            BrowseGridStandardizer.ApplyStaticHeaderStyle(grid, Color.FromArgb(245, 246, 248), Color.FromArgb(45, 48, 52))

            ' The fix text is the point of the window, so it takes whatever is left after the four
            ' short columns rather than an equal share.
            grid.Columns.Add(NewColumn("Fixed", "Fixed", 130))
            grid.Columns.Add(NewColumn("Fault", "Fault", 210))
            grid.Columns.Add(NewColumn("Page", "Page", 130))
            grid.Columns.Add(NewColumn("By", "Decided by", 110))
            grid.Columns.Add(NewColumn("Resolution", "What was done", 310))

            Controls.Add(grid)

            Dim note As New Label() With {
                .Text = "A row in red was fixed and came back. Its text is what was tried last time.",
                .Font = New Font("Segoe UI", 8.5F, FontStyle.Italic),
                .ForeColor = MutedColour,
                .Location = New Point(20, DialogHeight - 54),
                .Size = New Size(620, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(note)

            Dim closeButton As New Button() With {
                .Text = "Close",
                .Font = New Font("Segoe UI", 10.0F),
                .Location = New Point(DialogWidth - 120, DialogHeight - 58),
                .Size = New Size(100, 30)
            }
            AddHandler closeButton.Click, Sub(s, e) Close()
            Controls.Add(closeButton)

            CancelButton = closeButton

            ' Read on Shown rather than in the constructor, for the reason the health page does the
            ' same: a round trip before the window has painted reads as the window being slow to
            ' open, and over Thinfinity that delay is the network as well as the query.
            ' Zoomable in its own right, and remembered separately - it is its own page as far as
            ' PageZoomStore is concerned. Somebody reading fix text does not want to be told the
            ' window they opened it from was the zoomable one.
            AddHandler Load, Sub(s, e) PageZoom.Attach(Me)

            AddHandler Shown, AddressOf FixHistory_Shown
        End Sub

        Private Shared Function NewColumn(name As String, header As String, width As Integer) As DataGridViewTextBoxColumn
            Return New DataGridViewTextBoxColumn() With {
                .Name = name,
                .HeaderText = header,
                .Width = width,
                .SortMode = DataGridViewColumnSortMode.NotSortable,
                .ReadOnly = True
            }
        End Function

        Private Sub FixHistory_Shown(sender As Object, e As EventArgs)
            Cursor = Cursors.WaitCursor

            Try
                Dim lines = HealthDataAccess.GetFixHistory(windowDays, registrationId)

                grid.Rows.Clear()

                If lines.Count = 0 Then
                    ' Said plainly rather than left as an empty grid. Nothing having been fixed and
                    ' the query having failed look identical in an empty table.
                    summaryLabel.Text = "Nothing has been fixed or accepted in the last " &
                                        windowDays.ToString(CultureInfo.CurrentCulture) & " day(s)."
                    Return
                End If

                ' Where().Count() rather than Count(predicate): List(Of T) has its own Count
                ' property, which hides the extension method that takes one.
                Dim recurred = lines.Where(Function(line) line.RecurredAfterResolved).Count()

                summaryLabel.Text = lines.Count.ToString(CultureInfo.CurrentCulture) &
                                    " dealt with in the last " &
                                    windowDays.ToString(CultureInfo.CurrentCulture) & " day(s)" &
                                    If(recurred > 0,
                                       ", of which " & recurred.ToString(CultureInfo.CurrentCulture) & " came back.",
                                       ".")

                For Each line In lines
                    Dim index = grid.Rows.Add(
                        line.ResolvedOn.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture),
                        line.ExceptionType,
                        If(line.PageName = String.Empty, "(none)", line.PageName),
                        DecidedBy(line),
                        If(line.Resolution = String.Empty, "(nothing recorded)", line.Resolution))

                    Dim row = grid.Rows(index)

                    ' The whole fix text on hover. It is routinely longer than any column width
                    ' that leaves room for the rest of the row.
                    row.Cells("Resolution").ToolTipText = line.Resolution

                    If line.RecurredAfterResolved Then
                        row.DefaultCellStyle.ForeColor = RecurredColour
                    ElseIf Not line.StillResolved Then
                        ' Open again without having been recorded as a recurrence. Rare, and not
                        ' worth shouting about, but it should not read as settled either.
                        row.DefaultCellStyle.ForeColor = MutedColour
                    End If

                    ' A row keeps its colour when the cursor lands on it. A red row turning the
                    ' default dark grey on selection would hide the one thing it is red about.
                    If Not row.DefaultCellStyle.ForeColor.IsEmpty Then
                        row.DefaultCellStyle.SelectionForeColor = row.DefaultCellStyle.ForeColor
                    End If
                Next

            Finally
                Cursor = Cursors.Default
            End Try
        End Sub

        ''' <summary>
        ''' Who or what decided the fault was finished with.
        '''
        ''' A name where there is one, because "an administrator accepted this" is a different
        ''' claim depending on which administrator. "Code change" where the source says so, and
        ''' "(not recorded)" for rows resolved before the column existed - which is honest, where
        ''' guessing would not be.
        ''' </summary>
        Private Shared Function DecidedBy(line As HealthDataAccess.FixHistoryLine) As String
            If String.Equals(line.ResolvedSource, "Claude", StringComparison.OrdinalIgnoreCase) Then
                Return "Code change"
            End If

            If line.ResolvedByName <> String.Empty Then Return line.ResolvedByName
            If String.Equals(line.ResolvedSource, "User", StringComparison.OrdinalIgnoreCase) Then Return "A person"

            Return "(not recorded)"
        End Function

    End Class

End Namespace
