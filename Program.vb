Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms

Namespace HelloWorld
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
