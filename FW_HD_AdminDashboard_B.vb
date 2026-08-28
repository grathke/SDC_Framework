Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class FW_HD_AdminDashboard_B
        Inherits Form

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly totalTicketsLabel As Label
        Private ReadOnly openTicketsLabel As Label
        Private ReadOnly awaitingSupportLabel As Label
        Private ReadOnly firstResponseLabel As Label
        Private ReadOnly resolutionLabel As Label
        Private ReadOnly statusGrid As DataGridView
        Private ReadOnly priorityGrid As DataGridView
        Private ReadOnly categoryGrid As DataGridView
        Private ReadOnly accountGrid As DataGridView
        Private ReadOnly oldestGrid As DataGridView
        Private ReadOnly closeButton As Button
        Private oldestSection As GroupBox
        Private currentTicketFilterType As String
        Private currentTicketFilterValue As String
        Private totalTile As Panel
        Private openTile As Panel
        Private awaitingTile As Panel

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = If(user, BuildCurrentUserFromSession())
            accessProfile = If(profile, MenuFormInitializer.BuildAccessProfileForCurrentSession(currentUser, NameOf(FW_HD_AdminDashboard_B)))

            Text = "Admin Help Desk Dashboard"
            StartPosition = FormStartPosition.CenterParent
            MinimumSize = New Size(1100, 700)
            ClientSize = New Size(1250, 820)
            BackColor = Color.White

            totalTicketsLabel = New Label()
            openTicketsLabel = New Label()
            awaitingSupportLabel = New Label()
            firstResponseLabel = New Label()
            resolutionLabel = New Label()
            statusGrid = CreateBreakdownGrid("Status")
            priorityGrid = CreateBreakdownGrid("Priority")
            categoryGrid = CreateBreakdownGrid("Category")
            accountGrid = CreateBreakdownGrid("Account")
            oldestGrid = CreateOldestGrid()
            closeButton = New Button With {
                .Text = "Close",
                .Size = New Size(90, 36),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .TabStop = False
            }

            BuildDashboard()
            AddHandler closeButton.Click, AddressOf CloseButton_Click
            AddHandler Me.Shown, AddressOf DashboardPage_Shown
            AddHandler statusGrid.CellDoubleClick, AddressOf BreakdownGrid_CellDoubleClick
            AddHandler priorityGrid.CellDoubleClick, AddressOf BreakdownGrid_CellDoubleClick
            AddHandler categoryGrid.CellDoubleClick, AddressOf BreakdownGrid_CellDoubleClick
            AddHandler accountGrid.CellDoubleClick, AddressOf BreakdownGrid_CellDoubleClick
            AddHandler oldestGrid.CellDoubleClick, AddressOf OldestGrid_CellDoubleClick
        End Sub

        Private Sub BuildDashboard()
            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4, .BackColor = Color.White}
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 48.0F))
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 116.0F))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 52.0F))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 48.0F))
            Controls.Add(root)

            Dim heading As New Panel With {.Dock = DockStyle.Fill}
            AddHandler heading.Resize, AddressOf DashboardHeading_Resize
            heading.Controls.Add(New Label With {
                .Text = "Support Dashboard - All Accounts",
                .AutoSize = True,
                .Font = New Font("Segoe UI", 16.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(37, 58, 103),
                .Location = New Point(2, 3)
            })
            heading.Controls.Add(closeButton)
            PositionCloseButton(heading)
            closeButton.BringToFront()
            root.Controls.Add(heading, 0, 0)

            Dim kpis As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1}
            For index As Integer = 0 To 4
                kpis.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20.0F))
            Next
            totalTile = CreateKpiTile("Total tickets", totalTicketsLabel, "All", Nothing)
            openTile = CreateKpiTile("Open", openTicketsLabel, "Open", Nothing)
            awaitingTile = CreateKpiTile("Awaiting Support", awaitingSupportLabel, "Awaiting", Nothing)
            kpis.Controls.Add(totalTile, 0, 0)
            kpis.Controls.Add(openTile, 1, 0)
            kpis.Controls.Add(awaitingTile, 2, 0)
            kpis.Controls.Add(CreateKpiTile("Avg first response", firstResponseLabel, Nothing, Nothing), 3, 0)
            kpis.Controls.Add(CreateKpiTile("Avg resolution", resolutionLabel, Nothing, Nothing), 4, 0)
            root.Controls.Add(kpis, 0, 1)

            Dim breakdowns As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1}
            For index As Integer = 0 To 3
                breakdowns.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25.0F))
            Next
            breakdowns.Controls.Add(CreateSection("By status", statusGrid), 0, 0)
            breakdowns.Controls.Add(CreateSection("By priority", priorityGrid), 1, 0)
            breakdowns.Controls.Add(CreateSection("By category", categoryGrid), 2, 0)
            breakdowns.Controls.Add(CreateSection("By account", accountGrid), 3, 0)
            root.Controls.Add(breakdowns, 0, 2)
            oldestSection = CreateSection("All tickets", oldestGrid)
            root.Controls.Add(oldestSection, 0, 3)
        End Sub

        Private Sub DashboardHeading_Resize(sender As Object, e As EventArgs)
            PositionCloseButton(TryCast(sender, Control))
        End Sub

        Private Sub PositionCloseButton(container As Control)
            If container Is Nothing Then Return
            closeButton.Left = Math.Max(0, container.ClientSize.Width - closeButton.Width - 4)
            closeButton.Top = 6
            closeButton.BringToFront()
        End Sub

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Close()
        End Sub

        Private Sub DashboardPage_Shown(sender As Object, e As EventArgs)
            currentTicketFilterType = "Open"
            currentTicketFilterValue = Nothing
            UpdateFilterTileHighlight()
            RefreshDashboard()
        End Sub

        Public Sub RefreshDashboard()
            Try
                Dim snapshot = HelpDeskDataAccess.GetAdminDashboardSnapshot(currentTicketFilterType, currentTicketFilterValue)
                Dim kpis = snapshot.Kpis
                If kpis.Rows.Count > 0 Then
                    Dim row = kpis.Rows(0)
                    totalTicketsLabel.Text = NumberText(row("TotalTickets"))
                    openTicketsLabel.Text = NumberText(row("OpenTickets"))
                    awaitingSupportLabel.Text = NumberText(row("AwaitingSupport"))
                    firstResponseLabel.Text = HoursText(row("AvgFirstResponseHours"))
                    resolutionLabel.Text = HoursText(row("AvgResolutionHours"))
                End If

                statusGrid.DataSource = AddEmptyBreakdownRow(snapshot.ByStatus)
                priorityGrid.DataSource = AddEmptyBreakdownRow(snapshot.ByPriority)
                categoryGrid.DataSource = AddEmptyBreakdownRow(snapshot.ByCategory)
                accountGrid.DataSource = AddEmptyBreakdownRow(snapshot.ByRegistration)
                oldestGrid.DataSource = snapshot.OldestAwaitingSupport
                oldestSection.Text = FilterCaption()
                ConfigureGridColumns()
            Catch ex As Exception
                MessageBox.Show(Me, "Error loading the Help Desk dashboard: " & ex.Message, "Dashboard Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub ApplyTicketFilter(filterType As String, filterValue As String, caption As String)
            If String.Equals(filterType, "All", StringComparison.OrdinalIgnoreCase) AndAlso
               Not String.Equals(currentTicketFilterType, "All", StringComparison.OrdinalIgnoreCase) Then
                Dim result = MessageBox.Show(Me,
                                             "DISPLAY ALL TICKETS?" & Environment.NewLine & Environment.NewLine &
                                             "LOADING ALL TICKETS MAY TAKE SOME TIME ...",
                                             "Show All Tickets",
                                             MessageBoxButtons.YesNo,
                                             MessageBoxIcon.Question)
                If result <> DialogResult.Yes Then Return
            End If

            currentTicketFilterType = filterType
            currentTicketFilterValue = filterValue
            oldestSection.Text = caption
            UpdateFilterTileHighlight()
            RefreshDashboard()
        End Sub

        Private Function FilterCaption() As String
            Select Case currentTicketFilterType
                Case "All" : Return "All tickets"
                Case "Open" : Return "Open tickets"
                Case "Awaiting" : Return "Awaiting support"
                Case Else : Return "Filtered tickets"
            End Select
        End Function

        Private Sub UpdateFilterTileHighlight()
            SetFilterTileStyle(totalTile, String.Equals(currentTicketFilterType, "All", StringComparison.OrdinalIgnoreCase))
            SetFilterTileStyle(openTile, String.Equals(currentTicketFilterType, "Open", StringComparison.OrdinalIgnoreCase))
            SetFilterTileStyle(awaitingTile, String.Equals(currentTicketFilterType, "Awaiting", StringComparison.OrdinalIgnoreCase))
        End Sub

        Private Shared Sub SetFilterTileStyle(tile As Panel, isSelected As Boolean)
            If tile Is Nothing Then Return
            tile.BackColor = If(isSelected, Color.FromArgb(232, 245, 255), Color.White)
            tile.BorderStyle = BorderStyle.FixedSingle
        End Sub

        Private Sub BreakdownGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
        End Sub

        Private Sub OldestGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
            Dim issueId = Convert.ToInt32(oldestGrid.Rows(e.RowIndex).Cells("IssueID").Value, CultureInfo.InvariantCulture)
            Dim registrationId = GetRegistrationIdForIssue(issueId)
            If registrationId <= 0 Then Return

            Using issuePage As New FW_HD_Issues_U(issueId, registrationId)
                issuePage.ShowDialog(Me)
            End Using
            RefreshDashboard()
        End Sub

        Private Function GetRegistrationIdForIssue(issueId As Integer) As Integer
            Dim issue = HelpDeskDataAccess.GetIssueById(issueId, GetSessionRegistrationId())
            If issue IsNot Nothing Then Return issue.RegistrationID

            For Each registration As DataRow In DataAccess.GetAllRegistrations().Rows
                Dim registrationId = Convert.ToInt32(registration("ID"), CultureInfo.InvariantCulture)
                issue = HelpDeskDataAccess.GetIssueById(issueId, registrationId)
                If issue IsNot Nothing Then Return issue.RegistrationID
            Next
            Return 0
        End Function

        Private Sub ConfigureGridColumns()
            ConfigureBreakdownGrid(statusGrid)
            ConfigureBreakdownGrid(priorityGrid)
            ConfigureBreakdownGrid(categoryGrid)
            ConfigureBreakdownGrid(accountGrid)
            oldestGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells)
            If oldestGrid.Columns.Contains("Subject") Then oldestGrid.Columns("Subject").AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            If oldestGrid.Columns.Contains("IssueID") Then oldestGrid.Columns("IssueID").Visible = False
            If oldestGrid.Columns.Contains("CreatedOn") Then oldestGrid.Columns("CreatedOn").DefaultCellStyle.Format = "ddd, MMM d, yyyy h:mm tt"
            If oldestGrid.Columns.Contains("UpdatedOn") Then oldestGrid.Columns("UpdatedOn").DefaultCellStyle.Format = "ddd, MMM d, yyyy h:mm tt"
        End Sub

        Private Shared Sub ConfigureBreakdownGrid(grid As DataGridView)
            If grid.Columns.Contains("Bucket") Then grid.Columns("Bucket").HeaderText = ""
            If grid.Columns.Contains("TicketCount") Then grid.Columns("TicketCount").HeaderText = "Tickets"
            If grid.Columns.Contains("RegistrationID") Then grid.Columns("RegistrationID").Visible = False
            grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells)
            If grid.Columns.Contains("Bucket") Then grid.Columns("Bucket").AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        End Sub

        Private Shared Function AddEmptyBreakdownRow(source As DataTable) As DataTable
            If source Is Nothing Then Return source
            If source.Rows.Count > 0 Then Return source

            Dim row = source.NewRow()
            If source.Columns.Contains("Bucket") Then row("Bucket") = "NA"
            If source.Columns.Contains("TicketCount") Then row("TicketCount") = 0
            If source.Columns.Contains("RegistrationID") Then row("RegistrationID") = 0
            source.Rows.Add(row)
            Return source
        End Function

        Private Shared Function CreateBreakdownGrid(title As String) As DataGridView
            Return New DataGridView With {
                .Name = title.Replace(" ", String.Empty) & "Grid",
                .Dock = DockStyle.Fill,
                .BackgroundColor = Color.White,
                .BorderStyle = BorderStyle.None,
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .AutoGenerateColumns = True,
                .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .MultiSelect = False
            }
        End Function

        Private Shared Function CreateOldestGrid() As DataGridView
            Dim grid = CreateBreakdownGrid("Oldest")
            grid.RowHeadersVisible = False
            Return grid
        End Function

        Private Shared Function CreateSection(caption As String, content As Control) As GroupBox
            Dim section As New GroupBox With {.Text = caption, .Dock = DockStyle.Fill, .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold), .ForeColor = Color.FromArgb(48, 48, 48), .Padding = New Padding(8, 24, 8, 8)}
            section.Controls.Add(content)
            Return section
        End Function

        Private Function CreateKpiTile(caption As String, valueLabel As Label, filterType As String, filterValue As String) As Panel
            Dim tile As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.White, .Margin = New Padding(4), .BorderStyle = BorderStyle.FixedSingle}
            valueLabel.AutoSize = True
            valueLabel.Text = "-"
            valueLabel.Location = New Point(12, 12)
            valueLabel.Font = New Font("Segoe UI", 20.0F, FontStyle.Bold)
            valueLabel.ForeColor = Color.FromArgb(42, 70, 132)
            tile.Controls.Add(valueLabel)
            Dim captionLabel = New Label With {.Text = caption, .AutoSize = True, .Location = New Point(13, 62), .Font = New Font("Segoe UI", 9.0F), .ForeColor = Color.DimGray}
            tile.Controls.Add(captionLabel)
            If Not String.IsNullOrWhiteSpace(filterType) Then
                AddHandler tile.Click, Sub(sender, e) ApplyTicketFilter(filterType, filterValue, caption & " tickets")
                AddHandler valueLabel.Click, Sub(sender, e) ApplyTicketFilter(filterType, filterValue, caption & " tickets")
                AddHandler captionLabel.Click, Sub(sender, e) ApplyTicketFilter(filterType, filterValue, caption & " tickets")
                tile.Cursor = Cursors.Hand
                valueLabel.Cursor = Cursors.Hand
                captionLabel.Cursor = Cursors.Hand
            End If
            Return tile
        End Function

        Private Shared Function NumberText(value As Object) As String
            If value Is Nothing OrElse Convert.IsDBNull(value) Then Return "0"
            Return Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
        End Function

        Private Shared Function HoursText(value As Object) As String
            If value Is Nothing OrElse Convert.IsDBNull(value) Then Return "NA"
            Return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("0.0", CultureInfo.InvariantCulture) & " h"
        End Function

        Private Shared Function BuildCurrentUserFromSession() As UserContext
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                Dim session = SessionState.Current.Value
                Return New UserContext With {.UserId = session.UserID, .FirstName = session.FirstName, .LastName = session.LastName}
            End If
            Return New UserContext()
        End Function

        Private Shared Function GetSessionRegistrationId() As Integer
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then Return SessionState.Current.Value.RegistrationID
            Return 0
        End Function
    End Class
End Namespace
