Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class Dashboard_Application
        Inherits Form

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        Private ReadOnly topStripLabel As Label
        Private ReadOnly headerLabel As Label
        Private ReadOnly rolesButton As DashboardIconButton
        Private ReadOnly registrationButton As DashboardIconButton
        Private ReadOnly auditHistoryButton As DashboardIconButton
        Private ReadOnly helpDeskButton As DashboardIconButton
        Private ReadOnly helpDeskSupportButton As DashboardIconButton
        Private ReadOnly newPageRequestsButton As DashboardIconButton
        Private ReadOnly databaseConfigButton As DashboardIconButton
        Private ReadOnly companyDashboardButton As DashboardIconButton
        Private ReadOnly updateSchemaButton As DashboardIconButton
        Private ReadOnly closeIconButton As Button
        Private ReadOnly generatedFW_Employees_BButton As DashboardIconButton
        Private ReadOnly generatedFW_UserAccessDiagnostic_BButton As DashboardIconButton
        Private iconDragController As DashboardIconDragController
        Private iconImageController As IconImageController

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = user
            accessProfile = profile

            Me.Text = "DASHBOARD: ADMIN"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.MinimumSize = New Size(DashboardGridLayout.StandardClientWidth, DashboardGridLayout.StandardClientHeight)
            Me.ClientSize = New Size(DashboardGridLayout.StandardClientWidth, DashboardGridLayout.StandardClientHeight)
            Me.BackColor = Color.White

            topStripLabel = New Label() With {
                .Text = "DASHBOARD: ADMIN",
                .Location = New Point(0, 0),
                .Size = New Size(Me.ClientSize.Width, 28),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .BackColor = Color.FromArgb(0, 136, 172),
                .ForeColor = Color.White,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .TextAlign = ContentAlignment.MiddleLeft
            }

            closeIconButton = New Button() With {
                .Text = "X",
                .Size = New Size(30, 28),
                .Location = New Point(Me.ClientSize.Width - 34, 0),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .BackColor = Color.Red,
                .ForeColor = Color.White,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 11.0F, FontStyle.Bold),
                .TabStop = False
            }
            closeIconButton.FlatAppearance.BorderSize = 0

            headerLabel = New Label() With {
                .Text = "DASHBOARD: ADMIN",
                .Location = New Point(0, 48),
                .Size = New Size(Me.ClientSize.Width, 38),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Font = New Font("Segoe UI", 17.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(42, 42, 42),
                .TextAlign = ContentAlignment.MiddleCenter
            }

            rolesButton = New DashboardIconButton() With {
                .Name = "ActionKey_Roles",
                .Text = "Roles",
                .Location = DashboardGridLayout.CellLocation(1, 1),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Color_Shield.png", SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            rolesButton.FlatAppearance.BorderSize = 0
            rolesButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            rolesButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            registrationButton = New DashboardIconButton() With {
                .Name = "ActionKey_Registration",
                .Text = "Registration",
                .Location = DashboardGridLayout.CellLocation(1, 3),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Color_New.png", SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            registrationButton.FlatAppearance.BorderSize = 0
            registrationButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            registrationButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            auditHistoryButton = New DashboardIconButton() With {
                .Name = "ActionKey_AuditHistory",
                .Text = "Audit History",
                .Location = DashboardGridLayout.CellLocation(2, 1),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Color_Search.png", SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            auditHistoryButton.FlatAppearance.BorderSize = 0
            auditHistoryButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            auditHistoryButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            helpDeskButton = New DashboardIconButton() With {
                .Name = "ActionKey_HelpDesk",
                .Text = "Help Desk",
                .Location = DashboardGridLayout.CellLocation(2, 2),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Color_Help_Desk.png", SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            helpDeskButton.FlatAppearance.BorderSize = 0
            helpDeskButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            helpDeskButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            helpDeskSupportButton = New DashboardIconButton() With {
                .Name = "ActionKey_HelpDeskSupport",
                .Text = "HD Support Tickets",
                .Location = DashboardGridLayout.CellLocation(2, 3),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 11.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Color_Question.png", SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            helpDeskSupportButton.FlatAppearance.BorderSize = 0
            helpDeskSupportButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            helpDeskSupportButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            databaseConfigButton = New DashboardIconButton() With {
                .Name = "ActionKey_DatabaseConfig",
                .Text = "Database Config",
                .Location = DashboardGridLayout.CellLocation(2, 4),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Color_Settings.png", SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            databaseConfigButton.FlatAppearance.BorderSize = 0
            databaseConfigButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            databaseConfigButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            companyDashboardButton = New DashboardIconButton() With {
                .Name = "ActionKey_DashboardCompany",
                .Text = "Company" & Environment.NewLine & "Dashboard",
                .Location = DashboardGridLayout.CellLocation(3, 2),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Fluent_Home.png", SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            companyDashboardButton.FlatAppearance.BorderSize = 0
            companyDashboardButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            companyDashboardButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            newPageRequestsButton = New DashboardIconButton() With {
                .Name = "ActionKey_PageGeneration",
                .Text = "Page Generator",
                .Location = DashboardGridLayout.CellLocation(3, 1),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Color_Add.png", SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            newPageRequestsButton.FlatAppearance.BorderSize = 0
            newPageRequestsButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            newPageRequestsButton.FlatAppearance.MouseDownBackColor = Color.Transparent





            generatedFW_UserAccessDiagnostic_BButton = New DashboardIconButton() With {
                .Name = "ActionKey_FW_UserAccessDiagnostic_B",
                .PageName = "FW_UserAccessDiagnostic_B",
                .Text = "User Access Diagnostic",
                .Location = DashboardGridLayout.CellLocation(1, 4),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = LoadDashboardIcon("Color_Information.png", SystemIcons.Question.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            generatedFW_UserAccessDiagnostic_BButton.FlatAppearance.BorderSize = 0
            generatedFW_UserAccessDiagnostic_BButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            generatedFW_UserAccessDiagnostic_BButton.FlatAppearance.MouseDownBackColor = Color.Transparent




            generatedFW_Employees_BButton = New DashboardIconButton() With {
                .Name = "ActionKey_FW_Employees_B",
                .PageName = "FW_Employees_B",
                .Text = "FW_Employees",
                .Location = DashboardGridLayout.CellLocation(1, 5),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = IconScaler.Load("Color_OK.png", DashboardIconSize, SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            generatedFW_Employees_BButton.FlatAppearance.BorderSize = 0
            generatedFW_Employees_BButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            generatedFW_Employees_BButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            updateSchemaButton = New DashboardIconButton() With {
                .Name = "ActionKey_UpdateSchema",
                .Text = "Update Schema",
                .Location = DashboardGridLayout.CellLocation(3, 3),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = IconScaler.Load("Color_Refresh.png", DashboardIconSize, SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            updateSchemaButton.FlatAppearance.BorderSize = 0
            updateSchemaButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            updateSchemaButton.FlatAppearance.MouseDownBackColor = Color.Transparent
            AddHandler Me.Load, AddressOf Dashboard_Application_Load
            AddHandler Me.Resize, AddressOf Dashboard_Application_Resize
            AddHandler rolesButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler rolesButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler rolesButton.Click, AddressOf RolesButton_Click
            AddHandler databaseConfigButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler databaseConfigButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler databaseConfigButton.Click, AddressOf DatabaseConfigButton_Click
            AddHandler companyDashboardButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler companyDashboardButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler companyDashboardButton.Click, AddressOf CompanyDashboardButton_Click
            AddHandler registrationButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler registrationButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler registrationButton.Click, AddressOf RegistrationButton_Click
            AddHandler auditHistoryButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler auditHistoryButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler auditHistoryButton.Click, AddressOf AuditHistoryButton_Click
            AddHandler helpDeskButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler helpDeskButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler helpDeskButton.Click, AddressOf HelpDeskButton_Click
            AddHandler helpDeskSupportButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler helpDeskSupportButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler helpDeskSupportButton.Click, AddressOf HelpDeskSupportButton_Click
            AddHandler updateSchemaButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler updateSchemaButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler updateSchemaButton.Click, AddressOf UpdateSchemaButton_Click
            AddHandler newPageRequestsButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler newPageRequestsButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler newPageRequestsButton.Click, AddressOf NewPageRequestsButton_Click
            AddHandler generatedFW_UserAccessDiagnostic_BButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler generatedFW_UserAccessDiagnostic_BButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler generatedFW_UserAccessDiagnostic_BButton.Click, AddressOf GeneratedFW_UserAccessDiagnostic_BButton_Click
            AddHandler generatedFW_Employees_BButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler generatedFW_Employees_BButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler generatedFW_Employees_BButton.Click, AddressOf GeneratedFW_Employees_BButton_Click
            AddHandler closeIconButton.Click, AddressOf CloseButton_Click
            Me.Controls.Add(generatedFW_UserAccessDiagnostic_BButton)
            Me.Controls.Add(generatedFW_Employees_BButton)
            Me.Controls.Add(topStripLabel)
            Me.Controls.Add(closeIconButton)
            Me.Controls.Add(headerLabel)
            Me.Controls.Add(databaseConfigButton)
            Me.Controls.Add(companyDashboardButton)
            Me.Controls.Add(rolesButton)
            Me.Controls.Add(registrationButton)
            Me.Controls.Add(auditHistoryButton)
            Me.Controls.Add(helpDeskButton)
            Me.Controls.Add(helpDeskSupportButton)
            Me.Controls.Add(newPageRequestsButton)
            Me.Controls.Add(updateSchemaButton)
        End Sub

        Private Sub Dashboard_Application_Load(sender As Object, e As EventArgs)
            ' Icons can be dragged between cells and stay where they are put. Wired here rather
            ' than in the constructor so the arrangement is read once the session is settled, and
            ' handed every icon on the form so a generated one is draggable without being listed.
            ' No session test: the arrangement is not scoped to a registration, and the user id is
            ' only stamped on the audit columns, so a missing session is worth nothing more than a
            ' zero there. Refusing to wire the drag over it would disable the feature for no reason.
            Dim session = SessionState.Current
            iconDragController = New DashboardIconDragController(Me,
                                                                 "Dashboard_Application",
                                                                 If(session.HasValue, session.Value.UserID, 0))
            iconDragController.Attach(Me.Controls.OfType(Of DashboardIconButton)().Cast(Of Control)())

            ' The chosen pictures, from the same controller the company dashboard uses. After the
            ' drag controller, so an icon is in its saved cell before its picture is looked up -
            ' the two are independent, but reading in this order keeps one story on screen.
            iconImageController = New IconImageController(Me,
                                                          "Dashboard_Application",
                                                          If(session.HasValue, session.Value.UserID, 0),
                                                          Function(fileName, fallback) IconScaler.Load(fileName, DashboardIconSize, fallback))
            iconImageController.Attach(Me.Controls.OfType(Of DashboardIconButton)().
                                       Select(Function(icon) New KeyValuePair(Of String, ButtonBase)(icon.Name, icon)))

            ' What the role calls the table behind each generated icon, from the same
            ' FW_RoleDetails override the ribbon tiles and the page titles read.
            '
            ' Generated icons only. Roles, User Admin, Registration, Audit History, Help Desk and
            ' the rest were captioned by whoever asked for them, and an override would overrule a
            ' deliberate name with a generic one. The table is written in rather than looked up,
            ' because the generator knows it - the page was generated from it.
            ActionCaptionOverrides.Apply(Me)

            rolesButton.Enabled = True
            registrationButton.Enabled = True
            auditHistoryButton.Enabled = True
            helpDeskButton.Enabled = True
            helpDeskSupportButton.Enabled = True
            newPageRequestsButton.Enabled = True
        End Sub

        Private Sub Dashboard_Application_Resize(sender As Object, e As EventArgs)
            rolesButton.Top = DashboardGridLayout.CellTop(1)
            generatedFW_Employees_BButton.Left = DashboardGridLayout.CellLeft(5)
            generatedFW_Employees_BButton.Top = DashboardGridLayout.CellTop(1)
            generatedFW_UserAccessDiagnostic_BButton.Left = DashboardGridLayout.CellLeft(4)
            generatedFW_UserAccessDiagnostic_BButton.Top = DashboardGridLayout.CellTop(1)
            registrationButton.Top = DashboardGridLayout.CellTop(1)
            auditHistoryButton.Top = DashboardGridLayout.CellTop(2)
            helpDeskButton.Top = DashboardGridLayout.CellTop(2)
            helpDeskSupportButton.Top = DashboardGridLayout.CellTop(2)
            newPageRequestsButton.Left = rolesButton.Left
            newPageRequestsButton.Top = DashboardGridLayout.CellTop(3)

            ' Last, because everything above pins icons to the cells written in this file. Anything
            ' that has been dragged elsewhere is laid back over the top; anything untouched keeps
            ' the position just set.
            iconDragController?.ApplySavedPositions()
        End Sub

        Private Sub RolesButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using roles As New Roles_B(currentUser, accessProfile, "ROLES")
                roles.ShowDialog(Me)
            End Using

            Dim ownerMenu = TryCast(Me.Owner, FW_MainMenu)
            If ownerMenu IsNot Nothing Then
                MenuFormInitializer.Configure(ownerMenu, currentUser, True)
            End If
        End Sub

        Private Sub RegistrationButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using frm As New FW_Registration_B()
                frm.ShowDialog(Me)
            End Using
        End Sub

        Private Sub AuditHistoryButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using frm As New FW_AuditTrail_B()
                frm.ShowDialog(Me)
            End Using
        End Sub

        Private Sub HelpDeskButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New FW_HD_Admin_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub

        Private Sub HelpDeskSupportButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New FW_HD_Issues_Support_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub

        ''' <summary>
        ''' Opens the database configuration behind a developer password prompt. The dashboard is
        ''' already Application Admin only; the prompt is there so this is never opened by a stray
        ''' click, since a wrong entry here takes the application off its database.
        ''' </summary>
        Private Sub CompanyDashboardButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()

            ' The user and access profile go through, as they do to every other page opened from
            ' here. A dashboard created without them would be a screen with no idea who is looking
            ' at it.
            Using dashboard As New Dashboard_Company(currentUser, accessProfile)
                dashboard.ShowDialog(Me)
            End Using
        End Sub


        ''' <summary>
        ''' Brings every role's field permissions back in line with the database.
        '''
        ''' Confirmed first, because a row for a column that no longer exists is physically
        ''' removed and there is nothing to restore it from. The counts come back afterwards
        ''' rather than being predicted, so the message says what happened rather than what was
        ''' expected to.
        ''' </summary>
        Private Sub UpdateSchemaButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()

            Dim warning = "Update field permissions for every role, in every registration, against the current database?" &
                          Environment.NewLine & Environment.NewLine &
                          "Columns that have been added get a new permission row, set inactive." & Environment.NewLine &
                          "Rows for columns that no longer exist are permanently deleted." & Environment.NewLine &
                          "Links that no longer match their column are repaired." & Environment.NewLine & Environment.NewLine &
                          "This cannot be undone."

            If MessageBox.Show(Me, warning, "Update Schema",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                               MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
                Return
            End If

            Dim updatedBy = If(SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)
            Dim result As DataAccess.SchemaSweepResult

            Dim previousCursor = Me.Cursor
            Me.Cursor = Cursors.WaitCursor
            Try
                result = DataAccess.SyncAllRoleFieldsWithSchema(updatedBy)
            Catch ex As Exception
                Me.Cursor = previousCursor
                MessageBox.Show(Me, "The schema update did not finish." & Environment.NewLine & Environment.NewLine & ex.Message,
                                "Update Schema", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            Finally
                Me.Cursor = previousCursor
            End Try

            Dim summary = $"Roles and tables visited: {result.RolesVisited}" & Environment.NewLine &
                          $"Fields added:             {result.Inserted}" & Environment.NewLine &
                          $"Fields deleted:           {result.Deleted}" & Environment.NewLine &
                          $"Links repaired:           {result.Repaired}"

            If result.Failures.Count > 0 Then
                summary &= Environment.NewLine & Environment.NewLine &
                           $"Could not be updated ({result.Failures.Count}):" & Environment.NewLine &
                           String.Join(Environment.NewLine, result.Failures.Take(10))
            End If

            MessageBox.Show(Me, summary, "Update Schema", MessageBoxButtons.OK,
                            If(result.Failures.Count > 0, MessageBoxIcon.Warning, MessageBoxIcon.Information))
        End Sub
        Private Sub DatabaseConfigButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()

            If Not DeveloperAccessGate.Prompt(Me, "Changing the database connection affects every user of this installation.") Then
                Return
            End If

            Using page As New FW_DatabaseConfig("Update the database credentials this application uses.")
                page.ShowDialog(Me)
            End Using
        End Sub

        ''' <summary>
        ''' Page generation is a desktop tool, and says so rather than half-working in a browser.
        '''
        ''' It writes .vb files into the workspace and shells out to dotnet to compile-check them.
        ''' In a VirtualUI session the compile check fails with "access is denied" - measured
        ''' 2026-09-14, with the full path to dotnet.exe, so it is the session rather than PATH -
        ''' and a generator that cannot compile what it wrote is worse than one that is closed:
        ''' it reports a pass it never established.
        '''
        ''' The rest of the page would work, which is exactly why this is refused up front.
        ''' </summary>
        Private Sub NewPageRequestsButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()

            If Program.InBrowserSession Then
                MessageBox.Show(Me,
                                "PAGE GENERATION RUNS ON THE DESKTOP." & Environment.NewLine & Environment.NewLine &
                                "It writes source files and compiles them to check they build, and the compile step cannot run inside a browser session." & Environment.NewLine & Environment.NewLine &
                                "Close this and start the application with 'run no tf'.",
                                "Page Generation",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information)
                Return
            End If
            Using page As New FW_PageGeneration_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub





        Private Sub GeneratedFW_UserAccessDiagnostic_BButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New FW_UserAccessDiagnostic_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub



        Private Sub GeneratedFW_Employees_BButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New FW_Employees_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub
        Private Sub IconButton_MouseEnter(sender As Object, e As EventArgs)
            Dim button = TryCast(sender, Button)
            If button Is Nothing Then
                Return
            End If

            button.BackColor = Color.FromArgb(232, 245, 255)
            button.FlatAppearance.BorderSize = 1
            button.FlatAppearance.BorderColor = Color.FromArgb(91, 161, 217)
        End Sub

        Private Sub IconButton_MouseLeave(sender As Object, e As EventArgs)
            Dim button = TryCast(sender, Button)
            If button Is Nothing Then
                Return
            End If

            button.BackColor = Color.Transparent
            button.FlatAppearance.BorderSize = 0
        End Sub

        Private Sub ResetIconButtonVisuals()
            rolesButton.BackColor = Color.Transparent
            rolesButton.FlatAppearance.BorderSize = 0
            registrationButton.BackColor = Color.Transparent
            registrationButton.FlatAppearance.BorderSize = 0
            auditHistoryButton.BackColor = Color.Transparent
            auditHistoryButton.FlatAppearance.BorderSize = 0
            helpDeskButton.BackColor = Color.Transparent
            helpDeskButton.FlatAppearance.BorderSize = 0
            helpDeskSupportButton.BackColor = Color.Transparent
            helpDeskSupportButton.FlatAppearance.BorderSize = 0
            newPageRequestsButton.BackColor = Color.Transparent
            newPageRequestsButton.FlatAppearance.BorderSize = 0
        End Sub

        ''' A dashboard tile is 150 x 118 with the caption below the picture, so the icon is given 80.
        Private Const DashboardIconSize As Integer = 80

        Private Function LoadDashboardIcon(fileName As String, fallback As Image) As Image
            Return IconScaler.Load(fileName, DashboardIconSize, fallback)
        End Function

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub
    End Class
End Namespace
