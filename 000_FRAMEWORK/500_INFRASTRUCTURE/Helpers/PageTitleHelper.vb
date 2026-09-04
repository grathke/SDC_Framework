Option Strict On
Option Explicit On

Imports System

Namespace SDC.Framework

    ''' <summary>
    ''' Builds a page's title from the alias its role gives the table behind it, so a page is
    ''' called what the customer calls it rather than what the schema calls it.
    '''
    ''' Was EntityDisplayNameHelper until 2026-09-03, and the name was misleading: nothing here is
    ''' specific to any one table. It takes whichever table name it is handed, and the only caller
    ''' is Roles_B. It outlived FW_Entity because it never depended on it.
    '''
    ''' Formatting is not owned here. DisplayNameFormatter turns a name into display text, and the
    ''' FW_ prefix is stripped because an alias is table-derived and the prefix is ours, not the
    ''' reader's.
    ''' </summary>
    Public Module PageTitleHelper

        ''' <summary>
        ''' The role's caption for a table, falling back to the name supplied when the session has
        ''' no role, no registration, or no override recorded.
        ''' </summary>
        Public Function ResolveTableAlias(registrationId As Integer,
                                          tableName As String,
                                          fallbackAlias As String) As String
            Dim aliasName = RemoveFrameworkPrefix(If(fallbackAlias, String.Empty).Trim())
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

        Public Function BuildListingTitle(registrationId As Integer,
                                          tableName As String,
                                          fallbackAlias As String) As String
            Return ResolveTableAlias(registrationId, tableName, fallbackAlias) & " Listing"
        End Function

        Private Function RemoveFrameworkPrefix(value As String) As String
            Return DisplayNameFormatter.ToDisplayName(value, stripFrameworkPrefix:=True)
        End Function
    End Module
End Namespace
