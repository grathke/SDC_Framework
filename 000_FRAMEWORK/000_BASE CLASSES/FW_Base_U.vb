Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Windows.Forms

Namespace SDC.Framework

    Public MustInherit Class FW_Base_U
        Inherits Form

        ''' <summary>
        ''' The two colours that say a field is required, and the only place either value lives.
        '''
        ''' Blue means App Admin required - declared on the page, so it applies to every role.
        ''' Yellow means permission required, from FW_RoleFields. Blue wins where both apply.
        '''
        ''' One owner matters more here than almost anywhere else, because the blue is not only
        ''' painted but *read back*: ShouldSkipBrRequiredStyling decides App Admin ownership by
        ''' comparing a label's BackColor to this exact value. A paint site changed without the
        ''' comparison would silently invert the precedence rather than fail.
        '''
        ''' Note the same light blue is used elsewhere for entirely different things - the browse
        ''' grid header and the chosen tile in Page Generation. Those are not signals and must not
        ''' follow this value.
        ''' </summary>
        Public Shared ReadOnly AppAdminRequiredBackColor As Color = Color.FromArgb(168, 201, 237)
        Public Shared ReadOnly PermissionRequiredBackColor As Color = Color.FromArgb(250, 236, 155)

        Protected loading As Boolean
        Protected hasUnsavedChanges As Boolean
        Private originalRowVersion As Byte()
        Private baselineControlSnapshotJson As String = String.Empty
        Private baselineRecordSnapshotJson As String = String.Empty
        Private bypassCancelCloseCheck As Boolean
        Private ReadOnly requiredBorderPanels As New Dictionary(Of Control, Panel)()

        ''' <summary>
        ''' Required fields the user has actually been in. A required field only turns red once it
        ''' has been entered or left while empty - never merely because the page opened on a blank
        ''' record. The focus the page sets for itself does not count, hence suppressRequiredTouch.
        ''' </summary>
        Private ReadOnly touchedRequiredControls As New HashSet(Of Control)()
        Private suppressRequiredTouch As Boolean
        Private ReadOnly focusOriginalBackColors As New Dictionary(Of Control, Color)()
        Private ReadOnly focusBorderPanels As New Dictionary(Of Control, Panel)()
        Private ReadOnly readOnlyMouseHandled As New HashSet(Of Control)()
        Protected ReadOnly okButton As Button
        Protected ReadOnly cancelActionButton As Button
        Private tabOrderToggleButton As Button


        ''' <summary>
        ''' Parks the page out of sight until a remembered zoom can be applied to it.
        '''
        ''' The zoom cannot be applied here. CollapseHiddenFieldRows runs after Shown and moves the
        ''' rows up and shrinks the window, so a snapshot taken at Load is of a taller page than the
        ''' one anybody sees - and a saved 1.3 applied to it came back looking like 1.4. The snapshot
        ''' has to wait for the collapse, and the page waits off-screen until then rather than being
        ''' seen at normal size and then jumping. PageZoom owns how the parking and the reveal are
        ''' done, since FW_Base_B needs the same thing for the same reason.
        ''' </summary>
        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            PageZoom.ParkIfZoomPending(Me)
        End Sub

        ''' <summary>Attaches the zoom once the page has finished laying itself out.</summary>
        Private Sub AttachZoomAfterLayout()
            PageZoom.AttachAndReveal(Me)
        End Sub

        ''' <summary>
        ''' Lines the header buttons up with Cancel, so the page has one right-hand edge.
        ''' </summary>
        ''' <remarks>
        ''' HelpDeskLauncher places its button against the window, which is right for a page whose
        ''' content runs to the window. A maintenance page's content ends at its Cancel button, so
        ''' measuring from the window left the button floating past everything else on the form.
        '''
        ''' Tab Order moves with it rather than staying where it was. It has always been positioned
        ''' relative to the Help Desk button - through HelpDeskLauncher.ReservedWidth - and the two
        ''' are a pair; leaving one behind would open a gap where the other used to be.
        '''
        ''' Run from OnShown because the derived page places Cancel in its own constructor, after
        ''' this class has finished building. Reading it any earlier reads where it used to be.
        ''' </remarks>
        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            AlignHeaderButtonsToCancel()
        End Sub

        Private Sub AlignHeaderButtonsToCancel()
            If cancelActionButton Is Nothing Then
                Return
            End If

            Dim matches = Controls.Find(HelpDeskLauncher.ButtonName, True)
            If matches.Length = 0 Then
                Return
            End If

            Dim helpDeskButton = matches(0)
            helpDeskButton.Left = Math.Max(0, cancelActionButton.Right - helpDeskButton.Width)

            If tabOrderToggleButton IsNot Nothing AndAlso tabOrderToggleButton.Visible Then
                tabOrderToggleButton.Left = Math.Max(0, helpDeskButton.Left - tabOrderToggleButton.Width - 12)
            End If
        End Sub
        Private tabOrderPanel As Panel
        Private tabOrderList As CheckedListBox
        Private tabOrderUpButton As Button
        Private tabOrderDownButton As Button
        Private tabOrderSaveButton As Button
        Private tabOrderHideButton As Button
        Private tabOrderBaselineOrder As List(Of TabOrderManagerItem)
        Private tabOrderBaselineTabStops As Dictionary(Of Control, Boolean)
        Private tabOrderCommitInProgress As Boolean
        Private loadingTabOrderManager As Boolean
        Private saveFailureAlreadyReported As Boolean
        Private tabOrderCheckClickIndex As Integer = -1
        Private tabOrderPanelDragging As Boolean
        Private tabOrderPanelDragStart As Point
        Private tabOrderPanelLocationStart As Point
        Private pageCaptionApplied As Boolean
        Private Shared ReadOnly TabOrderCollapsedText As String = "Tab Order " & ChrW(&H25BC)
        Private Shared ReadOnly TabOrderExpandedText As String = "Tab Order " & ChrW(&H25B2)

        Protected Sub New()
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False

            okButton = New Button() With {
                .Text = OkButtonText(),
                .Location = New Point(330, 500),
                .Size = New Size(120, 36)
            }
            cancelActionButton = New Button() With {
                .Text = "Cancel",
                .Location = New Point(465, 500),
                .Size = New Size(120, 36)
            }

            AddHandler okButton.Click, AddressOf OkButton_Click
            AddHandler cancelActionButton.Click, AddressOf CancelButton_Click
            AddHandler Me.FormClosing, AddressOf FW_Base_U_FormClosing
            AddHandler Me.Shown, AddressOf FW_Base_U_Shown

            Me.Controls.Add(okButton)
            Me.Controls.Add(cancelActionButton)
            Me.CancelButton = cancelActionButton

            ' Every maintenance page can raise a report against itself.
            HelpDeskLauncher.Attach(Me, Me.GetType().Name)
        End Sub

        ''' <summary>
        ''' Takes the page background from whatever opened this page, so a maintenance page matches
        ''' the listing it was opened from.
        '''
        ''' The owner is the window that called ShowDialog(Me), which every call site already does -
        ''' so this needs no change at any of them. A page opened from a menu or dashboard has no
        ''' owner to inherit from and falls back to the colour stored for its paired _B page.
        '''
        ''' Only the form's own BackColor is set, and deliberately nothing else. Labels that were
        ''' never given a colour inherit it automatically; the ones that were - blue for App Admin
        ''' required, yellow for permission required - keep theirs, as do the red required borders.
        ''' Tinting labels or panels here, the way a browse page does, would destroy the
        ''' required-field colour contract.
        ''' </summary>
        Private Sub ApplyInheritedPageBackground()
            Try
                Dim source = TryCast(Me.Owner, Form)
                If source IsNot Nothing AndAlso (TypeOf source Is FW_Base_B OrElse TypeOf source Is FW_Base_U) Then
                    Me.BackColor = source.BackColor
                    KeepButtonsUntinted(Me)
                    Return
                End If

                ' No owner to inherit from - opened from a menu or dashboard. Fall back to the colour
                ' stored for the paired _B page, and to the shared default when there is none, so a
                ' maintenance page never sits there white while every listing is Paper.
                Dim stored As Integer? = Nothing
                Dim pageName = GetPageName()
                If pageName.EndsWith("_U", StringComparison.OrdinalIgnoreCase) Then
                    stored = DataAccess.GetPageBackgroundColor(pageName.Substring(0, pageName.Length - 2) & "_B")
                End If

                Me.BackColor = If(stored.HasValue, Color.FromArgb(stored.Value), FW_Base_B.DefaultPageBackground)
                KeepButtonsUntinted(Me)
            Catch
                ' A page colour is decoration. It must never stop a maintenance page opening.
            End Try
        End Sub

        ''' <summary>
        ''' Buttons keep their own look rather than inheriting the page tint, which makes them read
        ''' as disabled. Only themed buttons are touched: a button given a deliberate colour or a
        ''' flat style keeps what it was given.
        ''' </summary>
        Private Shared Sub KeepButtonsUntinted(container As Control)
            If container Is Nothing OrElse container.Controls Is Nothing Then Return

            For Each child As Control In container.Controls
                Dim button = TryCast(child, Button)
                If button IsNot Nothing Then
                    If button.FlatStyle = FlatStyle.Standard OrElse button.FlatStyle = FlatStyle.System Then
                        button.UseVisualStyleBackColor = True
                    End If
                Else
                    KeepButtonsUntinted(child)
                End If
            Next
        End Sub

        Private Sub FW_Base_U_Shown(sender As Object, e As EventArgs)
            ApplyInheritedPageBackground()
            BeginInvoke(New Action(Sub()
                                       ApplySharedPageCaption()
                                       RemoveReadOnlyControlsFromTabOrder(Me)
                                       WireFocusIndicators(Me)
                                       okButton.TabStop = False
                                       cancelActionButton.TabStop = False
                                       CollapseHiddenFieldRows()
                                       RefreshLocalRequiredBorders()
                                       ApplySavedTabOrder()
                                       InitializeTabOrderManager()
                                       AttachZoomAfterLayout()
                                       BeginInvoke(New Action(Sub()
                                                                  SetInitialFieldFocus()
                                                                  ResetPendingRecordBaseline()
                                                              End Sub))
                                   End Sub))
        End Sub

        ''' <summary>
        ''' The page title, decided in one place. The default names what the page is doing and what
        ''' it is doing it to: "New Entity X", "Edit Entity X", "View Entity X". `_B` and `_U` are a
        ''' developer convention and never reach the screen.
        '''
        ''' A page that needs its own title overrides this instead of assigning Me.Text, so there is
        ''' exactly one answer to where a caption comes from: this method, on this page. It mirrors
        ''' BuildBrowseListingTitle on Base_B.
        ''' </summary>
        Protected Overridable Function BuildMaintenanceTitle() As String
            Dim subject = DisplayNameFormatter.ToPageDisplayName(Me.GetType().Name)

            ' What the role calls the table wins over what the page is called, so a company that
            ' renames Users to Staff gets "Edit Staff" here as well as "Staff Listing" on the browse
            ' page and "Staff" on the button that opened it. One override, every surface.
            '
            ' ResolveTableRename rather than the plain override, so an alias that merely restates the
            ' derived name changes nothing: FW_Users is aliased "Users" against four roles, and
            ' applying that would flatten two maintenance pages over the same table to one title while renaming
            ' nothing.
            Dim session = SessionState.Current
            If session.HasValue AndAlso session.Value.RegistrationID > 0 Then
                ' The alias belongs to the _B partner: FW_Pages is keyed by browse page name and has
                ' no rows for maintenance pages. Deriving it here rather than storing a second row
                ' keeps one page name per page - a _U that drifted from its _B would be captioned
                ' from a row nobody knew existed.
                Dim pageAlias = DataAccess.GetPageAliasByWindowOrPage(session.Value.RegistrationID, BrowsePartnerPageName())
                Dim caption = PageTitleHelper.ResolvePageCaption(session.Value.RegistrationID, ResolveTableNameForConstraints(), pageAlias)
                If Not String.IsNullOrWhiteSpace(caption) Then
                    subject = caption
                End If
            End If

            Dim action = If(IsCreatingNewRecord(), "New", If(IsViewOnly(), "View", "Edit"))
            Return action & " " & subject
        End Function

        ''' <summary>
        ''' Re-asks the page for its title. Call this when something the title is built from changes
        ''' while the page is open, such as the selected role.
        ''' </summary>
        Protected Sub RefreshPageCaption()
            Me.Text = BuildMaintenanceTitle()
            Dim titleMatches = Controls.Find("Label_UserTitle", True)
            If titleMatches.Length = 0 Then titleMatches = Controls.Find("Label_PageTitle", True)
            If titleMatches.Length > 0 Then titleMatches(0).Text = Me.Text
        End Sub

        Private Sub ApplySharedPageCaption()
            If pageCaptionApplied Then Return
            pageCaptionApplied = True

            Me.Text = BuildMaintenanceTitle()

            Dim captionLabel As Label = Nothing
            Dim existingTitle = Controls.Find("Label_UserTitle", True)
            If existingTitle.Length > 0 Then
                captionLabel = TryCast(existingTitle(0), Label)
            End If

            If captionLabel IsNot Nothing Then
                captionLabel.Text = Me.Text
                captionLabel.Font = New Font("Segoe UI", 14.0F, FontStyle.Bold)
                captionLabel.Location = New Point(20, 15)
                captionLabel.BringToFront()
                HelpDeskLauncher.AlignToCaption(Me, captionLabel)
                Return
            End If

            If captionLabel Is Nothing Then
                captionLabel = New Label() With {
                    .Name = "Label_PageTitle",
                    .Text = Me.Text,
                    .AutoSize = True
                }
                Controls.Add(captionLabel)
            Else
                captionLabel.Text = Me.Text
            End If

            captionLabel.Font = New Font("Segoe UI", 14.0F, FontStyle.Bold)
            captionLabel.Location = New Point(20, 15)
            captionLabel.BringToFront()

            ' The page's own content moves down to make room for the caption. The header does not:
            ' the Help Desk button belongs to the caption line, and shifting it with the fields put
            ' it a row below the caption on every page that did not bring its own title - which is
            ' every generated page. Tab Order escaped only because it is created after this pass.
            For Each control As Control In Controls
                If control Is captionLabel OrElse control.Location.Y < 0 Then Continue For
                If String.Equals(control.Name, HelpDeskLauncher.ButtonName, StringComparison.Ordinal) Then Continue For
                control.Location = New Point(control.Location.X, control.Location.Y + 42)
            Next

            ClientSize = New Size(ClientSize.Width, ClientSize.Height + 42)

            ' Aligned after the shift, so it centres on where the caption actually ended up.
            HelpDeskLauncher.AlignToCaption(Me, captionLabel)
        End Sub

        ' â”€â”€ Hidden-field row collapse â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        ''' <summary>
        ''' Vertical tolerance for treating two controls as being on the same row. A label sits
        ''' a few pixels below the top of its text box, so an exact Top match is not usable.
        ''' Must stay well under the 42px row pitch the pages lay out on.
        ''' </summary>
        Private Const FieldRowTolerance As Integer = 12

        Private Shared ReadOnly FieldControlPrefixes As String() =
            {"Label_", "TextBox_", "ComboBox_", "CheckBox_", "DateTimePicker_",
             "NumericUpDown_", "MaskedTextBox_", "RichTextBox_"}

        ''' <summary>Clearance kept between a row being pulled up and whatever sits above it.</summary>
        Private Const FieldRowClearance As Integer = 6

        ''' <summary>
        ''' Left edge of each layout column, ascending, for pages laid out in more than one column.
        ''' The default - Nothing - treats the page as a single column, so whole rows collapse
        ''' across the full width and no existing page changes behavior.
        '''
        ''' Declaring columns lets each column close its own gaps, so hiding a left-hand field is
        ''' no longer held up by the right-hand field that happens to share its row.
        ''' </summary>
        Protected Overridable Function GetLayoutColumnLefts() As Integer()
            Return Nothing
        End Function

        ''' <summary>
        ''' Closes the vertical gap left by fields hidden through Make_Invisible on FW_RoleFields.
        '''
        ''' Rows are the unit of movement, never individual controls, so a row that mixes hidden
        ''' and visible fields stays exactly where it is. Within a column the rows below a fully
        ''' hidden row move up by its pitch; a row is held back if rising would run it into a
        ''' control in another column, and the rows already moved above it stay moved.
        '''
        ''' Runs on Shown rather than during BindToForm because Control.Visible reports False for
        ''' every child until the form itself is displayed.
        '''
        ''' Single owner of this behavior. Do not reposition controls page-locally.
        ''' </summary>
        Protected Sub CollapseHiddenFieldRows()
            Dim candidates = Controls.Cast(Of Control)().
                Where(Function(control) IsRowLayoutControl(control)).
                OrderBy(Function(control) control.Location.Y).ToList()
            If candidates.Count = 0 Then Return

            Dim fieldControls = candidates.Where(Function(control) IsFieldControl(control)).ToList()
            If fieldControls.Count = 0 Then Return

            ' Controls with no field on their row - grids, section headings, transfer buttons -
            ' are not part of the field grid. They follow it up as one block instead.
            Dim fieldArea = candidates.Where(Function(control) BelongsToFieldGrid(control, fieldControls)).ToList()
            Dim trailing = candidates.Where(Function(control) Not BelongsToFieldGrid(control, fieldControls)).ToList()

            Dim visibleArea = fieldArea.Where(Function(control) control.Visible).ToList()
            If visibleArea.Count = 0 Then Return

            Dim columnLefts = NormalizeColumnLefts(GetLayoutColumnLefts())
            Dim columnOf = fieldArea.ToDictionary(
                Function(control) control,
                Function(control) ResolveColumnIndex(control, fieldControls, columnLefts))

            SuspendLayout()

            Try
                Dim originalBottom = visibleArea.Max(Function(control) control.Bottom)

                For Each column In fieldArea.GroupBy(Function(control) columnOf(control))
                    Dim obstacles = fieldArea.
                        Where(Function(control) control.Visible AndAlso columnOf(control) <> column.Key).ToList()
                    ShiftColumn(column.OrderBy(Function(control) control.Location.Y).ToList(), obstacles)
                Next

                Dim trailingShift = originalBottom - visibleArea.Max(Function(control) control.Bottom)
                If trailingShift <= 0 Then Return

                For Each control In trailing
                    control.Location = New Point(control.Location.X, control.Location.Y - trailingShift)
                Next

                For Each actionButton As Control In New Control() {okButton, cancelActionButton}
                    If actionButton Is Nothing Then Continue For
                    actionButton.Location = New Point(actionButton.Location.X, actionButton.Location.Y - trailingShift)
                Next

                ClientSize = New Size(ClientSize.Width, Math.Max(200, ClientSize.Height - trailingShift))
            Finally
                ResumeLayout(True)
            End Try
        End Sub

        ''' <summary>
        ''' Moves one column's rows up over its own hidden rows, clamped so nothing collides with
        ''' another column. A blocked row stops the column there: the rows below it would only run
        ''' into the blocked row itself.
        ''' </summary>
        Private Shared Sub ShiftColumn(columnControls As List(Of Control), obstacles As List(Of Control))
            Dim rows = BuildRows(columnControls)
            Dim carryShift As Integer = 0

            For rowIndex = 0 To rows.Count - 1
                Dim currentRow = rows(rowIndex)

                If IsCollapsibleRow(currentRow) Then
                    ' Consume the pitch to the next row so the rows below keep their spacing.
                    ' The last row has nothing following it, so it contributes no shift.
                    If rowIndex < rows.Count - 1 Then
                        carryShift += RowTop(rows(rowIndex + 1)) - RowTop(currentRow)
                    End If
                    Continue For
                End If

                Dim rowShift = Math.Min(carryShift, MaxShiftWithoutCollision(currentRow, obstacles))
                If rowShift < 0 Then rowShift = 0

                If rowShift > 0 Then
                    For Each control In currentRow
                        control.Location = New Point(control.Location.X, control.Location.Y - rowShift)
                    Next
                End If

                carryShift = rowShift
            Next
        End Sub

        ''' <summary>
        ''' How far a row can rise before one of its controls would touch a control in another
        ''' column. Integer.MaxValue when the row has clear air above it.
        ''' </summary>
        Private Shared Function MaxShiftWithoutCollision(row As List(Of Control), obstacles As List(Of Control)) As Integer
            Dim allowed = Integer.MaxValue

            For Each control In row
                If Not control.Visible Then Continue For

                For Each obstacle In obstacles
                    ' Only obstacles above the control, and only where the two overlap horizontally.
                    If obstacle.Bottom > control.Top Then Continue For
                    If obstacle.Right <= control.Left OrElse obstacle.Left >= control.Right Then Continue For

                    allowed = Math.Min(allowed, control.Top - obstacle.Bottom - FieldRowClearance)
                Next
            Next

            Return allowed
        End Function

        ''' <summary>Groups controls already ordered by Top into rows.</summary>
        Private Shared Function BuildRows(ordered As List(Of Control)) As List(Of List(Of Control))
            Dim rows As New List(Of List(Of Control))()

            For Each control In ordered
                If rows.Count > 0 AndAlso control.Location.Y - RowTop(rows(rows.Count - 1)) <= FieldRowTolerance Then
                    rows(rows.Count - 1).Add(control)
                Else
                    rows.Add(New List(Of Control) From {control})
                End If
            Next

            Return rows
        End Function

        Private Shared Function BelongsToFieldGrid(control As Control, fieldControls As List(Of Control)) As Boolean
            If IsFieldControl(control) Then Return True
            Return fieldControls.Any(Function(field) Math.Abs(field.Location.Y - control.Location.Y) <= FieldRowTolerance)
        End Function

        Private Shared Function NormalizeColumnLefts(declared As Integer()) As Integer()
            If declared Is Nothing OrElse declared.Length = 0 Then Return New Integer() {0}
            Return declared.OrderBy(Function(value) value).ToArray()
        End Function

        ''' <summary>
        ''' A control with a name of its own is placed by its own Left. One without - the Zip Coder
        ''' button, for instance - belongs to the field group it was put beside, not to whichever
        ''' column its Left happens to fall in.
        ''' </summary>
        Private Shared Function ResolveColumnIndex(control As Control,
                                                   fieldControls As List(Of Control),
                                                   columnLefts As Integer()) As Integer
            If columnLefts.Length <= 1 Then Return 0
            If IsFieldControl(control) Then Return ColumnIndexForLeft(control.Location.X, columnLefts)

            Dim neighbour = fieldControls.
                Where(Function(field) Math.Abs(field.Location.Y - control.Location.Y) <= FieldRowTolerance AndAlso
                                      field.Location.X <= control.Location.X).
                OrderByDescending(Function(field) field.Location.X).FirstOrDefault()

            If neighbour Is Nothing Then Return ColumnIndexForLeft(control.Location.X, columnLefts)
            Return ColumnIndexForLeft(neighbour.Location.X, columnLefts)
        End Function

        Private Shared Function ColumnIndexForLeft(left As Integer, columnLefts As Integer()) As Integer
            Dim index = 0
            For candidate = 0 To columnLefts.Length - 1
                If left >= columnLefts(candidate) Then index = candidate
            Next
            Return index
        End Function

        Private Function IsRowLayoutControl(control As Control) As Boolean
            If control Is Nothing OrElse control.Location.Y < 0 Then Return False

            ' The header sits above the field grid and must not move with it: the caption, and the
            ' two buttons that share its line. ApplySharedPageCaption uses either caption name.
            '
            ' The buttons belong here for the same reason the caption does. Collapsing a hidden
            ' field row pulls everything below it upwards, and a header button caught in that pull
            ' is carried off the top of the page - which is exactly what happened to Help Desk on a
            ' page with a permission-hidden field.
            If String.Equals(control.Name, "Label_UserTitle", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(control.Name, "Label_PageTitle", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(control.Name, HelpDeskLauncher.ButtonName, StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(control.Name, "Button_TabOrderManager", StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            Return Not (control Is okButton OrElse control Is cancelActionButton)
        End Function

        ''' <summary>
        ''' The row's Top. Candidates are added in ascending Top order, so the first control in a
        ''' row always holds the minimum.
        ''' </summary>
        Private Shared Function RowTop(row As List(Of Control)) As Integer
            Return row(0).Location.Y
        End Function

        ''' <summary>
        ''' A row collapses only when it actually carries fields and none of them are showing.
        ''' The field test keeps decorative rows, grids and standalone buttons from being removed
        ''' just because they happen to be invisible.
        ''' </summary>
        Private Shared Function IsCollapsibleRow(row As List(Of Control)) As Boolean
            If Not row.Any(Function(control) IsFieldControl(control)) Then Return False
            Return row.All(Function(control) Not control.Visible OrElse IsRequiredBorderPanel(control))
        End Function

        ''' <summary>
        ''' Required-border panels are decoration for the field they sit behind, not content, so
        ''' they never keep a row alive. They carry no Name - only a Tag - so they are matched on
        ''' that. Both the Base_U local border and the one ApplyControlUpdates adds are covered.
        ''' </summary>
        Private Shared Function IsRequiredBorderPanel(control As Control) As Boolean
            If Not (TypeOf control Is Panel) Then Return False

            Dim tagText = TryCast(control.Tag, String)
            If tagText Is Nothing Then Return False

            Return tagText.StartsWith("RequiredBorder_", StringComparison.OrdinalIgnoreCase) OrElse
                   tagText.StartsWith("LocalRequiredBorder_", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function IsFieldControl(control As Control) As Boolean
            Dim controlName = If(control.Name, String.Empty)
            Return FieldControlPrefixes.Any(Function(prefix) controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        End Function

        Protected Sub SetManualTabOrder(ParamArray orderedControls() As Control)
            If orderedControls Is Nothing Then Return

            Dim tabIndex = 0
            For Each control In orderedControls
                If control Is Nothing Then Continue For

                Dim isReadOnlyText = TypeOf control Is TextBoxBase AndAlso DirectCast(control, TextBoxBase).ReadOnly
                If isReadOnlyText Then
                    control.TabStop = False
                    Continue For
                End If

                control.TabStop = True
                control.TabIndex = tabIndex
                tabIndex += 1
            Next
        End Sub

        ' A page that lays out its own questions in a fixed order has nothing for the tab order
        ' manager to configure. Overriding this to False suppresses the button and its panel; the
        ' saved tab order, if any, is still applied.
        Protected Overridable Function SupportsTabOrderManager() As Boolean
            Return True
        End Function

        ''' Centres a header button on the page caption. The caption is placed by
        ''' ApplySharedPageCaption, which runs before this, so its position is known.
        Private Sub AlignHeaderButtonToCaption(button As Control)
            If button Is Nothing Then Return

            Dim matches = Controls.Find("Label_UserTitle", True)
            If matches.Length = 0 Then matches = Controls.Find("Label_PageTitle", True)
            If matches.Length = 0 Then Return

            Dim caption = matches(0)
            button.Top = Math.Max(0, caption.Top + ((caption.Height - button.Height) \ 2))
        End Sub

        Private Sub InitializeTabOrderManager()
            If Not SupportsTabOrderManager() Then Return
            If Not IsApplicationAdminSession() OrElse tabOrderToggleButton IsNot Nothing Then Return

            ' UseVisualStyleBackColor keeps the themed button face instead of the page tint, which
            ' is what KeepButtonsUntinted does for every other button on the page.
            '
            ' Set here rather than left to that sweep, because this button does not exist when the
            ' sweep runs. ApplyInheritedPageBackground untints on Shown; this button is created a
            ' BeginInvoke later, so it was the one button on a tinted page still wearing the tint.
            tabOrderToggleButton = New Button() With {
                .Name = "Button_TabOrderManager",
                .Text = TabOrderCollapsedText,
                .Size = New Size(100, 28),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .TabStop = False,
                .FlatStyle = FlatStyle.Standard,
                .UseVisualStyleBackColor = True
            }
            ' ReservedWidth already includes the edge margin, so it is not subtracted again here.
            ' Doing both left a 22px gap where every browse page has 12.
            tabOrderToggleButton.Location = New Point(ClientSize.Width - tabOrderToggleButton.Width - HelpDeskLauncher.ReservedWidth, 10)
            AlignHeaderButtonToCaption(tabOrderToggleButton)
            AddHandler tabOrderToggleButton.Click, AddressOf TabOrderToggleButton_Click
            Controls.Add(tabOrderToggleButton)
            Dim tabOrderToggleToolTip As New ToolTip()
            tabOrderToggleToolTip.SetToolTip(tabOrderToggleButton, "Configure tab order")

            tabOrderPanel = New Panel() With {
                .Name = "Panel_TabOrderManager",
                .Size = New Size(300, Math.Min(420, Math.Max(260, ClientSize.Height - 30))),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .BorderStyle = BorderStyle.FixedSingle,
                .BackColor = SystemColors.Control,
                .Visible = False,
                .TabStop = False
            }
            tabOrderPanel.Location = New Point(ClientSize.Width - tabOrderPanel.Width - 10, 42)

            ' Up and Down group on the left; OK and Cancel pair on the right with the same 6px gap,
            ' so the reordering controls read as separate from the accept/discard pair.
            tabOrderUpButton = New Button() With {.Text = "Up", .Size = New Size(60, 26), .Location = New Point(8, 8), .TabStop = False}
            tabOrderDownButton = New Button() With {.Text = "Down", .Size = New Size(60, 26), .Location = New Point(74, 8), .TabStop = False}
            tabOrderSaveButton = New Button() With {.Text = "OK", .Size = New Size(60, 26), .Location = New Point(162, 8), .TabStop = False}
            tabOrderHideButton = New Button() With {.Text = "Cancel", .Size = New Size(60, 26), .Location = New Point(228, 8), .TabStop = False}
            tabOrderList = New CheckedListBox() With {
                .Name = "CheckedListBox_TabOrder",
                .Location = New Point(8, 42),
                .Size = New Size(282, tabOrderPanel.Height - 52),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right,
                .CheckOnClick = True,
                .TabStop = False
            }

            AddHandler tabOrderHideButton.Click, AddressOf TabOrderHideButton_Click
            AddHandler tabOrderUpButton.Click, AddressOf TabOrderUpButton_Click
            AddHandler tabOrderDownButton.Click, AddressOf TabOrderDownButton_Click
            AddHandler tabOrderSaveButton.Click, AddressOf TabOrderSaveButton_Click
            AddHandler tabOrderList.SelectedIndexChanged, AddressOf TabOrderList_SelectedIndexChanged
            AddHandler tabOrderList.KeyDown, AddressOf TabOrderList_KeyDown
            AddHandler tabOrderList.MouseDown, AddressOf TabOrderList_MouseDown
            AddHandler tabOrderList.MouseUp, AddressOf TabOrderList_MouseUp
            AddHandler tabOrderList.ItemCheck, AddressOf TabOrderList_ItemCheck
            AddHandler tabOrderPanel.MouseDown, AddressOf TabOrderPanel_MouseDown
            AddHandler tabOrderPanel.MouseMove, AddressOf TabOrderPanel_MouseMove
            AddHandler tabOrderPanel.MouseUp, AddressOf TabOrderPanel_MouseUp

            tabOrderPanel.Controls.AddRange({tabOrderHideButton, tabOrderUpButton, tabOrderDownButton, tabOrderSaveButton, tabOrderList})
            Controls.Add(tabOrderPanel)
            tabOrderPanel.BringToFront()
            tabOrderToggleButton.BringToFront()
            LoadTabOrderManager()
        End Sub

        Private Shared Function IsApplicationAdminSession() As Boolean
            Return SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso
                   SessionState.Current.Value.IsApplicationAdminRole
        End Function

        Private Sub LoadTabOrderManager()
            If tabOrderList Is Nothing Then Return

            Dim controls = ApplySavedTabOrder()

            loadingTabOrderManager = True
            Try
                For Each control In controls
                    Dim displayName = GetControlDisplayName(control)
                    Dim itemIndex = tabOrderList.Items.Add(New TabOrderManagerItem With {.Control = control, .DisplayName = displayName})
                    tabOrderList.SetItemChecked(itemIndex, control.TabStop)
                Next
            Finally
                loadingTabOrderManager = False
            End Try
            CaptureTabOrderBaseline()
            UpdateTabOrderManagerButtons()
        End Sub

        Private Sub TabOrderPanel_MouseDown(sender As Object, e As MouseEventArgs)
            If e.Button <> MouseButtons.Left OrElse e.Y > 40 Then Return
            tabOrderPanelDragging = True
            tabOrderPanelDragStart = Cursor.Position
            tabOrderPanelLocationStart = tabOrderPanel.Location
        End Sub

        Private Sub TabOrderPanel_MouseMove(sender As Object, e As MouseEventArgs)
            If Not tabOrderPanelDragging Then Return

            Dim delta = Point.Subtract(Cursor.Position, New Size(tabOrderPanelDragStart))
            Dim newX = Math.Max(0, Math.Min(ClientSize.Width - tabOrderPanel.Width, tabOrderPanelLocationStart.X + delta.X))
            Dim newY = Math.Max(0, Math.Min(ClientSize.Height - tabOrderPanel.Height, tabOrderPanelLocationStart.Y + delta.Y))
            tabOrderPanel.Location = New Point(newX, newY)
        End Sub

        Private Sub TabOrderPanel_MouseUp(sender As Object, e As MouseEventArgs)
            If e.Button = MouseButtons.Left Then tabOrderPanelDragging = False
        End Sub

        Private Function ApplySavedTabOrder() As List(Of Control)
            Dim controls = GetTabOrderControls()
            Dim savedSettings = DataAccess.GetTabOrderSettings(GetPageName())
            Dim savedByName = savedSettings.ToDictionary(Function(setting) setting.ControlName, StringComparer.OrdinalIgnoreCase)
            Dim savedControls = controls.Where(Function(control) savedByName.ContainsKey(control.Name)).
                OrderBy(Function(control) savedByName(control.Name).TabOrder).ToList()
            Dim newControls = controls.Where(Function(control) Not savedByName.ContainsKey(control.Name)).
                OrderBy(Function(control) control.TabIndex).ToList()
            Dim orderedControls = savedControls.Concat(newControls).ToList()

            Dim appliedTabIndex = 0
            For Each control In orderedControls
                Dim setting As TabOrderSetting = Nothing
                If savedByName.TryGetValue(control.Name, setting) Then
                    control.TabStop = setting.TabStop
                End If
                control.TabIndex = appliedTabIndex
                appliedTabIndex += 1
            Next

            Return orderedControls
        End Function

        Private Function GetTabOrderControls() As List(Of Control)
            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(Me, allControls)
            Return allControls.Where(Function(control) IsEligibleTabOrderControl(control)).
                OrderBy(Function(control) control.TabIndex).ToList()
        End Function

        Private Function IsEligibleTabOrderControl(control As Control) As Boolean
            If IsTabOrderManagerControl(control) OrElse String.IsNullOrWhiteSpace(control.Name) OrElse
               Not control.Visible OrElse Not control.Enabled Then
                Return False
            End If

            ' Page furniture, not part of the sequence somebody would reorder. OK and Cancel have
            ' always been excluded; Help Desk was not, only because it is not named here - and
            ' IsRowLayoutControl already treats it as furniture, so the two tests disagreed about
            ' what it is. Every generated page listed a "Button_HelpDesk" row in its Tab Order
            ' panel that nobody would ever want to move.
            '
            ' Excluded from the manager, not from tabbing: these keep whatever position the page
            ' gave them, the way SetManualTabOrder puts OK and Cancel at the end.
            If control Is okButton OrElse control Is cancelActionButton OrElse
               String.Equals(control.Name, HelpDeskLauncher.ButtonName, StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            If TypeOf control Is TextBoxBase Then
                Return Not DirectCast(control, TextBoxBase).ReadOnly
            End If

            Return TypeOf control Is ComboBox OrElse TypeOf control Is CheckBox OrElse
                   TypeOf control Is Button OrElse TypeOf control Is DateTimePicker OrElse
                   TypeOf control Is NumericUpDown
        End Function

        Private Function IsTabOrderManagerControl(control As Control) As Boolean
            Dim current = control
            While current IsNot Nothing
                If current Is tabOrderToggleButton OrElse current Is tabOrderPanel Then Return True
                current = current.Parent
            End While
            Return False
        End Function

        Private Shared Function GetControlDisplayName(control As Control) As String
            Dim controlName = control.Name
            Dim suffix As String = String.Empty
            If controlName.StartsWith("TextBox_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(8)
            ElseIf controlName.StartsWith("ComboBox_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(9)
            ElseIf controlName.StartsWith("CheckBox_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(9)
            ElseIf controlName.StartsWith("DateTimePicker_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(15)
            ElseIf controlName.StartsWith("NumericUpDown_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(14)
            End If

            If suffix <> String.Empty Then
                Dim labels = control.FindForm().Controls.Find("Label_" & suffix, True)
                If labels.Length > 0 AndAlso TypeOf labels(0) Is Label Then
                    Return DirectCast(labels(0), Label).Text.TrimEnd(" "c, "*"c).Trim()
                End If
            End If

            Return controlName
        End Function

        Private Sub TabOrderToggleButton_Click(sender As Object, e As EventArgs)
            If tabOrderPanel.Visible Then
                RevertTabOrderChanges()
                tabOrderPanel.Visible = False
            Else
                tabOrderPanel.Visible = True
                CaptureTabOrderBaseline()
            End If

            UpdateTabOrderToggleButton()
            tabOrderPanel.BringToFront()
        End Sub

        Private Sub TabOrderHideButton_Click(sender As Object, e As EventArgs)
            If Not tabOrderCommitInProgress Then RevertTabOrderChanges()
            tabOrderPanel.Visible = False
            UpdateTabOrderToggleButton()
        End Sub

        ''' <summary>
        ''' Remembers the row order and each control's TabStop as the panel opens, so closing
        ''' without OK can put everything back. Ticking a row changes TabStop immediately, while
        ''' reordering is only written to the controls by OK, so both have to be captured.
        ''' </summary>
        Private Sub CaptureTabOrderBaseline()
            If tabOrderList Is Nothing Then Return

            tabOrderBaselineOrder = tabOrderList.Items.OfType(Of TabOrderManagerItem)().ToList()
            tabOrderBaselineTabStops = New Dictionary(Of Control, Boolean)()

            For Each item In tabOrderBaselineOrder
                If item.Control IsNot Nothing Then
                    tabOrderBaselineTabStops(item.Control) = item.Control.TabStop
                End If
            Next
        End Sub

        Private Sub RevertTabOrderChanges()
            If tabOrderList Is Nothing OrElse tabOrderBaselineOrder Is Nothing Then Return

            loadingTabOrderManager = True
            Try
                tabOrderList.Items.Clear()

                For Each item In tabOrderBaselineOrder
                    Dim wasTabStop = True
                    If item.Control IsNot Nothing AndAlso tabOrderBaselineTabStops.TryGetValue(item.Control, wasTabStop) Then
                        item.Control.TabStop = wasTabStop
                    End If

                    tabOrderList.SetItemChecked(tabOrderList.Items.Add(item), wasTabStop)
                Next
            Finally
                loadingTabOrderManager = False
            End Try

            UpdateTabOrderManagerButtons()
        End Sub

        Private Sub UpdateTabOrderToggleButton()
            If tabOrderToggleButton Is Nothing OrElse tabOrderPanel Is Nothing Then Return
            tabOrderToggleButton.Text = If(tabOrderPanel.Visible, TabOrderExpandedText, TabOrderCollapsedText)
        End Sub

        Private Sub TabOrderUpButton_Click(sender As Object, e As EventArgs)
            MoveTabOrderManagerItem(-1)
        End Sub

        Private Sub TabOrderDownButton_Click(sender As Object, e As EventArgs)
            MoveTabOrderManagerItem(1)
        End Sub

        Private Sub MoveTabOrderManagerItem(delta As Integer)
            Dim selectedIndex = tabOrderList.SelectedIndex
            Dim targetIndex = selectedIndex + delta
            If selectedIndex < 0 OrElse targetIndex < 0 OrElse targetIndex >= tabOrderList.Items.Count Then Return

            Dim item = tabOrderList.Items(selectedIndex)
            Dim wasChecked = tabOrderList.GetItemChecked(selectedIndex)

            ' Reordering is not the user clicking a checkbox. Without this guard the ItemCheck
            ' handler sees an index that was never clicked and reverts the state we just restored,
            ' so the row loses its tab stop every time it is moved.
            loadingTabOrderManager = True
            Try
                tabOrderList.Items.RemoveAt(selectedIndex)
                tabOrderList.Items.Insert(targetIndex, item)
                tabOrderList.SetItemChecked(targetIndex, wasChecked)
            Finally
                loadingTabOrderManager = False
            End Try

            tabOrderList.SelectedIndex = targetIndex
            UpdateTabOrderManagerButtons()
        End Sub

        Private Sub TabOrderSaveButton_Click(sender As Object, e As EventArgs)
            Dim settings As New List(Of TabOrderSetting)()
            For index As Integer = 0 To tabOrderList.Items.Count - 1
                Dim item = TryCast(tabOrderList.Items(index), TabOrderManagerItem)
                If item Is Nothing OrElse item.Control Is Nothing Then Continue For
                item.Control.TabIndex = index
                item.Control.TabStop = tabOrderList.GetItemChecked(index)
                settings.Add(New TabOrderSetting With {.ControlName = item.Control.Name, .TabOrder = index, .TabStop = item.Control.TabStop})
            Next

            Dim updatedBy = SessionState.ActingUserID
            DataAccess.SaveTabOrderSettings(GetPageName(), settings, updatedBy)

            ' Closing after OK must not undo what OK just applied, and the saved state becomes
            ' the new baseline for the next time the panel is opened.
            tabOrderCommitInProgress = True
            Try
                tabOrderHideButton.PerformClick()
            Finally
                tabOrderCommitInProgress = False
            End Try

            CaptureTabOrderBaseline()
            MessageBox.Show(Me, "Tab order saved for this page.", "Tab Order", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub TabOrderList_SelectedIndexChanged(sender As Object, e As EventArgs)
            UpdateTabOrderManagerButtons()
            FocusTabOrderSelection()
        End Sub

        ''' <summary>
        ''' Puts the cursor on the field the selected row refers to, so it is obvious which control
        ''' the row stands for. Browsing the list is not the user filling the form in, so this does
        ''' not count as visiting a required field.
        ''' </summary>
        ''' <summary>
        ''' Selecting a row used to focus the control it names, which took focus off the list and put
        ''' the panel behind the page: the arrow keys stopped moving the selection and the panel the
        ''' user was working in disappeared. The panel keeps the focus and stays in front instead.
        ''' </summary>
        Private Sub FocusTabOrderSelection()
            If loadingTabOrderManager Then Return
            If tabOrderPanel Is Nothing OrElse Not tabOrderPanel.Visible Then Return

            tabOrderPanel.BringToFront()
            If tabOrderToggleButton IsNot Nothing Then tabOrderToggleButton.BringToFront()
        End Sub

        Private Sub TabOrderList_KeyDown(sender As Object, e As KeyEventArgs)
            If e.KeyCode = Keys.Up Then MoveTabOrderManagerItem(-1)
            If e.KeyCode = Keys.Down Then MoveTabOrderManagerItem(1)
        End Sub

        Private Sub TabOrderList_MouseDown(sender As Object, e As MouseEventArgs)
            tabOrderCheckClickIndex = -1
            If e.Button <> MouseButtons.Left Then Return

            Dim itemIndex = tabOrderList.IndexFromPoint(e.Location)
            If itemIndex < 0 Then Return

            Dim itemBounds = tabOrderList.GetItemRectangle(itemIndex)
            If e.X <= itemBounds.Left + SystemInformation.MenuCheckSize.Width + 4 Then
                tabOrderCheckClickIndex = itemIndex
            End If
        End Sub

        Private Sub TabOrderList_MouseUp(sender As Object, e As MouseEventArgs)
            tabOrderCheckClickIndex = -1
        End Sub

        Private Sub TabOrderList_ItemCheck(sender As Object, e As ItemCheckEventArgs)
            If loadingTabOrderManager Then Return

            If tabOrderCheckClickIndex <> e.Index Then
                e.NewValue = e.CurrentValue
                Return
            End If

            BeginInvoke(New MethodInvoker(Sub()
                                              Dim item = TryCast(tabOrderList.Items(e.Index), TabOrderManagerItem)
                                              If item IsNot Nothing Then item.Control.TabStop = e.NewValue = CheckState.Checked
                                          End Sub))
        End Sub

        Private Sub UpdateTabOrderManagerButtons()
            If tabOrderList Is Nothing Then Return
            tabOrderUpButton.Enabled = tabOrderList.SelectedIndex > 0
            tabOrderDownButton.Enabled = tabOrderList.SelectedIndex >= 0 AndAlso tabOrderList.SelectedIndex < tabOrderList.Items.Count - 1
        End Sub

        ''' <summary>
        ''' Puts the cursor on the first field the validation message complained about, so the
        ''' user lands on the problem instead of hunting for it. Silently does nothing when the
        ''' field cannot take focus - hidden by permissions, disabled, or read-only.
        ''' </summary>
        Private Sub FocusValidationControl(control As Control)
            If control Is Nothing OrElse Not control.Visible OrElse Not control.Enabled Then Return

            Dim editable = TryCast(control, TextBoxBase)
            If editable IsNot Nothing AndAlso editable.ReadOnly Then Return

            Try
                Me.ActiveControl = control
                control.Select()
                control.Focus()
                ClearTextSelection(control)
                ApplyFocusIndicator(control)
            Catch
                ' Focus is a convenience; never let it block the validation result.
            End Try
        End Sub

        Private Sub SetInitialFieldFocus()
            Dim firstField = FindFirstFocusableField(Me)
            If firstField Is Nothing Then Return

            ' The page putting the cursor somewhere is not the user visiting the field, so this
            ' must not turn a blank required field red before they have touched anything.
            suppressRequiredTouch = True
            Try
                Me.ActiveControl = firstField
                firstField.Select()
                firstField.Focus()
                ClearTextSelection(firstField)
                ApplyFocusIndicator(firstField)
            Finally
                suppressRequiredTouch = False
            End Try
        End Sub

        Private Sub RemoveReadOnlyControlsFromTabOrder(container As Control)
            For Each control As Control In container.Controls
                If TypeOf control Is TextBoxBase AndAlso DirectCast(control, TextBoxBase).ReadOnly Then
                    control.TabStop = False
                    If Not readOnlyMouseHandled.Contains(control) Then
                        readOnlyMouseHandled.Add(control)
                        AddHandler control.MouseDown, AddressOf ReadOnlyControl_MouseDown
                    End If
                End If

                If control.HasChildren Then
                    RemoveReadOnlyControlsFromTabOrder(control)
                End If
            Next
        End Sub

        ''' <summary>
        ''' A click on a read-only field is ignored: focus stays where it was.
        '''
        ''' This used to move focus to the next editable control, which meant clicking something
        ''' you cannot type in scrolled the page to somewhere else entirely - the answer to "you
        ''' cannot edit this" should not be to take you somewhere you did not ask to go.
        ''' </summary>
        Private Sub ReadOnlyControl_MouseDown(sender As Object, e As MouseEventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return

            Dim previous = ActiveControl
            If previous IsNot Nothing AndAlso previous IsNot control Then
                ' The click focuses the field before this runs, so focus is handed back rather than
                ' prevented. Deferred, or WinForms puts it straight back on the clicked control.
                BeginInvoke(New Action(Sub()
                                           If previous.IsDisposed OrElse Not previous.CanFocus Then Return
                                           previous.Focus()
                                       End Sub))
            End If
        End Sub

        Private Sub WireFocusIndicators(container As Control)
            Dim childControls As New List(Of Control)()
            For Each control As Control In container.Controls
                childControls.Add(control)
            Next

            For Each control As Control In childControls
                If IsFocusIndicatorControl(control) AndAlso Not IsBaseActionButton(control) Then
                    ' Guarded on the border rather than the remembered colour, because a button now
                    ' gets a border but no remembered colour - and this block also attaches
                    ' handlers, which must not happen twice.
                    If Not focusBorderPanels.ContainsKey(control) Then
                        ' A button is remembered as Nothing-to-restore. Focus assigns this colour
                        ' back on the way in, and assigning BackColor to a themed button turns its
                        ' visual style off - so it would paint as a flat rectangle in whatever
                        ' colour it had inherited from the page. The green border is enough to say
                        ' where the focus is; a button does not need its face repainted too.
                        If Not TypeOf control Is Button Then
                            focusOriginalBackColors(control) = control.BackColor
                        End If
                        HostFlowChildForFocusBorder(control)
                        Dim borderPanel = FindExistingRequiredBorderPanel(control)
                        If borderPanel Is Nothing Then
                            borderPanel = New Panel() With {
                                .Name = "FocusBorder_" & control.Name,
                                .BackColor = Color.FromArgb(55, 180, 105),
                                .Visible = False,
                                .TabStop = False
                            }
                            control.Parent.Controls.Add(borderPanel)
                        End If
                        borderPanel.Location = New Point(Math.Max(0, control.Left - 2), Math.Max(0, control.Top - 2))
                        borderPanel.Size = New Size(control.Width + 4, control.Height + 4)
                        borderPanel.SendToBack()
                        control.BringToFront()
                        focusBorderPanels(control) = borderPanel
                        AddHandler control.Enter, AddressOf FocusIndicator_Enter
                        AddHandler control.Leave, AddressOf FocusIndicator_Leave
                        AddHandler control.MouseEnter, AddressOf EditableControl_MouseEnter
                        AddHandler control.MouseLeave, AddressOf EditableControl_MouseLeave
                        If TypeOf control Is TextBoxBase Then
                            AddHandler control.TextChanged, AddressOf FocusIndicator_ValueChanged
                        ElseIf TypeOf control Is ComboBox Then
                            AddHandler control.TextChanged, AddressOf FocusIndicator_ValueChanged
                            AddHandler DirectCast(control, ComboBox).SelectedIndexChanged, AddressOf FocusIndicator_ValueChanged
                        End If
                    End If
                End If

                If control.HasChildren Then
                    WireFocusIndicators(control)
                End If
            Next
        End Sub

        ' A FlowLayoutPanel treats child index as flow position, so a focus border added beside a
        ' flow child becomes a visible gap in the row, and the SendToBack/BringToFront that give the
        ' border its z-order silently reorder the row instead. Hosting the control in a plain panel
        ' first gives the border somewhere to sit that is not the flow, and keeps the control where
        ' the page put it. This mirrors what pages already do by hand for their required borders.
        Private Shared Sub HostFlowChildForFocusBorder(control As Control)
            If control Is Nothing Then Return
            Dim flow = TryCast(control.Parent, FlowLayoutPanel)
            If flow Is Nothing Then Return

            Dim flowIndex = flow.Controls.GetChildIndex(control)
            Dim host As New Panel() With {
                .Name = "FocusHost_" & control.Name,
                .AutoSize = True,
                .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                .Padding = New Padding(2),
                .Margin = control.Margin,
                .TabIndex = control.TabIndex,
                .TabStop = False
            }

            flow.Controls.Remove(control)
            control.Margin = New Padding(0)
            control.Location = New Point(2, 2)
            host.Controls.Add(control)
            control.TabIndex = 0
            flow.Controls.Add(host)
            flow.Controls.SetChildIndex(host, flowIndex)
        End Sub

        Private Shared Function FindExistingRequiredBorderPanel(control As Control) As Panel
            If control Is Nothing OrElse control.Parent Is Nothing Then Return Nothing

            Dim expectedLocalTag = String.Empty
            If control.Name.StartsWith("TextBox_", StringComparison.OrdinalIgnoreCase) Then
                expectedLocalTag = "LocalRequiredBorder_" & control.Name.Substring(8)
            ElseIf control.Name.StartsWith("ComboBox_", StringComparison.OrdinalIgnoreCase) Then
                expectedLocalTag = "LocalRequiredBorder_" & control.Name.Substring(9)
            ElseIf control.Name.StartsWith("CheckBox_", StringComparison.OrdinalIgnoreCase) Then
                expectedLocalTag = "LocalRequiredBorder_" & control.Name.Substring(9)
            End If
            Dim expectedPermissionTag = "RequiredBorder_" & control.Name

            For Each sibling As Control In control.Parent.Controls
                Dim panel = TryCast(sibling, Panel)
                If panel Is Nothing OrElse panel.Tag Is Nothing Then Continue For

                Dim tagText = panel.Tag.ToString()
                If String.Equals(tagText, expectedLocalTag, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(tagText, expectedPermissionTag, StringComparison.OrdinalIgnoreCase) Then
                    Return panel
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' Buttons are included so a command sitting in the field grid - Zip Coder, for one -
        ''' shows the same green focus border as the fields around it when tabbed to.
        ''' </summary>
        Private Shared Function IsFocusIndicatorControl(control As Control) As Boolean
            Return TypeOf control Is TextBoxBase OrElse TypeOf control Is ComboBox OrElse
                   TypeOf control Is CheckBox OrElse TypeOf control Is DateTimePicker OrElse
                   TypeOf control Is NumericUpDown OrElse TypeOf control Is Button
        End Function

        ''' <summary>
        ''' Save, Cancel and the enum button sit outside the field grid and keep their standard
        ''' appearance, so they are left out of the focus indicator.
        ''' </summary>
        Private Function IsBaseActionButton(control As Control) As Boolean
            Return control Is okButton OrElse control Is cancelActionButton
        End Function

        Private Shared Function IsEmptyRequiredControl(control As Control) As Boolean
            If control Is Nothing OrElse Not String.Equals(If(control.Tag, String.Empty).ToString(), "Required", StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            If TypeOf control Is ComboBox Then
                Return DataAccess.IsEmptyComboSelection(DirectCast(control, ComboBox))
            End If

            Return String.IsNullOrWhiteSpace(control.Text)
        End Function

        Private Sub FocusIndicator_Enter(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return
            Dim originalColor As Color
            If focusOriginalBackColors.TryGetValue(control, originalColor) Then control.BackColor = originalColor
            MarkRequiredTouched(control)
            ApplyFocusIndicator(control)

            ' Arriving at a field selects its text, so typing replaces rather than appends. Stated
            ' here rather than left to WinForms, which only does it for some arrivals - the first
            ' field on the page behaved differently from every other one.
            '
            ' A mouse click still places the caret: the click sets its own selection after this
            ' runs. And the page's own opening focus is excluded, so a page does not open with text
            ' already highlighted.
            If suppressRequiredTouch Then Return
            Dim editable = TryCast(control, TextBoxBase)
            If editable IsNot Nothing AndAlso Not editable.ReadOnly Then editable.SelectAll()
        End Sub

        Private Sub ApplyFocusIndicator(control As Control)
            If control Is Nothing Then Return
            Dim borderPanel As Panel = Nothing
            If focusBorderPanels.TryGetValue(control, borderPanel) Then
                borderPanel.BackColor = If(ShouldShowRequiredWarning(control), Color.Red, Color.FromArgb(55, 180, 105))
                borderPanel.Visible = True
                borderPanel.BringToFront()
                control.BringToFront()
            End If
        End Sub

        ''' <summary>
        ''' Records that the user has been in a required field. Entering and leaving both count,
        ''' so a field goes red as soon as it is visited empty and stays red until it has data.
        ''' </summary>
        Private Sub MarkRequiredTouched(control As Control)
            If control Is Nothing OrElse suppressRequiredTouch Then Return
            If Not String.Equals(If(control.Tag, String.Empty).ToString(), "Required", StringComparison.OrdinalIgnoreCase) Then Return
            touchedRequiredControls.Add(control)
        End Sub

        ''' <summary>
        ''' Whether a required field is currently showing its red border.
        '''
        ''' Two ways to earn it, and both require the field to be empty - red never appears on a
        ''' field that has a value, so seeing red always means something needs doing:
        '''
        '''   - visited and left empty. Persists until the field is filled; the mouse is irrelevant.
        '''   - hovered while empty. Withdrawn on mouse-out, but only because it was never earned
        '''     the first way. Hovering cannot clear a border the visit rule turned on.
        ''' </summary>
        Private Function ShouldShowRequiredWarning(control As Control) As Boolean
            If Not IsEmptyRequiredControl(control) Then Return False
            Return touchedRequiredControls.Contains(control) OrElse hoveredRequiredControls.Contains(control)
        End Function

        ''' <summary>
        ''' Required fields the mouse is currently over. Separate from touchedRequiredControls
        ''' because the two are withdrawn differently: a hover ends, a visit does not.
        ''' </summary>
        Private ReadOnly hoveredRequiredControls As New HashSet(Of Control)()

        Private Sub WatchRequiredHover(field As Control)
            If field Is Nothing Then Return

            AddHandler field.MouseEnter,
                Sub()
                    hoveredRequiredControls.Add(field)
                    RefreshLocalRequiredBorders()
                End Sub

            AddHandler field.MouseLeave,
                Sub()
                    hoveredRequiredControls.Remove(field)
                    RefreshLocalRequiredBorders()
                End Sub
        End Sub

        Private Sub FocusIndicator_Leave(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return
            Dim originalColor As Color
            If focusOriginalBackColors.TryGetValue(control, originalColor) Then control.BackColor = originalColor

            MarkRequiredTouched(control)

            Dim borderPanel As Panel = Nothing
            If Not focusBorderPanels.TryGetValue(control, borderPanel) Then Return

            ' The green focus ring and the red required ring are the same panel. Leaving the field
            ' drops the focus ring, but a required field left empty keeps its red one and holds it
            ' until the field has data.
            If ShouldShowRequiredWarning(control) Then
                borderPanel.BackColor = Color.Red
                borderPanel.Visible = True
                borderPanel.BringToFront()
                control.BringToFront()
            Else
                borderPanel.Visible = False
            End If
        End Sub

        Private Sub FocusIndicator_ValueChanged(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing OrElse Not control.Focused Then Return

            ' Editing a field counts as visiting it. Without this, a field the page put the cursor
            ' in at open - which is deliberately not treated as a visit - would not turn red until
            ' the user tabbed away from it.
            If Not loading Then MarkRequiredTouched(control)
            ApplyFocusIndicator(control)
        End Sub

        ''' <summary>
        ''' Buttons are left alone by both of these. Windows already paints a button's hover, and
        ''' repainting its face turns the visual style off - the button then keeps whatever colour
        ''' was last assigned, because there is nothing to restore it to.
        ''' </summary>
        Private Sub EditableControl_MouseEnter(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing OrElse control.Focused OrElse TypeOf control Is Button Then Return
            control.BackColor = AppAdminRequiredBackColor
        End Sub

        Private Sub EditableControl_MouseLeave(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing OrElse control.Focused OrElse TypeOf control Is Button Then Return

            Dim originalColor As Color
            If focusOriginalBackColors.TryGetValue(control, originalColor) Then
                control.BackColor = originalColor
            End If
        End Sub

        Private Shared Sub ClearTextSelection(control As Control)
            Dim textControl = TryCast(control, TextBoxBase)
            If textControl IsNot Nothing Then
                textControl.SelectionStart = textControl.TextLength
                textControl.SelectionLength = 0
            End If
        End Sub

        Private Shared Function FindFirstFocusableField(container As Control) As Control
            Dim firstControl As Control = Nothing
            Dim lowestTabIndex = Integer.MaxValue
            For Each control As Control In container.Controls
                Dim isFocusableType = TypeOf control Is TextBoxBase OrElse TypeOf control Is ComboBox OrElse TypeOf control Is CheckBox OrElse TypeOf control Is DateTimePicker OrElse TypeOf control Is NumericUpDown
                Dim isEditableText = Not TypeOf control Is TextBoxBase OrElse Not DirectCast(control, TextBoxBase).ReadOnly
                If control.Visible AndAlso control.Enabled AndAlso control.TabStop AndAlso isFocusableType AndAlso isEditableText AndAlso control.TabIndex < lowestTabIndex Then
                    firstControl = control
                    lowestTabIndex = control.TabIndex
                End If
            Next

            Return firstControl
        End Function

        ' â”€â”€ Overridable behaviour â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        Protected Overridable Function OkButtonText() As String
            Return "OK"
        End Function

        Protected Overridable Function ShouldWarnOnCancel() As Boolean
            Return False
        End Function

        Protected Overridable Function SaveRecord() As Boolean
            ' Default implementation: no persistence required. Override in child classes to persist to database.
            Return True
        End Function

        ''' <summary>
        ''' The record this page saved, so the browse page can reselect its row after a Create.
        '''
        ''' Zero means nothing was saved, or the page does not track it - which is why the default
        ''' is safe for every page that has never needed this.
        '''
        ''' It lives here rather than on each generated page because FW_Base_B reads it through
        ''' whatever CreateMaintenancePage handed back, and the browse base cannot see a
        ''' page-local property on a class it does not know.
        ''' </summary>
        Public Overridable ReadOnly Property SavedRecordId As Integer
            Get
                Return 0
            End Get
        End Property

        ''' <summary>
        ''' Why a computed field cannot be typed into.
        '''
        ''' It still takes focus and still draws the green focus border, so it looks like every
        ''' other field right up until the first keystroke does nothing. Both are worth keeping -
        ''' the border says where the user is, and the value is worth reading - so the field
        ''' explains itself rather than being made unreachable.
        '''
        ''' One owner for the wording. A generated page asks for the hint and carries no text.
        ''' </summary>
        Public Const ComputedFieldHint As String = "COMPUTED FIELD, ENTRY NOT POSSIBLE."

        Private ReadOnly fieldHintToolTip As New ToolTip()

        ''' <summary>
        ''' Puts the computed-field explanation on the field and on its label, so it is found from
        ''' whichever the pointer reaches first - the label is the wider target of the two.
        ''' </summary>
        Protected Sub ShowComputedFieldHint(field As Control)
            If field Is Nothing OrElse String.IsNullOrWhiteSpace(field.Name) Then Return

            fieldHintToolTip.SetToolTip(field, ComputedFieldHint)

            If Not field.Name.StartsWith("TextBox_", StringComparison.OrdinalIgnoreCase) Then Return

            For Each label In Controls.Find("Label_" & field.Name.Substring("TextBox_".Length), True)
                fieldHintToolTip.SetToolTip(label, ComputedFieldHint)
            Next
        End Sub

        Protected Overridable Function IsViewOnly() As Boolean
            Return False
        End Function

        ''' <summary>
        ''' True when the page is creating a record rather than editing an existing one.
        ''' Field-level permissions use this to choose between Can_Create and Can_Update.
        ''' Override on any page that has a create mode; the default is safe for pages that
        ''' only ever edit.
        ''' </summary>
        Protected Overridable Function IsCreatingNewRecord() As Boolean
            Return False
        End Function

        Protected Overridable Function GetAdditionalValidationMessageLines() As IEnumerable(Of String)
            Return Array.Empty(Of String)()
        End Function

        Protected Overridable Function ResolveAuditRecordKey() As String
            Return String.Empty
        End Function

        Protected Overridable Function ResolveAuditOperationType() As String
            Return String.Empty
        End Function

        ''' <summary>
        ''' True when the save failed because the record has been soft-deleted rather than merely
        ''' changed. Reports it, and closes the page - there is nothing to overwrite and no useful
        ''' decision to offer.
        '''
        ''' This has to be checked before offering an overwrite. A soft-deleted row still exists,
        ''' so an overwrite would succeed and quietly write the user's edits onto a deleted record,
        ''' where nobody would see them again.
        '''
        ''' Returns False when the record is not deleted, leaving the caller to treat it as an
        ''' ordinary concurrency conflict.
        ''' </summary>
        Protected Function HandleRecordDeletedDuringSave() As Boolean
            Dim recordId As Integer
            If Not Integer.TryParse(ResolveAuditRecordKey(), recordId) OrElse recordId <= 0 Then
                Return False
            End If

            Dim info = DataAccess.GetSoftDeleteInfo(ResolveTableNameForConcurrency(), recordId)
            If info Is Nothing Then Return False

            MessageBox.Show(Me,
                            (info.Describe() & Environment.NewLine & Environment.NewLine &
                             "Your changes have not been saved.").ToUpperInvariant(),
                            "RECORD DELETED",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)

            ' This explains the failure completely, so the generic save-failed message that would
            ' otherwise follow is suppressed - two dialogs for one cause is just noise.
            saveFailureAlreadyReported = True

            ' Abort, not Cancel: nothing was saved, so this is not success, but the browse grid is
            ' now stale and must refresh. Cancel would leave the deleted record on screen.
            DialogResult = DialogResult.Abort
            bypassCancelCloseCheck = True
            Close()
            Return True
        End Function

        Protected Function ConfirmConcurrencyOverwrite() As Boolean
            Return MessageBox.Show(Me,
                                   "This record was changed by another user after you opened it." & Environment.NewLine & Environment.NewLine &
                                   "Do you want to overwrite that newer version with your current changes?",
                                   "Record Changed",
                                   MessageBoxButtons.YesNo,
                                   MessageBoxIcon.Warning) = DialogResult.Yes
        End Function

        Protected Sub ShowConcurrencyUnavailable()
            MessageBox.Show(Me,
                            "This record cannot be saved safely because its table does not have a RowVersion column.",
                            "Concurrency Protection Unavailable",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub

        Protected Sub CaptureOriginalRowVersion(rowVersion As Byte())
            If rowVersion Is Nothing Then
                originalRowVersion = Nothing
            Else
                originalRowVersion = CType(rowVersion.Clone(), Byte())
            End If
        End Sub

        Protected Function CopyOriginalRowVersion() As Byte()
            If originalRowVersion Is Nothing Then
                Return Nothing
            End If

            Return CType(originalRowVersion.Clone(), Byte())
        End Function

        ' â”€â”€ Must override in child page â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        Protected MustOverride Sub BindToFormInternal()
        Protected MustOverride Sub ApplyMode()
        Protected MustOverride Function TryBuildRecord() As Boolean

        ' â”€â”€ Shared form wiring â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        Protected Sub BindToForm()
            loading = True
            WarnIfMissingRowVersion()
            BindToFormInternal()
            loading = False
            hasUnsavedChanges = False
            DataAccess.ApplyControlUpdates(Me, Me.GetType().Name, ResolveTableNameForConstraints(), IsCreatingNewRecord())
            ReportUnmappedFieldControls()
            AdoptRequiredBorderPanels()
            ' Sized only here, on load. The save pass runs the same normalisation to trim and cap
            ' the values, but a box that changed width while somebody was typing in it would be
            ' the page rearranging itself under them.
            NormalizeTextInputsForSave(sizeToColumnLength:=True)
            baselineControlSnapshotJson = CaptureControlSnapshotJson()
            ResetPendingRecordBaseline()
        End Sub

        Protected Overridable Sub WarnIfMissingRowVersion()
            Dim tableName = ResolveTableNameForConcurrency()
            If String.IsNullOrWhiteSpace(tableName) OrElse DataAccess.TableHasRowVersion(tableName) Then
                Return
            End If

            MessageBox.Show(Me,
                            "Table dbo." & tableName & " does not have a RowVersion column. Concurrent edit protection is unavailable for this page.",
                            "Concurrency Protection Unavailable",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub

        Protected Overridable Function ResolveTableNameForConcurrency() As String
            Dim tableName = If(GetTableNameOverride(), String.Empty).Trim()
            If tableName <> String.Empty Then
                Return tableName
            End If

            Dim pageName = GetPageName()
            If pageName.EndsWith("_U", StringComparison.OrdinalIgnoreCase) Then
                Return "FW_" & pageName.Substring(0, pageName.Length - 2)
            End If

            Return String.Empty
        End Function

        Protected Sub MarkDirty(sender As Object, e As EventArgs)
            If Not loading Then hasUnsavedChanges = True
        End Sub

        Protected Sub CloseAfterSuccessfulCommand()
            hasUnsavedChanges = False
            Me.DialogResult = DialogResult.OK
            bypassCancelCloseCheck = True
            Me.Close()
        End Sub

        Private Sub OkButton_Click(sender As Object, e As EventArgs)
            If IsViewOnly() Then
                Me.DialogResult = DialogResult.OK
                bypassCancelCloseCheck = True
                Me.Close()
                Return
            End If

            If Not ExecuteSaveWorkflow() Then
                Return
            End If

            hasUnsavedChanges = False
            Me.DialogResult = DialogResult.OK
            bypassCancelCloseCheck = True
            Me.Close()
        End Sub

        Protected Function ExecuteSaveWorkflow() As Boolean
            If Not SwitchedUserGuard.AllowWrite(Me, "SAVE THIS RECORD") Then
                Return False
            End If

            If Not ValidateAndBuildForSave() Then
                Return False
            End If

            Return SaveRecordWithAudit()
        End Function


        Protected Function ValidateAndBuildForSave() As Boolean
            NormalizeTextInputsForSave()

            ' A failed save names the empty required fields, so mark them all visited: the red
            ' borders then match the message even for fields the user never went into.
            For Each requiredControl In requiredBorderPanels.Keys
                touchedRequiredControls.Add(requiredControl)
            Next
            RefreshLocalRequiredBorders()
            Dim errorMsg As String = String.Empty
            Dim validationLines As New List(Of String)()

            Dim firstMissingControl As Control = Nothing

            If Not DataAccess.ValidateRequiredControls(Me, errorMsg, firstMissingControl) Then
                If Not String.IsNullOrWhiteSpace(errorMsg) Then
                    validationLines.AddRange(errorMsg.Split({Environment.NewLine}, StringSplitOptions.None))
                End If
            End If

            Dim uniqueErrorMsg As String = String.Empty
            If Not DataAccess.ValidateUniqueFields(Me,
                                                   GetPageName(),
                                                   ResolveTableNameForConstraints(),
                                                   IsCreatingNewRecord(),
                                                   ResolveAuditRecordKey(),
                                                   uniqueErrorMsg) Then
                If Not String.IsNullOrWhiteSpace(uniqueErrorMsg) Then
                    validationLines.AddRange(uniqueErrorMsg.Split({Environment.NewLine}, StringSplitOptions.None))
                End If
            End If

            Dim additionalLines = GetAdditionalValidationMessageLines()
            If additionalLines IsNot Nothing Then
                For Each line In additionalLines
                    If line Is Nothing Then
                        Continue For
                    End If
                    If String.IsNullOrWhiteSpace(line) Then
                        validationLines.Add(String.Empty)
                    Else
                        validationLines.Add(line.Trim())
                    End If
                Next
            End If

            If validationLines.Count > 0 Then
                Dim validationMessage = String.Join(Environment.NewLine, validationLines)
                Dim sqlMarkerIndex = validationMessage.IndexOf("SQL VALIDATION", StringComparison.OrdinalIgnoreCase)
                If sqlMarkerIndex > 0 Then
                    validationMessage = validationMessage.Substring(0, sqlMarkerIndex).TrimEnd() &
                                        Environment.NewLine & Environment.NewLine &
                                        validationMessage.Substring(sqlMarkerIndex)
                End If
                MessageBox.Show(validationMessage, "The Following Occurred", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                FocusValidationControl(firstMissingControl)
                Return False
            End If

            ' Masked fields display a placeholder, never their value. Restore the real values so
            ' TryBuildRecord reads what was loaded, then mask again. Without this the mask text
            ' itself would be written to the database.
            FieldPermissions.UnmaskForSave(Me)
            Try
                If Not TryBuildRecord() Then
                    Return False
                End If
            Finally
                FieldPermissions.RemaskAfterSave(Me)
            End Try

            Return True
        End Function

        Protected Function SaveRecordWithAudit() As Boolean
            saveFailureAlreadyReported = False
            Dim pageName = GetPageName()
            Dim tableName = ResolveTableNameForConstraints()
            Dim beforeRecordKey = ResolveRecordKeyForAudit()
            Dim operationType = If(ResolveAuditOperationType(), String.Empty).Trim()
            If operationType = String.Empty Then
                operationType = ResolveOperationType(beforeRecordKey)
            End If
            Dim beforeSnapshot = If(baselineControlSnapshotJson, String.Empty)
            If beforeSnapshot = String.Empty Then
                beforeSnapshot = CaptureControlSnapshotJson()
            End If

            DataAccess.LogUpdateAudit(pageName,
                                      tableName,
                                      operationType,
                                      "BeforeSave",
                                      beforeRecordKey,
                                      beforeSnapshot)

            Dim saveSucceeded = False
            Dim saveError As String = String.Empty
            Try
                saveSucceeded = SaveRecord()
            Catch ex As Exception
                saveError = ex.Message
            End Try

            Dim afterRecordKey = ResolveRecordKeyForAudit()
            Dim afterSnapshot = CaptureControlSnapshotJson()

            DataAccess.LogUpdateAudit(pageName,
                                      tableName,
                                      operationType,
                                      "AfterSave",
                                      afterRecordKey,
                                      afterSnapshot,
                                      saveSucceeded)

            If saveSucceeded Then
                hasUnsavedChanges = False
                baselineControlSnapshotJson = CaptureControlSnapshotJson()
                ResetPendingRecordBaseline()
            Else
                ' Skipped when the page has already explained the failure - a deleted record, for
                ' one - so the user is not told twice about one cause.
                If Not saveFailureAlreadyReported Then
                    Dim message = If(String.IsNullOrWhiteSpace(saveError),
                                     "THE RECORD COULD NOT BE SAVED.",
                                     "SAVE FAILED: " & saveError)
                    MessageBox.Show(Me, message.ToUpperInvariant(), "SAVE FAILED", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End If
            End If

            Return saveSucceeded
        End Function

        Private Sub CancelButton_Click(sender As Object, e As EventArgs)
            If Not TryConfirmCancelClose() Then
                Me.DialogResult = DialogResult.None
                Return
            End If
            Me.DialogResult = DialogResult.Cancel
            bypassCancelCloseCheck = True
            Me.Close()
        End Sub

        Private Function TryConfirmCancelClose() As Boolean
            If ShouldWarnOnCancel() AndAlso HasNetUnsavedChanges() Then
                Dim result = MessageBox.Show("You have unsaved changes. Discard them?", "Unsaved Changes",
                                             MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                If result = DialogResult.No Then
                    Return False
                End If
            End If

            Return True
        End Function

        Private Function HasNetUnsavedChanges() As Boolean
            Dim currentSnapshot = CapturePendingRecordSnapshotJson()
            Dim baselineSnapshot = If(baselineRecordSnapshotJson, String.Empty)
            Dim hasChanges = Not String.Equals(currentSnapshot, baselineSnapshot, StringComparison.Ordinal)

            If Not hasChanges Then
                hasUnsavedChanges = False
            Else
                hasUnsavedChanges = True
            End If

            Return hasChanges
        End Function

        Private Sub ResetPendingRecordBaseline()
            baselineRecordSnapshotJson = CapturePendingRecordSnapshotJson()
            hasUnsavedChanges = False
        End Sub

        Private Sub FW_Base_U_FormClosing(sender As Object, e As FormClosingEventArgs)
            If bypassCancelCloseCheck Then
                Return
            End If

            If e.CloseReason <> CloseReason.UserClosing Then
                Return
            End If

            If Not TryConfirmCancelClose() Then
                e.Cancel = True
                Me.DialogResult = DialogResult.None
                Return
            End If

            Me.DialogResult = DialogResult.Cancel
        End Sub

        ''' <summary>Override to specify a custom page name for enumeration/business rules. Defaults to class name.</summary>
        Protected Overridable Function GetPageName() As String
            Return Me.GetType().Name
        End Function

        ''' <summary>Override to specify a custom DB table name for enumeration FileLink. Nothing = auto-derived.</summary>
        Protected Overridable Function GetTableNameOverride() As String
            Return Nothing
        End Function

        ''' <param name="sizeToColumnLength">
        ''' Whether to also narrow each box towards what its column can hold. Load only - see the
        ''' call site.
        ''' </param>
        Private Sub NormalizeTextInputsForSave(Optional sizeToColumnLength As Boolean = False)
            Dim tableName = ResolveTableNameForConstraints()
            Dim pageName = GetPageName()
            Dim columnLengths = DataAccess.GetTextColumnMaxLengths(tableName)
            Dim controlMap = DataAccess.GetPageControlFieldMap(Me, pageName, tableName)

            If columnLengths Is Nothing OrElse columnLengths.Count = 0 Then
                TrimTextControlsOnly(Me)
                Return
            End If

            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(Me, allControls)

            For Each ctrl In allControls
                If TypeOf ctrl Is TextBox Then
                    Dim tb = DirectCast(ctrl, TextBox)
                    NormalizeTextControl(tb, controlMap, columnLengths, sizeToColumnLength)
                ElseIf TypeOf ctrl Is MaskedTextBox Then
                    Dim mb = DirectCast(ctrl, MaskedTextBox)
                    NormalizeTextControl(mb, controlMap, columnLengths, sizeToColumnLength)
                ElseIf TypeOf ctrl Is RichTextBox Then
                    Dim rb = DirectCast(ctrl, RichTextBox)
                    NormalizeTextControl(rb, controlMap, columnLengths, sizeToColumnLength)
                End If
            Next
        End Sub

        Private Sub TrimTextControlsOnly(container As Control)
            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(container, allControls)
            For Each ctrl In allControls
                If TypeOf ctrl Is TextBoxBase Then
                    Dim tb = DirectCast(ctrl, TextBoxBase)
                    tb.Text = If(tb.Text, String.Empty).Trim()
                End If
            Next
        End Sub

        ''' <summary>
        ''' The narrowest a field is allowed to get. Two characters' worth of text is not a
        ''' comfortable click target, and a row of stubs reads as a broken layout rather than a
        ''' set of short fields.
        ''' </summary>
        Private Const MinimumSizedFieldWidth As Integer = 44

        ''' <summary>
        ''' Narrows a box towards what its column can hold.
        '''
        ''' A State that takes two characters and a Notes that takes 255 are the same 320 pixels
        ''' wide today, which tells the reader nothing about either. The width is a hint, never a
        ''' promise: the font is proportional, so a measured average is right for ordinary text
        ''' and wrong for a field full of Ws.
        '''
        ''' It only ever shrinks. Growing a box could push it over the Zip Coder button, past the
        ''' edge of a two-column page, or over whatever a hand-written page put beside it - and
        ''' the width the page chose is a deliberate statement that this cannot know better than.
        ''' That also settles Registration_U, which sizes State and Zip by hand: the measurement
        ''' agrees with it or is ignored.
        '''
        ''' Multiline boxes are left alone. Their size says how many lines to show, which has
        ''' nothing to do with how many characters the column holds.
        ''' </summary>
        Private Sub SizeControlToColumnLength(ctrl As TextBoxBase, maxLength As Integer)
            If maxLength <= 0 OrElse ctrl.Multiline OrElse ctrl.Width <= MinimumSizedFieldWidth Then Return

            ' Measured rather than multiplied by a constant, so it follows the page's font and DPI
            ' instead of assuming this machine's. Capped at 40 characters because anything longer
            ' already exceeds the width the page gave the box and would be clamped away.
            Dim sample As New String("n"c, Math.Min(maxLength, 40))
            Dim measured = TextRenderer.MeasureText(sample, ctrl.Font).Width + 12

            Dim sized = Math.Max(MinimumSizedFieldWidth, Math.Min(measured, ctrl.Width))
            If sized = ctrl.Width Then Return

            ctrl.Width = sized

            ' The required border sits one pixel outside its field, so it has to follow or it is
            ' left framing empty space to the right of the box it belongs to.
            Dim border As Panel = Nothing
            If requiredBorderPanels.TryGetValue(ctrl, border) AndAlso border IsNot Nothing Then
                border.Size = New Size(ctrl.Width + 2, ctrl.Height + 2)
            End If
        End Sub

        Private Sub NormalizeTextControl(ctrl As TextBoxBase,
                                         controlMap As Dictionary(Of String, String),
                                         columnLengths As Dictionary(Of String, Integer),
                                         Optional sizeToColumnLength As Boolean = False)
            Dim textValue = If(ctrl.Text, String.Empty).Trim()
            Dim maxLength = ResolveColumnMaxLength(ctrl.Name, controlMap, columnLengths)

            If maxLength > 0 AndAlso textValue.Length > maxLength Then
                textValue = textValue.Substring(0, maxLength)
            End If

            ctrl.Text = textValue

            If TypeOf ctrl Is TextBox AndAlso maxLength > 0 Then
                DirectCast(ctrl, TextBox).MaxLength = maxLength
            ElseIf TypeOf ctrl Is RichTextBox AndAlso maxLength > 0 Then
                DirectCast(ctrl, RichTextBox).MaxLength = maxLength
            End If

            If sizeToColumnLength Then SizeControlToColumnLength(ctrl, maxLength)
        End Sub

        Private Function ResolveColumnMaxLength(controlName As String,
                                                controlMap As Dictionary(Of String, String),
                                                columnLengths As Dictionary(Of String, Integer)) As Integer
            Dim mappedColumn As String = Nothing
            If controlMap IsNot Nothing AndAlso controlMap.TryGetValue(controlName, mappedColumn) AndAlso
               Not String.IsNullOrWhiteSpace(mappedColumn) Then
                If columnLengths.ContainsKey(mappedColumn) Then
                    Return columnLengths(mappedColumn)
                End If
            End If

            Dim fallbackColumn = InferColumnNameFromControlName(controlName)
            If fallbackColumn <> String.Empty AndAlso columnLengths.ContainsKey(fallbackColumn) Then
                Return columnLengths(fallbackColumn)
            End If

            Return 0
        End Function

        Private Function InferColumnNameFromControlName(controlName As String) As String
            If String.IsNullOrWhiteSpace(controlName) Then
                Return String.Empty
            End If

            Dim prefixes = New String() {"TextBox_", "MaskedTextBox_", "RichTextBox_"}
            For Each prefix In prefixes
                If controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return controlName.Substring(prefix.Length)
                End If
            Next

            Return String.Empty
        End Function

        ''' <summary>
        ''' Controls this page carries deliberately that are not columns of its own table.
        ''' </summary>
        Private ReadOnly declaredUnboundControls As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' Declares a field-shaped control that is not a column of this page's table, so the
        ''' unmapped-field check does not report it.
        '''
        ''' The reason is required and is shown in the report, because an undocumented exception is
        ''' indistinguishable from the defect this check exists to find. Call it before the record
        ''' is bound.
        ''' </summary>
        Protected Sub DeclareUnboundField(controlName As String, reason As String)
            If String.IsNullOrWhiteSpace(controlName) Then
                Throw New ArgumentException("An unbound field declaration needs the control name.", NameOf(controlName))
            End If

            If String.IsNullOrWhiteSpace(reason) Then
                Throw New ArgumentException("An unbound field declaration needs a reason.", NameOf(reason))
            End If

            declaredUnboundControls.Add(controlName.Trim())
        End Sub

        ''' <summary>
        ''' Reports controls whose name maps to no column, and stops the page saving.
        '''
        ''' Field permissions are derived from control names, so a control named for a column that
        ''' does not exist silently receives nothing - no caption, no required marker, no hiding.
        ''' The value it holds cannot round-trip either, which is why the save is blocked rather
        ''' than merely flagged: a page that looks like it saved and did not is worse than one that
        ''' says it cannot.
        '''
        ''' An administrator gets the detail and can copy it. Everyone else gets a page that plainly
        ''' shows which field is unavailable and a Help Desk to report it to.
        ''' </summary>
        Private Sub ReportUnmappedFieldControls()
            Dim unmapped = DataAccess.FindUnmappedFieldControls(Me, ResolveTableNameForConstraints(), declaredUnboundControls)
            If unmapped.Count = 0 Then Return

            For Each controlName In unmapped
                ReplaceUnmappedControlWithPlaceholder(controlName)
            Next

            okButton.Enabled = False
            Dim unavailableToolTip As New ToolTip()
            unavailableToolTip.SetToolTip(okButton,
                                          "SAVE IS NOT AVAILABLE, FIELD ON PAGE NOT MAPPED." & vbCrLf & vbCrLf &
                                          "PLEASE REPORT IT TO THE HELP DESK")

            If Not IsAdministratorSession() Then Return

            Dim report As New System.Text.StringBuilder()
            report.AppendLine("PAGE: " & GetPageName())
            report.AppendLine("TABLE: " & ResolveTableNameForConstraints())
            report.AppendLine()
            report.AppendLine("These controls are named for a column the table does not have, so no")
            report.AppendLine("field permission can reach them and their values cannot be saved:")
            report.AppendLine()
            For Each controlName In unmapped
                report.AppendLine("    " & controlName & "   ->   no column " &
                                  DataAccess.ColumnNameFromControlName(controlName))
            Next
            report.AppendLine()
            report.AppendLine("Either rename the control to match its column, or declare it on the page")
            report.AppendLine("with DeclareUnboundField if it is deliberately not a column of this table.")

            ShowUnmappedFieldReport(report.ToString())
        End Sub

        Private Shared Function IsAdministratorSession() As Boolean
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return False
            Dim session = SessionState.Current.Value
            Return session.IsApplicationAdminRole OrElse session.IsCompanyAdminRole
        End Function

        ''' <summary>
        ''' Puts an N/A marker where the control was, keeping the label so the row still reads as a
        ''' field rather than silently vanishing.
        ''' </summary>
        Private Sub ReplaceUnmappedControlWithPlaceholder(controlName As String)
            Dim matches = Controls.Find(controlName, True)
            If matches.Length = 0 Then Return

            Dim ctrl = matches(0)
            Dim placeholder As New Label() With {
                .Name = "Label_Unmapped_" & controlName,
                .Text = "N/A",
                .Location = ctrl.Location,
                .Size = ctrl.Size,
                .TextAlign = ContentAlignment.MiddleLeft,
                .BackColor = SystemColors.Control,
                .ForeColor = SystemColors.GrayText,
                .BorderStyle = BorderStyle.FixedSingle
            }

            Dim host = If(ctrl.Parent, CType(Me, Control))
            host.Controls.Add(placeholder)
            placeholder.BringToFront()
            ctrl.Visible = False
        End Sub

        Private Sub ShowUnmappedFieldReport(reportText As String)
            Using dialog As New Form() With {
                .Text = "Fields Not Mapped - " & GetPageName(),
                .StartPosition = FormStartPosition.CenterParent,
                .Size = New Size(640, 420),
                .MinimizeBox = False,
                .MaximizeBox = False
            }
                Dim body As New TextBox() With {
                    .Multiline = True,
                    .ReadOnly = True,
                    .ScrollBars = ScrollBars.Vertical,
                    .Dock = DockStyle.Fill,
                    .Text = reportText,
                    .BackColor = Color.White
                }

                Dim buttonRow As New Panel() With {.Dock = DockStyle.Bottom, .Height = 52}

                Dim copyButton As New Button() With {
                    .Text = "Copy",
                    .Size = New Size(120, 32),
                    .Location = New Point(12, 10)
                }
                AddHandler copyButton.Click,
                    Sub()
                        Clipboard.SetText(reportText)
                        copyButton.Text = "Copied"
                    End Sub

                Dim closeButton As New Button() With {
                    .Text = "Close",
                    .Size = New Size(120, 32),
                    .Location = New Point(dialog.ClientSize.Width - 132, 10),
                    .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                    .DialogResult = DialogResult.OK
                }

                buttonRow.Controls.AddRange({copyButton, closeButton})
                dialog.Controls.Add(body)
                dialog.Controls.Add(buttonRow)
                dialog.AcceptButton = closeButton
                dialog.ShowDialog(Me)
            End Using
        End Sub

        ''' <summary>
        ''' This page's browse partner, by name: FW_Employees_U to FW_Employees_B.
        '''
        ''' FW_Pages is keyed by browse page name, so a maintenance page has no row of its own and
        ''' takes the caption of the page it was opened from. A page whose partner is not named that
        ''' way overrides BuildMaintenanceTitle instead - the convention is a default, not a rule.
        ''' </summary>
        Protected Overridable Function BrowsePartnerPageName() As String
            Dim pageName = If(GetPageName(), String.Empty).Trim()
            If Not pageName.EndsWith("_U", StringComparison.OrdinalIgnoreCase) Then
                Return String.Empty
            End If

            Return pageName.Substring(0, pageName.Length - 2) & "_B"
        End Function

        Private Function ResolveTableNameForConstraints() As String
            Dim tableName = If(GetTableNameOverride(), String.Empty).Trim()
            If tableName <> String.Empty Then
                Return tableName
            End If

            Dim pageName = GetPageName()
            If pageName.EndsWith("_U", StringComparison.OrdinalIgnoreCase) Then
                Dim baseName = pageName.Substring(0, pageName.Length - 2)
                If baseName <> String.Empty Then
                    Return "FW_" & baseName
                End If
            End If

            Return String.Empty
        End Function

        Private Function ResolveOperationType(beforeRecordKey As String) As String
            If String.Equals(OkButtonText(), "Delete", StringComparison.OrdinalIgnoreCase) Then
                Return "Delete"
            End If

            Dim parsedId As Integer
            If Integer.TryParse(beforeRecordKey, parsedId) AndAlso parsedId > 0 Then
                Return "Modify"
            End If

            Return "Create"
        End Function

        Private Function ResolveRecordKeyFromControls() As String
            ' TextBox_ID stays in the list: the key column is being renamed table by table, so a
            ' page whose table still has a bare ID has to keep being recognised.
            Dim keyControlNames = New String() {"TextBox_EntityID", "TextBox_ID", "TextBox_UserID", "TextBox_RegistrationID"}
            For Each keyControlName In keyControlNames
                Dim matches = Me.Controls.Find(keyControlName, True)
                If matches Is Nothing OrElse matches.Length = 0 Then
                    Continue For
                End If

                Dim textValue = If(matches(0).Text, String.Empty).Trim()
                If textValue <> String.Empty Then
                    Return textValue
                End If
            Next

            Return String.Empty
        End Function

        Private Function ResolveRecordKeyForAudit() As String
            Dim overrideValue = If(ResolveAuditRecordKey(), String.Empty).Trim()
            If overrideValue <> String.Empty Then
                Return overrideValue
            End If

            Return ResolveRecordKeyFromControls()
        End Function

        Private Function CaptureControlSnapshotJson() As String
            Dim snapshot As New SortedDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(Me, allControls)

            For Each ctrl In allControls
                If String.IsNullOrWhiteSpace(ctrl.Name) Then
                    Continue For
                End If

                If ctrl.Name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    snapshot(ctrl.Name) = "[REDACTED]"
                    Continue For
                End If

                If TypeOf ctrl Is TextBoxBase Then
                    snapshot(ctrl.Name) = DirectCast(ctrl, TextBoxBase).Text
                ElseIf TypeOf ctrl Is ComboBox Then
                    Dim combo = DirectCast(ctrl, ComboBox)
                    Dim selectedDisplayText = ResolveComboDisplayText(combo)
                    snapshot(ctrl.Name) = selectedDisplayText
                ElseIf TypeOf ctrl Is CheckBox Then
                    snapshot(ctrl.Name) = DirectCast(ctrl, CheckBox).Checked.ToString()
                ElseIf TypeOf ctrl Is DateTimePicker Then
                    snapshot(ctrl.Name) = DateFieldSnapshot(DirectCast(ctrl, DateTimePicker))
                ElseIf TypeOf ctrl Is NumericUpDown Then
                    snapshot(ctrl.Name) = DirectCast(ctrl, NumericUpDown).Value.ToString()
                End If
            Next

            Return JsonSerializer.Serialize(snapshot)
        End Function

        Private Function CapturePendingRecordSnapshotJson() As String
            Dim snapshot As New SortedDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(Me, allControls)

            For Each ctrl In allControls
                Dim fieldName = ResolvePendingSnapshotFieldName(ctrl)
                If String.IsNullOrWhiteSpace(fieldName) Then
                    Continue For
                End If

                Dim controlName = If(ctrl.Name, String.Empty)
                If fieldName.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   controlName.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    snapshot(fieldName) = CaptureSensitivePendingSnapshotValue(ctrl)
                    Continue For
                End If

                snapshot(fieldName) = CapturePendingSnapshotValue(ctrl)
            Next

            Return JsonSerializer.Serialize(snapshot)
        End Function

        Private Shared Function ResolvePendingSnapshotFieldName(control As Control) As String
            If control Is Nothing Then
                Return String.Empty
            End If

            For Each binding As Binding In control.DataBindings
                Dim fieldName = If(binding.BindingMemberInfo.BindingField, String.Empty).Trim()
                If fieldName <> String.Empty Then
                    Return fieldName
                End If
            Next

            Return InferPendingSnapshotFieldName(control.Name)
        End Function

        Private Shared Function InferPendingSnapshotFieldName(controlName As String) As String
            If String.IsNullOrWhiteSpace(controlName) Then
                Return String.Empty
            End If

            Dim prefixes = New String() {"TextBox_", "MaskedTextBox_", "RichTextBox_", "ComboBox_", "CheckBox_", "DateTimePicker_", "NumericUpDown_"}
            For Each prefix In prefixes
                If controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return controlName.Substring(prefix.Length)
                End If
            Next

            Return String.Empty
        End Function

        Private Shared Function CapturePendingSnapshotValue(control As Control) As String
            If TypeOf control Is TextBoxBase Then
                Return If(DirectCast(control, TextBoxBase).Text, String.Empty).Trim()
            End If

            If TypeOf control Is ComboBox Then
                Dim combo = DirectCast(control, ComboBox)
                If combo.SelectedValue IsNot Nothing AndAlso Not IsDBNull(combo.SelectedValue) Then
                    Return combo.SelectedValue.ToString().Trim()
                End If

                Return ResolveComboDisplayText(combo)
            End If

            If TypeOf control Is CheckBox Then
                Return DirectCast(control, CheckBox).Checked.ToString()
            End If

            If TypeOf control Is DateTimePicker Then
                Return DateFieldSnapshot(DirectCast(control, DateTimePicker))
            End If

            If TypeOf control Is NumericUpDown Then
                Return DirectCast(control, NumericUpDown).Value.ToString(Globalization.CultureInfo.InvariantCulture)
            End If

            If TypeOf control Is Label Then
                Return If(DirectCast(control, Label).Text, String.Empty).Trim()
            End If

            Return If(control.Text, String.Empty).Trim()
        End Function

        Private Shared Function CaptureSensitivePendingSnapshotValue(control As Control) As String
            Dim value = CapturePendingSnapshotValue(control)
            Dim hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))
            Return "[REDACTED:" & Convert.ToBase64String(hashBytes) & "]"
        End Function

        Private Shared Function ResolveComboDisplayText(combo As ComboBox) As String
            If combo Is Nothing Then
                Return String.Empty
            End If

            Dim text = If(combo.Text, String.Empty).Trim()
            If text <> String.Empty Then
                Return text
            End If

            Dim selectedRowView = TryCast(combo.SelectedItem, DataRowView)
            If selectedRowView IsNot Nothing Then
                Dim displayMember = If(combo.DisplayMember, String.Empty).Trim()
                If displayMember <> String.Empty AndAlso selectedRowView.Row IsNot Nothing AndAlso selectedRowView.Row.Table IsNot Nothing AndAlso selectedRowView.Row.Table.Columns.Contains(displayMember) Then
                    Return If(selectedRowView(displayMember), String.Empty).ToString().Trim()
                End If

                Return selectedRowView.Row(0).ToString().Trim()
            End If

            Return String.Empty
        End Function

        ' â”€â”€ Shared helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        ''' <summary>
        ''' Creates a bound selection field. The same contract as AddField: the field name decides
        ''' the control name and the label, so a combo cannot be misnamed relative to its column.
        ''' Fill it through ConfigureLookupCombo.
        ''' </summary>
        ''' The label a field helper created, by field name. Nothing else needs to know how labels
        ''' are named.
        Protected Function FieldLabel(fieldName As String) As Label
            If String.IsNullOrWhiteSpace(fieldName) Then Return Nothing
            Dim matches = Controls.Find("Label_" & fieldName.Trim(), True)
            If matches.Length = 0 Then Return Nothing
            Return TryCast(matches(0), Label)
        End Function

        Protected Function AddComboField(caption As String, y As Integer,
                                         Optional required As Boolean = False,
                                         Optional fieldLeft As Integer = 20,
                                         Optional fieldWidth As Integer = 320,
                                         Optional labelText As String = Nothing) As ComboBox
            Dim lbl As New Label() With {
                .Name = "Label_" & caption,
                .Text = If(String.IsNullOrWhiteSpace(labelText), ToPascalCaseDisplay(caption), labelText),
                .Location = New Point(fieldLeft, y),
                .Size = New Size(120, 26),
                .TextAlign = ContentAlignment.MiddleLeft
            }

            If required Then
                If Not lbl.Text.EndsWith(" *", StringComparison.Ordinal) Then
                    lbl.Text &= " *"
                End If

                ' App Admin required: declared here on the page, so it applies to every role.
                lbl.BackColor = AppAdminRequiredBackColor
            End If

            Me.Controls.Add(lbl)

            Dim combo As New ComboBox() With {
                .Name = "ComboBox_" & caption,
                .Location = New Point(fieldLeft + 130, y),
                .Size = New Size(fieldWidth, 26),
                .DropDownStyle = ComboBoxStyle.DropDownList
            }

            Me.Controls.Add(combo)

            If required Then
                combo.Tag = "Required"

                Dim borderPanel As New Panel() With {
                    .BackColor = SystemColors.Control,
                    .Location = New Point(combo.Left - 1, combo.Top - 1),
                    .Size = New Size(combo.Width + 2, combo.Height + 2),
                    .Tag = "LocalRequiredBorder_" & caption
                }

                Me.Controls.Add(borderPanel)
                borderPanel.Visible = False
                borderPanel.BringToFront()
                combo.BringToFront()
                requiredBorderPanels(combo) = borderPanel
                WatchRequiredHover(combo)

                Dim refresh = Sub(s As Object, e As EventArgs)
                                  If Not loading Then MarkRequiredTouched(combo)
                                  RefreshLocalRequiredBorders()
                              End Sub
                AddHandler combo.SelectedIndexChanged, refresh
                AddHandler combo.TextChanged, refresh
            End If

            Return combo
        End Function

        ''' <summary>
        ''' Creates a yes-or-no field. The control becomes CheckBox_&lt;field&gt; and its label
        ''' Label_&lt;field&gt;, the same naming every other field helper follows.
        '''
        ''' A bit column used to get a text box, which asked somebody to type "True" and accepted
        ''' "Ture" - and a checkbox is the one control where the value and the way it is shown
        ''' cannot disagree.
        '''
        ''' No required marker. A check box always holds one of its two values, so there is no
        ''' such thing as leaving it blank, and an asterisk on one would promise a rule that can
        ''' never fire. A column that must be True is a business rule, not a required field.
        ''' </summary>
        Protected Function AddCheckField(caption As String, y As Integer,
                                         Optional fieldLeft As Integer = 20,
                                         Optional labelText As String = Nothing) As CheckBox
            Dim lbl As New Label() With {
                .Name = "Label_" & caption,
                .Text = If(String.IsNullOrWhiteSpace(labelText), ToPascalCaseDisplay(caption), labelText),
                .Location = New Point(fieldLeft, y),
                .Size = New Size(120, 26),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            Me.Controls.Add(lbl)

            ' Sits where a text box would start, so a column of fields lines up whatever mix of
            ' controls it holds.
            '
            ' Exactly y, not y + 3. The framework decides what a row is by comparing Top, and a
            ' check box nudged down to look centred was not on its own row: permission collapse
            ' and HideFieldAndCloseGap both read it as the row below, hiding the label while the
            ' box stayed behind. The box is given the full row height instead and centres its
            ' glyph within it, which looks the same and is true.
            Dim box As New CheckBox() With {
                .Name = "CheckBox_" & caption,
                .Location = New Point(fieldLeft + 130, y),
                .Size = New Size(24, 26),
                .CheckAlign = ContentAlignment.MiddleLeft,
                .UseVisualStyleBackColor = True
            }
            Me.Controls.Add(box)
            Return box
        End Function

        ''' <summary>What a check box contributes to a save.</summary>
        Protected Shared Function CheckFieldValue(box As CheckBox) As Object
            If box Is Nothing Then Return False
            Return box.Checked
        End Function

        ''' <summary>
        ''' Puts a stored value into a check box.
        '''
        ''' A null reads as False. The column allows one and the control cannot show it, and
        ''' "not set" is far closer to No than to Yes for every flag this framework has.
        ''' </summary>
        Protected Shared Sub SetCheckField(box As CheckBox, value As Object)
            If box Is Nothing Then Return

            If value Is Nothing OrElse Convert.IsDBNull(value) Then
                box.Checked = False
                Return
            End If

            Dim flag As Boolean
            If Boolean.TryParse(Convert.ToString(value), flag) Then
                box.Checked = flag
                Return
            End If

            Dim number As Integer
            If Integer.TryParse(Convert.ToString(value), number) Then box.Checked = number <> 0
        End Sub

        ''' <summary>
        ''' Creates a date field. The control becomes DateTimePicker_&lt;field&gt; and its label
        ''' Label_&lt;field&gt;, the same naming every other field helper follows.
        '''
        ''' A date column used to get a plain text box, which is how an empty Termination Date
        ''' reached a datetime parameter as "" and failed the save with a message naming no field.
        ''' A picker cannot produce that value, or 31 February, or a month spelled out in a
        ''' language the parser does not read.
        '''
        ''' It still accepts typing. The segments take digits and arrow keys, so somebody entering
        ''' forty records is not made to click through a calendar - the constraint is on what can
        ''' be expressed, not on how it is entered. That is the distinction worth keeping: free
        ''' text is the problem, typing is not.
        '''
        ''' Everything else here mirrors AddField exactly - the asterisk, the App Admin blue, the
        ''' required border, the hover watch - because a required date has to behave like every
        ''' other required field, and a second implementation is how one of them stops doing so.
        ''' </summary>
        ''' <param name="nullable">
        ''' Whether "no date" is a value the column accepts. Shows the picker's own check box,
        ''' which is the built-in way to say it: unchecked greys the date and reads as null. A NOT
        ''' NULL column gets no check box, because there is nothing for it to express.
        ''' </param>
        ''' <param name="showTime">
        ''' Whether the time is part of the value. Off by default: a hire date shown as
        ''' "15/03/2026 00:00" puts a time on screen that nobody entered and nobody means.
        ''' </param>
        Protected Function AddDateField(caption As String, y As Integer,
                                        Optional required As Boolean = False,
                                        Optional fieldLeft As Integer = 20,
                                        Optional nullable As Boolean = False,
                                        Optional showTime As Boolean = False,
                                        Optional labelText As String = Nothing) As DateTimePicker
            Dim lbl As New Label() With {
                .Name = "Label_" & caption,
                .Text = If(String.IsNullOrWhiteSpace(labelText), ToPascalCaseDisplay(caption), labelText),
                .Location = New Point(fieldLeft, y),
                .Size = New Size(120, 26),
                .TextAlign = ContentAlignment.MiddleLeft
            }

            If required Then
                If Not lbl.Text.EndsWith(" *", StringComparison.Ordinal) Then
                    lbl.Text &= " *"
                End If

                ' The same ARGB ShouldSkipBrRequiredStyling reads. See AddField.
                lbl.BackColor = AppAdminRequiredBackColor
            End If

            Me.Controls.Add(lbl)

            ' Wide enough for the format it shows, and no wider. The check box adds its own room.
            Dim pickerWidth = If(showTime, 200, 130) + If(nullable, 22, 0)

            Dim picker As New DateTimePicker() With {
                .Name = "DateTimePicker_" & caption,
                .Location = New Point(fieldLeft + 130, y),
                .Size = New Size(pickerWidth, 26),
                .ShowCheckBox = nullable
            }
            ApplyDateFieldFormat(picker, showTime)

            ' An unticked date shows nothing at all rather than a greyed-out date. A greyed date
            ' still reads as a value - somebody looking at a Termination Date of 09/14/2026 has
            ' to notice a tick to know the person has not left - and the grey is easy to miss
            ' next to a field that is legitimately read-only.
            '
            ' A DateTimePicker cannot be empty, so the display is emptied instead: the format is
            ' swapped for a blank one while the box is unticked, and put back when it is ticked.
            ' The real format is kept here because ValueChanged has no way to recover it, and
            ' recomputing it would need showTime, which only this method knows.
            dateFieldFormats(picker) = picker.CustomFormat
            AddHandler picker.ValueChanged, Sub(sender As Object, e As EventArgs) RefreshDateFieldDisplay(picker)
            RefreshDateFieldDisplay(picker)

            Me.Controls.Add(picker)

            If required Then
                picker.Tag = "Required"

                Dim borderPanel As New Panel() With {
                    .BackColor = SystemColors.Control,
                    .Location = New Point(picker.Left - 1, picker.Top - 1),
                    .Size = New Size(picker.Width + 2, picker.Height + 2),
                    .Tag = "LocalRequiredBorder_" & caption
                }

                Me.Controls.Add(borderPanel)
                borderPanel.Visible = False
                borderPanel.BringToFront()
                picker.BringToFront()
                requiredBorderPanels(picker) = borderPanel
                WatchRequiredHover(picker)

                Dim refresh = Sub(s As Object, e As EventArgs)
                                  If Not loading Then MarkRequiredTouched(picker)
                                  RefreshLocalRequiredBorders()
                              End Sub
                AddHandler picker.ValueChanged, refresh
            End If

            Return picker
        End Function

        ''' <summary>
        ''' How a date field displays its value - the one place the format is decided.
        '''
        ''' Explicit rather than DateTimePickerFormat.Short, which reads the machine's locale.
        ''' Under VirtualUI the machine is the server, so Short would show every user in every
        ''' country whatever the server happened to be installed as - a format nobody chose, and
        ''' one with no visible reason for being what it is.
        '''
        ''' The patterns come from the registration, resolved at login and answered from the
        ''' session. DisplayFormats owns that, and the grids will read the same two patterns, so
        ''' a page and the grid that lists it cannot write a date two ways.
        ''' </summary>
        ''' <summary>
        ''' Each date field's real display format, so it can be put back after the field has been
        ''' blanked. Keyed by the control, like requiredBorderPanels, and per page - the pages
        ''' come and go with their controls.
        ''' </summary>
        Private ReadOnly dateFieldFormats As New Dictionary(Of DateTimePicker, String)()

        ''' <summary>The blank a DateTimePicker shows when its value is null. A space, because an
        ''' empty custom format is ignored and the control falls back to the short date.</summary>
        Private Const EmptyDateFormat As String = " "

        ''' <summary>
        ''' Shows or hides a date field's value according to its check box.
        '''
        ''' Called whenever the box is toggled, by the user or in code, so the display and the
        ''' value can never disagree about whether there is a date.
        ''' </summary>
        Private Sub RefreshDateFieldDisplay(picker As DateTimePicker)
            If picker Is Nothing Then Return

            Dim realFormat As String = Nothing
            If Not dateFieldFormats.TryGetValue(picker, realFormat) OrElse String.IsNullOrEmpty(realFormat) Then Return

            Dim showsNothing = picker.ShowCheckBox AndAlso Not picker.Checked
            Dim wanted = If(showsNothing, EmptyDateFormat, realFormat)
            If Not String.Equals(picker.CustomFormat, wanted, StringComparison.Ordinal) Then
                picker.CustomFormat = wanted
            End If
        End Sub

        Protected Shared Sub ApplyDateFieldFormat(picker As DateTimePicker, showTime As Boolean)
            If picker Is Nothing Then Return

            picker.Format = DateTimePickerFormat.Custom
            picker.CustomFormat = If(showTime, DisplayFormats.DateTimePattern(), DisplayFormats.DatePattern())
        End Sub

        ''' <summary>
        ''' What a date field contributes to a save: the date, or Nothing for no date.
        '''
        ''' Shared and here rather than written into each generated page, so "unchecked means
        ''' null" is stated once. TrySaveGeneratedPageRecord turns Nothing into DBNull.
        ''' </summary>
        ''' <summary>
        ''' Hides a field and pulls the rest of its column up over the space it leaves.
        '''
        ''' Plain Visible = False leaves a hole. The row-collapse the permission system uses
        ''' cannot help here: it only collapses a row when every control on it is hidden, and on
        ''' a two-column page the other column keeps the row alive. A field hidden from the
        ''' middle of one column therefore sits there as white space, which reads as a control
        ''' that failed to draw.
        '''
        ''' The distance to close is measured from the next row in the same column rather than
        ''' assumed. The generator spaces rows 42 apart today; a constant here would be a second
        ''' copy of that figure, wrong the moment either changed.
        '''
        ''' Only field controls move. The role grids, the Zip Coder button and anything else a
        ''' page has put below the fields stay where they are, because they are positioned from
        ''' where the fields end rather than from each other.
        ''' </summary>
        Protected Sub HideFieldAndCloseGap(fieldName As String)
            If String.IsNullOrWhiteSpace(fieldName) Then Return

            Dim label = FieldLabel(fieldName)
            If label Is Nothing Then Return

            Dim rowTop = label.Top
            Dim columnLeft = label.Left

            ' Everything on the page that belongs to a field, so the grids and buttons are left
            ' out of both the measuring and the moving.
            Dim fieldControls = Controls.Cast(Of Control)().
                Where(Function(item) IsFieldControl(item)).
                ToList()

            ' The same column, judged by where its labels start. A control belongs to the column
            ' its label anchors, which is what keeps two columns from pulling each other about.
            Dim inColumn = fieldControls.Where(Function(item) item.Left >= columnLeft AndAlso
                                                              item.Left < columnLeft + 460).ToList()

            ' Both sets are taken before anything moves. Shifting first and hiding afterwards
            ' hides whatever has just moved into the vacated row instead of the field that was
            ' asked for - the row below disappears and the gap stays exactly where it was.
            Dim onRow = inColumn.Where(Function(item) item.Top = rowTop).ToList()
            Dim below = inColumn.Where(Function(item) item.Top > rowTop).ToList()

            For Each hidden In onRow
                hidden.Visible = False
            Next

            If below.Count > 0 Then
                Dim shift = below.Min(Function(item) item.Top) - rowTop
                For Each moved In below
                    moved.Top -= shift
                Next
            End If
        End Sub

        ''' <summary>
        ''' A date field as the unsaved-changes check sees it.
        '''
        ''' The value that would be saved, not the value the control happens to hold. Both
        ''' snapshot paths recorded Value alone and ignored Checked, which was wrong in two
        ''' directions at once.
        '''
        ''' Ticking or unticking without touching the date changed nothing in the snapshot, so
        ''' clearing a Termination Date and pressing Cancel reported nothing to discard. And
        ''' unticking after editing the date left the edited value behind, so a field returned to
        ''' null still read as changed - which is how this was found.
        '''
        ''' One string for "no date", because two nulls have to compare equal however the control
        ''' arrived at them.
        ''' </summary>
        Private Shared Function DateFieldSnapshot(picker As DateTimePicker) As String
            If picker Is Nothing Then Return String.Empty
            If picker.ShowCheckBox AndAlso Not picker.Checked Then Return "(no date)"
            Return picker.Value.ToString("o", Globalization.CultureInfo.InvariantCulture)
        End Function

        Protected Shared Function DateFieldValue(picker As DateTimePicker) As Object
            If picker Is Nothing Then Return Nothing
            If picker.ShowCheckBox AndAlso Not picker.Checked Then Return Nothing
            Return picker.Value
        End Function

        ''' <summary>
        ''' Puts a stored value into a date field, or clears it.
        '''
        ''' A null with no check box to express it has to land somewhere, and today is the least
        ''' surprising place - the alternative is 01/01/1753, which reads as data rather than as
        ''' an empty field. A column that can be null should be declared nullable so the check box
        ''' exists to say so.
        ''' </summary>
        Protected Sub SetDateField(picker As DateTimePicker, value As Object)
            If picker Is Nothing Then Return

            If value Is Nothing OrElse Convert.IsDBNull(value) Then
                If picker.ShowCheckBox Then
                    SetDateFieldChecked(picker, False)
                Else
                    picker.Value = Date.Today
                End If
                Return
            End If

            Dim stored As Date
            If Not Date.TryParse(Convert.ToString(value, Globalization.CultureInfo.CurrentCulture), stored) Then
                If TypeOf value Is Date Then
                    stored = DirectCast(value, Date)
                Else
                    Return
                End If
            End If

            ' Outside what the control can show is a data problem, not a reason to throw on load.
            If stored < picker.MinDate OrElse stored > picker.MaxDate Then Return

            picker.Value = stored
            If picker.ShowCheckBox Then SetDateFieldChecked(picker, True)
        End Sub

        ''' <summary>
        ''' Ticks or unticks a date field's check box so that it survives the control being shown.
        '''
        ''' DateTimePicker.Checked does not stick before the window handle exists. BindToForm runs
        ''' from the page's constructor, long before the form is displayed, so setting it there
        ''' looked right and did nothing: at handle creation the control initialises itself from
        ''' Value and comes up ticked. Every nullable date therefore opened as though it held
        ''' today's date, and a null Termination Date read as "terminated today" - wrong in the
        ''' most alarming possible direction, and it would have been saved that way on the next
        ''' Save.
        '''
        ''' Set now for the case where the handle already exists, and again when it is created.
        ''' The handler removes itself, so reloading a page cannot accumulate them.
        ''' </summary>
        Private Sub SetDateFieldChecked(picker As DateTimePicker, isChecked As Boolean)
            picker.Checked = isChecked
            RefreshDateFieldDisplay(picker)
            If picker.IsHandleCreated Then Return

            Dim reapply As EventHandler = Nothing
            reapply = Sub(sender As Object, e As EventArgs)
                          RemoveHandler picker.HandleCreated, reapply
                          picker.Checked = isChecked
                          RefreshDateFieldDisplay(picker)
                      End Sub
            AddHandler picker.HandleCreated, reapply
        End Sub

        ''' <summary>
        ''' Creates a bound text field. The field name is the single input: the control becomes
        ''' TextBox_&lt;field&gt; and its label Label_&lt;field&gt;, so the name a permission is keyed on can
        ''' never disagree with the control that carries it.
        ''' </summary>
        ''' <param name="multiline">A taller box for free text. The caller sets the height it wants.</param>
        Protected Function AddField(caption As String, y As Integer, [readOnly] As Boolean,
                                    Optional required As Boolean = False,
                                    Optional fieldLeft As Integer = 20,
                                    Optional multiline As Boolean = False,
                                    Optional fieldWidth As Integer = 320,
                                    Optional fieldHeight As Integer = 26,
                                    Optional labelText As String = Nothing) As TextBox
            Dim lbl As New Label() With {
                .Name = "Label_" & caption,
                .Text = If(String.IsNullOrWhiteSpace(labelText), ToPascalCaseDisplay(caption), labelText),
                .Location = New Point(fieldLeft, y),
                .Size = New Size(120, 26),
                .TextAlign = ContentAlignment.MiddleLeft
            }

            If required Then
                If Not lbl.Text.EndsWith(" *", StringComparison.Ordinal) Then
                    lbl.Text &= " *"
                End If

                ' App Admin required: declared here on the page, so it applies to every role. The
                ' blue label is not decoration - ShouldSkipBrRequiredStyling reads this exact ARGB
                ' and makes the permission path skip the field, which is how App Admin required
                ' takes precedence over the yellow FW_RoleFields required.
                lbl.BackColor = AppAdminRequiredBackColor
            End If

            Me.Controls.Add(lbl)

            Dim txt As New TextBox() With {
                .Name = "TextBox_" & caption,
                .Location = New Point(fieldLeft + 130, y),
                .Size = New Size(fieldWidth, If(multiline, fieldHeight, 26)),
                .Multiline = multiline,
                .ScrollBars = If(multiline, ScrollBars.Vertical, ScrollBars.None),
                .ReadOnly = [readOnly],
                .TabStop = Not [readOnly],
                .BorderStyle = BorderStyle.FixedSingle,
                .BackColor = SystemColors.Window
            }

            Me.Controls.Add(txt)

            If required Then
                txt.Tag = "Required"

                Dim borderPanel As New Panel() With {
                    .BackColor = SystemColors.Control,
                    .Location = New Point(txt.Left - 1, txt.Top - 1),
                    .Size = New Size(txt.Width + 2, txt.Height + 2),
                    .Tag = "LocalRequiredBorder_" & caption
                }

                Me.Controls.Add(borderPanel)
                borderPanel.Visible = False
                borderPanel.BringToFront()
                txt.BringToFront()
                requiredBorderPanels(txt) = borderPanel
                WatchRequiredHover(txt)
                AddHandler txt.TextChanged,
                    Sub(borderSender, borderEventArgs)
                        If Not loading Then MarkRequiredTouched(txt)
                        RefreshLocalRequiredBorders()
                    End Sub
            End If
            Return txt
        End Function

        ''' <summary>
        ''' Takes ownership of the required-border panels ApplyControlUpdates creates from
        ''' FW_RoleFields, so every required field on the page - combos included - follows the one
        ''' rule in RefreshLocalRequiredBorders rather than a second copy of it in data access.
        ''' </summary>
        Private Sub AdoptRequiredBorderPanels()
            Const tagPrefix As String = "RequiredBorder_"

            For Each candidate As Control In Me.Controls
                Dim panel = TryCast(candidate, Panel)
                If panel Is Nothing OrElse panel.Tag Is Nothing Then Continue For

                Dim tagText = panel.Tag.ToString()
                If Not tagText.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase) Then Continue For

                Dim matches = Me.Controls.Find(tagText.Substring(tagPrefix.Length), True)
                If matches.Length = 0 Then Continue For

                Dim field = matches(0)
                If requiredBorderPanels.ContainsKey(field) Then Continue For

                requiredBorderPanels(field) = panel
                WatchRequiredHover(field)

                Dim watched = field
                Dim refresh = Sub(s As Object, e As EventArgs)
                                  If Not loading Then MarkRequiredTouched(watched)
                                  RefreshLocalRequiredBorders()
                              End Sub

                AddHandler watched.TextChanged, refresh
                Dim combo = TryCast(watched, ComboBox)
                If combo IsNot Nothing Then AddHandler combo.SelectedIndexChanged, refresh
            Next
        End Sub

        Private Sub RefreshLocalRequiredBorders()
            For Each pair In requiredBorderPanels
                Dim field = pair.Key
                Dim border = pair.Value

                ' Follow the control. A page is free to move and resize its fields, and the border
                ' is behind whichever one it belongs to.
                If field.Parent Is border.Parent Then
                    border.Location = New Point(field.Left - 2, field.Top - 2)
                    border.Size = New Size(field.Width + 4, field.Height + 4)
                End If

                Dim showWarning = ShouldShowRequiredWarning(field)
                border.BackColor = If(showWarning, Color.Red, SystemColors.Control)
                border.Visible = showWarning
            Next
        End Sub

        ''' Re-seats the required borders behind their controls. Call after a layout pass.
        Protected Sub RefreshRequiredBorderGeometry()
            RefreshLocalRequiredBorders()
        End Sub

        Protected Sub ConfigureLookupCombo(combo As ComboBox,
                                           source As DataTable,
                                           valueMember As String,
                                           displayMember As String,
                                           selectedId As Integer,
                                           Optional placeholderText As String = "Make a Selection",
                                           Optional placeholderValue As Integer = 0)
            If combo Is Nothing Then
                Return
            End If

            If source Is Nothing OrElse source.Columns Is Nothing OrElse
               Not source.Columns.Contains(valueMember) OrElse
               Not source.Columns.Contains(displayMember) Then
                combo.DataSource = Nothing
                combo.Items.Clear()
                Return
            End If

            Dim table = source.Copy()
            Dim placeholderRow = table.NewRow()
            placeholderRow(valueMember) = placeholderValue
            placeholderRow(displayMember) = placeholderText
            table.Rows.InsertAt(placeholderRow, 0)

            combo.DataSource = table
            combo.DisplayMember = displayMember
            combo.ValueMember = valueMember

            If selectedId > 0 Then
                combo.SelectedValue = selectedId
                If combo.SelectedIndex < 0 Then
                    combo.SelectedIndex = 0
                End If
            Else
                combo.SelectedValue = placeholderValue
                If combo.SelectedIndex < 0 Then
                    combo.SelectedIndex = 0
                End If
                If String.IsNullOrWhiteSpace(combo.Text) Then
                    combo.Text = placeholderText
                End If
            End If

            ComboWidth.FitToContent(combo, table, displayMember)
        End Sub

        Protected Function GetComboSelectedIdOrZero(combo As ComboBox) As Integer
            If combo Is Nothing OrElse combo.SelectedValue Is Nothing Then
                Return 0
            End If

            Dim selectedId As Integer
            If Not Integer.TryParse(combo.SelectedValue.ToString(), selectedId) Then
                Return 0
            End If

            If selectedId <= 0 Then
                Return 0
            End If

            Return selectedId
        End Function

        ''' <summary>
        ''' The same selection, for a column rather than an Integer variable: no selection is
        ''' Nothing, which the parameter binder writes as NULL. Zero was never a real answer - it
        ''' points at a record that does not exist, which is why a declared foreign key rejects it -
        ''' and once stored it is indistinguishable from a value the user chose.
        ''' </summary>
        Protected Function GetComboSelectedIdOrNull(combo As ComboBox) As Object
            Dim selectedId = GetComboSelectedIdOrZero(combo)
            If selectedId <= 0 Then Return Nothing

            Return selectedId
        End Function

        ''' <summary>
        ''' Control captions are already field names, so the FW_ prefix is not stripped.
        ''' Formatting itself is owned by DisplayNameFormatter.
        ''' </summary>
        Protected Shared Function ToPascalCaseDisplay(name As String) As String
            If String.IsNullOrWhiteSpace(name) Then Return name
            Return DisplayNameFormatter.ToDisplayName(name, stripFrameworkPrefix:=False)
        End Function

        Protected Shared Function ParseIntOrZero(value As String) As Integer
            Dim parsed As Integer
            If Integer.TryParse(value, parsed) Then Return parsed
            Return 0
        End Function

    End Class

End Namespace
