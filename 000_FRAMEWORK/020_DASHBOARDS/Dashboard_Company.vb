Option Strict On
Option Explicit On

Imports System.Linq
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class Dashboard_Company
        Inherits Form

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        Private ReadOnly topStripLabel As Label
        Private ReadOnly headerLabel As Label
        Private ReadOnly rolesButton As DashboardIconButton
        Private ReadOnly userDiagnosticButton As DashboardIconButton
        Private iconDragController As DashboardIconDragController
        Private iconImageController As IconImageController

        ''' The size a chosen picture is scaled to. The icons written into this form are system
        ''' glyphs at their own size; a file graphic needs a target, and 80 is the one the
        ''' application dashboard uses, so the two look alike when the same picture is chosen.
        Private Const DashboardIconSize As Integer = 80
        Private ReadOnly closeIconButton As Button
        Private ReadOnly generatedFW_Employees_BButton As DashboardIconButton

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = user
            accessProfile = profile

            Me.Text = "DASHBOARD: COMPANY"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.MinimumSize = New Size(DashboardGridLayout.StandardClientWidth, DashboardGridLayout.StandardClientHeight)
            Me.ClientSize = New Size(DashboardGridLayout.StandardClientWidth, DashboardGridLayout.StandardClientHeight)
            Me.BackColor = Color.White

            topStripLabel = New Label() With {
                .Text = "DASHBOARD: COMPANY",
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
                .Text = "DASHBOARD: COMPANY",
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
                .Image = IconScaler.Load("Color_Shield.png", DashboardIconSize, SystemIcons.Application.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            rolesButton.FlatAppearance.BorderSize = 0
            rolesButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            rolesButton.FlatAppearance.MouseDownBackColor = Color.Transparent

            userDiagnosticButton = New DashboardIconButton() With {
                .Name = "ActionKey_FW_UserAccessDiagnostic_B",
                .PageName = "FW_UserAccessDiagnostic_B",
                .Text = "User Access Diagnostic",
                .Location = DashboardGridLayout.CellLocation(1, 4),
                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),
                .BackColor = Color.Transparent,
                .UseVisualStyleBackColor = False,
                .FlatStyle = FlatStyle.Flat,
                .Font = New Font("Segoe UI", 13.0F, FontStyle.Regular),
                .Image = IconScaler.Load("Color_Information.png", DashboardIconSize, SystemIcons.Question.ToBitmap()),
                .TextImageRelation = TextImageRelation.ImageAboveText,
                .ImageAlign = ContentAlignment.TopCenter,
                .TextAlign = ContentAlignment.BottomCenter,
                .TabStop = False
            }
            userDiagnosticButton.FlatAppearance.BorderSize = 0
            userDiagnosticButton.FlatAppearance.MouseOverBackColor = Color.Transparent
            userDiagnosticButton.FlatAppearance.MouseDownBackColor = Color.Transparent


            generatedFW_Employees_BButton = New DashboardIconButton() With {
                .Name = "ActionKey_FW_Employees_B",
                .PageName = "FW_Employees_B",
                .Text = "FW_Employees",
                .Location = DashboardGridLayout.CellLocation(1, 2),
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
            AddHandler Me.Load, AddressOf Dashboard_Company_Load
            AddHandler Me.Resize, AddressOf Dashboard_Company_Resize
            AddHandler rolesButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler rolesButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler rolesButton.Click, AddressOf RolesButton_Click
            AddHandler userDiagnosticButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler userDiagnosticButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler userDiagnosticButton.Click, AddressOf UserDiagnosticButton_Click
            AddHandler generatedFW_Employees_BButton.MouseEnter, AddressOf IconButton_MouseEnter
            AddHandler generatedFW_Employees_BButton.MouseLeave, AddressOf IconButton_MouseLeave
            AddHandler generatedFW_Employees_BButton.Click, AddressOf GeneratedFW_Employees_BButton_Click
            AddHandler closeIconButton.Click, AddressOf CloseButton_Click

            Me.Controls.Add(generatedFW_Employees_BButton)
            Me.Controls.Add(topStripLabel)
            Me.Controls.Add(closeIconButton)
            Me.Controls.Add(headerLabel)
            Me.Controls.Add(rolesButton)
            Me.Controls.Add(userDiagnosticButton)
        End Sub

        Private Sub Dashboard_Company_Load(sender As Object, e As EventArgs)
            ' The same arrangement behaviour as the admin dashboard, from the same controller.
            Dim session = SessionState.Current
            iconDragController = New DashboardIconDragController(Me,
                                                                 "Dashboard_Company",
                                                                 If(session.HasValue, session.Value.UserID, 0))
            iconDragController.Attach(Me.Controls.OfType(Of DashboardIconButton)().Cast(Of Control)())

            ' The chosen pictures, from the same controller the admin dashboard uses. The App Admin
            ' test inside it matters here: a Company Admin can open this dashboard and must not be
            ' able to re-picture its icons.
            iconImageController = New IconImageController(Me,
                                                          "Dashboard_Company",
                                                          If(session.HasValue, session.Value.UserID, 0),
                                                          Function(fileName, fallback) IconScaler.Load(fileName, DashboardIconSize, fallback))
            iconImageController.Attach(Me.Controls.OfType(Of DashboardIconButton)().
                                       Select(Function(icon) New KeyValuePair(Of String, ButtonBase)(icon.Name, icon)))

            ' The same override the App Admin dashboard, the ribbon and the page titles read, so a
            ' rename cannot show on one surface and not another.
            '
            ' Only the generated icon. Roles and User Admin were captioned by whoever asked for
            ' them and are left alone.
            ActionCaptionOverrides.Apply(Me)

            rolesButton.Enabled = True
            userDiagnosticButton.Enabled = True
        End Sub

        Private Sub Dashboard_Company_Resize(sender As Object, e As EventArgs)
            rolesButton.Top = DashboardGridLayout.CellTop(1)
            generatedFW_Employees_BButton.Left = DashboardGridLayout.CellLeft(2)
            generatedFW_Employees_BButton.Top = DashboardGridLayout.CellTop(1)
            userDiagnosticButton.Top = rolesButton.Top

            ' Last, so a dragged arrangement is laid back over the cells this file pins.
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

        Private Sub UserDiagnosticButton_Click(sender As Object, e As EventArgs)
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
            userDiagnosticButton.BackColor = Color.Transparent
            userDiagnosticButton.FlatAppearance.BorderSize = 0
        End Sub

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub
    End Class
End Namespace
