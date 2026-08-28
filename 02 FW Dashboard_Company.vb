Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class Dashboard_Company
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
        Private ReadOnly userDiagnosticButton As DashboardIconButton
        Private ReadOnly closeIconButton As Button

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = user
            accessProfile = profile

            Me.Text = "Dashboard_Company"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.MinimumSize = New Size(DashboardGridLayout.StandardClientWidth, DashboardGridLayout.StandardClientHeight)
            Me.ClientSize = New Size(DashboardGridLayout.StandardClientWidth, DashboardGridLayout.StandardClientHeight)
            Me.BackColor = Color.White

            topStripLabel = New Label() With {
                .Text = "Dashboard_Company",
                .Location = New Point(0, 0),
                .Size = New Size(Me.ClientSize.Width, 28),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .BackColor = Color.FromArgb(0, 136, 172),
                .ForeColor = Color.White,
                .Font = New Font("Segoe UI", 10.0F, FontStyle.Regular),
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
                .Text = "Dashboard_Company",
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

            userDiagnosticButton = New DashboardIconButton() With {
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
            userDiagnosticButton.FlatAppearance.BorderSize = 0
            userDiagnosticButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            userDiagnosticButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            AddHandler Me.Load, AddressOf Dashboard_Company_Load
            AddHandler Me.Resize, AddressOf Dashboard_Company_Resize
            AddHandler rolesButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler rolesButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler rolesButton.Click, AddressOf RolesButton_Click
            AddHandler userAdminButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler userAdminButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler userAdminButton.Click, AddressOf UserAdminButton_Click
            AddHandler userDiagnosticButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler userDiagnosticButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler userDiagnosticButton.Click, AddressOf UserDiagnosticButton_Click
            AddHandler closeIconButton.Click, AddressOf CloseButton_Click

            Me.Controls.Add(topStripLabel)
            Me.Controls.Add(closeIconButton)
            Me.Controls.Add(headerLabel)
            Me.Controls.Add(rolesButton)
            Me.Controls.Add(userAdminButton)
            Me.Controls.Add(userDiagnosticButton)
        End Sub

        Private Sub Dashboard_Company_Load(sender As Object, e As EventArgs)
            rolesButton.Enabled = True
            userAdminButton.Enabled = True
            userDiagnosticButton.Enabled = True
        End Sub

        Private Sub Dashboard_Company_Resize(sender As Object, e As EventArgs)
            rolesButton.Top = DashboardGridLayout.CellTop(1)
            userAdminButton.Top = rolesButton.Top
            userDiagnosticButton.Top = rolesButton.Top
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

        Private Sub UserDiagnosticButton_Click(sender As Object, e As EventArgs)
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
            userDiagnosticButton.BackColor = Color.Transparent
            userDiagnosticButton.FlatAppearance.BorderSize = 0
        End Sub

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub
    End Class
End Namespace
