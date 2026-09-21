Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports System.Net
Imports System.Net.Mail
Imports System.Text
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>
    ''' Telling the people who asked to be told.
    '''
    ''' **Only when something new lands in Needs Attention.** Not on every fault - a fault ticking
    ''' from four occurrences to five is not news. A fault nobody has seen before is, and so is one
    ''' somebody marked fixed that has happened again, because that says the fix did not work.
    '''
    ''' **One mail for a batch, never one per fault.** Telemetry writes in thirty-second batches.
    ''' A bad deploy produces a dozen distinct faults in a minute, and a dozen separate mails about
    ''' it is how somebody learns to filter this address into a folder they never open.
    '''
    ''' **No credentials in any file that travels.** Host, port, user and password come from the
    ''' environment, exactly as the database password does. run-local.ps1 supplies them here and is
    ''' gitignored; run-local.ps1.example documents the names with the password left blank.
    '''
    ''' **Absent credentials are the off switch.** A machine with no SMTP settings sends nothing
    ''' and says nothing about it, which is what a developer's machine should do. SDC_HEALTH_MAIL
    ''' set to off, 0 or false silences it even where the settings exist.
    '''
    ''' **It never throws and never blocks the caller.** This rides the telemetry flush, and a mail
    ''' server being slow must not hold up a background write - still less take down the thing it
    ''' was trying to report on.
    ''' </summary>
    Public Module HealthMail

        ''' <summary>One fault worth mentioning, as the mail will list it.</summary>
        Public Class Item
            Public Property Headline As String = String.Empty
            Public Property PageName As String = String.Empty
            Public Property Context As String = String.Empty
            Public Property Message As String = String.Empty
            Public Property Recurred As Boolean
        End Class

        Private Const DefaultPort As Integer = 587

        ''' <summary>
        ''' Whether mail can be sent at all.
        '''
        ''' Deliberately not a database read. This is asked while reporting that something has gone
        ''' wrong, and a switch that needs the database is unreadable at the moment it matters.
        ''' </summary>
        Public Function IsConfigured() As Boolean
            If Off() Then Return False

            Return Setting("SDC_MAIL_HOST") <> String.Empty AndAlso
                   Setting("SDC_MAIL_USER") <> String.Empty AndAlso
                   Setting("SDC_MAIL_PASSWORD") <> String.Empty
        End Function

        ''' <summary>
        ''' Tells the recipients about faults that have just appeared.
        '''
        ''' Takes the connection the telemetry flush already has open, because the recipient list
        ''' is rows in a table and this is called immediately after a successful write - the one
        ''' moment the database is known to be reachable.
        ''' </summary>
        Friend Sub NotifyNewFaults(conn As SqlConnection, items As List(Of Item))
            Try
                If items Is Nothing OrElse items.Count = 0 Then Return
                If Not IsConfigured() Then Return

                Dim recipients = ReadRecipients(conn)
                If recipients.Count = 0 Then Return

                Dim subject = BuildSubject(items)
                Dim body = BuildBody(items)

                ' On a thread of its own. An unreachable mail server takes as long to give up as an
                ' unreachable database did, and this is called from the flush that every page is
                ' waiting behind.
                Dim toSend = recipients
                Threading.Tasks.Task.Run(Sub() Send(toSend, subject, body))

            Catch
                ' Silent on purpose. A failure to report a fault must not become a second fault,
                ' and the faults themselves are already safely written.
            End Try
        End Sub

        ''' <summary>
        ''' Who asked to be told.
        '''
        ''' An App Admin with the box ticked and an email address. All three are required: the box
        ''' alone would mail somebody who has lost the role, and the role alone would mail every
        ''' administrator whether they wanted it or not.
        '''
        ''' Deliberately not scoped to a registration. An application-wide fault is not any one
        ''' tenant's business, and the App Admin is the person whose business it is.
        ''' </summary>
        Private Function ReadRecipients(conn As SqlConnection) As List(Of String)
            Dim found As New List(Of String)()

            Using cmd As New SqlCommand(
                "SELECT DISTINCT LTRIM(RTRIM(e.Email)) AS Email " &
                "FROM dbo.FW_Employees e " &
                "JOIN dbo.FW_EmployeeRoles er ON er.EmployeeID = e.EmployeeID AND ISNULL(er.DeletedFlag, 0) = 0 " &
                "JOIN dbo.FW_Roles r ON r.ID = er.RoleID " &
                "WHERE ISNULL(e.ReceivesHealthAlerts, 0) = 1 " &
                "  AND ISNULL(e.DeletedFlag, 0) = 0 " &
                "  AND NULLIF(LTRIM(RTRIM(e.Email)), '') IS NOT NULL " &
                "  AND ISNULL(r.Typ_AppAdmin, 0) = 1", conn)

                Using reader = cmd.ExecuteReader()
                    While reader.Read()
                        found.Add(Convert.ToString(reader("Email"), CultureInfo.InvariantCulture))
                    End While
                End Using
            End Using

            Return found
        End Function

        ''' <summary>
        ''' The subject line, which is the whole message for somebody reading on a phone.
        '''
        ''' It says the count and the worst thing in it. "SDC health: 1 new fault" is answerable
        ''' from the lock screen; "SDC Framework Notification" is not.
        ''' </summary>
        Private Function BuildSubject(items As List(Of Item)) As String
            ' Where().Count() rather than Count(predicate): List(Of T)'s own Count property hides
            ' the extension method that takes one.
            Dim recurred = items.Where(Function(i) i.Recurred).Count()
            Dim fresh = items.Count - recurred

            Dim parts As New List(Of String)()
            If fresh > 0 Then parts.Add(fresh.ToString(CultureInfo.InvariantCulture) & " new fault" & Plural(fresh))
            If recurred > 0 Then parts.Add(recurred.ToString(CultureInfo.InvariantCulture) & " back after a fix")

            Return "SDC health: " & String.Join(", ", parts)
        End Function

        ''' <summary>
        ''' Plain text, not HTML.
        '''
        ''' It is read on a phone at an awkward hour by somebody deciding whether to get up. Every
        ''' line earns its place, the machine and build are named because the first question is
        ''' always "where", and it closes by saying what to open rather than leaving that implied.
        ''' </summary>
        Private Function BuildBody(items As List(Of Item)) As String
            Dim body As New StringBuilder()

            body.AppendLine("Something new arrived in Needs Attention on " & SafeMachineName() & ".")
            body.AppendLine()

            For Each item In items
                body.AppendLine(If(item.Recurred, "BACK AFTER A FIX  ", "NEW  ") & item.Headline)

                If item.PageName <> String.Empty Then body.AppendLine("    Page     " & item.PageName)
                If item.Context <> String.Empty Then body.AppendLine("    Caught   " & item.Context)
                If item.Message <> String.Empty Then body.AppendLine("    " & item.Message)

                body.AppendLine()
            Next

            body.AppendLine("Open System Health from the Application dashboard for the stack trace,")
            body.AppendLine("how often it has happened, and what was done about it last time.")
            body.AppendLine()
            body.AppendLine("Build " & SafeAppVersion() & ", " & Date.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) & " UTC.")
            body.AppendLine("You are receiving this because Receives Health Alerts is ticked on your employee record.")

            Return body.ToString()
        End Function

        Private Sub Send(recipients As List(Of String), subject As String, body As String)
            Try
                Dim host = Setting("SDC_MAIL_HOST")
                Dim user = Setting("SDC_MAIL_USER")
                Dim password = Setting("SDC_MAIL_PASSWORD")
                Dim port = DefaultPort
                Integer.TryParse(Setting("SDC_MAIL_PORT"), port)
                If port <= 0 Then port = DefaultPort

                Using client As New SmtpClient(host, port)
                    ' STARTTLS on 587. Not 465, whose implicit SSL SmtpClient has never properly
                    ' supported - it connects, negotiates nothing, and times out looking healthy.
                    client.EnableSsl = True
                    client.Credentials = New NetworkCredential(user, password)
                    client.Timeout = 20000

                    Using message As New MailMessage()
                        message.From = New MailAddress(user, "SDC Framework")
                        message.Subject = subject
                        message.Body = body
                        message.IsBodyHtml = False

                        For Each address In recipients
                            message.To.Add(address)
                        Next

                        client.Send(message)
                    End Using
                End Using

                Program.Log("Health mail sent to " & recipients.Count.ToString(CultureInfo.InvariantCulture) & " recipient(s)")

            Catch ex As Exception
                ' To the log, never to Telemetry. A failure to send a fault report becoming a fault
                ' report would mail itself about being unable to mail.
                Program.Log("Health mail failed: " & ex.Message)
            End Try
        End Sub

        Private Function Off() As Boolean
            Dim switch = Setting("SDC_HEALTH_MAIL").ToLowerInvariant()
            Return switch = "off" OrElse switch = "0" OrElse switch = "false" OrElse switch = "no"
        End Function

        Private Function Setting(name As String) As String
            Try
                Return If(Environment.GetEnvironmentVariable(name), String.Empty).Trim()
            Catch
                Return String.Empty
            End Try
        End Function

        Private Function Plural(count As Integer) As String
            Return If(count = 1, "", "s")
        End Function

        Private Function SafeMachineName() As String
            Try
                Return Environment.MachineName
            Catch
                Return "this server"
            End Try
        End Function

        Private Function SafeAppVersion() As String
            Try
                Return Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            Catch
                Return "unknown"
            End Try
        End Function

    End Module
End Namespace
