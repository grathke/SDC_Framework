Option Strict On
Option Explicit On

Imports System.Data
Imports System
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class FW_HD_Issues_B
        Inherits FW_Base_B

        Public Sub New()
            MyBase.New(BuildCurrentUserFromSession(),
                       MenuFormInitializer.BuildAccessProfileForCurrentSession(BuildCurrentUserFromSession(), "FW_HD_Issues_B"),
                       "FW_HD_Issues")
            Me.Text = "Help Desk Issues Listing - USER"
            AddHandler Me.Load, AddressOf UserPage_Load
        End Sub

        Protected Overrides Function GetBrowseUserScopePredicate() As String
            Return "i.ReporterUserID = @UserID"
        End Function

        Protected Overrides Function GetBrowseUserId() As Integer
            Return CurrentUserId()
        End Function

        Protected Overrides Function HandleCustomCreateAction() As Boolean
            Using page As New FW_HD_Issues_U(0, GetSessionRegistrationId())
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
            Dim id = SelectedIssueId()
            If Not id.HasValue Then
                MessageBox.Show(Me, "Select an issue first.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            If MessageBox.Show(Me, "Delete the selected Help Desk issue?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                HelpDeskDataAccess.DeleteIssue(id.Value, GetSessionRegistrationId(), CurrentUserId())
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

        Private Function CurrentUserId() As Integer
            Dim session = SessionState.Current
            If session.HasValue Then Return session.Value.UserID
            Return 0
        End Function

        Private Shared Function BuildCurrentUserFromSession() As UserContext
            Dim session = SessionState.Current
            If session.HasValue Then
                Return New UserContext With {.UserId = session.Value.UserID, .FirstName = session.Value.FirstName, .LastName = session.Value.LastName}
            End If
            Return New UserContext()
        End Function
    End Class
End Namespace