Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class Dashboard_Application
        Inherits Form

        Private Class DashboardIconButton
            Inherits Button

            Protected Overrides ReadOnly Property ShowFocusCues As Boolean
                Get
                    Return False
                End Get
            End Property
        End Class

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        Private ReadOnly topStripLabel As Label
        Private ReadOnly headerLabel As Label
        Private ReadOnly rolesButton As DashboardIconButton
        Private ReadOnly userAdminButton As DashboardIconButton
        Private ReadOnly registrationButton As DashboardIconButton
        Private ReadOnly auditHistoryButton As DashboardIconButton
        Private ReadOnly helpDeskButton As DashboardIconButton
        Private ReadOnly helpDeskSupportButton As DashboardIconButton
        Private ReadOnly newPageRequestsButton As DashboardIconButton
        Private ReadOnly closeIconButton As Button
        Private ReadOnly generatedFW_UserAccessExplanation_BButton As DashboardIconButton
        Private ReadOnly generatedEntityX_BButton As DashboardIconButton
        Private ReadOnly generatedEntityA_BButton As DashboardIconButton
        Private ReadOnly generatedEntityY_BButton As DashboardIconButton

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = user
            accessProfile = profile

            Me.Text = "Dashboard_Application"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.MinimumSize = New Size(DashboardGridLayout.StandardClientWidth, DashboardGridLayout.StandardClientHeight)
            Me.ClientSize = New Size(DashboardGridLayout.StandardClientWidth, DashboardGridLayout.StandardClientHeight)
            Me.BackColor = Color.White

            topStripLabel = New Label() With {
                .Text = "Dashboard_Application",
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
                .Text = "Dashboard_Application",
                .Location = New Point(0, 48),
                .Size = New Size(Me.ClientSize.Width, 38),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Font = New Font("Segoe UI", 17.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(42, 42, 42),
                .TextAlign = ContentAlignment.MiddleCenter
            }

            rolesButton = New DashboardIconButton() With {
                .Text = "Roles",
                .Location = DashboardGridLayout.CellLocation(1, 1),
                .Size = New Size(130, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Information.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            rolesButton.FlatAppearance.BorderSize = 0
            rolesButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            rolesButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            userAdminButton = New DashboardIconButton() With {
                .Text = "User Admin",
                .Location = DashboardGridLayout.CellLocation(1, 2),
                .Size = New Size(130, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Shield.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            userAdminButton.FlatAppearance.BorderSize = 0
            userAdminButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            userAdminButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            registrationButton = New DashboardIconButton() With {
                .Text = "Registration",
                .Location = DashboardGridLayout.CellLocation(1, 3),
                .Size = New Size(130, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Application.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            registrationButton.FlatAppearance.BorderSize = 0
            registrationButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            registrationButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            auditHistoryButton = New DashboardIconButton() With {
                .Text = "Audit History",
                .Location = DashboardGridLayout.CellLocation(2, 1),
                .Size = New Size(130, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Warning.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            auditHistoryButton.FlatAppearance.BorderSize = 0
            auditHistoryButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            auditHistoryButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            helpDeskButton = New DashboardIconButton() With {
                .Text = "Help Desk",
                .Location = DashboardGridLayout.CellLocation(2, 2),
                .Size = New Size(130, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Question.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            helpDeskButton.FlatAppearance.BorderSize = 0
            helpDeskButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            helpDeskButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            helpDeskSupportButton = New DashboardIconButton() With {
                .Text = "HD Support Tickets",
                .Location = DashboardGridLayout.CellLocation(2, 3),
                .Size = New Size(130, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 11.0F, FontStyle.Regular),
                .Image = SystemIcons.Question.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            helpDeskSupportButton.FlatAppearance.BorderSize = 0
            helpDeskSupportButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            helpDeskSupportButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            newPageRequestsButton = New DashboardIconButton() With {
                .Text = "Page Gen _B _U",
                .Location = DashboardGridLayout.CellLocation(3, 1),
                .Size = New Size(150, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Application.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            newPageRequestsButton.FlatAppearance.BorderSize = 0
            newPageRequestsButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            newPageRequestsButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            generatedEntityY_BButton = New DashboardIconButton() With {
                .Name = "GeneratedPageActionKey_EntityY_B",
                .Text = "EntityY",
                .Location = DashboardGridLayout.CellLocation(3, 4),
                .Size = New Size(150, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Application.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            generatedEntityY_BButton.FlatAppearance.BorderSize = 0
            generatedEntityY_BButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            generatedEntityY_BButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            generatedEntityA_BButton = New DashboardIconButton() With {
                .Name = "GeneratedPageActionKey_EntityA_B",
                .Text = "EntityA",
                .Location = DashboardGridLayout.CellLocation(3, 2),
                .Size = New Size(150, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Application.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            generatedEntityA_BButton.FlatAppearance.BorderSize = 0
            generatedEntityA_BButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            generatedEntityA_BButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            generatedEntityX_BButton = New DashboardIconButton() With {
                .Name = "GeneratedPageActionKey_EntityX_B",
                .Text = "EntityX",
                .Location = DashboardGridLayout.CellLocation(3, 3),
                .Size = New Size(150, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Application.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            generatedEntityX_BButton.FlatAppearance.BorderSize = 0
            generatedEntityX_BButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            generatedEntityX_BButton.FlatAppearance.MouseDownBackColor = Color.Transparent


            generatedFW_UserAccessExplanation_BButton = New DashboardIconButton() With {
                .Name = "GeneratedPageActionKey_FW_UserAccessExplanation_B",
                .Text = "User Access Explanation",
                .Location = DashboardGridLayout.CellLocation(1, 4),
                .Size = New Size(150, 118),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = SystemIcons.Question.ToBitmap(),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            generatedFW_UserAccessExplanation_BButton.FlatAppearance.BorderSize = 0
            generatedFW_UserAccessExplanation_BButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            generatedFW_UserAccessExplanation_BButton.FlatAppearance.MouseDownBackColor = Color.Transparent
            AddHandler Me.Load, AddressOf Dashboard_Application_Load
            AddHandler Me.Resize, AddressOf Dashboard_Application_Resize
            AddHandler rolesButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler rolesButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler rolesButton.Click, AddressOf RolesButton_Click
            AddHandler userAdminButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler userAdminButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler userAdminButton.Click, AddressOf UserAdminButton_Click
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
            AddHandler newPageRequestsButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler newPageRequestsButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler newPageRequestsButton.Click, AddressOf NewPageRequestsButton_Click
            AddHandler generatedEntityY_BButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler generatedEntityY_BButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler generatedEntityY_BButton.Click, AddressOf GeneratedEntityY_BButton_Click
            AddHandler generatedEntityA_BButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler generatedEntityA_BButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler generatedEntityA_BButton.Click, AddressOf GeneratedEntityA_BButton_Click
            AddHandler generatedEntityX_BButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler generatedEntityX_BButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler generatedEntityX_BButton.Click, AddressOf GeneratedEntityX_BButton_Click
            AddHandler generatedFW_UserAccessExplanation_BButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler generatedFW_UserAccessExplanation_BButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler generatedFW_UserAccessExplanation_BButton.Click, AddressOf GeneratedFW_UserAccessExplanation_BButton_Click
            AddHandler closeIconButton.Click, AddressOf CloseButton_Click

            Me.Controls.Add(generatedEntityY_BButton)
            Me.Controls.Add(generatedEntityA_BButton)
            Me.Controls.Add(generatedEntityX_BButton)
            Me.Controls.Add(generatedFW_UserAccessExplanation_BButton)
            Me.Controls.Add(topStripLabel)
            Me.Controls.Add(closeIconButton)
            Me.Controls.Add(headerLabel)
            Me.Controls.Add(rolesButton)
            Me.Controls.Add(userAdminButton)
            Me.Controls.Add(registrationButton)
            Me.Controls.Add(auditHistoryButton)
            Me.Controls.Add(helpDeskButton)
            Me.Controls.Add(helpDeskSupportButton)
            Me.Controls.Add(newPageRequestsButton)
        End Sub

        Private Sub Dashboard_Application_Load(sender As Object, e As EventArgs)
            rolesButton.Enabled = True
            userAdminButton.Enabled = True
            registrationButton.Enabled = True
            auditHistoryButton.Enabled = True
            helpDeskButton.Enabled = True
            helpDeskSupportButton.Enabled = True
            newPageRequestsButton.Enabled = True
        End Sub

        Private Sub Dashboard_Application_Resize(sender As Object, e As EventArgs)
            rolesButton.Top = DashboardGridLayout.CellTop(1)
            generatedFW_UserAccessExplanation_BButton.Left = DashboardGridLayout.CellLeft(4)
            generatedFW_UserAccessExplanation_BButton.Top = DashboardGridLayout.CellTop(1)
            generatedEntityX_BButton.Left = DashboardGridLayout.CellLeft(3)
            generatedEntityX_BButton.Top = DashboardGridLayout.CellTop(3)
            generatedEntityA_BButton.Left = DashboardGridLayout.CellLeft(2)
            generatedEntityA_BButton.Top = DashboardGridLayout.CellTop(3)
            generatedEntityY_BButton.Left = DashboardGridLayout.CellLeft(4)
            generatedEntityY_BButton.Top = DashboardGridLayout.CellTop(3)
            userAdminButton.Top = DashboardGridLayout.CellTop(1)
            registrationButton.Top = DashboardGridLayout.CellTop(1)
            auditHistoryButton.Top = DashboardGridLayout.CellTop(2)
            helpDeskButton.Top = DashboardGridLayout.CellTop(2)
            helpDeskSupportButton.Top = DashboardGridLayout.CellTop(2)
            newPageRequestsButton.Left = rolesButton.Left
            newPageRequestsButton.Top = DashboardGridLayout.CellTop(3)
        End Sub

        Private Sub RolesButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using roles As New Roles_B(currentUser, accessProfile, "ROLES")
                roles.ShowDialog(Me)
            End Using

            Dim ownerMenu = TryCast(Me.Owner, MainMenu)
            If ownerMenu IsNot Nothing Then
                MenuFormInitializer.Configure(ownerMenu, currentUser, True)
            End If
        End Sub

        Private Sub UserAdminButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using frm As New Users_AppAdmin_B(accessProfile)
                frm.ShowDialog(Me)
            End Using
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

        Private Sub NewPageRequestsButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New FW_PageGeneration_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub

        Private Sub GeneratedEntityY_BButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New EntityY_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub

        Private Sub GeneratedEntityA_BButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New EntityA_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub

        Private Sub GeneratedEntityX_BButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New EntityX_B(currentUser, accessProfile)
                page.ShowDialog(Me)
            End Using
        End Sub


        Private Sub GeneratedFW_UserAccessExplanation_BButton_Click(sender As Object, e As EventArgs)
            ResetIconButtonVisuals()
            Using page As New FW_UserAccessExplanation_B(currentUser, accessProfile)
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
            userAdminButton.BackColor = Color.Transparent
            userAdminButton.FlatAppearance.BorderSize = 0
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

        Private Function LoadDashboardIcon(fileName As String, fallback As Image) As Image
            Dim candidates As String() = {
                Path.Combine(Application.StartupPath, "assets", "images", fileName),
                Path.Combine(Application.StartupPath, "..", "..", "..", "assets", "images", fileName),
                Path.Combine(Application.StartupPath, "..", "..", "..", "..", "assets", "images", fileName)
            }

            For Each candidate In candidates
                Dim fullPath = Path.GetFullPath(candidate)
                If File.Exists(fullPath) Then
                    Return Image.FromFile(fullPath)
                End If
            Next

            Return fallback
        End Function

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub
    End Class
End Namespace