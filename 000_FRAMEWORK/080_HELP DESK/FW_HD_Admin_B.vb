Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class FW_HD_Admin_B
        Inherits Form

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly tabs As TabControl
        Private ReadOnly dashboardTab As TabPage
        Private ReadOnly ticketsTab As TabPage
        Private dashboardPage As FW_HD_AdminDashboard_B
        Private supportPage As FW_HD_Issues_Support_B
        Private wasOnTicketsTab As Boolean
        Private closingAdminPage As Boolean

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            currentUser = If(user, SessionState.CurrentUser())
            accessProfile = If(profile, MenuFormInitializer.BuildAccessProfileForCurrentSession(currentUser, NameOf(FW_HD_Admin_B)))

            Text = "Admin Help Desk"
            StartPosition = FormStartPosition.CenterParent
            ' Not resizable by dragging. Nearly every run of this application is a VirtualUI
            ' session, where a window has no business growing past the canvas it is drawn on, and
            ' a page that can be dragged wider fights both the zoom and its own layout. The
            ' application still sizes the window itself - F9, and Hot Fields widening a browse page -
            ' because a fixed border only stops the drag handles, not code.
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimumSize = New Size(1100, 700)
            ClientSize = New Size(1250, 820)
            BackColor = Color.White

            tabs = New TabControl With {.Dock = DockStyle.Fill}
            dashboardTab = New TabPage("Dashboard") With {.BackColor = Color.White, .Padding = New Padding(0)}
            ticketsTab = New TabPage("Support Tickets") With {.BackColor = Color.White, .Padding = New Padding(0)}
            tabs.TabPages.Add(dashboardTab)
            tabs.TabPages.Add(ticketsTab)
            Controls.Add(tabs)

            LoadDashboardPage()
            LoadSupportPage()

            AddHandler tabs.SelectedIndexChanged, AddressOf Tabs_SelectedIndexChanged
            AddHandler dashboardPage.FormClosing, AddressOf DashboardPage_FormClosing
            AddHandler Me.FormClosing, AddressOf AdminPage_FormClosing
            AddHandler Me.Shown, AddressOf AdminPage_Shown
        End Sub

        Private Sub LoadDashboardPage()
            dashboardPage = New FW_HD_AdminDashboard_B(currentUser, accessProfile) With {
                .TopLevel = False,
                .FormBorderStyle = FormBorderStyle.None,
                .Dock = DockStyle.Fill
            }
            dashboardTab.Controls.Add(dashboardPage)
            dashboardPage.Show()
        End Sub

        Private Sub LoadSupportPage()
            supportPage = New FW_HD_Issues_Support_B(currentUser, accessProfile) With {
                .TopLevel = False,
                .FormBorderStyle = FormBorderStyle.None,
                .Dock = DockStyle.Fill
            }
            ticketsTab.Controls.Add(supportPage)
            AddHandler supportPage.FormClosing, AddressOf SupportPage_FormClosing
            supportPage.Show()
        End Sub

        Private Sub AdminPage_Shown(sender As Object, e As EventArgs)
            dashboardPage.RefreshDashboard()
        End Sub

        Private Sub Tabs_SelectedIndexChanged(sender As Object, e As EventArgs)
            If tabs.SelectedTab Is ticketsTab Then
                wasOnTicketsTab = True
            ElseIf wasOnTicketsTab Then
                wasOnTicketsTab = False
                dashboardPage.RefreshDashboard()
            End If
        End Sub

        Private Sub SupportPage_FormClosing(sender As Object, e As FormClosingEventArgs)
            If closingAdminPage Then Return
            e.Cancel = True
            Close()
        End Sub

        Private Sub DashboardPage_FormClosing(sender As Object, e As FormClosingEventArgs)
            If closingAdminPage Then Return
            e.Cancel = True
            Close()
        End Sub

        Private Sub AdminPage_FormClosing(sender As Object, e As FormClosingEventArgs)
            closingAdminPage = True
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                If dashboardPage IsNot Nothing Then
                    dashboardPage.Dispose()
                    dashboardPage = Nothing
                End If
                If supportPage IsNot Nothing Then
                    supportPage.Dispose()
                    supportPage = Nothing
                End If
            End If
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Namespace
