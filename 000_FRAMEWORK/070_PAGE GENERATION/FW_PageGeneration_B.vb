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
            Dim browseSplit = BrowseSplitPanel
            If browseSplit Is Nothing Then
                Return
            End If

            Dim qbeButton = QbeToggleButton
            Dim closeButton = CloseCommandButton
            ' The shell's own geometry, not a copy of it. These were 112 and 162 for an App Admin,
            ' which was right while the SQL path and Apply SQL row sat above the actions; with that
            ' row hidden the shell moved to 48, and this page did not - so QBE and Close hung below
            ' Add, Edit and Delete with a band of white between them.
            Dim actionTop = HeaderBandHeight
            Dim splitTop = BrowseGridTop

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

        Protected Overrides Function HandleCustomUpdateAction(recordId As Integer) As Boolean
            ' Refused before the page opens, not warned about once it is. A request whose browse
            ' page holds hand-written code in the file the generator replaces cannot be updated
            ' safely at all, and a dialog inside the page is one click away from doing it anyway.
            Dim refusal = UnsafeToOpenReason(recordId)
            If refusal <> String.Empty Then
                MessageBox.Show(Me, refusal, "This Request Cannot Be Updated",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return True
            End If

            Using page As New PageGeneration_U(recordId, currentUser, accessProfile)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction(recordId)
                End If
            End Using
            Return True
        End Function

        ''' <summary>
        ''' The request's browse page name, asked of the generator for its verdict on it.
        ''' </summary>
        Private Shared Function UnsafeToOpenReason(recordId As Integer) As String
            If recordId <= 0 Then Return String.Empty

            Try
                Dim request = DataAccess.GetPageGenerationById(recordId)
                If request Is Nothing OrElse Not request.Table.Columns.Contains("BrowsePageName") Then Return String.Empty
                If request.IsNull("BrowsePageName") Then Return String.Empty

                Return PageGenerator.UnsafeToOpenReason(Convert.ToString(request("BrowsePageName")))
            Catch
                ' A request that cannot be read is not a request that is unsafe - the page itself
                ' reports the failure when it tries to load the same row.
                Return String.Empty
            End Try
        End Function

        Private Function OpenSelectedRequest() As Boolean
            Dim recordId = GetSelectedRecordIdForCustomAction()
            If Not recordId.HasValue Then
                MessageBox.Show(Me, "Select a request first.", "Page Generation", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            Return HandleCustomUpdateAction(recordId.Value)
        End Function
    End Class
End Namespace