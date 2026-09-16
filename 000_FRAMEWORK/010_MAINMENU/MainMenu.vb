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

        ''' <summary>
        ''' The panels of the menu body.
        '''
        ''' RegionLeft is named for where it is; the rest are still named for what they hold. That
        ''' is deliberate and temporary. A region's occupant changes - RegionLeft shows Messages to
        ''' a role that may read them and Overview to one that may not - so a content name stops
        ''' being true the moment a region can swap. Position does not change.
        '''
        ''' The others keep their content names until the layout is settled: the right-hand cell
        ''' holds two regions side by side rather than one, and whether they end up left/right or
        ''' top/bottom decides what they should be called. Renaming them twice is worse than
        ''' leaving them for now.
        ''' </summary>
        Public Enum MenuRegion
            RegionLeft
            GeneralDashboard
            AcmeDashboard
            UsersAndLists
            Chart

            ''' <summary>
            ''' The whole content area, holding one picture. It is the only occupant of the Home
            ''' layout and appears in no other, which is what lets it be the full width and height
            ''' rather than a cell of the grid.
            ''' </summary>
            Home
        End Enum

        Private Class ActionTile
            Public Property Key As String
            Public Property Button As Button
            Public Property OnClick As EventHandler

            ''' The page this tile opens, or nothing.
            '''
            ''' Given only to a tile whose caption should follow the page. A tile without one keeps
            ''' the caption whoever asked for the button chose, whatever any override says - Close,
            ''' Admin and My Profile among them. That is the final word in the order, and saying
            ''' nothing here is how it is said.
            Public Property PageName As String

            ''' The caption the tile was given in code, kept so an override can be applied *and
            ''' withdrawn*. Without it, a role change that removes an override would have nothing to
            ''' put back and the previous role's wording would stick.
            Public Property DefaultCaption As String
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

        ''' <summary>
        ''' How often to look for new messages. **Temporary** - it belongs on the registration and
        ''' user-adjustable from there, because the right number depends on the site: a support
        ''' desk wants a minute, a two-person office does not want the traffic. Five minutes is a
        ''' placeholder chosen to be cheap rather than right.
        ''' </summary>
        Private Const MessageCheckIntervalMs As Integer = 5 * 60 * 1000

        ''' How far below the registration name's top edge the asterisk sits. It used to be six
        ''' pixels *above* that edge; +8 was tried and overshot, so this is half that move.
        Private Const MessageMarkerDrop As Integer = 1

        ''' <summary>
        ''' The shortest gap between two checks, which only bites on Activated. Coming back to the
        ''' menu should refresh the marker at once, but the menu is activated by every alt-tab and
        ''' every closed dialog, and without a floor a user clicking about would fire a query per
        ''' click.
        ''' </summary>
        Private Const MessageCheckMinGapMs As Integer = 15 * 1000

        ''' One timer for the session, not one per open page. Owned here because this form is the
        ''' one alive from sign-in to sign-out.
        Private messageCheckTimer As Timer

        ''' When the last check actually ran, for MessageCheckMinGapMs. Never written by a skipped
        ''' check, so a run of skips does not push the next real one further away.
        Private lastMessageCheckUtc As DateTime = DateTime.MinValue

        Private ReadOnly titleLabel As Label
        Private ReadOnly ribbonPanel As Panel
        Private ReadOnly leftActionsFlow As FlowLayoutPanel
        Private ReadOnly rightPinnedActionsPanel As FlowLayoutPanel
        Private ReadOnly headingLabel As Label
        Private ReadOnly timeZoneOverrideCombo As ComboBox
        Private ReadOnly timeZoneOverrideCaption As Label
        Private ReadOnly newMessageMarker As Label
        Private ReadOnly welcomeLabel As Label
        ''' <summary>
        ''' The area under the ribbon. It holds every layout, one of which is showing.
        '''
        ''' The layouts are built once and shown or hidden, rather than one grid being rebuilt each
        ''' time. A region therefore belongs to exactly one layout and keeps whatever was loaded
        ''' into it - pressing Home and coming back does not re-read anything, which is the whole
        ''' reason a swap costs no database round trip.
        ''' </summary>
        Private ReadOnly contentHost As Panel

        ''' <summary>
        ''' The layouts by name, and which is up. A name rather than an enum because a project
        ''' registers its own - MenuFormInitializer decides what this application's menu offers,
        ''' the same way it already decides which tiles the ribbon has.
        ''' </summary>
        Private ReadOnly menuLayouts As Dictionary(Of String, Control)
        Private currentLayoutName As String = String.Empty

        ''' <summary>The Home layout's picture, kept so the graphic can be replaced without rebuilding it.</summary>
        Private homePicture As PictureBox

        ''' <summary>The arrangement Home returns to, and the one every region-loading tile needs showing.</summary>
        Public Const DashboardsLayoutName As String = "Dashboards"
        Public Const HomeLayoutName As String = "Home"

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
        ''' The figure that matters is the one at the narrowest allowed window, since a tile that
        ''' fits only at the default width disappears the moment somebody drags the window in - the
        ''' panel neither wraps nor scrolls, so it is not moved or reachable, it is simply not drawn.
        ''' That figure is MovableTileCapacityAtMinimumWidth below, and PageGenerator asks it.
        '''
        ''' A function of that name was deleted on 2026-09-04 for measuring live controls with a
        ''' sixteen-pixel error, leaving PageGenerator holding the answer as the constant 8 while
        ''' every value deciding it stayed here. It is back on 2026-09-05 as arithmetic on these
        ''' constants, which is what the generator can actually ask with no form instantiated.
        '''
        ''' </summary>
        Private Const TileWidth As Integer = 96

        ''' <summary>
        ''' A tile is as tall as what is in it: a 42-pixel icon at the top, two lines of 9.5pt
        ''' caption at the bottom - a line measures 17 - and a gap between them. 86, where it was
        ''' 96.
        '''
        ''' Two lines, because the captions are written that way: "Application" and "Settings" on
        ''' separate lines, by an explicit newline in the text.
        '''
        ''' The gap is what the last six pixels buy, and they are not slack. The caption is pinned
        ''' to the bottom, so a second line grows upward into the icon: at 80 the one-line tiles
        ''' looked right and every two-line one had its first line against the graphic.
        '''
        ''' The icon is untouched. NormalizeActionIcon draws every graphic into a 42x42 bitmap at
        ''' its own aspect ratio, and that is unchanged: this shortens the button, not the picture.
        ''' Width is unchanged too, because MovableTileCapacityAtMinimumWidth is arithmetic on
        ''' TileWidth and the generator asks it how many tiles fit.
        ''' </summary>
        Private Const TileHeight As Integer = 86
        Private Const TileMargin As Integer = 4
        Private Const TilePitch As Integer = TileWidth + TileMargin

        ''' <summary>
        ''' The pinned row holds three tiles and is sized to them, so the last one finishes at the
        ''' panel edge instead of short of it.
        '''
        ''' Three, not four, since 2026-09-05. login-as-substitute was counted here long after its
        ''' button went on 2026-09-04 - the action moved into the Application Settings drop-down and
        ''' the tile stayed registered only so the menu item could invoke its handler, hidden
        ''' unconditionally by MenuFormInitializer. It is still pinned by IsPinnedActionKey and still
        ''' excluded from PageGenerator's movable count, both of which keep a hidden tile out of the
        ''' movable row; what it must not do any longer is reserve 100px that nothing occupies.
        '''
        ''' That reservation cost a whole tile: the row is 100px wider in practice than the capacity
        ''' figure assumed, so MovableTileCapacityAtMinimumWidth reported 8 where 9 fit.
        ''' </summary>
        Private Const PinnedTileCount As Integer = 3
        Private Const PinnedPanelWidth As Integer = PinnedTileCount * TilePitch

        ''' How far each panel sits from its end of the ribbon. The same on both sides, so the row
        ''' is inset evenly.
        Private Const PanelInset As Integer = 10

        ''' <summary>
        ''' The narrowest the window may be dragged. Read by MovableTileCapacityAtMinimumWidth as
        ''' well as by MinimumSize, so the two cannot say different things.
        '''
        ''' Raised from 1180 to 1260 on 2026-09-05 to buy an eighth movable tile. Eight tiles need
        ''' 800px of flow, and the flow gets the window width less 52 for the borders and insets and
        ''' less the pinned row's 400 - so 1252 is the least that works and this is 1260 for slack.
        '''
        ''' Chosen against a laptop rather than a desktop. A 1366-wide screen leaves roughly 1350 of
        ''' browser viewport under Thinfinity, so this still clears it by about 90px. Width was never
        ''' the tight dimension there; the 760 height is the one that does not fit a 768-tall screen,
        ''' and raising this does not make that worse.
        ''' </summary>
        Private Const MinimumWindowWidth As Integer = 1260

        ''' What the ribbon loses before any of it is usable for tiles: the window's own border,
        ''' then ribbonPanel's 8px margin at each side, then its FixedSingle border of one pixel a
        ''' side. Named rather than folded into one number so a change to any of them is findable.
        Private Const WindowBorderWidth As Integer = 16

        ''' <summary>
        ''' The band above the ribbon that carries the application's name.
        '''
        ''' Everything below it moves down by this much and the window loses the same, so the
        ''' ribbon keeps its height and its tiles exactly as they were - only their position on the
        ''' page changes. One number, so the band and the shift cannot disagree.
        ''' </summary>
        Private Const TitleBandHeight As Integer = 44

        ''' <summary>
        ''' The ribbon, and what the page does with the space it gave back.
        '''
        ''' The panel inset above the tiles, the tile flow, and 6 below - 104 where it was 140.
        '''
        ''' LayoutShift is what everything below the ribbon moves by: down for the title band, up
        ''' for the shorter ribbon. The window loses both, so the dashboard area below keeps exactly
        ''' the height it had.
        ''' </summary>
        Private Const RibbonFlowHeight As Integer = TileHeight + 6
        ''' <summary>
        ''' The tiles start here. It was 30, clearing a "Main" tab label at the top left that was
        ''' removed on 2026-09-10 - the ribbon has one tab and never named the others, so the label
        ''' titled nothing. With it gone the row moves up to the panel's own inset.
        ''' </summary>
        Private Const RibbonTopInset As Integer = 6
        Private Const RibbonHeight As Integer = RibbonTopInset + RibbonFlowHeight + 6
        Private Const RibbonTrim As Integer = 140 - RibbonHeight
        Private Const LayoutShift As Integer = TitleBandHeight - RibbonTrim

        ''' <summary>
        ''' What the title band says: the application's name, taken from the assembly.
        '''
        ''' Read rather than typed, so a second application built on this framework shows its own
        ''' name without editing the shell - the same boundary MenuFormInitializer draws for the
        ''' tiles.
        '''
        ''' Separators become spaces and each word is title cased, which is DisplayNameFormatter's
        ''' job and not repeated here: an acronym that title casing would flatten belongs in its
        ''' list rather than in a special case at this call site. A dot is turned into the same
        ''' separator first, because an assembly name is dotted and a dot reads as a file extension
        ''' in a heading.
        '''
        ''' An acronym that title casing would flatten - SDC into Sdc - is handled by adding it to
        ''' DisplayNameFormatter's list, which is what CLAUDE.md asks for and what makes it come out
        ''' right everywhere else it appears too. A rule here about how many letters a first word
        ''' has would be a second formatter in all but name, and would still be wrong for the first
        ''' product whose initials run to four.
        ''' </summary>
        Private Shared Function ResolveApplicationTitle() As String
            Dim name = Application.ProductName
            If String.IsNullOrWhiteSpace(name) Then
                name = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name
            End If
            If String.IsNullOrWhiteSpace(name) Then Return "Main Menu"

            Dim normalized = name.Trim().Replace("."c, "_"c)
            Return DisplayNameFormatter.ToDisplayName(normalized, stripFrameworkPrefix:=False)
        End Function
        Private Const RibbonPanelMargin As Integer = 16
        Private Const RibbonPanelBorder As Integer = 2

        ''' <summary>
        ''' How many movable tiles fit at the narrowest window the form allows, with every pinned
        ''' tile showing.
        '''
        ''' One owner for a number that used to be written down twice. PageGenerator held 8 as a
        ''' constant while every value that decides it - tile pitch, panel inset, pinned tile count,
        ''' minimum width - lives here. Nothing failed when they disagreed; the ribbon simply
        ''' stopped drawing a tile.
        '''
        ''' Counted against the pinned tiles that actually appear. The panel neither wraps nor
        ''' scrolls, so a tile that does not fit is not moved and not reachable, it is simply not
        ''' drawn - which makes over-counting the pinned row expensive in exactly one direction:
        ''' every 100px reserved for a tile nobody sees is a movable tile nobody gets.
        '''
        ''' Asked before any form exists - the generator is a development tool with no menu to
        ''' query - so this is arithmetic on the constants rather than a measurement of live
        ''' controls. It mirrors LayoutRibbonPanels, which does the same sum against real widths.
        ''' </summary>
        Public Shared Function MovableTileCapacityAtMinimumWidth() As Integer
            Dim ribbonClientWidth = MinimumWindowWidth - WindowBorderWidth - RibbonPanelMargin - RibbonPanelBorder
            Dim roomForFlow = ribbonClientWidth - (PanelInset * 2) - PinnedPanelWidth

            Return Math.Max(0, roomForFlow \ TilePitch)
        End Function

        Private Shared ReadOnly RibbonHoverBackColor As Color = Color.FromArgb(232, 245, 255)
        Private Shared ReadOnly RibbonHoverBorderColor As Color = Color.FromArgb(91, 161, 217)

        Public Sub New(user As UserContext)
            Me.New(user, Nothing)
        End Sub

        Public Sub New(user As UserContext, initializeMenu As Action(Of FW_MainMenu, UserContext))
            currentUser = user

            Me.Text = "Main Menu"
            ' CenterScreen, not CenterParent. The parent is the login form, so the menu inherited
            ' wherever that happened to be and was only centred if the login form was.
            '
            ' Under Thinfinity the screen is the browser session surface, so this centres the menu
            ' in the browser rather than on a desktop that is not there. It centres vertically too,
            ' which is wanted for the same reason.
            Me.StartPosition = FormStartPosition.CenterScreen
            Me.FormBorderStyle = FormBorderStyle.Sizable
            Me.MaximizeBox = True

            ' No minimise box in a browser. Minimising puts the window somewhere the session has no
            ' taskbar to bring it back from, so the button is a way to lose the application inside
            ' its own tab. Windows will not hide one box on its own - a form with a maximise box
            ' and no minimise box draws the minimise button greyed rather than absent - so the
            ' maximise box goes with it, and maximising stays available by double-clicking the
            ' caption or dragging the window to the top of the session.
            If Program.InBrowserSession Then
                Me.MinimizeBox = False
                Me.MaximizeBox = False
            Else
                Me.MinimizeBox = True
                Me.MaximizeBox = True
            End If

            ' No grip in the corner. The default is Auto, which draws one whenever a form is shown
            ' as a dialog - and the menu is, through LoginForm's ShowDialog - so the shell carried a
            ' dialog's furniture without anyone asking for it.
            '
            ' Hide removes the handle, not the resizing: the borders and the maximise box still
            ' work, and the ribbon still lays itself out on every resize.
            Me.SizeGripStyle = SizeGripStyle.Hide
            Me.MinimumSize = New Size(MinimumWindowWidth, 760)
            ' Opens at exactly its narrowest, so the ribbon shows its durable layout from the first
            ' moment rather than one that shrinks under the user. The width is MinimumWindowWidth
            ' less the 16px the window border takes, so the two move together; it was 1280 against a
            ' 1260 minimum, leaving 36px of play that showed nothing extra because the row is capped
            ' at the slots that survive the narrowest window anyway.
            '
            ' The height still has 40px of play. It is left alone deliberately: 760 is already taller
            ' than a 768-high laptop's browser viewport, and that wants measuring rather than
            ' guessing before anything here moves.
            Me.ClientSize = New Size(MinimumWindowWidth - WindowBorderWidth, 800 - TitleBandHeight - RibbonTrim)

            ' In a browser the shell is one size and the browser does the scaling. VirtualUI's "fit
            ' to browser window" grows the whole canvas with the tab, which enlarges the menu
            ' without re-laying it out - where resizing the window instead leaves the pinned tiles
            ' anchored to a right edge that has moved, and the ribbon opens up a gap across the
            ' middle.
            '
            ' MaximumSize rather than only hiding the maximise box, because the box is not the only
            ' way to maximise: double-clicking the caption or dragging the window to the top of the
            ' session does it too. With minimum and maximum equal there is no size to argue about.
            If Program.InBrowserSession Then
                Me.MaximumSize = Me.Size

                ' And no taskbar button. The window is invisible on the desktop, so the button
                ' restores nothing - it advertises a window that cannot be shown, for the whole
                ' life of the session and for the two or three minutes the process lingers after
                ' the browser has gone.
                Me.ShowInTaskbar = False
            End If

            Me.BackColor = Color.White

            ' The application's name, from the assembly rather than typed here, so a second
            ' application built on this framework carries its own without editing the shell.
            titleLabel = New Label() With {
                .AutoSize = False,
                .Location = New Point(8, 6),
                .Size = New Size(Me.ClientSize.Width - 16, TitleBandHeight - 10),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Text = ResolveApplicationTitle(),
                .TextAlign = ContentAlignment.MiddleCenter,
                .Font = New Font("Segoe UI", 18.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(52, 60, 70),
                .BackColor = Color.White
            }
            Me.Controls.Add(titleLabel)

            ribbonPanel = New Panel() With {
                .Location = New Point(8, 8 + TitleBandHeight),
                .Size = New Size(Me.ClientSize.Width - 16, RibbonHeight),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .BackColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle
            }

            ' Not anchored Right. Its width is set by LayoutRibbonPanels on every resize, to end
            ' exactly where the pinned row begins; a Right anchor would stretch it between those
            ' calculations and the row would jitter as it resized.
            leftActionsFlow = New FlowLayoutPanel() With {
                .Location = New Point(PanelInset, RibbonTopInset),
                .Size = New Size(760, RibbonFlowHeight),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
                .WrapContents = False,
                .FlowDirection = FlowDirection.LeftToRight,
                .AutoScroll = False,
                .Margin = New Padding(0)
            }

            ' A flow panel like the left one, so that a pinned tile hidden by a permission lets the
            ' rest close up behind it rather than leaving a hole. Its tiles are still fixed in place
            ' - no drag is wired here - and their order is set by LayoutPinnedActions.
            ' Not anchored Right either, for the same jitter reason as the flow panel, but
            ' LayoutRibbonPanels does place it against the right-hand edge - PanelInset from it, the
            ' same inset the flow panel has on the left, so the ribbon is evenly inset at both ends.
            rightPinnedActionsPanel = New FlowLayoutPanel() With {
                .Location = New Point(ribbonPanel.Width - PinnedPanelWidth - PanelInset - 4, RibbonTopInset),
                .Size = New Size(PinnedPanelWidth, RibbonFlowHeight),
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
                .Location = New Point(48, 158 + LayoutShift),
                .Text = registrationNameForHeader & " (" & registrationIdForHeader.ToString() & ")",
                .Font = New Font("Segoe UI", 20.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(24, 45, 78)
            }

            ' A session-only time zone, on the registration name's baseline and under the pinned
            ' row. Nothing is stored: it exists for somebody working away from their usual zone,
            ' and signing in again returns to the employee's or the registration's.
            '
            ' The whole list, unlike the registration and employee settings. Those name a business
            ' location and are kept to the US; this one answers "where am I today".
            timeZoneOverrideCaption = New Label() With {
                .Name = "Label_SessionTimeZone",
                .Text = "Time Zone:",
                .AutoSize = True,
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .ForeColor = Color.DimGray,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Regular),
                .Location = New Point(Me.ClientSize.Width - 380, 162 + LayoutShift)
            }

            timeZoneOverrideCombo = New ComboBox() With {
                .Name = "ComboBox_SessionTimeZone",
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Size = New Size(260, 26),
                .Location = New Point(Me.ClientSize.Width - 300, 158 + LayoutShift),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Regular),
                .TabStop = False
            }

            ' Left of the registration name, and big enough to be seen without being looked for -
            ' 26pt against the name's 20. Red is unused elsewhere on this part of the page, so it
            ' means one thing here.
            '
            ' Decided since: a FW_RoleDetails permission on FW_Messages. The registration's
            ' AllowMessaging flag was the other candidate and was removed on 2026-09-15 - nothing
            ' ever read it, and two switches for one question is how they end up disagreeing.
            ' Horizontal position unchanged - x = 18, where it has always been. Only the vertical
            ' moves: an asterisk is drawn in the upper part of its em box, being a superscript
            ' glyph by design, so sitting it level with the registration name's top put the mark
            ' up by the letter's cap height rather than beside its middle.
            '
            ' MessageMarkerDrop is the one number to change if it still looks off.
            newMessageMarker = New Label() With {
                .AutoSize = True,
                .Location = New Point(18, headingLabel.Top + MessageMarkerDrop),
                .Text = "*",
                .Font = New Font("Segoe UI", 26.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(196, 43, 43),
                .Visible = False
            }

            welcomeLabel = New Label() With {
                .AutoSize = False,
                .Location = New Point(52, 198 + LayoutShift),
                .Size = New Size(540, 28),
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Regular),
                .Text = "Welcome " & welcomeName & " (" & welcomeUserId.ToString() & ")"
            }


            ' The host carries the position and the anchoring that contentLayout used to. Every
            ' layout inside it docks to fill, which is what keeps them all the same size as each
            ' other without any of them repeating the arithmetic.
            contentHost = New Panel() With {
                .Location = New Point(24, 236 + LayoutShift),
                .Size = New Size(Me.ClientSize.Width - 48, Me.ClientSize.Height - 260 - LayoutShift),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom,
                .BackColor = Color.White,
                .Padding = New Padding(0),
                .Margin = New Padding(0)
            }

            contentLayout = New TableLayoutPanel() With {
                .Dock = DockStyle.Fill,
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
            regionShells(MenuRegion.RegionLeft) = CreateRegionShell("Messages")
            regionShells(MenuRegion.GeneralDashboard) = CreateRegionShell("General Dashboard")
            regionShells(MenuRegion.AcmeDashboard) = CreateRegionShell("Acme Dashboard")
            regionShells(MenuRegion.UsersAndLists) = CreateRegionShell("Users & Lists")
            regionShells(MenuRegion.Chart) = CreateRegionShell("Evolution of Acme Products", True)

            contentLayout.Controls.Add(regionShells(MenuRegion.RegionLeft).RootPanel, 0, 0)
            contentLayout.SetRowSpan(regionShells(MenuRegion.RegionLeft).RootPanel, 2)
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

            ' The Home layout: one cell, the whole area, one picture. A layout rather than a panel
            ' toggled on top of the grid, because every other arrangement this menu grows will be
            ' registered the same way and there is no reason for the first one to be special.
            homePicture = New PictureBox() With {
                .Dock = DockStyle.Fill,
                .SizeMode = PictureBoxSizeMode.Zoom,
                .BackColor = Color.White
            }
            regionShells(MenuRegion.Home) = CreateRegionShell("Home")
            regionShells(MenuRegion.Home).ContentHost.Controls.Add(homePicture)

            Dim homeLayout As New TableLayoutPanel() With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 1,
                .RowCount = 1,
                .BackColor = Color.White,
                .Padding = New Padding(0),
                .Margin = New Padding(0)
            }
            homeLayout.GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            homeLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            homeLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            homeLayout.Controls.Add(regionShells(MenuRegion.Home).RootPanel, 0, 0)

            ' No caption bar. The picture is the whole point of the region, and a title strip above
            ' it would be a heading over a banner that already says what it is.
            menuLayouts = New Dictionary(Of String, Control)(StringComparer.OrdinalIgnoreCase)
            RegisterMenuLayout(DashboardsLayoutName, contentLayout)
            RegisterMenuLayout(HomeLayoutName, homeLayout)
            SetRegionChrome(MenuRegion.Home, False)

            ' Applied here rather than left to the project, because every layout is registered
            ' hidden and a menu whose configuration never ran would otherwise open on a blank
            ' panel. A project wanting to open somewhere else calls ApplyMenuLayout itself; this is
            ' the floor, not the policy.
            ApplyMenuLayout(HomeLayoutName)

            actionTilesByKey = New Dictionary(Of String, ActionTile)(StringComparer.OrdinalIgnoreCase)

            ' First tile, because it is where the menu opens and the way back to it. The region
            ' selectors below all switch to the Dashboards layout as part of what they do, which
            ' means Home is the only tile whose whole job is the arrangement rather than an
            ' occupant of one.
            AddActionTile("layout-home", "Home", AddressOf ShowHomeLayout_Click, LoadMenuIcon("Color_Home.png", SystemIcons.Application.ToBitmap()))

            ' Messages sits here and nowhere else. It is a fixed position, not a movable tile the
            ' user arranges - when messaging is not permitted the tile is absent and the flow
            ' closes up behind it, which is the whole signal that messaging is off.
            '
            ' A selector, not a toggle: a toggle only has a meaning while there are exactly two
            ' occupants, and this region is expected to gain more. Pressed while messages are
            ' already showing it re-reads the folder instead.
            AddActionTile("region-messages", "Messages", AddressOf ShowMessagesRegion_Click, LoadMenuIcon("Color_Information.png", SystemIcons.Information.ToBitmap()))
            AddActionTile("application-settings", "Application" & Environment.NewLine & "Settings", AddressOf ApplicationSettings_Click, LoadMenuIcon("gear.png", SystemIcons.Shield.ToBitmap()))

            ' Overview is the default occupant of that region, and this tile is how it is reached
            ' while something else is showing. Every region always has an occupant, so the Messages
            ' tile always has something to come back to and the cell is never a blank third of the
            ' page - and this is what sits there when messaging is switched off.
            AddActionTile("region-overview", "QDesk", AddressOf ShowOverviewRegion_Click, LoadMenuIcon("Color_Search.png", SystemIcons.Application.ToBitmap()))

            ' The "users" tile was removed on 2026-09-06. It was captioned Users and opened Roles_B,
            ' which is a mislabelled tile rather than a missing feature: Roles_B is reached from the
            ' App Admin and Company dashboards, so nothing became unreachable. Found while wiring
            ' caption overrides - the role's alias for FW_Roles would have re-captioned it "Roles",
            ' correcting the label and making the mismatch obvious.
            ' Only where the company allows it. Not added rather than added and disabled: the
            ' tiles flow, so the row closes up and an absent tile reads as a feature this company
            ' does not have, where a greyed one reads as something broken.
            If SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso SessionState.Current.Value.AllowUpdateMyProfile Then
                AddActionTile("my-profile", "My" & Environment.NewLine & "Profile", AddressOf MyProfile_Click, LoadMenuIcon("my-profile.png", SystemIcons.Question.ToBitmap()))
            End If
            AddActionTile("login-as-substitute", "Login as" & Environment.NewLine & "Different User", AddressOf LoginAsSubstitute_Click, LoadMenuIcon("substitute-user.png", SystemIcons.Warning.ToBitmap()))
            AddActionTile("select-role", "Select a Role (Application Admin)", AddressOf SelectRole_Click, LoadMenuIcon("users.png", SystemIcons.WinLogo.ToBitmap()))
            AddActionTile("help-desk", "Help" & Environment.NewLine & "Desk", AddressOf HelpDesk_Click, LoadMenuIcon("Color_Help_Desk.png", SystemIcons.Question.ToBitmap()))

            AddHandler ribbonPanel.Resize, AddressOf RibbonPanel_Resize

            ribbonPanel.Controls.Add(leftActionsFlow)
            ribbonPanel.Controls.Add(rightPinnedActionsPanel)

            Me.Controls.Add(ribbonPanel)
            Me.Controls.Add(headingLabel)
            Me.Controls.Add(timeZoneOverrideCaption)
            Me.Controls.Add(timeZoneOverrideCombo)
            timeZoneOverrideCaption.BringToFront()
            Me.Controls.Add(newMessageMarker)
            Me.Controls.Add(welcomeLabel)
            Me.Controls.Add(contentHost)

            ConfigureActionVisibility("application-settings", True, True)
            ConfigureActionVisibility("login-as-substitute", True, True)
            ConfigureActionVisibility("select-role", True, True)

            SetTimezoneControlsVisible(False)
            LoadTimeZoneOverride()
            LoadSamplePlaceholders()
            UpdateRibbonLayout()

            If initializeMenu IsNot Nothing Then
                initializeMenu(Me, currentUser)
            End If

            UpdateRoleSelectionTile()

            ' Seated again once the form has been laid out. Doing it only during construction put
            ' the caption where the combo was going to be rather than where it ended up.
            ' F9 larger, F10 smaller, F8 back to normal. On Load rather than Shown: a zoom applied
            ' after the window is up is seen to jump, and a remembered one has to be what the page
            ' opens as.
            AddHandler Me.Load, Sub(sender, e) PageZoom.Attach(Me)

            AddHandler Me.Shown,
                Sub(sender, e)
                    SeatTimeZoneCaption()

                    ' Nothing on this screen asks for the keyboard. Without this the time zone
                    ' combo took focus simply by being the only control that could, and a focused
                    ' combo paints its box highlighted - which read as a choice made here.
                    Me.ActiveControl = Nothing
                End Sub
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

        ''' <summary>
        ''' What is in a region now, or Nothing. Lets a caller ask before replacing, which is how
        ''' the ribbon's region selectors avoid rebuilding what is already on screen.
        ''' </summary>
        Public Function GetRegionContent(region As MenuRegion) As Control
            Dim shell = GetRegionShell(region)
            If shell Is Nothing OrElse shell.ContentHost Is Nothing Then Return Nothing
            If shell.ContentHost.Controls.Count = 0 Then Return Nothing

            Return shell.ContentHost.Controls(0)
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
            StartMessageChecks()
        End Sub

        ''' <summary>
        ''' Starts, restarts or stops the new-message check according to the current role.
        '''
        ''' Called whenever the profile changes, which is also when the answer can change - a role
        ''' switch can grant or remove messaging. A role that may not read FW_Messages is not
        ''' polled at all: there is nothing to find and no reason to spend the query.
        ''' </summary>
        Private Sub StartMessageChecks()
            Dim canUseMessaging = activeAccessProfile IsNot Nothing AndAlso
                                  activeAccessProfile.Can("FW_Messages", AccessCapability.Read)

            If Not canUseMessaging Then
                If messageCheckTimer IsNot Nothing Then messageCheckTimer.Stop()
                SetNewMessageIndicator(False)
                Return
            End If

            If messageCheckTimer Is Nothing Then
                messageCheckTimer = New Timer() With {.Interval = MessageCheckIntervalMs}
                AddHandler messageCheckTimer.Tick, AddressOf MessageCheckTimer_Tick

                ' Wired once, with the timer, and left wired: the check inside answers whether
                ' messaging is permitted, so a role change needs no rewiring here.
                AddHandler Me.Activated, AddressOf MainMenu_Activated
            End If

            messageCheckTimer.Stop()
            messageCheckTimer.Start()

            ' Once immediately, so a message waiting at sign-in is not hidden for five minutes.
            CheckForNewMessages()
        End Sub

        ''' <summary>
        ''' Ticks are cheap to skip and expensive to take, so the tick asks whether taking one
        ''' could change anything a user can see.
        '''
        ''' The asterisk lives on this form and nowhere else - a page open over the menu shows no
        ''' new-mail clue and was never meant to - so while a page is open the check has no
        ''' audience. Skipping it is not only the saved query: the query runs on the UI thread, and
        ''' an unreachable database blocks SqlConnection.Open for the connect timeout, which would
        ''' otherwise land as a ten-second freeze in the middle of someone editing a record.
        ''' Activated picks it straight back up when the page closes.
        ''' </summary>
        Private Sub MessageCheckTimer_Tick(sender As Object, e As EventArgs)
            If Not MenuIsFrontmost() Then Return
            CheckForNewMessages()
        End Sub

        ''' <summary>
        ''' Returning to the menu is the moment the marker becomes visible again, so it is checked
        ''' then rather than leaving the user to wait out the rest of the interval looking at a
        ''' marker that was accurate when they left.
        ''' </summary>
        Private Sub MainMenu_Activated(sender As Object, e As EventArgs)
            If messageCheckTimer Is Nothing OrElse Not messageCheckTimer.Enabled Then Return
            If (DateTime.UtcNow - lastMessageCheckUtc).TotalMilliseconds < MessageCheckMinGapMs Then Return
            CheckForNewMessages()
        End Sub

        ''' <summary>
        ''' True when this form is the active one. A modal page makes the page the active form, and
        ''' the whole application losing focus makes ActiveForm nothing - both are cases where the
        ''' marker cannot be seen, and both are answered by the same test.
        ''' </summary>
        Private Function MenuIsFrontmost() As Boolean
            Return Form.ActiveForm Is Me
        End Function

        ''' <summary>
        ''' One query, then two consequences: the marker beside the registration name always, and
        ''' a reload of the list when the user is looking at it.
        '''
        ''' The reload is what puts a newly arrived message into the Inbox grid and rewrites the
        ''' tab caption to the new count - RefreshMessages does both, and does the count whichever
        ''' folder is showing, so "Inbox (3)" is right even while the user is reading Sent.
        '''
        ''' Failures are swallowed. A background check that cannot reach the database must not
        ''' interrupt somebody mid-sentence; the marker simply does not change until the next tick.
        ''' </summary>
        Private Sub CheckForNewMessages()
            Try
                If Not SessionState.Current.HasValue Then Return
                Dim session = SessionState.Current.Value

                lastMessageCheckUtc = DateTime.UtcNow

                ' The interval measures from the last check, not from the last tick. Coming back
                ' from a page checks, so without this the timer could fire seconds later and ask
                ' again - and every return to the menu would leave a shorter gap behind it.
                If messageCheckTimer IsNot Nothing AndAlso messageCheckTimer.Enabled Then
                    messageCheckTimer.Stop()
                    messageCheckTimer.Start()
                End If

                Dim unread = MessagingDataAccess.CountUnread(session.RegistrationID, currentUser.UserId)
                SetNewMessageIndicator(unread > 0)

                Dim showing = TryCast(GetRegionContent(MenuRegion.RegionLeft), MessagesWindowControl)
                If showing IsNot Nothing Then
                    showing.ReloadCurrentFolder()
                End If
            Catch
            End Try
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

                ' The same figure the generator asks before placing a tile, so what an App Admin is
                ' shown as free and what a new page is allowed to occupy are one number rather than
                ' two that can disagree.
                arrangementController.DurableSlotCount = MovableTileCapacityAtMinimumWidth()
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

            ' The caption the caller supplies is the tile's own, whatever an override may later put
            ' on top of it. Recorded before it is displayed so ApplyCaptionOverrides has something to
            ' restore when an override is withdrawn.
            tile.DefaultCaption = caption
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

        ''' <summary>
        ''' Tells a tile which page it opens, so its caption can follow that page.
        '''
        ''' Given to generated tiles, whose page the generator writes in. A one-off tile is never
        ''' given one: its caption was chosen by whoever asked for the button, and that is the final
        ''' word - Close, Admin and the pinned row all keep what they were called whatever any
        ''' override says.
        ''' </summary>
        Public Sub SetActionPage(actionKey As String, pageName As String)
            If String.IsNullOrWhiteSpace(actionKey) Then
                Return
            End If

            Dim tile As ActionTile = Nothing
            If Not actionTilesByKey.TryGetValue(actionKey, tile) Then
                Return
            End If

            tile.PageName = If(pageName, String.Empty).Trim()
        End Sub

        ''' <summary>
        ''' Re-captions every tile that opens a page, from the role's alias for that page's table.
        '''
        ''' A company that calls Gender "Pronoun" changes it once in Roles_U and it follows through
        ''' the menu, the browse grid and the maintenance page. Until 2026-09-06 the menu was the one
        ''' that did not follow: the override reached the page title and stopped, so a renamed table
        ''' showed on the page and not on the button that opened it - which reads as a half-applied
        ''' setting rather than as a missing feature.
        '''
        ''' Both directions, which is what DefaultCaption is for. Applying an override is only half
        ''' the job; a role that has none must get the coded caption back, or the previous role's
        ''' wording stays on the button after a role change.
        '''
        ''' Call this wherever role or registration may have changed. It reads the session itself, so
        ''' it needs no arguments and cannot be called with a stale registration.
        ''' </summary>
        Public Sub ApplyCaptionOverrides()
            Dim session = SessionState.Current
            Dim registrationId = If(session.HasValue, session.Value.RegistrationID, 0)

            For Each tile In actionTilesByKey.Values
                If tile Is Nothing OrElse tile.Button Is Nothing Then Continue For
                If String.IsNullOrWhiteSpace(tile.PageName) Then Continue For

                Dim caption = tile.DefaultCaption

                If registrationId > 0 Then
                    Try
                        ' Both sides come from caches held for the session - the page aliases in one
                        ' query for every page, the role overrides one per table - so a ribbon of
                        ' tiles costs no round trip after the first.
                        Dim tableName = DataAccess.GetPageDbTableByWindowOrPage(registrationId, tile.PageName)
                        Dim pageAlias = DataAccess.GetPageAliasByWindowOrPage(registrationId, tile.PageName)
                        Dim overrideCaption = PageTitleHelper.ResolveTableAliasOverride(registrationId, tableName)

                        Dim resolved = PageTitleHelper.ResolvePageCaptionFrom(tableName, overrideCaption, pageAlias)
                        If Not String.IsNullOrWhiteSpace(resolved) Then
                            caption = resolved
                        End If
                    Catch
                        ' A caption is not worth a broken menu. The tile keeps the wording it was
                        ' given, which is the same thing every session saw before this existed.
                    End Try
                End If

                tile.Button.Text = If(caption, String.Empty)
            Next
        End Sub

        Public Sub SetActionCaption(actionKey As String, caption As String)
            If String.IsNullOrWhiteSpace(actionKey) Then
                Return
            End If

            Dim tile As ActionTile = Nothing
            If Not actionTilesByKey.TryGetValue(actionKey, tile) Then
                Return
            End If

            ' Recorded as the tile's own caption as well as displayed. A caller setting a caption is
            ' stating what the tile is called, not painting over an override, so this is what
            ' ApplyCaptionOverrides restores to.
            tile.DefaultCaption = If(caption, String.Empty)
            tile.Button.Text = If(caption, String.Empty)
        End Sub

        ''' <summary>
        ''' Shows or hides the unread marker beside the registration name.
        '''
        ''' The only switch, and public because two things decide it: this form's own check, and
        ''' MessagesWindowControl.RefreshMessages, which has the count in hand already.
        '''
        ''' The permission is checked here rather than only at the call sites, so nothing can put
        ''' the marker on screen for a role that may not read messages - not a caller that forgets,
        ''' and not the test scaffold. One guard, in the place that does the showing.
        ''' </summary>
        Public Sub SetNewMessageIndicator(hasUnread As Boolean)
            If newMessageMarker Is Nothing Then Return

            Dim canUseMessaging = activeAccessProfile IsNot Nothing AndAlso
                                  activeAccessProfile.Can("FW_Messages", AccessCapability.Read)

            newMessageMarker.Visible = hasUnread AndAlso canUseMessaging
        End Sub

        ''' <summary>
        ''' Whether a region shows its caption bar, and whether its content is inset.
        '''
        ''' Some occupants are a caption and a list, and want the shell's furniture. Others are a
        ''' whole board - a banner and tabs of their own - and a caption above them is a second
        ''' title saying less than the picture does, with a strip of white between the two. Those
        ''' fill the cell instead.
        ''' </summary>
        Public Sub SetRegionChrome(region As MenuRegion, showHeader As Boolean)
            Dim shell = GetRegionShell(region)
            If shell Is Nothing Then Return

            If shell.HeaderPanel IsNot Nothing Then shell.HeaderPanel.Visible = showHeader
            If shell.ContentHost IsNot Nothing Then
                shell.ContentHost.Padding = If(showHeader, New Padding(8), New Padding(0))
            End If
        End Sub

        Public Sub SetRegionHeader(region As MenuRegion, headerText As String)
            Dim shell = GetRegionShell(region)
            shell.HeaderLabel.Text = headerText
        End Sub

        ''' <summary>
        ''' Adds an arrangement of the content area under a name, ready to be shown.
        '''
        ''' Public because the arrangement belongs to the application rather than the shell. The
        ''' framework owns what a layout *is* - a control filling the area under the ribbon - and
        ''' MenuFormInitializer owns which ones this menu has, the same division that already
        ''' decides the ribbon's tiles.
        '''
        ''' Registering a name twice replaces it; the old one is removed from the host rather than
        ''' left behind invisible, or a layout nobody can reach would keep its occupants alive.
        ''' </summary>
        Public Sub RegisterMenuLayout(layoutName As String, root As Control)
            If String.IsNullOrWhiteSpace(layoutName) OrElse root Is Nothing Then Return

            Dim existing As Control = Nothing
            If menuLayouts.TryGetValue(layoutName, existing) AndAlso existing IsNot root Then
                contentHost.Controls.Remove(existing)
                existing.Dispose()
            End If

            root.Dock = DockStyle.Fill
            root.Visible = False
            menuLayouts(layoutName) = root
            If Not contentHost.Controls.Contains(root) Then contentHost.Controls.Add(root)
        End Sub

        ''' <summary>
        ''' Shows one arrangement and hides the rest. Unknown names are ignored rather than thrown:
        ''' a tile naming a layout the project did not register should leave the menu as it is, not
        ''' take the application down.
        ''' </summary>
        Public Sub ApplyMenuLayout(layoutName As String)
            Dim target As Control = Nothing
            If String.IsNullOrWhiteSpace(layoutName) OrElse Not menuLayouts.TryGetValue(layoutName, target) Then Return
            If String.Equals(currentLayoutName, layoutName, StringComparison.OrdinalIgnoreCase) AndAlso target.Visible Then Return

            contentHost.SuspendLayout()
            Try
                For Each entry In menuLayouts
                    entry.Value.Visible = entry.Value Is target
                Next
                target.BringToFront()
                currentLayoutName = layoutName
            Finally
                contentHost.ResumeLayout(True)
            End Try
        End Sub

        ''' <summary>Which arrangement is showing, for a caller deciding whether it needs to switch.</summary>
        Public Function CurrentMenuLayout() As String
            Return currentLayoutName
        End Function

        ''' <summary>
        ''' Whether a region is part of the arrangement now showing. Every caller that loads a
        ''' region needs this: a layout that does not include Messages must not be asked to show
        ''' messages, and the answer is no rather than an exception.
        ''' </summary>
        Public Function IsRegionInCurrentLayout(region As MenuRegion) As Boolean
            Dim shell As RegionShell = Nothing
            If Not regionShells.TryGetValue(region, shell) Then Return False
            If shell.RootPanel Is Nothing Then Return False

            Dim ancestor As Control = shell.RootPanel.Parent
            While ancestor IsNot Nothing
                For Each entry In menuLayouts
                    If entry.Value Is ancestor Then
                        Return String.Equals(entry.Key, currentLayoutName, StringComparison.OrdinalIgnoreCase)
                    End If
                Next
                ancestor = ancestor.Parent
            End While

            Return False
        End Function

        ''' <summary>
        ''' The picture the Home Region shows, by file name in assets\images.
        '''
        ''' Loaded at its own size and scaled by the PictureBox, never scaled here: the content
        ''' area is 1196 x 452 at the smallest window and about 1856 x 733 maximised, and a picture
        ''' fitted to the first is soft at the second.
        '''
        ''' A name that resolves to nothing leaves whatever is showing, rather than blanking the
        ''' region - a mistyped registration setting should not produce an empty home page.
        ''' </summary>
        Public Sub SetHomeGraphic(fileName As String)
            If homePicture Is Nothing OrElse String.IsNullOrWhiteSpace(fileName) Then Return

            Dim loaded = IconScaler.LoadFullSize(fileName, Nothing)
            If loaded Is Nothing Then Return

            Dim previous = homePicture.Image
            homePicture.Image = loaded
            If previous IsNot Nothing Then previous.Dispose()
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

        ''' <summary>
        ''' Icon to the top of the tile, caption to the bottom.
        '''
        ''' Both were TopCenter, which only looked right while the tile was tall enough for the
        ''' slack to hide it - at 80 the caption ran into the graphic. Pinned to opposite ends they
        ''' cannot collide at any height that fits them both.
        ''' </summary>
        Private Sub AddActionTile(key As String, caption As String, onClick As EventHandler, tileImage As Image)
            Dim tileButton As New RibbonActionButton() With {
                .Name = "ACTION_" & key,
                .Text = caption,
                .Size = New Size(TileWidth, TileHeight),
                .TextAlign = ContentAlignment.BottomCenter,
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
                .OnClick = onClick,
                .DefaultCaption = caption
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

            ' All the room there is, rather than the largest whole number of tiles that fits into it.
            ' The remainder used to be discarded and collected at the right-hand end as dead space;
            ' now the panel keeps it, so the drop zone for arranging tiles runs right up to the
            ' pinned row instead of stopping at the last whole tile. Only whole tiles are ever drawn,
            ' so this changes where a tile may be dropped and not how many fit.
            '
            ' A drop past the last tile is already handled: IndexUnderPointer returns the last
            ' visible tile once the pointer is beyond it, so the extra width means "put it at the
            ' end" rather than an index off the end of the row.
            leftActionsFlow.Width = Math.Max(TilePitch, roomForFlow)

            ' Against the right edge, PanelInset from it, so the ribbon is inset by the same amount
            ' at both ends. It used to sit immediately after the flow panel, which was meant to make
            ' the gap before the first pinned tile match the gap between any two tiles - but the flow
            ' panel is rounded up to a whole number of tiles and is wider than its contents whenever
            ' the ribbon is not full, so that gap was never the tile gap anyway. The leftover width
            ' simply collected at the right-hand end, and the ribbon looked inset on the left and
            ' ragged on the right.
            rightPinnedActionsPanel.Left = ribbonPanel.ClientSize.Width -
                                           PanelInset -
                                           rightPinnedActionsPanel.Width
        End Sub

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


        ''' <summary>
        ''' Puts the caption immediately left of the combo, on its baseline.
        '''
        ''' Seated from the combo rather than placed at a fixed x, for the same reason the
        ''' registration caption is: the combo is right-anchored and narrows to its content, so any
        ''' position worked out in advance is wrong by the time it is seen.
        ''' </summary>
        Private Sub SeatTimeZoneCaption()
            If timeZoneOverrideCaption Is Nothing OrElse timeZoneOverrideCombo Is Nothing Then Return

            RegistrationComboHelper.SeatLabel(timeZoneOverrideCaption, timeZoneOverrideCombo)
            timeZoneOverrideCaption.Top = timeZoneOverrideCombo.Top + 4
            timeZoneOverrideCaption.Visible = timeZoneOverrideCombo.Visible
        End Sub
        ''' <summary>
        ''' Fills the session time zone combo and selects the one login resolved.
        ''' </summary>
        ''' <summary>
        ''' How many entries the US block runs to. GetLookupTable puts TimeZoneID 1 to 9 first, and
        ''' the rule and the box width are both measured against that block.
        ''' </summary>
        Private Const UsTimeZoneCount As Integer = 9

        Private Sub LoadTimeZoneOverride()
            If timeZoneOverrideCombo Is Nothing Then Return

            Dim zones = DataAccess.GetLookupTable("FW_TimeZones", "TimeZoneID", "DisplayName", False, 0, True)
            If zones Is Nothing OrElse zones.Rows.Count = 0 Then
                timeZoneOverrideCombo.Visible = False
                timeZoneOverrideCaption.Visible = False
                Return
            End If

            zones.Columns.Add("IanaId", GetType(String))
            Dim names = DataAccess.GetTimeZoneIanaIds()
            For Each row As Data.DataRow In zones.Rows
                Dim id = Convert.ToInt32(row("TimeZoneID"), Globalization.CultureInfo.InvariantCulture)
                row("IanaId") = If(names.ContainsKey(id), names(id), String.Empty)
            Next

            timeZoneOverrideCombo.DisplayMember = "DisplayName"
            timeZoneOverrideCombo.ValueMember = "IanaId"
            timeZoneOverrideCombo.DataSource = zones
            ' The US zones lead the list and are what anybody here picks; the rest of the world
            ' follows and is what makes the widest entry wide. The box takes the prompt and the US
            ' ones, and the rule falls where the world begins.
            ComboWidth.FitToLeadingItems(timeZoneOverrideCombo, UsTimeZoneCount)
            ComboSeparator.After(timeZoneOverrideCombo, UsTimeZoneCount - 1)
            SeatTimeZoneCaption()

            ' The zone the session is actually on - the employee's where they have one, otherwise the
            ' registration's.
            Dim current = If(SessionState.IsActive AndAlso SessionState.Current.HasValue,
                             If(SessionState.Current.Value.TimeZoneName, String.Empty), String.Empty)
            If current <> String.Empty Then timeZoneOverrideCombo.SelectedValue = current

            AddHandler timeZoneOverrideCombo.SelectedIndexChanged, AddressOf TimeZoneOverride_Changed
            AddHandler timeZoneOverrideCombo.SizeChanged, Sub(sender, e) SeatTimeZoneCaption()
            AddHandler timeZoneOverrideCombo.LocationChanged, Sub(sender, e) SeatTimeZoneCaption()
        End Sub

        Private Sub TimeZoneOverride_Changed(sender As Object, e As EventArgs)
            If timeZoneOverrideCombo.SelectedValue Is Nothing Then Return
            SessionState.OverrideTimeZone(Convert.ToString(timeZoneOverrideCombo.SelectedValue))
        End Sub

        Private Sub LoadSamplePlaceholders()
            LoadRegionControl(MenuRegion.RegionLeft, New MessagesWindowControl())
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
        ''' <summary>
        ''' The two region selectors. Each loads its own control into the left-hand region and
        ''' names the region after it, so the header always says what is being looked at.
        '''
        ''' LoadRegionControl replaces whatever is in the cell, so there is no stacking of hidden
        ''' panels and no state to keep about which was there before.
        ''' </summary>
        ''' <summary>
        ''' Back to the picture. Nothing is unloaded - the Dashboards layout keeps its occupants
        ''' and comes back exactly as it was left.
        ''' </summary>
        Private Sub ShowHomeLayout_Click(sender As Object, e As EventArgs)
            ApplyMenuLayout(HomeLayoutName)
        End Sub

        Private Sub ShowMessagesRegion_Click(sender As Object, e As EventArgs)
            ' The region lives in the Dashboards layout, and pressing Messages while Home is up is
            ' asking to see messages - not to load them into an arrangement nobody is looking at.
            ' Before the reload test below, or a click from Home would find the messages already
            ' loaded, reload them, and leave the picture showing.
            ApplyMenuLayout(DashboardsLayoutName)

            ' Already showing: reload in place rather than build a second control. Rebuilding
            ' flashed the region and lost the selected row, for a click that had asked for
            ' nothing. Re-reading the folder is what someone pressing Messages while looking at
            ' messages actually wants.
            Dim showing = TryCast(GetRegionContent(MenuRegion.RegionLeft), MessagesWindowControl)
            If showing IsNot Nothing Then
                showing.ReloadCurrentFolder()
                Return
            End If

            SetRegionChrome(MenuRegion.RegionLeft, True)
            SetRegionHeader(MenuRegion.RegionLeft, "Messages")
            LoadRegionControl(MenuRegion.RegionLeft, New MessagesWindowControl())
        End Sub

        Private Sub ShowOverviewRegion_Click(sender As Object, e As EventArgs)
            ' As Messages: the arrangement first, for the same reason.
            ApplyMenuLayout(DashboardsLayoutName)

            ' Nothing to reload - it shows no data - so the click simply does nothing when it is
            ' already up, rather than rebuilding it for no reason.
            If TypeOf GetRegionContent(MenuRegion.RegionLeft) Is QDeskWindowControl Then
                Return
            End If

            SetRegionHeader(MenuRegion.RegionLeft, "QDesk")
            SetRegionChrome(MenuRegion.RegionLeft, False)
            LoadRegionControl(MenuRegion.RegionLeft, New QDeskWindowControl())
        End Sub

        ''' <summary>
        ''' Opens the signed-in person's own employee record.
        '''
        ''' Their profile is their employee row - the name, address and phones live there now,
        ''' not on the login. Opened in self-service mode, which is what stops somebody granting
        ''' themselves a role or a different manager.
        ''' </summary>
        Private Sub MyProfile_Click(sender As Object, e As EventArgs)
            Dim employeeId = DataAccess.GetEmployeeIdForUser(If(SessionState.IsActive AndAlso SessionState.Current.HasValue,
                                                                SessionState.Current.Value.UserID, 0))
            If employeeId <= 0 Then
                MessageBox.Show(Me,
                                "THERE IS NO EMPLOYEE RECORD FOR THIS SIGN-IN." & Environment.NewLine & Environment.NewLine &
                                "PLEASE CONTACT YOUR ADMINISTRATOR.",
                                "My Profile", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Using page As New FW_Employees_U(employeeId, currentUser, activeAccessProfile, selfService:=True)
                page.ShowDialog(Me)
            End Using
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

        ''' <summary>
        ''' Leaves the menu, which returns to the login screen.
        '''
        ''' There was a Close tile at the head of the ribbon doing exactly this, and it was removed
        ''' on 2026-09-12: the window's own close box already does it. The menu is shown with
        ''' ShowDialog from the login form, so closing it with DialogResult.Cancel - however that
        ''' is asked for - lands back at the login screen rather than ending the process. A tile
        ''' for it cost a slot in a row that runs out of them.
        ''' </summary>
        Private Sub LogoutButton_Click(sender As Object, e As EventArgs)
            Me.DialogResult = DialogResult.Cancel
            Me.Close()
        End Sub
    End Class
End Namespace
