Option Strict On
Option Explicit On

Imports System.Windows.Forms

Namespace HelloWorld
    Public Class EntityX_B
        Inherits FW_Base_B

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, "FW_ENTITY")
            currentUser = user
            accessProfile = profile
        End Sub


        Protected Overrides Function HandleDefaultCreateAction() As Boolean
            Using page As New EntityX_U(0, currentUser, accessProfile)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then RefreshGridForCustomAction(page.SavedRecordId)
            End Using
            Return True
        End Function

        Protected Overrides Function HandleDefaultUpdateAction(recordId As Integer) As Boolean
            Using page As New EntityX_U(recordId, currentUser, accessProfile)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then RefreshGridForCustomAction(recordId)
            End Using
            Return True
        End Function
        ''' <summary>
        ''' Soft-deletes the selected record. Without this the Delete button falls through to
        ''' the base placeholder and silently does nothing.
        ''' </summary>
        Protected Overrides Function HandleDefaultDeleteAction() As Boolean
            Dim recordId = GetSelectedRecordIdForCustomAction()
            If Not recordId.HasValue Then Return False

            Dim summary = GetSelectedRowSummary()
            Dim prompt = If(String.IsNullOrWhiteSpace(summary), "Delete the selected record?", "Delete " & summary & "?")
            If MessageBox.Show(Me,
                               (prompt & Environment.NewLine & Environment.NewLine &
                                "It will be removed from this list.").ToUpperInvariant(),
                               "CONFIRM DELETE",
                               MessageBoxButtons.YesNo,
                               MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return True
            End If

            Dim failure = DataAccess.SoftDeleteGeneratedPageRecord("FW_ENTITY",
                                                                  "EntityID",
                                                                  recordId.Value,
                                                                  If(SessionState.IsActive, SessionState.Current.Value.UserID, 0),
                                                                  NameOf(EntityX_B))
            If Not String.IsNullOrWhiteSpace(failure) Then
                MessageBox.Show(Me, failure.ToUpperInvariant(), "DELETE FAILED", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return True
            End If

            RefreshGridForCustomAction()
            Return True
        End Function
    End Class
End Namespace
