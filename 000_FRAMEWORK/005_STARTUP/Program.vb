Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Windows.Forms

Namespace SDC.Framework
    Friend Module Program
        Private ReadOnly logPath As String = Path.Combine(AppContext.BaseDirectory, "startup.log")

        Private Sub Log(message As String)
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

        Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
            Log("UnhandledException: " & e.ExceptionObject.ToString())
        End Sub

        ''' <summary>
        ''' How long Start() waits for a browser before giving up and letting the application open
        ''' on the desktop. The SDK's own default is 60000.
        ''' </summary>
        Private Const VirtualUIStartTimeoutMs As Integer = 5000

        ''' <summary>
        ''' Held for the life of the process. THINFINITY_NOTES.md section 3: constructing VirtualUI
        ''' also constructs the shared static instance the event handlers attach to, so it is
        ''' constructed once and kept - not left to be collected and disposed mid-session.
        ''' </summary>
        Private virtualUI As Cybele.Thinfinity.VirtualUI

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

            Try
                virtualUI = New Cybele.Thinfinity.VirtualUI()
                virtualUI.DevMode = True

                ' Five seconds, not the default sixty. Start() blocks until a browser attaches or
                ' the timeout expires, and on a developer machine nobody is usually waiting at the
                ' other end - measured on 2026-09-10, the first run sat for sixty-nine seconds
                ' showing nothing at all before the login screen appeared.
                '
                ' A browser that is already open attaches in well under five. One that is not
                ' costs five seconds and then the application opens on the desktop, which is what
                ' a developer wanted in that case anyway.
                Dim started = virtualUI.Start(VirtualUIStartTimeoutMs)
                Log("VirtualUI Start() returned " & started.ToString() &
                    "; Active=" & virtualUI.Active.ToString() &
                    "; DevServer.Enabled=" & virtualUI.DevServer.Enabled.ToString() &
                    "; DevServer.Port=" & virtualUI.DevServer.Port.ToString())
            Catch ex As Exception
                Log("VirtualUI did not start: " & ex.Message)
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

                Log("Creating LoginForm")
                Dim loginForm = New LoginForm()
                Log("Showing LoginForm")
                Application.Run(loginForm)

                Log("Main end")
            Catch ex As Exception
                Log("Unhandled exception: " & ex.ToString())
                Throw
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
