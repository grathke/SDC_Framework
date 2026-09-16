Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.Text

Namespace SDC.Framework

    ''' <summary>
    ''' What the browser on the other end actually is.
    '''
    ''' A probe, for now. It reports what VirtualUI says and writes it to startup.log; nothing acts
    ''' on the answer yet. The point is to find out what a phone really reports through the session
    ''' before a layout is designed around an assumption about it - THINFINITY_NOTES.md section 10
    ''' already records that two places assume the viewport is fixed for the session, and that has
    ''' never been tested.
    '''
    ''' Deliberately not parsing the user agent to decide. A narrow viewport is the thing that
    ''' actually breaks a 980 pixel page, and user agent strings have lied for thirty years. The
    ''' agent is logged so it can be read, not so it can be believed.
    ''' </summary>
    Public NotInheritable Class ClientDevice

        Private Sub New()
        End Sub

        ''' <summary>
        ''' The widest viewport still treated as a phone.
        '''
        ''' A guess until the probe says otherwise, which is the whole reason the probe exists.
        ''' </summary>
        Public Const PhoneViewWidth As Integer = 640

        Public Enum Shape
            Unknown
            Desktop
            Phone
        End Enum

        ''' <summary>
        ''' Reads the connected browser and writes what it found.
        '''
        ''' Returns Unknown on the desktop and on --no-tf: there is no browser to ask, and guessing
        ''' from the monitor would answer a different question.
        ''' </summary>
        Public Shared Function Probe() As Shape
            If Not Program.InBrowserSession Then
                Program.Log("Client device: no browser session, nothing to ask")
                Return Shape.Unknown
            End If

            Try
                Dim session = Program.VirtualUISession
                If session Is Nothing OrElse session.BrowserInfo Is Nothing Then
                    Program.Log("Client device: session active but BrowserInfo unavailable")
                    Return Shape.Unknown
                End If

                Dim info = session.BrowserInfo
                Dim viewWidth = info.ViewWidth
                Dim viewHeight = info.ViewHeight

                Dim report As New StringBuilder("Client device: ")
                report.Append("view ").Append(viewWidth.ToString(CultureInfo.InvariantCulture)).
                       Append("x").Append(viewHeight.ToString(CultureInfo.InvariantCulture))
                report.Append("; browser ").Append(info.BrowserWidth.ToString(CultureInfo.InvariantCulture)).
                       Append("x").Append(info.BrowserHeight.ToString(CultureInfo.InvariantCulture))
                report.Append("; screen ").Append(info.ScreenWidth.ToString(CultureInfo.InvariantCulture)).
                       Append("x").Append(info.ScreenHeight.ToString(CultureInfo.InvariantCulture))
                report.Append("; resolution ").Append(info.ScreenResolution.ToString(CultureInfo.InvariantCulture))
                report.Append("; orientation ").Append(info.Orientation.ToString())
                report.Append("; agent ").Append(If(info.UserAgent, "(none)"))

                ' A viewport of zero means the browser has not reported one yet rather than that it
                ' has none, and calling that a phone would be the worst possible guess.
                Dim shaped = If(viewWidth <= 0, Shape.Unknown,
                                If(viewWidth <= PhoneViewWidth, Shape.Phone, Shape.Desktop))

                report.Append("; read as ").Append(shaped.ToString())
                Program.Log(report.ToString())

                Return shaped
            Catch ex As Exception
                Program.Log("Client device: probe failed - " & ex.Message)
                Return Shape.Unknown
            End Try
        End Function

    End Class

End Namespace
