Option Strict On
Option Explicit On

Imports System
Imports System.ComponentModel
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Find a place by city, state or zip, and hand the chosen one back.
    '''
    ''' The manual alternative to the Smarty type-ahead, opened by the Zip Coder button. The three
    ''' boxes arrive filled with whatever the calling page already has, so a half-entered address
    ''' is a starting point rather than something to retype.
    '''
    ''' It reads nothing and writes nothing beyond the search: the caller owns the fields, and
    ''' takes SelectedCity, SelectedState and SelectedZip when the dialog returns OK.
    ''' </summary>
    Public Class ZipCodeLookupDialog
        Inherits Form

        ''' Stored under its own name in FW_Pages.Background, like any other page's colour. A page
        ''' with no FW_Pages row can still be tinted, which is what this is.
        Private Const PageNameForColour As String = "FW_ZipCodeLookup"

        ''' The framework's action button, as FW_Base_B builds them: 90 by 36. Used for every
        ''' button here so the dialog matches a page rather than approximating one.
        Private Shared ReadOnly ActionButtonSize As New Size(90, 36)

        Private ReadOnly titleLabel As Label
        Private ReadOnly backgroundColorPicker As PageBackgroundColorPicker
        Private ReadOnly cityTextBox As TextBox
        Private ReadOnly stateTextBox As TextBox
        Private ReadOnly zipTextBox As TextBox
        Private ReadOnly searchButton As Button
        Private ReadOnly resultsGrid As DataGridView
        Private ReadOnly statusLabel As Label
        Private ReadOnly selectButton As Button
        Private ReadOnly closeButton As Button

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SelectedCity As String = String.Empty

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SelectedState As String = String.Empty

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SelectedZip As String = String.Empty

        Public Sub New(city As String, state As String, zipCode As String)
            Me.Text = "Zip Coder"
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.StartPosition = FormStartPosition.CenterParent
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New Size(470, 520)
            Me.BackColor = Color.White

            ' Captioned the way every other page is - Segoe UI 14 bold at 20,15 - so this reads as
            ' part of the application rather than as a stray dialog.
            titleLabel = New Label() With {
                .Text = "Zip Coder",
                .Font = New Font("Segoe UI", 14.0F, FontStyle.Bold),
                .AutoSize = True,
                .Location = New Point(20, 15)
            }

            Dim cityLabel As New Label() With {
                .Text = "City", .AutoSize = True, .Location = New Point(20, 72)
            }
            cityTextBox = New TextBox() With {
                .Name = "TextBox_City", .Location = New Point(110, 68), .Size = New Size(230, 26),
                .Text = If(city, String.Empty).Trim()
            }

            Dim stateLabel As New Label() With {
                .Text = "State Abbrev", .AutoSize = True, .Location = New Point(20, 106)
            }
            stateTextBox = New TextBox() With {
                .Name = "TextBox_State", .Location = New Point(110, 102), .Size = New Size(70, 26),
                .MaxLength = 4,
                .CharacterCasing = CharacterCasing.Upper,
                .Text = If(state, String.Empty).Trim()
            }

            Dim zipLabel As New Label() With {
                .Text = "Zip", .AutoSize = True, .Location = New Point(20, 140)
            }
            zipTextBox = New TextBox() With {
                .Name = "TextBox_Zip", .Location = New Point(110, 136), .Size = New Size(110, 26),
                .MaxLength = 10,
                .Text = If(zipCode, String.Empty).Trim()
            }

            searchButton = New Button() With {
                .Text = "Search", .Location = New Point(330, 100), .Size = ActionButtonSize
            }

            ' Select sits on its own line above the grid, the last of the right-hand column.
            ' That leaves nothing to put at the foot of the dialog - Close is Cancel and Select is
            ' OK - so the bottom pair went and the status line ends the page.
            selectButton = New Button() With {
                .Text = "Select", .Location = New Point(20, 176), .Size = ActionButtonSize, .Enabled = False
            }
            ' Close goes on the caption line at the right edge, with the colour picker
            ' immediately to its left - the order every browse page uses. It was down in the
            ' action row, which put the two buttons that finish a page in different places
            ' depending on which page you were on.
            closeButton = New Button() With {
                .Text = "Close", .Location = New Point(Me.ClientSize.Width - ActionButtonSize.Width - 20, 12), .Size = ActionButtonSize,
                .DialogResult = DialogResult.Cancel
            }

            resultsGrid = New DataGridView() With {
                .Location = New Point(20, 216),
                .Size = New Size(420, 250),
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .ReadOnly = True,
                .RowHeadersVisible = False,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .AutoGenerateColumns = False,
                .BackgroundColor = Color.White
            }
            resultsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "City", .DataPropertyName = "City", .HeaderText = "City",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            })
            resultsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "State", .DataPropertyName = "State", .HeaderText = "State", .Width = 70
            })
            resultsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "ZipCode", .DataPropertyName = "ZipCode", .HeaderText = "Zip Code", .Width = 90
            })

            statusLabel = New Label() With {
                .AutoSize = False,
                .Location = New Point(20, 476),
                .Size = New Size(420, 34),
                .ForeColor = Color.FromArgb(90, 98, 108),
                .Text = "Fill in any of City, State or Zip, then Search."
            }

            Me.Controls.AddRange(New Control() {titleLabel, cityLabel, cityTextBox, stateLabel, stateTextBox,
                                                zipLabel, zipTextBox, searchButton,
                                                selectButton, closeButton, resultsGrid, statusLabel})

            ' The shared picker, not a second one. It is App Admin only and stores the colour
            ' against this form's name, exactly as it does for a browse or maintenance page.
            backgroundColorPicker = New PageBackgroundColorPicker(Me, PageNameForColour)
            backgroundColorPicker.Attach()
            ' Height taken from Close rather than assumed, which is what FW_Base_B does, so the
            ' picker and the button beside it sit on one baseline.
            backgroundColorPicker.Button.Height = closeButton.Height
            backgroundColorPicker.Button.Location = New Point(closeButton.Left - backgroundColorPicker.Button.Width - 10, 12)
            backgroundColorPicker.PositionPanel()
            backgroundColorPicker.ApplyStored()
            backgroundColorPicker.UpdateVisibility()

            ' Search keeps the colour picker's width and finishes on the same right edge as Close,
            ' so the right-hand side reads as one column rather than three buttons at three
            ' different margins.
            searchButton.Width = backgroundColorPicker.Button.Width
            searchButton.Left = closeButton.Right - searchButton.Width

            ' Select keeps its line, above the grid, but moves under Search so the right-hand side
            ' is one column all the way down: Close and the picker, Search, Select.
            selectButton.Width = searchButton.Width
            selectButton.Left = searchButton.Left

            ' A box arrives with its caret at the end, not with its contents selected. These are
            ' seeded from the calling page, so the text is something to add to or correct - and
            ' selecting it all means the first keystroke silently destroys what was passed in.
            For Each box In New TextBox() {cityTextBox, stateTextBox, zipTextBox}
                AddHandler box.Enter, AddressOf TextBox_Enter
            Next

            ' Shown as well as Enter. The first box is focused while the form is activating, which
            ' is before Enter's deferred deselect can win - so the dialog opened with the city
            ' highlighted and one keystroke away from being lost. This clears all three once the
            ' form has settled, whichever happens to hold focus.
            AddHandler Me.Shown, AddressOf ZipCodeLookupDialog_Shown

            Me.AcceptButton = searchButton
            Me.CancelButton = closeButton

            AddHandler searchButton.Click, AddressOf SearchButton_Click
            AddHandler selectButton.Click, AddressOf SelectButton_Click
            AddHandler resultsGrid.SelectionChanged, AddressOf ResultsGrid_SelectionChanged
            AddHandler resultsGrid.CellDoubleClick, AddressOf ResultsGrid_CellDoubleClick

            ' Searched on opening when the caller already had something to go on, so the common
            ' case - a zip typed on the page, then Zip Coder - needs no second click.
            If cityTextBox.Text <> String.Empty OrElse
               stateTextBox.Text <> String.Empty OrElse
               zipTextBox.Text <> String.Empty Then
                RunSearch()
            End If
        End Sub

        ''' <summary>
        ''' Puts the caret at the end instead of selecting the whole value.
        '''
        ''' BeginInvoke because WinForms selects the text after Enter has run; setting the
        ''' selection here directly would be overwritten a moment later.
        ''' </summary>
        Private Sub TextBox_Enter(sender As Object, e As EventArgs)
            Dim box = TryCast(sender, TextBox)
            If box Is Nothing Then Return

            box.BeginInvoke(New Action(Sub()
                                           box.SelectionStart = box.TextLength
                                           box.SelectionLength = 0
                                       End Sub))
        End Sub

        Private Sub ZipCodeLookupDialog_Shown(sender As Object, e As EventArgs)
            For Each box In New TextBox() {cityTextBox, stateTextBox, zipTextBox}
                box.SelectionStart = box.TextLength
                box.SelectionLength = 0
            Next
        End Sub

        Private Sub SearchButton_Click(sender As Object, e As EventArgs)
            RunSearch()
        End Sub

        ''' <summary>
        ''' One query, and the grid either fills or explains why it has not.
        '''
        ''' A search with every box empty is allowed rather than refused - it simply reports that
        ''' there are more matches than the limit, which tells the user what to do next more
        ''' usefully than a validation message would.
        ''' </summary>
        Private Sub RunSearch()
            Try
                Cursor = Cursors.WaitCursor
                selectButton.Enabled = False

                Dim limitReached As Boolean
                Dim results = DataAccess.SearchZipCodes(cityTextBox.Text,
                                                        stateTextBox.Text,
                                                        zipTextBox.Text,
                                                        limitReached)
                resultsGrid.DataSource = results

                If results.Rows.Count = 0 Then
                    statusLabel.Text = "No match. City and alias are matched in full, so try the " &
                                       "whole name - or search on the Zip alone."
                ElseIf limitReached Then
                    statusLabel.Text = "More than " & DataAccess.ZipCodeSearchLimit.ToString() &
                                       " matches, showing the first " & DataAccess.ZipCodeSearchLimit.ToString() &
                                       ". Narrow the search."
                Else
                    statusLabel.Text = results.Rows.Count.ToString() &
                                       If(results.Rows.Count = 1, " match.", " matches.")
                End If
            Catch ex As Exception
                statusLabel.Text = "The zip code search failed: " & ex.Message
            Finally
                Cursor = Cursors.Default
            End Try
        End Sub

        Private Sub ResultsGrid_SelectionChanged(sender As Object, e As EventArgs)
            selectButton.Enabled = resultsGrid.SelectedRows.Count > 0
        End Sub

        Private Sub ResultsGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then Return
            AcceptSelection()
        End Sub

        Private Sub SelectButton_Click(sender As Object, e As EventArgs)
            AcceptSelection()
        End Sub

        ''' <summary>
        ''' Takes the selected row and closes. The caller reads the three properties; nothing is
        ''' written to the page from here, so one place owns that.
        ''' </summary>
        Private Sub AcceptSelection()
            If resultsGrid.SelectedRows.Count = 0 Then Return

            Dim row = TryCast(resultsGrid.SelectedRows(0).DataBoundItem, DataRowView)
            If row Is Nothing Then Return

            SelectedCity = Convert.ToString(row("City"))
            SelectedState = Convert.ToString(row("State"))
            SelectedZip = Convert.ToString(row("ZipCode"))

            Me.DialogResult = DialogResult.OK
            Me.Close()
        End Sub

    End Class

End Namespace
