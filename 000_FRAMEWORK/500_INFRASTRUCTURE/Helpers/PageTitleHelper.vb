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
        ''' The role's caption for a table, or an empty string when there is none.
        '''
        ''' Separate from ResolveTableAlias because some callers have to tell "no override" from "an
        ''' override that happens to match the fallback". A ribbon tile is the case: its caption is
        ''' deliberately two lines to fit a 96px button, and passing that as a fallback would put it
        ''' through the formatter and rewrite text nobody asked to change. Such a caller keeps its
        ''' own wording unless this returns something.
        ''' </summary>
        Public Function ResolveTableAliasOverride(registrationId As Integer, tableName As String) As String
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return String.Empty
            End If

            Dim session = SessionState.Current
            If Not session.HasValue OrElse session.Value.RoleID <= 0 Then
                Return String.Empty
            End If

            Dim resolvedAlias = DataAccess.GetRoleDetailOverrideCaption(session.Value.RoleID, registrationId, tableName.Trim())
            If String.IsNullOrWhiteSpace(resolvedAlias) Then
                Return String.Empty
            End If

            Return RemoveFrameworkPrefix(resolvedAlias.Trim())
        End Function

        ''' <summary>
        ''' The role's caption for a table, but only when it is genuinely a different name.
        '''
        ''' An override that matches what the formatter derives from the table name renames nothing -
        ''' it is the fallback written down twice. FW_Users aliased "Users" is the case: identical to
        ''' what ToDisplayName gives, and recorded against four roles.
        '''
        ''' Titles cannot tell the difference and do not need to; the text is the same either way. A
        ''' caller that keeps its own wording unless overridden does need to. Applied literally, an
        ''' alias of "Users" would replace a deliberate two-line "User Admin" with a word that was
        ''' never a rename, and two buttons opening different pages on one table would end up
        ''' captioned identically.
        '''
        ''' Empty means "nothing to apply here", not "no override recorded". The caller keeps what it
        ''' has, which is the same thing either answer produces.
        ''' </summary>
        Public Function ResolveTableRename(registrationId As Integer, tableName As String) As String
            Dim overrideCaption = ResolveTableAliasOverride(registrationId, tableName)
            If String.IsNullOrWhiteSpace(overrideCaption) Then
                Return String.Empty
            End If

            If String.Equals(overrideCaption, RemoveFrameworkPrefix(tableName), StringComparison.OrdinalIgnoreCase) Then
                Return String.Empty
            End If

            Return overrideCaption
        End Function

        ''' <summary>
        ''' What the role calls the table behind a page, or an empty string when there is no rename
        ''' to apply.
        '''
        ''' The page-to-table step lives here rather than at each caller, so a button only has to
        ''' know which page it opens. The ribbon and the dashboards ask exactly this question, and
        ''' two answers to it is how a menu and a dashboard end up disagreeing about what something
        ''' is called.
        ''' </summary>
        Public Function ResolvePageRename(registrationId As Integer, pageName As String) As String
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(pageName) Then
                Return String.Empty
            End If

            Dim tableName = DataAccess.GetPageDbTableByWindowOrPage(registrationId, pageName.Trim())
            If String.IsNullOrWhiteSpace(tableName) Then
                Return String.Empty
            End If

            Return ResolveTableRename(registrationId, tableName)
        End Function

        ''' <summary>
        ''' The role's caption for a table, falling back to the name supplied when the session has
        ''' no role, no registration, or no override recorded.
        ''' </summary>
        Public Function ResolveTableAlias(registrationId As Integer,
                                          tableName As String,
                                          fallbackAlias As String) As String
            Dim overrideCaption = ResolveTableAliasOverride(registrationId, tableName)
            If Not String.IsNullOrWhiteSpace(overrideCaption) Then
                Return overrideCaption
            End If

            Return RemoveFrameworkPrefix(If(fallbackAlias, String.Empty).Trim())
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
