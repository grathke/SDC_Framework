Option Strict On
Option Explicit On

Imports System

Namespace SDC.Framework

    ''' <summary>
    ''' Which machine, which build and which process wrote a row: the three facts every log,
    ''' journal, telemetry and session record stamps on itself.
    '''
    ''' Each answer is empty (or zero) rather than an exception when the platform will not give it,
    ''' because every caller is recording something - often a failure - and a failure to describe
    ''' the recorder is not worth a second one. A caller that shows the value to a person supplies
    ''' its own wording for the empty case; HealthMail says "this server".
    '''
    ''' One owner since 2026-09-25. DataAccess, HealthMail, OutageJournal, SessionTracking and
    ''' Telemetry each had a private copy, and they had already drifted on what failure returns.
    ''' </summary>
    Public Module ProcessIdentity

        Public Function MachineName() As String
            Try
                Return Environment.MachineName
            Catch
                Return String.Empty
            End Try
        End Function

        Public Function AppVersion() As String
            Try
                Return If(Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(), String.Empty)
            Catch
                Return String.Empty
            End Try
        End Function

        Public Function ProcessId() As Integer
            Try
                Return Diagnostics.Process.GetCurrentProcess().Id
            Catch
                Return 0
            End Try
        End Function

    End Module
End Namespace
