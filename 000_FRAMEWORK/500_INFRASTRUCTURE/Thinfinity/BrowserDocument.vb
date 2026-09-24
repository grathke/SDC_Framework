Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Shows a document - an HTML page, a CSV - in the user's own browser, in a tab of its own,
    ''' and owns every file staged on the server for a browser to fetch.
    '''
    ''' **In a browser session** the file is written on the server, Thinfinity is asked for a
    ''' time-limited address for it (HTMLDoc.GetSafeUrl), and the user is offered that address as
    ''' a link (OpenLinkDlg). A link rather than a tab opened for them, because browsers block a
    ''' tab nobody clicked for; the click is what lets it through.
    '''
    ''' Not DownloadFile for this. That hands the browser a file to fetch later - it returns before
    ''' the fetch, which is what OnDownloadEnd exists to report - and the delivery that deleted its
    ''' copy straight after calling it produced a "resource ... might have been removed" page on
    ''' 2026-09-24. The attachment delivery still uses DownloadFile, and stages through here too.
    '''
    ''' **On the desktop** the file is written under the user's temp folder and opened with
    ''' whatever opens that kind of file, which for a page is the default browser.
    '''
    ''' **Where.** SDC_Framework\&lt;area&gt;\&lt;session&gt;\&lt;one folder per file&gt;\ - under
    ''' ProgramData in a session, where the Thinfinity server can read it whatever account it runs
    ''' under, and under temp on the desktop. The area says whose files they are (the import's, the
    ''' Help Desk's); the session folder is this process's alone, so everything in it can be
    ''' deleted at once without touching anybody else signed in to the same server.
    '''
    ''' **Nothing staged may stay.** The import's results hold every new user's PIN, and Glenn's
    ''' rule (2026-09-24) is that these files disappear with nobody doing anything:
    '''
    ''' 1. The address lasts LifetimeMinutes, so a link copied out of the session dies on its own.
    ''' 2. While the process runs, a timer deletes its own files older than LifetimeMinutes.
    ''' 3. When the session ends - Exit, or the application's browser tab closed - its session
    '''    folders are deleted whole, from the same three places that record the session's end.
    '''    Proved 2026-09-24: the tab closed, and the folder was empty.
    ''' 4. When any session starts, every session folder whose process is gone is deleted whole.
    '''    Where that cannot be told - another account's process - the age rule applies instead.
    '''
    ''' A fifth, a hidden PowerShell per folder that slept and then deleted it, was removed:
    ''' Thinfinity will not let a session start another program ("Access is denied"). What it
    ''' covered - a killed process on a server nobody signs in to again - needs a scheduled task.
    ''' </summary>
    Public Module BrowserDocument

        ''' <summary>The employee import's files: the problems page and the results with their PINs.</summary>
        Public Const ImportsArea As String = "imports"

        ''' <summary>Help Desk attachments downloaded from an issue.</summary>
        Public Const AttachmentsArea As String = "attachments"

        Private ReadOnly Areas As String() = {ImportsArea, AttachmentsArea}

        ''' <summary>How long an address works, and how old a staged file may get before the timer takes it.</summary>
        Private Const LifetimeMinutes As Integer = 15

        Private ReadOnly gate As New Object()
        Private ReadOnly sessionFoldersUsed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private sweepTimer As System.Threading.Timer
        Private cachedSessionKey As String

        Public Sub Show(owner As IWin32Window, area As String, fileName As String, data As Byte(), caption As String)
            If data Is Nothing Then Return

            If InBrowser() Then
                Dim staged = StageForSession(area, fileName, data)
                Dim url = Program.VirtualUISession.HTMLDoc.GetSafeUrl(staged, LifetimeMinutes)
                Program.VirtualUISession.OpenLinkDlg(url, caption)
                Return
            End If

            Dim local = StageForSession(area, fileName, data)
            Process.Start(New ProcessStartInfo(local) With {.UseShellExecute = True})
        End Sub

        ''' <summary>
        ''' Writes a file into this session's folder for the area, where a browser can be given it.
        ''' The one staging place for anything handed to a browser, so every such file is under the
        ''' same rules.
        ''' </summary>
        Public Function StageForSession(area As String, fileName As String, data As Byte()) As String
            Dim sessionFolder = Path.Combine(BaseFolder(InBrowser()), SafeFolderName(area), SessionKey())
            Directory.CreateDirectory(sessionFolder)

            Dim folder = Path.Combine(sessionFolder, Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(folder)
            Dim target = Path.Combine(folder, SafeFileName(fileName))
            File.WriteAllBytes(target, data)

            SyncLock gate
                sessionFoldersUsed.Add(sessionFolder)
                If sweepTimer Is Nothing Then
                    ' Every five minutes for as long as the process lives. Started by the first
                    ' file staged, so a session that never shows a document never runs it.
                    sweepTimer = New System.Threading.Timer(Sub(state) ClearOwnOld(), Nothing,
                                                            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5))
                End If
            End SyncLock

            Return target
        End Function

        ''' <summary>
        ''' Deletes this process's session folders whole. Called as the session ends; safe to call
        ''' more than once, and never throws - it runs while the process is already leaving.
        ''' </summary>
        Public Sub ClearThisSession()
            Try
                Dim folders As List(Of String)
                SyncLock gate
                    folders = New List(Of String)(sessionFoldersUsed)
                    sessionFoldersUsed.Clear()
                End SyncLock

                For Each sessionFolder In folders
                    TryDelete(sessionFolder)
                Next
            Catch
                ' Nothing useful can be done here; the startup sweep takes whatever this missed.
            End Try
        End Sub

        ''' <summary>
        ''' Deletes what earlier processes left behind: every session folder, in every area, whose
        ''' process is no longer running. On the desktop and on the server alike, and the folders
        ''' used before the areas existed (Documents), which go by age.
        ''' </summary>
        Public Sub SweepAtStartup()
            Try
                For Each inBrowserBase In {True, False}
                    Dim root = BaseFolder(inBrowserBase)
                    For Each area In Areas
                        Dim areaFolder = Path.Combine(root, area)
                        If Not Directory.Exists(areaFolder) Then Continue For

                        For Each sessionFolder In Directory.GetDirectories(areaFolder)
                            Dim alive = SessionIsAlive(Path.GetFileName(sessionFolder))
                            If alive.HasValue AndAlso Not alive.Value Then
                                TryDelete(sessionFolder)
                            ElseIf Not alive.HasValue Then
                                ClearOld(sessionFolder)
                            End If
                        Next
                    Next
                Next

                ClearOld(Path.Combine(BaseFolder(True), "Documents"))
                ClearOld(Path.Combine(Path.GetTempPath(), "SDC_Documents"))
            Catch ex As Exception
                Telemetry.Error(ex, "BrowserDocument.SweepAtStartup")
            End Try
        End Sub

        Private Function InBrowser() As Boolean
            Return Program.InBrowserSession AndAlso Program.VirtualUISession IsNot Nothing
        End Function

        Private Function BaseFolder(forBrowser As Boolean) As String
            If forBrowser Then
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SDC_Framework")
            End If
            Return Path.Combine(Path.GetTempPath(), "SDC_Framework")
        End Function

        ''' <summary>
        ''' This process, as a folder name: its id and the moment it started. The start time is
        ''' there because Windows reuses process ids - an id alone would let a new process be
        ''' mistaken for a dead one's owner and keep its files for ever.
        ''' </summary>
        Private Function SessionKey() As String
            If cachedSessionKey Is Nothing Then
                Using current = Process.GetCurrentProcess()
                    cachedSessionKey = current.Id.ToString(CultureInfo.InvariantCulture) & "-" &
                                       current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)
                End Using
            End If
            Return cachedSessionKey
        End Function

        ''' <summary>
        ''' True when the process that owns a session folder is still running, False when it is
        ''' gone, Nothing when that cannot be told - a name that is not a session key, or another
        ''' account's process whose start time Windows will not give.
        ''' </summary>
        Private Function SessionIsAlive(folderName As String) As Boolean?
            Dim parts = folderName.Split("-"c)
            Dim processId As Integer
            Dim startTicks As Long
            If parts.Length <> 2 OrElse
               Not Integer.TryParse(parts(0), NumberStyles.Integer, CultureInfo.InvariantCulture, processId) OrElse
               Not Long.TryParse(parts(1), NumberStyles.Integer, CultureInfo.InvariantCulture, startTicks) Then
                Return Nothing
            End If

            Try
                Using owner = Process.GetProcessById(processId)
                    Return owner.StartTime.ToUniversalTime().Ticks = startTicks
                End Using
            Catch ex As ArgumentException
                Return False            ' no process with that id at all
            Catch
                Return Nothing          ' running, but not ours to inspect
            End Try
        End Function

        ''' <summary>The timer's job: this process's own files, past the address's life.</summary>
        Private Sub ClearOwnOld()
            Dim folders As List(Of String)
            SyncLock gate
                folders = New List(Of String)(sessionFoldersUsed)
            End SyncLock

            For Each sessionFolder In folders
                ClearOld(sessionFolder)
            Next
        End Sub

        ''' <summary>
        ''' Deletes the file folders under root older than the address's life. Swallowed per
        ''' folder: one still held open by a browser fetch is simply tried again on the next sweep.
        ''' </summary>
        Private Sub ClearOld(root As String)
            If Not Directory.Exists(root) Then Return

            Dim cutoff = DateTime.Now.AddMinutes(-LifetimeMinutes)
            For Each oldFolder In Directory.GetDirectories(root)
                Try
                    If Directory.GetCreationTime(oldFolder) < cutoff Then TryDelete(oldFolder)
                Catch ex As Exception
                    Telemetry.Error(ex, "BrowserDocument.ClearOld")
                End Try
            Next
        End Sub

        Private Sub TryDelete(folder As String)
            Try
                If Directory.Exists(folder) Then Directory.Delete(folder, True)
            Catch ex As Exception
                Telemetry.Error(ex, "BrowserDocument.TryDelete")
            End Try
        End Sub

        Private Function SafeFolderName(area As String) As String
            Dim name = If(String.IsNullOrWhiteSpace(area), "documents", area.Trim())
            For Each invalid In Path.GetInvalidFileNameChars()
                name = name.Replace(invalid, "_"c)
            Next
            Return name
        End Function

        Private Function SafeFileName(name As String) As String
            Dim candidate = If(String.IsNullOrWhiteSpace(name), "document.htm", name)
            For Each invalid In Path.GetInvalidFileNameChars()
                candidate = candidate.Replace(invalid, "_"c)
            Next
            Return candidate
        End Function
    End Module
End Namespace
