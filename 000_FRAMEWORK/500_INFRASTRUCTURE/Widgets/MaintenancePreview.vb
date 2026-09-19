Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
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
            Dim pageType = RealPagePreview.FindPageType(pageName)

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
                    page = RealPagePreview.BuildPage(pageType, user)
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

        ''' <summary>
        ''' Moves the page's own field controls to where the request puts them, draws in the fields
        ''' the page does not have, hides the ones the request no longer wants, and takes everything
        ''' the companion added along with it.
        ''' </summary>
        Private Sub Rearrange(page As Form, placedPage As PlacedPage, tableName As String)
            Dim columnLengths As Dictionary(Of String, Integer)
            Try
                columnLengths = DataAccess.GetTextColumnMaxLengths(tableName)
            Catch
                columnLengths = New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            End Try

            ' Where the page's fields currently stop. Measured rather than asked for, because
            ' GeneratedFieldsBottom belongs to the page and a preview has no business reaching into
            ' it - and measuring works just as well on a page that predates the split.
            Dim fieldControls = FieldControlsOn(page)
            Dim oldFieldsBottom = If(fieldControls.Count = 0, 0, fieldControls.Max(Function(item) item.Bottom))

            ' Everything the companion put below the fields, as one block. It moves by whatever the
            ' field block's own bottom moves by, so a page whose fields got shorter does not leave
            ' its role grids stranded halfway down.
            Dim companion = page.Controls.Cast(Of Control)().
                Where(Function(item) item.Top > oldFieldsBottom AndAlso Not IsActionButton(page, item)).
                ToList()

            Dim touched As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim drawnIn = 0

            page.SuspendLayout()

            Try
                For Each placement In placedPage.Fields
                    If placement.Kind = PlacedFieldKind.BlankLine Then Continue For

                    Dim top = placement.Top + MaintenanceLayout.CaptionShift
                    Dim label = Find(page, "Label_" & placement.Field)
                    Dim field = FindField(page, placement)

                    If label Is Nothing AndAlso field Is Nothing Then
                        ' In the request, not on the page. Drawn the same way the layout preview
                        ' draws it, so it reads as a field rather than as a note about one.
                        Dim shifted = Shift(placement, MaintenanceLayout.CaptionShift)
                        For Each made In PreviewControls.Create(shifted, columnLengths, page.Font)
                            page.Controls.Add(made)
                            made.BringToFront()
                        Next
                        If placement.Kind <> PlacedFieldKind.Divider Then drawnIn += 1
                        touched.Add(placement.Field)
                        Continue For
                    End If

                    If label IsNot Nothing Then
                        label.Location = New Point(placement.Left, top)
                        label.Visible = True
                    End If

                    If field IsNot Nothing Then
                        field.Location = New Point(placement.Left + MaintenanceLayout.ControlOffset, top)
                        field.Visible = True
                    End If

                    touched.Add(placement.Field)
                Next

                ' On the page, no longer in the request. Hidden rather than removed: the page owns
                ' these controls and may still be binding to them.
                For Each item In fieldControls
                    Dim columnName = ColumnNameOf(item)
                    If columnName <> String.Empty AndAlso Not touched.Contains(columnName) Then item.Visible = False
                Next

                Dim newFieldsBottom = placedPage.FieldsBottom + MaintenanceLayout.CaptionShift
                Dim shiftBy = newFieldsBottom - oldFieldsBottom
                For Each item In companion
                    item.Location = New Point(item.Left, item.Top + shiftBy)
                Next

                page.ClientSize = New Size(placedPage.Width, placedPage.Height + MaintenanceLayout.CaptionShift)

                For Each actionButton In page.Controls.Cast(Of Control)().Where(Function(item) IsActionButton(page, item))
                    actionButton.Location = New Point(actionButton.Left, page.ClientSize.Height - 46)
                Next
            Finally
                page.ResumeLayout(True)
            End Try

            If drawnIn > 0 Then
                page.Text = page.Text & "   (" & drawnIn.ToString() & " field" & If(drawnIn = 1, "", "s") & " not yet generated)"
            End If
        End Sub

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
