Option Strict On
Option Explicit On

Imports System
Imports System.ComponentModel
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The speedometer on the health page. A 180-degree arc, three coloured bands, tick marks, a
    ''' needle, and the score in a large face beneath it.
    '''
    ''' GDI+ only, no third-party dependency. It draws on refresh and on resize and at no other
    ''' time: over Thinfinity every frame is a round trip to the browser, so a sweeping needle
    ''' would cost a great deal to say exactly what a still one says. There is no hover behaviour
    ''' for the same reason - anything the gauge has to communicate is drawn on it.
    '''
    ''' Every measurement is taken from its own bounds rather than from constants, so it scales with
    ''' PageZoom like any other control without knowing that PageZoom exists.
    '''
    ''' The band is said twice on purpose - in the colour of the needle and arc, and in the word
    ''' beneath the number. Colour alone would not reach somebody who cannot distinguish these
    ''' three, and the word alone would not read at a glance.
    ''' </summary>
    Public Class HealthGauge
        Inherits Control

        Private scoreValue As Double = 0
        Private hasScoreValue As Boolean = False

        ''' <summary>Band thresholds, from HEALTH_DASHBOARD_SPEC.md section 3.</summary>
        Private Const GreenFloor As Double = 90
        Private Const AmberFloor As Double = 75

        Private Shared ReadOnly GreenColour As Color = Color.FromArgb(35, 160, 85)
        Private Shared ReadOnly AmberColour As Color = Color.FromArgb(232, 160, 25)
        Private Shared ReadOnly RedColour As Color = Color.FromArgb(200, 55, 50)
        Private Shared ReadOnly DialColour As Color = Color.FromArgb(225, 228, 232)
        Private Shared ReadOnly TextColour As Color = Color.FromArgb(45, 48, 52)

        Public Sub New()
            ' Double buffered because the arc, the bands, the ticks and the needle are four passes
            ' over the same pixels. Unbuffered, a resize shows them arriving separately.
            '
            ' SupportsTransparentBackColor is not optional here and its absence is not a cosmetic
            ' fault: a plain Control throws ArgumentException - "Control does not support
            ' transparent background colors" - the moment BackColor is set to Transparent below.
            ' Caught on the first open of the page on 2026-09-20.
            SetStyle(ControlStyles.AllPaintingInWmPaint Or
                     ControlStyles.UserPaint Or
                     ControlStyles.OptimizedDoubleBuffer Or
                     ControlStyles.SupportsTransparentBackColor Or
                     ControlStyles.ResizeRedraw, True)

            ' Transparent rather than a colour of its own, so the gauge sits on whatever the page
            ' behind it is and follows a page restyled later without being told.
            BackColor = Color.Transparent
            Size = New Size(360, 240)
        End Sub

        ''' <summary>
        ''' The score, 0 to 100. Nothing is drawn but the dial until this is set, so an empty
        ''' database shows an unpopulated gauge rather than a confident zero - which would read as
        ''' "everything is broken" when it means "nothing has been measured".
        '''
        ''' Hidden from the designer: this is set from a query at run time and has no business
        ''' being serialised into designer code, which is also what WFO1000 is warning about.
        ''' </summary>
        <Browsable(False)>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Score As Double
            Get
                Return scoreValue
            End Get
            Set(value As Double)
                scoreValue = Math.Max(0, Math.Min(100, value))
                hasScoreValue = True
                Invalidate()
            End Set
        End Property

        ''' <summary>Puts it back to the unmeasured state, for a refresh that finds no data.</summary>
        Public Sub ClearScore()
            hasScoreValue = False
            scoreValue = 0
            Invalidate()
        End Sub

        Public ReadOnly Property BandColour As Color
            Get
                Return ColourFor(scoreValue)
            End Get
        End Property

        Public ReadOnly Property BandName As String
            Get
                If Not hasScoreValue Then Return "NO DATA"
                If scoreValue >= GreenFloor Then Return "HEALTHY"
                If scoreValue >= AmberFloor Then Return "WATCH"
                Return "UNHEALTHY"
            End Get
        End Property

        Private Shared Function ColourFor(value As Double) As Color
            If value >= GreenFloor Then Return GreenColour
            If value >= AmberFloor Then Return AmberColour
            Return RedColour
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)

            Dim g = e.Graphics
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

            ' Everything below is derived from these. Change the control's size and the whole gauge
            ' follows; there is no constant here that assumes a particular one.
            '
            ' The readout band is reserved FIRST and the arc sized into what is left. Sizing the arc
            ' to the full height and then drawing the number under it is what clipped the score off
            ' the bottom of the control on 2026-09-20 - the number being the one thing the gauge
            ' exists to show.
            Dim pad = Math.Max(8, Width \ 24)
            Dim readoutBand = Math.Max(46.0F, Height * 0.30F)

            Dim heightForArc = Height - readoutBand - pad
            If heightForArc <= 20 Then Return

            Dim diameter = Math.Min(Width - (pad * 2), heightForArc * 2.0F)
            If diameter <= 40 Then Return

            Dim radius = diameter / 2.0F
            Dim centre = New PointF(Width / 2.0F, pad + radius)
            Dim thickness = Math.Max(10.0F, radius * 0.22F)

            DrawBands(g, centre, radius, thickness)
            DrawTicks(g, centre, radius, thickness)

            If hasScoreValue Then
                DrawNeedle(g, centre, radius - thickness - 4.0F)
                DrawHub(g, centre, radius)
            End If

            DrawReadout(g, centre, radius, readoutBand)
        End Sub

        ''' <summary>
        ''' The three bands, drawn as arcs of the same ring. Swept left to right - 180 degrees is
        ''' due west in GDI+ terms, and the arc runs clockwise from there to due east.
        ''' </summary>
        Private Shared Sub DrawBands(g As Graphics, centre As PointF, radius As Single, thickness As Single)
            Dim outer = New RectangleF(centre.X - radius, centre.Y - radius, radius * 2, radius * 2)
            Dim inset = thickness / 2.0F
            Dim ring = RectangleF.Inflate(outer, -inset, -inset)

            Using dial As New Pen(DialColour, thickness)
                dial.StartCap = LineCap.Flat
                dial.EndCap = LineCap.Flat
                g.DrawArc(dial, ring, 180, 180)
            End Using

            DrawBandArc(g, ring, thickness, RedColour, 0, AmberFloor)
            DrawBandArc(g, ring, thickness, AmberColour, AmberFloor, GreenFloor)
            DrawBandArc(g, ring, thickness, GreenColour, GreenFloor, 100)
        End Sub

        Private Shared Sub DrawBandArc(g As Graphics,
                                       ring As RectangleF,
                                       thickness As Single,
                                       colour As Color,
                                       fromValue As Double,
                                       toValue As Double)
            Dim startAngle = CSng(180 + (fromValue * 1.8))
            Dim sweep = CSng((toValue - fromValue) * 1.8)
            If sweep <= 0 Then Return

            ' Bands sit on a paler version of themselves rather than at full strength. The needle
            ' is the thing being read; a ring at full saturation competes with it.
            Using pen As New Pen(Color.FromArgb(150, colour), thickness)
                pen.StartCap = LineCap.Flat
                pen.EndCap = LineCap.Flat
                g.DrawArc(pen, ring, startAngle, sweep)
            End Using
        End Sub

        ''' <summary>Tick marks every 20, with the number outside each one.</summary>
        Private Sub DrawTicks(g As Graphics, centre As PointF, radius As Single, thickness As Single)
            Dim labelFont = New Font(Font.FontFamily, Math.Max(7.0F, radius * 0.085F), FontStyle.Regular)

            Try
                Using pen As New Pen(Color.FromArgb(120, 128, 136), Math.Max(1.5F, radius * 0.012F))
                    Using brush As New SolidBrush(Color.FromArgb(105, 112, 120))
                        For value = 0 To 100 Step 20
                            Dim radians = DegreesToRadians(180 + (value * 1.8))
                            Dim cos = CSng(Math.Cos(radians))
                            Dim sin = CSng(Math.Sin(radians))

                            Dim innerR = radius - thickness - 2.0F
                            Dim outerR = radius - thickness + (thickness * 0.55F)

                            g.DrawLine(pen,
                                       centre.X + (cos * innerR), centre.Y + (sin * innerR),
                                       centre.X + (cos * outerR), centre.Y + (sin * outerR))

                            Dim labelR = radius - thickness - (radius * 0.14F)
                            Dim text = value.ToString(Globalization.CultureInfo.InvariantCulture)
                            Dim size = g.MeasureString(text, labelFont)

                            g.DrawString(text, labelFont, brush,
                                         centre.X + (cos * labelR) - (size.Width / 2.0F),
                                         centre.Y + (sin * labelR) - (size.Height / 2.0F))
                        Next
                    End Using
                End Using
            Finally
                labelFont.Dispose()
            End Try
        End Sub

        Private Sub DrawNeedle(g As Graphics, centre As PointF, length As Single)
            Dim radians = DegreesToRadians(180 + (scoreValue * 1.8))
            Dim tip = New PointF(centre.X + CSng(Math.Cos(radians) * length),
                                 centre.Y + CSng(Math.Sin(radians) * length))

            ' A tapered needle rather than a line: the wide end at the hub is what makes it read as
            ' pointing rather than as a stray rule across the dial.
            Dim halfWidth = Math.Max(3.0F, length * 0.045F)
            Dim perpendicular = radians + CSng(Math.PI / 2.0)
            Dim offsetX = CSng(Math.Cos(perpendicular) * halfWidth)
            Dim offsetY = CSng(Math.Sin(perpendicular) * halfWidth)

            Dim body = New PointF() {
                tip,
                New PointF(centre.X + offsetX, centre.Y + offsetY),
                New PointF(centre.X - offsetX, centre.Y - offsetY)
            }

            Using brush As New SolidBrush(ColourFor(scoreValue))
                g.FillPolygon(brush, body)
            End Using
        End Sub

        Private Sub DrawHub(g As Graphics, centre As PointF, radius As Single)
            Dim hub = Math.Max(6.0F, radius * 0.09F)

            Using brush As New SolidBrush(ColourFor(scoreValue))
                g.FillEllipse(brush, centre.X - hub, centre.Y - hub, hub * 2, hub * 2)
            End Using

            Using inner As New SolidBrush(Color.White)
                Dim core = hub * 0.42F
                g.FillEllipse(inner, centre.X - core, centre.Y - core, core * 2, core * 2)
            End Using
        End Sub

        ''' <summary>
        ''' The number, and the band said in words beneath it.
        '''
        ''' Both are sized from the band reserved for them rather than from the radius, so they
        ''' cannot outgrow the space the arc left behind. The band is split roughly two to one
        ''' between the number and the word.
        ''' </summary>
        Private Sub DrawReadout(g As Graphics, centre As PointF, radius As Single, readoutBand As Single)
            Dim scoreText = If(hasScoreValue,
                               scoreValue.ToString("0.0", Globalization.CultureInfo.InvariantCulture),
                               "--")

            ' 0.62 of the band for the number, converted from pixels to points. The word takes a
            ' little over a third of what is left.
            Dim scorePoints = Math.Max(12.0F, (readoutBand * 0.62F) * 72.0F / g.DpiY)
            Dim bandPoints = Math.Max(7.5F, scorePoints * 0.30F)

            Dim scoreFont = New Font(Font.FontFamily, scorePoints, FontStyle.Bold)
            Dim bandFont = New Font(Font.FontFamily, bandPoints, FontStyle.Bold)

            Try
                Dim scoreSize = g.MeasureString(scoreText, scoreFont)
                Dim bandText = BandName
                Dim bandSize = g.MeasureString(bandText, bandFont)

                ' Anchored to the bottom of the control rather than measured down from the centre.
                ' The arc's own size varies with the aspect ratio; the band does not, so the
                ' readout sits in the same place whatever shape the control is given.
                Dim blockHeight = scoreSize.Height + bandSize.Height - (scoreSize.Height * 0.18F)
                Dim blockTop = Height - blockHeight - 2.0F

                Using brush As New SolidBrush(If(hasScoreValue, ColourFor(scoreValue), TextColour))
                    g.DrawString(scoreText, scoreFont, brush,
                                 centre.X - (scoreSize.Width / 2.0F), blockTop)
                End Using

                Using brush As New SolidBrush(TextColour)
                    g.DrawString(bandText, bandFont, brush,
                                 centre.X - (bandSize.Width / 2.0F),
                                 blockTop + scoreSize.Height - (scoreSize.Height * 0.18F))
                End Using
            Finally
                scoreFont.Dispose()
                bandFont.Dispose()
            End Try
        End Sub

        Private Shared Function DegreesToRadians(degrees As Double) As Single
            Return CSng(degrees * Math.PI / 180.0)
        End Function

    End Class
End Namespace
