Option Strict On
Option Explicit On

Imports System.Windows.Forms

Namespace HelloWorld
    Public Class Entity_B
        Inherits FW_Base_B

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing, Optional tableName As String = "ENTITY")
            MyBase.New(user, profile, tableName)
        End Sub

        Protected Overrides Function BuildBrowseListingTitle(registrationId As Integer, tableName As String) As String
            Return EntityDisplayNameHelper.BuildEntityListingTitle(registrationId, tableName)
        End Function

        Protected Overrides Function HandleDefaultCreateAction() As Boolean
            Dim registrationId = GetSessionRegistrationId()
            If registrationId <= 0 Then Return False
            FW_EntityCrudAdapter.HandleCreate(Me, registrationId, CurrentUserId(), ResolveCurrentRoleFieldTableName(), Sub() RefreshGridForCustomAction())
            Return True
        End Function

        Protected Overrides Function HandleDefaultReadAction() As Boolean
            Dim recordId = GetSelectedRecordIdForCustomAction()
            If Not recordId.HasValue Then Return False
            FW_EntityCrudAdapter.HandleRead(Me, recordId.Value, ResolveCurrentRoleFieldTableName(), Sub() RefreshGridForCustomAction())
            Return True
        End Function

        Protected Overrides Function HandleDefaultUpdateAction(recordId As Integer) As Boolean
            FW_EntityCrudAdapter.HandleUpdate(Me, recordId, CurrentUserId(), ResolveCurrentRoleFieldTableName(), Sub(selectedId As Integer) RefreshGridForCustomAction(selectedId))
            Return True
        End Function

        Protected Overrides Function HandleDefaultDeleteAction() As Boolean
            Dim recordId = GetSelectedRecordIdForCustomAction()
            If Not recordId.HasValue Then Return False
            FW_EntityCrudAdapter.HandleDelete(Me, recordId.Value, ResolveCurrentRoleFieldTableName(), Sub() RefreshGridForCustomAction())
            Return True
        End Function

        Protected Overrides Function HandleDefaultRestoreAction() As Boolean
            Dim recordId = GetSelectedRecordIdForCustomAction()
            If Not recordId.HasValue Then Return False
            DataAccess.RestoreEntity(recordId.Value, CurrentUserId(), Me.GetType().Name)
            RefreshGridForCustomAction()
            FW_EntityCrudAdapter.ShowAutoClosingMessage(Me, "Record restored.", "Restore", MessageBoxIcon.Information, 1000)
            Return True
        End Function

        Private Function CurrentUserId() As Integer
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then Return SessionState.Current.Value.UserID
            Return 0
        End Function
    End Class
End Namespace
