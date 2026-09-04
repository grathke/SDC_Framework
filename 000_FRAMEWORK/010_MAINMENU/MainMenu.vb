Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class FW_MainMenu
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

        ''' <summary>
        ''' A ribbon tile. The hidden focus rectangle and the one suppressible click both come from
        ''' SuppressClickButton, which the dashboards' icons share - a tile that can be dragged has
        ''' to be able to swallow the click its own drop raises.
        ''' </summary>
        Private Class RibbonActionButton
            Inherits SuppressClickButton
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
        Private ReadOnly rightPinnedActionsPanel As FlowLayoutPanel
        Private ReadOnly headingLabel As Label
        Private ReadOnly welcomeLabel As Label
        Private ReadOnly userBadgeLabel As Label
        Private ReadOnly eodLabel As Label
        Private ReadOnly rfrLabel As Label
        Private ReadOnly contentLayout As TableLayoutPanel
        Private ReadOnly actionTilesByKey As Dictionary(Of String, ActionTile)
        Private ReadOnly regionShells As Dictionary(Of MenuRegion, RegionShell)
        Private arrangementController As RibbonTileArrangementController
        Private imageController As IconImageController
        ''' <summary>
        ''' One tile, and the space after it. Every tile in both panels is this size with this
        ''' margin, so the spacing across the whole ribbon is a single number rather than one value
        ''' on the left and another on the right.
        '''
        ''' 96 rather than the 122 these were: at 126 to a tile the left panel ran out of room at
        ''' six tiles and silently clipped the last one, because the panel neither wraps nor
        ''' scrolls. At 100 to a tile eight fit at the smallest window the form allows, and nine at
        ''' the default width - two and three spare against the six tiles in use. The pinned row
        ''' dropping to three tiles on 2026-09-04 bought the last of those.
        '''
        ''' Ask MovableTileCapacityAtMinimumWidth rather than counting from these by hand. The
        ''' figure that matters is the one at the narrowest allowed window, since a tile that fits
        ''' only at the default width disappears the moment somebody drags the window in.
        ''' </summary>
        Private Const TileWidth As Integer = 96
        Private Const TileHeight As Integer = 96
        Private Const TileMargin As Integer = 4
        Private Const TilePitch As Integer = TileWidth + TileMargin

        ''' The pinned row holds four tiles and is sized to them, so the last one finishes at the
        ''' panel edge instead of 12px short of it.
        Private Const PinnedTileCount As Integer = 4
        Private Const PinnedPanelWidth As Integer = PinnedTileCount * TilePitch

        ''' How far each panel sits from its end of the ribbon. The same on both sides, so the row
        ''' is inset evenly.
        Private Const PanelInset As Integer = 10

        Private Shared ReadOnly RibbonHoverBackColor As Color = Color.FromArgb(232, 245, 255)
        Private Shared ReadOnly RibbonHoverBorderColor As Color = Color.FromArgb(91, 161, 217)

        Public Sub New(user As UserContext)
            Me.New(user, Nothing)
        End Sub

        Public Sub New(user As UserContext, initializeMenu As Action(Of FW_MainMenu, UserContext))
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

            ' Not anchored Right. Its width is a whole number of tiles, worked out by
            ' LayoutRibbonPanels on every resize; a Right anchor would stretch it to a fractional
            ' tile between those calculations and the row would jitter as it resized.
            leftActionsFlow = New FlowLayoutPanel() With {
                .Location = New Point(PanelInset, 32),
                .Size = New Size(760, 102),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .AutoScroll = False,
                .Margin = New Padding(0)
            }

            ' A flow panel like the left one, so that a pinned tile hidden by a permission lets the
            ' rest close up behind it rather than leaving a hole. Its tiles are still fixed in place
            ' - no drag is wired here - and their order is set by LayoutPinnedActions.
            ' Not anchored Right either. It is placed where the flow panel ends, so the gap between
            ' the last movable tile and the first pinned one is the same TileMargin as every other
            ' gap in the ribbon.
            rightPinnedActionsPanel = New FlowLayoutPanel() With {
                .Location = New Point(ribbonPanel.Width - PinnedPanelWidth - PanelInset - 4, 32),
                .Size = New Size(PinnedPanelWidth, 102),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .AutoScroll = False,
                .BackColor = Color.Transparent,
                .Margin = New Padding(0)
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
            AddActionTile("dashboard", "Dashboard", AddressOf Dashboard_Click, LoadMenuIcon("dashboard.png", SystemIcons.Application.ToBitmap()))
            AddActionTile("application-settings", "Application" & Environment.NewLine & "Settings", AddressOf ApplicationSettings_Click, LoadMenuIcon("gear.png", SystemIcons.Shield.ToBitmap()))
            AddActionTile("users", "Users", AddressOf Users_Click, LoadMenuIcon("users.png", SystemIcons.Information.ToBitmap()))
            AddActionTile("my-profile", "My" & Environment.NewLine & "Profile", AddressOf MyProfile_Click, LoadMenuIcon("my-profile.png", SystemIcons.Question.ToBitmap()))
            AddActionTile("login-as-substitute", "Login as" & Environment.NewLine & "Different User", AddressOf LoginAsSubstitute_Click, LoadMenuIcon("substitute-user.png", SystemIcons.Warning.ToBitmap()))
            AddActionTile("select-role", "Select a Role (Application Admin)", AddressOf SelectRole_Click, LoadMenuIcon("users.png", SystemIcons.WinLogo.ToBitmap()))
            AddActionTile("help-desk", "Help" & Environment.NewLine & "Desk", AddressOf HelpDesk_Click, LoadMenuIcon("Color_Help_Desk.png", SystemIcons.Question.ToBitmap()))

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

            ' The role tile has just been rebuilt from source, picture included, so a chosen one
            ' goes back on top.
            ReapplyChosenIcons()
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

            ' Hiding a pinned tile does not raise a resize, so the panel would keep the width of
            ' the tile that just left and the row would sit off the ribbon's right edge. The whole
            ' ribbon is re-laid rather than just the panel, because the movable flow's width is
            ' measured from where the pinned panel ends and has just changed too.
            If IsPinnedActionKey(actionKey) AndAlso ribbonPanel IsNot Nothing AndAlso ribbonPanel.ClientSize.Width > 0 Then
                UpdateRibbonLayout()
            End If
        End Sub

        Public Sub SetAccessProfile(profile As AccessProfile)
            activeAccessProfile = profile
        End Sub

        ''' <summary>
        ''' Wires the ribbon's arrangement and chosen pictures, and re-applies them afterwards.
        '''
        ''' The surface name comes from the initializer rather than from a constant here, because
        ''' this form is the part that does not vary: one menu form can serve more than one
        ''' application, each supplying its own tiles through its own initializer. A name fixed in
        ''' here would make two applications share one arrangement. Required, with no default, so a
        ''' new initializer cannot inherit another application's ribbon by omission.
        '''
        ''' Called from the initializer, which runs six times over a session - every role change and
        ''' several dialog returns - so the controllers are created once and re-applied thereafter,
        ''' never re-wired.
        ''' </summary>
        Public Sub ConfigureArrangement(surfaceName As String, ParamArray anchoredKeys As String())
            If String.IsNullOrWhiteSpace(surfaceName) Then
                Return
            End If

            Dim session = SessionState.Current
            Dim sessionUserId = If(session.HasValue AndAlso session.Value.UserID > 0,
                                   session.Value.UserID,
                                   currentUser.UserId)

            If arrangementController Is Nothing Then
                arrangementController = New RibbonTileArrangementController(Me,
                                                                           leftActionsFlow,
                                                                           surfaceName,
                                                                           sessionUserId,
                                                                           If(anchoredKeys, New String() {}))
            End If

            If imageController Is Nothing Then
                imageController = New IconImageController(Me,
                                                          surfaceName,
                                                          sessionUserId,
                                                          Function(fileName, fallback) NormalizeRibbonImage(LoadMenuIcon(fileName, fallback)))
            End If

            arrangementController.Attach(FlowTiles())

            ' The pinned row cannot be dragged either, and to an App Admin it looks no different
            ' from the row that can, so it carries the same "Fixed position" tooltip.
            arrangementController.MarkFixedElsewhere(PinnedTiles())

            imageController.Attach(AllTiles())
        End Sub

        ''' The tiles that can be rearranged: the ones in the flow panel, and only those. The pinned
        ''' row is positioned by hand and stays where it is.
        Private Function FlowTiles() As List(Of KeyValuePair(Of String, Control))
            Return actionTilesByKey.
                Where(Function(entry) entry.Value.Button IsNot Nothing AndAlso
                                      entry.Value.Button.Parent Is leftActionsFlow).
                Select(Function(entry) New KeyValuePair(Of String, Control)(entry.Key, CType(entry.Value.Button, Control))).
                ToList()
        End Function

        ''' The pinned row: fixed in place, and told apart from the movable row only by its tooltip.
        Private Function PinnedTiles() As List(Of Control)
            Return actionTilesByKey.
                Where(Function(entry) entry.Value.Button IsNot Nothing AndAlso
                                      entry.Value.Button.Parent Is rightPinnedActionsPanel).
                Select(Function(entry) CType(entry.Value.Button, Control)).
                ToList()
        End Function

        ''' Every tile, pinned row included: a picture can be changed on any of them. Deliberate for
        ''' `close` too - it never moves and never hides, but its picture is the App Admin's.
        Private Function AllTiles() As List(Of KeyValuePair(Of String, ButtonBase))
            Return actionTilesByKey.
                Where(Function(entry) entry.Value.Button IsNot Nothing).
                Select(Function(entry) New KeyValuePair(Of String, ButtonBase)(entry.Key, CType(entry.Value.Button, ButtonBase))).
                ToList()
        End Function

        ''' <summary>
        ''' Lays a chosen picture back over one that has just been re-asserted from source.
        '''
        ''' Two places do that: the pinned row rewrites Help Desk's picture on every resize, and the
        ''' role tile is rebuilt on every role change. Without this an App Admin's choice would
        ''' revert the first time the window was resized or the role switched, which reads as the
        ''' feature not working rather than as something overwriting it.
        ''' </summary>
        Private Sub ReapplyChosenIcons()
            If imageController Is Nothing Then
                Return
            End If

            imageController.ApplySavedImages()
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
                .Size = New Size(TileWidth, TileHeight),
                .TextAlign = ContentAlignment.TopCenter,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .UseVisualStyleBackColor = False,
                .BackColor = Color.Transparent,
                .FlatStyle = FlatStyle.Flat,
                .Margin = New Padding(0, 0, TileMargin, 0),
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

            If IsPinnedActionKey(key) Then
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

        ''' <summary>
        ''' Fixes the order of the pinned row. The panel owns where each tile sits, so this sets an
        ''' index rather than a coordinate - which is also what lets a hidden tile close the gap
        ''' behind it.
        '''
        ''' It used to place them by hand and, while it was there, rewrite Help Desk's caption,
        ''' padding and picture on every pass. That rewrote exactly what AddActionTile had already
        ''' set, and since this runs on every ribbon resize it was also what wiped an App Admin's
        ''' chosen picture the moment the window was resized. Nothing else in the application
        ''' touches that tile, so the block was doing no work except the harm.
        ''' </summary>
        Private Sub LayoutPinnedActions()
            Dim orderedKeys As String() = {"my-profile", "login-as-substitute", "select-role", "help-desk"}
            Dim position As Integer = 0

            For Each key In orderedKeys
                Dim tile As ActionTile = Nothing
                If Not actionTilesByKey.TryGetValue(key, tile) Then
                    Continue For
                End If

                If tile.Button.Parent IsNot rightPinnedActionsPanel Then
                    Continue For
                End If

                rightPinnedActionsPanel.Controls.SetChildIndex(tile.Button, position)
                position += 1
            Next

            LayoutRibbonPanels()
        End Sub

        ''' <summary>
        ''' Shrinks the pinned panel to the tiles actually showing, and puts its right edge back
        ''' against the ribbon's.
        '''
        ''' The panel was a fixed four tiles wide. A flow panel packs its children to the left, so
        ''' hiding one left the gap at the **right** edge and the remaining tiles looked as though
        ''' they had slid away from the corner. Sizing the panel to what is in it moves the gap to
        ''' the left, where the empty ribbon already is, and the tiles stay in the corner.
        '''
        ''' Called on every visibility change as well as on resize, because hiding a tile does not
        ''' raise a resize.
        ''' </summary>
        ''' <summary>
        ''' Sizes and places both ribbon panels, because their geometry is one calculation and not
        ''' two: the flow panel's width depends on how wide the pinned panel is, and the pinned
        ''' panel's position depends on where the flow panel ends.
        '''
        ''' The flow panel is given a whole number of tiles of the room available, never a fraction,
        ''' and the pinned panel starts exactly where it ends. The gap between the last movable tile
        ''' and the first pinned one is then the same TileMargin as every other gap in the row, so
        ''' every button in the ribbon is spaced identically - which is only visible when the row is
        ''' full, and is the point of doing it this way.
        '''
        ''' The leftover pixels - never as much as one tile - collect to the right of the pinned
        ''' panel. They have to go somewhere, and the middle of the row is the one place a varying
        ''' gap would read as a mistake rather than as margin.
        ''' </summary>
        Private Sub LayoutRibbonPanels()
            If rightPinnedActionsPanel Is Nothing OrElse leftActionsFlow Is Nothing Then
                Return
            End If

            Dim visibleTiles As Integer = 0
            For Each control As Control In rightPinnedActionsPanel.Controls
                If control.Visible Then
                    visibleTiles += 1
                End If
            Next

            rightPinnedActionsPanel.Width = Math.Max(TilePitch, visibleTiles * TilePitch)

            ' Skipped while the form is still being built - ribbonPanel has no width yet, and the
            ' first resize runs this properly.
            If ribbonPanel Is Nothing OrElse ribbonPanel.ClientSize.Width <= 0 Then
                Return
            End If

            Dim roomForFlow = ribbonPanel.ClientSize.Width -
                              leftActionsFlow.Left -
                              PanelInset -
                              rightPinnedActionsPanel.Width

            leftActionsFlow.Width = Math.Max(1, roomForFlow \ TilePitch) * TilePitch
            rightPinnedActionsPanel.Left = leftActionsFlow.Left + leftActionsFlow.Width
        End Sub

        ''' <summary>
        ''' How many tiles the movable row can hold at the window's narrowest allowed size, which is
        ''' the only capacity worth quoting: a tile that fits today and is clipped when somebody
        ''' drags the window in has not fitted at all.
        '''
        ''' The flow panel does not wrap and does not scroll, so a tile past the end is not moved to
        ''' a second row or reachable by scrolling - it is simply not drawn, with nothing said.
        ''' </summary>
        Public Function MovableTileCapacityAtMinimumWidth() As Integer
            Dim pinnedWidth = If(rightPinnedActionsPanel Is Nothing, PinnedPanelWidth, rightPinnedActionsPanel.Width)
            Dim flowLeft = If(leftActionsFlow Is Nothing, PanelInset, leftActionsFlow.Left)

            ' The ribbon is inset 8 each side of the form and draws a one-pixel border.
            Dim narrowestRibbonClient = Me.MinimumSize.Width - 16 - 2
            Return Math.Max(1, (narrowestRibbonClient - flowLeft - PanelInset - pinnedWidth) \ TilePitch)
        End Function

        ''' <summary>
        ''' Tiles that live in the pinned panel on the right rather than the movable flow on the
        ''' left. Placement only - it says nothing about whether a tile can be hidden.
        '''
        ''' Split from IsAlwaysVisibleActionKey on 2026-09-03. One list had been answering two
        ''' questions, so taking a key out to let a permission hide it would have moved the tile to
        ''' the other side of the ribbon as well.
        ''' </summary>
        Private Shared Function IsPinnedActionKey(actionKey As String) As Boolean
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

        ''' <summary>
        ''' Tiles that stay on screen whatever visibility is asked for. ConfigureActionVisibility
        ''' honours the enabled flag for these and ignores the visible one.
        '''
        ''' They are the ways out of wherever the user is: their own profile, the role they are
        ''' working under, and the way to report that something is wrong. A permission that hid one
        ''' would strand somebody with no route back, so the tile is kept and disabled instead.
        '''
        ''' login-as-substitute was in this list until 2026-09-03 and is not a way out - it is an
        ''' administrator's action, offered only to an App Admin. Being here is what stopped
        ''' MenuFormInitializer hiding it: the visibility passed in was simply discarded.
        ''' </summary>
        Private Shared Function IsAlwaysVisibleActionKey(actionKey As String) As Boolean
            If String.IsNullOrWhiteSpace(actionKey) Then
                Return False
            End If

            Select Case actionKey.Trim().ToLowerInvariant()
                Case "my-profile", "select-role", "help-desk"
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

        ''' <summary>
        ''' The pinned panel is sized and placed first, then the movable flow takes whatever is left
        ''' beside it. In that order, because the flow's width is measured from where the pinned
        ''' panel ends - measuring before it moves leaves the flow a cycle behind, which shows up
        ''' as a stale gap the first time a pinned tile is hidden.
        ''' </summary>
        ''' The flow panel's width used to be set here, from wherever the pinned panel had ended up.
        ''' Both panels are now placed by one calculation in LayoutRibbonPanels, which
        ''' LayoutPinnedActions runs - setting the width again here would undo it, and would put back
        ''' the fractional tile that left an uneven gap in the middle of a full row.
        Private Sub UpdateRibbonLayout()
            LayoutPinnedActions()
            rightPinnedActionsPanel.BringToFront()
        End Sub

        Private Sub CloseMenu_Click(sender As Object, e As EventArgs)
            LogoutButton_Click(sender, e)
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

        ''' <summary>
        ''' Placeholder. The tile exists so that it holds its place in the row - anchored between
        ''' Close and Application Settings, hideable by permission, and moving the tiles to its
        ''' right when it is hidden - but it is not wired to anything yet.
        '''
        ''' When it is, it will load an internal page into one of the regions below rather than
        ''' opening a dialog, which is what every other ribbon tile does. That makes it the first
        ''' region-loading action, so it needs its own decisions about which region and what
        ''' content; a dialog stubbed in here now would be the wrong shape to grow from.
        ''' </summary>
        Private Sub Dashboard_Click(sender As Object, e As EventArgs)
            MessageBox.Show(Me,
                            "The Dashboard is not wired up yet. It will display an internal page in the panels below.",
                            "Dashboard",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
        End Sub

        Private Sub MyProfile_Click(sender As Object, e As EventArgs)
            MessageBox.Show("Hook your Profile form here.", "Framework Menu", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub LoginAsSubstitute_Click(sender As Object, e As EventArgs)
            MessageBox.Show("Hook your substitute user workflow here.", "Framework Menu", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        ''' <summary>
        ''' The two actions above, reachable from a tile's drop-down menu as well as from its button.
        '''
        ''' They exist so a menu item can invoke the action rather than repeat it. Application
        ''' Settings in particular decides between the Application and Company dashboards by role,
        ''' and a menu item that opened Dashboard_Application directly would be a second copy of
        ''' that decision - correct on the day it was written and wrong the first time the rule
        ''' changed in only one of them.
        '''
        ''' Deliberately not a general "invoke this action key" method. The tile's own Click is what
        ''' opens the menu, so invoking the tile from inside its own menu would reopen it.
        ''' </summary>
        Public Sub OpenApplicationSettings()
            ApplicationSettings_Click(Me, EventArgs.Empty)
        End Sub

        Public Sub OpenSubstituteUser()
            LoginAsSubstitute_Click(Me, EventArgs.Empty)
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
