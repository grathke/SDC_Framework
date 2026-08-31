Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace HelloWorld
    ''' <summary>
    ''' Shown while the startup database check runs, so a slow or unreachable server looks busy
    ''' rather than frozen. Closes itself the moment the check finishes.
    '''
    ''' It is deliberately only shown when the check is actually slow — the caller waits briefly
    ''' first, so a healthy connection produces no window at all and startup stays instant.
    '''
    ''' There is no Cancel. The check is bounded by its connect timeout, and cancelling would only
    ''' leave the application with no idea whether the database is there.
    ''' </summary>
    Public Class FW_DatabaseProbe
        Inherits Form

        Private ReadOnly probeTask As Task(Of String)
        Private ReadOnly countdownLabel As Label
        Private ReadOnly countdownTimer As Timer
        Private ReadOnly startedAt As DateTime = DateTime.UtcNow
        Private ReadOnly timeoutSeconds As Integer

        Public Sub New(probe As Task(Of String), timeoutSeconds As Integer)
            Me.probeTask = probe
            Me.timeoutSeconds = timeoutSeconds

            Text = "Connecting"
            ClientSize = New Size(420, 150)
            FormBorderStyle = FormBorderStyle.FixedDialog
            StartPosition = FormStartPosition.CenterScreen
            MaximizeBox = False
            MinimizeBox = False
            ControlBox = False

            Dim titleLabel As New Label() With {
                .Text = "Connecting to the database",
                .Font = New Font("Segoe UI", 12.0F, FontStyle.Bold),
                .Location = New Point(20, 18),
                .AutoSize = True
            }

            Dim targetLabel As New Label() With {
                .Text = DataAccess.GetConnectionDescription(),
                .Location = New Point(20, 46),
                .Size = New Size(380, 20),
                .ForeColor = SystemColors.GrayText
            }

            Dim progress As New ProgressBar() With {
                .Style = ProgressBarStyle.Marquee,
                .MarqueeAnimationSpeed = 30,
                .Location = New Point(20, 76),
                .Size = New Size(380, 16)
            }

            countdownLabel = New Label() With {
                .Location = New Point(20, 102),
                .Size = New Size(380, 20),
                .ForeColor = SystemColors.GrayText,
                .Text = "Waiting for a response..."
            }

            Controls.AddRange({titleLabel, targetLabel, progress, countdownLabel})

            ' Polls the probe rather than continuing on its thread, so the close happens on the UI
            ' thread and the countdown stays honest about how long is left.
            countdownTimer = New Timer() With {.Interval = 250}
            AddHandler countdownTimer.Tick, AddressOf CountdownTimer_Tick
            AddHandler Me.Shown, Sub(sender, e) countdownTimer.Start()
            AddHandler Me.FormClosed, Sub(sender, e) countdownTimer.Stop()
        End Sub

        ''' <summary>
        ''' Closes when the probe finishes, or when the deadline passes - whichever comes first.
        ''' The deadline matters: the driver does not reliably honour its own connect timeout
        ''' against an unreachable address, so the only dependable bound on what the user waits is
        ''' one the application enforces itself. An abandoned attempt finishes in the background
        ''' and its result is discarded.
        ''' </summary>
        Private Sub CountdownTimer_Tick(sender As Object, e As EventArgs)
            Dim elapsed = (DateTime.UtcNow - startedAt).TotalSeconds

            If probeTask.IsCompleted OrElse elapsed >= timeoutSeconds Then
                countdownTimer.Stop()
                DialogResult = DialogResult.OK
                Close()
                Return
            End If

            Dim remaining = timeoutSeconds - CInt(Math.Floor(elapsed))
            countdownLabel.Text = "Waiting for a response... giving up in " & Math.Max(1, remaining).ToString() & "s"
        End Sub
    End Class
End Namespace
