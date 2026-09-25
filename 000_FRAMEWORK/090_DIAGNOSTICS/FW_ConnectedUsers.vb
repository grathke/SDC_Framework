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
    ''' Who is signed in right now.
    '''
    ''' **It runs no query of its own.** The list is handed to it by the health page, already read
    ''' as the tenth result set of the snapshot the page was going to take anyway. A window opened
    ''' by clicking a number must not be able to disagree with the number that was clicked, and it
    ''' cannot if it never reads again. That also makes it free to open.
    '''
    ''' **It says when, not now.** The stamp is the page's, because that is the moment these rows
    ''' describe. HEALTH_DASHBOARD_SPEC.md section 6 argues this at length: a panel claiming to be
    ''' current when it is as old as the last refresh is worse than one that admits its age. What
    ''' has changed since that was written is the size of the lie - a closed browser used to linger
    ''' for minutes and now goes in about five seconds, so the staleness is the refresh interval
    ''' and nothing else.
    '''
    ''' **An abandoned session is shown, not hidden.** A process killed without writing an end
    ''' stays open until the sweep at the next startup closes it as a Crash, and until then it is
    ''' in this list inflating the count. Filtering those out would make the number right and the
    ''' window a liar about why. They are marked instead, so an inflated count explains itself.
    '''
    ''' The duration wording deliberately matches scripts\sessions.ps1, which answers the same
    ''' question from a terminal. Two readings of one table that are formatted differently invite
    ''' somebody to wonder whether they are measuring different things.
    ''' </summary>
    Public Class FW_ConnectedUsers
        Inherits Form

        Private ReadOnly sessions As IReadOnlyList(Of HealthDataAccess.ConnectedSession)
        Private ReadOnly takenAtUtc As Date
        Private ReadOnly grid As DataGridView
        Private ReadOnly summaryLabel As Label

        Private Const DialogWidth As Integer = 940
        Private Const DialogHeight As Integer = 520

        Private Shared ReadOnly MutedColour As Color = Color.FromArgb(110, 118, 126)
        Private Shared ReadOnly TextColour As Color = Color.FromArgb(45, 48, 52)

        ''' <summary>
        ''' The list and the moment it was read, both from the page. Nothing is optional: a window
        ''' that could be opened without a stamp would eventually be opened without one.
        ''' </summary>
        Public Sub New(connected As IReadOnlyList(Of HealthDataAccess.ConnectedSession), takenAt As Date)
            sessions = If(connected, CType(New List(Of HealthDataAccess.ConnectedSession)(), IReadOnlyList(Of HealthDataAccess.ConnectedSession)))
            takenAtUtc = takenAt

            Text = "Connected"
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
            grid.DefaultCellStyle.SelectionForeColor = TextColour

            ' Nothing here acts on the selected row, so a header that lights up with the selection
            ' would read as a chosen column or a sort order. Same reasoning as FW_FixHistory.
            BrowseGridStandardizer.ApplyStaticHeaderStyle(grid, Color.FromArgb(245, 246, 248), Color.FromArgb(45, 48, 52))

            grid.Columns.Add(NewColumn("Who", "Who", 170))
            grid.Columns.Add(NewColumn("Registration", "Registration", 150))
            grid.Columns.Add(NewColumn("How", "How", 90))

            ' Over Thinfinity the machine name is the server's, the same for everybody, so the
            ' browser's address is what distinguishes one person from another. The query prefers
            ' it and falls back to the machine only when there is none.
            grid.Columns.Add(NewColumn("From", "From", 150))

            grid.Columns.Add(NewColumn("Since", "Signed in", 110))
            grid.Columns.Add(NewColumn("For", "Connected", 100))

            ' Idle is the column that explains a count nobody believes. A session at four hours is
            ' almost certainly a process that died without saying so.
            grid.Columns.Add(NewColumn("Idle", "Idle", 100))

            Controls.Add(grid)

            Dim note As New Label() With {
                .Text = "One row per connection, so the same name twice is one person on two machines. Italics means idle long enough to have ended without saying so.",
                .Font = New Font("Segoe UI", 8.5F, FontStyle.Italic),
                .ForeColor = MutedColour,
                .Location = New Point(20, DialogHeight - 54),
                .Size = New Size(700, 20),
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

            ' Zoomable in its own right and remembered separately, as FW_FixHistory is.
            AddHandler Load, Sub(s, e) PageZoom.Attach(Me)

            ' Filled here rather than on Shown. FW_FixHistory waits because it queries, and a round
            ' trip before the window paints reads as the window being slow. This has the rows
            ' already, so waiting would only make an instant window appear to hesitate.
            Fill()
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

        Private Sub Fill()
            Dim stamp = " as at " & SessionTime.ToSessionZone(takenAtUtc).ToString("HH:mm", CultureInfo.CurrentCulture) & "."

            If sessions.Count = 0 Then
                ' This should not be reachable - reading the page makes you one of these rows - so
                ' it is said plainly rather than left as an empty grid. An empty list and a failed
                ' read look identical otherwise.
                summaryLabel.Text = "Nothing is recorded as connected" & stamp
                Return
            End If

            ' Where().Count() rather than Count(predicate): List(Of T) has its own Count property,
            ' which hides the extension method taking one.
            Dim abandoned = sessions.Where(Function(s) s.LooksAbandoned).Count()

            ' Connections, not people. The same name twice is one person on two machines, which is
            ' two licences and is exactly what somebody opens this window to find out.
            Dim people = sessions.Select(Function(s) s.WhoName).Distinct().Count()

            summaryLabel.Text = sessions.Count.ToString(CultureInfo.CurrentCulture) &
                                If(sessions.Count = 1, " connection", " connections") &
                                If(people < sessions.Count,
                                   " from " & people.ToString(CultureInfo.CurrentCulture) &
                                   If(people = 1, " person", " people"),
                                   String.Empty) &
                                stamp &
                                If(abandoned > 0,
                                   " " & abandoned.ToString(CultureInfo.CurrentCulture) &
                                   If(abandoned = 1, " looks", " look") &
                                   " abandoned and will close at the next restart.",
                                   String.Empty)

            For Each item In sessions
                Dim index = grid.Rows.Add(
                    item.WhoName,
                    If(item.RegistrationName = String.Empty, "(none)", item.RegistrationName),
                    If(item.SessionKind = String.Empty, "(unknown)", item.SessionKind),
                    If(item.FromWhere = String.Empty, "(unrecorded)", item.FromWhere),
                    SessionTime.ToSessionZone(item.StartedOn).ToString("d MMM HH:mm", CultureInfo.CurrentCulture),
                    Duration(item.ConnectedSeconds),
                    IdleText(item))

                Dim row = grid.Rows(index)
                row.DefaultCellStyle.ForeColor = TextColour
                row.DefaultCellStyle.SelectionForeColor = TextColour

                ' Italic rather than red. The same argument FW_FixHistory makes for a column over a
                ' colour: weight and style survive a grey screenshot, a colour-blind reader and a
                ' printer, and the Idle column already says the number out loud.
                If item.LooksAbandoned Then
                    row.DefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Italic)
                End If
            Next

            ' Nobody chose a row. A grid selects its first the moment it fills, and the tint that
            ' follows reads as a choice somebody made.
            grid.ClearSelection()
            grid.CurrentCell = Nothing
        End Sub

        ''' <summary>
        ''' "45s", "4m 40s", "2h 15m" - the same wording scripts\sessions.ps1 prints, so the two
        ''' readings of one table cannot look like two different measurements.
        ''' </summary>
        Private Shared Function Duration(seconds As Integer) As String
            If seconds < 0 Then Return String.Empty
            If seconds < 60 Then Return seconds.ToString(CultureInfo.CurrentCulture) & "s"

            If seconds < 3600 Then
                Return (seconds \ 60).ToString(CultureInfo.CurrentCulture) & "m " &
                       (seconds Mod 60).ToString(CultureInfo.CurrentCulture) & "s"
            End If

            Return (seconds \ 3600).ToString(CultureInfo.CurrentCulture) & "h " &
                   ((seconds Mod 3600) \ 60).ToString(CultureInfo.CurrentCulture) & "m"
        End Function

        ''' <summary>
        ''' Idle is blank-looking for a reason when nothing has been done: "nothing yet" says that
        ''' the person has not searched or opened a page, which is not the same as their having
        ''' been active a moment ago. A zero would claim the second.
        ''' </summary>
        Private Shared Function IdleText(item As HealthDataAccess.ConnectedSession) As String
            If item.IdleSeconds < 0 Then Return "nothing yet"
            Return Duration(item.IdleSeconds)
        End Function
    End Class

End Namespace
