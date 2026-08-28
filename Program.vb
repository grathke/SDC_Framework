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

                Dim configurationError = DataAccess.GetMissingConfigurationMessage()
                If Not String.IsNullOrWhiteSpace(configurationError) Then
                    Log("Startup blocked, database not configured")
                    MessageBox.Show(configurationError, "Database Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Error)
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
    End Module
End Namespace
