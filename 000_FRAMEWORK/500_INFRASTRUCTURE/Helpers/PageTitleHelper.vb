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
        ''' What to call a page: the role's rename of its table, else the page's own alias, else
        ''' nothing and the caller keeps what it has.
        '''
        ''' **The one place this precedence is decided.** Buttons and titles all ask here, so a menu
        ''' and the page it opens cannot disagree.
        '''
        ''' Role first because a rename is a statement about the thing itself - a company that calls
        ''' Users "Staff" means it everywhere - and a page alias only distinguishes one view of that
        ''' thing from another. The cost is accepted and worth knowing: a genuine rename applies to
        ''' every page on the table, so "Users X" and "Users Y" both become "Staff". Without a rename
        ''' recorded they keep their own names, which is the ordinary case.
        '''
        ''' ResolveTableRename rather than the plain override, so an alias that merely restates what
        ''' the formatter derives does not count. FW_Users is aliased "Users" against four roles;
        ''' treating that as a rename would beat "Users X" with a word that renamed nothing.
        ''' </summary>
        Public Function ResolvePageCaption(registrationId As Integer, tableName As String, pageAlias As String) As String
            Dim renamed = ResolveTableRename(registrationId, tableName)
            If Not String.IsNullOrWhiteSpace(renamed) Then
                Return renamed
            End If

            Return If(pageAlias, String.Empty).Trim()
        End Function

        ''' <summary>
        ''' The same order, decided from values already in hand. **Reads nothing.**
        '''
        ''' The form to prefer. A browse page fetches its table, SQL, alias and role override in one
        ''' query, and a ribbon fetches them for every tile in one more - so by the time a caption is
        ''' wanted the answer is already in memory, and asking the database again would be three
        ''' round trips to rebuild what was just returned.
        '''
        ''' ResolvePageCaption above is the same rule for callers that do not have the values; it
        ''' delegates here so there is still one definition of the order.
        ''' </summary>
        Public Function ResolvePageCaptionFrom(tableName As String,
                                               roleOverrideCaption As String,
                                               pageAlias As String) As String
            Dim overrideCaption = RemoveFrameworkPrefix(If(roleOverrideCaption, String.Empty).Trim())

            ' A rename, not merely an override. FW_Users is aliased "Users" against four roles, which
            ' is exactly what the formatter derives, so counting it would beat a page's own "Users X"
            ' with a word that renamed nothing.
            If overrideCaption <> String.Empty AndAlso
               Not String.Equals(overrideCaption, RemoveFrameworkPrefix(If(tableName, String.Empty).Trim()), StringComparison.OrdinalIgnoreCase) Then
                Return overrideCaption
            End If

            Return If(pageAlias, String.Empty).Trim()
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

        ''' <summary>
        ''' The caption for a page, found from the page's own name.
        '''
        ''' The form for a page that has no table context of its own - a one-off dialog that
        ''' inherits Form rather than FW_Base_B, and so never fetched a table, an alias or an
        ''' override on the way in. It looks up what the other callers already hold, then applies
        ''' the same order through ResolvePageCaptionFrom, so a page cannot end up captioned by a
        ''' different rule than the button that opened it.
        '''
        ''' **Costs no round trip in steady state.** Both lookups are served from caches held for
        ''' the session - every page's alias in one query, the role overrides one per table - so
        ''' this resolves from memory once any page has been opened.
        '''
        ''' Returns the fallback when nothing is recorded, so a page with no FW_Pages row keeps the
        ''' wording written into it rather than losing its title.
        ''' </summary>
        Public Function ResolveCaptionForPage(registrationId As Integer,
                                              pageName As String,
                                              fallbackCaption As String) As String
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(pageName) Then
                Return fallbackCaption
            End If

            Try
                Dim tableName = DataAccess.GetPageDbTableByWindowOrPage(registrationId, pageName)
                Dim pageAlias = DataAccess.GetPageAliasByWindowOrPage(registrationId, pageName)
                Dim overrideCaption = ResolveTableAliasOverride(registrationId, tableName)

                Dim resolved = ResolvePageCaptionFrom(tableName, overrideCaption, pageAlias)
                If Not String.IsNullOrWhiteSpace(resolved) Then
                    Return resolved
                End If
            Catch
                ' A caption is not worth a page that will not open. The written wording stands,
                ' which is what every session saw before the chain existed.
            End Try

            Return fallbackCaption
        End Function

        Private Function RemoveFrameworkPrefix(value As String) As String
            Return DisplayNameFormatter.ToDisplayName(value, stripFrameworkPrefix:=True)
        End Function
    End Module
End Namespace
