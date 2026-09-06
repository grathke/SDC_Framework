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

            ApplyActionAccess(menu, user, profile)
            ApplyRegionAccess(menu, profile)

            ' Last, because ApplyActionAccess adds a tile and rewrites captions and pictures. The
            ' saved arrangement and the chosen pictures are laid over the finished ribbon rather
            ' than over a half-built one.
            menu.ConfigureArrangement(MenuSurfaceName, AnchoredMenuKeys)

            ' After the captions have been written, so an override lands on the finished wording
            ' rather than being overwritten by it. Configure runs on every role change and on several
            ' dialog returns, which is exactly when a role's aliases stop or start applying.
            menu.ApplyCaptionOverrides()
        End Sub

        ' user is carried through because a generated ribbon tile opens a page whose constructor
        ' takes it. Nothing else here needed it, so it was not passed - and a generated tile is
        ' written into this method, where currentUser is not in scope and no shared helper builds
        ' one.
        Private Sub ApplyActionAccess(menu As FW_MainMenu, user As UserContext, profile As AccessProfile)
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
            ConfigureApplicationSettingsTile(menu, applicationSettingsCaption, canAccessApplicationSettings)
            menu.ConfigureActionVisibility("dashboard", True, True)

            ' Signing in as somebody else is an administrator's action, so only an App Admin is
            ' offered it. Company Admin is deliberately excluded, unlike Application Settings above:
            ' administering a company is not the same as being able to become one of its users.
            '
            ' The tile is still a placeholder - its handler says the workflow is not hooked up yet -
            ' so this hides a button that does nothing rather than protecting anything. When the
            ' workflow is written, the check that matters goes at its action boundary; a hidden tile
            ' is not authorization.
            '
            ' The button itself is gone as of 2026-09-04: the action moved into the Application
            ' Settings drop-down, so the ribbon no longer spends a tile on it. Hidden rather than
            ' unregistered, because the menu item invokes this tile's own handler - see
            ' FW_MainMenu.OpenSubstituteUser. The pinned row closes up on its own, since
            ' LayoutPinnedActions skips what is not there and SizePinnedPanelToVisibleTiles resizes.
            menu.ConfigureActionVisibility("login-as-substitute", False, False)

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

            menu.UpsertActionTile(
                actionKey:="generated-usersy_b",
                caption:="UsersY",
                onClick:=Sub(sender, e)
                             Using frm As New UsersY_B(user, profile)
                                 frm.ShowDialog(menu)
                             End Using
                         End Sub,
                iconFileName:="Color_OK.png",
                fallbackIcon:=SystemIcons.Application.ToBitmap(),
                isVisible:=True,
                isEnabled:=True)

            ' Which page each tile opens, so its caption can follow the role's alias for that page's
            ' table. Declared here rather than inferred, because a click handler is a delegate and
            ' there is nothing in one to read a page name out of.
            '
            ' Only tiles that open a table-backed page appear. Close, Dashboard, Application Settings
            ' and the pinned row keep their coded captions: they are not a table under another name.
            menu.SetActionPage("user-admin", "Users_AppAdmin_B")
            menu.SetActionPage("generated-usersy_b", "UsersY_B")

            ' Generated ribbon tiles are inserted above this marker. PageGenerator matches the next
            ' line exactly, so rewording it stops generation placing tiles on the ribbon - it says it
            ' found no recognised place rather than failing quietly. It used to anchor on an
            ' AddMenuTestTile call that happened to sit last in this method, which meant deleting a
            ' demonstration tile would have broken page generation.
            ' PAGEGEN RIBBON ANCHOR
        End Sub


        ''' <summary>
        ''' Application Settings drops a menu down for an App Admin, and stays a plain button for a
        ''' Company Admin.
        '''
        ''' The two roles want different things from it. An App Admin has more than one
        ''' administrative action, so the tile becomes the way in to all of them; a Company Admin
        ''' has exactly one - their own dashboard - and a one-item menu is a worse button.
        '''
        ''' The role is read when the tile is clicked rather than when the ribbon is configured.
        ''' Selecting a role rebuilds the menu, so reading it here would work too - but it would
        ''' leave a handler behind that is right only until the next role change, and this way there
        ''' is nothing to keep in step.
        '''
        ''' No icon is passed. UpsertActionTile only touches the picture when it is given one, and
        ''' an App Admin may have chosen their own - rewriting it here is the bug that wiped Help
        ''' Desk's picture on every ribbon resize.
        ''' </summary>
        Private Sub ConfigureApplicationSettingsTile(menu As FW_MainMenu, caption As String, isAvailable As Boolean)
            menu.UpsertActionTile(
                actionKey:="application-settings",
                caption:=caption,
                onClick:=Sub(sender, e)
                             If IsApplicationAdminSession() Then
                                 tileDropDowns.Open(TryCast(sender, Control),
                                                    Function() BuildApplicationSettingsItems(menu))
                             Else
                                 menu.OpenApplicationSettings()
                             End If
                         End Sub,
                isVisible:=isAvailable,
                isEnabled:=isAvailable)
        End Sub

        Private Function IsApplicationAdminSession() As Boolean
            Dim session = SessionState.Current
            Return session.HasValue AndAlso
                   session.Value.RoleID > 0 AndAlso
                   session.Value.RegistrationID > 0 AndAlso
                   session.Value.IsApplicationAdminRole
        End Function

        ''' <summary>
        ''' What an App Admin can reach from Application Settings. The dashboard first, because it is
        ''' what the button did before it grew a menu and muscle memory should still land on it.
        '''
        ''' Every item invokes the tile handler that already owns the action rather than repeating
        ''' it - Application Settings still decides between the Application and Company dashboards
        ''' by role, in one place.
        ''' </summary>
        Private Function BuildApplicationSettingsItems(menu As FW_MainMenu) As IEnumerable(Of ToolStripItem)
            Dim items As New List(Of ToolStripItem)()

            items.Add(BuildActionItem("Admin Dashboard", Sub() menu.OpenApplicationSettings()))
            items.Add(New ToolStripSeparator())
            items.Add(BuildActionItem("Switch User", Sub() menu.OpenSubstituteUser()))

            Return items
        End Function

        Private Function BuildActionItem(label As String, invoke As Action) As ToolStripMenuItem
            Dim item As New ToolStripMenuItem(label)
            AddHandler item.Click, Sub() invoke()
            Return item
        End Function

        ' The Menu Test tile was removed on 2026-09-05. It was a demonstration - it dropped a menu
        ' down and said what you chose - kept as the working example to copy when a real tile needed
        ' a menu. ConfigureApplicationSettingsTile above is now that example, doing the same thing
        ' through the same TileDropDownController for a real purpose, so the demonstration was
        ' costing a ribbon slot to show what a live tile already shows.

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
