Option Strict On
Option Explicit On

Imports System
Imports System.Threading
Imports System.Threading.Tasks

Namespace SDC.Framework

    ''' <summary>
    ''' Pays the browse path's one-time startup cost while somebody is looking at the menu, rather
    ''' than when they open their first page.
    '''
    ''' Measured on 2026-09-22 rather than guessed at. The first browse refresh of a session reports
    ''' around 500ms that belongs to none of its parts:
    '''
    '''     first open   fetch=533  open:0  wrap:7  db:28
    '''     second       fetch=26   open:0  wrap:0  db:26
    '''
    ''' The connection was pooled, the wrapper decision cost 7ms, and the query itself took 28ms
    ''' both times. The difference is first-use initialisation inside the data path - the JIT of the
    ''' adapter machinery and the regexes that rewrite a page's SQL - none of which is a step, so
    ''' none of it was ever timed.
    '''
    ''' It is paid once per process, not once per page, and the log shows that plainly: a different
    ''' browse page opened seconds after the first one fetched in 25ms rather than 567ms. So one
    ''' throwaway query through the same path is enough to buy it for every page afterwards.
    '''
    ''' On a background thread, and deliberately after the menu is on screen: the point is that the
    ''' windows appear quickly, and a warm-up that delays the menu has moved the wait rather than
    ''' removed it.
    ''' </summary>
    Friend NotInheritable Class BrowseWarmUp

        Private Sub New()
        End Sub

        Private Shared started As Integer = 0

        ''' <summary>
        ''' Deliberately a registration read, and deliberately the shape a browse page uses: a PK
        ''' alias, a real table and the registration predicate, so the SQL goes through the same
        ''' rewriting and wrapping a page's own SQL would. A query that skipped those would warm the
        ''' connection and nothing else, which the measurement shows was never the cost.
        ''' </summary>
        Private Const WarmSql As String =
            "SELECT R.[RegistrationID] AS PK, R.[RegistrationID] FROM dbo.[FW_Registration] R WHERE R.[RegistrationID] = @RegistrationID"

        ''' <summary>
        ''' Runs once per process. Later calls return immediately, so a second login or a Switch
        ''' User cannot start it again - the cost it exists to pay has already been paid.
        ''' </summary>
        Friend Shared Sub Start()
            If Interlocked.Exchange(started, 1) = 1 Then
                Return
            End If

            Task.Run(AddressOf Warm)
        End Sub

        Private Shared Sub Warm()
            Try
                Dim session = SessionState.Current
                If Not session.HasValue Then
                    Return
                End If

                Dim registrationId = session.Value.RegistrationID
                If registrationId <= 0 Then
                    Return
                End If

                Dim timer = UsageCounters.StartTimer()

                DataAccess.GetBrowseRowsByRegistration(registrationId,
                                                       Nothing,
                                                       WarmSql,
                                                       False,
                                                       Nothing,
                                                       0,
                                                       1,
                                                       False,
                                                       "FW_Registration")

                Dim elapsed = UsageCounters.ElapsedMillis(timer)
                If elapsed.HasValue Then
                    Program.Log("Browse warm-up: " & elapsed.Value.ToString(Globalization.CultureInfo.InvariantCulture) & "ms")
                End If
            Catch
                ' Silent by design. This buys speed and nothing else: a page that opens a moment
                ' slower is the whole of the loss, and there is no user action to report it against.
            End Try
        End Sub

    End Class

End Namespace
