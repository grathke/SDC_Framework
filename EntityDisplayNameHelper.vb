Option Strict On
Option Explicit On

Imports System

Namespace HelloWorld
    Public Module EntityDisplayNameHelper
        Public Function ResolveEntityAlias(registrationId As Integer,
                                           tableName As String,
                                           Optional fallbackAlias As String = "Entity") As String
            Dim aliasName = RemoveFrameworkPrefix(If(fallbackAlias, "Entity").Trim())
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return aliasName
            End If

            Dim session = SessionState.Current
            If Not session.HasValue OrElse session.Value.RoleID <= 0 Then
                Return aliasName
            End If

            Dim resolvedAlias = DataAccess.GetRoleDetailOverrideCaption(session.Value.RoleID, registrationId, tableName.Trim())
            If Not String.IsNullOrWhiteSpace(resolvedAlias) Then
                aliasName = RemoveFrameworkPrefix(resolvedAlias.Trim())
            End If

            Return aliasName
        End Function

        ''' <summary>
        ''' Entity aliases are table-derived, so the FW_ prefix is stripped.
        ''' Formatting itself is owned by DisplayNameFormatter.
        ''' </summary>
        Private Function RemoveFrameworkPrefix(value As String) As String
            Return DisplayNameFormatter.ToDisplayName(value, stripFrameworkPrefix:=True)
        End Function

        Public Function BuildEntityListingTitle(registrationId As Integer,
                                                tableName As String,
                                                Optional fallbackAlias As String = "Entity") As String
            Return ResolveEntityAlias(registrationId, tableName, fallbackAlias) & " Listing"
        End Function

        Public Function BuildEntityMaintenanceTitle(registrationId As Integer,
                                                   tableName As String,
                                                   editMode As EntityEditMode,
                                                   Optional fallbackAlias As String = "Entity") As String
            Dim aliasName = ResolveEntityAlias(registrationId, tableName, fallbackAlias)

            Select Case editMode
                Case EntityEditMode.CreateMode
                    Return aliasName & " Maintenance - Create"
                Case EntityEditMode.ReadMode
                    Return aliasName & " Maintenance - Read"
                Case EntityEditMode.UpdateMode
                    Return aliasName & " Maintenance - Update"
                Case EntityEditMode.DeleteMode
                    Return aliasName & " Maintenance - Delete"
                Case Else
                    Return aliasName & " Maintenance"
            End Select
        End Function
    End Module
End Namespace
