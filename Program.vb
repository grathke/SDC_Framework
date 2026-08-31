Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace HelloWorld
    Friend Module Program
        Private ReadOnly logPath As String = Path.Combine(AppContext.BaseDirectory, "startup.log")

        Private Sub Log(message As String)
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & message & Environment.NewLine)
        End Sub

        Private Sub OnThreadException(sender As Object, e As ThreadExceptionEventArgs)
            Log("ThreadException: " & e.Exception.ToString())
        End Sub

        Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
            Log("UnhandledException: " & e.ExceptionObject.ToString())
        End Sub

        <STAThread>
        Public Sub Main()
            Try
                Log("Main start")

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
                            Console.Error.WriteLine("A PageRequestID is required after --generate-request.")
                            Environment.ExitCode = 2
                            Return
                        End If

                        Dim requestId As Integer
                        If Not Integer.TryParse(args(generateIndex + 1), requestId) OrElse requestId <= 0 Then
                            Console.Error.WriteLine("PageRequestID must be a positive integer.")
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

                Dim startupForm = Environment.GetEnvironmentVariable("HELLOWORLD_START_FORM")
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
        ''' Three ways in: --configure-db to change credentials that already work, nothing
        ''' configured at all, or configured credentials that no longer connect - the last of
        ''' these matters because a password changed on the server would otherwise leave the
        ''' application failing with no way to correct it.
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

            ' A short probe, so a dead server is reported plainly instead of surfacing as a
            ' confusing failure on the login screen. Kept brief because it runs on every launch.
            Const probeTimeoutSeconds As Integer = 5

            ' The driver does not reliably honour its own connect timeout against an unreachable
            ' address - measured at 27 seconds for a 5 second setting - so the wait is bounded here
            ' instead. An abandoned attempt finishes in the background and its result is ignored.
            Const probeGiveUpSeconds As Integer = 8

            Dim probeTask = Task.Run(Function() DataAccess.TestConfiguredConnection(probeTimeoutSeconds))

            ' Only put a window up if the check is actually slow. A healthy connection finishes
            ' well inside this, so normal startup shows nothing and stays instant.
            If Not probeTask.Wait(TimeSpan.FromMilliseconds(600)) Then
                Using probeWindow As New FW_DatabaseProbe(probeTask, probeGiveUpSeconds)
                    probeWindow.ShowDialog()
                End Using
            End If

            Dim connectionError = If(probeTask.IsCompleted,
                                     probeTask.Result,
                                     "The database did not respond within " & probeGiveUpSeconds.ToString() & " seconds.")
            If String.IsNullOrWhiteSpace(connectionError) Then Return True

            ' An unreachable database is not a credentials problem, so it is never answered with a
            ' dialog offering to replace credentials that are probably fine. Report it, and let the
            ' user start the server and retry without relaunching. Credentials are changed
            ' deliberately, through --configure-db.
            Log("Database unreachable: " & connectionError)

            Dim message = "The database could not be reached." & Environment.NewLine & Environment.NewLine &
                          connectionError & Environment.NewLine & Environment.NewLine &
                          "Start the database and choose Retry."

            If Not DataAccess.IsUsingEnvironmentCredentials() Then
                message &= Environment.NewLine & Environment.NewLine &
                           "To change the saved credentials, run the application with --configure-db."
            End If

            If MessageBox.Show(message, "Database Unavailable",
                               MessageBoxButtons.RetryCancel, MessageBoxIcon.Error) = DialogResult.Retry Then
                DataAccess.RefreshConnectionString()
                Return EnsureDatabaseConfigured(args)
            End If

            Return False
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
