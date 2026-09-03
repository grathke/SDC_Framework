Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Module MenuFormInitializer
        Private Const TableApplicationSettingsDashboard As String = "APPLICATION SETTINGS DASHBOARD"
        Private Const TableRoles As String = "ROLES"
        Private Const TableEntity As String = "FW_Entity"
        Private Const TableMessaging As String = "MESSAGING"
        Private Const TableFrameworkDashboard As String = "FRAMEWORK DASHBOARD"
        Private Const TableRegistrationDashboard As String = "REGISTRATION DASHBOARD"

        ''' <summary>
        ''' Which ribbon this initializer is arranging: application, then surface.
        '''
        ''' It belongs here rather than in FW_MainMenu because this module is the only part of the
        ''' menu that is application specific - the form serves whichever application configures it.
        ''' A second application writes its own initializer and its own name here, and the two
        ''' ribbons keep separate arrangements in FW_DashboardLayouts.
        '''
        ''' Deliberately not the assembly or exe name: this repository already builds under a second
        ''' output path for hotfixes, and a renamed exe would orphan every saved arrangement without
        ''' saying so.
        ''' </summary>
        Private Const MenuSurfaceName As String = "HelloWorld.MainMenu"

        ''' <summary>
        ''' Tiles pinned to the head of the flow panel, in this order. They cannot be dragged and
        ''' nothing can be dropped in front of them.
        '''
        ''' Close, then Dashboard, then Application Settings. The movable tiles follow them.
        '''
        ''' Anchoring is not visibility. Any of the three can still be hidden by a permission, and
        ''' the row closes up around it: hide Dashboard and Application Settings moves left into its
        ''' place. That falls out of the flow panel skipping invisible children, so it needs no code
        ''' of its own - which is exactly why what is saved is a rank and never a coordinate.
        '''
        ''' `dashboard` is a placeholder tile: it holds its place in the row and reports that it is
        ''' not wired up. An anchor naming a key with no tile would match nothing and cost nothing,
        ''' so this list can name one before the ribbon has it.
        '''
        ''' Here rather than in the menu form for the same reason as the surface name - which tiles
        ''' lead a ribbon is the application's decision, and the form serves whichever application
        ''' configures it.
        ''' </summary>
        Private ReadOnly AnchoredMenuKeys As String() = {"close", "dashboard", "application-settings"}

        Private cachedAccessRoleId As Integer = 0
        Private cachedAccessRegistrationId As Integer = 0
        Private cachedAccessProfile As AccessProfile = Nothing

        Public Sub InvalidateAccessCache()
            cachedAccessRoleId = 0
            cachedAccessRegistrationId = 0
            cachedAccessProfile = Nothing
            DataAccess.InvalidateRoleMetadataCache()
        End Sub

        Public Function BuildAccessProfileForCurrentSession(user As UserContext,
                                                            Optional pageName As String = "Browse") As AccessProfile
            Dim session = SessionState.Current
            If session.HasValue AndAlso session.Value.RoleID > 0 AndAlso session.Value.RegistrationID > 0 Then
                Dim roleRows = DataAccess.GetRoleTableAccessEntries(session.Value.RoleID, session.Value.RegistrationID)
                Dim roleLevel = DataAccess.GetRoleDisplayOrder(session.Value.RoleID)
                Dim roleName = If(String.IsNullOrWhiteSpace(session.Value.RoleName), "Unassigned", session.Value.RoleName)
                Dim profileProvider As IAccessProfileProvider = New RoleMatrixAccessProfileProvider(roleLevel, roleName, roleRows)
                Return profileProvider.GetProfile(user)
            End If

            DataAccess.LogFallbackUsage("CRUD_Fallback_NoSessionProfile",
                                        "Browse page opened without active role/registration session; using no-access profile.",
                                        pageName)
            Return New AccessProfile()
        End Function

        Public Sub Configure(menu As FW_MainMenu, user As UserContext, Optional forceRefresh As Boolean = False)
            If menu Is Nothing Then
                Return
            End If

            If forceRefresh Then
                InvalidateAccessCache()
            End If

            Dim profile As AccessProfile = Nothing
            Dim session = SessionState.Current

            If session.HasValue AndAlso session.Value.RoleID > 0 AndAlso session.Value.RegistrationID > 0 Then
                Dim roleId = session.Value.RoleID
                Dim registrationId = session.Value.RegistrationID

                If Not forceRefresh AndAlso
                   cachedAccessProfile IsNot Nothing AndAlso
                   cachedAccessRoleId = roleId AndAlso
                   cachedAccessRegistrationId = registrationId Then
                    profile = cachedAccessProfile
                Else
                    Dim roleRows = DataAccess.GetRoleTableAccessEntries(roleId, registrationId)
                    Dim roleLevel = DataAccess.GetRoleDisplayOrder(roleId)
                    Dim roleName = If(String.IsNullOrWhiteSpace(session.Value.RoleName), "Unassigned", session.Value.RoleName)
                    Dim profileProvider As IAccessProfileProvider = New RoleMatrixAccessProfileProvider(roleLevel, roleName, roleRows)
                    profile = profileProvider.GetProfile(user)

                    cachedAccessRoleId = roleId
                    cachedAccessRegistrationId = registrationId
                    cachedAccessProfile = profile
                End If

                Dim crudCaptions = DataAccess.GetCrudButtonCaptions(registrationId)
                SessionState.UpdateCrudCaptions(crudCaptions.CreateCaption,
                                                crudCaptions.ReadCaption,
                                                crudCaptions.UpdateCaption,
                                                crudCaptions.DeleteCaption)
            Else
                InvalidateAccessCache()
                profile = New AccessProfile()
                profile.SetRole(0, "No Access")
                DataAccess.LogFallbackUsage("CRUD_Fallback_NoSessionProfile", "Missing session role/registration context; using no-access profile.", "MainMenu")
            End If

            menu.SetAccessProfile(profile)

            ApplyActionAccess(menu, profile)
            ApplyRegionAccess(menu, profile)

            ' Last, because ApplyActionAccess adds a tile and rewrites captions and pictures. The
            ' saved arrangement and the chosen pictures are laid over the finished ribbon rather
            ' than over a half-built one.
            menu.ConfigureArrangement(MenuSurfaceName, AnchoredMenuKeys)
        End Sub

        Private Sub ApplyActionAccess(menu As FW_MainMenu, profile As AccessProfile)
            If menu Is Nothing OrElse profile Is Nothing Then
                Return
            End If

            Dim entityCaption As String = "Entity"
            Dim applicationSettingsCaption As String = "Application" & Environment.NewLine & "Settings"
            Dim session = SessionState.Current
            Dim isAppAdminSession As Boolean = False
            Dim isCompanyAdminSession As Boolean = False
            If session.HasValue AndAlso session.Value.RoleID > 0 AndAlso session.Value.RegistrationID > 0 Then
                Dim overrideCaption = DataAccess.GetRoleDetailOverrideCaption(session.Value.RoleID, session.Value.RegistrationID, TableEntity)
                If Not String.IsNullOrWhiteSpace(overrideCaption) Then
                    entityCaption = overrideCaption
                End If

                isAppAdminSession = session.Value.IsApplicationAdminRole
                isCompanyAdminSession = session.Value.IsCompanyAdminRole

            End If

            Dim canAccessApplicationSettings = isAppAdminSession OrElse isCompanyAdminSession
            menu.ConfigureActionVisibility("application-settings", canAccessApplicationSettings, canAccessApplicationSettings)
            menu.SetActionCaption("application-settings", applicationSettingsCaption)
            menu.ConfigureActionVisibility("users", True, True)
            menu.ConfigureActionVisibility("entity", True, True)
            menu.SetActionCaption("entity", entityCaption)
            menu.ConfigureActionVisibility("dashboard", True, True)

            menu.ConfigureActionVisibility("select-role", True, True)

            menu.UpsertActionTile(
                actionKey:="user-admin",
                caption:="User" & Environment.NewLine & "Admin",
                onClick:=Sub(sender, e)
                             Using frm As New Users_AppAdmin_B(profile)
                                 frm.ShowDialog(menu)
                             End Using
                         End Sub,
                iconFileName:="users.png",
                fallbackIcon:=SystemIcons.WinLogo.ToBitmap(),
                isVisible:=True,
                isEnabled:=True)

            AddMenuTestTile(menu)
        End Sub

        ''' <summary>
        ''' A demonstration tile: it drops a menu down over the regions below, and choosing an item
        ''' only says what was chosen. Kept deliberately, as the working example to copy when a real
        ''' tile needs a menu.
        '''
        ''' The menu is a ContextMenuStrip shown explicitly rather than assigned to the button's
        ''' ContextMenuStrip property. Two reasons: a left-click should open it, and that property is
        ''' already taken on every tile by the App Admin icon picker.
        '''
        ''' A ContextMenuStrip and not a panel, because it is its own top-level window and so drops
        ''' over the regions below. A child panel would be clipped at the ribbon's edge, which is the
        ''' whole difficulty this answers.
        ''' </summary>
        Private menuTestDropDown As ContextMenuStrip
        Private menuTestCloseWatcher As Timer
        Private menuTestTile As Control

        Private Sub AddMenuTestTile(menu As FW_MainMenu)
            menu.UpsertActionTile(
                actionKey:="menu-test",
                caption:="Menu" & Environment.NewLine & "Test",
                onClick:=Sub(sender, e) ShowMenuTestDropDown(menu, TryCast(sender, Control)),
                iconFileName:="Fluent_Open.png",
                fallbackIcon:=SystemIcons.Application.ToBitmap(),
                isVisible:=True,
                isEnabled:=True)
        End Sub

        Private Sub ShowMenuTestDropDown(owner As FW_MainMenu, tile As Control)
            If tile Is Nothing Then
                Return
            End If

            If menuTestDropDown Is Nothing Then
                menuTestDropDown = New ContextMenuStrip()

                For Each choice In {"Messages", "General Dashboard", "Acme Dashboard"}
                    menuTestDropDown.Items.Add(BuildMenuTestItem(owner, choice))
                Next

                menuTestDropDown.Items.Add(New ToolStripSeparator())
                menuTestDropDown.Items.Add(BuildMenuTestItem(owner, "Users && Lists"))
                menuTestDropDown.Items.Add(BuildMenuTestItem(owner, "Evolution of Acme Products"))
            End If

            ' Anchored to the tile's bottom-left corner. A ContextMenuStrip is its own top-level
            ' window, so it drops down over the regions below instead of being clipped by the ribbon
            ' the way a child panel would be.
            menuTestTile = tile
            menuTestDropDown.Show(tile, New Point(0, tile.Height))
            StartMenuTestCloseWatcher()
        End Sub

        ''' <summary>
        ''' Closes the menu once the pointer is over neither the menu nor the tile that opened it.
        '''
        ''' Polled rather than driven by MouseLeave. The pointer crosses from the tile to the menu
        ''' and back between two separate top-level windows, and each crossing raises a leave on one
        ''' of them - so closing on leave would shut the menu the instant somebody moved towards it.
        ''' Asking where the pointer actually is answers the question once, for both.
        '''
        ''' Both rectangles are inflated slightly so that a diagonal move across the seam between
        ''' the two does not clip a corner and count as having left.
        ''' </summary>
        Private Sub StartMenuTestCloseWatcher()
            If menuTestCloseWatcher Is Nothing Then
                menuTestCloseWatcher = New Timer() With {.Interval = 200}
                AddHandler menuTestCloseWatcher.Tick, AddressOf MenuTestCloseWatcher_Tick
            End If

            menuTestCloseWatcher.Start()
        End Sub

        Private Sub MenuTestCloseWatcher_Tick(sender As Object, e As EventArgs)
            If menuTestDropDown Is Nothing OrElse Not menuTestDropDown.Visible Then
                menuTestCloseWatcher.Stop()
                Return
            End If

            Dim pointer = Cursor.Position

            Dim overMenu = Rectangle.Inflate(menuTestDropDown.Bounds, 6, 6).Contains(pointer)
            Dim overTile = menuTestTile IsNot Nothing AndAlso
                           Rectangle.Inflate(menuTestTile.RectangleToScreen(menuTestTile.ClientRectangle), 6, 6).Contains(pointer)

            If overMenu OrElse overTile Then
                Return
            End If

            menuTestCloseWatcher.Stop()
            menuTestDropDown.Close()
        End Sub

        Private Function BuildMenuTestItem(owner As FW_MainMenu, label As String) As ToolStripMenuItem
            Dim item As New ToolStripMenuItem(label)

            AddHandler item.Click,
                Sub()
                    MessageBox.Show(owner,
                                    "Menu test - you chose: " & label.Replace("&&", "&"),
                                    "Menu Test",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information)
                End Sub

            Return item
        End Function

        Private Sub ApplyRegionAccess(menu As FW_MainMenu, profile As AccessProfile)
            If menu Is Nothing OrElse profile Is Nothing Then
                Return
            End If

            LoadRegionAlways(menu, profile, FW_MainMenu.MenuRegion.Messages, TableMessaging, Function() New MessagesWindowControl(), "Messages")
            LoadRegionAlways(menu, profile, FW_MainMenu.MenuRegion.GeneralDashboard, TableFrameworkDashboard, Function() New GeneralDashboardWindowControl(), "General Dashboard")
            LoadRegionAlways(menu, profile, FW_MainMenu.MenuRegion.AcmeDashboard, TableRegistrationDashboard, Function() New AcmeDashboardWindowControl(), "Acme Dashboard")
            LoadRegionAlways(menu, profile, FW_MainMenu.MenuRegion.UsersAndLists, TableRoles, Function() New UsersListsWindowControl(), "Users & Lists")
            menu.SetRegionHeader(FW_MainMenu.MenuRegion.Chart, "Evolution of Acme Products")
            menu.SetRegionVisible(FW_MainMenu.MenuRegion.Chart, True)
        End Sub

        Private Sub LoadRegionAlways(menu As FW_MainMenu,
                                     profile As AccessProfile,
                                     region As FW_MainMenu.MenuRegion,
                                     tableName As String,
                                     controlFactory As Func(Of Control),
                                     headerText As String)
            menu.SetRegionHeader(region, headerText)

            Dim control = controlFactory()
            Dim accessControlled = TryCast(control, IAccessControlledControl)
            If accessControlled IsNot Nothing Then
                accessControlled.ApplyAccess(profile, tableName)
            End If

            menu.LoadRegionControl(region, control)
            menu.SetRegionVisible(region, True)
        End Sub

        Private Sub LoadRegionByAccess(menu As FW_MainMenu,
                                       profile As AccessProfile,
                           region As FW_MainMenu.MenuRegion,
                                       tableName As String,
                                       required As AccessCapability,
                                       controlFactory As Func(Of Control),
                                       headerText As String)
            If profile.Can(tableName, required) Then
                menu.SetRegionHeader(region, headerText)

                Dim control = controlFactory()
                Dim accessControlled = TryCast(control, IAccessControlledControl)
                If accessControlled IsNot Nothing Then
                    accessControlled.ApplyAccess(profile, tableName)
                End If

                menu.LoadRegionControl(region, control)
                menu.SetRegionVisible(region, True)
                Return
            End If

            menu.SetRegionHeader(region, headerText & " [No Access]")
            menu.ShowRegionAccessDenied(region, tableName, profile.RoleName)
            menu.SetRegionVisible(region, True)
        End Sub
    End Module
End Namespace
