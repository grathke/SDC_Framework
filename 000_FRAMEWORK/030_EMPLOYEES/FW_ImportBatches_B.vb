Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Past Imports: every employee import that committed, one row per batch, and the way to take
    ''' one back.
    '''
    ''' The four commands keep the registration's own captions (Glenn, 2026-09-24: "the buttons can
    ''' get their captions from the registration table too"), and each does what its word says of
    ''' a batch:
    '''
    '''   Create   a new import - opens Import Employees.
    '''   Read     the people in the batch, as a page; double-click does the same.
    '''   Update   the batch's note. The name is how it was found at the time and stays.
    '''   Delete   Undo Import - a physical delete of everybody in it, behind two confirmations:
    '''            the first names what goes, the second asks for UNDO to be typed. See
    '''            ImportBatchDataAccess.Undo for exactly what is removed and what is kept.
    '''
    ''' **Gated by role, not by permission rows**, exactly as the import is: an Application Admin
    ''' or a Company Admin, checked when the page opens and again at every write. Role-based CRUD is
    ''' therefore off - the permission rows for FW_ImportBatches would be a second answer to a
    ''' question already settled, and they start blank for everybody.
    '''
    ''' **Scope:** one registration at a time - the one in the registration combo, which an
    ''' Application Admin sees and chooses, or the session's when the combo is hidden, as it is for a
    ''' Company Admin (Glenn, 2026-09-24). The list, People, Edit Note and Undo Import all follow
    ''' it, and the data layer refuses an import from any other registration. The page SQL's
    ''' @RegistrationID is answered by the framework, as on any browse page.
    ''' </summary>
    Public Class FW_ImportBatches_B
        Inherits FW_Base_B

        Private Const Title As String = "Past Imports"

        ''' <summary>Kept here as well: Base_B holds its own privately, and New Import passes both on.</summary>
        Private ReadOnly pageUser As UserContext
        Private ReadOnly pageProfile As AccessProfile

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, "FW_ImportBatches")
            pageUser = user
            pageProfile = profile
            AddHandler Shown, AddressOf FW_ImportBatches_B_Shown
        End Sub

        ''' <summary>Refused as the import is refused: a dashboard tile is not a boundary.</summary>
        Private Sub FW_ImportBatches_B_Shown(sender As Object, e As EventArgs)
            If DataAccess.CanImportEmployees() Then Return

            MessageBox.Show(Me, "Only an Application Admin or a Company Admin may see past imports.",
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Close()
        End Sub

        Protected Overrides Function UseRoleBasedCrudAccess() As Boolean
            Return False
        End Function

        ''' <summary>A batch is undone, never soft-deleted, so there is no deleted view to offer.</summary>
        Protected Overrides Function CurrentTableSupportsDeletedView() As Boolean
            Return False
        End Function

        ''' <summary>
        ''' The combo by role, not by a permission row: an Application Admin chooses, and a Company
        ''' Admin is held to the session's registration - the rule the import itself follows.
        ''' </summary>
        Protected Overrides Function MayChooseRegistration() As Boolean
            Return DataAccess.MayImportIntoAnyRegistration()
        End Function

        ''' <summary>The registration the page is working in: the combo's, or the session's when it is hidden.</summary>
        Private Function PageRegistrationId() As Integer
            Dim registrationId = 0
            Return If(TryGetActiveRegistrationId(registrationId), registrationId, 0)
        End Function

        ''' <summary>No maintenance page: every command here is the page's own.</summary>
        Protected Overrides Function CreateMaintenancePage(recordId As Integer) As FW_Base_U
            Return Nothing
        End Function

        ''' <summary>Double-clicking a batch shows who was in it - the Read command, not a second path.</summary>
        Protected Overrides Function DoubleClickCommandButton() As Button
            Return readButton
        End Function

        Private Function SelectedBatchId() As Integer
            Dim selected = GetSelectedRecordIdForCustomAction()
            If selected.HasValue AndAlso selected.Value > 0 Then Return selected.Value

            MessageBox.Show(Me, "Select an import first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return 0
        End Function

        ''' <summary>A new import, then the list again with it at the top.</summary>
        Protected Overrides Function HandleCustomCreateAction() As Boolean
            Using page As New FW_EmployeeImport(pageUser, pageProfile)
                page.ShowDialog(Me)
            End Using
            RefreshGridForCustomAction()
            Return True
        End Function

        ''' <summary>The batch and its people, as a page that can be opened in the browser to print.</summary>
        Protected Overrides Function HandleCustomReadAction() As Boolean
            Dim batchId = SelectedBatchId()
            If batchId <= 0 Then Return True

            Try
                Dim batch = ImportBatchDataAccess.GetBatchWithPeople(batchId, PageRegistrationId(), pageProfile)
                Dim html = EmployeeImportReport.BatchPeopleHtml(batch.Batch, batch.People)
                Using viewer As New ImportReportViewer("Import #" & batchId.ToString(CultureInfo.InvariantCulture), html,
                                                       "import-" & batchId.ToString(CultureInfo.InvariantCulture) & "-people.htm",
                                                       "Open the import's people")
                    viewer.ShowDialog(Me)
                End Using
            Catch ex As Exception
                Telemetry.Error(ex, "FW_ImportBatches_B.HandleCustomReadAction")
                MessageBox.Show(Me, "The import could not be read: " & ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
            Return True
        End Function

        ''' <summary>
        ''' The note, and only the note. Saved against the row version it was read with, so a note
        ''' changed by somebody else meanwhile is reported rather than overwritten.
        ''' </summary>
        Protected Overrides Function HandleCustomUpdateAction(recordId As Integer) As Boolean
            If recordId <= 0 Then Return True
            If Not SwitchedUserGuard.AllowWrite(Me, "EDIT AN IMPORT'S NOTE") Then Return True

            Try
                Dim current = ImportBatchDataAccess.GetNote(recordId, PageRegistrationId(), pageProfile)
                Dim note = ImportBatchNoteDialog.Ask(Me, current.BatchName, current.Note)
                If note Is Nothing Then Return True

                If Not ImportBatchDataAccess.UpdateNote(recordId, PageRegistrationId(), note, current.RowVersion, pageProfile, SessionState.ActingUserID) Then
                    MessageBox.Show(Me, "Somebody changed this import while the note was open, so nothing was saved. " &
                                    "Open it again to see their change.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
                RefreshGridForCustomAction(recordId)
            Catch ex As Exception
                Telemetry.Error(ex, "FW_ImportBatches_B.HandleCustomUpdateAction")
                MessageBox.Show(Me, "The note could not be saved: " & ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
            Return True
        End Function

        ''' <summary>
        ''' Undo Import. Counted first, then asked twice - the second time by typing UNDO, because
        ''' this is the one command in the framework that physically deletes people and cannot be
        ''' taken back (Glenn, 2026-09-24: "a double dog dare").
        ''' </summary>
        Protected Overrides Function HandleCustomDeleteAction() As Boolean
            Dim batchId = SelectedBatchId()
            If batchId <= 0 Then Return True
            If Not SwitchedUserGuard.AllowWrite(Me, "UNDO AN IMPORT") Then Return True

            Try
                Dim impact = ImportBatchDataAccess.GetUndoImpact(batchId, PageRegistrationId(), pageProfile, SessionState.ActingUserID)
                Dim name = "Import #" & batchId.ToString(CultureInfo.InvariantCulture) & " - " & impact.BatchName

                If impact.UndoneOn.HasValue Then
                    MessageBox.Show(Me, name & " was already undone.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return True
                End If
                If impact.IncludesCurrentUser Then
                    MessageBox.Show(Me, name & " includes the person signed in, so it cannot be undone from this session. " &
                                    "Sign in as somebody else to undo it.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return True
                End If

                If Not ImportBatchUndoDialog.Confirm(Me, name, impact) Then Return True

                Cursor = Cursors.WaitCursor
                Dim removed = ImportBatchDataAccess.Undo(batchId, PageRegistrationId(), pageProfile, SessionState.ActingUserID)
                Cursor = Cursors.Default

                MessageBox.Show(Me, name & " was undone. " & removed.ToString("N0", CultureInfo.CurrentCulture) &
                                If(removed = 1, " person was", " people were") & " removed.",
                                Title, MessageBoxButtons.OK, MessageBoxIcon.Information)
                RefreshGridForCustomAction(batchId)
            Catch ex As Exception
                Cursor = Cursors.Default
                Telemetry.Error(ex, "FW_ImportBatches_B.HandleCustomDeleteAction")
                MessageBox.Show(Me, "The import was not undone - nothing was removed." & Environment.NewLine & Environment.NewLine &
                                ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
            Return True
        End Function
    End Class
End Namespace
