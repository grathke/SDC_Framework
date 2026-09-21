Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Is this installation healthy right now.
    '''
    ''' One number and a needle, backed by three tables that already exist, with the counts behind
    ''' the number underneath it so the reading can be checked rather than merely trusted. Fully
    ''' specified in HEALTH_DASHBOARD_SPEC.md - read that before changing the scoring, the layout,
    ''' or what the page is allowed to show.
    '''
    ''' **Inherits Form, not FW_Base_B.** The _B suffix is for consistency with the other
    ''' dashboards rather than because it browses a table, and FW_HD_AdminDashboard_B is the same
    ''' shape. There is no single table behind it, no QBE row, no CRUD: the base class has nothing
    ''' to offer it and its contract could not be satisfied.
    '''
    ''' **It does not take the registration predicate, and that is deliberate.** Spec section 5:
    ''' health is our view of the installation, not a customer's view of their slice. It is only
    ''' safe because the page is App Admin only and because it shows counts and fault signatures,
    ''' never record values.
    '''
    ''' The period selector drives every panel at once. One window of time for the whole page, so
    ''' the needle and the counts below it cannot disagree.
    ''' </summary>
    Public Class FW_Health_B
        Inherits Form

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        Private ReadOnly gauge As HealthGauge

        ''' <summary>
        ''' The arithmetic behind the needle, itemised.
        '''
        ''' A gauge reading 99.3 with an empty fault list looks broken. It is not - the missing
        ''' seven tenths were three failed saves out of 209 - but nothing on the page said so, and
        ''' a number nobody can account for stops being believed.
        ''' </summary>
        Private ReadOnly breakdownLabel As Label

        Private ReadOnly historyButton As Button
        Private ReadOnly periodCombo As ComboBox
        Private ReadOnly registrationCombo As ComboBox
        Private ReadOnly refreshButton As Button
        Private ReadOnly closeButton As Button
        Private ReadOnly asAtLabel As Label
        Private ReadOnly queryStoreLabel As Label

        ''' <summary>
        ''' How many people are signed in, and the way into the list of who.
        '''
        ''' A label rather than a tile because the tile column is full and the three that are in it
        ''' measure the window the period combo selects. This measures right now, which is a
        ''' different kind of fact, and putting it beside the "as at" stamp says so.
        ''' </summary>
        Private ReadOnly connectedLabel As LinkLabel

        ''' <summary>Who is on, beside the tiles. The dialog carries what will not fit here.</summary>
        Private ReadOnly connectedGrid As DataGridView

        ''' <summary>
        ''' Query Store on or off, shown and changed by the one control.
        '''
        ''' Ticked means recording. READ_ONLY - filled its quota and stopped - reads as unticked,
        ''' because the tick answers "is anything being kept" and the honest answer there is no.
        ''' It also leaves the person something to click: ticking names READ_WRITE and repairs it.
        ''' </summary>
        Private ReadOnly queryStoreCheck As CheckBox

        Private ReadOnly savesTile As Panel
        Private ReadOnly faultsTile As Panel
        Private ReadOnly fallbacksTile As Panel

        Private ReadOnly activityPanel As Panel
        Private ReadOnly timingPanel As Panel

        ''' <summary>
        ''' Which page the timing panel is describing.
        '''
        ''' The panel showed the slowest page and nothing else, which was defensible while one page
        ''' had ever been searched and misleading the moment four had: three pages were being
        ''' recorded and had never once been on screen. A drop-down keeps the four figures that
        ''' matter - database and whole find, average and worst - for whichever page is asked about,
        ''' rather than reducing every page to one row to fit them all in.
        '''
        ''' It sits beside the heading rather than inside the panel, so refilling the rows does not
        ''' destroy the control that chose them.
        ''' </summary>
        Private ReadOnly timingPageCombo As ComboBox

        ''' <summary>Set while the combo is being repopulated, so doing so does not redraw.</summary>
        Private timingComboFilling As Boolean
        Private ReadOnly attentionGrid As DataGridView
        Private ReadOnly loginGrid As DataGridView

        Private snapshot As HealthDataAccess.HealthSnapshot

        Private Const PageWidth As Integer = 1180
        Private Const PageHeight As Integer = 800

        ''' <summary>
        ''' The tile column and the connected grid share the space the tiles used to have alone.
        '''
        ''' The three tiles ran 560 to 1100 at 540 wide. The grid is carved out of that, not added
        ''' beside it: the tiles lose the grid's width plus a gap and the block's right edge stays
        ''' at 1100, so the page keeps its footprint and nothing outside the column moves.
        '''
        ''' Change <see cref="ConnectedGridWidth"/> and the tiles follow. Two figures that have to
        ''' be edited together are two figures that eventually are not.
        ''' </summary>
        Private Const TileBlockLeft As Integer = 560
        Private Const TileBlockRight As Integer = 1100
        Private Const ConnectedGridWidth As Integer = 241
        Private Const ConnectedGridGap As Integer = 16
        Private Const ConnectedGridLeft As Integer = TileBlockRight - ConnectedGridWidth
        Private Const TileWidth As Integer = ConnectedGridLeft - ConnectedGridGap - TileBlockLeft

        Private Shared ReadOnly HeadingColour As Color = Color.FromArgb(45, 48, 52)
        Private Shared ReadOnly MutedColour As Color = Color.FromArgb(110, 118, 126)

        ''' <summary>The scope entry that means no filter, which is how the page opens.</summary>
        Private Shared ReadOnly EveryRegistration As New RegistrationOption With {.ID = 0, .Name = "All registrations"}
        Private Shared ReadOnly PanelBorder As Color = Color.FromArgb(222, 226, 230)

        ''' <summary>The resting colour of a grid header here, and its selected colour too.</summary>
        Private Shared ReadOnly GridHeaderColour As Color = Color.FromArgb(245, 246, 248)

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = If(user, BuildCurrentUserFromSession())
            accessProfile = If(profile,
                               MenuFormInitializer.BuildAccessProfileForCurrentSession(currentUser, NameOf(FW_Health_B)))

            Text = "System Health"
            StartPosition = FormStartPosition.CenterParent

            ' Fixed, like every other page. Nearly every run is a VirtualUI session, where a window
            ' has no business growing past the canvas it is drawn on.
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ClientSize = New Size(PageWidth, PageHeight)
            BackColor = Color.White

            gauge = New HealthGauge()
            breakdownLabel = New Label()
            historyButton = New Button()
            periodCombo = New ComboBox()
            registrationCombo = New ComboBox()
            refreshButton = New Button()
            closeButton = New Button()
            asAtLabel = New Label()
            connectedLabel = New LinkLabel()
            connectedGrid = New DataGridView()
            queryStoreLabel = New Label()
            queryStoreCheck = New CheckBox()
            savesTile = New Panel()
            faultsTile = New Panel()
            fallbacksTile = New Panel()
            activityPanel = New Panel()
            timingPanel = New Panel()
            timingPageCombo = New ComboBox()
            attentionGrid = New DataGridView()
            loginGrid = New DataGridView()

            BuildHeader()
            BuildGaugeAndTiles()
            BuildActivity()
            BuildNeedsAttention()

            ' Zoom on Load rather than Shown, so a remembered zoom is what the window opens as.
            ' From Shown the reader sees the unzoomed page for a frame and watches it jump. Every
            ' control here is placed with an explicit Location and Size in the constructor, which
            ' is what makes the layout snapshot safe this early.
            '
            ' The grids get their columns in the constructor too, so the snapshot has them. Rows
            ' arriving later on Shown take RowTemplate.Height, which PageZoom has already scaled.
            AddHandler Load,
                Sub(s, e)
                    PageZoom.Attach(Me)

                    ' The read-out lines up under the fault grid rather than in the form's
                    ' corner, which puts it in the gap that already exists below the lowest
                    ' panel instead of hard against the window edge.
                    PageZoom.SetIndicatorAnchor(Me, attentionGrid)
                End Sub

            AddHandler Shown, AddressOf Health_Shown
        End Sub

        ''' <summary>
        ''' Loaded once the window is up rather than in the constructor.
        '''
        ''' The query is quick, but it is still a round trip, and doing it before the form is
        ''' painted means a session opening the page sees nothing at all until it returns. Over
        ''' Thinfinity that delay is the network as well as the query.
        ''' </summary>
        Private Sub Health_Shown(sender As Object, e As EventArgs)
            LoadSnapshot()
        End Sub

        Private Sub BuildHeader()
            Dim title As New Label() With {
                .Text = "SYSTEM HEALTH",
                .Font = New Font("Segoe UI", 15.0F, FontStyle.Bold),
                .ForeColor = HeadingColour,
                .Location = New Point(24, 18),
                .Size = New Size(360, 34),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(title)

            ' The scope is a control rather than a caption, because it can now be narrowed. It
            ' still opens on every registration, which is what this page is for - narrowing is for
            ' answering "is it just them?" and the page should not start by assuming it is.
            ' Beside the period combo and on its baseline, because the two are one thought: the
            ' slice of the installation being looked at. Under the title they read as a caption on
            ' the page rather than as a control somebody can change.
            registrationCombo.DropDownStyle = ComboBoxStyle.DropDownList
            registrationCombo.Font = New Font("Segoe UI", 10.0F)
            registrationCombo.Location = New Point(PageWidth - 660, 22)
            registrationCombo.Size = New Size(220, 28)
            registrationCombo.DropDownWidth = registrationCombo.Width
            Controls.Add(registrationCombo)

            ' Only the "everything" entry is added here. The rest arrive with the first snapshot,
            ' which carries them - see HealthDataAccess. Reading them in the constructor would be
            ' a database round trip before the form has painted, which is the very thing the
            ' snapshot load was moved to Shown to avoid, and over Thinfinity that delay is the
            ' network as well as the query.
            registrationCombo.Items.Add(EveryRegistration)
            registrationCombo.SelectedIndex = 0

            AddHandler registrationCombo.SelectedIndexChanged, AddressOf RegistrationCombo_Changed

            ' A fact about the installation, not a suggestion. Query Store is SQL Server's own
            ' flight recorder - every query's text, plan and timings - and with it off none of
            ' that is being kept. Shown for the same reason telemetry being off would be shown:
            ' an installation recording nothing looks identical to one with no problems.
            ' Moved under the title once the scope combo took its place on the top row. It reads
            ' better there anyway: a standing fact about the installation belongs with the heading,
            ' not among the controls somebody is about to change.
            ' The tick is the control and the label is the commentary. Splitting them lets the
            ' label say the thing a tick cannot - that it is on but has stopped recording - while
            ' the tick stays a plain answer to "is anything being kept".
            queryStoreCheck.Text = "Query Store"
            queryStoreCheck.Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)
            queryStoreCheck.ForeColor = MutedColour
            queryStoreCheck.Location = New Point(24, 48)
            queryStoreCheck.Size = New Size(100, 22)
            queryStoreCheck.TextAlign = ContentAlignment.MiddleLeft
            queryStoreCheck.Visible = False
            Controls.Add(queryStoreCheck)

            ' Click rather than CheckedChanged. CheckedChanged fires when the page sets the tick
            ' from the snapshot, which would have every refresh trying to alter the database.
            AddHandler queryStoreCheck.Click, AddressOf QueryStoreCheck_Click

            queryStoreLabel.Text = String.Empty
            queryStoreLabel.Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)
            queryStoreLabel.ForeColor = MutedColour
            queryStoreLabel.Location = New Point(128, 50)
            queryStoreLabel.Size = New Size(420, 20)
            queryStoreLabel.TextAlign = ContentAlignment.MiddleLeft
            Controls.Add(queryStoreLabel)

            periodCombo.DropDownStyle = ComboBoxStyle.DropDownList
            periodCombo.Font = New Font("Segoe UI", 10.0F)
            periodCombo.Location = New Point(PageWidth - 430, 22)
            periodCombo.Size = New Size(160, 28)
            periodCombo.Items.AddRange(New Object() {"Last 24 hours", "Last 7 days", "Last 30 days", "Last 90 days"})

            ' Opens on the last 24 hours. It was 7 days, on the reasoning that a week is enough to
            ' show a trend - but the first question anybody asks this page is "is it all right
            ' now", and a week-wide window answers a different one. A week of history is one click
            ' away; today is what the page is for.
            periodCombo.SelectedIndex = 0

            ' The drop-down never opens wider than its box - the rule ComboWidth.Narrow applies
            ' everywhere else, said by hand here because this page builds its own controls.
            periodCombo.DropDownWidth = periodCombo.Width
            AddHandler periodCombo.SelectedIndexChanged, Sub(s, e) LoadSnapshot()
            Controls.Add(periodCombo)

            asAtLabel.Text = String.Empty
            asAtLabel.Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)
            asAtLabel.ForeColor = MutedColour
            asAtLabel.Location = New Point(PageWidth - 430, 52)
            asAtLabel.Size = New Size(260, 18)
            asAtLabel.TextAlign = ContentAlignment.MiddleLeft
            Controls.Add(asAtLabel)

            ' The gap between the Query Store text, which ends at x=548, and the "as at" stamp,
            ' which begins at x=750. The page is FixedDialog at 1180 wide with every band spoken
            ' for, and this is the one piece of header that was free.
            connectedLabel.Text = String.Empty
            connectedLabel.Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)
            connectedLabel.LinkColor = Color.FromArgb(28, 90, 168)
            connectedLabel.ActiveLinkColor = Color.FromArgb(28, 90, 168)
            connectedLabel.VisitedLinkColor = Color.FromArgb(28, 90, 168)
            connectedLabel.LinkBehavior = LinkBehavior.HoverUnderline
            connectedLabel.Location = New Point(556, 50)
            connectedLabel.Size = New Size(186, 20)
            connectedLabel.TextAlign = ContentAlignment.MiddleLeft
            AddHandler connectedLabel.LinkClicked, AddressOf ConnectedLabel_LinkClicked
            Controls.Add(connectedLabel)

            refreshButton.Text = "Refresh"
            refreshButton.Font = New Font("Segoe UI", 10.0F)
            refreshButton.Location = New Point(PageWidth - 250, 21)
            refreshButton.Size = New Size(100, 30)
            AddHandler refreshButton.Click, Sub(s, e) LoadSnapshot()
            Controls.Add(refreshButton)

            closeButton.Text = "Close"
            closeButton.Font = New Font("Segoe UI", 10.0F)
            closeButton.Location = New Point(PageWidth - 140, 21)
            closeButton.Size = New Size(100, 30)
            AddHandler closeButton.Click, Sub(s, e) Close()
            Controls.Add(closeButton)

            CancelButton = closeButton
        End Sub

        Private Sub BuildGaugeAndTiles()
            ' Twenty-four pixels shorter than it was, to give the breakdown a line under it. The
            ' readout sizes itself into whatever the arc leaves, so the gauge takes the loss
            ' without anything being clipped.
            gauge.Location = New Point(60, 90)
            gauge.Size = New Size(420, 246)
            Controls.Add(gauge)

            breakdownLabel.Text = String.Empty
            breakdownLabel.Font = New Font("Segoe UI", 8.5F, FontStyle.Regular)
            breakdownLabel.ForeColor = MutedColour
            breakdownLabel.Location = New Point(26, 340)
            breakdownLabel.Size = New Size(516, 42)
            breakdownLabel.TextAlign = ContentAlignment.TopCenter
            Controls.Add(breakdownLabel)

            StyleTile(savesTile, "SAVES", 560, 95)
            StyleTile(faultsTile, "FAULTS", 560, 190)
            StyleTile(fallbacksTile, "FALLBACKS", 560, 285)

            BuildConnectedGrid()
        End Sub

        ''' <summary>
        ''' The connected list beside the tiles, in space the tiles gave up.
        '''
        ''' The three tiles were 540 wide with a great deal of nothing between their heading and
        ''' their number. They are now <see cref="TileWidth"/>, and the difference plus a gap is
        ''' exactly this grid - so the block still runs from 560 to 1100 and nothing else on the
        ''' page moved.
        '''
        ''' Two columns and no more. Registration, where from and idle do not fit in 241 pixels
        ''' and are what the dialog is for; this answers "who is on" without a click.
        ''' </summary>
        Private Sub BuildConnectedGrid()
            Dim heading As New Label() With {
                .Text = "CONNECTED",
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = MutedColour,
                .Location = New Point(ConnectedGridLeft, 74),
                .Size = New Size(200, 18),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(heading)

            connectedGrid.Location = New Point(ConnectedGridLeft, 95)
            connectedGrid.Size = New Size(ConnectedGridWidth, 270)
            connectedGrid.AllowUserToAddRows = False
            connectedGrid.AllowUserToDeleteRows = False
            connectedGrid.AllowUserToResizeRows = False
            connectedGrid.AllowUserToResizeColumns = False
            connectedGrid.ReadOnly = True
            connectedGrid.RowHeadersVisible = False
            connectedGrid.ColumnHeadersVisible = True
            connectedGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            connectedGrid.MultiSelect = False
            connectedGrid.BackgroundColor = Color.White
            connectedGrid.BorderStyle = BorderStyle.FixedSingle
            connectedGrid.Font = New Font("Segoe UI", 9.0F)
            connectedGrid.EnableHeadersVisualStyles = False
            connectedGrid.ScrollBars = ScrollBars.Vertical
            connectedGrid.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)

            BrowseGridStandardizer.ApplyStaticHeaderStyle(connectedGrid,
                                                          Color.FromArgb(245, 246, 248),
                                                          Color.FromArgb(45, 48, 52))

            connectedGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "Who", .HeaderText = "Who", .Width = 140,
                .SortMode = DataGridViewColumnSortMode.NotSortable, .ReadOnly = True})

            connectedGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "Since", .HeaderText = "Since", .Width = 80,
                .SortMode = DataGridViewColumnSortMode.NotSortable, .ReadOnly = True})

            ' The Existing Owner Gate: double-click performs the same action the visible command
            ' does, by calling it, rather than opening the dialog a second way.
            AddHandler connectedGrid.CellDoubleClick,
                Sub(s, e) ConnectedLabel_LinkClicked(Nothing, Nothing)

            Controls.Add(connectedGrid)
        End Sub

        Private Sub StyleTile(tile As Panel, heading As String, left As Integer, top As Integer)
            tile.Location = New Point(left, top)
            tile.Size = New Size(TileWidth, 80)
            tile.BackColor = Color.FromArgb(249, 250, 251)
            tile.BorderStyle = BorderStyle.FixedSingle

            ' Narrowed with the tile. The heading used to have 200 pixels and the value 200 more,
            ' with a hundred of nothing between them; at 283 they have to share, so the heading
            ' takes what "FALLBACKS" needs and the value takes the rest, still right-aligned
            ' against the same 20-pixel margin it always had.
            Dim headingLabel As New Label() With {
                .Name = "Heading",
                .Text = heading,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = MutedColour,
                .Location = New Point(14, 10),
                .Size = New Size(120, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            tile.Controls.Add(headingLabel)

            Dim valueLabel As New Label() With {
                .Name = "Value",
                .Text = "--",
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Bold),
                .ForeColor = HeadingColour,
                .Location = New Point(TileWidth - 20 - 120, 8),
                .Size = New Size(120, 38),
                .TextAlign = ContentAlignment.MiddleRight
            }
            tile.Controls.Add(valueLabel)

            Dim detailLabel As New Label() With {
                .Name = "Detail",
                .Text = String.Empty,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                .ForeColor = MutedColour,
                .Location = New Point(14, 48),
                .Size = New Size(TileWidth - 28, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            tile.Controls.Add(detailLabel)

            Controls.Add(tile)
        End Sub

        Private Shared Sub SetTile(tile As Panel, value As String, detail As String, valueColour As Color)
            Dim valueLabel = TryCast(tile.Controls("Value"), Label)
            Dim detailLabel = TryCast(tile.Controls("Detail"), Label)

            If valueLabel IsNot Nothing Then
                valueLabel.Text = value
                valueLabel.ForeColor = valueColour
            End If

            If detailLabel IsNot Nothing Then detailLabel.Text = detail
        End Sub

        Private Sub BuildActivity()
            Dim heading As New Label() With {
                .Text = "ACTIVITY",
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = MutedColour,
                .Location = New Point(26, 386),
                .Size = New Size(200, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(heading)

            ' Narrowed from full width to make room for the timing beside it. Six operation types
            ' in two rows still fit; a seventh would wrap to a third row and be clipped, which the
            ' fill guards against by stopping rather than drawing off the bottom.
            activityPanel.Location = New Point(24, 410)
            activityPanel.Size = New Size(700, 92)
            activityPanel.BackColor = Color.White
            activityPanel.BorderStyle = BorderStyle.FixedSingle
            Controls.Add(activityPanel)

            Dim timingHeading As New Label() With {
                .Text = "SEARCH TIMING",
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = MutedColour,
                .Location = New Point(742, 386),
                .Size = New Size(250, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(timingHeading)

            timingPageCombo.DropDownStyle = ComboBoxStyle.DropDownList
            timingPageCombo.Font = New Font("Segoe UI", 9.0F)
            timingPageCombo.Location = New Point(PageWidth - 204, 383)
            timingPageCombo.Size = New Size(180, 22)

            ' The drop-down never opens wider than its box - the rule ComboWidth.Narrow applies
            ' everywhere else, said by hand here because this page builds its own controls.
            timingPageCombo.DropDownWidth = timingPageCombo.Width
            timingPageCombo.Visible = False
            Controls.Add(timingPageCombo)
            AddHandler timingPageCombo.SelectedIndexChanged, AddressOf TimingPageCombo_Changed

            timingPanel.Location = New Point(740, 410)
            timingPanel.Size = New Size(PageWidth - 764, 92)
            timingPanel.BackColor = Color.White
            timingPanel.BorderStyle = BorderStyle.FixedSingle
            Controls.Add(timingPanel)
        End Sub

        ''' <summary>
        ''' What Find costs, and where the time is going.
        '''
        ''' The slowest page by its worst case, not by its average: an average that looks
        ''' acceptable while one Find in twenty takes four seconds is the shape of complaint this
        ''' panel exists to catch.
        '''
        ''' Never labelled "response time". The stopwatch stops when the grid paints server-side,
        ''' and over Thinfinity the pixels still have to reach the browser - so this is a floor on
        ''' what the user experienced, not the thing itself.
        ''' </summary>
        Private Sub FillTiming()
            timingPanel.Controls.Clear()

            If snapshot Is Nothing OrElse snapshot.SearchTimings.Count = 0 Then
                ' Nothing to choose between. Left visible it would offer last refresh's pages
                ' against a panel saying there were none.
                timingPageCombo.Visible = False

                timingPanel.Controls.Add(New Label() With {
                    .Text = "No searches recorded in this period.",
                    .Font = New Font("Segoe UI", 9.5F, FontStyle.Italic),
                    .ForeColor = MutedColour,
                    .Location = New Point(12, 12),
                    .Size = New Size(380, 22),
                    .TextAlign = ContentAlignment.MiddleLeft
                })
                Return
            End If

            ' The page that was showing, if it is still in the window. Somebody who picked
            ' Registration and pressed Refresh should still be looking at Registration.
            Dim wanted = Convert.ToString(timingPageCombo.SelectedItem, CultureInfo.InvariantCulture)

            timingComboFilling = True
            Try
                timingPageCombo.Items.Clear()

                ' Every page together, first. It is the question somebody opening this panel asks
                ' before any other - is searching all right - and a page name at the top answered
                ' a narrower one while looking like the answer to the broad one.
                timingPageCombo.Items.Add(AllPages)

                For Each timing In snapshot.SearchTimings
                    timingPageCombo.Items.Add(timing.PageName)
                Next

                Dim index = timingPageCombo.Items.IndexOf(wanted)

                ' Falls back to the first, which the query orders slowest first. A page nobody has
                ' chosen should be the one worth looking at.
                timingPageCombo.SelectedIndex = If(index >= 0, index, 0)
            Finally
                timingComboFilling = False
            End Try

            ' Shown as soon as there is a page to compare against the everything entry.
            timingPageCombo.Visible = snapshot.SearchTimings.Count > 0

            Dim totalSearches = 0
            For Each timing In snapshot.SearchTimings
                totalSearches += timing.Searches
            Next

            DrawTiming(totalSearches)
        End Sub

        ''' <summary>Redraws the rows for whichever page is chosen, leaving the combo alone.</summary>
        Private Sub TimingPageCombo_Changed(sender As Object, e As EventArgs)
            If timingComboFilling Then Return
            If snapshot Is Nothing OrElse snapshot.SearchTimings.Count = 0 Then Return

            Dim totalSearches = 0
            For Each timing In snapshot.SearchTimings
                totalSearches += timing.Searches
            Next

            DrawTiming(totalSearches)
        End Sub

        ''' <summary>
        ''' The four figures for the chosen page: database and whole find, average and worst.
        '''
        ''' The worst case is on the panel and not only the average, because an average that looks
        ''' acceptable while one Find in twenty takes four seconds is exactly the complaint this
        ''' exists to catch.
        ''' </summary>
        Private Sub DrawTiming(totalSearches As Integer)
            timingPanel.Controls.Clear()

            Dim chosen = ChosenTiming()

            ' No verdict line. There was one - "not the database, the time is client-side" - and it
            ' came out on 2026-09-20 for two reasons. The two figures below already say which half
            ' the time is in, more precisely than a sentence can; and it passed judgement on a
            ' 729ms Find that feels instant, against a threshold picked with no data behind it.
            ' A panel that cries wolf about something nobody can feel teaches people to ignore it.
            timingPanel.Controls.Add(New Label() With {
                .Text = totalSearches.ToString("N0", CultureInfo.CurrentCulture) &
                        If(totalSearches = 1, " search", " searches"),
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = HeadingColour,
                .Location = New Point(12, 6),
                .Size = New Size(timingPanel.Width - 24, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            })

            ' Labelled once, across the top. Repeating "average" and "slowest" on every row is
            ' noise when the numbers already line up under their heading.
            AddTimingRow(If(timingPageCombo.Visible, String.Empty, chosen.PageName), "average", "slowest", 28, FontStyle.Regular, MutedColour)

            AddTimingRow("database",
                         chosen.DbAverage.ToString("0", CultureInfo.InvariantCulture) & " ms",
                         chosen.DbMax.ToString("N0", CultureInfo.CurrentCulture) & " ms",
                         46, FontStyle.Regular, HeadingColour)

            AddTimingRow("whole find",
                         chosen.PerceivedAverage.ToString("0", CultureInfo.InvariantCulture) & " ms",
                         chosen.PerceivedMax.ToString("N0", CultureInfo.CurrentCulture) & " ms",
                         64, FontStyle.Regular, HeadingColour)
        End Sub

        ''' <summary>
        ''' One row of the timing panel: a label on the left and two figures right-aligned under
        ''' their heading, so a column of numbers reads down rather than being re-labelled on every
        ''' line.
        ''' </summary>
        ''' <summary>The entry that means every page at once.</summary>
        Private Const AllPages As String = "All pages"

        ''' <summary>
        ''' The figures for whatever is chosen, including the everything entry.
        '''
        ''' The averages are weighted by how many searches each page contributed, not averaged
        ''' across pages. A page searched once at 3 seconds and a page searched a hundred times at
        ''' 50ms are not "1.5 seconds on average" to anybody who used the application.
        '''
        ''' The worst case is the worst anywhere, not an average of the worsts, because the point
        ''' of a worst case is that it happened to somebody.
        ''' </summary>
        Private Function ChosenTiming() As HealthDataAccess.SearchTiming
            Dim index = timingPageCombo.SelectedIndex

            If index > 0 AndAlso index - 1 < snapshot.SearchTimings.Count Then
                Return snapshot.SearchTimings(index - 1)
            End If

            Dim every As New HealthDataAccess.SearchTiming With {.PageName = AllPages}
            Dim dbWeighted As Double = 0
            Dim findWeighted As Double = 0

            For Each timing In snapshot.SearchTimings
                every.Searches += timing.Searches
                dbWeighted += timing.DbAverage * timing.Searches
                findWeighted += timing.PerceivedAverage * timing.Searches
                every.DbMax = Math.Max(every.DbMax, timing.DbMax)
                every.PerceivedMax = Math.Max(every.PerceivedMax, timing.PerceivedMax)
            Next

            If every.Searches > 0 Then
                every.DbAverage = dbWeighted / every.Searches
                every.PerceivedAverage = findWeighted / every.Searches
            End If

            Return every
        End Function

        Private Sub AddTimingRow(caption As String,
                                 average As String,
                                 slowest As String,
                                 top As Integer,
                                 style As FontStyle,
                                 colour As Color)
            Dim rowFont = New Font("Segoe UI", 9.0F, style)

            timingPanel.Controls.Add(New Label() With {
                .Text = caption,
                .Font = rowFont,
                .ForeColor = MutedColour,
                .Location = New Point(12, top),
                .Size = New Size(170, 18),
                .AutoEllipsis = True,
                .TextAlign = ContentAlignment.MiddleLeft
            })

            timingPanel.Controls.Add(New Label() With {
                .Text = average,
                .Font = rowFont,
                .ForeColor = colour,
                .Location = New Point(186, top),
                .Size = New Size(100, 18),
                .TextAlign = ContentAlignment.MiddleRight
            })

            timingPanel.Controls.Add(New Label() With {
                .Text = slowest,
                .Font = rowFont,
                .ForeColor = colour,
                .Location = New Point(292, top),
                .Size = New Size(110, 18),
                .TextAlign = ContentAlignment.MiddleRight
            })
        End Sub

        ''' <summary>
        ''' Bars drawn as panels rather than painted, because the widths come from data that is not
        ''' known until the query returns and a chart library is a dependency this page does not
        ''' need for six horizontal rectangles.
        ''' </summary>
        Private Sub FillActivity()
            activityPanel.Controls.Clear()

            If snapshot Is Nothing OrElse snapshot.Activity.Count = 0 Then
                activityPanel.Controls.Add(New Label() With {
                    .Text = "No recorded activity in this period.",
                    .Font = New Font("Segoe UI", 9.5F, FontStyle.Italic),
                    .ForeColor = MutedColour,
                    .Location = New Point(12, 12),
                    .Size = New Size(500, 22),
                    .TextAlign = ContentAlignment.MiddleLeft
                })
                Return
            End If

            Dim widest = 0
            For Each item In snapshot.Activity
                If item.Count > widest Then widest = item.Count
            Next
            If widest <= 0 Then widest = 1

            Dim columnWidth = 250
            Dim perRow = Math.Max(1, (activityPanel.Width - 24) \ columnWidth)
            Dim index = 0

            For Each item In snapshot.Activity
                Dim column = index Mod perRow
                Dim row = index \ perRow
                Dim left = 12 + (column * columnWidth)
                Dim top = 12 + (row * 34)

                If top + 28 > activityPanel.Height Then Exit For

                activityPanel.Controls.Add(New Label() With {
                    .Text = item.OperationType,
                    .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                    .ForeColor = HeadingColour,
                    .Location = New Point(left, top),
                    .Size = New Size(78, 20),
                    .TextAlign = ContentAlignment.MiddleLeft
                })

                Dim maxBar = columnWidth - 140
                Dim barWidth = Math.Max(3, CInt((item.Count / CDbl(widest)) * maxBar))

                activityPanel.Controls.Add(New Panel() With {
                    .Location = New Point(left + 82, top + 4),
                    .Size = New Size(barWidth, 13),
                    .BackColor = Color.FromArgb(90, 140, 210)
                })

                activityPanel.Controls.Add(New Label() With {
                    .Text = item.Count.ToString("N0", CultureInfo.CurrentCulture),
                    .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                    .ForeColor = MutedColour,
                    .Location = New Point(left + 82 + maxBar + 6, top),
                    .Size = New Size(50, 20),
                    .TextAlign = ContentAlignment.MiddleLeft
                })

                index += 1
            Next
        End Sub

        Private Sub BuildNeedsAttention()
            Dim heading As New Label() With {
                .Text = "NEEDS ATTENTION",
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = MutedColour,
                .Location = New Point(26, 518),
                .Size = New Size(250, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(heading)

            ' Sits above the grid's right edge rather than in the header. The history is the
            ' history of this list, and a button for it anywhere else would have to explain
            ' itself.
            historyButton.Text = "History"
            historyButton.Font = New Font("Segoe UI", 9.0F)
            historyButton.Location = New Point(700, 516)
            historyButton.Size = New Size(84, 24)
            historyButton.FlatStyle = FlatStyle.System
            Controls.Add(historyButton)
            AddHandler historyButton.Click, AddressOf HistoryButton_Click

            ' Narrowed to make room for failed sign-ins beside it. Faults and failed logins are
            ' both "what needs looking at", and side by side they are read in one glance rather
            ' than one scrolled past to reach the other.
            attentionGrid.Location = New Point(24, 542)
            attentionGrid.Size = New Size(760, PageHeight - 562)
            attentionGrid.AllowUserToAddRows = False
            attentionGrid.AllowUserToDeleteRows = False
            attentionGrid.AllowUserToResizeRows = False
            attentionGrid.ReadOnly = False
            attentionGrid.RowHeadersVisible = False
            attentionGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            attentionGrid.MultiSelect = False
            attentionGrid.BackgroundColor = Color.White
            attentionGrid.BorderStyle = BorderStyle.FixedSingle
            attentionGrid.Font = New Font("Segoe UI", 9.5F)
            attentionGrid.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)

            ' Nothing on this panel acts on the selected row - the buttons act on their own - so a
            ' header that lights up when a row is selected is claiming something that is not true.
            BrowseGridStandardizer.ApplyStaticHeaderStyle(attentionGrid, GridHeaderColour, HeadingColour)

            ' Selection is a pale tint rather than the system's inverted blue. Nothing on this
            ' panel acts on the selected row - the buttons act on their own row - so a full-width
            ' band of blue is loud about something that means nothing. The foreground is left to
            ' each row, so an acknowledged row stays grey and a recurred one stays red when the
            ' cursor happens to be on it.
            attentionGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 250)

            ' And a foreground to go with it. Left unset, a selected row paints in the system's
            ' selection colour - white - which over a pale tint is invisible. The fault rows set
            ' their own and were fine; the "nothing recorded" row returns before that code and
            ' vanished the moment the grid selected it, which is always.
            attentionGrid.DefaultCellStyle.SelectionForeColor = HeadingColour

            ' Widths add up to less than the grid, so the buttons sit in view rather than being
            ' pushed off the right edge by a Fault column wide enough for the longest name anybody
            ' might ever see. A long fault ellipses; the whole of it is a Detail click away.
            attentionGrid.Columns.Add(NewTextColumn("Fault", "Fault", 380))
            attentionGrid.Columns.Add(NewTextColumn("Origin", "Origin", 140))
            attentionGrid.Columns.Add(NewTextColumn("Count", "Count", 70))
            attentionGrid.Columns.Add(NewTextColumn("Age", "Last seen", 110))
            attentionGrid.Columns.Add(NewTextColumn("State", "State", 150))

            Dim detailColumn As New DataGridViewButtonColumn() With {
                .Name = "Detail",
                .HeaderText = String.Empty,
                .Text = "Detail",
                .UseColumnTextForButtonValue = True,
                .Width = 80
            }
            attentionGrid.Columns.Add(detailColumn)

            Dim ackColumn As New DataGridViewButtonColumn() With {
                .Name = "Ack",
                .HeaderText = String.Empty,
                .Text = "Ack",
                .UseColumnTextForButtonValue = True,
                .Width = 70
            }
            attentionGrid.Columns.Add(ackColumn)

            Dim fixedColumn As New DataGridViewButtonColumn() With {
                .Name = "Fixed",
                .HeaderText = String.Empty,
                .Text = "Fixed",
                .UseColumnTextForButtonValue = True,
                .Width = 75
            }
            attentionGrid.Columns.Add(fixedColumn)

            Dim idColumn = NewTextColumn("ErrorLogID", "ErrorLogID", 60)
            idColumn.Visible = False
            attentionGrid.Columns.Add(idColumn)

            For Each column As DataGridViewColumn In attentionGrid.Columns
                If Not TypeOf column Is DataGridViewButtonColumn Then column.ReadOnly = True
            Next

            AddHandler attentionGrid.CellContentClick, AddressOf AttentionGrid_CellContentClick

            ' Double-click opens the detail, which is what the Detail button does. One owner: the
            ' guardrail's rule is that a double-click invokes the visible command rather than
            ' growing a second path to the same place.
            AddHandler attentionGrid.CellDoubleClick, AddressOf AttentionGrid_CellDoubleClick

            Controls.Add(attentionGrid)

            Dim loginHeading As New Label() With {
                .Text = "FAILED SIGN-INS",
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = MutedColour,
                .Location = New Point(802, 518),
                .Size = New Size(260, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Controls.Add(loginHeading)

            loginGrid.Location = New Point(800, 542)
            loginGrid.Size = New Size(PageWidth - 824, PageHeight - 562)
            loginGrid.AllowUserToAddRows = False
            loginGrid.AllowUserToDeleteRows = False
            loginGrid.AllowUserToResizeRows = False
            loginGrid.ReadOnly = True
            loginGrid.RowHeadersVisible = False
            loginGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            loginGrid.MultiSelect = False
            loginGrid.BackgroundColor = Color.White
            loginGrid.BorderStyle = BorderStyle.FixedSingle
            loginGrid.Font = New Font("Segoe UI", 9.5F)
            loginGrid.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
            BrowseGridStandardizer.ApplyStaticHeaderStyle(loginGrid, GridHeaderColour, HeadingColour)
            loginGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 250)
            loginGrid.DefaultCellStyle.SelectionForeColor = HeadingColour

            loginGrid.Columns.Add(NewTextColumn("Name", "Name tried", 150))
            loginGrid.Columns.Add(NewTextColumn("Reason", "Reason", 110))
            loginGrid.Columns.Add(NewTextColumn("Attempts", "Tries", 55))
            loginGrid.Columns.Add(NewTextColumn("LoginAge", "Last", 80))

            Controls.Add(loginGrid)
        End Sub

        ''' <summary>
        ''' Failed sign-ins, grouped by name and reason, worst first.
        '''
        ''' Three tries against one name is the trigger the spec names, so the count is what the eye
        ''' should land on. A name that does not exist is coloured: somebody mistyping their own
        ''' password is routine, a run of names that were never real is somebody trying names.
        ''' </summary>
        Private Sub FillLoginFailures()
            loginGrid.Rows.Clear()

            If snapshot Is Nothing OrElse snapshot.LoginFailures.Count = 0 Then
                Dim emptyIndex = loginGrid.Rows.Add()
                loginGrid.Rows(emptyIndex).Cells("Name").Value = "None in this period."
                loginGrid.Rows(emptyIndex).DefaultCellStyle.ForeColor = HeadingColour
                loginGrid.Rows(emptyIndex).DefaultCellStyle.SelectionForeColor = HeadingColour
                loginGrid.Rows(emptyIndex).DefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Italic)
                ClearGridSelection(loginGrid)
                Return
            End If

            For Each failure In snapshot.LoginFailures
                Dim index = loginGrid.Rows.Add()
                Dim row = loginGrid.Rows(index)

                row.Cells("Name").Value = failure.AttemptedUserName
                row.Cells("Reason").Value = FriendlyLoginReason(failure.Reason)
                row.Cells("Attempts").Value = failure.Attempts.ToString("N0", CultureInfo.CurrentCulture)
                row.Cells("LoginAge").Value = AgeOf(failure.LastAttempt)

                ' Black, whatever the reason. A name that does not exist and a name tried four
                ' times were red and amber, and the Reason and Tries columns already say both in
                ' words - the colour was the same fact a second time, in the one form somebody
                ' cannot read aloud, cannot search for, and may not be able to distinguish.
                row.DefaultCellStyle.ForeColor = HeadingColour
                row.DefaultCellStyle.SelectionForeColor = HeadingColour
            Next

            ClearGridSelection(loginGrid)
        End Sub

        ''' <summary>The stored reason as somebody would say it.</summary>
        Private Shared Function FriendlyLoginReason(reason As String) As String
            Select Case If(reason, String.Empty).ToUpperInvariant()
                Case "UNKNOWNUSER" : Return "No such name"
                Case "WRONGPASSWORD" : Return "Wrong password"
                Case "INACTIVE" : Return "Not active"
                Case "NOPASSWORD" : Return "No password set"
                Case "DATABASEDOWN" : Return "Database down"
                Case Else : Return reason
            End Select
        End Function

        ''' <summary>"2h ago", "3d ago" - the same wording the fault list uses.</summary>
        Private Shared Function AgeOf(value As Date) As String
            Dim span = Date.UtcNow - value
            If span.TotalMinutes < 1 Then Return "just now"
            If span.TotalMinutes < 60 Then Return CInt(span.TotalMinutes).ToString(CultureInfo.InvariantCulture) & "m ago"
            If span.TotalHours < 24 Then Return CInt(span.TotalHours).ToString(CultureInfo.InvariantCulture) & "h ago"
            Return CInt(span.TotalDays).ToString(CultureInfo.InvariantCulture) & "d ago"
        End Function

        Private Shared Function NewTextColumn(name As String, header As String, width As Integer) As DataGridViewTextBoxColumn
            Return New DataGridViewTextBoxColumn() With {
                .Name = name,
                .HeaderText = header,
                .Width = width,
                .SortMode = DataGridViewColumnSortMode.NotSortable
            }
        End Function

        ''' <summary>
        ''' The only write this page performs, and the guardrails want a destructive-ish action
        ''' identified before it happens. Acknowledging is not destructive - the fault row survives
        ''' - but it does remove the fault from the score, so it names the fault and asks.
        ''' </summary>
        Private Sub AttentionGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
            OpenFaultDetail(FaultIdOnRow(e.RowIndex))
        End Sub

        ''' <summary>
        ''' Opens the full fault, so it can be read and handed to whoever will fix it.
        '''
        ''' The list carries a headline and a count; this is where the message and the stack trace
        ''' are, and they are fetched only when somebody asks - see HealthDataAccess.GetFaultDetail.
        ''' </summary>
        Private Sub OpenFaultDetail(errorLogId As Integer)
            If errorLogId <= 0 Then Return

            Cursor = Cursors.WaitCursor
            Dim detail As HealthDataAccess.FaultDetail

            Try
                detail = HealthDataAccess.GetFaultDetail(errorLogId)
            Finally
                Cursor = Cursors.Default
            End Try

            Using window As New FW_FaultDetail(detail)
                window.ShowDialog(Me)
            End Using
        End Sub

        ''' <summary>
        ''' Records that a fault has been fixed, and what the fix was.
        '''
        ''' It asks for the text rather than offering to skip it. A resolution with no note is a
        ''' tick that tells the next person nothing, and the next person is usually the same person
        ''' three months later. A commit hash is the most useful thing to put there.
        ''' </summary>
        Private Sub MarkFaultFixed(rowIndex As Integer)
            Dim errorLogId = FaultIdOnRow(rowIndex)
            If errorLogId <= 0 Then Return

            Dim row = attentionGrid.Rows(rowIndex)
            Dim headline = Convert.ToString(row.Cells("Fault").Value, CultureInfo.InvariantCulture)

            Using prompt As New FW_FaultResolution(headline)
                If prompt.ShowDialog(Me) <> DialogResult.OK Then Return

                If HealthDataAccess.Resolve(errorLogId, currentUser.UserId, prompt.Resolution) Then
                    LoadSnapshot()
                Else
                    MessageBox.Show(Me, "THE FAULT COULD NOT BE MARKED AS FIXED.", "FIXED",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
            End Using
        End Sub

        ''' <summary>
        ''' What state a fault is in, said in a few words.
        '''
        ''' "Fixed - RECURRED" is the one worth reading. It is checked first because it outranks
        ''' everything else: a fault that came back after a fix is not merely open, it is evidence
        ''' that a fix did not hold.
        ''' </summary>
        Private Shared Function StateOf(fault As HealthDataAccess.FaultLine) As String
            If fault.RecurredAfterResolved AndAlso Not fault.Resolved Then Return "RECURRED after fix"
            If fault.Resolved Then Return "Fixed"
            If fault.Acknowledged Then Return "Acknowledged"
            Return "Open"
        End Function

        ''' <summary>The fault on a row, or zero for the placeholder row that says there are none.</summary>
        Private Function FaultIdOnRow(rowIndex As Integer) As Integer
            If rowIndex < 0 OrElse rowIndex >= attentionGrid.Rows.Count Then Return 0

            Dim idText = Convert.ToString(attentionGrid.Rows(rowIndex).Cells("ErrorLogID").Value,
                                          CultureInfo.InvariantCulture)

            Dim errorLogId As Integer
            If Not Integer.TryParse(idText, errorLogId) Then Return 0

            Return errorLogId
        End Function

        Private Sub AttentionGrid_CellContentClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return

            Dim columnName = attentionGrid.Columns(e.ColumnIndex).Name

            If columnName = "Detail" Then
                OpenFaultDetail(FaultIdOnRow(e.RowIndex))
                Return
            End If

            If columnName = "Fixed" Then
                MarkFaultFixed(e.RowIndex)
                Return
            End If

            If columnName <> "Ack" Then Return

            Dim row = attentionGrid.Rows(e.RowIndex)
            Dim errorLogId = FaultIdOnRow(e.RowIndex)
            If errorLogId <= 0 Then Return

            If Convert.ToString(row.Cells("State").Value, CultureInfo.InvariantCulture) = "Acknowledged" Then
                MessageBox.Show(Me, "THIS FAULT HAS ALREADY BEEN ACKNOWLEDGED.", "ACKNOWLEDGE",
                                MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim headline = Convert.ToString(row.Cells("Fault").Value, CultureInfo.InvariantCulture)

            Dim confirm = MessageBox.Show(Me,
                                          "ACKNOWLEDGE THIS FAULT?" & Environment.NewLine & Environment.NewLine &
                                          headline & Environment.NewLine & Environment.NewLine &
                                          "IT STAYS IN THE LOG, BUT STOPS COUNTING AGAINST THE HEALTH SCORE.",
                                          "ACKNOWLEDGE FAULT",
                                          MessageBoxButtons.YesNo,
                                          MessageBoxIcon.Question)

            If confirm <> DialogResult.Yes Then Return

            If HealthDataAccess.Acknowledge(errorLogId, currentUser.UserId) Then
                LoadSnapshot()
            Else
                MessageBox.Show(Me, "THE FAULT COULD NOT BE ACKNOWLEDGED.", "ACKNOWLEDGE",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
        End Sub

        ''' <summary>One row per registration that exists, with "All registrations" first.</summary>
        Private NotInheritable Class RegistrationOption
            Public Property ID As Integer
            Public Property Name As String = String.Empty

            Public Overrides Function ToString() As String
                Return Name
            End Function
        End Class

        ''' <summary>
        ''' Fills the scope list from a snapshot, once.
        '''
        ''' Refilled on every load would be wasteful and worse than wasteful: replacing the items
        ''' fires SelectedIndexChanged, which would call LoadSnapshot again, from inside
        ''' LoadSnapshot. So it runs only while the list holds nothing but "All registrations".
        '''
        ''' A snapshot that carries none leaves the list as it is, which is the page's normal
        ''' state - a scope selector that could not be read should not stop the page reporting.
        ''' </summary>
        Private Sub FillRegistrations(loaded As HealthDataAccess.HealthSnapshot)
            If registrationCombo.Items.Count > 1 Then Return
            If loaded Is Nothing OrElse loaded.Registrations.Count = 0 Then Return

            RemoveHandler registrationCombo.SelectedIndexChanged, AddressOf RegistrationCombo_Changed

            Try
                For Each registration In loaded.Registrations
                    registrationCombo.Items.Add(New RegistrationOption With {
                        .ID = registration.ID,
                        .Name = registration.Name
                    })
                Next
            Finally
                AddHandler registrationCombo.SelectedIndexChanged, AddressOf RegistrationCombo_Changed
            End Try
        End Sub

        Private Sub RegistrationCombo_Changed(sender As Object, e As EventArgs)
            LoadSnapshot()
        End Sub

        ''' <summary>
        ''' Says whether SQL Server is keeping a record of its own queries.
        '''
        ''' Amber when off rather than red: nothing is broken, but nothing is being kept either,
        ''' and the distinction matters. Plain grey when on, because a working flight recorder is
        ''' not news.
        ''' </summary>
        Private Sub ShowQueryStoreState()
            ShowQueryStoreState(If(snapshot Is Nothing, String.Empty, snapshot.QueryStoreState))
        End Sub

        ''' <summary>
        ''' Shows a Query Store state, whether it came from the snapshot or from having just
        ''' changed it. Taking the state as an argument is what lets the checkbox report what the
        ''' server says afterwards rather than what the click assumed.
        ''' </summary>
        Private Sub ShowQueryStoreState(state As String)
            Dim known = If(state, String.Empty).ToUpperInvariant()

            Select Case known
                Case "READ_WRITE"
                    queryStoreCheck.Visible = True
                    queryStoreCheck.Checked = True
                    queryStoreLabel.Text = "recording"
                    queryStoreLabel.ForeColor = MutedColour

                Case "READ_ONLY"
                    ' On, and taking nothing new - almost always because it filled its quota. The
                    ' tick is off because nothing is being kept, which is what the tick claims.
                    ' Ticking it names READ_WRITE and puts it back.
                    queryStoreCheck.Visible = True
                    queryStoreCheck.Checked = False
                    queryStoreLabel.Text = "read only - it has stopped recording"
                    queryStoreLabel.ForeColor = Color.FromArgb(232, 160, 25)

                Case "OFF", "ERROR"
                    queryStoreCheck.Visible = True
                    queryStoreCheck.Checked = False
                    queryStoreLabel.Text = "off - no query history is being kept"
                    queryStoreLabel.ForeColor = Color.FromArgb(232, 160, 25)

                Case Else
                    ' An older SQL Server, or no permission to read the view. Not worth shouting
                    ' about, and not worth claiming either way - and certainly not worth offering
                    ' a tick that cannot be honoured.
                    queryStoreCheck.Visible = False
                    queryStoreLabel.Text = String.Empty
            End Select
        End Sub

        ''' <summary>
        ''' Turns Query Store on or off.
        '''
        ''' **Off is confirmed and on is not.** Turning it off throws away every query, plan and
        ''' timing it has collected, and nothing archives them first. Turning it on costs storage
        ''' and loses nothing, which is not worth a dialog.
        '''
        ''' The tick is set from what the server reports afterwards, never from what the click
        ''' asked for. A refused change - no ALTER DATABASE permission, most likely - must leave
        ''' the tick showing the truth rather than the intention.
        ''' </summary>
        Private Sub QueryStoreCheck_Click(sender As Object, e As EventArgs)
            Dim turningOn = queryStoreCheck.Checked

            If Not turningOn Then
                Dim confirmed = MessageBox.Show(
                    Me,
                    "TURNING QUERY STORE OFF DISCARDS EVERY QUERY, PLAN AND TIMING IT HAS COLLECTED." & vbCrLf & vbCrLf &
                    "NOTHING KEEPS A COPY. TURNING IT BACK ON STARTS FROM EMPTY." & vbCrLf & vbCrLf &
                    "TURN IT OFF?",
                    "Turn Query Store Off",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2)

                If confirmed <> DialogResult.Yes Then
                    ' Put the tick back where it was. Nothing was changed.
                    ShowQueryStoreState()
                    Return
                End If
            End If

            Dim result As HealthDataAccess.QueryStoreResult
            queryStoreCheck.Enabled = False
            Cursor = Cursors.WaitCursor
            Try
                result = HealthDataAccess.SetQueryStore(turningOn)
            Finally
                Cursor = Cursors.Default
                queryStoreCheck.Enabled = True
            End Try

            ' The state the server reports, not the one the click asked for.
            If Not String.IsNullOrWhiteSpace(result.State) Then
                ShowQueryStoreState(result.State)
                If snapshot IsNot Nothing Then snapshot.QueryStoreState = result.State
            Else
                ShowQueryStoreState()
            End If

            If Not result.Succeeded Then
                MessageBox.Show(Me, result.Message.ToUpperInvariant(),
                                "Query Store Unchanged", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
        End Sub

        Private Function SelectedRegistrationId() As Integer
            Dim selected = TryCast(registrationCombo.SelectedItem, RegistrationOption)
            If selected Is Nothing Then Return 0
            Return selected.ID
        End Function

        Private Function SelectedWindowDays() As Integer
            Select Case periodCombo.SelectedIndex
                Case 0 : Return 1
                Case 1 : Return 7
                Case 2 : Return 30
                Case Else : Return 90
            End Select
        End Function

        Private Sub LoadSnapshot()
            Cursor = Cursors.WaitCursor

            Try
                snapshot = HealthDataAccess.GetSnapshot(SelectedWindowDays(), SelectedRegistrationId())

                If snapshot.Failed Then
                    gauge.ClearScore()
                    asAtLabel.Text = "could not read"

                    ' Blanked rather than left holding the previous count. A number from the last
                    ' successful refresh beside the words "could not read" is the worst of both.
                    connectedLabel.Text = String.Empty
                    connectedLabel.Links.Clear()
                    connectedGrid.Rows.Clear()
                    SetTile(savesTile, "--", "The health data could not be read.", MutedColour)
                    SetTile(faultsTile, "--", String.Empty, MutedColour)
                    SetTile(fallbacksTile, "--", String.Empty, MutedColour)
                    attentionGrid.Rows.Clear()
                    loginGrid.Rows.Clear()
                    activityPanel.Controls.Clear()
                    timingPanel.Controls.Clear()
                    breakdownLabel.Text = String.Empty
                    Return
                End If

                ' "as at", never "now". The figures are as old as the last refresh, and a page that
                ' implies otherwise is claiming something it cannot know.
                asAtLabel.Text = "as at " & snapshot.TakenAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)

                ShowConnected()
                FillConnected()

                ShowQueryStoreState()

                If snapshot.HasData Then
                    gauge.Score = snapshot.Score
                Else
                    gauge.ClearScore()
                End If

                ShowBreakdown()

                FillRegistrations(snapshot)
                FillTiles()
                FillActivity()
                FillTiming()
                FillNeedsAttention()
                FillLoginFailures()

            Finally
                Cursor = Cursors.Default
            End Try
        End Sub

        ''' <summary>
        ''' Says what the needle is made of, and which parts of it can be recovered.
        '''
        ''' Two lines. The first is the arithmetic, itemised in the same order the spec lists the
        ''' inputs. The second exists because the first invites a question it cannot answer on its
        ''' own - somebody who has just fixed everything and still sees 99.3 needs to be told that
        ''' saves and fallbacks come back with time rather than with work.
        '''
        ''' Each figure is read from the snapshot's own penalty properties rather than recomputed
        ''' here. A breakdown that does not add up to the needle above it would be worse than none.
        ''' </summary>
        Private Sub ShowBreakdown()
            If snapshot Is Nothing OrElse Not snapshot.HasData Then
                breakdownLabel.Text = String.Empty
                Return
            End If

            Dim saves = snapshot.SavePenalty
            Dim faults = snapshot.FaultPenalty
            Dim fallbacks = snapshot.FallbackPenalty
            Dim total = saves + faults + fallbacks

            If total < 0.05 Then
                breakdownLabel.Text = "Nothing is costing the score."
                Return
            End If

            Dim parts As New List(Of String)()
            parts.Add(Penalty(saves) & " saves")
            parts.Add(Penalty(faults) & " faults")
            parts.Add(Penalty(fallbacks) & " fallbacks")

            breakdownLabel.Text = String.Join("   ", parts) & vbCrLf &
                                  "Faults clear when they are fixed. Saves and fallbacks age out of the window."
        End Sub

        ''' <summary>
        ''' A penalty as it should be read: a minus sign only where something was actually lost.
        ''' "-0.0" against an input costing nothing reads as a rounded-away problem rather than as
        ''' no problem.
        ''' </summary>
        Private Shared Function Penalty(value As Double) As String
            If value < 0.05 Then Return "0.0"
            Return "-" & value.ToString("0.0", CultureInfo.CurrentCulture)
        End Function

        ''' <summary>
        ''' Opens the history of what has been dealt with, over the same window and scope the page
        ''' is showing. Reading the history of a different period from the number above it would be
        ''' the kind of quiet mismatch nobody notices for months.
        ''' </summary>
        ''' <summary>
        ''' The connected count, and whether it is a link.
        '''
        ''' The whole text is the link, so the target is the number somebody is already looking at
        ''' rather than a separate word beside it.
        '''
        ''' It only ever reads zero if the snapshot failed or the registration filter excludes
        ''' everybody - reading this page makes you one of the rows - and in that case the text
        ''' stays plain, because a link to an empty window is a promise the window cannot keep.
        '''
        ''' CONNECTIONS, NOT PEOPLE. One person signed in on two machines is two rows and two
        ''' licences, and "2 people connected" would be false. Counting distinct users instead
        ''' would make the number on the page disagree with the rows behind it, which is the one
        ''' fault this design avoids everywhere else.
        ''' </summary>
        Private Sub ShowConnected()
            Dim count = snapshot.ConnectedSessions.Count

            connectedLabel.Links.Clear()

            If count = 0 Then
                connectedLabel.Text = "nothing connected"
                Return
            End If

            connectedLabel.Text = count.ToString("N0", CultureInfo.CurrentCulture) &
                                  If(count = 1, " connection", " connections")
            connectedLabel.Links.Add(0, connectedLabel.Text.Length)
        End Sub

        ''' <summary>
        ''' The same rows the count came from, so the grid cannot disagree with the number above
        ''' it - and, like every other figure on this page, cut by the registration combo.
        '''
        ''' SINCE IS A CLOCK TIME, and a date for anything that did not start today. A session from
        ''' yesterday showing only "21:31" reads as one from this morning, which is the kind of
        ''' quiet wrongness nobody checks.
        ''' </summary>
        Private Sub FillConnected()
            connectedGrid.Rows.Clear()

            Dim today = Date.Now.Date

            For Each item In snapshot.ConnectedSessions
                Dim startedLocal = item.StartedOn.ToLocalTime()

                Dim since = If(startedLocal.Date = today,
                               startedLocal.ToString("HH:mm", CultureInfo.CurrentCulture),
                               startedLocal.ToString("d MMM", CultureInfo.CurrentCulture))

                Dim index = connectedGrid.Rows.Add(item.WhoName, since)
                Dim row = connectedGrid.Rows(index)

                ' The whole story on hover, because 140 pixels of name is all there is room for.
                row.Cells("Who").ToolTipText = item.WhoName &
                                               If(item.SessionKind = String.Empty, String.Empty, " - " & item.SessionKind) &
                                               If(item.RegistrationName = String.Empty, String.Empty, " - " & item.RegistrationName)

                ' Italic for a session that has been idle long enough to have ended without
                ' saying so. Same marking as the dialog, so the two never disagree about which
                ' rows are doubtful.
                If item.LooksAbandoned Then
                    row.DefaultCellStyle.Font = New Font("Segoe UI", 9.0F, FontStyle.Italic)
                End If
            Next

            ' Nobody chose a row. A grid selects its first the moment it fills, and the tint that
            ' follows reads as a choice somebody made.
            connectedGrid.ClearSelection()
            connectedGrid.CurrentCell = Nothing
        End Sub

        ''' <summary>
        ''' Opens the list the count was taken from - the page's own rows, not a fresh read, so
        ''' the window cannot contradict the number that was clicked.
        ''' </summary>
        Private Sub ConnectedLabel_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
            If snapshot Is Nothing OrElse snapshot.ConnectedSessions.Count = 0 Then Return

            Using connected As New FW_ConnectedUsers(snapshot.ConnectedSessions, snapshot.TakenAtUtc)
                connected.ShowDialog(Me)
            End Using
        End Sub

        Private Sub HistoryButton_Click(sender As Object, e As EventArgs)
            Using history As New FW_FixHistory(SelectedWindowDays(), SelectedRegistrationId())
                history.ShowDialog(Me)
            End Using
        End Sub

        Private Sub FillTiles()
            If snapshot.SaveTotal > 0 Then
                SetTile(savesTile,
                        snapshot.SaveTotal.ToString("N0", CultureInfo.CurrentCulture),
                        "succeeded " & (snapshot.SaveSuccessRate * 100).ToString("0.0", CultureInfo.InvariantCulture) & "%",
                        If(snapshot.SaveSuccessRate >= 0.99, Color.FromArgb(35, 160, 85), Color.FromArgb(200, 55, 50)))
            Else
                SetTile(savesTile, "0", "No saves recorded in this period.", MutedColour)
            End If

            SetTile(faultsTile,
                    snapshot.FaultsTotal.ToString("N0", CultureInfo.CurrentCulture),
                    "unacknowledged " & snapshot.FaultsUnacknowledged.ToString("N0", CultureInfo.CurrentCulture),
                    If(snapshot.FaultsUnacknowledged > 0, Color.FromArgb(200, 55, 50), Color.FromArgb(35, 160, 85)))

            ' The number is the degraded ones only, because that is what the score counts and a
            ' tile disagreeing with the needle beside it is worse than no tile. Pages that have
            ' simply never been configured are said after it, in words, because they are worth
            ' knowing about and are not ill health.
            Dim unconfigured = If(snapshot.FallbackUnconfigured > 0,
                                  ", " & snapshot.FallbackUnconfigured.ToString("N0", CultureInfo.CurrentCulture) &
                                  " unconfigured page" & If(snapshot.FallbackUnconfigured = 1, "", "s"),
                                  String.Empty)

            If snapshot.AuditedOperations > 0 Then
                SetTile(fallbacksTile,
                        snapshot.FallbackCount.ToString("N0", CultureInfo.CurrentCulture),
                        "per 100 operations " & snapshot.FallbackRatePer100.ToString("0.0", CultureInfo.InvariantCulture) & unconfigured,
                        If(snapshot.FallbackRatePer100 > 5, Color.FromArgb(232, 160, 25), HeadingColour))
            Else
                SetTile(fallbacksTile,
                        snapshot.FallbackCount.ToString("N0", CultureInfo.CurrentCulture),
                        "no audited operations to compare against" & unconfigured,
                        MutedColour)
            End If
        End Sub

        Private Sub FillNeedsAttention()
            attentionGrid.Rows.Clear()

            ' Cells are set by name rather than positionally. A positional Rows.Add silently put the
            ' fault's id into the Detail button's cell the moment a column was inserted before the
            ' hidden one, and nothing about that reads as wrong until a button does nothing.
            If snapshot.NeedsAttention.Count = 0 Then
                ' An empty panel is the correct reading on a database where nothing has gone wrong,
                ' not a page that failed to load - so it says which.
                Dim index = attentionGrid.Rows.Add()
                Dim row = attentionGrid.Rows(index)

                row.Cells("Fault").Value = "Nothing has been recorded in this period."
                row.Cells("ErrorLogID").Value = "0"
                ' Black and italic. The grey said "this is a note, not a fault" and said it by
                ' being hard to read, which is a poor way to say anything. The italic carries it
                ' on its own.
                row.DefaultCellStyle.ForeColor = HeadingColour
                row.DefaultCellStyle.SelectionForeColor = HeadingColour
                row.DefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Italic)
                ClearGridSelection(attentionGrid)
                Return
            End If

            For Each fault In snapshot.NeedsAttention
                Dim index = attentionGrid.Rows.Add()
                Dim row = attentionGrid.Rows(index)

                row.Cells("Fault").Value = fault.Headline
                row.Cells("Origin").Value = fault.Origin
                row.Cells("Count").Value = fault.OccurrenceCount.ToString("N0", CultureInfo.CurrentCulture)
                row.Cells("Age").Value = fault.Age
                row.Cells("State").Value = StateOf(fault)
                row.Cells("ErrorLogID").Value = fault.ErrorLogID.ToString(CultureInfo.InvariantCulture)

                ' Every row black, selected or not. A recurred fault was red and an acknowledged
                ' one grey, and the State column says "RECURRED after fix" and "Acknowledged" in
                ' as many words - the colour repeated what the text already said, in the one form
                ' that cannot be read aloud, cannot be searched for and is not available to
                ' everybody who has to read this page.
                '
                ' A recurred fault keeps its bold. Weight is not colour: it survives a screenshot
                ' in grey, a colour-blind reader and a printer, and a fix that has already failed
                ' once is worth the emphasis.
                row.DefaultCellStyle.ForeColor = HeadingColour
                row.DefaultCellStyle.SelectionForeColor = HeadingColour

                If fault.RecurredAfterResolved AndAlso Not fault.Resolved Then
                    row.DefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
                End If
            Next

            ClearGridSelection(attentionGrid)
        End Sub

        ''' <summary>
        ''' Leaves a freshly filled grid with nothing selected.
        '''
        ''' A DataGridView selects its first row the moment rows arrive, and the tint that follows
        ''' reads as a choice somebody made. On these panels nobody chose anything - the buttons
        ''' act on their own row - so the page opened claiming a selection that was never made.
        '''
        ''' CurrentCell as well as the selection: clearing one leaves the other's focus rectangle
        ''' sitting on the first cell, which is the same false claim in a thinner line.
        ''' </summary>
        Private Shared Sub ClearGridSelection(grid As DataGridView)
            If grid Is Nothing OrElse grid.Rows.Count = 0 Then Return

            grid.ClearSelection()
            grid.CurrentCell = Nothing
        End Sub

        Private Shared Function BuildCurrentUserFromSession() As UserContext
            If SessionState.IsActive Then
                Dim session = SessionState.Current.Value
                Return New UserContext With {
                    .UserId = session.UserID,
                    .FirstName = session.FirstName,
                    .LastName = session.LastName
                }
            End If

            Return New UserContext()
        End Function

    End Class
End Namespace
