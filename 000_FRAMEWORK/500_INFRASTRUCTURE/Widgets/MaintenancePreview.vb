Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' One answer to "what will this page look like", whether the page exists yet or not.
    '''
    ''' A page that has never been generated is drawn from the request: every field, at its placed
    ''' position, with an outline where companion code would go. A page that has been generated is
    ''' opened for real and then re-arranged to match the request - which is the better answer,
    ''' because everything a placement cannot know comes with it. The role grids under an employee's
    ''' fields, the Zip Coder button beside Zip, the Smarty lookup: all hand-written, none of them
    ''' derivable from a field list, and all of them already on the page.
    '''
    ''' A field that is in the request but was not there when the page was last generated is drawn
    ''' in, by the same routine that draws a whole preview. It looks like its neighbours because it
    ''' is made the same way; it is simply not bound to a column, like every other preview control.
    '''
    ''' The whole of it runs inside a ReadOnlyPreview scope. A compiled page is a working page, and
    ''' a working page writes - four of its seven write paths need no button pressed at all.
    ''' </summary>
    Public Module MaintenancePreview

        ''' <summary>
        ''' Shows the best preview available for this request. Never throws, and never shows a
        ''' window with nothing in it and no reason given.
        ''' </summary>
        Public Sub Show(owner As IWin32Window,
                        placedPage As PlacedPage,
                        pageName As String,
                        tableName As String,
                        user As UserContext)
            Dim pageType = FindPageType(pageName)

            If pageType Is Nothing Then
                ' Nothing compiled to borrow from. The layout preview is the whole answer, and for
                ' a page being designed it is not a lesser one - a page that has never been
                ' generated has no hand-written controls to miss.
                Dim preview As New LayoutPreviewForm(placedPage, pageName, tableName)
                preview.Show(owner)
                Return
            End If

            Dim page As Form = Nothing

            Try
                Using ReadOnlyPreview.Begin()
                    page = BuildPage(pageType, user)
                    page.Text = pageName & "  -  PREVIEW OF YOUR CHANGES"
                    page.StartPosition = FormStartPosition.CenterScreen

                    ' After the page has shown itself and finished its own layout, never before.
                    ' FW_Base_U does its collapse, tab order and zoom work from Shown through two
                    ' nested BeginInvoke calls, and anything moved earlier is moved back.
                    AddHandler page.Shown,
                        Sub(sender, e)
                            page.BeginInvoke(New Action(
                                Sub()
                                    page.BeginInvoke(New Action(
                                        Sub()
                                            page.BeginInvoke(New Action(Sub() Rearrange(page, placedPage, tableName)))
                                        End Sub))
                                End Sub))
                        End Sub

                    page.ShowDialog(owner)
                End Using
            Catch ex As Exception
                WideMessage.Show(owner,
                                 (pageName & " COULD NOT BE PREVIEWED." & Environment.NewLine & Environment.NewLine & ex.ToString()),
                                 "Preview",
                                 MessageBoxIcon.Error)
            Finally
                If page IsNot Nothing Then page.Dispose()
            End Try
        End Sub

        ''' <summary>The compiled page of that name, or nothing when it is not built yet.</summary>
        Private Function FindPageType(pageName As String) As Type
            If String.IsNullOrWhiteSpace(pageName) Then Return Nothing

            Return GetType(FW_Base_U).Assembly.
                GetTypes().
                FirstOrDefault(Function(candidate) String.Equals(candidate.Name, pageName.Trim(), StringComparison.OrdinalIgnoreCase) AndAlso
                                                   GetType(FW_Base_U).IsAssignableFrom(candidate))
        End Function

        ''' <summary>
        ''' Constructs a page, filling each constructor argument from its type. Every page has its
        ''' own signature - New(id, user, profile), New(registrationId, createNew), New(issueId,
        ''' registrationId, page) - so they are supplied by type rather than by name. The shortest
        ''' constructor wins, and a key of zero opens the page as a new record, which also settles
        ''' what happens on a table with no rows in it.
        ''' </summary>
        Private Function BuildPage(pageType As Type, user As UserContext) As Form
            Dim constructors = pageType.GetConstructors().
                OrderBy(Function(candidate) candidate.GetParameters().Length).
                ToList()

            If constructors.Count = 0 Then
                Throw New InvalidOperationException(pageType.Name & " has no public constructor.")
            End If

            Dim chosen = constructors.First()
            Dim arguments = chosen.GetParameters().
                Select(Function(parameter) ArgumentFor(parameter, user)).
                ToArray()

            Return DirectCast(chosen.Invoke(arguments), Form)
        End Function

        Private Function ArgumentFor(parameter As ParameterInfo, user As UserContext) As Object
            If parameter.ParameterType Is GetType(UserContext) Then Return user
            If parameter.ParameterType Is GetType(String) Then Return String.Empty
            If parameter.ParameterType Is GetType(Integer) Then Return 0
            If parameter.ParameterType Is GetType(Boolean) Then Return False
            Return Nothing
        End Function
        ''' <summary>
        ''' Moves the page's own field controls to where the request puts them, draws in the fields
        ''' the page does not have, hides the ones the request no longer wants, and takes everything
        ''' the companion added along with it.
        '''
        ''' A field is not one control. It is a label, an input, a focus border, sometimes a
        ''' required border, and sometimes a button that placed itself beside it - and all of them
        ''' have to move together. Moving only the input is what left a red bar at the old row and a
        ''' stray label under the one that took its place.
        ''' </summary>
        Private Sub Rearrange(page As Form, placedPage As PlacedPage, tableName As String)
            ' A page that opened zoomed has already scaled every control, and a placement is stated
            ' in design pixels. Comparing the two gave a shift of about minus fifty on a page where
            ' nothing had changed, and slid the role grids up under the OK button.
            '
            ' Taken back to 100 per cent for the re-arrangement and put back afterwards, rather
            ' than multiplying every coordinate here. PageZoom owns the scale rule; a second copy
            ' of it inside a preview is the one that would drift. The read-only latch stops the
            ' round trip touching the remembered zoom.
            Dim zoomFactor = PageZoom.CurrentFactor(page)
            Dim zoomed = Math.Abs(zoomFactor - 1.0F) > 0.001F
            If zoomed Then PageZoom.Apply(page, 1.0F)

            Dim columnLengths As Dictionary(Of String, Integer)
            Try
                columnLengths = DataAccess.GetTextColumnMaxLengths(tableName)
            Catch
                columnLengths = New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            End Try

            ' Where the page's fields currently stop. Measured rather than asked for, because
            ' GeneratedFieldsBottom belongs to the page and a preview has no business reaching into
            ' it - and measuring works just as well on a page that predates the split.
            ' Taken before anything moves, and used throughout. Asked again later, the test would
            ' be against a client height that has since changed - and while AutoScrollMinSize was
            ' briefly part of the test it was still (0,0), which made every button on the page an
            ' action button and swept the Zip Coder and the role transfer buttons into the OK row.
            Dim actionButtons = page.Controls.Cast(Of Control)().
                Where(Function(item) IsActionButton(page, item)).
                OrderBy(Function(item) item.Left).
                ToList()

            Dim fieldControls = FieldControlsOn(page)

            ' The top of the row after the last field, measured the same way the placement states
            ' it - max(Top) plus the pitch, never max(Bottom). A text box is 23 tall inside a 42
            ' pitch, so measuring the control bottom made every companion move about 19 pixels
            ' short and slid the role grids under the OK button.
            Dim oldFieldsBottom = If(fieldControls.Count = 0,
                                     0,
                                     fieldControls.Max(Function(item) item.Top) + MaintenanceLayout.RowPitch)

            ' The Zip Coder button sits beside whichever box holds Zip, placed against that box's
            ' final position. It has to travel with it, so which box it belongs to is worked out
            ' once, here, before anything moves.
            Dim zipButton = Find(page, ZipCoderController.ButtonName)
            Dim zipAnchor As Control = Nothing
            If zipButton IsNot Nothing Then
                zipAnchor = fieldControls.
                    Where(Function(item) item.Left < zipButton.Left AndAlso Math.Abs(item.Top - zipButton.Top) <= MaintenanceLayout.FieldHeight).
                    OrderByDescending(Function(item) item.Right).
                    FirstOrDefault()
            End If

            ' Everything the companion put below the fields, as one block.
            ' Page chrome is not companion furniture. The zoom read-out sits at the bottom left,
            ' below every field, so it joined the block and became its leftmost control - which
            ' made it the anchor the whole block was aligned by, and shunted the role grids
            ' sideways instead of the other way about.
            Dim companion = page.Controls.Cast(Of Control)().
                Where(Function(item) item.Top > oldFieldsBottom AndAlso
                                     Not actionButtons.Contains(item) AndAlso
                                     Not IsPageChrome(item)).
                ToList()

            Dim touched As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim matched = 0
            Dim drawnIn = 0
            Dim hidden = 0

            page.SuspendLayout()

            Try
                For Each placement In placedPage.Fields
                    If placement.Kind = PlacedFieldKind.BlankLine Then Continue For

                    Dim top = placement.Top + MaintenanceLayout.CaptionShift

                    If placement.Kind = PlacedFieldKind.Divider Then
                        ' A divider's control is named for its number, not for a column, so it is
                        ' looked up that way. Matching it as Label_<Field> found nothing and drew a
                        ' second divider over the first.
                        Dim rule = Find(page, "Label_Divider" & placement.PlaceholderNumber.ToString())
                        If rule IsNot Nothing Then
                            rule.Location = New Point(placement.Left, top)
                            rule.Visible = True
                            matched += 1
                        Else
                            For Each made In PreviewControls.Create(Shift(placement, MaintenanceLayout.CaptionShift), columnLengths, page.Font)
                                page.Controls.Add(made)
                                made.BringToFront()
                            Next
                        End If
                        Continue For
                    End If

                    Dim label = Find(page, "Label_" & placement.Field)
                    Dim field = FindField(page, placement)

                    If label Is Nothing AndAlso field Is Nothing Then
                        ' In the request, not on the page. Drawn the same way the layout preview
                        ' draws it, so it reads as a field rather than as a note about one.
                        For Each made In PreviewControls.Create(Shift(placement, MaintenanceLayout.CaptionShift), columnLengths, page.Font)
                            page.Controls.Add(made)
                            made.BringToFront()
                        Next
                        drawnIn += 1
                        touched.Add(placement.Field)
                        Continue For
                    End If

                    If label IsNot Nothing Then
                        label.Location = New Point(placement.Left, top)
                        label.Visible = True
                    End If

                    If field IsNot Nothing Then
                        Dim moveBy = New Size(placement.Left + MaintenanceLayout.ControlOffset - field.Left, top - field.Top)
                        field.Location = New Point(placement.Left + MaintenanceLayout.ControlOffset, top)
                        field.Visible = True

                        For Each attached In AttachmentsOf(page, field, placement.Field)
                            attached.Location = New Point(attached.Left + moveBy.Width, attached.Top + moveBy.Height)
                        Next

                        If zipButton IsNot Nothing AndAlso field Is zipAnchor Then
                            zipButton.Location = New Point(zipButton.Left + moveBy.Width, zipButton.Top + moveBy.Height)
                        End If
                    End If

                    matched += 1
                    touched.Add(placement.Field)
                Next

                ' On the page, no longer in the request. Hidden rather than removed: the page owns
                ' these controls and may still be binding to them. The label and the borders go with
                ' it, or the row it vacated still looks occupied.
                For Each item In fieldControls
                    Dim columnName = ColumnNameOf(item)
                    If columnName = String.Empty OrElse touched.Contains(columnName) Then Continue For

                    item.Visible = False
                    hidden += 1

                    Dim strandedLabel = Find(page, "Label_" & columnName)
                    If strandedLabel IsNot Nothing Then strandedLabel.Visible = False

                    For Each attached In AttachmentsOf(page, item, columnName)
                        attached.Visible = False
                    Next
                Next

                Dim newFieldsBottom = placedPage.FieldsBottom + MaintenanceLayout.CaptionShift
                Dim shiftBy = newFieldsBottom - oldFieldsBottom

                ' Sideways as well as down. The companion was centred by the page for the width it
                ' was compiled at, so a request that changes the width leaves it off to one side -
                ' or off the edge, which is what a two column page taken down to one does. The
                ' block moves as a unit, keeping its internal spacing, so the grids and the
                ' transfer buttons between them stay where they are relative to each other.
                Dim acrossBy = 0
                If companion.Count > 0 AndAlso placedPage.ReservedWidth > 0 Then
                    acrossBy = placedPage.ReservedLeft - companion.Min(Function(item) item.Left)
                End If

                For Each item In companion
                    item.Location = New Point(item.Left + acrossBy, item.Top + shiftBy)
                Next

                ' The size the layout wants, which is not always the size the window can be. The
                ' buttons are placed against the wanted edges, not the capped ones, so a page too
                ' tall for the screen keeps them at the bottom of the layout where they belong and
                ' scrolling reaches them.
                Dim wanted = New Size(placedPage.Width, placedPage.Height + MaintenanceLayout.CaptionShift)

                Dim buttons = actionButtons
                Dim buttonTop = wanted.Height - (MaintenanceLayout.ButtonHeight + MaintenanceLayout.ButtonBottomGap)

                If buttons.Count > 0 Then
                    buttons(0).Location = New Point(wanted.Width - MaintenanceLayout.ButtonRowWidth, buttonTop)
                End If
                If buttons.Count > 1 Then
                    buttons(1).Location = New Point(wanted.Width - (MaintenanceLayout.ButtonWidth + MaintenanceLayout.ButtonRightGap), buttonTop)
                End If

                PreviewWindow.Fit(page, wanted)
                For Each extra In buttons.Skip(2)
                    extra.Location = New Point(extra.Left, buttonTop)
                Next
            Finally
                page.ResumeLayout(True)
            End Try

            ' Last, because the page sets its own caption from Shown and anything written before
            ' that is overwritten. The counts are here rather than in a status bar because this is
            ' the page's own window: if the next fault is a field that did not move, the title says
            ' whether it was matched, hidden or drawn in.
            ' The snapshot must learn the new layout before the zoom goes back on, or Apply puts
            ' every control back where it was and the re-arrangement is undone. Unconditional, not
            ' only when zoomed: a page left at 100 per cent would be undone the first time anyone
            ' pressed F9 afterwards.
            PageZoom.Recapture(page)
            If zoomed Then PageZoom.Apply(page, zoomFactor)

            page.Text = placedPage.Fields.Count.ToString() & " placed   " &
                        matched.ToString() & " matched   " &
                        hidden.ToString() & " hidden   " &
                        drawnIn.ToString() & " drawn in   -   PREVIEW, NOTHING IS SAVED"
        End Sub

        ''' <summary>
        ''' The controls that belong to a field and have to move with it: its focus border, and the
        ''' required border that frames it one pixel outside. Both are separate controls the page
        ''' positions against the field, so a field that moves without them leaves them behind.
        ''' </summary>
        Private Function AttachmentsOf(page As Form, field As Control, columnName As String) As List(Of Control)
            Dim attached As New List(Of Control)()

            Dim focusBorder = Find(page, "FocusBorder_" & field.Name)
            If focusBorder IsNot Nothing Then attached.Add(focusBorder)

            Dim requiredTags = New String() {"RequiredBorder_" & field.Name, "LocalRequiredBorder_" & columnName}
            For Each candidate As Control In page.Controls
                Dim panel = TryCast(candidate, Panel)
                If panel Is Nothing OrElse panel.Tag Is Nothing Then Continue For

                Dim tagText = panel.Tag.ToString()
                If requiredTags.Any(Function(wanted) String.Equals(tagText, wanted, StringComparison.OrdinalIgnoreCase)) Then
                    attached.Add(panel)
                End If
            Next

            Return attached
        End Function

        ''' <summary>
        ''' Controls the framework puts on every page, which belong to the window rather than to
        ''' the page's own layout and must not be moved with it.
        ''' </summary>
        Private Function IsPageChrome(item As Control) As Boolean
            Dim name = If(item.Name, String.Empty)
            Return String.Equals(name, "Label_ZoomLevel", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(name, HelpDeskLauncher.ButtonName, StringComparison.OrdinalIgnoreCase) OrElse
                   name.StartsWith("PreviewCaption", StringComparison.OrdinalIgnoreCase)
        End Function
        Private Function Shift(placement As PlacedField, by As Integer) As PlacedField
            Return New PlacedField() With {
                .Field = placement.Field,
                .Kind = placement.Kind,
                .Left = placement.Left,
                .Top = placement.Top + by,
                .Required = placement.Required,
                .Width = placement.Width,
                .Nullable = placement.Nullable,
                .ShowTime = placement.ShowTime,
                .PlaceholderNumber = placement.PlaceholderNumber
            }
        End Function

        Private ReadOnly FieldPrefixes As String() = {"TextBox_", "ComboBox_", "CheckBox_", "DateTimePicker_", "MaskedTextBox_", "RichTextBox_", "NumericUpDown_"}

        Private Function FieldControlsOn(page As Form) As List(Of Control)
            Return page.Controls.Cast(Of Control)().
                Where(Function(item) ColumnNameOf(item) <> String.Empty).
                ToList()
        End Function

        Private Function ColumnNameOf(item As Control) As String
            Dim name = If(item.Name, String.Empty)
            For Each prefix In FieldPrefixes
                If name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then Return name.Substring(prefix.Length)
            Next
            Return String.Empty
        End Function

        Private Function Find(page As Form, name As String) As Control
            Dim matches = page.Controls.Find(name, True)
            Return If(matches.Length = 0, Nothing, matches(0))
        End Function

        Private Function FindField(page As Form, placement As PlacedField) As Control
            For Each prefix In FieldPrefixes
                Dim found = Find(page, prefix & placement.Field)
                If found IsNot Nothing Then Return found
            Next
            Return Nothing
        End Function

        ''' <summary>
        ''' OK and Cancel, which are not part of the field block and are not companion furniture.
        ''' Identified by being buttons on the bottom row rather than by name, because FW_Base_U
        ''' creates them without one.
        ''' </summary>
        Private Function IsActionButton(page As Form, item As Control) As Boolean
            If Not TypeOf item Is Button Then Return False
            Return item.Top > page.ClientSize.Height - 80
        End Function

    End Module
End Namespace
