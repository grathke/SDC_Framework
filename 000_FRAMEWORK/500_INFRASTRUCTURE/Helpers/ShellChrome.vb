Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The look the shell already has, made available to the forms that come before it.
    '''
    ''' The main menu has a title band, a blue accent and a white card on a light ground. The login
    ''' and role-selection dialogs had none of it - grey boxes with system buttons - so the first
    ''' two screens a user sees are the two that look least like the product.
    '''
    ''' Nothing here knows about a particular application: the title comes from the assembly, the
    ''' same way the menu's does, so a second application built on this framework carries its own.
    ''' </summary>
    Public Module ShellChrome

        ''' The blue the ribbon and the region headers already use for emphasis.
        Public ReadOnly Accent As Color = Color.FromArgb(58, 133, 197)

        ''' The near-black the menu's title band uses. Not pure black, which reads as harsh against
        ''' white at this size.
        Public ReadOnly TitleInk As Color = Color.FromArgb(52, 60, 70)

        Public ReadOnly BodyInk As Color = Color.FromArgb(76, 84, 94)

        ''' The two ends of the backdrop gradient - a light, cool wash rather than a colour of its
        ''' own, so the white card in front of it stays the thing the eye lands on.
        Public ReadOnly BackdropTop As Color = Color.FromArgb(238, 244, 250)
        Public ReadOnly BackdropBottom As Color = Color.FromArgb(214, 228, 242)

        Public Const TitleBandHeight As Integer = 52

        ''' <summary>
        ''' The application's name, from the assembly rather than typed anywhere - the same
        ''' resolution the main menu's title band uses, so the three screens cannot disagree.
        ''' </summary>
        Public Function ApplicationTitle() As String
            Dim name = Application.ProductName
            If String.IsNullOrWhiteSpace(name) Then
                name = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name
            End If
            If String.IsNullOrWhiteSpace(name) Then Return "SDC Framework"

            Return DisplayNameFormatter.ToDisplayName(name.Trim().Replace("."c, "_"c), stripFrameworkPrefix:=False)
        End Function

        ''' <summary>
        ''' Paints a vertical gradient across a control, for use as a Paint handler.
        '''
        ''' Drawn rather than an image so it costs nothing to ship and scales to any size. Under
        ''' VirtualUI a gradient is also cheaper to stream than a photograph: large flat-ish areas
        ''' compress well, where a detailed background would be re-sent on every resize.
        ''' </summary>
        Public Sub PaintBackdrop(sender As Object, e As PaintEventArgs)
            Dim surface = TryCast(sender, Control)
            If surface Is Nothing OrElse surface.ClientSize.Width <= 0 OrElse surface.ClientSize.Height <= 0 Then Return

            Using brush As New LinearGradientBrush(surface.ClientRectangle, BackdropTop, BackdropBottom, LinearGradientMode.Vertical)
                e.Graphics.FillRectangle(brush, surface.ClientRectangle)
            End Using

            PaintShellMock(e.Graphics, surface.ClientRectangle)
        End Sub

        ''' <summary>
        ''' Draws the shape of the main menu behind the card - a title band, a ribbon of tiles and
        ''' the region panels - in pale grey, as a backdrop rather than a preview.
        '''
        ''' Drawn rather than a screenshot on purpose. A captured image shows one registration's
        ''' data, one role's tiles and one moment's layout, and it is wrong the day after it is
        ''' taken; this is the same shape at any size, with nothing in it that can go stale or leak
        ''' somebody's information onto a login screen.
        '''
        ''' Everything here is proportional to the surface, so it fills whatever window the session
        ''' gives it.
        ''' </summary>
        Private Sub PaintShellMock(g As Graphics, area As Rectangle)
            If area.Width < 320 OrElse area.Height < 240 Then Return

            g.SmoothingMode = SmoothingMode.AntiAlias

            Dim margin = CInt(area.Width * 0.02)
            Dim shell As New Rectangle(area.Left + margin, area.Top + margin,
                                       area.Width - margin * 2, area.Height - margin * 2)

            ' Strong enough to read as a shell rather than a smudge. It was 38 alpha on a pale
            ' gradient, which at a glance looked like nothing at all.
            Dim ink = Color.FromArgb(110, 74, 108, 142)
            Dim paper = Color.FromArgb(225, 255, 255, 255)

            Using body As New SolidBrush(paper)
                g.FillRectangle(body, shell)
            End Using
            Using edge As New Pen(Color.FromArgb(150, 74, 108, 142))
                g.DrawRectangle(edge, shell)
            End Using

            ' The title band: a bar standing in for the application name, centred.
            Dim bandHeight = CInt(shell.Height * 0.07)
            Using bar As New SolidBrush(ink)
                Dim titleWidth = CInt(shell.Width * 0.28)
                g.FillRectangle(bar, shell.Left + (shell.Width - titleWidth) \ 2,
                                     shell.Top + bandHeight \ 3,
                                     titleWidth, Math.Max(6, bandHeight \ 3))
            End Using

            ' The ribbon: a bordered strip of evenly spaced tiles, the row this shell is known by.
            Dim ribbon As New Rectangle(shell.Left + 12, shell.Top + bandHeight + 8,
                                        shell.Width - 24, CInt(shell.Height * 0.17))
            Using edge As New Pen(Color.FromArgb(140, 74, 108, 142))
                g.DrawRectangle(edge, ribbon)
            End Using

            Dim tileCount = 11
            Dim pitch = ribbon.Width \ tileCount
            Dim tileSize = CInt(Math.Min(pitch * 0.62, ribbon.Height * 0.46))
            Using tile As New SolidBrush(ink)
                For index = 0 To tileCount - 1
                    Dim cx = ribbon.Left + pitch * index + (pitch - tileSize) \ 2
                    Dim cy = ribbon.Top + CInt(ribbon.Height * 0.22)
                    g.FillEllipse(tile, cx, cy, tileSize, tileSize)
                    g.FillRectangle(tile, cx - tileSize \ 4, cy + tileSize + 6,
                                          tileSize + tileSize \ 2, Math.Max(3, tileSize \ 5))
                Next
            End Using

            ' The regions below: one tall panel on the left and three across, which is the shape
            ' the menu actually has.
            Dim regionTop = ribbon.Bottom + 14
            Dim regionHeight = shell.Bottom - regionTop - 12
            If regionHeight < 40 Then Return

            Dim leftWidth = CInt(shell.Width * 0.3)
            Using edge As New Pen(Color.FromArgb(130, 74, 108, 142))
                g.DrawRectangle(edge, shell.Left + 12, regionTop, leftWidth, regionHeight)

                Dim restLeft = shell.Left + 12 + leftWidth + 12
                Dim restWidth = shell.Right - 12 - restLeft
                Dim topHeight = CInt(regionHeight * 0.52)
                Dim columnWidth = (restWidth - 24) \ 3

                For column = 0 To 2
                    g.DrawRectangle(edge, restLeft + column * (columnWidth + 12), regionTop, columnWidth, topHeight)
                Next

                g.DrawRectangle(edge, restLeft, regionTop + topHeight + 12,
                                      restWidth, regionHeight - topHeight - 12)
            End Using
        End Sub

        ''' <summary>
        ''' Turns a form into a card on a backdrop: the gradient behind, a white panel in front,
        ''' and the application's name in a title band across the top of that panel.
        '''
        ''' Returns the card, which the caller fills. Everything the caller adds goes on the card
        ''' rather than the form, so the backdrop stays visible around it.
        ''' </summary>
        Public Function BuildCard(host As Form, cardSize As Size, Optional subtitle As String = Nothing) As Panel
            host.BackColor = BackdropTop
            AddHandler host.Paint, AddressOf PaintBackdrop
            AddHandler host.Resize, Sub() host.Invalidate()

            Dim card As New Panel() With {
                .Size = cardSize,
                .BackColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle
            }
            CentreOn(host, card)
            AddHandler host.Resize, Sub() CentreOn(host, card)

            Dim band As New Panel() With {
                .Dock = DockStyle.Top,
                .Height = If(String.IsNullOrWhiteSpace(subtitle), TitleBandHeight, TitleBandHeight + 22),
                .BackColor = Color.White
            }

            Dim title As New Label() With {
                .Text = ApplicationTitle(),
                .Dock = DockStyle.Top,
                .Height = TitleBandHeight,
                .TextAlign = ContentAlignment.MiddleCenter,
                .Font = New Font("Segoe UI", 18.0F, FontStyle.Regular),
                .ForeColor = TitleInk,
                .BackColor = Color.White
            }
            band.Controls.Add(title)

            If Not String.IsNullOrWhiteSpace(subtitle) Then
                Dim caption As New Label() With {
                    .Text = subtitle,
                    .Dock = DockStyle.Top,
                    .Height = 22,
                    .TextAlign = ContentAlignment.MiddleCenter,
                    .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                    .ForeColor = BodyInk,
                    .BackColor = Color.White
                }
                band.Controls.Add(caption)
                caption.BringToFront()
            End If

            ' A hairline under the band, the width of the card. Separates the heading from the
            ' content without the weight of a second border.
            Dim rule As New Panel() With {
                .Dock = DockStyle.Bottom,
                .Height = 1,
                .BackColor = Color.FromArgb(224, 231, 238)
            }
            band.Controls.Add(rule)

            card.Controls.Add(band)
            host.Controls.Add(card)
            card.BringToFront()

            Return card
        End Function

        Private Sub CentreOn(host As Form, child As Control)
            child.Location = New Point(Math.Max(0, (host.ClientSize.Width - child.Width) \ 2),
                                       Math.Max(0, (host.ClientSize.Height - child.Height) \ 2))
        End Sub

        ''' <summary>
        ''' The button that does the thing - filled in the accent colour, white text.
        ''' </summary>
        Public Sub StylePrimary(button As Button)
            If button Is Nothing Then Return

            button.FlatStyle = FlatStyle.Flat
            button.BackColor = Accent
            button.ForeColor = Color.White
            button.Font = New Font("Segoe UI", 9.75F, FontStyle.Regular)
            button.FlatAppearance.BorderSize = 0
            button.UseVisualStyleBackColor = False
        End Sub

        ''' <summary>
        ''' The button that declines - outlined, so it reads as the quieter of the pair.
        ''' </summary>
        Public Sub StyleSecondary(button As Button)
            If button Is Nothing Then Return

            button.FlatStyle = FlatStyle.Flat
            button.BackColor = Color.White
            button.ForeColor = BodyInk
            button.Font = New Font("Segoe UI", 9.75F, FontStyle.Regular)
            button.FlatAppearance.BorderSize = 1
            button.FlatAppearance.BorderColor = Color.FromArgb(196, 206, 216)
            button.UseVisualStyleBackColor = False
        End Sub

    End Module

End Namespace
