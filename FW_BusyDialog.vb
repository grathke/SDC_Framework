Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Namespace HelloWorld
    ''' <summary>
    ''' A small modal window shown while something slow runs, so the application looks busy rather
    ''' than frozen. Closes itself the moment the work finishes.
    '''
    ''' Use WaitFor rather than constructing this directly: it waits briefly first, so quick work
    ''' produces no window at all and nothing flickers on the normal path.
    '''
    ''' There is no Cancel. The work it covers is bounded by its own timeout, and abandoning it
    ''' would leave the caller with no result to act on.
    ''' </summary>
    Public Class FW_BusyDialog
        Inherits Form

        Private ReadOnly work As Task
        Private ReadOnly poll As Timer

        ''' <summary>
        ''' Runs the message loop until the task completes, showing a window only if the work takes
        ''' longer than showAfterMilliseconds. Returns once the task is finished.
        ''' </summary>
        Public Shared Sub WaitFor(owner As IWin32Window,
                                  work As Task,
                                  title As String,
                                  message As String,
                                  Optional showAfterMilliseconds As Integer = 700)
            If work Is Nothing Then Return
            If work.Wait(showAfterMilliseconds) Then Return

            Using dialog As New FW_BusyDialog(work, title, message)
                dialog.ShowDialog(owner)
            End Using
        End Sub

        Private Sub New(work As Task, title As String, message As String)
            Me.work = work

            Text = title
            ClientSize = New Size(400, 120)
            FormBorderStyle = FormBorderStyle.FixedDialog
            StartPosition = FormStartPosition.CenterParent
            MaximizeBox = False
            MinimizeBox = False
            ControlBox = False

            Dim messageLabel As New Label() With {
                .Text = message,
                .Font = New Font("Segoe UI", 10.0F, FontStyle.Regular),
                .Location = New Point(20, 24),
                .Size = New Size(360, 40)
            }

            Dim progress As New ProgressBar() With {
                .Style = ProgressBarStyle.Marquee,
                .MarqueeAnimationSpeed = 30,
                .Location = New Point(20, 74),
                .Size = New Size(360, 16)
            }

            Controls.AddRange({messageLabel, progress})

            ' Polled rather than continued on the worker thread, so the close happens on the UI
            ' thread that owns this window.
            poll = New Timer() With {.Interval = 150}
            AddHandler poll.Tick, AddressOf Poll_Tick
            AddHandler Me.Shown, Sub(sender, e) poll.Start()
            AddHandler Me.FormClosed, Sub(sender, e) poll.Stop()
        End Sub

        Private Sub Poll_Tick(sender As Object, e As EventArgs)
            If Not work.IsCompleted Then Return

            poll.Stop()
            DialogResult = DialogResult.OK
            Close()
        End Sub
    End Class
End Namespace
