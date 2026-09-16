Option Strict On
Option Explicit On

Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Keeps field permissions in step with the database on a development run, without anybody
    ''' having to remember to press Update Schema.
    '''
    ''' Only on --no-tf. A browser session is somebody using the application, and a sweep that
    ''' physically deletes permission rows is not something to start behind their back. A
    ''' development run is the machine where columns are being added and dropped in the first
    ''' place, which is the only place the drift appears.
    '''
    ''' Started before the login screen and never waited on, so the work happens while the user is
    ''' typing their password. DataAccess.HasSchemaDrifted decides in one round trip whether there
    ''' is anything to do at all - the sweep costs a few hundred, and must not run on every startup
    ''' only to discover that nothing changed.
    ''' </summary>
    Public NotInheritable Class SchemaDriftWatch

        Private Sub New()
        End Sub

        ''' <summary>The user id recorded on rows this writes. Nobody has logged in yet.</summary>
        Private Const SystemUserId As Integer = 0

        Private Shared ReadOnly gate As New Object()
        Private Shared notifyForm As Form
        Private Shared pendingMessage As String

        ''' <summary>
        ''' The window a finished sweep reports through.
        '''
        ''' The sweep runs on a background thread and usually finishes before anything is on
        ''' screen, so the message cannot simply be shown where it is produced - a MessageBox raised
        ''' off the UI thread opens behind the login window and cannot be dismissed with it. The
        ''' result is held instead, and shown by whichever happens second: the form appearing, or
        ''' the sweep finishing.
        ''' </summary>
        Public Shared Sub ReportThrough(form As Form)
            If form Is Nothing Then Return

            SyncLock gate
                notifyForm = form
            End SyncLock

            AddHandler form.Shown, Sub(sender, e) ShowPending(form)
        End Sub

        Public Shared Sub StartInBackground(args As String())
            If args Is Nothing Then Return
            If Not args.Any(Function(arg) String.Equals(arg, "--no-tf", StringComparison.OrdinalIgnoreCase)) Then Return

            ' Fire and forget on purpose. Nothing on screen waits for this, and a failure here must
            ' never stop the application starting - the worst case is that permissions stay as they
            ' were and the Update Schema tile still does the job on request.
            Task.Run(Sub() RunQuietly())
        End Sub

        Private Shared Sub RunQuietly()
            Try
                If Not DataAccess.HasSchemaDrifted() Then Return

                Dim result = DataAccess.SyncAllRoleFieldsWithSchema(SystemUserId)
                Program.Log($"Schema drift corrected: {result.TablesAdded} tables added, {result.TablesRemoved} tables removed, " &
                            $"{result.Inserted} fields added, {result.Deleted} fields deleted, " &
                            $"{result.Repaired} repaired, across {result.RolesVisited} role and table pairs")

                Dim message = "The database schema had changed, and role permissions have been brought back in line." &
                              Environment.NewLine & Environment.NewLine &
                              $"Tables added:    {result.TablesAdded}" & Environment.NewLine &
                              $"Tables removed:  {result.TablesRemoved}" & Environment.NewLine &
                              $"Fields added:    {result.Inserted}" & Environment.NewLine &
                              $"Fields deleted:  {result.Deleted}" & Environment.NewLine &
                              $"Links repaired:  {result.Repaired}" & Environment.NewLine &
                              $"Roles visited:   {result.RolesVisited}"

                If result.Failures.Count > 0 Then
                    Program.Log($"Schema drift: {result.Failures.Count} pairs could not be updated - " &
                                String.Join("; ", result.Failures.Take(5)))
                    message &= Environment.NewLine & Environment.NewLine &
                               $"{result.Failures.Count} could not be updated. See startup.log."
                End If

                Announce(message)
            Catch ex As Exception
                Program.Log("Schema drift check failed: " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Holds the message, and shows it if there is already a window to show it on.
        ''' </summary>
        Private Shared Sub Announce(message As String)
            Dim target As Form

            SyncLock gate
                pendingMessage = message
                target = notifyForm
            End SyncLock

            If target Is Nothing OrElse Not target.IsHandleCreated Then Return

            Try
                target.BeginInvoke(Sub() ShowPending(target))
            Catch
                ' The window went away between the check and the call. The message stays pending
                ' and startup.log still has the counts.
            End Try
        End Sub

        Private Shared Sub ShowPending(owner As Form)
            Dim message As String

            SyncLock gate
                message = pendingMessage
                pendingMessage = Nothing
            End SyncLock

            If String.IsNullOrEmpty(message) Then Return

            MessageBox.Show(owner, message, "Schema Updated",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

    End Class

End Namespace
