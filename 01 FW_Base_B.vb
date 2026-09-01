Option Strict On
Option Explicit On

Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class FW_Base_B
        Inherits Form

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly accessTableName As String
        Private ReadOnly imagePanel As Panel
        Private ReadOnly imageBox As PictureBox
        Private ReadOnly imageLabel As Label
        Private ReadOnly titleLabel As Label
        Private ReadOnly recordCountLabel As Label
        Private ReadOnly layoutLabel As Label
        Private ReadOnly layoutComboBox As ComboBox
        Private ReadOnly toggleColumnsPanelButton As Button
        Private ReadOnly resetLayoutButton As Button
        Private ReadOnly deleteLayoutButton As Button
        Private ReadOnly saveMyLayoutButton As Button
        Private ReadOnly sqlLabel As Label
        Private ReadOnly sqlTextBox As TextBox
        Private ReadOnly applySqlButton As Button
        Private ReadOnly registrationIdLabel As Label
        Private ReadOnly registrationComboBox As ComboBox
        Private ReadOnly browseGrid As DataGridView
        Private ReadOnly createButton As Button
        Private ReadOnly restoreButton As Button
        Private ReadOnly readButton As Button
        Private ReadOnly updateButton As Button
        Private ReadOnly deleteButton As Button
        Private ReadOnly toggleQbeButton As Button
        Private ReadOnly findButton As Button
        Private ReadOnly clearFiltersButton As Button
        Private ReadOnly saveQbeButton As Button
        Private ReadOnly retrieveQbeButton As Button
        Private ReadOnly showDeletedButton As Button
        Private ReadOnly showNormalButton As Button
        Private ReadOnly closeButton As Button
        Private ReadOnly qbeSplitContainer As SplitContainer
        Private ReadOnly qbePanel As Panel
        Private ReadOnly layoutToolbarPanel As Panel
        Private ReadOnly columnsManagerPanel As Panel
        Private ReadOnly columnsManagerLabel As Label
        Private ReadOnly columnsManagerHideButton As Button
        Private ReadOnly columnsManagerOkButton As Button
        Private columnsManagerBaseline As List(Of Tuple(Of String, Boolean))
        Private qbeRebuildPending As Boolean
        Private ReadOnly columnsManagerList As CheckedListBox
        Private ReadOnly columnsMoveUpButton As Button
        Private ReadOnly columnsMoveDownButton As Button
        Private ReadOnly activeFilterLabel As Label
        Private ReadOnly retrievalStatusLabel As Label
        Private ReadOnly retrievalStatusFlashTimer As Timer
        Private retrievalStatusFlashRed As Boolean
        Private ReadOnly qbeGrid As DataGridView
        Private ReadOnly qbeFieldDefinitions As List(Of QbeFieldDefinition)
        Private currentFilters As Dictionary(Of String, String)
        Private qbeExpanded As Boolean = False
        Private qbeSplitterDistance As Integer = 150
        Private showDeletedRecordsOnly As Boolean = False
        Private ReadOnly qbeFieldColumnWidthIncrease As Integer
        Private ReadOnly qbeValueColumnWidthIncrease As Integer
        Private missingSqlWarningShown As Boolean = False
        Private lastAppliedSqlSignature As String = String.Empty
        Private lastVisibleColumnsSignature As String = String.Empty
        Private sqlLoadedFromRoleTable As Boolean = False
        Private currentDbTableName As String = String.Empty
        Private pendingInitialLayoutApply As Boolean = True
        Private baselineLayoutSnapshot As String = String.Empty
        Private hasBaselineLayoutSnapshot As Boolean = False
        Private userChangedLayout As Boolean = False
        Private suppressLayoutSelectionChanged As Boolean = False
        Private suppressRegistrationSelectionChanged As Boolean = False
        Private lastSelectedRegistrationId As Integer = 0
        Private lastRefreshExceededRowLimit As Boolean = False
        Private suppressColumnsManagerSync As Boolean = False
        Private allowColumnsManagerCheckToggle As Boolean = False
        Private missingMaintenancePkInResult As Boolean = False
        Private missingMaintenancePkWarningShown As Boolean = False
        Private pendingInitialMissingPkWarning As Boolean = False
        Private viewOnlyMyWarningShown As Boolean = False

        Private Shared ReadOnly QbeCollapsedText As String = "QBE " & ChrW(&H25BC)
        Private Shared ReadOnly QbeExpandedText As String = "QBE " & ChrW(&H25B2)
        Private Shared ReadOnly ShowDeletedText As String = "Show Deleted"
        Private Shared ReadOnly ShowNormalText As String = "Show Normal"
        Private Shared ReadOnly ColumnsCollapsedText As String = "Columns " & ChrW(&H25BC)
        Private Shared ReadOnly ColumnsExpandedText As String = "Columns " & ChrW(&H25B2)
        Private Shared ReadOnly ColumnsUsageHintKey As String = "FW_Base_B.ColumnsUsage"
        Private Const EmptyQbeResultLimit As Integer = 10

        Private Function GetEmptyQbeRowLimit() As Integer
            Dim session = SessionState.Current
            If session.HasValue Then
                Return session.Value.MaxRecordsNoQBE
            End If
            Return EmptyQbeResultLimit
        End Function

        Private Class LayoutSelectionItem
            Public Property LayoutType As String
            Public Property LayoutName As String
            Public Property OwnerUserID As Integer
            Public Property IsShared As Boolean
            Public Property DisplayName As String

            Public Overrides Function ToString() As String
                Return DisplayName
            End Function
        End Class

        Private Class LayoutSaveOptions
            Public Property LayoutName As String
            Public Property ShareWithRegistration As Boolean
            Public Property SaveAsDefault As Boolean
        End Class

        Private Structure GridViewState
            Public HasSelection As Boolean
            Public SelectedRecordId As Integer
            Public SelectedRowOffsetFromTop As Integer
            Public FallbackFirstDisplayedIndex As Integer
        End Structure

        Protected Sub New()
        End Sub

        Protected Function GetSessionRegistrationId() As Integer
            Dim activeSession = SessionState.Current
            If activeSession.HasValue AndAlso activeSession.Value.RegistrationID > 0 Then
                Return activeSession.Value.RegistrationID
            End If

            Return 0
        End Function

        Protected Function GetInitialRegistrationId() As Integer
            Return GetSessionRegistrationId()
        End Function

        Protected Function GetActiveRegistrationId() As Integer
            Dim registrationId As Integer = 0
            If TryGetActiveRegistrationId(registrationId) Then
                Return registrationId
            End If

            Return 0
        End Function

        ''' <summary>
        ''' TEMPORARY - a picker for trying a faint page background on a real browse page, before
        ''' deciding whether the colour becomes a per-registration setting stored beside the CRUD
        ''' button captions. Applies to the open page only and stores nothing: reopen the page and
        ''' it is white again.
        '''
        ''' Admin only, like the Tab Order manager on _U pages, and built on the same shape - a
        ''' toggle button in the action row and a panel that expands over the page and collapses on
        ''' selection. The button sits in the row's layout rather than at a fixed point, so it
        ''' cannot overlap or be overlapped whatever the window width.
        ''' </summary>
        ''' <summary>
        ''' Page backgrounds, five to a row, grouped by family: neutrals, blue, greens, warms,
        ''' pinks, purples.
        '''
        ''' Chosen on their own merits rather than around the required-field colours, which is the
        ''' right way round: a signal should be more saturated than any background it lands on, so
        ''' the signals are fitted to the palette afterwards, not the palette to the signals.
        '''
        ''' Every colour is measured, not judged by eye:
        '''
        '''   - at least 15:1 contrast with black text, so no page is harder to read than white.
        '''   - at least 16 from every other colour here, so no two choices are a distinction the
        '''     eye cannot actually make. Warm Gray and Almond were dropped for failing this.
        '''
        ''' Sky was out of this palette until the App Admin required blue was deepened on
        ''' 2026-09-01. At the old pale value the two were 6 apart - the same colour - and a signal
        ''' cannot share its colour with a background. It is 108 clear of the new one.
        '''
        ''' The grid's own blues - the header band and the selected row - are deliberately not
        ''' treated as constraints here. They sit inside the grid, on white, and are read in that
        ''' context rather than against the page.
        '''
        ''' Paper is the default rather than white. Pure white as a whole-page background is
        ''' needlessly stark, and a faint grey lets the white grid and inputs read as content
        ''' sitting on the page rather than merging with it.
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

        Private backgroundColorButton As Button
        Private backgroundColorPanel As Panel
        ''' <summary>
        ''' The page background before anyone chooses one. Public and shared because maintenance
        ''' pages fall back to the same value: one default, not two that can drift apart.
        ''' </summary>
        Public Shared ReadOnly DefaultPageBackground As Color = Color.FromArgb(243, 245, 246)

        Private pageBackgroundColor As Color = DefaultPageBackground

        Private Sub BuildBackgroundColorPicker()
            backgroundColorButton = New Button() With {
                .Name = "Button_PageBackgroundColor",
                .Text = "Color",
                .Size = New Size(94, 36),
                .Location = New Point(430, 112),
                .Visible = False,
                .TabStop = False
            }

            ' A palette rather than a single swatch: nine pastels at 5px inside the same 16x16, so
            ' the button says what it offers without growing. The applied colour shows on the page
            ' itself, which is a better indicator than a square this small could ever be.
            AddHandler backgroundColorButton.Paint,
                Sub(paintSender As Object, paintArgs As PaintEventArgs)
                    Const cell As Integer = 5
                    Dim originX As Integer = 8
                    Dim originY As Integer = (backgroundColorButton.Height - (cell * 3)) \ 2

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

            AddHandler backgroundColorButton.Click,
                Sub()
                    If backgroundColorPanel.Visible Then
                        CancelPageBackgroundColor()
                    Else
                        OpenPageBackgroundColorPanel()
                    End If
                End Sub

            backgroundColorPanel = New Panel() With {
                .Name = "Panel_PageBackgroundColor",
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
                .Location = New Point(backgroundColorPanel.Width - 136, 8),
                .TabStop = False
            }
            Dim colorCancelButton As New Button() With {
                .Text = "Cancel",
                .Size = New Size(60, 26),
                .Location = New Point(backgroundColorPanel.Width - 70, 8),
                .TabStop = False
            }

            AddHandler colorOkButton.Click, Sub() CommitPageBackgroundColor()
            AddHandler colorCancelButton.Click, Sub() CancelPageBackgroundColor()

            backgroundColorPanel.Controls.Add(colorOkButton)
            backgroundColorPanel.Controls.Add(colorCancelButton)

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
                        pageBackgroundColor = chosen
                        ApplyPageBackgroundColor(Me, chosen)
                        backgroundColorButton.Invalidate()
                        backgroundColorPanel.BringToFront()
                    End Sub

                backgroundColorPanel.Controls.Add(swatchButton)
            Next

            Me.Controls.Add(backgroundColorButton)
            Me.Controls.Add(backgroundColorPanel)
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

        Private panelClickAwayFilter As ClickAwayFilter
        Private backgroundColorBeforePicker As Color = Color.White

        Private Sub OpenPageBackgroundColorPanel()
            backgroundColorBeforePicker = pageBackgroundColor
            PositionBackgroundColorPanel()
            backgroundColorPanel.Visible = True
            backgroundColorPanel.BringToFront()

            If panelClickAwayFilter Is Nothing Then
                panelClickAwayFilter = New ClickAwayFilter(backgroundColorPanel, AddressOf CancelPageBackgroundColor)
            End If
            Application.AddMessageFilter(panelClickAwayFilter)
        End Sub

        Private Sub ClosePageBackgroundColorPanel()
            backgroundColorPanel.Visible = False
            If panelClickAwayFilter IsNot Nothing Then
                Application.RemoveMessageFilter(panelClickAwayFilter)
            End If
        End Sub

        ''' <summary>Keeps the previewed colour and stores it against the page.</summary>
        Private Sub CommitPageBackgroundColor()
            ClosePageBackgroundColorPanel()

            Dim pageName = Me.GetType().Name
            Dim savedBy = If(SessionState.IsActive AndAlso SessionState.Current.HasValue,
                             SessionState.Current.Value.UserID, 0)

            If Not DataAccess.SavePageBackgroundColor(pageName, pageBackgroundColor.ToArgb(), savedBy) Then
                MessageBox.Show(Me,
                                "THE COLOUR WAS APPLIED BUT NOT SAVED." & Environment.NewLine & Environment.NewLine &
                                "THIS PAGE HAS NO FW_RoleTables ROW TO STORE IT AGAINST." & Environment.NewLine &
                                Environment.NewLine &
                                "PAGE: " & pageName,
                                "PAGE COLOUR NOT SAVED",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
            End If
        End Sub

        ''' <summary>Puts back the colour the page had when the panel was opened.</summary>
        Private Sub CancelPageBackgroundColor()
            ClosePageBackgroundColorPanel()
            pageBackgroundColor = backgroundColorBeforePicker
            ApplyPageBackgroundColor(Me, pageBackgroundColor)
            If backgroundColorButton IsNot Nothing Then backgroundColorButton.Invalidate()
        End Sub

        ''' <summary>
        ''' Applies the colour stored for this page, if any. Runs for every user, not just an
        ''' administrator: the picker is admin-only, the colour it sets is not.
        ''' </summary>
        Private Sub RestorePageBackgroundColor()
            Try
                Dim stored = DataAccess.GetPageBackgroundColor(Me.GetType().Name)

                ' No stored colour means the default, which is applied rather than assumed: the form
                ' is built white, so leaving it alone would make "no choice" look different from
                ' choosing Paper.
                pageBackgroundColor = If(stored.HasValue, Color.FromArgb(stored.Value), DefaultPageBackground)
                ApplyPageBackgroundColor(Me, pageBackgroundColor)
                If backgroundColorButton IsNot Nothing Then backgroundColorButton.Invalidate()
            Catch
                ' A page colour is decoration. It must never be the reason a listing fails to open.
            End Try
        End Sub

        Private Sub PositionBackgroundColorPanel()
            If backgroundColorButton Is Nothing OrElse backgroundColorPanel Is Nothing Then Return
            backgroundColorPanel.Location = New Point(
                Math.Max(0, Math.Min(backgroundColorButton.Left, Me.ClientSize.Width - backgroundColorPanel.Width)),
                backgroundColorButton.Bottom + 4)
        End Sub

        ''' <summary>
        ''' Tints the page and its plain panels, leaving the grid, inputs and buttons alone - those
        ''' carry their own colour, and repainting them would swamp the effect being judged.
        ''' </summary>
        Private Shared Sub ApplyPageBackgroundColor(container As Control, colour As Color)
            container.BackColor = colour

            For Each child As Control In container.Controls
                ' The picker itself keeps a fixed ground. Tinting it would mean judging every swatch
                ' against whatever is currently applied, so the same colour would look different
                ' depending on what it is replacing.
                If String.Equals(child.Name, "Panel_PageBackgroundColor", StringComparison.Ordinal) Then
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
                    ApplyPageBackgroundColor(child, colour)
                ElseIf TypeOf child Is Label Then
                    child.BackColor = Color.Transparent
                End If
            Next
        End Sub

        Protected Function IsAppAdminSession() As Boolean
            Dim activeSession = SessionState.Current
            Return activeSession.HasValue AndAlso activeSession.Value.IsApplicationAdminRole
        End Function

        Protected Function IsCompanyAdminSession() As Boolean
            Dim activeSession = SessionState.Current
            Return activeSession.HasValue AndAlso activeSession.Value.IsCompanyAdminRole
        End Function

        Protected Function IsRegistrationAwareSession() As Boolean
            Return IsAppAdminSession() OrElse IsCompanyAdminSession()
        End Function

        Protected Function GetSessionBusinessRuleType() As String
            Dim activeSession = SessionState.Current
            If activeSession.HasValue Then
                Return NormalizeBusinessRuleType(activeSession.Value.BusinessRuleType)
            End If

            Return BR_Legacy
        End Function

        Public Sub New(user As UserContext,
                       Optional profile As AccessProfile = Nothing,
                       Optional tableName As String = Nothing,
                       Optional buildDefaultBrowseShell As Boolean = True)
            currentUser = user
            accessProfile = profile
            accessTableName = If(tableName, String.Empty).Trim()

            If Not buildDefaultBrowseShell Then
                Return
            End If

            Me.Text = "Browse Listing"
            Me.StartPosition = FormStartPosition.CenterScreen
            Me.MinimumSize = New Size(920, 620)
            Me.ClientSize = New Size(980, 680)
            Me.BackColor = Color.White

            imagePanel = New Panel() With {
                .Location = New Point(10, 10),
                .Size = New Size(120, 140),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
                .BackColor = Color.FromArgb(246, 246, 246),
                .BorderStyle = BorderStyle.None
            }

            imageLabel = New Label() With {
                .Text = String.Empty,
                .AutoSize = False,
                .Location = New Point(10, 10),
                .Size = New Size(198, 24),
                .TextAlign = ContentAlignment.MiddleCenter,
                .ForeColor = Color.DimGray,
                .Visible = False
            }

            imageBox = New PictureBox() With {
                .Location = New Point(10, 40),
                .Size = New Size(198, 540),
                .SizeMode = PictureBoxSizeMode.Zoom,
                .BackColor = Color.Transparent
            }
            imageBox.Image = SystemIcons.Application.ToBitmap()
            imagePanel.Controls.Add(imageLabel)
            imagePanel.Controls.Add(imageBox)

            titleLabel = New Label() With {
                .Text = "Browse Listing",
                .Font = New Font("Segoe UI", 14.0F, FontStyle.Bold),
                .AutoSize = True,
                .Location = New Point(20, 15)
            }

            recordCountLabel = New Label() With {
                .Text = "Record Count: 0",
                .AutoSize = True,
                .ForeColor = Color.DimGray
            }

            layoutLabel = New Label() With {
                .Text = "Grid Layout:",
                .AutoSize = True,
                .ForeColor = Color.DimGray,
                .Location = New Point(540, 50)
            }

            layoutComboBox = New ComboBox() With {
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Location = New Point(590, 44),
                .Size = New Size(240, 28)
            }

            toggleColumnsPanelButton = New Button() With {
                .Text = ColumnsCollapsedText,
                .Location = New Point(14, 42),
                .Size = New Size(102, 28)
            }

            saveMyLayoutButton = New Button() With {
                .Text = "Save Layout",
                .Location = New Point(864, 42),
                .Size = New Size(102, 28)
            }

            resetLayoutButton = New Button() With {
                .Text = "Reset",
                .Location = New Point(756, 42),
                .Size = New Size(78, 28)
            }

            deleteLayoutButton = New Button() With {
                .Text = "Delete",
                .Location = New Point(674, 42),
                .Size = New Size(78, 28)
            }

            sqlLabel = New Label() With {
                .Text = "SQL:",
                .AutoSize = True,
                .Location = New Point(20, 84),
                .ForeColor = Color.DimGray
            }

            sqlTextBox = New TextBox() With {
                .Location = New Point(58, 80),
                .Size = New Size(640, 26),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Text = String.Empty
            }

            registrationIdLabel = New Label() With {
                .Text = "Registration:",
                .AutoSize = True,
                .Location = New Point(708, 84),
                .ForeColor = Color.DimGray
            }

            registrationComboBox = New ComboBox() With {
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .Location = New Point(802, 80),
                .Size = New Size(275, 26),
                .DropDownWidth = 280
            }

            applySqlButton = New Button() With {
                .Text = "Apply SQL",
                .Size = New Size(90, 30),
                .Location = New Point(960, 78)
            }

            createButton = New Button() With {.Text = "New", .Size = New Size(90, 36), .Location = New Point(20, 112)}
            restoreButton = New Button() With {.Text = "Restore", .Size = New Size(64, 36), .Location = New Point(116, 112), .Visible = False}
            readButton = New Button() With {.Text = "Read", .Size = New Size(90, 36), .Location = New Point(120, 112)}
            updateButton = New Button() With {.Text = "Modify", .Size = New Size(90, 36), .Location = New Point(220, 112)}
            deleteButton = New Button() With {.Text = "Delete", .Size = New Size(90, 36), .Location = New Point(320, 112)}
            closeButton = New Button() With {.Text = "Close", .Size = New Size(90, 36), .Location = New Point(520, 112)}
            toggleQbeButton = New Button() With {.Text = QbeCollapsedText, .Size = New Size(110, 36), .Location = New Point(620, 112)}

            BuildBackgroundColorPicker()

            qbeSplitContainer = New SplitContainer() With {
                .Location = New Point(20, 162),
                .Size = New Size(940, 428),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom,
                .Orientation = Orientation.Horizontal,
                .BorderStyle = BorderStyle.FixedSingle,
                .SplitterWidth = 6,
                .Panel1MinSize = 120,
                .Panel2MinSize = 120,
                .SplitterDistance = 150
            }

            qbePanel = New Panel() With {
                .Location = New Point(0, 0),
                .Size = New Size(940, 150),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
                .BackColor = Color.FromArgb(248, 248, 248),
                .BorderStyle = BorderStyle.FixedSingle
            }

            layoutToolbarPanel = New Panel() With {
                .Dock = DockStyle.Top,
                .Height = 38,
                .BackColor = Color.FromArgb(245, 245, 245),
                .Padding = New Padding(8, 6, 8, 6)
            }

            columnsManagerPanel = New Panel() With {
                .Dock = DockStyle.None,
                .Width = 218,
                .BackColor = Color.FromArgb(248, 248, 248),
                .BorderStyle = BorderStyle.FixedSingle,
                .Visible = False
            }

            columnsManagerLabel = New Label() With {
                .Text = "Columns",
                .AutoSize = True,
                .Location = New Point(8, 10),
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
                .ForeColor = Color.DimGray,
                .Visible = False
            }

            columnsMoveUpButton = New Button() With {
                .Text = ChrW(&H25B2),
                .Size = New Size(32, 28),
                .Location = New Point(8, 6)
            }

            columnsMoveDownButton = New Button() With {
                .Text = ChrW(&H25BC),
                .Size = New Size(32, 28),
                .Location = New Point(46, 6)
            }

            columnsManagerOkButton = New Button() With {
                .Text = "OK",
                .Size = New Size(58, 28),
                .Location = New Point(84, 6)
            }

            columnsManagerHideButton = New Button() With {
                .Text = "Cancel",
                .Size = New Size(58, 28),
                .Location = New Point(148, 6)
            }

            columnsManagerList = New CheckedListBox() With {
                .CheckOnClick = True,
                .Location = New Point(8, 40),
                .Size = New Size(198, 340),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom,
                .BorderStyle = BorderStyle.FixedSingle
            }

            findButton = New Button() With {.Text = "Find", .Size = New Size(110, 36)}
            clearFiltersButton = New Button() With {.Text = "Clear", .Size = New Size(110, 36)}
            saveQbeButton = New Button() With {.Text = "Save", .Size = New Size(110, 36)}
            retrieveQbeButton = New Button() With {.Text = "Retrieve", .Size = New Size(110, 36)}
            showDeletedButton = New Button() With {.Text = ShowDeletedText, .Size = New Size(130, 36)}
            showNormalButton = New Button() With {.Text = "Normal", .Size = New Size(64, 36), .Visible = False}

            activeFilterLabel = New Label() With {
                .Location = New Point(10, 8),
                .Size = New Size(920, 20),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .ForeColor = Color.DimGray,
                .Text = "Active Filter: (none)"
            }

            retrievalStatusLabel = New Label() With {
                .Name = "RetrievalStatusLabel",
                .Location = New Point(150, 8),
                .Size = New Size(500, 40),
                .AutoSize = False,
                .AutoEllipsis = False,
                .TextAlign = ContentAlignment.MiddleLeft,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
                .ForeColor = Color.DimGray,
                .Text = String.Empty,
                .Visible = False
            }

            retrievalStatusFlashTimer = New Timer() With {.Interval = 400}
            AddHandler retrievalStatusFlashTimer.Tick, AddressOf RetrievalStatusFlashTimer_Tick
            AddHandler Me.FormClosed, AddressOf FW_Base_B_FormClosed

            qbeGrid = New DataGridView() With {
                .Location = New Point(10, 32),
                .Size = New Size(650, 116),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left,
                .BorderStyle = BorderStyle.FixedSingle,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .ColumnHeadersVisible = True,
                .RowHeadersVisible = False,
                .SelectionMode = DataGridViewSelectionMode.CellSelect,
                .MultiSelect = False,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            }
            qbeGrid.RowTemplate.Height = 22

            ApplyLightBlueHeaderStyle(qbeGrid)
            qbeGrid.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)

            qbeGrid.Columns.Add("FieldName", "Field")
            qbeGrid.Columns.Add("FriendlyName", "Field")
            qbeGrid.Columns.Add(New DataGridViewComboBoxColumn() With {
                .Name = "Operator",
                .HeaderText = "Operator",
                .FlatStyle = FlatStyle.Flat,
                .DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                .Width = 160,
                .MinimumWidth = 160
            })
            qbeGrid.Columns.Add("FieldValue", "Value")
            qbeGrid.Columns("FieldName").ReadOnly = True
            qbeGrid.Columns("FriendlyName").ReadOnly = True
            qbeGrid.Columns("FieldName").Visible = False
            qbeGrid.Columns("Operator").AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            qbeGrid.Columns("Operator").Width = 160
            qbeFieldColumnWidthIncrease = Math.Max(0, qbeGrid.Columns("Operator").Width - qbeGrid.Columns("FriendlyName").Width)
            qbeValueColumnWidthIncrease = Math.Max(0, qbeGrid.Columns("Operator").Width - qbeGrid.Columns("FieldValue").Width)
            qbeGrid.Columns("FriendlyName").AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            qbeGrid.Columns("FriendlyName").Width = qbeGrid.Columns("Operator").Width
            qbeGrid.Columns("FieldValue").AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            qbeGrid.Columns("FieldValue").Width = qbeGrid.Columns("Operator").Width

            qbeFieldDefinitions = New List(Of QbeFieldDefinition)()

            currentFilters = New Dictionary(Of String, String)()

            browseGrid = New DataGridView() With {
                .Name = "browseGrid",
                .Dock = DockStyle.Fill,
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToOrderColumns = True,
                .ColumnHeadersVisible = True,
                .ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                .ColumnHeadersHeight = 34,
                .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .MultiSelect = False,
                .RowHeadersVisible = False,
                .ScrollBars = ScrollBars.Both
            }
            ApplyBrowseGridStandard(browseGrid)

            AddHandler createButton.Click, AddressOf CreateButton_Click
            AddHandler restoreButton.Click, AddressOf RestoreButton_Click
            AddHandler readButton.Click, AddressOf ReadButton_Click
            AddHandler updateButton.Click, AddressOf UpdateButton_Click
            AddHandler deleteButton.Click, AddressOf DeleteButton_Click
            AddHandler toggleQbeButton.Click, AddressOf ToggleQbeButton_Click
            AddHandler findButton.Click, AddressOf FindButton_Click
            AddHandler clearFiltersButton.Click, AddressOf ClearFiltersButton_Click
            AddHandler saveQbeButton.Click, AddressOf SaveQbeButton_Click
            AddHandler retrieveQbeButton.Click, AddressOf RetrieveQbeButton_Click
            AddHandler showDeletedButton.Click, AddressOf ShowDeletedButton_Click
            AddHandler showNormalButton.Click, AddressOf ShowNormalButton_Click
            AddHandler closeButton.Click, AddressOf CloseButton_Click
            AddHandler layoutComboBox.SelectedIndexChanged, AddressOf LayoutComboBox_SelectedIndexChanged
            AddHandler toggleColumnsPanelButton.Click, AddressOf ToggleColumnsPanelButton_Click
            AddHandler resetLayoutButton.Click, AddressOf ResetLayoutButton_Click
            AddHandler deleteLayoutButton.Click, AddressOf DeleteLayoutButton_Click
            AddHandler saveMyLayoutButton.Click, AddressOf SaveMyLayoutButton_Click
            AddHandler applySqlButton.Click, AddressOf ApplySqlButton_Click
            AddHandler registrationComboBox.SelectedIndexChanged, AddressOf RegistrationComboBox_SelectedIndexChanged
            AddHandler browseGrid.CellDoubleClick, AddressOf BrowseGrid_CellDoubleClick
            AddHandler browseGrid.SelectionChanged, AddressOf BrowseGrid_SelectionChanged
            AddHandler browseGrid.ColumnDisplayIndexChanged, AddressOf BrowseGrid_ColumnDisplayIndexChanged
            AddHandler browseGrid.ColumnStateChanged, AddressOf BrowseGrid_ColumnStateChanged
            AddHandler columnsManagerList.ItemCheck, AddressOf ColumnsManagerList_ItemCheck
            AddHandler columnsManagerList.KeyDown, AddressOf ColumnsManagerList_KeyDown
            AddHandler columnsManagerList.MouseDown, AddressOf ColumnsManagerList_MouseDown
            AddHandler columnsManagerList.MouseUp, AddressOf ColumnsManagerList_MouseUp
            AddHandler columnsManagerList.SelectedIndexChanged, AddressOf ColumnsManagerList_SelectedIndexChanged
            AddHandler columnsManagerHideButton.Click, AddressOf ColumnsManagerHideButton_Click
            AddHandler columnsManagerOkButton.Click, AddressOf ColumnsManagerOkButton_Click
            AddHandler columnsMoveUpButton.Click, AddressOf ColumnsMoveUpButton_Click
            AddHandler columnsMoveDownButton.Click, AddressOf ColumnsMoveDownButton_Click
            AddHandler Me.Resize, AddressOf ContactsForm_Resize
            AddHandler qbeSplitContainer.SplitterMoved, AddressOf QbeSplitContainer_SplitterMoved
            AddHandler Me.FormClosing, AddressOf BrowsePage_FormClosing
            AddHandler Me.Load, AddressOf BrowsePage_Load
            AddHandler Me.Shown, AddressOf BrowsePage_Shown

            qbePanel.Controls.Add(imagePanel)
            qbePanel.Controls.Add(activeFilterLabel)
            qbePanel.Controls.Add(retrievalStatusLabel)
            qbePanel.Controls.Add(qbeGrid)
            qbePanel.Controls.Add(findButton)
            qbePanel.Controls.Add(clearFiltersButton)
            qbePanel.Controls.Add(saveQbeButton)
            qbePanel.Controls.Add(retrieveQbeButton)
            columnsManagerPanel.Controls.Add(columnsManagerLabel)
            columnsManagerPanel.Controls.Add(columnsManagerHideButton)
            columnsManagerPanel.Controls.Add(columnsManagerOkButton)
            columnsManagerPanel.Controls.Add(columnsMoveUpButton)
            columnsManagerPanel.Controls.Add(columnsMoveDownButton)
            columnsManagerPanel.Controls.Add(columnsManagerList)
            layoutToolbarPanel.Controls.Add(layoutLabel)
            layoutToolbarPanel.Controls.Add(recordCountLabel)
            layoutToolbarPanel.Controls.Add(layoutComboBox)
            layoutToolbarPanel.Controls.Add(toggleColumnsPanelButton)
            layoutToolbarPanel.Controls.Add(resetLayoutButton)
            layoutToolbarPanel.Controls.Add(deleteLayoutButton)
            layoutToolbarPanel.Controls.Add(saveMyLayoutButton)
            qbeSplitContainer.Panel1.Controls.Add(qbePanel)
            qbeSplitContainer.Panel2.Controls.Add(browseGrid)
            qbeSplitContainer.Panel2.Controls.Add(layoutToolbarPanel)
            qbeSplitContainer.Panel2.Controls.Add(columnsManagerPanel)
            Me.Controls.Add(titleLabel)

            ' Every browse page can raise a report against itself, in the same screen position as
            ' the one on a maintenance page.
            HelpDeskLauncher.Attach(Me, Me.GetType().Name)
            Me.Controls.Add(sqlLabel)
            Me.Controls.Add(sqlTextBox)
            Me.Controls.Add(registrationIdLabel)
            Me.Controls.Add(registrationComboBox)
            Me.Controls.Add(applySqlButton)
            Me.Controls.Add(createButton)
            Me.Controls.Add(restoreButton)
            Me.Controls.Add(readButton)
            Me.Controls.Add(updateButton)
            Me.Controls.Add(deleteButton)
            Me.Controls.Add(closeButton)
            Me.Controls.Add(showDeletedButton)
            Me.Controls.Add(showNormalButton)
            Me.Controls.Add(toggleQbeButton)
            Me.Controls.Add(qbeSplitContainer)

            AddHandler Me.Load, AddressOf ContactsForm_Load
        End Sub

        Private Sub ContactsForm_Load(sender As Object, e As EventArgs)
            LoadReferenceImageFromAssets()
            WarnIfMissingRowVersion()
            RestorePageBackgroundColor()
            LoadRegistrationCombo()
            ApplyCrudButtonCaptions(GetRegistrationIdForCaptions())
            ApplyCrudAccess()
            UpdateActiveFilterLabel()

            Dim registrationId As Integer = GetRegistrationIdForCaptions()
            If registrationId > 0 Then
                PopulateLayoutSelector(registrationId)

                If IsMaintenancePkMissingInSqlSchema(registrationId) Then
                    missingMaintenancePkInResult = True
                    pendingInitialMissingPkWarning = True
                    UpdateShowDeletedButtonState()
                End If
            End If

            toggleQbeButton.Text = If(qbeExpanded, QbeExpandedText, QbeCollapsedText)
            qbeSplitContainer.Panel1Collapsed = Not qbeExpanded
            LayoutQbeSection()
            UpdateShowDeletedButtonState()
            ConfigureBrowseContentPanel(qbeSplitContainer.Panel2)

            If StartsEmptyOnInitialLoad() Then
                InitializeEmptyBrowseState()
            Else
                RefreshGrid(Nothing, True)
            End If
        End Sub

        Private Sub WarnIfMissingRowVersion()
            Dim tableName = ResolveCurrentRoleFieldTableName()
            If String.IsNullOrWhiteSpace(tableName) OrElse DataAccess.TableHasRowVersion(tableName) Then
                Return
            End If

            MessageBox.Show(Me,
                            "Table dbo." & tableName & " does not have a RowVersion column. Concurrent edit protection is unavailable for this page.",
                            "Concurrency Protection Unavailable",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub

        Private Sub ApplyCrudAccess()
            Dim showAdminQueryControls = IsRegistrationAwareSession()
            sqlLabel.Visible = showAdminQueryControls AndAlso Not OnlyUseQbe()
            sqlTextBox.Visible = showAdminQueryControls AndAlso Not OnlyUseQbe()
            UpdateRegistrationSelectorVisibility(IsAppAdminSession())
            applySqlButton.Visible = showAdminQueryControls AndAlso Not OnlyUseQbe()

            ' Admin only, like the Tab Order manager on _U pages.
            If backgroundColorButton IsNot Nothing Then
                backgroundColorButton.Visible = IsAppAdminSession()
                If Not backgroundColorButton.Visible Then backgroundColorPanel.Visible = False
            End If

            If Not UseRoleBasedCrudAccess() Then
                createButton.Visible = Not OnlyUseQbe()
                restoreButton.Visible = True
                readButton.Visible = Not OnlyUseQbe()
                updateButton.Visible = Not OnlyUseQbe()
                deleteButton.Visible = Not OnlyUseQbe()
                createButton.Enabled = True
                restoreButton.Enabled = True
                readButton.Enabled = True
                updateButton.Enabled = True
                deleteButton.Enabled = True
                LayoutQbeSection()
                Return
            End If

            If accessProfile Is Nothing Then
                createButton.Visible = False
                restoreButton.Visible = False
                readButton.Visible = False
                updateButton.Visible = False
                deleteButton.Visible = False

                createButton.Enabled = True
                restoreButton.Enabled = True
                readButton.Enabled = True
                updateButton.Enabled = True
                deleteButton.Enabled = True

                LayoutQbeSection()
                Return
            End If

            Dim canRead = accessProfile.Can(accessTableName, AccessCapability.Read)
            Dim canCreate = accessProfile.Can(accessTableName, AccessCapability.Create)
            Dim canUpdate = accessProfile.Can(accessTableName, AccessCapability.Update)
            Dim canDelete = accessProfile.Can(accessTableName, AccessCapability.Delete)
            Dim canUseQbe = accessProfile.Can(accessTableName, AccessCapability.UseQbe)
            Dim canExpandQbe = accessProfile.Can(accessTableName, AccessCapability.ExpandQbe)
            Dim canViewAll = accessProfile.Can(accessTableName, AccessCapability.ViewAllRecords)

            createButton.Visible = Not OnlyUseQbe() AndAlso canCreate
            readButton.Visible = Not OnlyUseQbe() AndAlso canRead
            updateButton.Visible = Not OnlyUseQbe() AndAlso canUpdate
            deleteButton.Visible = Not OnlyUseQbe() AndAlso canDelete

            createButton.Enabled = True
            readButton.Enabled = True
            updateButton.Enabled = True
            deleteButton.Enabled = True
            restoreButton.Enabled = True

            qbeExpanded = canUseQbe
            toggleQbeButton.Enabled = canUseQbe
            findButton.Enabled = canUseQbe
            clearFiltersButton.Enabled = canUseQbe
            saveQbeButton.Enabled = canUseQbe
            retrieveQbeButton.Enabled = canUseQbe
            UpdateShowDeletedButtonState()
            UpdateLayoutUiAvailability()

            ' QBE expand state is not role-limited.
            registrationComboBox.Enabled = True
            applySqlButton.Enabled = True

            LayoutQbeSection()
        End Sub

        Private Sub LoadSqlFromRoleTable()
            Try
                Dim activeSession = SessionState.Current
                If Not activeSession.HasValue OrElse activeSession.Value.RegistrationID <= 0 Then
                    Return
                End If

                Dim registrationId = activeSession.Value.RegistrationID
                Dim pageName = ResolveBrowsePageName()
                currentDbTableName = DataAccess.GetDbTableFromRoleTableByWindowOrPage(registrationId, pageName)
                UpdateShowDeletedButtonState()

                Me.Text = BuildBrowseListingTitle(registrationId, ResolveCurrentRoleFieldTableName())
                titleLabel.Text = Me.Text
                
                ' Try to get SQL from RoleTables
                Dim sql = DataAccess.GetTableSqlFromRoleTableByWindowOrPage(registrationId, pageName)
                
                If Not String.IsNullOrWhiteSpace(sql) Then
                    ' SQL exists - use it and replace ? placeholders
                    sql = sql.Trim()
                    sqlTextBox.Text = sql
                    sqlTextBox.SelectionStart = 0
                    sqlTextBox.SelectionLength = 0
                    sqlTextBox.ScrollToCaret()
                    sqlLoadedFromRoleTable = True
                Else
                    Dim fallbackTableName = ResolveCurrentRoleFieldTableName()
                    Dim fallbackSql = DataAccess.GetTableSqlFromRoleTable(registrationId, fallbackTableName)
                    Dim copiedExistingTableSql = Not String.IsNullOrWhiteSpace(fallbackSql)
                    Dim parentRoleTableId As Integer? = If(copiedExistingTableSql,
                                                           DataAccess.GetRoleTableIdByTable(registrationId, fallbackTableName),
                                                           Nothing)
                    If String.IsNullOrWhiteSpace(fallbackSql) Then
                        fallbackSql = BuildDefaultSqlForBrowse(registrationId)
                    End If
                    sqlTextBox.Text = fallbackSql
                    sqlTextBox.SelectionStart = 0
                    sqlTextBox.SelectionLength = 0
                    sqlTextBox.ScrollToCaret()
                    sqlLoadedFromRoleTable = False

                    Dim fallbackUserId = If(activeSession.Value.UserID > 0, activeSession.Value.UserID, 0)
                    If Not String.IsNullOrWhiteSpace(fallbackTableName) AndAlso
                       Not String.IsNullOrWhiteSpace(fallbackSql) Then
                        Dim persisted = DataAccess.UpsertRoleTableRecord(registrationId,
                                                                         pageName,
                                                                         fallbackTableName,
                                                                         If(fallbackTableName.StartsWith("FW_", StringComparison.OrdinalIgnoreCase),
                                                                             fallbackTableName.Substring(3),
                                                                             fallbackTableName),
                                                                         fallbackSql,
                                                                         fallbackUserId)
                        If persisted AndAlso DataAccess.CheckIfRoleTableRecordExists(registrationId, pageName) Then
                            MessageBox.Show(Me,
                                                          ("FW_ROLETABLES RECORD CREATED FOR " & pageName & ". " &
                                            If(copiedExistingTableSql,
                                                              "SQL WAS COPIED FROM PARENT RECORD PK " & If(parentRoleTableId.HasValue, parentRoleTableId.Value.ToString(), "UNKNOWN") & ".",
                                                              "DEFAULT SQL WAS CREATED FOR " & fallbackTableName & ".")).ToUpperInvariant(),
                                                          "BROWSE PAGE REGISTERED",
                                            MessageBoxButtons.OK,
                                            MessageBoxIcon.Information)
                        Else
                            Throw New InvalidOperationException("FW_RoleTables did not create a record for " & pageName & ".")
                        End If
                    End If

                    DataAccess.LogFallbackUsage("SQL_Fallback_DefaultBuilder",
                                                "No role SQL for page; persisted BuildDefaultSqlForBrowse result for editing.",
                                                ResolveBrowsePageName(),
                                                registrationId)
                End If
            Catch ex As Exception
                DataAccess.LogFallbackUsage("SQL_Fallback_LoadSqlException",
                                            "Exception while loading SQL from role table; page keeps current/default SQL.",
                                            ResolveBrowsePageName())
                ' Fail silently
            End Try
        End Sub

        Private Function BuildDefaultSqlForBrowse(registrationId As Integer) As String
            Dim tableName = ResolveCurrentRoleFieldTableName()
            If String.IsNullOrWhiteSpace(tableName) Then
                Return String.Empty
            End If

            If tableName.Contains(".") Then
                Return "SELECT * FROM " & tableName
            End If

            Return "SELECT * FROM dbo." & tableName
        End Function

        Protected Overridable Function ResolveBrowsePageName() As String
            Return Me.GetType().Name
        End Function

        Protected Overridable Sub ConfigureBrowseContentPanel(panel As Panel)
        End Sub

        Protected Overridable Sub NotifyBrowseSelectionChanged()
        End Sub

        Protected Overridable Function StartsEmptyOnInitialLoad() As Boolean
            Return True
        End Function

        Protected Overridable Function GetVisibleQbeRowCount() As Integer
            Return 4
        End Function

        Protected Overridable Function BuildBrowseListingTitle(registrationId As Integer, tableName As String) As String
            Return ToFriendlyCaption(tableName) & " Listing"
        End Function

        Protected Overridable Function HandleDefaultCreateAction() As Boolean
            Return False
        End Function

        Protected Overridable Function HandleDefaultReadAction() As Boolean
            Return False
        End Function

        Protected Overridable Function HandleDefaultUpdateAction(recordId As Integer) As Boolean
            Return False
        End Function

        Protected Overridable Function HandleDefaultDeleteAction() As Boolean
            Return False
        End Function

        Protected Overridable Function HandleDefaultRestoreAction() As Boolean
            Return False
        End Function

        Private Sub BrowsePage_Load(sender As Object, e As EventArgs)
            LoadSqlFromRoleTable()

            If Not EnsureSqlOrClose() Then
                Me.DialogResult = DialogResult.Cancel
                Return
            End If
        End Sub

        Private Sub BrowsePage_Shown(sender As Object, e As EventArgs)
            BeginInvoke(New MethodInvoker(AddressOf ShowInitialMissingPkWarningAndFocus))
        End Sub

        Private Sub ShowInitialMissingPkWarningAndFocus()
            If pendingInitialMissingPkWarning Then
                pendingInitialMissingPkWarning = False
                MaintenanceKeyGuard.UpdateAvailabilityAndMaybeWarn(missingMaintenancePkInResult,
                                                                   missingMaintenancePkWarningShown,
                                                                   Me)
            End If

            FocusPrimaryBrowseGrid()
        End Sub

        Private Sub FocusPrimaryBrowseGrid()
            FocusGridForBrowseEntry(browseGrid)
        End Sub

        Private Function PromptForTableName() As String
            Dim dlg As New Form()
            dlg.Text = "Select Database Table"
            dlg.StartPosition = FormStartPosition.CenterParent
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog
            dlg.MaximizeBox = False
            dlg.MinimizeBox = False
            dlg.ClientSize = New Size(320, 140)

            Dim lbl As New Label() With {
                .Text = "Select Table:",
                .Location = New Point(15, 15),
                .AutoSize = True
            }
            dlg.Controls.Add(lbl)

            ' Create ComboBox dropdown
            Dim combo As New ComboBox() With {
                .Location = New Point(15, 40),
                .Size = New Size(290, 25),
                .DropDownStyle = ComboBoxStyle.DropDownList
            }
            
            ' Load database tables with aliases
            Dim tablesWithAliases = DataAccess.GetDatabaseTablesWithAliases()
            For Each tableData In tablesWithAliases
                combo.Items.Add(tableData.TableName)
            Next
            
            If combo.Items.Count > 0 Then
                combo.SelectedIndex = 0
            End If
            
            dlg.Controls.Add(combo)

            Dim okBtn As New Button() With {
                .Text = "OK",
                .Location = New Point(145, 85),
                .Size = New Size(75, 32),
                .DialogResult = DialogResult.OK
            }
            dlg.Controls.Add(okBtn)

            Dim cancelBtn As New Button() With {
                .Text = "Cancel",
                .Location = New Point(230, 85),
                .Size = New Size(75, 32),
                .DialogResult = DialogResult.Cancel
            }
            dlg.Controls.Add(cancelBtn)

            dlg.AcceptButton = okBtn
            dlg.CancelButton = cancelBtn

            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso combo.SelectedIndex >= 0 Then
                Return combo.SelectedItem.ToString()
            Else
                Return String.Empty
            End If
        End Function

        Private Sub LoadReferenceImageFromAssets()
            Dim appBase = AppDomain.CurrentDomain.BaseDirectory
            Dim projectRoot = Path.GetFullPath(Path.Combine(appBase, "..", "..", ".."))
            Dim imagesFolder = Path.Combine(projectRoot, "assets", "images")

            If Not Directory.Exists(imagesFolder) Then
                Return
            End If

            Dim preferredImageFile = Path.Combine(imagesFolder, "SDC_LOGO.PNG")
            If File.Exists(preferredImageFile) Then
                Try
                    Using fs As New FileStream(preferredImageFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                        Using loaded = Image.FromStream(fs)
                            imageBox.Image = New Bitmap(loaded)
                        End Using
                    End Using
                Catch
                End Try
                Return
            End If

            Dim patterns = New String() {"*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp", "*.ico"}
            Dim imageFile As String = Nothing

            For Each pattern In patterns
                Dim files = Directory.GetFiles(imagesFolder, pattern, SearchOption.TopDirectoryOnly)
                If files.Length > 0 Then
                    imageFile = files(0)
                    Exit For
                End If
            Next

            If String.IsNullOrWhiteSpace(imageFile) Then
                Return
            End If

            Try
                Using fs As New FileStream(imageFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    Using loaded = Image.FromStream(fs)
                        imageBox.Image = New Bitmap(loaded)
                    End Using
                End Using
            Catch
            End Try
        End Sub

        Private Sub ContactsForm_Resize(sender As Object, e As EventArgs)
            LayoutQbeSection()
            GridColumnsManager.FitVisibleColumnsToAvailableWidth(browseGrid)
        End Sub

        Private Sub ToggleQbeButton_Click(sender As Object, e As EventArgs)
            If Not qbeSplitContainer.Panel1Collapsed Then
                qbeSplitterDistance = qbeSplitContainer.SplitterDistance
            End If

            qbeExpanded = Not qbeExpanded
            toggleQbeButton.Text = If(qbeExpanded, QbeExpandedText, QbeCollapsedText)
            qbeSplitContainer.Panel1Collapsed = Not qbeExpanded

            If qbeExpanded Then
                Dim maxSplitter = Math.Max(qbeSplitContainer.Panel1MinSize, qbeSplitContainer.Height - qbeSplitContainer.Panel2MinSize)
                qbeSplitContainer.SplitterDistance = Math.Min(Math.Max(qbeSplitContainer.Panel1MinSize, qbeSplitterDistance), maxSplitter)
            End If

            LayoutQbeSection()
        End Sub

        Private Sub QbeSplitContainer_SplitterMoved(sender As Object, e As SplitterEventArgs)
            If Not qbeSplitContainer.Panel1Collapsed Then
                qbeSplitterDistance = qbeSplitContainer.SplitterDistance
                LayoutQbeSection()
            End If
        End Sub

        Private Sub LayoutQbeSection()
            Dim margin As Integer = 20
            Dim contentWidth = Math.Min(1120, Math.Max(300, Me.ClientSize.Width - (margin * 2)))
            Dim contentLeft As Integer = Math.Max(margin, (Me.ClientSize.Width - contentWidth) \ 2)
            Dim qbeContentTop As Integer = 32
            Dim adminQueryControlsVisible = IsAppAdminSession()

            titleLabel.Left = contentLeft
            Dim headerCenterY As Integer = Math.Max(0, sqlTextBox.Top \ 2)
            titleLabel.Top = Math.Max(0, headerCenterY - (titleLabel.Height \ 2))
            HelpDeskLauncher.AlignToCaption(Me, titleLabel)

            sqlLabel.Left = contentLeft
            Dim sqlBaseLeft = sqlLabel.Right + 8
            applySqlButton.Left = contentLeft + contentWidth - applySqlButton.Width
            applySqlButton.Top = 78

            registrationComboBox.Left = contentLeft + contentWidth - registrationComboBox.Width - HelpDeskLauncher.ReservedWidth
            registrationComboBox.Top = Math.Max(0, headerCenterY - (registrationComboBox.Height \ 2))
            registrationIdLabel.Left = registrationComboBox.Left - registrationIdLabel.PreferredWidth - 8
            registrationIdLabel.Top = Math.Max(0, headerCenterY - (registrationIdLabel.Height \ 2))

            Dim sqlRight = applySqlButton.Left - 8
            sqlTextBox.Left = sqlBaseLeft
            sqlTextBox.Width = Math.Max(200, sqlRight - sqlBaseLeft)

            Dim actionTop As Integer = If(adminQueryControlsVisible, 112, 42)
            Dim actionLeft As Integer = contentLeft
            Dim actionGap As Integer = 10
            Dim actionRight As Integer = contentLeft + contentWidth

            closeButton.Top = actionTop
            closeButton.Left = actionRight - closeButton.Width

            Dim crudButtons As Button() = {createButton, readButton, updateButton, deleteButton}
            For Each btn In crudButtons
                If btn.Visible Then
                    btn.Left = actionLeft
                    btn.Top = actionTop
                    actionLeft = btn.Right + actionGap
                End If
            Next

            toggleQbeButton.Top = actionTop
            toggleQbeButton.Left = closeButton.Left - toggleQbeButton.Width - actionGap

            Dim deletedActionLeft = toggleQbeButton.Left - showDeletedButton.Width - actionGap
            showDeletedButton.Top = actionTop
            showDeletedButton.Left = deletedActionLeft

            ' Positioned from its neighbour like everything else in this row, so it cannot overlap
            ' or be overlapped at any window width. This is the slot the Enum button used to hold.
            If backgroundColorButton IsNot Nothing Then
                backgroundColorButton.Top = actionTop
                backgroundColorButton.Left = deletedActionLeft - backgroundColorButton.Width - actionGap
                PositionBackgroundColorPanel()
            End If
            restoreButton.Top = actionTop
            restoreButton.Left = deletedActionLeft
            showNormalButton.Top = actionTop
            showNormalButton.Left = restoreButton.Right + 2


            qbeSplitContainer.Left = contentLeft
            qbeSplitContainer.Top = If(adminQueryControlsVisible, 162, 92)
            qbeSplitContainer.Width = contentWidth
            qbeSplitContainer.Height = Math.Max(180, Me.ClientSize.Height - qbeSplitContainer.Top - margin)

            Dim toolbarWidth = Math.Max(220, layoutToolbarPanel.ClientSize.Width)
            Dim layoutTop As Integer = 6
            Dim layoutRightCursor As Integer = toolbarWidth - 8

            toggleColumnsPanelButton.Top = layoutTop
            toggleColumnsPanelButton.Left = layoutRightCursor - toggleColumnsPanelButton.Width
            layoutRightCursor = toggleColumnsPanelButton.Left - 8

            saveMyLayoutButton.Top = layoutTop
            saveMyLayoutButton.Left = layoutRightCursor - saveMyLayoutButton.Width
            layoutRightCursor = saveMyLayoutButton.Left - 8

            resetLayoutButton.Top = layoutTop
            resetLayoutButton.Left = layoutRightCursor - resetLayoutButton.Width
            layoutRightCursor = resetLayoutButton.Left - 8

            deleteLayoutButton.Top = layoutTop
            deleteLayoutButton.Left = layoutRightCursor - deleteLayoutButton.Width
            layoutRightCursor = deleteLayoutButton.Left - 8

            layoutComboBox.Top = layoutTop + 1
            layoutComboBox.Left = Math.Max(80, layoutRightCursor - layoutComboBox.Width)
            recordCountLabel.Left = 8
            recordCountLabel.Top = layoutTop + 6
            layoutLabel.Left = Math.Max(8, layoutComboBox.Left - layoutLabel.PreferredWidth - 8)
            layoutLabel.Top = layoutTop + 6

            Dim preferredQbeWidth As Integer = 940
            Dim qbePanelWidth As Integer = Math.Min(preferredQbeWidth, Math.Max(320, qbeSplitContainer.Panel1.ClientSize.Width))
            qbePanel.Left = 0
            qbePanel.Top = 0
            qbePanel.Width = qbePanelWidth
            qbePanel.Height = qbeSplitContainer.Panel1.ClientSize.Height

            If Not qbeSplitContainer.Panel1Collapsed Then
                Dim maxSplitter = Math.Max(qbeSplitContainer.Panel1MinSize, qbeSplitContainer.Height - qbeSplitContainer.Panel2MinSize)
                qbeSplitContainer.SplitterDistance = Math.Min(Math.Max(qbeSplitContainer.Panel1MinSize, qbeSplitterDistance), maxSplitter)
            End If

            Dim qbeContentHeight As Integer = Math.Max(116, qbePanel.ClientSize.Height - qbeContentTop - 8)
            Dim qbeImageTop As Integer = 32

            imagePanel.Left = 10
            imagePanel.Top = qbeImageTop
            imagePanel.Width = 120
            imagePanel.Height = qbeContentHeight
            imageLabel.Width = imagePanel.Width - 20
            imageBox.Left = 0
            imageBox.Top = 0
            imageBox.Width = imagePanel.ClientSize.Width
            imageBox.Height = imagePanel.ClientSize.Height

            Dim rightButtonWidth As Integer = 110
            Dim rightButtonGap As Integer = 10
            Dim rightButtonsAreaWidth As Integer = (rightButtonWidth + rightButtonGap) * 2

            Dim availableGroupWidth As Integer = Math.Max(280, qbePanel.ClientSize.Width - 20)
            Dim gridPreferredWidth = Math.Min(750, Math.Max(260, Math.Min(760, availableGroupWidth) - imagePanel.Width - 10 - rightButtonsAreaWidth - 10))

            Dim gridLeft As Integer = imagePanel.Right + 10
            qbeGrid.Left = gridLeft
            qbeGrid.Top = qbeContentTop
            qbeGrid.Width = gridPreferredWidth + qbeFieldColumnWidthIncrease + qbeValueColumnWidthIncrease
            Dim targetVisibleRows As Integer = GetVisibleQbeRowCount()
            Dim qbeGridBottomWhitespace As Integer = 6
            Dim exactRowHeight = qbeGrid.ColumnHeadersHeight + (qbeGrid.RowTemplate.Height * targetVisibleRows) + 2
            qbeGrid.Height = Math.Max(exactRowHeight, Math.Max(80, qbeContentHeight - qbeGridBottomWhitespace))

            Dim col1X As Integer = qbeGrid.Right + rightButtonGap
            Dim col2X As Integer = col1X + rightButtonWidth + rightButtonGap
            Dim btnH As Integer = 36
            Dim btnGap As Integer = 6
            findButton.Location = New Point(col1X, qbeContentTop + 4)
            clearFiltersButton.Location = New Point(col1X, qbeContentTop + 4 + btnH + btnGap)
            saveQbeButton.Location = New Point(col2X, qbeContentTop + 4)
            retrieveQbeButton.Location = New Point(col2X, qbeContentTop + 4 + btnH + btnGap)
            retrievalStatusLabel.Location = New Point(col1X, retrieveQbeButton.Bottom + 4)
            retrievalStatusLabel.Width = Math.Max(160, qbePanel.ClientSize.Width - retrievalStatusLabel.Left - 10)

            If columnsManagerPanel.Visible Then
                PositionColumnsManagerPanel()
                columnsManagerPanel.BringToFront()
            End If

            ApplyPageSpecificLayout()
        End Sub

        Protected Overridable Sub ApplyPageSpecificLayout()
        End Sub

        Private Sub RefreshGrid(Optional selectedRecordId As Integer? = Nothing,
                     Optional reevaluateQbe As Boolean = False,
                     Optional maxRows As Integer = 0,
                     Optional registrationIdOverride As Integer? = Nothing)
            SetColumnsPanelVisible(False)
            ApplyCrudButtonCaptions(GetRegistrationIdForCaptions())

            If Not EnsureSqlOrClose() Then
                Return
            End If

            Dim viewState = CaptureGridViewState()

            If selectedRecordId.HasValue Then
                viewState.HasSelection = True
                viewState.SelectedRecordId = selectedRecordId.Value
            End If

            Try
                Dim registrationId As Integer = 0
                If registrationIdOverride.HasValue AndAlso registrationIdOverride.Value > 0 Then
                    registrationId = registrationIdOverride.Value
                ElseIf Not TryGetActiveRegistrationId(registrationId) Then
                    Return
                End If

                Dim existingVisibility = CaptureColumnVisibilityMap()
                Dim activeSql = GetActiveBaseSql()
                Dim browseScopePredicate = GetBrowseUserScopePredicate()
                Dim browseScopeUserId = GetBrowseUserId()
                Dim viewOnlyMyRequested = accessProfile IsNot Nothing AndAlso
                                          accessProfile.Can(accessTableName, AccessCapability.ViewOnlyMyRecords)
                Dim canApplyViewOnlyMyScope = ShouldApplyViewOnlyMyScope(activeSql, registrationId)
                If canApplyViewOnlyMyScope Then
                    browseScopePredicate = "UserID = @UserID"
                    browseScopeUserId = currentUser.UserId
                ElseIf viewOnlyMyRequested Then
                    If Not viewOnlyMyWarningShown Then
                        viewOnlyMyWarningShown = True
                        MessageBox.Show(Me,
                                        "VIEW ONLY MY RECORDS IS NOT AVAILABLE BECAUSE OF A MISSING USERID.",
                                        "VIEW MINE NOT AVAILABLE",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Information)
                    End If

                    Return
                End If

                Dim dt = DataAccess.GetBrowseRowsByRegistration(registrationId,
                                                               currentFilters,
                                                               activeSql,
                                                               showDeletedRecordsOnly,
                                                               browseScopePredicate,
                                                               browseScopeUserId,
                                                               maxRows,
                                                               registrationId > 0)
                lastRefreshExceededRowLimit = maxRows > 0 AndAlso
                                              dt.ExtendedProperties.ContainsKey("BrowseRowsLimited") AndAlso
                                              Convert.ToBoolean(dt.ExtendedProperties("BrowseRowsLimited"))
                ' Fields this role may not see are removed from the result before anything can bind
                ' to them, so no later step can put them back on screen.
                RemoveInvisibleRoleFieldColumns(dt)
                browseGrid.DataSource = dt
                recordCountLabel.Text = "Record Count: " & dt.Rows.Count.ToString()
                browseGrid.ColumnHeadersVisible = True
                ApplyFriendlyColumnHeaders(browseGrid)
                ApplyPkColumnHiding(browseGrid)
                HideRegistrationIdColumn(browseGrid)
                HideSoftDeleteColumns(browseGrid)
                ApplyColumnVisibilityMap(existingVisibility)
                ApplyPkColumnHiding(browseGrid)
                HideRegistrationIdColumn(browseGrid)
                HideSoftDeleteColumns(browseGrid)
                EnsureAtLeastOneManageableVisibleColumn()
                UpdateMaintenanceKeyAvailability()
                GridColumnsManager.FitVisibleColumnsToAvailableWidth(browseGrid)
                UpdateLayoutUiAvailability()
                RefreshColumnsManagerFromGrid()
                UpdateShowDeletedButtonState()

                If pendingInitialLayoutApply Then
                    EnsureDefaultLayoutExists(registrationId)
                    ApplySavedLayoutIfAvailable(registrationId)
                    pendingInitialLayoutApply = False
                End If

                titleLabel.Text = Me.Text

                If reevaluateQbe Then
                    Dim sqlSignature = NormalizeSql(activeSql)
                    Dim visibleColumnsSignature = BuildVisibleColumnsSignature()
                    Dim shouldRebuildQbe = (sqlSignature <> lastAppliedSqlSignature) OrElse (visibleColumnsSignature <> lastVisibleColumnsSignature)

                    If shouldRebuildQbe Then
                        PopulateQbeFromGridColumns()
                        lastVisibleColumnsSignature = BuildVisibleColumnsSignature()
                    End If

                    lastAppliedSqlSignature = sqlSignature
                End If


                RestoreGridViewState(viewState)

                If Not hasBaselineLayoutSnapshot AndAlso browseGrid.Columns IsNot Nothing AndAlso browseGrid.Columns.Count > 0 Then
                    baselineLayoutSnapshot = BuildCurrentLayoutSnapshotJson()
                    hasBaselineLayoutSnapshot = True
                End If
            Catch ex As Exception
                MessageBox.Show("Failed to load page data: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub ApplySavedLayoutIfAvailable(registrationId As Integer)
            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return
            End If

            Dim pageName = Me.GetType().Name
            Dim preferredTableName = ResolveCurrentRoleFieldTableName()
            Dim tableCandidates As New List(Of String)()

            If Not String.IsNullOrWhiteSpace(preferredTableName) Then
                tableCandidates.Add(preferredTableName.Trim())
            End If
            If Not tableCandidates.Contains(String.Empty) Then
                tableCandidates.Add(String.Empty)
            End If

            For Each candidate In tableCandidates
                Dim preferredLayout = DataAccess.GetPreferredTableLayout(registrationId,
                                                                          session.Value.UserID,
                                                                          pageName,
                                                                          candidate,
                                                                          False)
                Dim layoutJson = preferredLayout.Item1
                If Not String.IsNullOrWhiteSpace(layoutJson) AndAlso
                   LayoutJsonHasAnyMatchingGridColumn(layoutJson) AndAlso
                   TryApplyLayoutSnapshotJson(layoutJson) Then
                    ApplyPkColumnHiding(browseGrid)
                    HideRegistrationIdColumn(browseGrid)
                    HideSoftDeleteColumns(browseGrid)
                    EnsureAtLeastOneManageableVisibleColumn()
                    GridColumnsManager.FitVisibleColumnsToAvailableWidth(browseGrid)
                    TryApplyLayoutSnapshotJson(layoutJson)
                    ApplyPkColumnHiding(browseGrid)
                    HideRegistrationIdColumn(browseGrid)
                    HideSoftDeleteColumns(browseGrid)
                    RefreshColumnsManagerFromGrid()
                    PopulateQbeFromGridColumns()
                    lastVisibleColumnsSignature = BuildVisibleColumnsSignature()
                    lastAppliedSqlSignature = NormalizeSql(GetActiveBaseSql())
                    Return
                End If
            Next

        End Sub

        Private Sub PopulateLayoutSelector(registrationId As Integer)
            Dim session = SessionState.Current
            If Not session.HasValue OrElse registrationId <= 0 Then
                suppressLayoutSelectionChanged = True
                layoutComboBox.DataSource = Nothing
                suppressLayoutSelectionChanged = False
                Return
            End If

            Dim pageName = Me.GetType().Name
            Dim tableName = ResolveCurrentRoleFieldTableName()
            Dim layouts = DataAccess.GetAvailableTableLayouts(registrationId, session.Value.UserID, pageName, tableName)

            Dim items As New List(Of LayoutSelectionItem)()
            For Each row As DataRow In layouts.Rows
                Dim ownerUserId As Integer = 0
                Integer.TryParse(Convert.ToString(row("OwnerUserID")), ownerUserId)

                Dim isShared As Boolean = False
                Dim sharedRaw = Convert.ToString(row("IsShared"))
                If Not String.IsNullOrWhiteSpace(sharedRaw) Then
                    isShared = (sharedRaw = "1" OrElse sharedRaw.Equals("true", StringComparison.OrdinalIgnoreCase))
                End If

                items.Add(New LayoutSelectionItem With {
                    .LayoutType = Convert.ToString(row("LayoutType")),
                    .LayoutName = Convert.ToString(row("LayoutName")),
                    .OwnerUserID = ownerUserId,
                    .IsShared = isShared,
                    .DisplayName = Convert.ToString(row("DisplayName"))
                })
            Next

            suppressLayoutSelectionChanged = True
            layoutComboBox.DataSource = Nothing
            layoutComboBox.DisplayMember = "DisplayName"
            layoutComboBox.ValueMember = Nothing
            layoutComboBox.DataSource = items
            suppressLayoutSelectionChanged = False
            UpdateDeleteLayoutButtonState()
        End Sub

        Private Sub LayoutComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If suppressLayoutSelectionChanged Then
                Return
            End If

            SetColumnsPanelVisible(False)
            UpdateDeleteLayoutButtonState()
            ApplySelectedLayoutFromCombo(False)
        End Sub

        Private Sub UpdateDeleteLayoutButtonState()
            If deleteLayoutButton Is Nothing Then
                Return
            End If

            If Not HasAnyManageableColumns() Then
                deleteLayoutButton.Enabled = False
                Return
            End If

            Dim selected = TryCast(layoutComboBox.SelectedItem, LayoutSelectionItem)
            If selected Is Nothing Then
                deleteLayoutButton.Enabled = False
                Return
            End If

            If Not String.Equals(selected.LayoutType, "UserNamed", StringComparison.OrdinalIgnoreCase) Then
                deleteLayoutButton.Enabled = False
                Return
            End If

            Dim session = SessionState.Current
            If Not session.HasValue Then
                deleteLayoutButton.Enabled = False
                Return
            End If

            Dim canDeleteOwn = selected.OwnerUserID = session.Value.UserID
            Dim canDeleteShared = selected.OwnerUserID = 0 AndAlso IsCompanyAdminSession()
            deleteLayoutButton.Enabled = canDeleteOwn OrElse canDeleteShared
        End Sub

        Private Sub ApplySelectedLayoutFromCombo(showWarnings As Boolean)
            Dim selected = TryCast(layoutComboBox.SelectedItem, LayoutSelectionItem)
            If selected Is Nothing Then
                If showWarnings Then
                    MessageBox.Show("No layout selected.", "Apply Layout", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
                Return
            End If

            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return
            End If

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                Return
            End If

            Dim pageName = Me.GetType().Name
            Dim tableName = ResolveCurrentRoleFieldTableName()

            Dim selectedUserId As Integer = selected.OwnerUserID
            If selected.LayoutType.Equals("LastUsed", StringComparison.OrdinalIgnoreCase) Then
                selectedUserId = session.Value.UserID
            ElseIf selected.LayoutType.Equals("Default", StringComparison.OrdinalIgnoreCase) Then
                selectedUserId = 0
            End If

            Dim layoutJson = DataAccess.GetTableLayoutJson(registrationId,
                                                           selectedUserId,
                                                           pageName,
                                                           tableName,
                                                           selected.LayoutType,
                                                           selected.LayoutName)
            If String.IsNullOrWhiteSpace(layoutJson) Then
                If showWarnings Then
                    MessageBox.Show("Selected layout was not found.", "Apply Layout", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
                Return
            End If

            If Not ApplyLayoutSnapshotAndRefresh(layoutJson) Then
                If showWarnings Then
                    MessageBox.Show("Selected layout could not be applied.", "Apply Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
                Return
            End If
        End Sub

        Private Function ApplyLayoutSnapshotAndRefresh(layoutJson As String) As Boolean
            If String.IsNullOrWhiteSpace(layoutJson) Then
                Return False
            End If

            If Not TryApplyLayoutSnapshotJson(layoutJson) Then
                Return False
            End If

            ApplyPkColumnHiding(browseGrid)
            HideRegistrationIdColumn(browseGrid)
            HideSoftDeleteColumns(browseGrid)
            EnsureAtLeastOneManageableVisibleColumn()
            RefreshColumnsManagerFromGrid()
            PopulateQbeFromGridColumns()
            lastVisibleColumnsSignature = BuildVisibleColumnsSignature()
            lastAppliedSqlSignature = NormalizeSql(GetActiveBaseSql())
            Return True
        End Function

        Private Sub ResetLayoutButton_Click(sender As Object, e As EventArgs)
            SetColumnsPanelVisible(False)

            Dim selected = TryCast(layoutComboBox.SelectedItem, LayoutSelectionItem)
            If selected IsNot Nothing Then
                ApplySelectedLayoutFromCombo(True)
                Return
            End If

            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return
            End If

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                Return
            End If

            Dim pageName = Me.GetType().Name
            Dim tableName = ResolveCurrentRoleFieldTableName()

            Dim preferredLayout = DataAccess.GetPreferredTableLayout(registrationId,
                                                                      session.Value.UserID,
                                                                      pageName,
                                                                      tableName,
                                                                      False)
            If ApplyLayoutSnapshotAndRefresh(preferredLayout.Item1) Then
                If String.Equals(preferredLayout.Item2, "Default", StringComparison.OrdinalIgnoreCase) Then
                    SelectLayoutItem("Default", "* Default", 0)
                Else
                    SelectLayoutItem("LastUsed", "Last Used", session.Value.UserID)
                End If
                Return
            End If

            MessageBox.Show("No saved layout is available to reset.", "Reset Layout", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub ToggleColumnsPanelButton_Click(sender As Object, e As EventArgs)
            SetColumnsPanelVisible(Not columnsManagerPanel.Visible)
            If columnsManagerPanel.Visible Then
                RefreshColumnsManagerFromGrid()
                CaptureColumnsManagerBaseline()
                columnsManagerList.Focus()
                PositionColumnsManagerPanel()
                If Not SessionState.HasSeenUiHint(ColumnsUsageHintKey) Then
                    MessageBox.Show("Tip: click the checkbox area to show or hide a column, and use Up/Down to reorder. Nothing changes on the grid until you choose OK.", "Columns", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    SessionState.MarkUiHintSeen(ColumnsUsageHintKey)
                End If
            End If
        End Sub

        Private Sub DeleteLayoutButton_Click(sender As Object, e As EventArgs)
            SetColumnsPanelVisible(False)

            Dim selected = TryCast(layoutComboBox.SelectedItem, LayoutSelectionItem)
            If selected Is Nothing OrElse Not String.Equals(selected.LayoutType, "UserNamed", StringComparison.OrdinalIgnoreCase) Then
                MessageBox.Show("Select a named layout to delete.", "Delete Layout", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return
            End If

            Dim canDeleteOwn = selected.OwnerUserID = session.Value.UserID
            Dim canDeleteShared = selected.OwnerUserID = 0 AndAlso IsCompanyAdminSession()
            If Not (canDeleteOwn OrElse canDeleteShared) Then
                MessageBox.Show("You can only delete your own named layouts. Company Admin can also delete shared layouts.", "Delete Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Dim promptName = If(String.IsNullOrWhiteSpace(selected.LayoutName), selected.DisplayName, selected.LayoutName)
            Dim confirm = MessageBox.Show("Delete layout '" & promptName & "'?", "Delete Layout", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            If confirm <> DialogResult.Yes Then
                Return
            End If

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                Return
            End If

            Dim pageName = Me.GetType().Name
            Dim tableName = ResolveCurrentRoleFieldTableName()
            Dim actingUserId = session.Value.UserID
            DataAccess.DeleteTableLayout(registrationId,
                                         selected.OwnerUserID,
                                         pageName,
                                         tableName,
                                         selected.LayoutType,
                                         selected.LayoutName,
                                         actingUserId)

            PopulateLayoutSelector(registrationId)
            UpdateDeleteLayoutButtonState()
        End Sub

        Private Sub ColumnsManagerHideButton_Click(sender As Object, e As EventArgs)
            ' Cancel. Nothing was applied while the panel was open, so restoring the list is enough.
            RestoreColumnsManagerBaseline()
            columnsManagerBaseline = Nothing
            SetColumnsPanelVisible(False)
        End Sub

        Private Sub SetColumnsPanelVisible(visible As Boolean)
            GridColumnsManager.SetColumnsPanelVisible(columnsManagerPanel,
                                                      toggleColumnsPanelButton,
                                                      visible,
                                                      ColumnsExpandedText,
                                                      ColumnsCollapsedText,
                                                      AddressOf PositionColumnsManagerPanel)
        End Sub

        Private Sub PositionColumnsManagerPanel()
            If qbeSplitContainer Is Nothing OrElse qbeSplitContainer.Panel2 Is Nothing Then
                Return
            End If

            Dim panel2 = qbeSplitContainer.Panel2
            Dim topOffset = layoutToolbarPanel.Bottom + 2
            Dim rightMargin = 2
            Dim bottomMargin = 2

            columnsManagerPanel.Left = Math.Max(0, panel2.ClientSize.Width - columnsManagerPanel.Width - rightMargin)
            columnsManagerPanel.Top = topOffset
            columnsManagerPanel.Height = Math.Max(120, panel2.ClientSize.Height - topOffset - bottomMargin)
        End Sub

        Protected Overridable Function IsManageableColumn(col As DataGridViewColumn) As Boolean
            If col Is Nothing Then
                Return False
            End If

            Dim columnKey = If(String.IsNullOrWhiteSpace(col.DataPropertyName), col.Name, col.DataPropertyName)
            Return Not IsPkAliasColumn(col) AndAlso Not IsSoftDeleteColumnName(columnKey)
        End Function

        Private Function IsPkAliasColumn(col As DataGridViewColumn) As Boolean
            Return MaintenanceKeyGuard.IsPkColumn(col)
        End Function

        Private Function IsSoftDeleteColumnName(columnName As String) As Boolean
            Return DeletedViewGuard.IsSoftDeleteColumnName(columnName)
        End Function

        Private Sub RefreshColumnsManagerFromGrid()
            If suppressColumnsManagerSync Then
                Return
            End If

            suppressColumnsManagerSync = True
            Try
                columnsManagerList.BeginUpdate()
                Dim ordered = browseGrid.Columns.Cast(Of DataGridViewColumn)().
                    Where(Function(c) IsManageableColumn(c)).
                    OrderBy(Function(c) c.DisplayIndex)
                GridColumnsManager.RefreshColumnsManagerFromGrid(columnsManagerList, ordered)
            Finally
                columnsManagerList.EndUpdate()
                suppressColumnsManagerSync = False
                UpdateColumnsManagerButtonsState()
            End Try
        End Sub

        Private Sub UpdateColumnsManagerButtonsState()
            GridColumnsManager.UpdateColumnsManagerButtonsState(columnsManagerList, columnsMoveUpButton, columnsMoveDownButton)
        End Sub

        Private Sub ColumnsManagerList_SelectedIndexChanged(sender As Object, e As EventArgs)
            UpdateColumnsManagerButtonsState()
        End Sub

        Private Sub ColumnsManagerList_KeyDown(sender As Object, e As KeyEventArgs)
            If suppressColumnsManagerSync Then
                Return
            End If

            If e.KeyCode = Keys.Space Then
                allowColumnsManagerCheckToggle = True
            End If

            GridColumnsManager.HandleColumnsManagerListKeyDown(columnsManagerList, e)

            If e.KeyCode = Keys.Space Then
                allowColumnsManagerCheckToggle = False
            End If
        End Sub

        Private Sub ColumnsManagerList_MouseDown(sender As Object, e As MouseEventArgs)
            allowColumnsManagerCheckToggle = False
            If e.Button <> MouseButtons.Left Then
                Return
            End If

            Dim itemIndex = columnsManagerList.IndexFromPoint(e.Location)
            If itemIndex < 0 Then
                Return
            End If

            Dim itemBounds = columnsManagerList.GetItemRectangle(itemIndex)
            allowColumnsManagerCheckToggle = e.X <= itemBounds.Left + SystemInformation.MenuCheckSize.Width + 4
        End Sub

        Private Sub ColumnsManagerList_MouseUp(sender As Object, e As MouseEventArgs)
            allowColumnsManagerCheckToggle = False
        End Sub

        Private Sub ColumnsManagerList_ItemCheck(sender As Object, e As ItemCheckEventArgs)
            If suppressColumnsManagerSync Then
                Return
            End If

            If Not allowColumnsManagerCheckToggle Then
                e.NewValue = e.CurrentValue
                Return
            End If

            If Not GridColumnsManager.ValidateItemCheck(columnsManagerList, e) Then
                Return
            End If

            ' Nothing is applied to the grid here. Ticking only edits the list, and OK commits the
            ' whole set at once - so the grid does not flicker column by column, and QBE is rebuilt
            ' once from the final selection rather than on every tick.
        End Sub

        ''' <summary>
        ''' Remembers the panel's contents as it opens, so Cancel can put them back. Nothing has
        ''' been applied to the grid by then, so only the list needs restoring.
        ''' </summary>
        Private Sub CaptureColumnsManagerBaseline()
            columnsManagerBaseline = New List(Of Tuple(Of String, Boolean))()

            For index = 0 To columnsManagerList.Items.Count - 1
                columnsManagerBaseline.Add(Tuple.Create(columnsManagerList.Items(index).ToString(),
                                                        columnsManagerList.GetItemChecked(index)))
            Next
        End Sub

        Private Sub RestoreColumnsManagerBaseline()
            If columnsManagerBaseline Is Nothing Then Return

            suppressColumnsManagerSync = True
            Try
                Dim byName = columnsManagerList.Items.Cast(Of Object)().ToDictionary(Function(item) item.ToString(), Function(item) item)
                columnsManagerList.Items.Clear()

                For Each entry In columnsManagerBaseline
                    Dim item As Object = Nothing
                    If Not byName.TryGetValue(entry.Item1, item) Then Continue For
                    columnsManagerList.SetItemChecked(columnsManagerList.Items.Add(item), entry.Item2)
                Next
            Finally
                suppressColumnsManagerSync = False
            End Try

            UpdateColumnsManagerButtonsState()
        End Sub

        Private Sub ColumnsManagerOkButton_Click(sender As Object, e As EventArgs)
            ApplyColumnsManagerStateToGrid()
            columnsManagerBaseline = Nothing
            SetColumnsPanelVisible(False)
        End Sub

        Private Sub ColumnsMoveUpButton_Click(sender As Object, e As EventArgs)
            MoveSelectedColumnsManagerItem(-1)
        End Sub

        Private Sub ColumnsMoveDownButton_Click(sender As Object, e As EventArgs)
            MoveSelectedColumnsManagerItem(1)
        End Sub

        Private Sub MoveSelectedColumnsManagerItem(delta As Integer)
            suppressColumnsManagerSync = True
            Try
                If Not GridColumnsManager.MoveSelectedItem(columnsManagerList, delta) Then
                    Return
                End If
            Finally
                suppressColumnsManagerSync = False
            End Try

            ' Reordering edits the list only. Like the checkboxes, it reaches the grid on OK, so
            ' the columns do not shuffle underneath the user while they arrange them.
            UpdateColumnsManagerButtonsState()
        End Sub

        Private Sub ApplyColumnsManagerStateToGrid()
            If suppressColumnsManagerSync Then
                Return
            End If

            suppressColumnsManagerSync = True
            Try
                GridColumnsManager.ApplyColumnsManagerStateToGrid(columnsManagerList, browseGrid)
                GridColumnsManager.FitVisibleColumnsToAvailableWidth(browseGrid)

                ApplyPkColumnHiding(browseGrid)
                HideRegistrationIdColumn(browseGrid)
                HideSoftDeleteColumns(browseGrid)

                ' QBE is derived from the visible columns, so hiding one here has to re-derive it.
                ' Without this the field stayed searchable after being hidden - and permanently so,
                ' because the signature below then told the next refresh that nothing had changed.
                ' Values already typed are preserved by field name, so a filter in progress
                ' survives on the columns that remain.
                PopulateQbeFromGridColumns()

                lastVisibleColumnsSignature = BuildVisibleColumnsSignature()
                lastAppliedSqlSignature = NormalizeSql(GetActiveBaseSql())
                If hasBaselineLayoutSnapshot Then
                    userChangedLayout = True
                End If
            Finally
                suppressColumnsManagerSync = False
            End Try
        End Sub

        Private Sub BrowseGrid_ColumnDisplayIndexChanged(sender As Object, e As DataGridViewColumnEventArgs)
            If suppressColumnsManagerSync Then
                Return
            End If

            If columnsManagerPanel.Visible Then
                RefreshColumnsManagerFromGrid()
            End If

            If hasBaselineLayoutSnapshot Then
                userChangedLayout = True
            End If

            ' QBE follows the grid's order, so dragging a column has to re-derive it. One drag
            ' raises this once per column whose position shifted, so the rebuild is deferred and
            ' coalesced - otherwise a single move of a left-hand column would rebuild QBE several
            ' times over. Reset and the layout combo re-derive it through their own path.
            If qbeRebuildPending Then Return

            qbeRebuildPending = True
            BeginInvoke(New MethodInvoker(Sub()
                                              qbeRebuildPending = False
                                              PopulateQbeFromGridColumns()
                                              lastVisibleColumnsSignature = BuildVisibleColumnsSignature()
                                          End Sub))
        End Sub

        Private Sub BrowseGrid_ColumnStateChanged(sender As Object, e As DataGridViewColumnStateChangedEventArgs)
            If suppressColumnsManagerSync Then
                Return
            End If

            If e.StateChanged = DataGridViewElementStates.Visible AndAlso columnsManagerPanel.Visible Then
                RefreshColumnsManagerFromGrid()
            End If

            If e.StateChanged = DataGridViewElementStates.Visible AndAlso hasBaselineLayoutSnapshot Then
                userChangedLayout = True
            End If

        End Sub

        Private Sub SaveMyLayoutButton_Click(sender As Object, e As EventArgs)
            SetColumnsPanelVisible(False)

            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return
            End If

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                Return
            End If

            Dim saveOptions = PromptForLayoutSaveOptions()
            If saveOptions Is Nothing OrElse String.IsNullOrWhiteSpace(saveOptions.LayoutName) Then
                Return
            End If

            Dim layoutName = saveOptions.LayoutName.Trim()
            If Not saveOptions.SaveAsDefault AndAlso layoutName.StartsWith("*", StringComparison.Ordinal) AndAlso Not IsCompanyAdminSession() Then
                MessageBox.Show("Only Company Admin can save layout names that start with '*'.", "Save Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            If Not saveOptions.SaveAsDefault AndAlso IsReservedLayoutName(layoutName) Then
                MessageBox.Show("'" & layoutName & "' is a reserved system layout name. Please choose a different name.", "Save Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            If Not HasAnyManageableVisibleColumns() Then
                MessageBox.Show("At least one visible data column is required before saving a layout.", "Save Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Dim snapshot = BuildCurrentLayoutSnapshotJson()
            If String.IsNullOrWhiteSpace(snapshot) Then
                MessageBox.Show("No layout data is available to save.", "Save Layout", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim pageName = Me.GetType().Name
            Dim tableName = ResolveCurrentRoleFieldTableName()
            Dim userId = session.Value.UserID

            Dim canManageSharedLayouts = IsCompanyAdminSession() OrElse IsAppAdminSession()

            If saveOptions.SaveAsDefault Then
                If Not canManageSharedLayouts Then
                    MessageBox.Show("Only Company Admin can set a default layout.", "Not Authorized", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                Dim existingDefaultJson = DataAccess.GetTableLayoutJson(registrationId,
                                                                         0,
                                                                         pageName,
                                                                         tableName,
                                                                         "Default",
                                                                         "* Default")
                If Not String.IsNullOrWhiteSpace(existingDefaultJson) Then
                    Dim overwriteDefault = MessageBox.Show("A default layout already exists. Overwrite it?", "Save Layout", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    If overwriteDefault <> DialogResult.Yes Then
                        Return
                    End If
                End If

                DataAccess.UpsertTableLayout(registrationId,
                                             0,
                                             pageName,
                                             tableName,
                                             "Default",
                                             "* Default",
                                             snapshot,
                                             userId)

                PopulateLayoutSelector(registrationId)
                SelectLayoutItem("Default", "* Default", 0)
                MessageBox.Show("Layout saved.", "Save Layout", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim ownerUserId = userId
            If saveOptions.ShareWithRegistration AndAlso canManageSharedLayouts Then
                ownerUserId = 0
                If Not layoutName.StartsWith("*", StringComparison.Ordinal) Then
                    layoutName = "* " & layoutName
                End If
            End If

            Dim existingNamedJson = DataAccess.GetTableLayoutJson(registrationId,
                                                                   ownerUserId,
                                                                   pageName,
                                                                   tableName,
                                                                   "UserNamed",
                                                                   layoutName)
            If Not String.IsNullOrWhiteSpace(existingNamedJson) Then
                Dim overwriteNamed = MessageBox.Show("A layout with this name already exists in this scope. Overwrite it?", "Save Layout", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                If overwriteNamed <> DialogResult.Yes Then
                    Return
                End If
            End If

            DataAccess.UpsertTableLayout(registrationId,
                                         ownerUserId,
                                         pageName,
                                         tableName,
                                         "UserNamed",
                                         layoutName,
                                         snapshot,
                                         userId)

            PopulateLayoutSelector(registrationId)
            SelectLayoutItem("UserNamed", layoutName, ownerUserId)
            MessageBox.Show("Layout saved.", "Save Layout", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Function IsReservedLayoutName(layoutName As String) As Boolean
            If String.IsNullOrWhiteSpace(layoutName) Then
                Return False
            End If

            Dim normalized = NormalizeLayoutName(layoutName)
            If normalized = String.Empty Then
                Return False
            End If

            Return normalized = "DEFAULT" OrElse normalized = "LASTUSED"
        End Function

        Protected Overridable Function HasAnyManageableVisibleColumns() As Boolean
            For Each col As DataGridViewColumn In browseGrid.Columns
                If IsManageableColumn(col) AndAlso col.Visible Then
                    Return True
                End If
            Next

            Return False
        End Function

        Protected Overridable Sub EnsureAtLeastOneManageableVisibleColumn()
            If browseGrid Is Nothing OrElse browseGrid.Columns Is Nothing OrElse browseGrid.Columns.Count = 0 Then
                Return
            End If

            If HasAnyManageableVisibleColumns() Then
                Return
            End If

            For Each col As DataGridViewColumn In browseGrid.Columns
                If IsManageableColumn(col) Then
                    col.Visible = True
                End If
            Next
        End Sub

        Private Sub EnsureDefaultLayoutExists(registrationId As Integer)
            Dim session = SessionState.Current
            If Not session.HasValue OrElse registrationId <= 0 OrElse browseGrid.Columns.Count = 0 Then
                Return
            End If

            Dim pageName = Me.GetType().Name
            Dim tableName = ResolveCurrentRoleFieldTableName()
            Dim existingDefault = DataAccess.GetTableLayoutJson(registrationId,
                                                                0,
                                                                pageName,
                                                                tableName,
                                                                "Default",
                                                                "* Default")
            If Not String.IsNullOrWhiteSpace(existingDefault) Then
                Return
            End If

            DataAccess.UpsertTableLayout(registrationId,
                                         0,
                                         pageName,
                                         tableName,
                                         "Default",
                                         "* Default",
                                         BuildCurrentLayoutSnapshotJson(),
                                         session.Value.UserID)
            PopulateLayoutSelector(registrationId)
        End Sub

        Protected Overridable Function HasAnyManageableColumns() As Boolean
            If browseGrid Is Nothing OrElse browseGrid.Columns Is Nothing OrElse browseGrid.Columns.Count = 0 Then
                Return False
            End If

            For Each col As DataGridViewColumn In browseGrid.Columns
                If IsManageableColumn(col) Then
                    Return True
                End If
            Next

            Return False
        End Function

        Protected Overridable Sub UpdateLayoutUiAvailability()
            Dim hasManageableColumns = HasAnyManageableColumns()
            layoutComboBox.Enabled = hasManageableColumns
            resetLayoutButton.Enabled = hasManageableColumns
            toggleColumnsPanelButton.Enabled = hasManageableColumns
            saveMyLayoutButton.Enabled = hasManageableColumns
            deleteLayoutButton.Enabled = False

            If hasManageableColumns Then
                UpdateDeleteLayoutButtonState()
            End If

            If Not hasManageableColumns Then
                SetColumnsPanelVisible(False)
            End If
        End Sub

        Private Function NormalizeLayoutName(layoutName As String) As String
            If String.IsNullOrWhiteSpace(layoutName) Then
                Return String.Empty
            End If

            Dim text = layoutName.Trim().ToUpperInvariant()
            text = text.Replace(" ", String.Empty)
            text = text.Replace("_", String.Empty)
            text = text.Replace("-", String.Empty)
            Return text
        End Function

        Private Sub SelectLayoutItem(layoutType As String, layoutName As String, Optional ownerUserId As Integer = Integer.MinValue)
            For i As Integer = 0 To layoutComboBox.Items.Count - 1
                Dim item = TryCast(layoutComboBox.Items(i), LayoutSelectionItem)
                If item Is Nothing Then
                    Continue For
                End If

                If String.Equals(item.LayoutType, layoutType, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(If(item.LayoutName, String.Empty), If(layoutName, String.Empty), StringComparison.OrdinalIgnoreCase) AndAlso
                   (ownerUserId = Integer.MinValue OrElse item.OwnerUserID = ownerUserId) Then
                    layoutComboBox.SelectedIndex = i
                    Return
                End If
            Next
        End Sub

        Private Function PromptForLayoutSaveOptions() As LayoutSaveOptions
            Dim dlg As New Form()
            dlg.Text = "Save Layout"
            dlg.StartPosition = FormStartPosition.CenterParent
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog
            dlg.MaximizeBox = False
            dlg.MinimizeBox = False
            Dim canManageSharedLayouts = IsCompanyAdminSession() OrElse IsAppAdminSession()

            dlg.ClientSize = New Size(420, If(canManageSharedLayouts, 210, 132))

            Dim lbl As New Label() With {
                .Text = "Layout name:",
                .Location = New Point(14, If(canManageSharedLayouts, 52, 14)),
                .AutoSize = True
            }
            dlg.Controls.Add(lbl)

            Dim txt As New TextBox() With {
                .Location = New Point(14, If(canManageSharedLayouts, 76, 38)),
                .Size = New Size(392, 24),
                .MaxLength = 100
            }
            dlg.Controls.Add(txt)

            Dim saveAsDefaultCheck As CheckBox = Nothing
            Dim shareCheck As CheckBox = Nothing
            If canManageSharedLayouts Then
                saveAsDefaultCheck = New CheckBox() With {
                    .Text = "Save as Default layout for this registration",
                    .Location = New Point(14, 14),
                    .AutoSize = True,
                    .Checked = False
                }
                dlg.Controls.Add(saveAsDefaultCheck)

                shareCheck = New CheckBox() With {
                    .Text = "Make available to everyone in this registration",
                    .Location = New Point(14, 108),
                    .AutoSize = True,
                    .Checked = False
                }
                dlg.Controls.Add(shareCheck)

                Dim previousCustomName As String = String.Empty
                AddHandler saveAsDefaultCheck.CheckedChanged,
                    Sub()
                        If saveAsDefaultCheck.Checked Then
                            previousCustomName = txt.Text.Trim()
                            txt.Text = "* Default"
                            txt.ReadOnly = True
                            shareCheck.Checked = True
                            shareCheck.Enabled = False
                        Else
                            txt.ReadOnly = False
                            txt.Text = previousCustomName
                            shareCheck.Enabled = True
                        End If
                    End Sub
            End If

            Dim okBtn As New Button() With {
                .Text = "OK",
                .Location = New Point(250, If(canManageSharedLayouts, 160, 82)),
                .Size = New Size(75, 30),
                .DialogResult = DialogResult.OK
            }
            dlg.Controls.Add(okBtn)

            Dim cancelBtn As New Button() With {
                .Text = "Cancel",
                .Location = New Point(331, If(canManageSharedLayouts, 160, 82)),
                .Size = New Size(75, 30),
                .DialogResult = DialogResult.Cancel
            }
            dlg.Controls.Add(cancelBtn)

            dlg.AcceptButton = okBtn
            dlg.CancelButton = cancelBtn

            If dlg.ShowDialog(Me) <> DialogResult.OK Then
                Return Nothing
            End If

            Return New LayoutSaveOptions With {
                .LayoutName = txt.Text.Trim(),
                .ShareWithRegistration = (shareCheck IsNot Nothing AndAlso shareCheck.Checked),
                .SaveAsDefault = (saveAsDefaultCheck IsNot Nothing AndAlso saveAsDefaultCheck.Checked)
            }
        End Function

        Protected Function BuildLayoutSnapshotJson(grid As DataGridView) As String
            If grid Is Nothing OrElse grid.Columns Is Nothing OrElse grid.Columns.Count = 0 Then
                Return String.Empty
            End If

            Dim orderedColumns = grid.Columns.Cast(Of DataGridViewColumn)().
                OrderBy(Function(c) c.DisplayIndex).
                ToList()

            Dim sb As New StringBuilder()
            sb.Append("[")

            For i As Integer = 0 To orderedColumns.Count - 1
                Dim col = orderedColumns(i)
                Dim columnKey = GetColumnLayoutKey(col)
                If String.IsNullOrWhiteSpace(columnKey) Then
                    Continue For
                End If

                If sb.Length > 1 Then
                    sb.Append(",")
                End If

                sb.Append("{""Key"":""")
                sb.Append(EscapeJson(columnKey))
                sb.Append(""",""DisplayIndex"":")
                sb.Append(col.DisplayIndex.ToString())
                sb.Append(",""Visible"":")
                sb.Append(If(col.Visible, "true", "false"))
                sb.Append(",""Width"":")
                sb.Append(Math.Max(0, col.Width).ToString())
                sb.Append("}")
            Next

            sb.Append("]")
            Return sb.ToString()
        End Function

        Private Function BuildCurrentLayoutSnapshotJson() As String
            Return BuildLayoutSnapshotJson(browseGrid)
        End Function

        Protected Function TryApplyLayoutSnapshotJson(grid As DataGridView, layoutJson As String) As Boolean
            If grid Is Nothing OrElse String.IsNullOrWhiteSpace(layoutJson) OrElse grid.Columns Is Nothing OrElse grid.Columns.Count = 0 Then
                Return False
            End If

            Dim parseJson = DataAccess.NormalizeLayoutJsonForParsing(layoutJson)
            If String.IsNullOrWhiteSpace(parseJson) Then
                Return False
            End If

            Dim columnMap As New Dictionary(Of String, DataGridViewColumn)(StringComparer.OrdinalIgnoreCase)
            For Each col As DataGridViewColumn In grid.Columns
                Dim key = GetColumnLayoutKey(col)
                If String.IsNullOrWhiteSpace(key) Then
                    Continue For
                End If

                If Not columnMap.ContainsKey(key) Then
                    columnMap(key) = col
                End If
            Next

            Dim changesApplied As Boolean = False

            Try
                Using doc = JsonDocument.Parse(parseJson)
                    If doc.RootElement.ValueKind <> JsonValueKind.Array Then
                        Return False
                    End If

                    Dim entries As New List(Of Tuple(Of DataGridViewColumn, Integer, Boolean, Integer))()
                    For Each item In doc.RootElement.EnumerateArray()
                        If item.ValueKind <> JsonValueKind.Object Then
                            Continue For
                        End If

                        Dim key As String = String.Empty
                        Dim displayIndex As Integer = Integer.MaxValue
                        Dim isVisible As Boolean = True
                        Dim width As Integer = 0

                        Dim keyProp As JsonElement
                        If item.TryGetProperty("Key", keyProp) Then
                            key = keyProp.GetString()
                        End If
                        If String.IsNullOrWhiteSpace(key) Then
                            Continue For
                        End If

                        Dim col As DataGridViewColumn = Nothing
                        If Not columnMap.TryGetValue(key, col) OrElse col Is Nothing Then
                            Continue For
                        End If

                        Dim idxProp As JsonElement
                        If item.TryGetProperty("DisplayIndex", idxProp) Then
                            If idxProp.ValueKind = JsonValueKind.Number Then
                                displayIndex = idxProp.GetInt32()
                            End If
                        End If

                        Dim visProp As JsonElement
                        If item.TryGetProperty("Visible", visProp) Then
                            If visProp.ValueKind = JsonValueKind.True OrElse visProp.ValueKind = JsonValueKind.False Then
                                isVisible = visProp.GetBoolean()
                            End If
                        End If

                        Dim widthProp As JsonElement
                        If item.TryGetProperty("Width", widthProp) Then
                            If widthProp.ValueKind = JsonValueKind.Number Then
                                width = widthProp.GetInt32()
                            End If
                        End If

                        entries.Add(Tuple.Create(col, displayIndex, isVisible, width))
                    Next

                    For Each entry In entries.OrderBy(Function(e) e.Item2)
                        Dim col = entry.Item1
                        Dim desiredVisible = entry.Item3
                        Dim desiredWidth = entry.Item4
                        Dim desiredDisplay = entry.Item2

                        If col.Visible <> desiredVisible Then
                            col.Visible = desiredVisible
                            changesApplied = True
                        End If

                        If desiredWidth > 0 Then
                            Dim minWidth = Math.Max(20, col.MinimumWidth)
                            Dim clampedWidth = Math.Max(minWidth, desiredWidth)
                            If col.Width <> clampedWidth Then
                                col.Width = clampedWidth
                                changesApplied = True
                            End If
                        End If

                        Dim maxDisplay = Math.Max(0, grid.Columns.Count - 1)
                        Dim clampedDisplay = Math.Max(0, Math.Min(maxDisplay, desiredDisplay))
                        If col.DisplayIndex <> clampedDisplay Then
                            col.DisplayIndex = clampedDisplay
                            changesApplied = True
                        End If
                    Next
                End Using
            Catch
                Return False
            End Try

            Return True
        End Function

        Private Function TryApplyLayoutSnapshotJson(layoutJson As String) As Boolean
            Return TryApplyLayoutSnapshotJson(browseGrid, layoutJson)
        End Function

        Protected Overridable Function LayoutJsonHasAnyMatchingGridColumn(layoutJson As String) As Boolean
            If String.IsNullOrWhiteSpace(layoutJson) OrElse browseGrid Is Nothing OrElse browseGrid.Columns Is Nothing OrElse browseGrid.Columns.Count = 0 Then
                Return False
            End If

            Dim columnKeys As New List(Of String)()
            For Each col As DataGridViewColumn In browseGrid.Columns
                Dim key = GetColumnLayoutKey(col)
                If Not String.IsNullOrWhiteSpace(key) Then
                    columnKeys.Add(key)
                End If
            Next

            Return DataAccess.LayoutJsonHasAnyMatchingKeys(layoutJson, columnKeys)
        End Function

        Private Shared Function EscapeJson(input As String) As String
            If input Is Nothing Then
                Return String.Empty
            End If

            Return input.Replace("\", "\\").Replace("""", "\" & ChrW(34))
        End Function

        Protected Overridable Function GetColumnLayoutKey(col As DataGridViewColumn) As String
            If col Is Nothing Then
                Return String.Empty
            End If

            Dim key = If(String.IsNullOrWhiteSpace(col.DataPropertyName), col.Name, col.DataPropertyName)
            Return If(key, String.Empty).Trim()
        End Function

        Protected Overridable Function IsRegistrationIdColumn(col As DataGridViewColumn) As Boolean
            If col Is Nothing Then
                Return False
            End If

            Dim dataProperty = If(col.DataPropertyName, String.Empty)
            Dim name = If(col.Name, String.Empty)
            Dim header = If(col.HeaderText, String.Empty)

            Return String.Equals(dataProperty, "RegistrationID", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(name, "RegistrationID", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(header, "RegistrationID", StringComparison.OrdinalIgnoreCase)
        End Function

        Protected Overridable Sub ApplyCrudButtonCaptions(registrationId As Integer)
            If registrationId <= 0 Then
                Return
            End If

            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return
            End If

            Dim roleId = session.Value.RoleID
            Dim tableName = ResolveCurrentRoleFieldTableName()

            If roleId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                ' Fallback to registration-only captions
                Dim captions = DataAccess.GetCrudButtonCaptions(registrationId)
                createButton.Text = captions.CreateCaption
                readButton.Text = captions.ReadCaption
                updateButton.Text = captions.UpdateCaption
                deleteButton.Text = captions.DeleteCaption
                Return
            End If

            ' Use consolidated metadata call
            Dim metadata = DataAccess.GetPageInitMetadata(roleId, registrationId, tableName)
            createButton.Text = metadata.CrudCaptions.CreateCaption
            readButton.Text = metadata.CrudCaptions.ReadCaption
            updateButton.Text = metadata.CrudCaptions.UpdateCaption
            deleteButton.Text = metadata.CrudCaptions.DeleteCaption
        End Sub

        Private Function GetRegistrationIdForCaptions() As Integer
            Dim registrationId As Integer = 0
            If TryGetActiveRegistrationId(registrationId) Then
                Return registrationId
            End If

            Return 0
        End Function

        Protected Overridable Function TryGetActiveRegistrationId(ByRef registrationId As Integer) As Boolean
            If RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId) Then
                Return True
            End If

            Dim activeSession = SessionState.Current
            If activeSession.HasValue AndAlso activeSession.Value.RegistrationID > 0 Then
                registrationId = activeSession.Value.RegistrationID
                Return True
            End If

            Return False
        End Function

        Protected Sub ClearBrowseGridForPendingQuery()
            browseGrid.DataSource = New DataTable()
            browseGrid.ClearSelection()
            recordCountLabel.Text = "Record Count: 0"
        End Sub

        Private Function HasRecordKeyColumn() As Boolean
            Return Not String.IsNullOrWhiteSpace(GetRecordKeyColumnName())
        End Function

        Private Sub ApplyPkColumnHiding(grid As DataGridView)
            MaintenanceKeyGuard.HidePkColumn(grid)
        End Sub

        Private Sub HideRegistrationIdColumn(grid As DataGridView)
            Return
        End Sub

        ''' <summary>
        ''' Hides columns whose field is flagged Make_Invisible in FW_RoleFields, so one
        ''' field-level setting hides it on both the browse grid and the maintenance page.
        ''' QBE follows automatically, since it derives from visible columns.
        ''' </summary>
        ''' <summary>
        ''' Removes fields the role may not see from the result outright, rather than hiding them.
        '''
        ''' Hiding is only a display convention, and anything that re-applies column visibility can
        ''' undo it - a saved layout did exactly that, putting the field back on screen the first
        ''' time a page opened. It also left the field listed in the columns manager, where it could
        ''' simply be ticked back on. Dropping the column removes all of those routes at once: there
        ''' is nothing to show, nothing to list, nothing for QBE to derive, and nothing a layout can
        ''' resurrect.
        '''
        ''' Only `Make_Invisible` role fields are dropped. `PK`, `RegistrationID` and the soft-delete
        ''' columns are also hidden from the user, but the framework reads them, so those stay in
        ''' the table and remain merely hidden.
        ''' </summary>
        Protected Overridable Sub RemoveInvisibleRoleFieldColumns(table As DataTable)
            If table Is Nothing OrElse table.Columns.Count = 0 Then Return

            Dim session = SessionState.Current
            If Not session.HasValue Then Return

            Dim roleId = session.Value.RoleID
            Dim registrationId = session.Value.RegistrationID
            Dim tableName = ResolveCurrentRoleFieldTableName()
            If roleId <= 0 OrElse registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return
            End If

            Dim invisibleFields = DataAccess.GetPageInitMetadata(roleId, registrationId, tableName).InvisibleFields
            If invisibleFields Is Nothing OrElse invisibleFields.Count = 0 Then Return

            For Each columnName In table.Columns.Cast(Of DataColumn)().
                                         Select(Function(c) c.ColumnName).
                                         Where(Function(name) invisibleFields.Contains(name)).
                                         ToList()

                ' Never drop a column the framework depends on, whatever the metadata says.
                If String.Equals(columnName, "PK", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(columnName, "RegistrationID", StringComparison.OrdinalIgnoreCase) OrElse
                   IsSoftDeleteColumnName(columnName) Then
                    Continue For
                End If

                table.Columns.Remove(columnName)
            Next
        End Sub

        Protected Overridable Sub HideSoftDeleteColumns(grid As DataGridView)
            If grid Is Nothing OrElse grid.Columns Is Nothing OrElse grid.Columns.Count = 0 Then
                Return
            End If

            For Each col As DataGridViewColumn In grid.Columns
                If col Is Nothing OrElse Not col.Visible Then
                    Continue For
                End If

                Dim columnKey = If(String.IsNullOrWhiteSpace(col.DataPropertyName), col.Name, col.DataPropertyName)
                If IsSoftDeleteColumnName(columnKey) Then
                    col.Visible = False
                End If
            Next
        End Sub

        Private Function IsPkColumn(col As DataGridViewColumn) As Boolean
            Return MaintenanceKeyGuard.IsPkColumn(col)
        End Function

        Private Function GetRecordKeyColumnName() As String
            Return MaintenanceKeyGuard.ResolveMaintenanceKeyColumnName(browseGrid,
                                                                       New String() {"PK"})
        End Function

        Private Function IsIntegerType(valueType As Type) As Boolean
            If valueType Is Nothing Then
                Return False
            End If

            Return valueType Is GetType(Byte) OrElse
                   valueType Is GetType(Int16) OrElse
                   valueType Is GetType(Int32) OrElse
                   valueType Is GetType(Int64)
        End Function

        Private Function TryGetRecordIdFromRow(row As DataGridViewRow, ByRef recordId As Integer) As Boolean
            recordId = 0

            Dim keyColumnName = GetRecordKeyColumnName()
            If String.IsNullOrWhiteSpace(keyColumnName) OrElse row Is Nothing Then
                Return False
            End If

            Dim cell = row.Cells(keyColumnName)
            If cell Is Nothing OrElse cell.Value Is Nothing OrElse IsDBNull(cell.Value) Then
                Return False
            End If

            Return Integer.TryParse(cell.Value.ToString(), recordId)
        End Function

        Private Sub PopulateQbeFromGridColumns()
            Dim existingQbeValues As New Dictionary(Of String, Tuple(Of String, String))(StringComparer.OrdinalIgnoreCase)
            For Each existingRow As DataGridViewRow In qbeGrid.Rows
                If existingRow Is Nothing OrElse existingRow.IsNewRow Then
                    Continue For
                End If

                Dim fieldName = Convert.ToString(existingRow.Cells("FieldName").Value).Trim()
                If String.IsNullOrWhiteSpace(fieldName) Then
                    Continue For
                End If

                existingQbeValues(fieldName) = Tuple.Create(
                    Convert.ToString(existingRow.Cells("Operator").Value),
                    Convert.ToString(existingRow.Cells("FieldValue").Value))
            Next

            qbeFieldDefinitions.Clear()
            qbeGrid.Rows.Clear()

            ' Ordered by DisplayIndex, not by the order the SQL returned the columns, so QBE reads
            ' in the same order as the grid. Arranging the important column first puts it first
            ' in QBE too, rather than leaving the two out of step.
            For Each col As DataGridViewColumn In browseGrid.Columns.Cast(Of DataGridViewColumn)().OrderBy(Function(c) c.DisplayIndex)
                If col Is Nothing OrElse Not col.Visible Then
                    Continue For
                End If

                Dim fieldName = col.DataPropertyName
                If String.IsNullOrWhiteSpace(fieldName) Then
                    fieldName = col.Name
                End If

                If String.IsNullOrWhiteSpace(fieldName) Then
                    Continue For
                End If

                If IsPkAliasColumn(col) OrElse IsSoftDeleteColumnName(fieldName) Then
                    Continue For
                End If

                Dim displayName = If(String.IsNullOrWhiteSpace(col.HeaderText), ToFriendlyCaption(fieldName), col.HeaderText.Trim())
                Dim fieldKind = InferFieldKind(col)
                Dim rowIndex As Integer

                qbeFieldDefinitions.Add(New QbeFieldDefinition With {
                    .FieldName = fieldName,
                    .DisplayName = displayName,
                    .FieldKind = fieldKind
                })

                rowIndex = qbeGrid.Rows.Add(fieldName, displayName, String.Empty, String.Empty)
                InitializeQbeOperatorCell(rowIndex, qbeFieldDefinitions(qbeFieldDefinitions.Count - 1))

                Dim existingQbeValue As Tuple(Of String, String) = Nothing
                If existingQbeValues.TryGetValue(fieldName, existingQbeValue) Then
                    If Not String.IsNullOrWhiteSpace(existingQbeValue.Item1) Then
                        qbeGrid.Rows(rowIndex).Cells("Operator").Value = existingQbeValue.Item1
                    End If
                    qbeGrid.Rows(rowIndex).Cells("FieldValue").Value = existingQbeValue.Item2
                End If
            Next

            UpdateActiveFilterLabel()
        End Sub

        Private Sub PopulateQbeFromSqlSchema(registrationId As Integer)
            Dim activeSql = GetActiveBaseSql()
            If String.IsNullOrWhiteSpace(activeSql) Then
                Return
            End If

            Dim schema = DataAccess.GetSchemaFromSelectSql(activeSql, registrationId)
            If schema Is Nothing OrElse schema.Columns.Count = 0 Then
                Return
            End If

            qbeFieldDefinitions.Clear()
            qbeGrid.Rows.Clear()

            For Each dc As DataColumn In schema.Columns
                If dc Is Nothing OrElse String.IsNullOrWhiteSpace(dc.ColumnName) Then
                    Continue For
                End If

                     If String.Equals(dc.ColumnName, "PK", StringComparison.OrdinalIgnoreCase) OrElse
                         IsSoftDeleteColumnName(dc.ColumnName) Then
                    Continue For
                End If

                Dim displayName = ToFriendlyCaption(dc.ColumnName)
                Dim fieldKind = InferFieldKindFromType(dc.DataType)

                qbeFieldDefinitions.Add(New QbeFieldDefinition With {
                    .FieldName = dc.ColumnName,
                    .DisplayName = displayName,
                    .FieldKind = fieldKind
                })

                Dim rowIndex = qbeGrid.Rows.Add(dc.ColumnName, displayName, String.Empty, String.Empty)
                InitializeQbeOperatorCell(rowIndex, qbeFieldDefinitions(qbeFieldDefinitions.Count - 1))
            Next

            currentFilters = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            UpdateActiveFilterLabel()
        End Sub

        Protected Overridable Function InferFieldKind(col As DataGridViewColumn) As QbeFieldKind
            Dim t = col.ValueType
            Return InferFieldKindFromType(t)
        End Function

        Protected Overridable Function InferFieldKindFromType(t As Type) As QbeFieldKind
            If t Is Nothing Then
                Return QbeFieldKind.TextField
            End If

            If t Is GetType(Boolean) Then
                Return QbeFieldKind.BooleanField
            End If

            If t Is GetType(Byte) OrElse
               t Is GetType(Int16) OrElse
               t Is GetType(Int32) OrElse
               t Is GetType(Int64) OrElse
               t Is GetType(Single) OrElse
               t Is GetType(Double) OrElse
               t Is GetType(Decimal) Then
                Return QbeFieldKind.NumericField
            End If

            If t Is GetType(DateTime) Then
                Return QbeFieldKind.DateField
            End If

            Return QbeFieldKind.TextField
        End Function

        Private Function CaptureColumnVisibilityMap() As Dictionary(Of String, Boolean)
            Dim visibility As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            If browseGrid.Columns Is Nothing OrElse browseGrid.Columns.Count = 0 Then
                Return visibility
            End If

            For Each col As DataGridViewColumn In browseGrid.Columns
                Dim key = If(String.IsNullOrWhiteSpace(col.DataPropertyName), col.Name, col.DataPropertyName)
                If Not visibility.ContainsKey(key) Then
                    visibility(key) = col.Visible
                End If
            Next

            Return visibility
        End Function

        Private Sub ApplyColumnVisibilityMap(visibility As Dictionary(Of String, Boolean))
            If visibility Is Nothing OrElse visibility.Count = 0 Then
                Return
            End If

            For Each col As DataGridViewColumn In browseGrid.Columns
                Dim key = If(String.IsNullOrWhiteSpace(col.DataPropertyName), col.Name, col.DataPropertyName)
                If visibility.ContainsKey(key) Then
                    col.Visible = visibility(key)
                End If
            Next
        End Sub

        Private Function BuildVisibleColumnsSignature() As String
            If browseGrid.Columns Is Nothing OrElse browseGrid.Columns.Count = 0 Then
                Return String.Empty
            End If

            Dim keys As New List(Of String)()
            For Each col As DataGridViewColumn In browseGrid.Columns
                If Not col.Visible Then
                    Continue For
                End If

                Dim key = If(String.IsNullOrWhiteSpace(col.DataPropertyName), col.Name, col.DataPropertyName)
                keys.Add(key)
            Next

            Return String.Join("|", keys)
        End Function

        Private Function NormalizeSql(sql As String) As String
            If sql Is Nothing Then
                Return String.Empty
            End If

            Return Regex.Replace(sql.Trim(), "\s+", " ").ToUpperInvariant()
        End Function

        Protected Overridable Function GetActiveBaseSql() As String
            Return If(sqlTextBox.Text, String.Empty).Trim()
        End Function

        Protected Function EnsureSqlOrClose() As Boolean
            Dim sql = GetActiveBaseSql()
            If Not String.IsNullOrWhiteSpace(sql) Then
                Return True
            End If

            If Not missingSqlWarningShown Then
                missingSqlWarningShown = True
                MessageBox.Show("SQL is required for this page. The page will close.", "Missing SQL", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                DataAccess.LogFallbackUsage("SQL_Fallback_MissingSqlClose",
                                            "SQL was empty; page was closed.",
                                            Me.GetType().Name)
            End If
            Me.Close()
            Return False
        End Function

        Private Function IsMaintenancePkMissingInSqlSchema(registrationId As Integer) As Boolean
            Dim activeSql = GetActiveBaseSql()
            If String.IsNullOrWhiteSpace(activeSql) Then
                Return True
            End If

            Dim schema = DataAccess.GetSchemaFromSelectSql(activeSql, registrationId)
            If schema Is Nothing OrElse schema.Columns Is Nothing OrElse schema.Columns.Count = 0 Then
                Return True
            End If

            For Each dc As DataColumn In schema.Columns
                If dc IsNot Nothing AndAlso String.Equals(dc.ColumnName, "PK", StringComparison.OrdinalIgnoreCase) Then
                    Return False
                End If
            Next

            Return True
        End Function

        Protected Overridable Sub ApplyFriendlyColumnHeaders(grid As DataGridView)
            Dim captionMap = GetRoleFieldCaptionMapForCurrentContext()

            For Each col As DataGridViewColumn In grid.Columns
                Dim sourceName = If(String.IsNullOrWhiteSpace(col.DataPropertyName), col.Name, col.DataPropertyName)
                Dim caption As String = Nothing
                If sourceName IsNot Nothing AndAlso captionMap.TryGetValue(sourceName, caption) AndAlso Not String.IsNullOrWhiteSpace(caption) Then
                    col.HeaderText = caption.Trim()
                Else
                    Dim fallbackCaption = ToFriendlyCaption(sourceName)
                    If String.IsNullOrWhiteSpace(fallbackCaption) Then
                        fallbackCaption = If(col.HeaderText, String.Empty).Trim()
                    End If
                    col.HeaderText = fallbackCaption
                End If
            Next
        End Sub

        Protected Overridable Function GetRoleFieldCaptionMapForCurrentContext() As Dictionary(Of String, String)
            Dim session = SessionState.Current
            If Not session.HasValue Then
                Return New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            End If

            Dim roleId = session.Value.RoleID
            Dim registrationId = session.Value.RegistrationID
            Dim tableName = ResolveCurrentRoleFieldTableName()

            If roleId <= 0 OrElse registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            End If

            ' Use consolidated metadata call
            Dim metadata = DataAccess.GetPageInitMetadata(roleId, registrationId, tableName)
            Return metadata.FieldCaptions
        End Function

        Protected Overridable Function ResolveCurrentRoleFieldTableName() As String
            If Not String.IsNullOrWhiteSpace(currentDbTableName) Then
                Return currentDbTableName.Trim()
            End If

            Dim tableName = If(accessTableName, String.Empty).Trim()
            If String.IsNullOrWhiteSpace(tableName) Then
                Return String.Empty
            End If

            Return tableName
        End Function

        Protected Overridable Function UseRoleBasedCrudAccess() As Boolean
            Return True
        End Function

        Protected Overridable Function OnlyUseQbe() As Boolean
            Return False
        End Function

        Protected Overridable Function UsesRegistrationSelector() As Boolean
            Return True
        End Function

        Protected Overridable Function GetBrowseUserScopePredicate() As String
            Return String.Empty
        End Function

        Protected Overridable Function GetBrowseUserId() As Integer
            Return 0
        End Function

        Private Function ShouldApplyViewOnlyMyScope(activeSql As String, registrationId As Integer) As Boolean
            If accessProfile Is Nothing OrElse currentUser Is Nothing OrElse currentUser.UserId <= 0 OrElse
               Not accessProfile.Can(accessTableName, AccessCapability.ViewOnlyMyRecords) OrElse
               String.IsNullOrWhiteSpace(activeSql) OrElse registrationId <= 0 Then
                Return False
            End If

            Dim schema = DataAccess.GetSchemaFromSelectSql(activeSql, registrationId)
            Return schema IsNot Nothing AndAlso schema.Columns.Contains("UserID")
        End Function

        ''' <summary>
        ''' Column headers and page titles are table-derived, so the FW_ prefix is stripped.
        ''' Formatting itself is owned by DisplayNameFormatter.
        ''' </summary>
        Protected Overridable Function ToFriendlyCaption(sourceName As String) As String
            If String.IsNullOrWhiteSpace(sourceName) Then
                Return String.Empty
            End If

            Return DisplayNameFormatter.ToDisplayName(sourceName, stripFrameworkPrefix:=True)
        End Function

        Private Function CaptureGridViewState() As GridViewState
            Dim state As New GridViewState With {
                .HasSelection = False,
                .SelectedRecordId = 0,
                .SelectedRowOffsetFromTop = 0,
                .FallbackFirstDisplayedIndex = 0
            }

            If browseGrid.Rows.Count = 0 Then
                Return state
            End If

            If Not HasRecordKeyColumn() Then
                Return state
            End If

            If browseGrid.FirstDisplayedScrollingRowIndex >= 0 Then
                state.FallbackFirstDisplayedIndex = browseGrid.FirstDisplayedScrollingRowIndex
            End If

            Dim selectedId = SelectedRecordId()
            If Not selectedId.HasValue Then
                Return state
            End If

            state.HasSelection = True
            state.SelectedRecordId = selectedId.Value

            Dim selectedIndex = FindRowIndexByRecordId(selectedId.Value)
            If selectedIndex >= 0 AndAlso browseGrid.FirstDisplayedScrollingRowIndex >= 0 Then
                state.SelectedRowOffsetFromTop = Math.Max(0, selectedIndex - browseGrid.FirstDisplayedScrollingRowIndex)
            End If

            Return state
        End Function

        Private Function GetSelectedGridRowForIdentity() As DataGridViewRow
            If browseGrid Is Nothing Then
                Return Nothing
            End If

            If browseGrid.SelectedRows.Count > 0 Then
                Return browseGrid.SelectedRows(0)
            End If

            If browseGrid.CurrentCell IsNot Nothing Then
                Dim rowIndex = browseGrid.CurrentCell.RowIndex
                If rowIndex >= 0 AndAlso rowIndex < browseGrid.Rows.Count Then
                    Return browseGrid.Rows(rowIndex)
                End If
            End If

            If browseGrid.CurrentRow IsNot Nothing Then
                Return browseGrid.CurrentRow
            End If

            Return Nothing
        End Function

        Private Sub RestoreGridViewState(state As GridViewState)
            If browseGrid.Rows.Count = 0 Then
                Return
            End If

            If Not HasRecordKeyColumn() Then
                Return
            End If

            If state.HasSelection Then
                Dim selectedIndex = FindRowIndexByRecordId(state.SelectedRecordId)
                If selectedIndex >= 0 Then
                    browseGrid.Rows(selectedIndex).Selected = True
                    
                    ' Find first visible column
                    Dim firstVisibleCell As DataGridViewCell = Nothing
                    For Each col As DataGridViewColumn In browseGrid.Columns
                        If col.Visible Then
                            firstVisibleCell = browseGrid.Rows(selectedIndex).Cells(col.Index)
                            Exit For
                        End If
                    Next
                    
                    If firstVisibleCell IsNot Nothing Then
                        browseGrid.CurrentCell = firstVisibleCell
                    End If

                    Dim targetTop = Math.Max(0, selectedIndex - state.SelectedRowOffsetFromTop)
                    targetTop = Math.Min(targetTop, browseGrid.RowCount - 1)
                    Try
                        browseGrid.FirstDisplayedScrollingRowIndex = targetTop
                    Catch
                        ' Ignore if grid cannot set scroll position yet.
                    End Try
                    Return
                End If
            End If

            ' The row that was selected is no longer in the result - deleted, or filtered out by a
            ' changed criterion. Selecting something else would imply the user picked it, so nothing
            ' is selected and the grid returns to the top rather than to a position that no longer
            ' means anything. Binding leaves row 0 selected by default, hence the explicit clear.
            If state.HasSelection Then
                browseGrid.ClearSelection()
                browseGrid.CurrentCell = Nothing

                Try
                    browseGrid.FirstDisplayedScrollingRowIndex = 0
                Catch
                    ' Ignore if grid cannot set scroll position yet.
                End Try

                Return
            End If

            Dim fallbackTop = Math.Max(0, Math.Min(state.FallbackFirstDisplayedIndex, browseGrid.RowCount - 1))
            Try
                browseGrid.FirstDisplayedScrollingRowIndex = fallbackTop
            Catch
                ' Ignore if grid cannot set scroll position yet.
            End Try
        End Sub

        Private Function FindRowIndexByRecordId(recordId As Integer) As Integer
            Dim keyColumnName = GetRecordKeyColumnName()
            If String.IsNullOrWhiteSpace(keyColumnName) Then
                Return -1
            End If

            For i As Integer = 0 To browseGrid.Rows.Count - 1
                Dim row = browseGrid.Rows(i)
                Dim rowId As Integer
                If TryGetRecordIdFromRow(row, rowId) AndAlso rowId = recordId Then
                    Return i
                End If
            Next

            Return -1
        End Function

        Private Sub SelectRowByRecordId(recordId As Integer)
            Dim index = FindRowIndexByRecordId(recordId)
            If index >= 0 Then
                browseGrid.Rows(index).Selected = True
                browseGrid.CurrentCell = browseGrid.Rows(index).Cells(0)
            End If
        End Sub

        Private Function SelectedRecordId() As Integer?
            If Not HasRecordKeyColumn() Then
                Return Nothing
            End If

            Dim row = GetSelectedGridRowForIdentity()
            If row Is Nothing Then
                Return Nothing
            End If

            Dim recordId As Integer
            If Not TryGetRecordIdFromRow(row, recordId) Then
                Return Nothing
            End If

            Return recordId
        End Function

        Protected Function GetSelectedRecordIdForCustomAction() As Integer?
            Return SelectedRecordId()
        End Function

        ''' <summary>
        ''' The first visible cell of the selected row, for naming the record in a confirmation.
        ''' Falls back to the record id when there is nothing readable.
        '''
        ''' "Delete this record?" over a grid of similar rows does not tell the user which one is
        ''' about to go, and the destructive-action guardrail requires the target be identified.
        ''' A generated page has no idea which field is the record's name, so the leftmost visible
        ''' column is the closest thing available - and it is the one the user is looking at.
        ''' </summary>
        Protected Function GetSelectedRowSummary() As String
            If browseGrid.SelectedRows.Count = 0 Then Return String.Empty

            Dim row = browseGrid.SelectedRows(0)

            For Each col As DataGridViewColumn In browseGrid.Columns.Cast(Of DataGridViewColumn)().OrderBy(Function(c) c.DisplayIndex)
                If Not col.Visible Then Continue For

                Dim value = Convert.ToString(row.Cells(col.Index).Value)
                If Not String.IsNullOrWhiteSpace(value) Then Return value.Trim()
            Next

            Dim recordId = SelectedRecordId()
            Return If(recordId.HasValue, "record " & recordId.Value.ToString(), String.Empty)
        End Function

        Protected Function GetSelectedBrowseValue(columnName As String) As String
            If String.IsNullOrWhiteSpace(columnName) OrElse Not browseGrid.Columns.Contains(columnName) Then
                Return String.Empty
            End If

            Dim row = GetSelectedGridRowForIdentity()
            If row Is Nothing Then
                Return String.Empty
            End If

            Return Convert.ToString(row.Cells(columnName).Value)
        End Function

        Protected Sub RefreshGridForCustomAction(Optional selectedRecordId As Integer? = Nothing)
            RefreshGrid(selectedRecordId, True)
        End Sub

        Private Sub CreateButton_Click(sender As Object, e As EventArgs)
            If HandleCustomCreateAction() Then
                Return
            End If

            If HandleDefaultCreateAction() Then
                Return
            End If

            MessageBox.Show("Create is not wired for this browse page yet.", "Create", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub ReadButton_Click(sender As Object, e As EventArgs)
            If HandleCustomReadAction() Then
                Return
            End If

            If HandleDefaultReadAction() Then
                Return
            End If

            MessageBox.Show("Read detail is not wired for this browse page yet.", "Read", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        ''' <summary>
        ''' Whether the grid must reload after a maintenance page closed.
        '''
        ''' OK means something was saved. Abort means nothing was saved but the row is gone - the
        ''' record was deleted by someone else while it was open - so the grid is stale either way.
        ''' Cancel means nothing changed and a reload would be wasted work.
        '''
        ''' Single owner of this rule. Do not re-test DialogResult at a call site.
        ''' </summary>
        Protected Shared Function ShouldRefreshAfterMaintenance(result As DialogResult) As Boolean
            Return result = DialogResult.OK OrElse result = DialogResult.Abort
        End Function

        Private Sub ExecuteUpdateForRecord(recordId As Integer)
            If HandleCustomUpdateAction(recordId) Then
                Return
            End If

            If HandleDefaultUpdateAction(recordId) Then
                Return
            End If

            MessageBox.Show("Update is not wired for this browse page yet.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub UpdateButton_Click(sender As Object, e As EventArgs)
            Dim id = SelectedRecordId()
            If Not id.HasValue Then
                MessageBox.Show("Select a row first.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            ExecuteUpdateForRecord(id.Value)
        End Sub

        Private Sub DeleteButton_Click(sender As Object, e As EventArgs)
            If HandleCustomDeleteAction() Then
                Return
            End If

            If HandleDefaultDeleteAction() Then
                Return
            End If

            MessageBox.Show("Delete is not wired for this browse page yet.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End Sub

        Protected Overridable Function HandleCustomCreateAction() As Boolean
            Return False
        End Function

        Protected Overridable Function HandleCustomReadAction() As Boolean
            Return False
        End Function

        Protected Overridable Function HandleCustomUpdateAction(recordId As Integer) As Boolean
            Return False
        End Function

        Protected Overridable Function HandleCustomDeleteAction() As Boolean
            Return False
        End Function

        Private Sub RestoreButton_Click(sender As Object, e As EventArgs)
            If HandleDefaultRestoreAction() Then
                Return
            End If

            If Not showDeletedRecordsOnly Then
                Return
            End If

            If Not EnsureMaintenanceKeyAvailable("Restore") Then
                Return
            End If

            Dim id = SelectedRecordId()
            If Not id.HasValue Then
                MessageBox.Show("Select a row first.", "Restore", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If MessageBox.Show("Restore the selected record?", "Confirm Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            MessageBox.Show("Restore is not wired for this browse page yet.", "Restore", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub ApplySqlButton_Click(sender As Object, e As EventArgs)
            ClearQbeFilters()
            RefreshGrid(Nothing, True)
            UpdateRegistrationSelectorVisibility(IsAppAdminSession())
            Dim activeSql = GetActiveBaseSql()
            DataAccess.EnumeratePageControls(Me, Me.GetType().Name, currentUser.UserId, activeSql)
        End Sub

        Private Sub UpdateRegistrationSelectorVisibility(adminControlsVisible As Boolean)
            Dim canChooseRegistration = accessProfile IsNot Nothing AndAlso
                                         accessProfile.Can(accessTableName, AccessCapability.ViewAllRecords)
            Dim showRegistrationSelector = UsesRegistrationSelector() AndAlso
                                           adminControlsVisible AndAlso
                                           canChooseRegistration
            registrationIdLabel.Visible = showRegistrationSelector
            registrationComboBox.Visible = showRegistrationSelector

            If showRegistrationSelector AndAlso registrationComboBox.Items.Count = 0 Then
                LoadRegistrationCombo()
            End If
        End Sub

        Private Function ActiveSqlHasExplicitRegistrationPredicate() As Boolean
            Dim activeSql = GetActiveBaseSql()
            If String.IsNullOrWhiteSpace(activeSql) Then
                Return False
            End If

            Return Regex.IsMatch(activeSql,
                                 "\bWHERE\b[\s\S]*?(?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?RegistrationID\]?\s*=",
                                 RegexOptions.IgnoreCase)
        End Function

        Private Sub LoadRegistrationCombo()
            suppressRegistrationSelectionChanged = True
            Try
                Dim sessionRegistrationId = GetSessionRegistrationId()
                RegistrationComboHelper.Populate(registrationComboBox, sessionRegistrationId, False)
                If sessionRegistrationId > 0 Then
                    registrationComboBox.SelectedValue = sessionRegistrationId
                End If
                RegistrationComboHelper.UpdateLabelForSelection(registrationIdLabel, registrationComboBox)
                lastSelectedRegistrationId = sessionRegistrationId
            Finally
                suppressRegistrationSelectionChanged = False
            End Try
        End Sub

        Private Sub RegistrationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If suppressRegistrationSelectionChanged Then
                Return
            End If

            Dim registrationId As Integer = 0
            RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId)
            RegistrationComboHelper.UpdateLabelForSelection(registrationIdLabel, registrationComboBox)
            If registrationId = lastSelectedRegistrationId Then
                Return
            End If

            lastSelectedRegistrationId = registrationId
            ClearBrowseGridForPendingQuery()
            PerformFindAfterRegistrationChange()
        End Sub

        Private Sub PerformFindAfterRegistrationChange()
            If findButton IsNot Nothing AndAlso findButton.Enabled Then
                findButton.PerformClick()
            End If
        End Sub

        Private Sub InitializeEmptyBrowseState()
            LayoutQbeSection()
            browseGrid.DataSource = New DataTable()
            recordCountLabel.Text = "Record Count: 0"

            Dim activeRegistrationId As Integer
            If TryGetActiveRegistrationId(activeRegistrationId) Then
                PopulateQbeFromSqlSchema(activeRegistrationId)
            End If

            UpdateLayoutUiAvailability()
            UpdateShowDeletedButtonState()
        End Sub

        Private Sub FindButton_Click(sender As Object, e As EventArgs)
            Dim selectedId = SelectedRecordId()
            Dim registrationId = GetRegistrationIdForQbeFind()
            If registrationId <= 0 Then
                SetRetrievalStatus("Select a registration first.", True)
                Return
            End If

            Dim filters As Dictionary(Of String, String) = Nothing
            Dim validationMessage As String = String.Empty
            If Not TryBuildFiltersFromQbe(filters, validationMessage) Then
                SetRetrievalStatus(validationMessage, True)
                Return
            End If

            currentFilters = filters
            UpdateActiveFilterLabel()
            Dim emptyQbeLimit = If(qbeGrid.Rows.Count > 0 AndAlso filters.Count = 0, GetEmptyQbeRowLimit(), 0)
            If emptyQbeLimit > 0 Then
                RefreshGrid(selectedId, False, emptyQbeLimit, registrationId)
                If lastRefreshExceededRowLimit Then
                    SetRetrievalStatus("Only showing the top " & emptyQbeLimit & " records. Enter at least one QBE criterion to see more.", False, True)
                ElseIf browseGrid.Rows.Count > 0 Then
                    SetRetrievalStatus("Retrieved " & browseGrid.Rows.Count & " record(s).", False)
                End If
                If browseGrid.Rows.Count = 0 Then
                    SetRetrievalStatus("No records found.", False)
                End If
                Return
            End If

            RefreshGrid(selectedId, False, emptyQbeLimit, registrationId)
            SetRetrievalStatus(If(browseGrid.Rows.Count = 0,
                                  "No records found.",
                                  "Retrieved " & browseGrid.Rows.Count & " record(s)."),
                               False)
        End Sub

        Private Function GetRegistrationIdForQbeFind() As Integer
            Dim registrationId As Integer = 0
            If RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId) Then
                Return registrationId
            End If

            LoadRegistrationCombo()
            If RegistrationComboHelper.TryGetSelectedId(registrationComboBox, registrationId) Then
                Return registrationId
            End If

            Return 0
        End Function

        Private Sub ClearFiltersButton_Click(sender As Object, e As EventArgs)
            ResetQbeAndDeletedState()
            ClearBrowseGridForPendingQuery()
            ClearRetrievalStatus()
        End Sub

        Private Sub SetRetrievalStatus(message As String, isError As Boolean, Optional flashRed As Boolean = False)
            retrievalStatusLabel.Text = If(message, String.Empty).Trim().ToUpperInvariant()
            retrievalStatusLabel.ForeColor = Color.Red
            retrievalStatusLabel.Visible = retrievalStatusLabel.Text <> String.Empty
            retrievalStatusFlashRed = flashRed
            retrievalStatusFlashTimer.Stop()
            retrievalStatusFlashStep = 0
            If retrievalStatusLabel.Visible Then
                retrievalStatusFlashTimer.Interval = 400
                retrievalStatusFlashTimer.Start()
            End If
        End Sub

        Private Sub ClearRetrievalStatus()
            retrievalStatusFlashTimer.Stop()
            retrievalStatusLabel.Text = String.Empty
            retrievalStatusLabel.Visible = False
        End Sub

        Private retrievalStatusFlashStep As Integer

        Private Sub RetrievalStatusFlashTimer_Tick(sender As Object, e As EventArgs)
            Select Case retrievalStatusFlashStep
                Case 0
                    retrievalStatusLabel.ForeColor = Color.Red
                    retrievalStatusLabel.Visible = False
                    retrievalStatusFlashTimer.Interval = 150
                    retrievalStatusFlashStep = 1
                Case 1
                    retrievalStatusLabel.ForeColor = Color.Red
                    retrievalStatusLabel.Visible = True
                    retrievalStatusFlashTimer.Interval = 400
                    retrievalStatusFlashStep = 2
                Case 2
                    retrievalStatusLabel.Visible = False
                    retrievalStatusFlashTimer.Interval = 150
                    retrievalStatusFlashStep = 3
                Case Else
                    retrievalStatusLabel.ForeColor = Color.Black
                    retrievalStatusLabel.Visible = True
                    retrievalStatusFlashTimer.Stop()
            End Select
        End Sub

        Private Sub FW_Base_B_FormClosed(sender As Object, e As FormClosedEventArgs)
            retrievalStatusFlashTimer.Stop()
            retrievalStatusFlashTimer.Dispose()
        End Sub

        Private Sub ShowDeletedButton_Click(sender As Object, e As EventArgs)
            If Not showDeletedButton.Visible OrElse Not showDeletedButton.Enabled Then
                Return
            End If

            ClearQbeFilters()
            showDeletedRecordsOnly = True
            UpdateShowDeletedButtonState()

            Dim selectedId = SelectedRecordId()
            RefreshGrid(selectedId, True)
        End Sub

        Private Sub ShowNormalButton_Click(sender As Object, e As EventArgs)
            If Not showNormalButton.Visible Then
                Return
            End If

            ClearQbeFilters()
            showDeletedRecordsOnly = False
            UpdateShowDeletedButtonState()

            Dim selectedId = SelectedRecordId()
            RefreshGrid(selectedId, True)
        End Sub

        Protected Overridable Function TryBuildFiltersFromQbe(ByRef filters As Dictionary(Of String, String), ByRef validationMessage As String) As Boolean
            Dim builtFilters As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            validationMessage = String.Empty
            Dim errors As New List(Of String)()

            For Each row As DataGridViewRow In qbeGrid.Rows
                If row Is Nothing Then
                    Continue For
                End If

                Dim fieldObj = row.Cells("FieldName").Value
                Dim valueObj = row.Cells("FieldValue").Value
                If fieldObj Is Nothing Then
                    Continue For
                End If

                Dim fieldName = fieldObj.ToString().Trim()
                Dim operatorObj = row.Cells("Operator").Value
                Dim filterValue = If(valueObj, String.Empty).ToString().Trim()
                Dim comparisonOperator = ParseOperatorValue(operatorObj)
                Dim fieldDefinition = GetFieldDefinition(fieldName)

                If filterValue = String.Empty Then
                    Continue For
                End If

                If fieldDefinition Is Nothing Then
                    errors.Add("Unknown field: " & fieldName)
                    Continue For
                End If

                If Not IsOperatorAllowedForField(fieldDefinition.FieldKind, comparisonOperator) Then
                    errors.Add(fieldDefinition.DisplayName & " does not support the selected operator.")
                    Continue For
                End If

                Select Case fieldDefinition.FieldKind
                    Case QbeFieldKind.NumericField
                        Dim parsedInt As Integer
                        If Not Integer.TryParse(filterValue, parsedInt) Then
                            errors.Add(fieldDefinition.DisplayName & " must be a whole number.")
                            Continue For
                        End If
                    Case QbeFieldKind.BooleanField
                        Dim normalized = filterValue.ToLowerInvariant()
                        If normalized <> "1" AndAlso normalized <> "0" AndAlso normalized <> "true" AndAlso normalized <> "false" AndAlso normalized <> "yes" AndAlso normalized <> "no" AndAlso normalized <> "y" AndAlso normalized <> "n" Then
                            errors.Add(fieldDefinition.DisplayName & " must be one of: true/false, yes/no, 1/0.")
                            Continue For
                        End If
                End Select

                builtFilters(fieldName & "|" & comparisonOperator.ToString()) = filterValue
            Next

            If errors.Count > 0 Then
                validationMessage = String.Join(Environment.NewLine, errors)
                filters = Nothing
                Return False
            End If

            filters = builtFilters
            Return True
        End Function

        Protected Overridable Sub ClearQbeFilters()
            If qbeGrid IsNot Nothing Then
                For Each row As DataGridViewRow In qbeGrid.Rows
                    row.Cells("FieldValue").Value = String.Empty
                Next
            End If

            currentFilters = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            UpdateActiveFilterLabel()
        End Sub

        Private Sub ResetQbeAndDeletedState()
            ClearQbeFilters()
            showDeletedRecordsOnly = False
            UpdateShowDeletedButtonState()
        End Sub

        Private Sub UpdateShowDeletedButtonState()
            Dim hasHydratedDeletedFlag = DeletedViewGuard.ResultHasDeletedFlagColumn(browseGrid)
            Dim hasResultKey = HasRecordKeyColumn() OrElse ResultHasMaintenanceKeyColumn() OrElse ActiveSqlContainsMaintenanceKeyAlias()
            Dim supportsDeletedView As Boolean = SupportsCurrentDeletedView() AndAlso (hasHydratedDeletedFlag OrElse hasResultKey)
            If Not supportsDeletedView Then
                showDeletedRecordsOnly = False
            End If

            Dim isAdmin = IsAppAdminSession() OrElse IsCompanyAdminSession()
            Dim canUpdateDeletedRecords = Not UseRoleBasedCrudAccess() OrElse
                                          (accessProfile IsNot Nothing AndAlso
                                           accessProfile.Can(accessTableName, AccessCapability.Update))
            Dim qbeOnlyPage = OnlyUseQbe()
            Dim canUseDeletedView = isAdmin AndAlso canUpdateDeletedRecords AndAlso supportsDeletedView AndAlso Not qbeOnlyPage
            Dim showSplitDeletedActions = canUseDeletedView AndAlso showDeletedRecordsOnly
            showDeletedButton.Visible = canUseDeletedView AndAlso Not showSplitDeletedActions
            showDeletedButton.Enabled = canUseDeletedView
            showDeletedButton.Text = If(supportsDeletedView, ShowDeletedText, "Deleted N/A")

            Dim previousRestoreVisible = restoreButton.Visible
            Dim previousShowNormalVisible = showNormalButton.Visible

            restoreButton.Visible = showSplitDeletedActions
            showNormalButton.Visible = showSplitDeletedActions

            Dim shouldReposition = (previousRestoreVisible <> restoreButton.Visible) OrElse
                                  (previousShowNormalVisible <> showNormalButton.Visible)
            If shouldReposition Then
                LayoutQbeSection()
            End If

            restoreButton.Enabled = True
            showNormalButton.Enabled = showSplitDeletedActions

            ApplyCrudVisibilityForCurrentState()
        End Sub

        Private Sub ApplyCrudVisibilityForCurrentState()
            If Not UseRoleBasedCrudAccess() Then
                createButton.Visible = Not OnlyUseQbe() AndAlso Not showDeletedRecordsOnly
                readButton.Visible = Not OnlyUseQbe() AndAlso Not showDeletedRecordsOnly AndAlso Not missingMaintenancePkInResult
                updateButton.Visible = Not OnlyUseQbe() AndAlso Not showDeletedRecordsOnly AndAlso Not missingMaintenancePkInResult
                deleteButton.Visible = Not OnlyUseQbe() AndAlso Not showDeletedRecordsOnly AndAlso Not missingMaintenancePkInResult
            ElseIf accessProfile Is Nothing Then
                createButton.Visible = False
                readButton.Visible = False
                updateButton.Visible = False
                deleteButton.Visible = False
            Else
                createButton.Visible = Not OnlyUseQbe() AndAlso accessProfile.Can(accessTableName, AccessCapability.Create) AndAlso Not showDeletedRecordsOnly
                readButton.Visible = Not OnlyUseQbe() AndAlso accessProfile.Can(accessTableName, AccessCapability.Read) AndAlso Not showDeletedRecordsOnly AndAlso Not missingMaintenancePkInResult
                updateButton.Visible = Not OnlyUseQbe() AndAlso accessProfile.Can(accessTableName, AccessCapability.Update) AndAlso Not showDeletedRecordsOnly AndAlso Not missingMaintenancePkInResult
                deleteButton.Visible = Not OnlyUseQbe() AndAlso accessProfile.Can(accessTableName, AccessCapability.Delete) AndAlso Not showDeletedRecordsOnly AndAlso Not missingMaintenancePkInResult
            End If

            createButton.Enabled = True
            readButton.Enabled = True
            updateButton.Enabled = True
            deleteButton.Enabled = True
            restoreButton.Enabled = True
        End Sub

        Private Function SupportsCurrentDeletedView() As Boolean
            If CurrentTableSupportsDeletedView() Then
                Return True
            End If

            Return DeletedViewGuard.TableSupportsDeletedView(accessTableName)
        End Function

        Private Function ActiveSqlContainsMaintenanceKeyAlias() As Boolean
            Dim activeSql = GetActiveBaseSql()
            Return Not String.IsNullOrWhiteSpace(activeSql) AndAlso
                   Regex.IsMatch(activeSql, "\bAS\s+\[?PK\]?\b", RegexOptions.IgnoreCase)
        End Function

        Private Function ResultHasMaintenanceKeyColumn() As Boolean
            Dim sourceTable = TryCast(browseGrid.DataSource, DataTable)
            If sourceTable Is Nothing OrElse sourceTable.Columns Is Nothing Then
                Return False
            End If

            Return sourceTable.Columns.Contains("PK")
        End Function

        Private Sub UpdateMaintenanceKeyAvailability()
            Dim hasRecordKey = HasRecordKeyColumn()
            missingMaintenancePkInResult = Not hasRecordKey

            If pendingInitialMissingPkWarning Then
                Return
            End If

            MaintenanceKeyGuard.UpdateAvailabilityAndMaybeWarn(missingMaintenancePkInResult,
                                                               missingMaintenancePkWarningShown,
                                                               Me)
        End Sub

        Private Function EnsureMaintenanceKeyAvailable(actionName As String) As Boolean
            Return MaintenanceKeyGuard.EnsureAvailable(missingMaintenancePkInResult, actionName, Me)
        End Function

        Protected Overridable Function CurrentTableSupportsDeletedView() As Boolean
            Dim tableName = ResolveCurrentRoleFieldTableName()
            If String.IsNullOrWhiteSpace(tableName) Then
                tableName = currentDbTableName
            End If

            Return DeletedViewGuard.TableSupportsDeletedView(tableName)
        End Function

        Protected Overridable Sub UpdateActiveFilterLabel()
            If currentFilters Is Nothing OrElse currentFilters.Count = 0 Then
                activeFilterLabel.Text = "Active Filter: (none)"
                Return
            End If

            Dim parts As New List(Of String)()
            For Each kvp In currentFilters
                Dim pieces = kvp.Key.Split("|"c)
                Dim fieldName = pieces(0)
                Dim operatorText = If(pieces.Length > 1, pieces(1), QbeComparisonOperator.EqualsTo.ToString())
                parts.Add(fieldName & " " & operatorText & " """ & kvp.Value & """")
            Next

            activeFilterLabel.Text = "Active Filter: " & String.Join("; ", parts)
        End Sub

        Protected Overridable Sub InitializeQbeOperatorCell(rowIndex As Integer, fieldDefinition As QbeFieldDefinition)
            Dim row = qbeGrid.Rows(rowIndex)
            Dim operatorCell = TryCast(row.Cells("Operator"), DataGridViewComboBoxCell)
            If operatorCell Is Nothing Then
                Return
            End If

            ConfigureOperatorCellItems(operatorCell, fieldDefinition.FieldKind)
            operatorCell.Value = GetDefaultOperator(fieldDefinition.FieldKind).ToString()
        End Sub

        Private Sub ConfigureQbeOperatorsForAllRows()
            For Each row As DataGridViewRow In qbeGrid.Rows
                If row Is Nothing OrElse row.IsNewRow Then
                    Continue For
                End If

                Dim fieldNameObj = row.Cells("FieldName").Value
                If fieldNameObj Is Nothing Then
                    Continue For
                End If

                Dim fieldDefinition = GetFieldDefinition(fieldNameObj.ToString())
                If fieldDefinition Is Nothing Then
                    Continue For
                End If

                Dim operatorCell = TryCast(row.Cells("Operator"), DataGridViewComboBoxCell)
                If operatorCell Is Nothing Then
                    Continue For
                End If

                ConfigureOperatorCellItems(operatorCell, fieldDefinition.FieldKind)
                operatorCell.Value = GetDefaultOperator(fieldDefinition.FieldKind).ToString()
            Next
        End Sub

        Protected Overridable Sub ConfigureOperatorCellItems(operatorCell As DataGridViewComboBoxCell, fieldKind As QbeFieldKind)
            operatorCell.Items.Clear()

            For Each op In GetAllowedOperators(fieldKind)
                operatorCell.Items.Add(op.ToString())
            Next
        End Sub

        Protected Overridable Function GetDefaultOperator(fieldKind As QbeFieldKind) As QbeComparisonOperator
            Select Case fieldKind
                Case QbeFieldKind.NumericField, QbeFieldKind.BooleanField, QbeFieldKind.DateField
                    Return QbeComparisonOperator.EqualsTo
                Case Else
                    Return QbeComparisonOperator.Contains
            End Select
        End Function

        Protected Overridable Function GetAllowedOperators(fieldKind As QbeFieldKind) As IEnumerable(Of QbeComparisonOperator)
            Select Case fieldKind
                Case QbeFieldKind.NumericField, QbeFieldKind.DateField
                    Return New QbeComparisonOperator() {
                        QbeComparisonOperator.EqualsTo,
                        QbeComparisonOperator.NotEquals,
                        QbeComparisonOperator.GreaterThan,
                        QbeComparisonOperator.GreaterThanOrEqual,
                        QbeComparisonOperator.LessThan,
                        QbeComparisonOperator.LessThanOrEqual
                    }
                Case QbeFieldKind.BooleanField
                    Return New QbeComparisonOperator() {
                        QbeComparisonOperator.EqualsTo,
                        QbeComparisonOperator.NotEquals
                    }
                Case Else
                    Return New QbeComparisonOperator() {
                        QbeComparisonOperator.EqualsTo,
                        QbeComparisonOperator.NotEquals,
                        QbeComparisonOperator.Contains,
                        QbeComparisonOperator.StartsWith,
                        QbeComparisonOperator.EndsWith
                    }
            End Select
        End Function

        Protected Overridable Function GetFieldDefinition(fieldName As String) As QbeFieldDefinition
            If String.IsNullOrWhiteSpace(fieldName) OrElse qbeFieldDefinitions Is Nothing Then
                Return Nothing
            End If

            For Each fieldDefinition In qbeFieldDefinitions
                If String.Equals(fieldDefinition.FieldName, fieldName, StringComparison.OrdinalIgnoreCase) Then
                    Return fieldDefinition
                End If
            Next

            Return Nothing
        End Function

        Protected Overridable Function ParseOperatorValue(operatorObj As Object) As QbeComparisonOperator
            If operatorObj Is Nothing Then
                Return QbeComparisonOperator.EqualsTo
            End If

            Dim raw = operatorObj.ToString()
            Dim parsed As QbeComparisonOperator
            If [Enum].TryParse(raw, True, parsed) Then
                Return parsed
            End If

            Return QbeComparisonOperator.EqualsTo
        End Function

        Protected Overridable Function IsOperatorAllowedForField(fieldKind As QbeFieldKind, comparisonOperator As QbeComparisonOperator) As Boolean
            Return GetAllowedOperators(fieldKind).Contains(comparisonOperator)
        End Function

        Private Sub BrowseGrid_SelectionChanged(sender As Object, e As EventArgs)
            NotifyBrowseSelectionChanged()
        End Sub

        Private Sub BrowseGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then
                Return
            End If

            browseGrid.ClearSelection()
            browseGrid.Rows(e.RowIndex).Selected = True
            browseGrid.CurrentCell = browseGrid.Rows(e.RowIndex).Cells(e.ColumnIndex)

            If updateButton.Visible AndAlso updateButton.Enabled Then
                updateButton.PerformClick()
            End If
        End Sub

        Private Sub SaveQbeButton_Click(sender As Object, e As EventArgs)
            Dim activeSession = SessionState.Current
            If Not activeSession.HasValue Then Return

            Dim qbeData = SerializeQbeFromGrid()
            If String.IsNullOrWhiteSpace(qbeData) Then
                MessageBox.Show("No filter criteria are set to save.", "Save QBE",
                    MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then Return

            Using dlg As New SaveQbeDialog()
                If dlg.ShowDialog(Me) = DialogResult.OK Then
                    Try
                        Dim record As New SavedQbeRecord With {
                            .RegistrationID = registrationId,
                            .UserID = activeSession.Value.UserID,
                            .QbeName = dlg.QbeName,
                            .IsCompanyWide = dlg.IsCompanyWide,
                            .TableContext = Me.Text,
                            .QbeData = qbeData
                        }
                        DataAccess.SaveQbe(record)
                        MessageBox.Show("QBE saved as """ & dlg.QbeName & """.", "Save QBE",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Catch ex As Exception
                        MessageBox.Show("Save failed: " & ex.Message, "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
                    End Try
                End If
            End Using
        End Sub

        Private Sub RetrieveQbeButton_Click(sender As Object, e As EventArgs)
            Dim activeSession = SessionState.Current
            If Not activeSession.HasValue Then Return

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then Return

            Try
                Dim saved = DataAccess.GetSavedQbes(registrationId, activeSession.Value.UserID, Me.Text)
                If saved.Count = 0 Then
                    SetRetrievalStatus("No saved QBE filters found for this listing.", False)
                    Return
                End If

                Using dlg As New RetrieveQbeDialog(saved, activeSession.Value.UserID)
                    If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.SelectedRecord IsNot Nothing Then
                        ApplyQbeDataToGrid(dlg.SelectedRecord.QbeData)
                        Dim filters As Dictionary(Of String, String) = Nothing
                        Dim validationMessage As String = String.Empty
                        If TryBuildFiltersFromQbe(filters, validationMessage) Then
                            currentFilters = filters
                            UpdateActiveFilterLabel()
                            Dim selectedId = SelectedRecordId()
                            RefreshGrid(selectedId)
                            SetRetrievalStatus(If(browseGrid.Rows.Count = 0,
                                                  "No records found.",
                                                  "Retrieved " & browseGrid.Rows.Count & " record(s)."),
                                               False)
                        End If
                    End If
                End Using
            Catch ex As Exception
                MessageBox.Show("Retrieve failed: " & ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Protected Overridable Function SerializeQbeFromGrid() As String
            Dim lines As New List(Of String)()
            For Each row As DataGridViewRow In qbeGrid.Rows
                Dim fieldObj = row.Cells("FieldName").Value
                If fieldObj Is Nothing Then Continue For
                Dim fieldName = fieldObj.ToString().Trim()
                Dim valueObj = row.Cells("FieldValue").Value
                Dim filterValue = If(valueObj, String.Empty).ToString().Trim()
                If filterValue = String.Empty Then Continue For
                Dim operatorObj = row.Cells("Operator").Value
                Dim operatorStr = If(operatorObj IsNot Nothing, operatorObj.ToString(), QbeComparisonOperator.EqualsTo.ToString())
                lines.Add(fieldName & "|" & operatorStr & Chr(9) & filterValue)
            Next
            Return String.Join(vbLf, lines)
        End Function

        Protected Overridable Sub ApplyQbeDataToGrid(data As String)
            For Each row As DataGridViewRow In qbeGrid.Rows
                row.Cells("FieldValue").Value = String.Empty
            Next

            If String.IsNullOrWhiteSpace(data) Then Return

            Dim lines = data.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            For Each line In lines
                Dim tabIdx = line.IndexOf(Chr(9))
                If tabIdx < 1 Then Continue For
                Dim keyPart = line.Substring(0, tabIdx)
                Dim valuePart = line.Substring(tabIdx + 1)

                Dim pipeIdx = keyPart.LastIndexOf("|"c)
                If pipeIdx < 1 Then Continue For
                Dim fieldName = keyPart.Substring(0, pipeIdx)
                Dim operatorStr = keyPart.Substring(pipeIdx + 1)

                For Each gridRow As DataGridViewRow In qbeGrid.Rows
                    Dim rowField = gridRow.Cells("FieldName").Value
                    If rowField IsNot Nothing AndAlso
                       String.Equals(rowField.ToString(), fieldName, StringComparison.OrdinalIgnoreCase) Then
                        Try
                            gridRow.Cells("Operator").Value = operatorStr
                        Catch
                        End Try
                        gridRow.Cells("FieldValue").Value = valuePart
                        Exit For
                    End If
                Next
            Next
        End Sub

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub

        Private Sub BrowsePage_FormClosing(sender As Object, e As FormClosingEventArgs)
            ' Central close pipeline for both Close button and window X.
            Try
                If Not hasBaselineLayoutSnapshot Then
                    Return
                End If

                Dim currentSnapshot = BuildCurrentLayoutSnapshotJson()
                If String.IsNullOrWhiteSpace(currentSnapshot) Then
                    Return
                End If

                Dim session = SessionState.Current
                If Not session.HasValue Then
                    Return
                End If

                Dim registrationId = GetRegistrationIdForCaptions()
                If registrationId <= 0 Then
                    Return
                End If

                Dim pageName = Me.GetType().Name
                Dim tableName = ResolveCurrentRoleFieldTableName()
                Dim userId = session.Value.UserID
                Dim existingLastUsed = DataAccess.GetTableLayoutJson(registrationId,
                                                                     userId,
                                                                     pageName,
                                                                     tableName,
                                                                     "LastUsed",
                                                                     "Last Used")
                Dim layoutChanged = userChangedLayout OrElse
                                    Not String.Equals(currentSnapshot,
                                                      baselineLayoutSnapshot,
                                                      StringComparison.Ordinal)
                If Not layoutChanged AndAlso Not String.IsNullOrWhiteSpace(existingLastUsed) Then
                    Return
                End If

                DataAccess.UpsertTableLayout(registrationId,
                                             userId,
                                             pageName,
                                             tableName,
                                             "LastUsed",
                                             "Last Used",
                                             currentSnapshot,
                                             userId)
            Catch
                ' Ignore close persistence errors to avoid blocking form close.
            End Try
        End Sub
    End Class
End Namespace



