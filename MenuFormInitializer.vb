Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Module MenuFormInitializer
        Private Const TableApplicationSettingsDashboard As String = "APPLICATION SETTINGS DASHBOARD"
        Private Const TableRoles As String = "ROLES"
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
        Private Const MenuSurfaceName As String = "SDC.Framework.MainMenu"

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

        ''' <summary>
        ''' Owns how every ribbon tile's drop-down menu behaves. One for the ribbon, shared by each
        ''' tile that grows a menu, so a second menu cannot behave differently from the first.
        ''' </summary>
        Private ReadOnly tileDropDowns As New TileDropDownController()

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
            Dim applicationSettingsCaption As String = "Application" & Environment.NewLine & "Settings"
            Dim session = SessionState.Current
            Dim isAppAdminSession As Boolean = False
            Dim isCompanyAdminSession As Boolean = False
            If session.HasValue AndAlso session.Value.RoleID > 0 AndAlso session.Value.RegistrationID > 0 Then
                isAppAdminSession = session.Value.IsApplicationAdminRole
                isCompanyAdminSession = session.Value.IsCompanyAdminRole

            End If

            Dim canAccessApplicationSettings = isAppAdminSession OrElse isCompanyAdminSession
            menu.ConfigureActionVisibility("application-settings", canAccessApplicationSettings, canAccessApplicationSettings)
            menu.SetActionCaption("application-settings", applicationSettingsCaption)
            menu.ConfigureActionVisibility("users", True, True)
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
        ''' Note what it does not do. It says what its menu contains and nothing about how the menu
        ''' behaves - opening, and closing when the pointer leaves, both belong to
        ''' TileDropDownController. A real tile copying this inherits that behaviour by writing no
        ''' part of it.
        '''
        ''' The items are built on first open rather than here, so a menu may be assembled from
        ''' state that does not exist yet when the ribbon is configured.
        ''' </summary>
        Private Sub AddMenuTestTile(menu As FW_MainMenu)
            menu.UpsertActionTile(
                actionKey:="menu-test",
                caption:="Menu" & Environment.NewLine & "Test",
                onClick:=Sub(sender, e) tileDropDowns.Open(TryCast(sender, Control),
                                                           Function() BuildMenuTestItems(menu)),
                iconFileName:="Fluent_Open.png",
                fallbackIcon:=SystemIcons.Application.ToBitmap(),
                isVisible:=True,
                isEnabled:=True)
        End Sub

        Private Function BuildMenuTestItems(owner As FW_MainMenu) As IEnumerable(Of ToolStripItem)
            Dim items As New List(Of ToolStripItem)()

            For Each choice In {"Messages", "General Dashboard", "Acme Dashboard"}
                items.Add(BuildMenuTestItem(owner, choice))
            Next

            items.Add(New ToolStripSeparator())
            items.Add(BuildMenuTestItem(owner, "Users && Lists"))
            items.Add(BuildMenuTestItem(owner, "Evolution of Acme Products"))

            Return items
        End Function

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
