Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class FW_HD_Issues_Support_B
        Inherits FW_Base_B

        Private ReadOnly registrationLabel As Label
        Private ReadOnly registrationComboBox As ComboBox
        Private _selectedRegistrationId As Integer
        Private _loadingRegistrations As Boolean

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(If(user, BuildCurrentUserFromSession()),
                       If(profile, MenuFormInitializer.BuildAccessProfileForCurrentSession(If(user, BuildCurrentUserFromSession()), "FW_HD_Issues_Support_B")),
                       "FW_HD_Issues")

            Me.Text = "Help Desk Issues Listing - SUPPORT"

            _selectedRegistrationId = GetSessionRegistrationId()
            registrationLabel = New Label With {.Text = "Registration:", .AutoSize = True, .ForeColor = Color.DimGray}
            registrationComboBox = New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .DropDownWidth = 280, .Size = New Size(275, 26)}

            AddHandler registrationComboBox.SelectedIndexChanged, AddressOf RegistrationComboBox_SelectedIndexChanged
            AddHandler Me.Load, AddressOf SupportPage_Load
            AddHandler Me.Resize, AddressOf SupportPage_Resize

            Controls.Add(registrationLabel)
            Controls.Add(registrationComboBox)
            registrationLabel.BringToFront()
            registrationComboBox.BringToFront()
            SupportPage_Resize(Me, EventArgs.Empty)
        End Sub

        ''' The support side of the same table - named so it cannot be mistaken for the user
        ''' listing, which the derived name would not distinguish.
        Protected Overrides Function BuildBrowseListingTitle(registrationId As Integer, tableName As String) As String
            Return "Help Desk Issues - Support"
        End Function

        Protected Overrides Function ResolveCurrentRoleFieldTableName() As String
            Return "FW_HD_Issues"
        End Function

        ''' <summary>
        ''' An application or company administrator sees every ticket in the registration. Everyone
        ''' else sees only the tickets they reported. Applied as a query predicate, so it holds for
        ''' the grid, QBE and every action that resolves a row from it.
        ''' </summary>
        Protected Overrides Function GetBrowseUserScopePredicate() As String
            If SeesAllRegistrationIssues() Then Return String.Empty
            Return "i.ReporterUserID = @UserID"
        End Function

        Protected Overrides Function GetBrowseUserId() As Integer
            If SeesAllRegistrationIssues() Then Return 0
            Return CurrentUserId()
        End Function

        Private Function SeesAllRegistrationIssues() As Boolean
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return False
            Dim session = SessionState.Current.Value
            Return session.IsApplicationAdminRole OrElse session.IsCompanyAdminRole
        End Function

        Protected Overrides Function TryGetActiveRegistrationId(ByRef registrationId As Integer) As Boolean
            If _selectedRegistrationId > 0 Then
                registrationId = _selectedRegistrationId
                Return True
            End If

            MessageBox.Show(Me, "Select a registration first.", "Selection Required", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return False
        End Function

        Protected Overrides Function HandleCustomReadAction() As Boolean
            Return OpenSelectedIssue(False)
        End Function

        Protected Overrides Function HandleCustomUpdateAction(recordId As Integer) As Boolean
            Using page As New FW_HD_Issues_U(recordId, _selectedRegistrationId)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then RefreshGridForCustomAction(recordId)
            End Using
            Return True
        End Function

        Protected Overrides Function HandleCustomDeleteAction() As Boolean
            Dim issueId = GetSelectedRecordIdForCustomAction()
            If Not issueId.HasValue Then
                MessageBox.Show(Me, "Select an issue first.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            If MessageBox.Show(Me, "Delete the selected Help Desk issue?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                HelpDeskDataAccess.DeleteIssue(issueId.Value, _selectedRegistrationId, CurrentUserId())
                RefreshGridForCustomAction()
            End If
            Return True
        End Function

        Private Function OpenSelectedIssue(readOnlyMode As Boolean) As Boolean
            Dim issueId = GetSelectedRecordIdForCustomAction()
            If Not issueId.HasValue Then
                MessageBox.Show(Me, "Select an issue first.", "Read", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            Using page As New FW_HD_Issues_U(issueId.Value, _selectedRegistrationId)
                page.ShowDialog(Me)
            End Using
            Return True
        End Function

        Protected Overrides Function HandleCustomCreateAction() As Boolean
            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then Return True

            Using page As New FW_HD_Issues_U(0, registrationId)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then RefreshGridForCustomAction()
            End Using
            Return True
        End Function

        Private Sub SupportPage_Load(sender As Object, e As EventArgs)
            LoadRegistrations()
        End Sub

        Private Sub SupportPage_Resize(sender As Object, e As EventArgs)
            ' Left of the Help Desk button, which owns the top right corner on every page.
            registrationComboBox.Top = 14
            registrationComboBox.Left = Math.Max(20, ClientSize.Width - 20 - registrationComboBox.Width - HelpDeskLauncher.ReservedWidth)
            registrationLabel.Top = registrationComboBox.Top + 4
            registrationLabel.Left = registrationComboBox.Left - registrationLabel.PreferredWidth - 8
        End Sub

        Private Sub LoadRegistrations()
            Try
                _loadingRegistrations = True
                RegistrationComboHelper.Populate(registrationComboBox, _selectedRegistrationId, True)
                RegistrationComboHelper.UpdateLabelForSelection(registrationLabel, registrationComboBox)
                If ResolveSelectedRegistrationId() > 0 Then
                    RefreshGridForCustomAction()
                    FitSupportGridColumns()
                End If
            Catch ex As Exception
                MessageBox.Show(Me, "Error loading registrations: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                _loadingRegistrations = False
            End Try
        End Sub

        Private Sub RegistrationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If _loadingRegistrations Then Return
            _selectedRegistrationId = ResolveSelectedRegistrationId()
            RegistrationComboHelper.UpdateLabelForSelection(registrationLabel, registrationComboBox)
            If _selectedRegistrationId > 0 Then
                RefreshGridForCustomAction()
                FitSupportGridColumns()
            End If
        End Sub

        Private Sub FitSupportGridColumns()
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

        Private Function ResolveSelectedRegistrationId() As Integer
            Dim id As Integer
            If RegistrationComboHelper.TryGetSelectedId(registrationComboBox, id) Then
                _selectedRegistrationId = id
                Return id
            End If
            Return 0
        End Function

        Private Function CurrentUserId() As Integer
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then Return SessionState.Current.Value.UserID
            Return 0
        End Function

        Private Shared Function BuildCurrentUserFromSession() As UserContext
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                Dim session = SessionState.Current.Value
                Return New UserContext With {.UserId = session.UserID, .FirstName = session.FirstName, .LastName = session.LastName}
            End If
            Return New UserContext()
        End Function
    End Class
End Namespace
