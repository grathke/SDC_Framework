Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Hot Fields: every field of the selected record, in a strip beside the browse grid.
    '''
    ''' A browse grid shows the few columns its page SQL selects. This shows the whole record
    ''' without leaving the page, so the answer to "what else is on this row" does not require
    ''' opening the maintenance page and closing it again.
    '''
    ''' Shaped after PageBackgroundColorPicker - a widget that attaches itself to a host form, owns
    ''' its button and its panel, and is positioned by the host's layout - with one deliberate
    ''' difference: this panel does not dismiss when you click away. Following the browse selection
    ''' is the whole point, so a panel that closed the moment you clicked another row would be
    ''' worse than no panel.
    '''
    ''' See BASE_BHF_SPEC.md for the decisions behind it.
    ''' </summary>
    Public NotInheritable Class HotFieldsPanel

        ''' <summary>How wide the strip wants to be. Not adjustable - see the spec, section 3.6.</summary>
        Private Const StripWidth As Integer = 330

        Private Const HeaderHeight As Integer = 30

        Private ReadOnly owner As Form
        Private ReadOnly toggleButton As Button
        Private ReadOnly stripPanel As Panel
        Private ReadOnly dockLeftButton As Button
        Private ReadOnly dockRightButton As Button
        Private ReadOnly closeButton As Button
        Private ReadOnly fieldsGrid As DataGridView

        ''' <summary>
        ''' How much the form was actually widened when the panel opened.
        '''
        ''' Kept because closing must give back exactly what opening took. It is not always the full
        ''' strip width: where the working area had no room, the shortfall came out of the page
        ''' instead, and giving back more than was taken would leave the form narrower than it
        ''' started.
        ''' </summary>
        Private grownBy As Integer

        ''' <summary>
        ''' Whether the form was moved left to make room, so closing can move it back.
        ''' </summary>
        Private shiftedLeftBy As Integer

        Public Sub New(hostForm As Form)
            If hostForm Is Nothing Then
                Throw New ArgumentNullException(NameOf(hostForm))
            End If

            owner = hostForm

            toggleButton = New Button() With {
                .Name = "Button_HotFields",
                .Text = "Hot Fields",
                .Size = New Size(88, 26),
                .TabStop = False
            }
            ' Opens and closes. The panel's own Close does the same, so there are two ways out and
            ' neither depends on the other being reachable.
            AddHandler toggleButton.Click, Sub(sender As Object, e As EventArgs) Toggle()

            stripPanel = New Panel() With {
                .Name = "Panel_HotFields",
                .Visible = False,
                .BorderStyle = BorderStyle.FixedSingle
            }

            ' The arrows say where the panel can go, not where it is: the one for the side it is
            ' already on is disabled. Clicks, never a drag - a drag is continuous pointer sampling
            ' and that degrades under VirtualUI.
            dockLeftButton = New Button() With {
                .Name = "Button_HotFieldsDockLeft",
                .Text = "◀",
                .Size = New Size(26, 24),
                .TabStop = False
            }
            AddHandler dockLeftButton.Click, Sub(sender As Object, e As EventArgs) DockTo(True)

            dockRightButton = New Button() With {
                .Name = "Button_HotFieldsDockRight",
                .Text = "▶",
                .Size = New Size(26, 24),
                .TabStop = False
            }
            AddHandler dockRightButton.Click, Sub(sender As Object, e As EventArgs) DockTo(False)

            closeButton = New Button() With {
                .Name = "Button_HotFieldsClose",
                .Text = "Close",
                .Size = New Size(64, 24),
                .TabStop = False
            }
            AddHandler closeButton.Click, Sub(sender As Object, e As EventArgs) ClosePanel()

            ' Not docked. The header sits above it and a filled grid would cover the arrows and
            ' Close entirely - which is exactly what it did the first time. PositionPanel gives it
            ' the space below the header instead.
            fieldsGrid = New DataGridView() With {
                .Name = "Grid_HotFields",
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .AllowUserToOrderColumns = False,
                .RowHeadersVisible = False,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .EditMode = DataGridViewEditMode.EditProgrammatically,
                .BackgroundColor = Color.White,
                .BorderStyle = BorderStyle.None
            }

            ' Vertical only. Sixty columns make sixty rows and the strip is tall, but not that tall.
            ' Horizontally the three columns are made to fit instead - a value too long for the
            ' space is the Value column's problem, not a reason to scroll sideways.
            fieldsGrid.ScrollBars = ScrollBars.Vertical

            ' Which fields this page shows, ticked by an App Admin while looking at a real record -
            ' which is the one place the question "is this field worth showing" is easy to answer.
            ' Hidden from everybody else, who simply see the shorter list that results.
            '
            ' Not ReadOnly like the rest of the grid: this is the one cell anyone can change.
            fieldsGrid.Columns.Add(New DataGridViewCheckBoxColumn() With {
                .Name = "HotFieldSelected",
                .HeaderText = String.Empty,
                .Width = 26,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                .SortMode = DataGridViewColumnSortMode.NotSortable,
                .ReadOnly = False,
                .Visible = False
            })
            fieldsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "HotFieldNumber",
                .HeaderText = "#",
                .Width = 34,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                .SortMode = DataGridViewColumnSortMode.NotSortable
            })
            fieldsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "HotFieldCaption",
                .HeaderText = "Field",
                .Width = 130,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                .SortMode = DataGridViewColumnSortMode.NotSortable
            })
            fieldsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "HotFieldValue",
                .HeaderText = "Value",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                .SortMode = DataGridViewColumnSortMode.NotSortable
            })
            fieldsGrid.Columns("HotFieldValue").DefaultCellStyle.WrapMode = DataGridViewTriState.True

            AddHandler fieldsGrid.CellFormatting, AddressOf FieldsGrid_CellFormatting
            AddHandler fieldsGrid.CellContentClick, AddressOf FieldsGrid_CellContentClick

            stripPanel.Controls.Add(fieldsGrid)
            stripPanel.Controls.Add(dockLeftButton)
            stripPanel.Controls.Add(closeButton)
            stripPanel.Controls.Add(dockRightButton)

            ' A control added later sits behind, so the header is brought forward explicitly.
            dockLeftButton.BringToFront()
            closeButton.BringToFront()
            dockRightButton.BringToFront()
        End Sub

        ''' <summary>The button the host places on its layout row.</summary>
        Public ReadOnly Property Button As Button
            Get
                Return toggleButton
            End Get
        End Property

        Public ReadOnly Property IsOpen As Boolean
            Get
                Return stripPanel.Visible
            End Get
        End Property

        ''' <summary>Which edge the strip occupies. False is the right-hand edge, the default.</summary>
        Public Property DockedLeft As Boolean

        ''' <summary>The width the page must lay itself out inside of, rather than over.</summary>
        ''' <remarks>
        ''' The whole strip width while open, not just the part the form could not grow by.
        '''
        ''' The first version returned only the shortfall, on the reasoning that a form which grew
        ''' by the full width had given the strip its own room. That was wrong, because the host
        ''' centres its content: a wider form re-centred the page rightwards, straight underneath
        ''' the strip. Reserving the full width means the growth exactly cancels the reservation, so
        ''' the content keeps the size and position it had - which is what opening was supposed to
        ''' do - and where the form could not grow, the content gives up the difference instead.
        ''' </remarks>
        Public ReadOnly Property ReservedWidth As Integer
            Get
                If Not IsOpen Then
                    Return 0
                End If
                Return StripWidth
            End Get
        End Property

        ''' <summary>Adds the button and the strip to the host form.</summary>
        Public Sub Attach()
            owner.Controls.Add(toggleButton)
            owner.Controls.Add(stripPanel)
        End Sub

        Public Sub Toggle()
            If IsOpen Then
                ClosePanel()
            Else
                OpenPanel()
            End If
        End Sub

        ''' <summary>
        ''' Raised before the panel opens, so the host can refuse.
        ''' </summary>
        ''' <remarks>
        ''' The widget does not know what a selected record is - the host owns the grid - so the
        ''' condition and its message stay there rather than being guessed at here.
        ''' </remarks>
        Public Event Opening As EventHandler(Of CancelEventArgs)

        Public Sub OpenPanel()
            If IsOpen Then
                Return
            End If

            Dim refusal As New CancelEventArgs()
            RaiseEvent Opening(Me, refusal)
            If refusal.Cancel Then
                Return
            End If

            stripPanel.Visible = True
            GrowForStrip()
            UpdateDockButtons()
            stripPanel.BringToFront()
            RaiseEvent OpenedOrClosed(Me, EventArgs.Empty)
        End Sub

        Public Sub ClosePanel()
            If Not IsOpen Then
                Return
            End If

            stripPanel.Visible = False
            ShrinkAfterStrip()
            fieldsGrid.Rows.Clear()
            RaiseEvent OpenedOrClosed(Me, EventArgs.Empty)
        End Sub

        ''' <summary>
        ''' Raised when the panel opens or closes, so the host can lay itself out around it.
        ''' </summary>
        Public Event OpenedOrClosed As EventHandler

        Private Sub DockTo(toLeft As Boolean)
            If DockedLeft = toLeft Then
                Return
            End If

            ' Give back the geometry taken on the old side before taking it on the new one, so the
            ' two sides cannot both be holding width at once.
            Dim wasOpen = IsOpen
            If wasOpen Then
                ShrinkAfterStrip()
            End If

            DockedLeft = toLeft

            If wasOpen Then
                GrowForStrip()
            End If

            UpdateDockButtons()
            RaiseEvent OpenedOrClosed(Me, EventArgs.Empty)
        End Sub

        Private Sub UpdateDockButtons()
            dockLeftButton.Enabled = Not DockedLeft
            dockRightButton.Enabled = DockedLeft
        End Sub

        ''' <summary>
        ''' Widens the form to make room for the strip, as far as the working area allows.
        ''' </summary>
        ''' <remarks>
        ''' Growing past the working area is pointless here: the VirtualUI viewport is fixed once
        ''' the session starts and the browser adds no scroll bars for a window wider than it, so
        ''' anything past the edge would simply be unreachable. Whatever cannot be gained by
        ''' growing is taken from the page instead, through ReservedWidth.
        ''' </remarks>
        Private Sub GrowForStrip()
            grownBy = 0
            shiftedLeftBy = 0

            If owner.WindowState = FormWindowState.Maximized Then
                Return
            End If

            Dim working = Screen.FromControl(owner).WorkingArea
            Dim room As Integer

            If DockedLeft Then
                room = Math.Max(0, owner.Left - working.Left)
            Else
                room = Math.Max(0, working.Right - (owner.Left + owner.Width))
            End If

            grownBy = Math.Max(0, Math.Min(StripWidth, room))
            If grownBy = 0 Then
                Return
            End If

            If DockedLeft Then
                ' Grow leftwards, so the grid stays exactly where it was on screen and the new
                ' space appears on its left.
                owner.Left -= grownBy
                shiftedLeftBy = grownBy
            End If

            owner.Width += grownBy
        End Sub

        ''' <summary>
        ''' Gives back exactly what opening took.
        ''' </summary>
        ''' <remarks>
        ''' Measured against the current width rather than a width remembered from before opening.
        ''' If the user resized the window while the strip was showing, restoring a stored number
        ''' would undo their resize, and a page that undoes your resize reads as a page fighting you.
        ''' </remarks>
        Private Sub ShrinkAfterStrip()
            If grownBy > 0 Then
                owner.Width = Math.Max(owner.MinimumSize.Width, owner.Width - grownBy)
            End If

            If shiftedLeftBy > 0 Then
                owner.Left += shiftedLeftBy
            End If

            grownBy = 0
            shiftedLeftBy = 0
        End Sub

        ''' <summary>Places the strip against its edge, filling the height the host gives it.</summary>
        Public Sub PositionPanel(contentTop As Integer, contentHeight As Integer)
            If Not IsOpen Then
                Return
            End If

            ' The same margin the page leaves at its own edges, so the strip sits on the page
            ' rather than being cropped against the window frame.
            Const EdgeMargin As Integer = 14

            Dim width = Math.Min(StripWidth - EdgeMargin, Math.Max(120, owner.ClientSize.Width - 40))
            Dim left = If(DockedLeft, EdgeMargin, Math.Max(0, owner.ClientSize.Width - width - EdgeMargin))

            stripPanel.SetBounds(left, contentTop, width, Math.Max(120, contentHeight))

            ' Close sits in the middle of the panel's width, with an arrow at each end.
            dockLeftButton.SetBounds(4, 3, dockLeftButton.Width, dockLeftButton.Height)
            closeButton.SetBounds(Math.Max(0, (stripPanel.ClientSize.Width - closeButton.Width) \ 2), 3,
                                  closeButton.Width, closeButton.Height)
            dockRightButton.SetBounds(Math.Max(0, stripPanel.ClientSize.Width - dockRightButton.Width - 4), 3,
                                      dockRightButton.Width, dockRightButton.Height)

            fieldsGrid.SetBounds(0, HeaderHeight, stripPanel.ClientSize.Width,
                                 Math.Max(0, stripPanel.ClientSize.Height - HeaderHeight))
        End Sub

        ''' <summary>
        ''' Fills the strip from one record's fields.
        ''' </summary>
        ''' <param name="record">One row, every column, as read from the table.</param>
        ''' <param name="captions">Field captions by column name, from the page's metadata.</param>
        ''' <param name="hiddenFields">
        ''' Fields this role may not see. They are left out: a permission that hides a field on one
        ''' surface and not another is not a permission, and a panel showing everything would
        ''' quietly become the way around the field permissions set in Roles_U.
        ''' </param>
        ''' <param name="resolvedValues">
        ''' What the browse row already shows for a field, by column name, where it has one.
        '''
        ''' A page's SQL resolves its lookups - it joins and aliases the description back to the
        ''' field's own name - so the selected row already holds "Jane Bell" where the table holds
        ''' the manager's id. Preferring it costs nothing: the row is in memory, and the alternative
        ''' is a lookup query per field per click.
        ''' </param>
        Public Sub ShowRecord(record As DataRow,
                              captions As IDictionary(Of String, String),
                              hiddenFields As ICollection(Of String),
                              resolvedValues As IDictionary(Of String, String))
            ShowRecord(record, captions, hiddenFields, resolvedValues, Nothing, False)
        End Sub

        ''' <summary>
        ''' Fills the strip for one record.
        ''' </summary>
        ''' <param name="selectedFields">
        ''' The fields this page shows. Empty means every field - the behaviour before anyone could
        ''' choose, and what every uncurated page still does.
        ''' </param>
        ''' <param name="allowSelection">
        ''' An App Admin sees every field with a tick beside it, unticked ones dimmed, so the choice
        ''' and its effect are visible at once. Everybody else sees the ticked fields and no ticks.
        ''' </param>
        Public Sub ShowRecord(record As DataRow,
                              captions As IDictionary(Of String, String),
                              hiddenFields As ICollection(Of String),
                              resolvedValues As IDictionary(Of String, String),
                              selectedFields As ICollection(Of String),
                              allowSelection As Boolean)
            fieldsGrid.Rows.Clear()
            fieldsGrid.Columns("HotFieldSelected").Visible = allowSelection

            If Not IsOpen OrElse record Is Nothing Then
                Return
            End If

            Dim curated = selectedFields IsNot Nothing AndAlso selectedFields.Count > 0

            ' Collected first, then sorted, then added - the grid is filled once rather than being
            ' asked to re-order itself afterwards. Field name travels with the caption because the
            ' caption is what is sorted and displayed, and the field name is what gets saved.
            Dim shown As New List(Of Tuple(Of String, String, String, Boolean))()

            For Each column As DataColumn In record.Table.Columns
                Dim fieldName = column.ColumnName

                If hiddenFields IsNot Nothing AndAlso hiddenFields.Contains(fieldName) Then
                    Continue For
                End If

                ' No password reaches this panel, whatever it is called. Matched on the name rather
                ' than against a list of two, so a column added later is covered without anyone
                ' remembering to add it. FW_Base_U redacts its audit snapshots the same way.
                If fieldName.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Continue For
                End If

                Dim caption As String = Nothing
                If captions Is Nothing OrElse Not captions.TryGetValue(fieldName, caption) OrElse
                   String.IsNullOrWhiteSpace(caption) Then
                    caption = DisplayNameFormatter.ToDisplayName(fieldName)
                End If

                ' What the page shows wins over what the table stores, so a resolved lookup reads as
                ' the name the grid is showing rather than as its id.
                Dim text As String = Nothing
                If resolvedValues Is Nothing OrElse Not resolvedValues.TryGetValue(fieldName, text) Then
                    Dim rawValue = record(column)
                    text = If(rawValue Is Nothing OrElse rawValue Is DBNull.Value,
                              String.Empty,
                              Convert.ToString(rawValue, CultureInfo.CurrentCulture))
                End If

                Dim isSelected = Not curated OrElse selectedFields.Contains(fieldName)

                ' A field this page does not show is dropped for everyone except the person who
                ' decides which fields it shows.
                If Not isSelected AndAlso Not allowSelection Then
                    Continue For
                End If

                shown.Add(Tuple.Create(caption, text, fieldName, isSelected))
            Next

            ' Sorted by the caption the reader sees, not by the column order the table happens to
            ' have. On a wide table that order is a history of when columns were added, which is of
            ' no use to somebody looking for one field. Alphabetical means it can be found.
            shown.Sort(Function(left, right) String.Compare(left.Item1, right.Item1, StringComparison.CurrentCultureIgnoreCase))

            For Each field In shown
                Dim rowIndex = fieldsGrid.Rows.Add(field.Item4, String.Empty, field.Item1, field.Item2)
                Dim row = fieldsGrid.Rows(rowIndex)

                ' The field name, for the tick handler. Not a column, because it is not the reader's
                ' business and the strip has no width to spare for it.
                row.Tag = field.Item3

                ' Dimmed rather than hidden, so an App Admin sees the selection and its effect at
                ' the same time and does not need a preview of somebody else's view.
                If allowSelection AndAlso Not field.Item4 Then
                    row.DefaultCellStyle.ForeColor = Color.Silver
                End If
            Next
        End Sub

        ''' <summary>
        ''' A tick changes which fields this page shows, for everyone, and saves at once.
        ''' </summary>
        ''' <remarks>
        ''' CellContentClick rather than an edit mode: the rest of the grid is read-only, and one
        ''' cell that commits on click is simpler than a grid that enters and leaves editing.
        '''
        ''' Unticking the last field clears the stored list, which means every field again rather
        ''' than none - otherwise an App Admin could empty the panel and have nothing left to tick.
        ''' </remarks>
        Private Sub FieldsGrid_CellContentClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then
                Return
            End If

            If Not String.Equals(fieldsGrid.Columns(e.ColumnIndex).Name, "HotFieldSelected", StringComparison.Ordinal) Then
                Return
            End If

            If Not fieldsGrid.Columns("HotFieldSelected").Visible Then
                Return
            End If

            ' Not merely hidden. The column is invisible to anyone else, and this refuses them too.
            If Not SessionState.IsApplicationAdmin Then
                Return
            End If

            Dim row = fieldsGrid.Rows(e.RowIndex)
            Dim fieldName = Convert.ToString(row.Tag)
            If String.IsNullOrWhiteSpace(fieldName) Then
                Return
            End If

            Dim nowSelected = Not Convert.ToBoolean(row.Cells("HotFieldSelected").Value)
            row.Cells("HotFieldSelected").Value = nowSelected
            row.DefaultCellStyle.ForeColor = If(nowSelected, fieldsGrid.DefaultCellStyle.ForeColor, Color.Silver)

            Dim selected As New List(Of String)()
            For Each gridRow As DataGridViewRow In fieldsGrid.Rows
                If Convert.ToBoolean(gridRow.Cells("HotFieldSelected").Value) Then
                    Dim name = Convert.ToString(gridRow.Tag)
                    If Not String.IsNullOrWhiteSpace(name) Then
                        selected.Add(name)
                    End If
                End If
            Next

            ' Every field ticked is the same answer as none ticked - show everything - and storing
            ' it as nothing keeps a page that gains a column later showing that column too.
            If selected.Count = fieldsGrid.Rows.Count Then
                selected.Clear()
            End If

            Dim updatedBy = If(SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)
            If Not DataAccess.SavePageHotFields(owner.GetType().Name, selected, updatedBy) Then
                MessageBox.Show(owner,
                                "The Hot Fields selection could not be saved for this page.",
                                "Hot Fields",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
            End If
        End Sub

        ''' <summary>Clears the strip, for when nothing is selected.</summary>
        Public Sub ShowNothing()
            fieldsGrid.Rows.Clear()
        End Sub

        ''' <summary>
        ''' Numbers the first column by row position as it draws.
        ''' </summary>
        ''' <remarks>
        ''' Nothing is stored and nothing is bound, so the number cannot disagree with what is on
        ''' screen. Roles_B numbers its order column the same way and for the same reason.
        ''' </remarks>
        Private Sub FieldsGrid_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
            ' By name, not by index. The tick column took index 0 on 2026-09-09 and an index test
            ' would have started numbering the checkboxes instead of the rows.
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse
               Not String.Equals(fieldsGrid.Columns(e.ColumnIndex).Name, "HotFieldNumber", StringComparison.Ordinal) Then
                Return
            End If

            e.Value = (e.RowIndex + 1).ToString(CultureInfo.InvariantCulture)
            e.FormattingApplied = True
        End Sub

    End Class

End Namespace
