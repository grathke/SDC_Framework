Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The page colour picker, as a widget any form can attach.
    '''
    ''' It began inside FW_Base_B, which was the right place while only browse pages offered it.
    ''' A page that inherits Form directly - the dashboards, and any one-off screen - could not have
    ''' it at all, and the only way to give one a colour picker was to write a second one.
    ''' Two pickers would mean two palettes, two defaults and two ideas of which controls a tint
    ''' reaches, so the shape that was already shared moved out here instead.
    '''
    ''' Storage was already shared: DataAccess.Get/SavePageBackgroundColor keeps the colour in
    ''' FW_Pages.Background against the page name. A page with no FW_Pages row can still be tinted
    ''' but cannot store it, and is told so rather than silently losing the choice.
    '''
    ''' Admin only, by the host's choice. UpdateVisibility applies the application-administrator
    ''' test; the colour it sets is restored for every user, because a page's appearance is not a
    ''' permission.
    ''' </summary>
    Public NotInheritable Class PageBackgroundColorPicker

        ''' <summary>
        ''' The page background before anyone chooses one. Public because maintenance pages fall
        ''' back to the same value: one default, not two that can drift apart.
        '''
        ''' Paper rather than white. Pure white as a whole-page background is needlessly stark, and
        ''' a faint grey lets the white grid and inputs read as content sitting on the page rather
        ''' than merging with it.
        ''' </summary>
        Public Shared ReadOnly DefaultPageBackground As Color = Color.FromArgb(243, 245, 246)

        ''' <summary>
        ''' Fifteen pastels, named. Muted deliberately: the page is a backdrop for a grid and a
        ''' form, and a saturated ground makes both harder to read.
        ''' </summary>
        Private Shared ReadOnly BackgroundColorChoices As KeyValuePair(Of String, Color)() = {
            New KeyValuePair(Of String, Color)("Paper", Color.FromArgb(243, 245, 246)),
            New KeyValuePair(Of String, Color)("White", Color.White),
            New KeyValuePair(Of String, Color)("Cool Gray", Color.FromArgb(236, 239, 241)),
            New KeyValuePair(Of String, Color)("Sky", Color.FromArgb(220, 232, 248)),
            New KeyValuePair(Of String, Color)("Mint", Color.FromArgb(230, 244, 236)),
            New KeyValuePair(Of String, Color)("Moss", Color.FromArgb(226, 234, 214)),
            New KeyValuePair(Of String, Color)("Fern", Color.FromArgb(214, 230, 214)),
            New KeyValuePair(Of String, Color)("Sand", Color.FromArgb(240, 234, 220)),
            New KeyValuePair(Of String, Color)("Peach", Color.FromArgb(250, 226, 208)),
            New KeyValuePair(Of String, Color)("Terra", Color.FromArgb(238, 214, 204)),
            New KeyValuePair(Of String, Color)("Rose", Color.FromArgb(247, 228, 232)),
            New KeyValuePair(Of String, Color)("Coral", Color.FromArgb(250, 224, 220)),
            New KeyValuePair(Of String, Color)("Dusk", Color.FromArgb(232, 220, 232)),
            New KeyValuePair(Of String, Color)("Orchid", Color.FromArgb(240, 224, 240)),
            New KeyValuePair(Of String, Color)("Lilac", Color.FromArgb(230, 226, 244))
        }

        ''' <summary>
        ''' The panel's control name, which ApplyColour uses to leave the picker itself untinted.
        ''' </summary>
        Public Const PanelControlName As String = "Panel_PageBackgroundColor"

        Private ReadOnly owner As Form
        Private ReadOnly pageName As String
        Private ReadOnly pickerButton As Button
        Private ReadOnly palettePanel As Panel

        Private panelClickAwayFilter As ClickAwayFilter
        Private currentColour As Color = DefaultPageBackground
        Private colourBeforePicker As Color = DefaultPageBackground

        ''' <param name="hostForm">The form tinted, and the one the controls are added to.</param>
        ''' <param name="storedUnderPageName">
        ''' The FW_Pages.WindowOrPage the colour is kept against - normally the form's class name.
        ''' </param>
        Public Sub New(hostForm As Form, storedUnderPageName As String)
            owner = hostForm
            pageName = storedUnderPageName

            pickerButton = New Button() With {
                .Name = "Button_PageBackgroundColor",
                .Text = "Color",
                .Size = New Size(94, 36),
                .Visible = False,
                .TabStop = False
            }

            ' A palette rather than a single swatch: nine pastels at 5px inside the same 16x16, so
            ' the button says what it offers without growing. The applied colour shows on the page
            ' itself, which is a better indicator than a square this small could ever be.
            AddHandler pickerButton.Paint,
                Sub(paintSender As Object, paintArgs As PaintEventArgs)
                    Const cell As Integer = 5
                    Dim originX As Integer = 8
                    Dim originY As Integer = (pickerButton.Height - (cell * 3)) \ 2

                    ' Vivid rather than pastel: the icon has to say "colour" at 15 pixels across.
                    ' The pastels it offers are far too faint to read at this size.
                    Dim paletteIcon As Color() = {
                        Color.FromArgb(214, 69, 65), Color.FromArgb(232, 140, 48), Color.FromArgb(240, 200, 60),
                        Color.FromArgb(112, 176, 74), Color.FromArgb(48, 150, 152), Color.FromArgb(58, 122, 205),
                        Color.FromArgb(96, 82, 170), Color.FromArgb(170, 78, 150), Color.FromArgb(120, 120, 128)
                    }

                    For index = 0 To 8
                        Dim swatchColour = paletteIcon(index)
                        Using brush As New SolidBrush(swatchColour)
                            paintArgs.Graphics.FillRectangle(brush,
                                                             originX + (index Mod 3) * cell,
                                                             originY + (index \ 3) * cell,
                                                             cell, cell)
                        End Using
                    Next

                    paintArgs.Graphics.DrawRectangle(Pens.Gray, originX, originY, cell * 3, cell * 3)
                End Sub

            AddHandler pickerButton.Click,
                Sub()
                    If palettePanel.Visible Then
                        Cancel()
                    Else
                        OpenPanel()
                    End If
                End Sub

            palettePanel = New Panel() With {
                .Name = PanelControlName,
                .Size = New Size(5 * 44 + 12, 42 + 3 * 34 + 6),
                .BorderStyle = BorderStyle.FixedSingle,
                .BackColor = Color.White,
                .Visible = False
            }

            ' OK and Cancel, in the same shape and sizes as the Tab Order panel on _U pages, so the
            ' two collapsible panels behave alike. A swatch previews; OK keeps it; Cancel and any
            ' click outside put back the colour the page had when the panel was opened.
            Dim colorOkButton As New Button() With {
                .Text = "OK",
                .Size = New Size(60, 26),
                .Location = New Point(palettePanel.Width - 136, 8),
                .TabStop = False
            }
            Dim colorCancelButton As New Button() With {
                .Text = "Cancel",
                .Size = New Size(60, 26),
                .Location = New Point(palettePanel.Width - 70, 8),
                .TabStop = False
            }

            AddHandler colorOkButton.Click, Sub() Commit()
            AddHandler colorCancelButton.Click, Sub() Cancel()

            palettePanel.Controls.Add(colorOkButton)
            palettePanel.Controls.Add(colorCancelButton)

            For index = 0 To BackgroundColorChoices.Length - 1
                Dim choice = BackgroundColorChoices(index)
                Dim swatchButton As New Button() With {
                    .Size = New Size(38, 28),
                    .Location = New Point(6 + (index Mod 5) * 44, 42 + (index \ 5) * 34),
                    .BackColor = choice.Value,
                    .FlatStyle = FlatStyle.Flat,
                    .TabStop = False
                }
                swatchButton.FlatAppearance.BorderColor = Color.FromArgb(150, 150, 150)
                swatchButton.FlatAppearance.BorderSize = 1

                Dim swatchTip As New ToolTip()
                swatchTip.SetToolTip(swatchButton, choice.Key)

                Dim chosen = choice.Value
                AddHandler swatchButton.Click,
                    Sub()
                        ' Preview only. Nothing is stored until OK.
                        currentColour = chosen
                        ApplyColour(owner, chosen)
                        pickerButton.Invalidate()
                        palettePanel.BringToFront()
                    End Sub

                palettePanel.Controls.Add(swatchButton)
            Next
        End Sub

        ''' <summary>The button, so the host can place it in its own layout.</summary>
        Public ReadOnly Property Button As Button
            Get
                Return pickerButton
            End Get
        End Property

        ''' <summary>Adds the button and its palette panel to the host form.</summary>
        Public Sub Attach()
            owner.Controls.Add(pickerButton)
            owner.Controls.Add(palettePanel)
        End Sub

        ''' <summary>
        ''' Applies the colour stored for this page, if any. Runs for every user, not just an
        ''' administrator: the picker is admin-only, the colour it sets is not.
        ''' </summary>
        Public Sub ApplyStored()
            Try
                Dim stored = DataAccess.GetPageBackgroundColor(pageName)

                ' No stored colour means the default, which is applied rather than assumed: the form
                ' is built white, so leaving it alone would make "no choice" look different from
                ' choosing Paper.
                currentColour = If(stored.HasValue, Color.FromArgb(stored.Value), DefaultPageBackground)
                ApplyColour(owner, currentColour)
                pickerButton.Invalidate()
            Catch
                ' A page colour is decoration. It must never be the reason a page fails to open.
            End Try
        End Sub

        ''' <summary>
        ''' Repaints the page in the colour already loaded, without reading it again.
        ''' </summary>
        ''' <remarks>
        ''' For a page whose controls are created after ApplyStored has run. Those controls were not
        ''' there to be treated when the colour was first applied, so they simply inherited the form
        ''' tint - which is how a button ends up the colour of the page instead of looking pressable.
        ''' </remarks>
        Public Sub Reapply()
            ApplyColour(owner, currentColour)
            pickerButton.Invalidate()
        End Sub

        ''' <summary>Shows the button only to an application administrator.</summary>
        Public Sub UpdateVisibility()
            pickerButton.Visible = SessionState.IsApplicationAdmin
            If Not pickerButton.Visible Then
                palettePanel.Visible = False
            End If
        End Sub

        ''' <summary>Keeps the panel under its button, and inside the form.</summary>
        Public Sub PositionPanel()
            palettePanel.Location = New Point(
                Math.Max(0, Math.Min(pickerButton.Left, owner.ClientSize.Width - palettePanel.Width)),
                pickerButton.Bottom + 4)
        End Sub

        Private Sub OpenPanel()
            colourBeforePicker = currentColour
            PositionPanel()
            palettePanel.Visible = True
            palettePanel.BringToFront()

            If panelClickAwayFilter Is Nothing Then
                panelClickAwayFilter = New ClickAwayFilter(palettePanel, AddressOf Cancel)
            End If
            Application.AddMessageFilter(panelClickAwayFilter)
        End Sub

        Private Sub ClosePanel()
            palettePanel.Visible = False
            If panelClickAwayFilter IsNot Nothing Then
                Application.RemoveMessageFilter(panelClickAwayFilter)
            End If
        End Sub

        ''' <summary>Keeps the previewed colour and stores it against the page.</summary>
        Private Sub Commit()
            ClosePanel()

            Dim savedBy = If(SessionState.IsActive AndAlso SessionState.Current.HasValue,
                             SessionState.Current.Value.UserID, 0)

            If Not DataAccess.SavePageBackgroundColor(pageName, currentColour.ToArgb(), savedBy) Then
                MessageBox.Show(owner,
                                "THE COLOUR WAS APPLIED BUT NOT SAVED." & Environment.NewLine & Environment.NewLine &
                                "THIS PAGE HAS NO FW_Pages ROW TO STORE IT AGAINST." & Environment.NewLine &
                                Environment.NewLine &
                                "PAGE: " & pageName,
                                "PAGE COLOUR NOT SAVED",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
            End If
        End Sub

        ''' <summary>Puts back the colour the page had when the panel was opened.</summary>
        Private Sub Cancel()
            ClosePanel()
            currentColour = colourBeforePicker
            ApplyColour(owner, currentColour)
            pickerButton.Invalidate()
        End Sub

        ''' <summary>
        ''' Tints the page and its plain panels, leaving the grid, inputs and buttons alone - those
        ''' carry their own colour, and repainting them would swamp the effect being judged.
        ''' </summary>
        Public Shared Sub ApplyColour(container As Control, colour As Color)
            container.BackColor = colour

            For Each child As Control In container.Controls
                ' The picker itself keeps a fixed ground. Tinting it would mean judging every swatch
                ' against whatever is currently applied, so the same colour would look different
                ' depending on what it is replacing.
                If String.Equals(child.Name, PanelControlName, StringComparison.Ordinal) Then
                    Continue For
                End If

                ' A button keeps its own look. Left to inherit, it takes the page tint and reads as
                ' disabled or selected - the visual style is what makes it look pressable.
                Dim button = TryCast(child, Button)
                If button IsNot Nothing Then
                    If button.FlatStyle = FlatStyle.Standard OrElse button.FlatStyle = FlatStyle.System Then
                        button.UseVisualStyleBackColor = True
                    End If
                    Continue For
                End If

                If TypeOf child Is DataGridView OrElse TypeOf child Is TextBox OrElse
                   TypeOf child Is ComboBox Then
                    Continue For
                End If

                If TypeOf child Is Panel OrElse TypeOf child Is SplitContainer OrElse
                   TypeOf child Is SplitterPanel Then
                    ApplyColour(child, colour)
                ElseIf TypeOf child Is Label Then
                    child.BackColor = Color.Transparent
                End If
            Next
        End Sub

        ''' <summary>
        ''' Watches for a click anywhere outside the open colour panel and closes it, because a
        ''' popup that stays open until you find the right button is a popup in the way. Focus
        ''' events are not enough on their own - a click can land on a control that never takes
        ''' focus, and the panel would sit there.
        ''' </summary>
        Private NotInheritable Class ClickAwayFilter
            Implements IMessageFilter

            Private Const WM_LBUTTONDOWN As Integer = &H201
            Private Const WM_RBUTTONDOWN As Integer = &H204
            Private Const WM_NCLBUTTONDOWN As Integer = &HA1

            Private ReadOnly panel As Control
            Private ReadOnly onClickAway As Action

            Public Sub New(watched As Control, dismiss As Action)
                panel = watched
                onClickAway = dismiss
            End Sub

            Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
                If m.Msg <> WM_LBUTTONDOWN AndAlso m.Msg <> WM_RBUTTONDOWN AndAlso m.Msg <> WM_NCLBUTTONDOWN Then
                    Return False
                End If

                If panel Is Nothing OrElse Not panel.Visible Then Return False

                Dim bounds = panel.RectangleToScreen(panel.ClientRectangle)
                If Not bounds.Contains(Cursor.Position) Then
                    onClickAway()
                End If

                ' Never swallow the click. Closing the panel must not also eat the button press
                ' that closed it, or the user has to click twice to do anything.
                Return False
            End Function
        End Class

    End Class

End Namespace
