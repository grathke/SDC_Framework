Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class FW_PageGeneration_B
        Inherits FW_Base_B

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, DataAccess.GeneratedPagesTable)
            currentUser = user
            accessProfile = profile
            Text = "Page Generation"
        End Sub

        ' ResolveBrowsePageName is not overridden. It used to return "PageGeneration_B", the name
        ' this page had before it took the FW_ prefix, so the page looked itself up under two names
        ' at once: its SQL, alias and caption came from a PageGeneration_B row while its background
        ' colour and Hot Fields flag came from the FW_PageGeneration_B row, because those are keyed
        ' on the class name. Two rows in FW_Pages for one page, each half used, and nothing on
        ' screen to say so.
        '
        ' Deleting the leftover row twice did not fix it - sql/063 removed it and the application
        ' recreated it, which sql/069 recorded and asked someone to explain. This override was the
        ' explanation.

        ''' <summary>
        ''' A generation request can be deleted from its own browse page.
        '''
        ''' FW_Base_B defaults this to False so that browse pages which never had a delete cannot
        ''' silently acquire one, and this page overrode create, read and update but not this - so
        ''' Delete reported that it was not wired, which is what it says when nothing has opted in.
        '''
        ''' FW_GeneratedPages carries a DeletedFlag, so the shared soft delete has somewhere to
        ''' write. Soft, not physical: a request holds every field choice a page was generated from,
        ''' and losing that is worse than a row sitting flagged in a table nobody reads directly.
        ''' </summary>
        Protected Overrides Function UsesStandardSoftDelete() As Boolean
            Return True
        End Function

        Protected Overrides Sub ApplyPageSpecificLayout()
            Dim browseSplit = FindBrowseSplitContainer(Me)
            If browseSplit Is Nothing Then
                Return
            End If

            Dim qbeButton = FindButtonStartingWithText(Me, "QBE")
            Dim closeButton = FindButtonByText(Me, "Close")
            Dim actionTop = If(IsAppAdminSession(), 112, 42)
            Dim splitTop = If(IsAppAdminSession(), 162, 92)

            If qbeButton IsNot Nothing Then
                qbeButton.Top = actionTop
            End If
            If closeButton IsNot Nothing Then
                closeButton.Top = actionTop
            End If

            browseSplit.Top = splitTop
            browseSplit.Height = Math.Max(180, ClientSize.Height - browseSplit.Top - 20)
        End Sub

        Protected Overrides Function HandleCustomCreateAction() As Boolean
            Using page As New PageGeneration_U(0, currentUser, accessProfile)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction()
                End If
            End Using
            Return True
        End Function

        Protected Overrides Function HandleCustomReadAction() As Boolean
            Return OpenSelectedRequest()
        End Function

        ''' <summary>
        ''' Modify maintains the page this request produced, or edits the request when it has not
        ''' produced one yet.
        ''' </summary>
        ''' <remarks>
        ''' Routing on whether the page exists rather than offering two buttons. A request that has
        ''' never been generated has no page to maintain, so the request form is the only thing
        ''' Modify could sensibly open; once it has, changing a caption or a field list is the
        ''' ordinary act and describing a different page is the rare one.
        '''
        ''' FW_PageSettings_U is opened with the page name, not the request id, so the same page can
        ''' later be reached from anywhere - including from a page nobody generated.
        '''
        ''' See PAGE_MAINTENANCE_SPEC.md section 6.
        ''' </remarks>
        Protected Overrides Function HandleCustomUpdateAction(recordId As Integer) As Boolean
            Dim browsePageName = ResolveBrowsePageNameForRequest(recordId)

            If Not String.IsNullOrWhiteSpace(browsePageName) AndAlso
               DataAccess.CheckIfPageRecordExists(0, browsePageName) Then

                Using settings As New FW_PageSettings_U(browsePageName, currentUser, accessProfile)
                    settings.ShowDialog(Me)
                End Using
                RefreshGridForCustomAction(recordId)
                Return True
            End If

            Using page As New PageGeneration_U(recordId, currentUser, accessProfile)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction(recordId)
                End If
            End Using
            Return True
        End Function

        ''' <summary>The browse page a request names, or empty when it names none.</summary>
        Private Shared Function ResolveBrowsePageNameForRequest(recordId As Integer) As String
            If recordId <= 0 Then Return String.Empty

            Dim request = DataAccess.GetPageGenerationById(recordId)
            If request Is Nothing OrElse Not request.Table.Columns.Contains("BrowsePageName") OrElse request.IsNull("BrowsePageName") Then
                Return String.Empty
            End If

            Return Convert.ToString(request("BrowsePageName")).Trim()
        End Function

        Private Function OpenSelectedRequest() As Boolean
            Dim recordId = GetSelectedRecordIdForCustomAction()
            If Not recordId.HasValue Then
                MessageBox.Show(Me, "Select a request first.", "Page Generation", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            Return HandleCustomUpdateAction(recordId.Value)
        End Function

        Private Shared Function FindBrowseSplitContainer(parent As Control) As SplitContainer
            For Each child As Control In parent.Controls
                Dim split = TryCast(child, SplitContainer)
                If split IsNot Nothing Then
                    Return split
                End If

                Dim nested = FindBrowseSplitContainer(child)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function FindButtonByText(parent As Control, text As String) As Button
            For Each child As Control In parent.Controls
                Dim button = TryCast(child, Button)
                If button IsNot Nothing AndAlso String.Equals(button.Text, text, StringComparison.OrdinalIgnoreCase) Then
                    Return button
                End If

                Dim nested = FindButtonByText(child, text)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function FindButtonStartingWithText(parent As Control, text As String) As Button
            For Each child As Control In parent.Controls
                Dim button = TryCast(child, Button)
                If button IsNot Nothing AndAlso button.Text.StartsWith(text, StringComparison.OrdinalIgnoreCase) Then
                    Return button
                End If

                Dim nested = FindButtonStartingWithText(child, text)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function
    End Class
End Namespace