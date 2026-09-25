Option Strict On
Option Explicit On

Imports System
Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Two panels, one above the other, with a bar between them that the user drags. The browse
    ''' page's QBE strip over its grid.
    '''
    ''' **Why not SplitContainer.** WinForms' SplitContainer paints its own splitter from inside
    ''' its layout - `ResizeSplitContainer` calls `RepaintSplitterRect`, which takes a Graphics from
    ''' the window and fills the bar. In a real Thinfinity session that fill throws "a generic error
    ''' occurred in GDI+" on every browse page open (FW_ErrorLog #7). It was caught and logged by a
    ''' SafeSplitContainer subclass, but the throw itself sits in private framework code and nothing
    ''' outside can stop it. Two theories were tested against a live session and refuted: that the
    ''' window could not take paint yet (2026-09-24), and that the rectangle ran off the control
    ''' because the base builds it from Location (2026-09-25 - true, but at (0,0) it still threw).
    '''
    ''' **This control paints nothing itself.** The bar is the control's own background, showing
    ''' between the two panels, and a background is drawn by the ordinary paint message - which
    ''' works in a session like everywhere else. Nothing takes a Graphics during layout.
    '''
    ''' **The distance clamps itself.** A value outside what the height allows is pulled back to
    ''' the nearest legal one, and a control too short for both minimums keeps its distance until
    ''' it is tall enough. SplitterLayout does the same for a SplitContainer; this owns it for
    ''' itself because nothing else can place its bar.
    '''
    ''' **SplitterMoved is raised when a drag ends**, not while it is under way and not when code
    ''' sets the distance - code that sets it already knows the value. The panels follow the
    ''' pointer during the drag.
    ''' </summary>
    Public Class QbeSplitPanel
        Inherits Panel

        Private splitterDistanceValue As Integer = 150
        Private panel1CollapsedValue As Boolean
        Private dragging As Boolean
        Private dragOffset As Integer

        Public ReadOnly Property Panel1 As Panel
        Public ReadOnly Property Panel2 As Panel

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SplitterWidth As Integer = 6
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Panel1MinSize As Integer = 25
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Panel2MinSize As Integer = 25

        Public Event SplitterMoved As EventHandler

        Public Sub New()
            ' A cursor of their own, because Cursor is ambient: without one each panel - and every
            ' control inside it - inherits whatever this control shows, and the splitter cursor set
            ' while crossing the bar spread over the whole search panel and grid (2026-09-25).
            Panel1 = New Panel() With {.Cursor = Cursors.Default}
            Panel2 = New Panel() With {.Cursor = Cursors.Default}
            Controls.Add(Panel1)
            Controls.Add(Panel2)
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SplitterDistance As Integer
            Get
                Return splitterDistanceValue
            End Get
            Set(value As Integer)
                TrySetDistance(value)
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Panel1Collapsed As Boolean
            Get
                Return panel1CollapsedValue
            End Get
            Set(value As Boolean)
                If panel1CollapsedValue = value Then Return
                panel1CollapsedValue = value
                Panel1.Visible = Not value
                PerformLayout()
            End Set
        End Property

        ''' <summary>
        ''' Puts the bar as close to <paramref name="desired"/> as the height allows, and returns
        ''' False without moving it when no position is legal.
        ''' </summary>
        Public Function TrySetDistance(desired As Integer) As Boolean
            Dim largest = MaxDistance()
            If largest < 0 Then Return False

            Dim wanted = Math.Min(Math.Max(Panel1MinSize, desired), largest)
            If wanted <> splitterDistanceValue Then
                splitterDistanceValue = wanted
                PerformLayout()
            End If
            Return True
        End Function

        ''' <summary>The largest legal distance, or -1 when the control is too short for any.</summary>
        Private Function MaxDistance() As Integer
            Dim height = ClientSize.Height
            If height < Panel1MinSize + Panel2MinSize + SplitterWidth Then Return -1
            Return height - Panel2MinSize - SplitterWidth
        End Function

        Private ReadOnly Property SplitterBar As Rectangle
            Get
                Return New Rectangle(0, splitterDistanceValue, ClientSize.Width, SplitterWidth)
            End Get
        End Property

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            If Panel1 Is Nothing OrElse Panel2 Is Nothing Then Return

            Dim area = ClientRectangle
            If panel1CollapsedValue Then
                Panel2.SetBounds(0, 0, area.Width, area.Height)
                Return
            End If

            ' Taller than the space allows only while the control is too short for both minimums;
            ' the distance is kept rather than squeezed, as SplitContainer does.
            Dim largest = MaxDistance()
            If largest >= 0 AndAlso splitterDistanceValue > largest Then splitterDistanceValue = largest

            Dim panel2Top = splitterDistanceValue + SplitterWidth
            Panel1.SetBounds(0, 0, area.Width, splitterDistanceValue)
            Panel2.SetBounds(0, panel2Top, area.Width, Math.Max(0, area.Height - panel2Top))
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            If e.Button <> MouseButtons.Left OrElse panel1CollapsedValue Then Return
            If Not SplitterBar.Contains(e.Location) Then Return

            dragging = True
            dragOffset = e.Y - splitterDistanceValue
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)

            If dragging Then
                TrySetDistance(e.Y - dragOffset)
                Return
            End If

            ' Only the cursor, and only a hint: the drag itself starts on a click, which always
            ' arrives in a session where movement may not.
            Cursor = If(Not panel1CollapsedValue AndAlso SplitterBar.Contains(e.Location), Cursors.HSplit, Cursors.Default)
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            If Not dragging Then Return

            dragging = False
            TrySetDistance(e.Y - dragOffset)
            RaiseEvent SplitterMoved(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnMouseCaptureChanged(e As EventArgs)
            MyBase.OnMouseCaptureChanged(e)

            ' Capture lost mid-drag - another window, a dialog - ends the drag where it is.
            If dragging AndAlso Not Capture Then
                dragging = False
                RaiseEvent SplitterMoved(Me, EventArgs.Empty)
            End If
        End Sub

    End Class

End Namespace
