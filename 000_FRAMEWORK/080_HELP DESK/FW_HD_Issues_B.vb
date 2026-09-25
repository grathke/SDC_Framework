Option Strict On
Option Explicit On

Imports System.Data
Imports System
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class FW_HD_Issues_B
        Inherits FW_Base_B

        ''' The page the listing is limited to. Empty means every page - the main menu route, where
        ''' the user is looking at everything they have raised.
        Private ReadOnly reportingPage As String = String.Empty

        ''' The originating page's own caption, so the title names the page the way the user saw it
        ''' rather than by its class name.
        Private ReadOnly reportingPageTitle As String = String.Empty

        Public Sub New(Optional reportedFromPage As String = "", Optional reportedFromPageTitle As String = "")
            MyBase.New(SessionState.CurrentUser(),
                       MenuFormInitializer.BuildAccessProfileForCurrentSession(SessionState.CurrentUser(), "FW_HD_Issues_B"),
                       "FW_HD_Issues")
            reportingPage = If(reportedFromPage, String.Empty).Trim()
            reportingPageTitle = If(reportedFromPageTitle, String.Empty).Trim()
            Me.Text = If(reportingPage = String.Empty,
                         "Help Desk Issues Listing - USER",
                         "Help Desk Issues - " & DisplayNameFormatter.ToPageDisplayName(reportingPage))
            AddHandler Me.Load, AddressOf UserPage_Load
        End Sub


        ''' <summary>
        ''' Raised from a page the listing is already filtered to it, so the title says so rather
        ''' than repeating the page name the user just came from. From the menu it is every ticket
        ''' they raised, and the derived name is right.
        ''' </summary>
        Protected Overrides Function BuildBrowseListingTitle(registrationId As Integer, tableName As String) As String
            If reportingPage = String.Empty Then Return MyBase.BuildBrowseListingTitle(registrationId, tableName)
            If reportingPageTitle <> String.Empty Then Return "HD Issues Listing For " & reportingPageTitle
            Return "HD Issues Listing For " & DisplayNameFormatter.ToPageDisplayName(reportingPage)
        End Function

        ''' <summary>
        ''' A user sees their own tickets, in their own registration - the registration scope is
        ''' Base_B's. Raised from a page, the list narrows to that page as well, so the first thing
        ''' the user sees is whether the thing they are about to report is already reported.
        ''' </summary>
        Protected Overrides Function GetBrowseUserScopePredicate() As String
            If reportingPage = String.Empty Then Return "i.ReporterUserID = @UserID"
            Return "i.ReporterUserID = @UserID AND i.ReportedFromPage = '" & reportingPage.Replace("'", "''") & "'"
        End Function

        Protected Overrides Function GetBrowseUserId() As Integer
            Return SessionState.SessionUserID
        End Function

        Protected Overrides Function HandleCustomCreateAction() As Boolean
            Using page As New FW_HD_Issues_U(0, GetSessionRegistrationId(), reportingPage)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction()
                    FitUserGridColumns()
                End If
            End Using
            Return True
        End Function

        Protected Overrides Function HandleCustomReadAction() As Boolean
            Dim id = SelectedIssueId()
            If Not id.HasValue Then
                MessageBox.Show(Me, "Select an issue first.", "Read", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            Using page As New FW_HD_Issues_U(id.Value, GetSessionRegistrationId())
                page.ShowDialog(Me)
            End Using
            Return True
        End Function

        Protected Overrides Function HandleCustomUpdateAction(recordId As Integer) As Boolean
            Dim registrationId = GetSessionRegistrationId()
            Using page As New FW_HD_Issues_U(recordId, registrationId)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction(recordId)
                    FitUserGridColumns()
                End If
            End Using
            Return True
        End Function

        Protected Overrides Function HandleCustomDeleteAction() As Boolean
            If Not SwitchedUserGuard.AllowWrite(Me, "DELETE AN ISSUE") Then Return True

            Dim id = SelectedIssueId()
            If Not id.HasValue Then
                MessageBox.Show(Me, "Select an issue first.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            If MessageBox.Show(Me, "Delete the selected Help Desk issue?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                HelpDeskDataAccess.DeleteIssue(id.Value, GetSessionRegistrationId(), SessionState.ActingUserID)
                RefreshGridForCustomAction()
                FitUserGridColumns()
            End If
            Return True
        End Function

        Private Sub UserPage_Load(sender As Object, e As EventArgs)
            FitUserGridColumns()
        End Sub

        Private Sub FitUserGridColumns()
            Dim matchingControls = Controls.Find("browseGrid", True)
            If matchingControls.Length = 0 Then Return

            Dim grid = TryCast(matchingControls(0), DataGridView)
            If grid Is Nothing OrElse grid.Columns.Count = 0 Then Return

            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            For Each column As DataGridViewColumn In grid.Columns
                If Not column.Visible Then Continue For

                If String.Equals(column.Name, "Subject", StringComparison.OrdinalIgnoreCase) Then
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
                    column.MinimumWidth = 180
                    column.FillWeight = 100.0F
                Else
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None
                    grid.AutoResizeColumn(column.Index, DataGridViewAutoSizeColumnMode.AllCells)
                    column.Width = Math.Max(70, Math.Min(column.Width, 220))
                End If
            Next
        End Sub

        Private Function SelectedIssueId() As Integer?
            Return GetSelectedRecordIdForCustomAction()
        End Function
    End Class
End Namespace