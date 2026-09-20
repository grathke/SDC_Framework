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
        Private ReadOnly periodCombo As ComboBox
        Private ReadOnly registrationCombo As ComboBox
        Private ReadOnly refreshButton As Button
        Private ReadOnly closeButton As Button
        Private ReadOnly asAtLabel As Label
        Private ReadOnly queryStoreLabel As Label

        Private ReadOnly savesTile As Panel
        Private ReadOnly faultsTile As Panel
        Private ReadOnly fallbacksTile As Panel

        Private ReadOnly activityPanel As Panel
        Private ReadOnly timingPanel As Panel
        Private ReadOnly attentionGrid As DataGridView

        Private snapshot As HealthDataAccess.HealthSnapshot

        Private Const PageWidth As Integer = 1180
        Private Const PageHeight As Integer = 800

        Private Shared ReadOnly HeadingColour As Color = Color.FromArgb(45, 48, 52)
        Private Shared ReadOnly MutedColour As Color = Color.FromArgb(110, 118, 126)

        ''' <summary>The scope entry that means no filter, which is how the page opens.</summary>
        Private Shared ReadOnly EveryRegistration As New RegistrationOption With {.ID = 0, .Name = "All registrations"}
        Private Shared ReadOnly PanelBorder As Color = Color.FromArgb(222, 226, 230)

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
            periodCombo = New ComboBox()
            registrationCombo = New ComboBox()
            refreshButton = New Button()
            closeButton = New Button()
            asAtLabel = New Label()
            queryStoreLabel = New Label()
            savesTile = New Panel()
            faultsTile = New Panel()
            fallbacksTile = New Panel()
            activityPanel = New Panel()
            timingPanel = New Panel()
            attentionGrid = New DataGridView()

            BuildHeader()
            BuildGaugeAndTiles()
            BuildActivity()
            BuildNeedsAttention()

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
            queryStoreLabel.Text = String.Empty
            queryStoreLabel.Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)
            queryStoreLabel.ForeColor = MutedColour
            queryStoreLabel.Location = New Point(26, 50)
            queryStoreLabel.Size = New Size(420, 20)
            queryStoreLabel.TextAlign = ContentAlignment.MiddleLeft
            Controls.Add(queryStoreLabel)

            periodCombo.DropDownStyle = ComboBoxStyle.DropDownList
            periodCombo.Font = New Font("Segoe UI", 10.0F)
            periodCombo.Location = New Point(PageWidth - 430, 22)
            periodCombo.Size = New Size(160, 28)
            periodCombo.Items.AddRange(New Object() {"Last 24 hours", "Last 7 days", "Last 30 days", "Last 90 days"})
            periodCombo.SelectedIndex = 1

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
            gauge.Location = New Point(60, 90)
            gauge.Size = New Size(420, 270)
            Controls.Add(gauge)

            StyleTile(savesTile, "SAVES", 560, 95)
            StyleTile(faultsTile, "FAULTS", 560, 190)
            StyleTile(fallbacksTile, "FALLBACKS", 560, 285)
        End Sub

        Private Sub StyleTile(tile As Panel, heading As String, left As Integer, top As Integer)
            tile.Location = New Point(left, top)
            tile.Size = New Size(540, 80)
            tile.BackColor = Color.FromArgb(249, 250, 251)
            tile.BorderStyle = BorderStyle.FixedSingle

            Dim headingLabel As New Label() With {
                .Name = "Heading",
                .Text = heading,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = MutedColour,
                .Location = New Point(14, 10),
                .Size = New Size(200, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            tile.Controls.Add(headingLabel)

            Dim valueLabel As New Label() With {
                .Name = "Value",
                .Text = "--",
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Bold),
                .ForeColor = HeadingColour,
                .Location = New Point(320, 8),
                .Size = New Size(200, 38),
                .TextAlign = ContentAlignment.MiddleRight
            }
            tile.Controls.Add(valueLabel)

            Dim detailLabel As New Label() With {
                .Name = "Detail",
                .Text = String.Empty,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                .ForeColor = MutedColour,
                .Location = New Point(14, 48),
                .Size = New Size(506, 20),
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

            Dim worst = snapshot.SearchTimings(0)
            Dim totalSearches = 0
            For Each timing In snapshot.SearchTimings
                totalSearches += timing.Searches
            Next

            timingPanel.Controls.Add(New Label() With {
                .Text = totalSearches.ToString("N0", CultureInfo.CurrentCulture) & " searches   |   slowest: " & worst.PageName,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Bold),
                .ForeColor = HeadingColour,
                .Location = New Point(12, 8),
                .Size = New Size(timingPanel.Width - 24, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            })

            timingPanel.Controls.Add(New Label() With {
                .Text = "database " & worst.DbAverage.ToString("0", CultureInfo.InvariantCulture) &
                        "ms avg, " & worst.DbMax.ToString("N0", CultureInfo.CurrentCulture) & "ms worst",
                .Font = New Font("Segoe UI", 9.0F),
                .ForeColor = MutedColour,
                .Location = New Point(12, 32),
                .Size = New Size(timingPanel.Width - 24, 18),
                .TextAlign = ContentAlignment.MiddleLeft
            })

            timingPanel.Controls.Add(New Label() With {
                .Text = "whole find " & worst.PerceivedAverage.ToString("0", CultureInfo.InvariantCulture) &
                        "ms avg, " & worst.PerceivedMax.ToString("N0", CultureInfo.CurrentCulture) & "ms worst",
                .Font = New Font("Segoe UI", 9.0F),
                .ForeColor = MutedColour,
                .Location = New Point(12, 50),
                .Size = New Size(timingPanel.Width - 24, 18),
                .TextAlign = ContentAlignment.MiddleLeft
            })

            ' The diagnosis, which is the part worth reading. "Slow" is not actionable; "not the
            ' database" is, because it says which half to go and look at.
            Dim diagnosis = worst.Diagnosis

            timingPanel.Controls.Add(New Label() With {
                .Text = diagnosis,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
                .ForeColor = If(diagnosis = "fine", Color.FromArgb(35, 160, 85), Color.FromArgb(232, 160, 25)),
                .Location = New Point(12, 68),
                .Size = New Size(timingPanel.Width - 24, 18),
                .TextAlign = ContentAlignment.MiddleLeft
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

            attentionGrid.Location = New Point(24, 542)
            attentionGrid.Size = New Size(PageWidth - 48, PageHeight - 562)
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
            attentionGrid.EnableHeadersVisualStyles = False
            attentionGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 246, 248)

            ' Selection is a pale tint rather than the system's inverted blue. Nothing on this
            ' panel acts on the selected row - the buttons act on their own row - so a full-width
            ' band of blue is loud about something that means nothing. The foreground is left to
            ' each row, so an acknowledged row stays grey and a recurred one stays red when the
            ' cursor happens to be on it.
            attentionGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 250)

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
        End Sub

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
            Dim state = If(snapshot Is Nothing, String.Empty, snapshot.QueryStoreState)

            Select Case state.ToUpperInvariant()
                Case "READ_WRITE"
                    queryStoreLabel.Text = "Query Store: recording"
                    queryStoreLabel.ForeColor = MutedColour

                Case "READ_ONLY"
                    ' It has stopped taking new data - almost always because it filled its quota.
                    queryStoreLabel.Text = "Query Store: read only - it has stopped recording"
                    queryStoreLabel.ForeColor = Color.FromArgb(232, 160, 25)

                Case "OFF", "ERROR"
                    queryStoreLabel.Text = "Query Store: off - no query history is being kept"
                    queryStoreLabel.ForeColor = Color.FromArgb(232, 160, 25)

                Case Else
                    ' An older SQL Server, or no permission to read the view. Not worth shouting
                    ' about, and not worth claiming either way.
                    queryStoreLabel.Text = String.Empty
            End Select
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
                    SetTile(savesTile, "--", "The health data could not be read.", MutedColour)
                    SetTile(faultsTile, "--", String.Empty, MutedColour)
                    SetTile(fallbacksTile, "--", String.Empty, MutedColour)
                    attentionGrid.Rows.Clear()
                    activityPanel.Controls.Clear()
                    timingPanel.Controls.Clear()
                    Return
                End If

                ' "as at", never "now". The figures are as old as the last refresh, and a page that
                ' implies otherwise is claiming something it cannot know.
                asAtLabel.Text = "as at " & snapshot.TakenAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)

                ShowQueryStoreState()

                If snapshot.HasData Then
                    gauge.Score = snapshot.Score
                Else
                    gauge.ClearScore()
                End If

                FillRegistrations(snapshot)
                FillTiles()
                FillActivity()
                FillTiming()
                FillNeedsAttention()

            Finally
                Cursor = Cursors.Default
            End Try
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

            If snapshot.AuditedOperations > 0 Then
                SetTile(fallbacksTile,
                        snapshot.FallbackCount.ToString("N0", CultureInfo.CurrentCulture),
                        "per 100 operations " & snapshot.FallbackRatePer100.ToString("0.0", CultureInfo.InvariantCulture),
                        If(snapshot.FallbackRatePer100 > 5, Color.FromArgb(232, 160, 25), HeadingColour))
            Else
                SetTile(fallbacksTile,
                        snapshot.FallbackCount.ToString("N0", CultureInfo.CurrentCulture),
                        "no audited operations to compare against",
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
                row.DefaultCellStyle.ForeColor = MutedColour
                row.DefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Italic)
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

                ' A fault that came back after somebody fixed it is the loudest thing this panel
                ' can show - louder than one nobody has seen before, because a fix has already
                ' failed. It gets the colour, and it gets it even when acknowledged.
                If fault.RecurredAfterResolved AndAlso Not fault.Resolved Then
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(200, 55, 50)
                    row.DefaultCellStyle.Font = New Font("Segoe UI", 9.5F, FontStyle.Bold)
                ElseIf fault.Resolved OrElse fault.Acknowledged Then
                    row.DefaultCellStyle.ForeColor = MutedColour
                End If

                ' Selected text keeps the colour the row earned. Without this the grid inverts it
                ' to white and a recurred fault stops looking like one the moment it is clicked.
                row.DefaultCellStyle.SelectionForeColor =
                    If(row.DefaultCellStyle.ForeColor.IsEmpty, HeadingColour, row.DefaultCellStyle.ForeColor)
            Next
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
