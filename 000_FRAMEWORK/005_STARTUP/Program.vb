Option Strict On
Option Explicit On

Imports System
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms

Namespace SDC.Framework
    Friend Module Program
        Private ReadOnly logPath As String = Path.Combine(AppContext.BaseDirectory, "startup.log")

        Friend Sub Log(message As String)
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & message & Environment.NewLine)
        End Sub

        ''' <summary>
        ''' An exception that reached the message loop: logged, and shown.
        '''
        ''' Logging alone made a failure invisible. A generated page whose lookup named a renamed
        ''' column threw here on load, and the window simply never opened - no message, nothing on
        ''' screen to say why, and startup.log the only record that anything had happened at all.
        '''
        ''' The message and the log path, not the stack: the stack is already in the file, and a
        ''' dialog nobody can read is barely better than no dialog. Showing it does not make the
        ''' failure recoverable - whatever was being opened has still failed - it makes it visible.
        ''' </summary>
        Private Sub OnThreadException(sender As Object, e As ThreadExceptionEventArgs)
            Log("ThreadException: " & e.Exception.ToString())
            Telemetry.Error(e.Exception, "Program.OnThreadException", Telemetry.FaultOrigin.ThreadException)

            Try
                Dim detail = If(e.Exception Is Nothing, "Unknown error.", e.Exception.Message)
                MessageBox.Show("Something went wrong and the action could not complete." &
                                Environment.NewLine & Environment.NewLine &
                                detail &
                                Environment.NewLine & Environment.NewLine &
                                "Full details are in:" & Environment.NewLine & logPath,
                                "Unexpected Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error)
            Catch
                ' A handler that throws replaces one failure with another and loses both. The log
                ' line above is already written, so there is nothing left worth risking here.
            End Try
        End Sub

        ''' <summary>
        ''' Anything thrown off the UI thread. Usually terminal, which is why this flushes rather
        ''' than queuing and hoping: the process may not be alive for the next flush.
        ''' </summary>
        Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
            Log("UnhandledException: " & e.ExceptionObject.ToString())

            Dim thrown = TryCast(e.ExceptionObject, Exception)
            If thrown IsNot Nothing Then
                Telemetry.Error(thrown, "Program.OnUnhandledException", Telemetry.FaultOrigin.UnhandledException)
            End If

            Telemetry.Flush()
        End Sub

        ''' <summary>
        ''' How long Start() waits for a browser before giving up and letting the application open
        ''' on the desktop. The SDK's own default is 60000.
        ''' </summary>
        Private Const VirtualUIStartTimeoutMs As Integer = 5000

        ''' Longer, because a dev run has just asked a browser to start and a cold one takes
        ''' several seconds to reach the page. Nothing waits this long unless a browser was asked
        ''' for and never arrived.
        Private Const VirtualUIDevStartTimeoutMs As Integer = 20000

        ''' <summary>
        ''' Held for the life of the process. THINFINITY_NOTES.md section 3: constructing VirtualUI
        ''' also constructs the shared static instance the event handlers attach to, so it is
        ''' constructed once and kept - not left to be collected and disposed mid-session.
        ''' </summary>
        Private virtualUI As Cybele.Thinfinity.VirtualUI

        ''' <summary>
        ''' True when this process is being watched through a browser rather than run on a desktop.
        '''
        ''' Set once, from Start()'s own answer, rather than read from Active on demand: Active was
        ''' measured on 2026-09-11 still reporting True three and a half minutes after the browser
        ''' had gone, so it says "this process belongs to a session" rather than "somebody is
        ''' looking". For deciding what a window should offer, the first question is the right one -
        ''' the answer must not change halfway through a session.
        '''
        ''' Exposed because window furniture is decided by the windows themselves. The main menu
        ''' hides its minimise box on this: minimising inside a browser tab puts the application
        ''' somewhere there is no taskbar to get it back from.
        ''' </summary>
        Friend ReadOnly Property InBrowserSession As Boolean
            Get
                Return virtualUISessionAttached
            End Get
        End Property

        Private virtualUISessionAttached As Boolean

        ''' <summary>
        ''' The SDK instance, for the few places that need more than "am I in a browser".
        '''
        ''' Nothing may assume it: it is Nothing on a desktop run and on a run started with
        ''' --no-tf, so a caller checks InBrowserSession first and treats this as the how rather
        ''' than the whether. Owned here because Start() is called here and the object must outlive
        ''' the call - a second instance would not be attached to the session.
        ''' </summary>
        Friend ReadOnly Property VirtualUISession As Cybele.Thinfinity.VirtualUI
            Get
                Return virtualUI
            End Get
        End Property

        ''' <summary>
        ''' Starts the VirtualUI session, before anything is drawn.
        '''
        ''' Section 3 of the notes is explicit that this comes first - ahead of EnableVisualStyles
        ''' and before any window exists - because the SDK has to be in place before the first
        ''' handle is created.
        '''
        ''' Skipped for --generate-request, which is the page generator's command-line path: it
        ''' draws nothing, exits immediately, and has no browser to wait for. Skipped too for
        ''' --no-tf, which is the ordinary development run: starting VirtualUI costs a few seconds
        ''' and a second server process, and most of the time nobody wants a browser.
        '''
        ''' Nothing here can stop the application starting. Section 2 records that the wrapper
        ''' no-ops when its DLL cannot be loaded - Start() returns False and every later call does
        ''' nothing - so a machine without VirtualUI runs on the desktop exactly as before. The
        ''' Catch is for the case the notes do not cover, and it is deliberately silent beyond the
        ''' log: a delivery mechanism that fails must not take the application down with it.
        ''' </summary>
        Private Sub StartVirtualUI(args As String())
            If args Is Nothing Then Return
            If args.Any(Function(arg) String.Equals(arg, "--generate-request", StringComparison.OrdinalIgnoreCase) OrElse
                                      String.Equals(arg, "--no-tf", StringComparison.OrdinalIgnoreCase)) Then
                Log("VirtualUI skipped by command line")
                Return
            End If

            ' Dev mode only when we launched the process ourselves. It tells the SDK to stand up
            ' its own development server and wait for a browser on 6080 - which is what "run TF"
            ' wants, and exactly wrong when VirtualUI Server launched the application for a
            ' session that is already waiting at 6580. With it forced on, Start() never returned,
            ' no development server appeared, and the browser sat on "Initializing..." while the
            ' process stayed in Main with the login screen never built.
            Dim devMode = args.Any(Function(arg) String.Equals(arg, "--tf-dev", StringComparison.OrdinalIgnoreCase))

            Try
                virtualUI = New Cybele.Thinfinity.VirtualUI()
                virtualUI.DevMode = devMode

                ' The session ending has to end the process. Nobody closes this application from
                ' the desktop any more - OPT_APPINVISIBLE means there is no window to close - so a
                ' browser closed on a session that keeps running leaves an invisible process
                ' holding a database connection and, in dev mode, the development server's port.
                AddHandler virtualUI.OnClose, AddressOf VirtualUISessionClosed

                ' We open the browser ourselves in dev mode, rather than letting the SDK ask.
                '
                ' Its prompt has a "do not show this again" box, and once that is ticked nothing
                ' opens a tab at all: Start() waits out its timeout, returns False, and the
                ' application falls back to the desktop looking as though TF had been skipped.
                ' Opening it here is also simply better - no dialog stands between "run" and the
                ' application.
                '
                ' StartBrowser off first, so a machine where the prompt is still enabled does not
                ' get two tabs.
                Dim timeout = VirtualUIStartTimeoutMs
                If devMode Then
                    timeout = VirtualUIDevStartTimeoutMs
                    OpenDevBrowser()
                End If

                ' Start() blocks until a browser attaches or the timeout expires. Five seconds is
                ' plenty for a tab that is already open and nowhere near enough for a cold browser
                ' start, which is why dev mode waits longer - it knows a browser is on its way.
                Dim started = virtualUI.Start(timeout)

                ' Hide the desktop window, but only once a browser is actually holding the
                ' session. OPT_APPINVISIBLE is what stops the application appearing twice - once
                ' on the desktop and once in the tab - and in a browser it is the only sensible
                ' setting. It is applied after Start() rather than before because Start() returns
                ' False when nothing attached, and the application then falls back to running on
                ' the desktop: invisible there would mean no interface at all.
                virtualUISessionAttached = started

                If started Then
                    ' APPINVISIBLE so the window exists in the tab and not on the desktop.
                    '
                    ' NOHTML_DRAG because VirtualUI otherwise treats a drag in the browser as an
                    ' HTML5 drag - which is how a file gets dragged from the desktop into the
                    ' application, and which swallows the mouse-down, move and up a drag *inside*
                    ' the application needs. Rearranging ribbon tiles did nothing in a browser for
                    ' that reason, and grid column reordering is the same gesture. Nothing here
                    ' accepts a dropped file - the Help Desk uses a file dialog - so the trade is
                    ' one-sided.
                    ' NOHTML_DRAG always: VirtualUI otherwise treats a drag in the browser as an
                    ' HTML5 file drag and swallows the mouse-down, move and up that a drag *inside*
                    ' the application needs. Ribbon tiles would not move without it.
                    virtualUI.Options = virtualUI.Options Or CUInt(Cybele.Thinfinity.Options.OPT_NOHTML_DRAG)

                    ' APPINVISIBLE always, dev run included. Seeing the application twice - once on
                    ' the desktop and once in the tab - is the thing that must not happen, decided
                    ' 2026-09-14 after trying it both ways.
                    '
                    ' The flag hides the application's window and nothing else. A combo dropdown, a
                    ' menu and a tooltip are each their own transient top-level window, so Windows
                    ' still draws those on the desktop: a lookup shows its list in the tab and again
                    ' on screen behind it. That is the known cost, and it is the smaller one - on a
                    ' server nobody watches that desktop, and here a stray list beats a stray copy
                    ' of the whole application.
                    virtualUI.Options = virtualUI.Options Or CUInt(Cybele.Thinfinity.Options.OPT_APPINVISIBLE)
                End If

                Log("VirtualUI DevMode=" & devMode.ToString() &
                    "; Start() returned " & started.ToString() &
                    "; Active=" & virtualUI.Active.ToString() &
                    "; DevServer.Enabled=" & virtualUI.DevServer.Enabled.ToString() &
                    "; DevServer.Port=" & virtualUI.DevServer.Port.ToString())
            Catch ex As Exception
                Log("VirtualUI did not start: " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Ends the process when the browser session ends.
        '''
        ''' OnClose is the only signal that works. Active was polled here as well, on the theory
        ''' that a closed tab might leave the session merely disconnected - but measured on
        ''' 2026-09-11, Active stayed True for the whole three and a half minutes between the
        ''' browser closing and OnClose arriving, so the poll never counted and never would. A
        ''' safety net that cannot fire is worse than none: it reads like cover that is not there.
        '''
        ''' The delay before OnClose is VirtualUI's own disconnect grace, and is a server setting
        ''' rather than anything this can hurry along.
        '''
        ''' Application.Exit first, so forms close through their own Closing handlers and anything
        ''' with cleanup gets to run. The timer behind it is not defensive decoration: Exit will
        ''' not return while a modal dialog is on screen, and in a session whose browser has gone
        ''' there is nobody to dismiss one. Three seconds, then the process goes regardless.
        '''
        ''' Both hard exits flush telemetry first. Environment.Exit does not run Finally blocks, so
        ''' the flush in Main's never happens on either of these paths and everything queued since
        ''' the last thirty-second tick is lost. It is a narrow window and the wrong one to lose:
        ''' these are the paths taken when a session has already gone wrong, or when a modal dialog
        ''' nobody can reach is holding the process open. Flush never throws and never blocks on a
        ''' database that is down, so it is safe on the way out.
        ''' </summary>
        Private Sub VirtualUISessionClosed(sender As Object, e As Cybele.Thinfinity.CloseArgs)
            Log("VirtualUI session closed - exiting")

            Dim killer As New System.Threading.Timer(Sub()
                                                         Log("VirtualUI session closed - forcing exit")
                                                         Telemetry.Flush()
                                                         Environment.Exit(0)
                                                     End Sub, Nothing, 3000, System.Threading.Timeout.Infinite)

            Try
                Dim form = If(Application.OpenForms.Count > 0, Application.OpenForms(0), Nothing)
                If form IsNot Nothing AndAlso form.IsHandleCreated Then
                    form.BeginInvoke(New Action(Sub() Application.Exit()))
                Else
                    Application.Exit()
                End If
            Catch ex As Exception
                Log("VirtualUI session close handler failed: " & ex.Message)
                Telemetry.Error(ex, "Program.VirtualUISessionClosed", Telemetry.FaultOrigin.Swallowed)
                Telemetry.Flush()
                Environment.Exit(0)
            End Try

            GC.KeepAlive(killer)
        End Sub

        ''' <summary>
        ''' Opens the development server's page in the default browser.
        '''
        ''' Called before Start(), which is what the page needs to connect to - the request will
        ''' arrive while Start() is waiting, which is exactly the handshake it is waiting for. The
        ''' port comes from the SDK rather than a constant here, so changing it in the manager
        ''' does not leave this opening the wrong address.
        ''' </summary>
        Private Sub OpenDevBrowser()
            Try
                virtualUI.DevServer.StartBrowser = False

                Dim port = virtualUI.DevServer.Port
                If port <= 0 Then port = 6080

                Dim url = "http://127.0.0.1:" & port.ToString(CultureInfo.InvariantCulture) & "/"
                Log("Opening " & url)

                Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
            Catch ex As Exception
                ' Not fatal. Without a browser Start() times out and the application opens on the
                ' desktop, which is a worse run but still a run.
                Log("Could not open the development browser: " & ex.Message)
            End Try
        End Sub

        <STAThread>
        Public Sub Main()
            Try
                Log("Main start")

                StartVirtualUI(Environment.GetCommandLineArgs())

                Application.SetHighDpiMode(HighDpiMode.SystemAware)
                Application.EnableVisualStyles()
                Application.SetCompatibleTextRenderingDefault(False)

                AddHandler Application.ThreadException, AddressOf OnThreadException
                AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException

                Dim args = Environment.GetCommandLineArgs()
                If args IsNot Nothing Then
                    Dim generateIndex = Array.FindIndex(args, Function(arg) String.Equals(arg, "--generate-request", StringComparison.OrdinalIgnoreCase))
                    If generateIndex >= 0 Then
                        If args.Length <= generateIndex + 1 Then
                            Console.Error.WriteLine("A GeneratedPageID is required after --generate-request.")
                            Environment.ExitCode = 2
                            Return
                        End If

                        Dim requestId As Integer
                        If Not Integer.TryParse(args(generateIndex + 1), requestId) OrElse requestId <= 0 Then
                            Console.Error.WriteLine("GeneratedPageID must be a positive integer.")
                            Environment.ExitCode = 2
                            Return
                        End If

                        Dim workspaceRoot = If(args.Length > generateIndex + 2, args(generateIndex + 2), Environment.CurrentDirectory)
                        Dim result = PageGenerator.Generate(requestId, workspaceRoot)
                        If result.Errors.Count > 0 Then
                            For Each errorMessage In result.Errors
                                Console.Error.WriteLine(errorMessage)
                            Next
                            Environment.ExitCode = 1
                            Return
                        End If

                        Console.WriteLine("PAGE GENERATION COMPLETED SUCCESSFULLY.")
                        For Each createdFile In result.CreatedFiles
                            Console.WriteLine("CREATED: " & createdFile)
                        Next
                        For Each skippedFile In result.SkippedFiles
                            Console.WriteLine("SKIPPED: " & skippedFile)
                        Next
                        Return
                    End If

                    For Each arg In args
                                If String.Equals(arg, "--preview", StringComparison.OrdinalIgnoreCase) Then
                            Log("Showing Preview")
                            Application.Run(New PagePickerForm())
                            Log("Main end")
                            Return
                        End If
                    Next
                End If

                Dim startupForm = Environment.GetEnvironmentVariable("SDC_START_FORM")
                If Not String.IsNullOrWhiteSpace(startupForm) Then
                    Select Case startupForm.Trim().ToUpperInvariant()
                        Case "FW_REGISTRATION_U"
                            Log("Showing override form: FW_Registration_U")
                            Application.Run(New FW_Registration_U())
                            Log("Main end")
                            Return
                        Case "FW_REGISTRATION_B"
                            Log("Showing override form: FW_Registration_B")
                            Application.Run(New FW_Registration_B())
                            Log("Main end")
                            Return
                    End Select
                End If

                If Not EnsureDatabaseConfigured(args) Then
                    Environment.ExitCode = 2
                    Return
                End If

                ' Field permissions are brought back in line with the database while the login
                ' screen is up, on a development run only. Nothing waits for it, and it reports
                ' through the login window because that is the first thing on screen.
                SchemaDriftWatch.StartInBackground(args)

                ' What is on the other end. A probe only - nothing acts on the answer yet.
                ClientDevice.Probe()

                Log("Creating LoginForm")
                Dim loginForm = New LoginForm()
                SchemaDriftWatch.ReportThrough(loginForm)
                Log("Showing LoginForm")
                Application.Run(loginForm)

                Log("Main end")
            Catch ex As Exception
                Log("Unhandled exception: " & ex.ToString())
                Telemetry.Error(ex, "Program.Main", Telemetry.FaultOrigin.UnhandledException)
                Throw
            Finally
                ' The last chance to write what was queued. A fault a second before the window
                ' closes is the one worth having, and it is the one a timer would miss.
                Telemetry.Flush()
            End Try
        End Sub

        ''' <summary>
        ''' Makes sure there is a usable database before the login screen, since without one there
        ''' is nothing to log in to. Returns False when the user declines to configure it.
        '''
        ''' Two ways in: --configure-db to change credentials deliberately, or nothing configured at
        ''' all. Credentials that no longer connect are reported by LoginForm, which distinguishes
        ''' a connection failure from a bad password.
        ''' </summary>
        Private Function EnsureDatabaseConfigured(args As String()) As Boolean
            Dim configureRequested = args IsNot Nothing AndAlso
                args.Any(Function(arg) String.Equals(arg, "--configure-db", StringComparison.OrdinalIgnoreCase))

            If configureRequested Then
                Log("Database configuration requested")
                Return ShowDatabaseConfiguration("Update the database credentials this application uses.")
            End If

            Dim configurationError = DataAccess.GetMissingConfigurationMessage()
            If Not String.IsNullOrWhiteSpace(configurationError) Then
                Log("Database not configured, prompting")
                Return ShowDatabaseConfiguration(configurationError)
            End If

            ' No connection check here. Probing before the login screen cost a full extra
            ' connection on every launch - about three seconds encrypted - to improve the message
            ' in a case that is rare, and it doubled the work of simply starting up. LoginForm
            ' already reports a connection failure distinctly, separate from a bad password, so
            ' the failure is surfaced where it actually happens.
            Return True
        End Function

        Private Function ShowDatabaseConfiguration(reason As String) As Boolean
            Using configForm As New FW_DatabaseConfig(reason)
                If configForm.ShowDialog() <> DialogResult.OK Then
                    Log("Database configuration cancelled")
                    Return False
                End If
            End Using

            Log("Database configuration saved")
            Return True
        End Function
    End Module
End Namespace
