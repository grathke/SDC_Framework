Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class MainMenu
        Inherits Form

        Public Enum MenuRegion
            Messages
            GeneralDashboard
            AcmeDashboard
            UsersAndLists
            Chart
        End Enum

        Private Class ActionTile
            Public Property Key As String
            Public Property Button As Button
            Public Property OnClick As EventHandler
        End Class

        Private Class RibbonActionButton
            Inherits Button

            Protected Overrides ReadOnly Property ShowFocusCues As Boolean
                Get
                    Return False
                End Get
            End Property
        End Class

        Private Class RegionShell
            Public Property RootPanel As Panel
            Public Property HeaderPanel As Panel
            Public Property HeaderLabel As Label
            Public Property ContentHost As Panel
        End Class

        Private ReadOnly currentUser As UserContext
        Private activeAccessProfile As AccessProfile
        Private ReadOnly ribbonPanel As Panel
        Private ReadOnly leftActionsFlow As FlowLayoutPanel
        Private ReadOnly rightPinnedActionsPanel As Panel
        Private ReadOnly headingLabel As Label
        Private ReadOnly welcomeLabel As Label
        Private ReadOnly userBadgeLabel As Label
        Private ReadOnly eodLabel As Label
        Private ReadOnly rfrLabel As Label
        Private ReadOnly contentLayout As TableLayoutPanel
        Private ReadOnly actionTilesByKey As Dictionary(Of String, ActionTile)
        Private ReadOnly regionShells As Dictionary(Of MenuRegion, RegionShell)
        Private Shared ReadOnly RibbonHoverBackColor As Color = Color.FromArgb(232, 245, 255)
        Private Shared ReadOnly RibbonHoverBorderColor As Color = Color.FromArgb(91, 161, 217)

        Public Sub New(user As UserContext)
            Me.New(user, Nothing)
        End Sub

        Public Sub New(user As UserContext, initializeMenu As Action(Of MainMenu, UserContext))
            currentUser = user

            Me.Text = "Main Menu"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.Sizable
            Me.MaximizeBox = True
            Me.MinimizeBox = True
            Me.MinimumSize = New Size(1180, 760)
            Me.ClientSize = New Size(1280, 800)
            Me.BackColor = Color.White

            ribbonPanel = New Panel() With {
                .Location = New Point(8, 8),
                .Size = New Size(Me.ClientSize.Width - 16, 140),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .BackColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle
            }

            Dim mainTabLabel As New Label() With {
                .Text = "Main",
                .Location = New Point(6, 4),
                .Size = New Size(52, 26),
                .BackColor = Color.White,
                .TextAlign = ContentAlignment.MiddleLeft,
                .Font = New Font("Segoe UI", 10.5F, FontStyle.Regular)
            }

            leftActionsFlow = New FlowLayoutPanel() With {
                .Location = New Point(10, 32),
                .Size = New Size(760, 102),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .AutoScroll = False,
                .Margin = New Padding(0)
            }

            rightPinnedActionsPanel = New Panel() With {
                .Location = New Point(ribbonPanel.Width - 514, 32),
                .Size = New Size(500, 102),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .BackColor = Color.Transparent
            }

            Dim session = SessionState.Current
            Dim registrationNameForHeader As String = "DEVELOPMENT TEAM"
            Dim registrationIdForHeader As Integer = 1
            Dim welcomeName As String = currentUser.DisplayName
            Dim welcomeUserId As Integer = currentUser.UserId

            If session.HasValue Then
                If session.Value.RegistrationName <> String.Empty Then
                    registrationNameForHeader = session.Value.RegistrationName
                End If

                If session.Value.RegistrationID > 0 Then
                    registrationIdForHeader = session.Value.RegistrationID
                End If

                If session.Value.FirstLast <> String.Empty Then
                    welcomeName = session.Value.FirstLast
                End If

                If session.Value.UserID > 0 Then
                    welcomeUserId = session.Value.UserID
                End If
            End If

            headingLabel = New Label() With {
                .AutoSize = True,
                .Location = New Point(48, 158),
                .Text = registrationNameForHeader & " (" & registrationIdForHeader.ToString() & ")",
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(24, 45, 78)
            }

            welcomeLabel = New Label() With {
                .AutoSize = False,
                .Location = New Point(52, 198),
                .Size = New Size(540, 28),
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Regular),
                .Text = "Welcome " & welcomeName & " (" & welcomeUserId.ToString() & ")"
            }

            userBadgeLabel = New Label() With {
                .AutoSize = False,
                .Location = New Point(Me.ClientSize.Width - 320, 182),
                .Size = New Size(180, 34),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .Text = currentUser.Email,
                .TextAlign = ContentAlignment.MiddleLeft,
                .BackColor = Color.FromArgb(144, 244, 251),
                .ForeColor = Color.FromArgb(27, 76, 110),
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Regular)
            }

            eodLabel = New Label() With {
                .AutoSize = False,
                .Location = New Point(Me.ClientSize.Width - 132, 184),
                .Size = New Size(120, 20),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .Text = "EOD: 10:51 AM",
                .TextAlign = ContentAlignment.MiddleLeft,
                .Font = New Font("Segoe UI", 10.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(52, 60, 70)
            }

            rfrLabel = New Label() With {
                .AutoSize = False,
                .Location = New Point(Me.ClientSize.Width - 132, 206),
                .Size = New Size(120, 20),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .Text = "RFR: 1 min",
                .TextAlign = ContentAlignment.MiddleLeft,
                .Font = New Font("Segoe UI", 10.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(52, 60, 70)
            }

            contentLayout = New TableLayoutPanel() With {
                .Location = New Point(24, 236),
                .Size = New Size(Me.ClientSize.Width - 48, Me.ClientSize.Height - 260),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom,
                .ColumnCount = 3,
                .RowCount = 2,
                .BackColor = Color.White,
                .Padding = New Padding(0),
                .Margin = New Padding(0)
            }
            contentLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            contentLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 34.0F))
            contentLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.0F))
            contentLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.0F))
            contentLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 47.0F))
            contentLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 53.0F))

            regionShells = New Dictionary(Of MenuRegion, RegionShell)()
            regionShells(MenuRegion.Messages) = CreateRegionShell("Messages")
            regionShells(MenuRegion.GeneralDashboard) = CreateRegionShell("General Dashboard")
            regionShells(MenuRegion.AcmeDashboard) = CreateRegionShell("Acme Dashboard")
            regionShells(MenuRegion.UsersAndLists) = CreateRegionShell("Users & Lists")
            regionShells(MenuRegion.Chart) = CreateRegionShell("Evolution of Acme Products", True)

            contentLayout.Controls.Add(regionShells(MenuRegion.Messages).RootPanel, 0, 0)
            contentLayout.SetRowSpan(regionShells(MenuRegion.Messages).RootPanel, 2)
            contentLayout.Controls.Add(regionShells(MenuRegion.GeneralDashboard).RootPanel, 1, 0)

            Dim rightTopLayout As New TableLayoutPanel() With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 2,
                .RowCount = 1,
                .CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
                .BackColor = Color.White
            }
            rightTopLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
            rightTopLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
            rightTopLayout.Controls.Add(regionShells(MenuRegion.AcmeDashboard).RootPanel, 0, 0)
            rightTopLayout.Controls.Add(regionShells(MenuRegion.UsersAndLists).RootPanel, 1, 0)
            contentLayout.Controls.Add(rightTopLayout, 2, 0)

            Dim chartContainer As New Panel() With {
                .Dock = DockStyle.Fill,
                .BackColor = Color.White
            }
            chartContainer.Controls.Add(regionShells(MenuRegion.Chart).RootPanel)
            contentLayout.Controls.Add(chartContainer, 1, 1)
            contentLayout.SetColumnSpan(chartContainer, 2)

            actionTilesByKey = New Dictionary(Of String, ActionTile)(StringComparer.OrdinalIgnoreCase)
            AddActionTile("close", "Close", AddressOf CloseMenu_Click, LoadMenuIcon("close.png", SystemIcons.Error.ToBitmap()))
            AddActionTile("application-settings", "Application" & Environment.NewLine & "Settings", AddressOf ApplicationSettings_Click, LoadMenuIcon("gear.png", SystemIcons.Shield.ToBitmap()))
            AddActionTile("users", "Users", AddressOf Users_Click, LoadMenuIcon("users.png", SystemIcons.Information.ToBitmap()))
            AddActionTile("entity", "Entity", AddressOf Clients_Click, LoadMenuIcon("clients.png", SystemIcons.Asterisk.ToBitmap()))
            AddActionTile("my-profile", "My Profile", AddressOf MyProfile_Click, LoadMenuIcon("my-profile.png", SystemIcons.Question.ToBitmap()))
            AddActionTile("login-as-substitute", "LOGIN AS SUBSTITUE USER", AddressOf LoginAsSubstitute_Click, LoadMenuIcon("substitute-user.png", SystemIcons.Warning.ToBitmap()))
            AddActionTile("select-role", "Select a Role (Application Admin)", AddressOf SelectRole_Click, LoadMenuIcon("users.png", SystemIcons.WinLogo.ToBitmap()))
            AddActionTile("help-desk", "Help Desk", AddressOf HelpDesk_Click, LoadMenuIcon("helpdesk.png", SystemIcons.Question.ToBitmap()))

            AddHandler ribbonPanel.Resize, AddressOf RibbonPanel_Resize

            ribbonPanel.Controls.Add(mainTabLabel)
            ribbonPanel.Controls.Add(leftActionsFlow)
            ribbonPanel.Controls.Add(rightPinnedActionsPanel)

            Me.Controls.Add(ribbonPanel)
            Me.Controls.Add(headingLabel)
            Me.Controls.Add(welcomeLabel)
            Me.Controls.Add(userBadgeLabel)
            Me.Controls.Add(eodLabel)
            Me.Controls.Add(rfrLabel)
            Me.Controls.Add(contentLayout)

            ConfigureActionVisibility("application-settings", True, True)
            ConfigureActionVisibility("login-as-substitute", True, True)
            ConfigureActionVisibility("select-role", True, True)

            SetTimezoneControlsVisible(False)
            LoadSamplePlaceholders()
            UpdateRibbonLayout()

            If initializeMenu IsNot Nothing Then
                initializeMenu(Me, currentUser)
            End If

            UpdateRoleSelectionTile()
        End Sub

        Private Sub UpdateRoleSelectionTile()
            Dim session = SessionState.Current
            Dim currentRoleName As String = "NO ROLE"
            If session.HasValue AndAlso Not String.IsNullOrWhiteSpace(session.Value.RoleName) Then
                currentRoleName = session.Value.RoleName.Trim()
            End If

            Dim formattedRoleCaption = FormatRoleCaption(currentRoleName)

            UpsertActionTile(
                actionKey:="select-role",
                caption:=formattedRoleCaption,
                onClick:=AddressOf SelectRole_Click,
                iconFileName:="users.png",
                fallbackIcon:=SystemIcons.WinLogo.ToBitmap(),
                isVisible:=True,
                isEnabled:=True)
        End Sub

        Private Shared Function FormatRoleCaption(roleName As String) As String
            Dim safeName = If(roleName, String.Empty).Trim()
            If safeName = String.Empty Then
                Return "NO ROLE"
            End If

            Dim words = safeName.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If words.Length <= 1 Then
                Return safeName
            End If

            Return words(0) & Environment.NewLine & String.Join(" ", words.Skip(1))
        End Function

        Private Function GetAvailableSessionRoles() As List(Of UserRoleOption)
            Dim roles As New List(Of UserRoleOption)()
            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return roles
            End If

            If session.Value.UserID <= 0 OrElse session.Value.RegistrationID <= 0 Then
                Return roles
            End If

            roles = DataAccess.GetAssignedRolesForUser(session.Value.UserID, session.Value.RegistrationID)
            If roles Is Nothing Then
                Return New List(Of UserRoleOption)()
            End If

            Return roles.OrderBy(Function(r) r.DisplayOrder).ThenBy(Function(r) r.RoleName).ToList()
        End Function

        Public Sub LoadRegionControl(region As MenuRegion, content As Control)
            If content Is Nothing Then
                Return
            End If

            content.Dock = DockStyle.Fill

            Dim shell = GetRegionShell(region)
            shell.ContentHost.SuspendLayout()
            shell.ContentHost.Controls.Clear()
            shell.ContentHost.Controls.Add(content)
            shell.ContentHost.ResumeLayout()
        End Sub

        Public Sub LoadRegionForm(region As MenuRegion, childForm As Form)
            If childForm Is Nothing Then
                Return
            End If

            childForm.TopLevel = False
            childForm.FormBorderStyle = FormBorderStyle.None
            childForm.Dock = DockStyle.Fill
            childForm.Visible = True

            LoadRegionControl(region, childForm)
        End Sub

        Public Sub ConfigureActionVisibility(actionKey As String, isVisible As Boolean, isEnabled As Boolean)
            Dim tile As ActionTile = Nothing
            If Not actionTilesByKey.TryGetValue(actionKey, tile) Then
                Return
            End If

            If IsAlwaysVisibleActionKey(actionKey) Then
                tile.Button.Visible = True
                tile.Button.Enabled = isEnabled
                Return
            End If

            tile.Button.Visible = isVisible
            tile.Button.Enabled = isEnabled
        End Sub

        Public Sub SetAccessProfile(profile As AccessProfile)
            activeAccessProfile = profile
        End Sub

        Public Sub UpsertActionTile(actionKey As String,
                                    caption As String,
                                    onClick As EventHandler,
                                    Optional iconFileName As String = Nothing,
                                    Optional fallbackIcon As Image = Nothing,
                                    Optional isVisible As Boolean = True,
                                    Optional isEnabled As Boolean = True)
            Dim tile As ActionTile = Nothing
            Dim hasExistingTile = actionTilesByKey.TryGetValue(actionKey, tile)

            Dim safeFallback As Image = fallbackIcon
            If safeFallback Is Nothing Then
                safeFallback = SystemIcons.Information.ToBitmap()
            End If

            If Not hasExistingTile Then
                Dim tileImage = If(String.IsNullOrWhiteSpace(iconFileName), safeFallback, LoadMenuIcon(iconFileName, safeFallback))
                AddActionTile(actionKey, caption, onClick, tileImage)
                ConfigureActionVisibility(actionKey, isVisible, isEnabled)
                Return
            End If

            tile.Button.Text = caption

            If tile.OnClick IsNot Nothing Then
                RemoveHandler tile.Button.Click, tile.OnClick
            End If

            tile.OnClick = onClick
            If onClick IsNot Nothing Then
                AddHandler tile.Button.Click, onClick
            End If

            If Not String.IsNullOrWhiteSpace(iconFileName) Then
                tile.Button.Image = NormalizeActionIcon(actionKey, LoadMenuIcon(iconFileName, safeFallback))
            ElseIf fallbackIcon IsNot Nothing Then
                tile.Button.Image = NormalizeActionIcon(actionKey, fallbackIcon)
            End If

            If IsAlwaysVisibleActionKey(actionKey) Then
                tile.Button.Visible = True
            Else
                tile.Button.Visible = isVisible
            End If
            tile.Button.Enabled = isEnabled
        End Sub

        Public Sub SetActionIconFromFile(actionKey As String, iconFileName As String, Optional fallbackIcon As Image = Nothing)
            If String.IsNullOrWhiteSpace(iconFileName) Then
                Return
            End If

            Dim tile As ActionTile = Nothing
            If Not actionTilesByKey.TryGetValue(actionKey, tile) Then
                Return
            End If

            Dim safeFallback As Image = fallbackIcon
            If safeFallback Is Nothing Then
                safeFallback = SystemIcons.Information.ToBitmap()
            End If

            tile.Button.Image = NormalizeActionIcon(actionKey, LoadMenuIcon(iconFileName, safeFallback))
        End Sub

        Public Sub SetActionCaption(actionKey As String, caption As String)
            If String.IsNullOrWhiteSpace(actionKey) Then
                Return
            End If

            Dim tile As ActionTile = Nothing
            If Not actionTilesByKey.TryGetValue(actionKey, tile) Then
                Return
            End If

            tile.Button.Text = If(caption, String.Empty)
        End Sub

        Public Sub SetRegionHeader(region As MenuRegion, headerText As String)
            Dim shell = GetRegionShell(region)
            shell.HeaderLabel.Text = headerText
        End Sub

        Public Sub SetRegionVisible(region As MenuRegion, isVisible As Boolean)
            Dim shell = GetRegionShell(region)
            shell.RootPanel.Visible = isVisible
            shell.RootPanel.Enabled = isVisible
        End Sub

        Public Sub ShowRegionAccessDenied(region As MenuRegion, tableName As String, roleName As String)
            Dim roleText = If(String.IsNullOrWhiteSpace(roleName), "current role", roleName)
            Dim tableText = If(String.IsNullOrWhiteSpace(tableName), "this table", tableName)

            Dim deniedPanel As New Panel() With {
                .Dock = DockStyle.Fill,
                .BackColor = Color.FromArgb(250, 250, 250),
                .Padding = New Padding(14)
            }

            Dim deniedLabel As New Label() With {
                .Dock = DockStyle.Fill,
                .TextAlign = ContentAlignment.MiddleCenter,
                .ForeColor = Color.FromArgb(107, 37, 29),
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Bold),
                .Text = "No access to " & tableText & Environment.NewLine &
                        "Role: " & roleText
            }

            deniedPanel.Controls.Add(deniedLabel)
            LoadRegionControl(region, deniedPanel)
        End Sub

        Public Sub SetTimezoneControlsVisible(isVisible As Boolean)
            ' Timezone controls are optional and currently removed from this shell revision.
        End Sub

        Private Function CreateRegionShell(title As String, Optional centerHeader As Boolean = False) As RegionShell
            Dim rootPanel As New Panel() With {
                .Dock = DockStyle.Fill,
                .BackColor = Color.White,
                .Padding = New Padding(0),
                .Margin = New Padding(5),
                .BorderStyle = BorderStyle.FixedSingle
            }

            Dim headerPanel As New Panel() With {
                .Dock = DockStyle.Top,
                .Height = 44,
                .BackColor = Color.White
            }

            Dim titleLabel As New Label() With {
                .Text = title,
                .Dock = DockStyle.Fill,
                .Font = New Font("Segoe UI", 15.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(76, 84, 94),
                .Padding = New Padding(10, 4, 10, 0),
                .TextAlign = If(centerHeader, ContentAlignment.MiddleCenter, ContentAlignment.MiddleLeft)
            }

            Dim headerAction As New Button() With {
                .Size = New Size(34, 34),
                .Location = New Point(2, 2),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .FlatStyle = FlatStyle.Flat,
                .Text = ChrW(9654).ToString(),
                .ForeColor = Color.FromArgb(58, 133, 197),
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Bold),
                .UseVisualStyleBackColor = True
            }
            headerAction.FlatAppearance.BorderSize = 0

            Dim contentHost As New Panel() With {
                .Dock = DockStyle.Fill,
                .BackColor = Color.White,
                .Padding = New Padding(8)
            }

            AddHandler headerPanel.Resize,
                Sub()
                    headerAction.Left = headerPanel.Width - headerAction.Width - 6
                End Sub

            headerPanel.Controls.Add(titleLabel)
            headerPanel.Controls.Add(headerAction)
            rootPanel.Controls.Add(contentHost)
            rootPanel.Controls.Add(headerPanel)

            Return New RegionShell() With {
                .RootPanel = rootPanel,
                .HeaderPanel = headerPanel,
                .HeaderLabel = titleLabel,
                .ContentHost = contentHost
            }
        End Function

        Private Sub AddActionTile(key As String, caption As String, onClick As EventHandler, tileImage As Image)
            Dim tileButton As New RibbonActionButton() With {
                .Name = "ACTION_" & key,
                .Text = caption,
                .Size = New Size(122, 96),
                .TextAlign = ContentAlignment.TopCenter,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .UseVisualStyleBackColor = False,
                .BackColor = Color.Transparent,
                .FlatStyle = FlatStyle.Flat,
                .Margin = New Padding(0, 0, 4, 0),
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                .Padding = New Padding(0, 2, 0, 0),
                .Image = NormalizeActionIcon(key, tileImage),
                .TabStop = False
            }
            tileButton.FlatAppearance.BorderSize = 0
            tileButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            tileButton.FlatAppearance.MouseDownBackColor = Color.Transparent
            AddHandler tileButton.Click, AddressOf RibbonTile_Click
            AddHandler tileButton.Click, onClick
            AddHandler tileButton.MouseEnter, AddressOf RibbonTile_MouseEnter
            AddHandler tileButton.MouseLeave, AddressOf RibbonTile_MouseLeave

            actionTilesByKey(key) = New ActionTile() With {
                .Key = key,
                .Button = tileButton,
                .OnClick = onClick
            }

            If IsAlwaysVisibleActionKey(key) Then
                tileButton.Margin = New Padding(0)
                rightPinnedActionsPanel.Controls.Add(tileButton)
                LayoutPinnedActions()
            Else
                leftActionsFlow.Controls.Add(tileButton)
            End If
        End Sub

        Private Sub RibbonTile_Click(sender As Object, e As EventArgs)
            ResetRibbonTileVisuals()
            Me.ActiveControl = Nothing
        End Sub

        Private Sub ResetRibbonTileVisuals()
            For Each kvp In actionTilesByKey
                Dim tileButton = kvp.Value.Button
                If tileButton Is Nothing Then
                    Continue For
                End If

                tileButton.BackColor = Color.Transparent
                tileButton.FlatAppearance.BorderSize = 0
            Next
        End Sub

        Private Sub RibbonTile_MouseEnter(sender As Object, e As EventArgs)
            Dim tileButton = TryCast(sender, Button)
            If tileButton Is Nothing Then
                Return
            End If

            tileButton.BackColor = RibbonHoverBackColor
            tileButton.FlatAppearance.BorderSize = 1
            tileButton.FlatAppearance.BorderColor = RibbonHoverBorderColor
        End Sub

        Private Sub RibbonTile_MouseLeave(sender As Object, e As EventArgs)
            Dim tileButton = TryCast(sender, Button)
            If tileButton Is Nothing Then
                Return
            End If

            tileButton.BackColor = Color.Transparent
            tileButton.FlatAppearance.BorderSize = 0
        End Sub

        Private Sub LayoutPinnedActions()
            Dim orderedKeys As String() = {"my-profile", "login-as-substitute", "select-role", "help-desk"}
            Dim left As Integer = 0

            For Each key In orderedKeys
                Dim tile As ActionTile = Nothing
                If Not actionTilesByKey.TryGetValue(key, tile) Then
                    Continue For
                End If

                tile.Button.Location = New Point(left, 0)
                tile.Button.Size = New Size(122, 96)
                If String.Equals(key, "help-desk", StringComparison.OrdinalIgnoreCase) Then
                    tile.Button.Text = "Help Desk"
                    tile.Button.Padding = New Padding(0, 2, 0, 0)
                    tile.Button.Image = NormalizeActionIcon("help-desk", LoadMenuIcon("helpdesk.png", SystemIcons.Question.ToBitmap()))
                End If
                left += tile.Button.Width
            Next
        End Sub

        Private Shared Function IsAlwaysVisibleActionKey(actionKey As String) As Boolean
            If String.IsNullOrWhiteSpace(actionKey) Then
                Return False
            End If

            Select Case actionKey.Trim().ToLowerInvariant()
                Case "my-profile", "login-as-substitute", "select-role", "help-desk"
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        Private Sub LoadSamplePlaceholders()
            LoadRegionControl(MenuRegion.Messages, New MessagesWindowControl())
            LoadRegionControl(MenuRegion.GeneralDashboard, New GeneralDashboardWindowControl())
            LoadRegionControl(MenuRegion.AcmeDashboard, New AcmeDashboardWindowControl())
            LoadRegionControl(MenuRegion.UsersAndLists, New UsersListsWindowControl())
            LoadRegionControl(MenuRegion.Chart, BuildChartPlaceholder())
        End Sub

        Private Function GetRegionShell(region As MenuRegion) As RegionShell
            Dim shell As RegionShell = Nothing
            If regionShells.TryGetValue(region, shell) Then
                Return shell
            End If

            Throw New InvalidOperationException("Region shell not found for " & region.ToString())
        End Function

        Private Function BuildChartPlaceholder() As Control
            Dim panel As New Panel() With {
                .Dock = DockStyle.Fill,
                .BackColor = Color.White
            }

            Dim chartBox As New PictureBox() With {
                .Dock = DockStyle.Fill,
                .BackColor = Color.White
            }
            AddHandler chartBox.Paint,
                Sub(sender As Object, e As PaintEventArgs)
                    DrawSimpleTrendChart(e.Graphics, chartBox.ClientRectangle)
                End Sub
            panel.Controls.Add(chartBox)
            Return panel
        End Function

        Private Sub DrawSimpleTrendChart(g As Graphics, bounds As Rectangle)
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
            Dim leftPad As Integer = 58
            Dim topPad As Integer = 22
            Dim rightPad As Integer = 22
            Dim bottomPad As Integer = 46

            Dim chartRect As New Rectangle(bounds.Left + leftPad, bounds.Top + topPad, Math.Max(20, bounds.Width - leftPad - rightPad), Math.Max(20, bounds.Height - topPad - bottomPad))
            Using gridPen As New Pen(Color.FromArgb(210, 218, 226)),
                linePen As New Pen(Color.FromArgb(197, 121, 213), 3.0F),
                fillBrush As New SolidBrush(Color.FromArgb(92, 230, 188, 245)),
                axisFontBrush As New SolidBrush(Color.FromArgb(80, 90, 100))

                For i As Integer = 0 To 5
                    Dim y = chartRect.Top + CInt((chartRect.Height / 5.0F) * i)
                    g.DrawLine(gridPen, chartRect.Left, y, chartRect.Right, y)
                Next

                g.DrawRectangle(gridPen, chartRect)

                Dim points As PointF() = {
                    New PointF(chartRect.Left, chartRect.Top + chartRect.Height * 0.72F),
                    New PointF(chartRect.Left + chartRect.Width * 0.25F, chartRect.Top + chartRect.Height * 0.60F),
                    New PointF(chartRect.Left + chartRect.Width * 0.52F, chartRect.Top + chartRect.Height * 0.56F),
                    New PointF(chartRect.Left + chartRect.Width * 0.78F, chartRect.Top + chartRect.Height * 0.50F),
                    New PointF(chartRect.Right, chartRect.Top + chartRect.Height * 0.32F)
                }

                Dim fillPoints As New List(Of PointF)(points)
                fillPoints.Add(New PointF(chartRect.Right, chartRect.Bottom))
                fillPoints.Add(New PointF(chartRect.Left, chartRect.Bottom))
                g.FillPolygon(fillBrush, fillPoints.ToArray())
                g.DrawLines(linePen, points)

                For Each p In points
                    g.FillEllipse(Brushes.White, p.X - 5, p.Y - 5, 10, 10)
                    g.DrawEllipse(Pens.Plum, p.X - 5, p.Y - 5, 10, 10)
                Next

                Dim months As String() = {"oct. 2022", "nov. 2022", "dec. 2022", "jan. 2023", "feb. 2023"}
                For i As Integer = 0 To months.Length - 1
                    Dim x = chartRect.Left + CInt((chartRect.Width / 4.0F) * i)
                    g.DrawString(months(i), New Font("Segoe UI", 9.0F, FontStyle.Regular), axisFontBrush, x - 24, chartRect.Bottom + 10)
                Next
            End Using
        End Sub

        ''' A menu tile is 122 x 96 with the caption below the picture, so the icon is given 56.
        Private Const MenuIconSize As Integer = 56

        Private Function LoadMenuIcon(fileName As String, fallback As Image) As Image
            Return IconScaler.Load(fileName, MenuIconSize, fallback)
        End Function

        Private Shared Function NormalizeRibbonImage(source As Image) As Image
            Return NormalizeRibbonImage(source, 42)
        End Function

        Private Shared Function NormalizeActionIcon(actionKey As String, source As Image) As Image
            Dim iconSize = 42
            Return NormalizeRibbonImage(source, iconSize)
        End Function

        Private Shared Function NormalizeRibbonImage(source As Image, iconSize As Integer) As Image
            If source Is Nothing Then
                Return Nothing
            End If

            Dim normalized As New Bitmap(42, 42)
            Using g = Graphics.FromImage(normalized)
                g.Clear(Color.Transparent)
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic

                Dim ratio = Math.Min(iconSize / Math.Max(1.0F, source.Width), iconSize / Math.Max(1.0F, source.Height))
                Dim drawWidth = CInt(source.Width * ratio)
                Dim drawHeight = CInt(source.Height * ratio)
                Dim left = (42 - drawWidth) \ 2
                Dim top = (42 - drawHeight) \ 2
                g.DrawImage(source, left, top, drawWidth, drawHeight)
            End Using

            Return normalized
        End Function

        Private Sub RibbonPanel_Resize(sender As Object, e As EventArgs)
            UpdateRibbonLayout()
        End Sub

        Private Sub UpdateRibbonLayout()
            rightPinnedActionsPanel.Left = ribbonPanel.ClientSize.Width - rightPinnedActionsPanel.Width - 10
            leftActionsFlow.Width = Math.Max(220, rightPinnedActionsPanel.Left - leftActionsFlow.Left - 12)
            LayoutPinnedActions()
            rightPinnedActionsPanel.BringToFront()
        End Sub

        Private Sub CloseMenu_Click(sender As Object, e As EventArgs)
            LogoutButton_Click(sender, e)
        End Sub

        Private Sub Clients_Click(sender As Object, e As EventArgs)
            Using contacts As New Entity_B(currentUser, activeAccessProfile)
                contacts.ShowDialog(Me)
            End Using
        End Sub

        Private Sub ApplicationSettings_Click(sender As Object, e As EventArgs)
            Dim session = SessionState.Current
            Dim isAppAdmin As Boolean = session.HasValue AndAlso session.Value.IsApplicationAdminRole
            Dim isCompanyAdmin As Boolean = session.HasValue AndAlso session.Value.IsCompanyAdminRole

            If isCompanyAdmin AndAlso Not isAppAdmin Then
                Using settings As New Dashboard_Company(currentUser, activeAccessProfile)
                    settings.ShowDialog(Me)
                End Using
                MenuFormInitializer.Configure(Me, currentUser, True)
                Return
            End If

            Using settings As New Dashboard_Application(currentUser, activeAccessProfile)
                settings.ShowDialog(Me)
            End Using
            MenuFormInitializer.Configure(Me, currentUser, True)
        End Sub

        Private Sub Users_Click(sender As Object, e As EventArgs)
            Using roles As New Roles_B(currentUser, activeAccessProfile, "ROLES")
                roles.ShowDialog(Me)
            End Using
            MenuFormInitializer.Configure(Me, currentUser, True)
        End Sub

        Private Sub MyProfile_Click(sender As Object, e As EventArgs)
            MessageBox.Show("Hook your Profile form here.", "Framework Menu", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub LoginAsSubstitute_Click(sender As Object, e As EventArgs)
            MessageBox.Show("Hook your substitute user workflow here.", "Framework Menu", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        ''' From the menu an administrator is handling the queue, not reporting against a page, so
        ''' they get the dashboard with the ticket counts. Everyone else gets the tickets they
        ''' raised. The Help Desk button on a page is the other route, and always about that page.
        Private Sub HelpDesk_Click(sender As Object, e As EventArgs)
            If HelpDeskLauncher.OpensSupportListing() Then
                Using page As New FW_HD_AdminDashboard_B(Nothing)
                    page.ShowDialog(Me)
                End Using
                Return
            End If

            Using page As New FW_HD_Issues_B()
                page.ShowDialog(Me)
            End Using
        End Sub

        Private Sub SelectRole_Click(sender As Object, e As EventArgs)
            Dim roles = GetAvailableSessionRoles()
            If roles.Count <= 1 Then
                UpdateRoleSelectionTile()
                Return
            End If

            Dim activeSession = SessionState.Current
            Dim currentRoleId As Integer = 0
            If activeSession.HasValue Then
                currentRoleId = activeSession.Value.RoleID
            End If

            Using selector As New FW_RoleSelection(roles, currentRoleId)
                Dim result = selector.ShowDialog(Me)
                If result <> DialogResult.OK OrElse selector.SelectedRole Is Nothing Then
                    Return
                End If

                Dim selectedRole = selector.SelectedRole
                activeSession = SessionState.Current
                If activeSession.HasValue Then
                    Dim companyAdminRoleId = activeSession.Value.CompanyAdminRoleID
                    If companyAdminRoleId <= 0 Then
                        companyAdminRoleId = DataAccess.GetCompanyAdminRoleId(activeSession.Value.RegistrationID)
                    End If

                    Dim companyAdminUserId = activeSession.Value.CompanyAdminUserID
                    If companyAdminUserId <= 0 Then
                        companyAdminUserId = DataAccess.GetCompanyAdminUserId(activeSession.Value.RegistrationID)
                    End If

                    Dim hdUserSupport = activeSession.Value.HDUserSupport
                    Dim hdApplicationSupport = activeSession.Value.HDApplicationSupport
                    If hdUserSupport <= 0 OrElse hdApplicationSupport <= 0 Then
                        DataAccess.GetHelpDeskRouting(activeSession.Value.RegistrationID, hdUserSupport, hdApplicationSupport)
                    End If

                    SessionState.StartSession(currentUser,
                                              activeSession.Value.RegistrationID,
                                              activeSession.Value.RegistrationName,
                                              activeSession.Value.Smarty_AuthID,
                                              activeSession.Value.Smarty_AuthToken,
                                              activeSession.Value.Smarty_EmbeddedKey,
                                              activeSession.Value.Smarty_UseEmbeddedKey,
                                              activeSession.Value.BusinessRuleType,
                                              selectedRole.RoleID,
                                              companyAdminRoleId,
                                              companyAdminUserId,
                                              hdUserSupport,
                                              hdApplicationSupport,
                                              selectedRole.RoleName,
                                              selectedRole.RoleType,
                                              selectedRole.IsApplicationAdmin,
                                              selectedRole.IsCompanyAdmin)
                    MenuFormInitializer.Configure(Me, currentUser, True)
                    UpdateRoleSelectionTile()
                End If
            End Using
        End Sub

        Private Sub LogoutButton_Click(sender As Object, e As EventArgs)
            Me.DialogResult = DialogResult.Cancel
            Me.Close()
        End Sub
    End Class
End Namespace
