Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Makes the controls a preview shows for a placed field.
    '''
    ''' One routine, because two previews need it and they must not disagree. The layout preview
    ''' builds a whole page from these; the merged preview drops one onto a compiled page for a
    ''' field that is in the request but was not there when the page was last generated. A field
    ''' added this afternoon has to look exactly like a field added last month, or the preview
    ''' teaches the wrong thing about the layout.
    '''
    ''' Named, sized and coloured as the real helpers name, size and colour them - the width from
    ''' the column's own length through MaintenanceLayout, the required marker and the App Admin
    ''' blue from the page. What they do not have is a column behind them: nothing here is bound,
    ''' and nothing here saves.
    ''' </summary>
    Public Module PreviewControls

        ''' <summary>
        ''' The label and the control for one placed field, ready to be added to a form. A blank
        ''' line returns nothing; a divider returns its rule.
        ''' </summary>
        Public Function Create(placement As PlacedField,
                               columnLengths As Dictionary(Of String, Integer),
                               referenceFont As Font) As List(Of Control)
            Dim made As New List(Of Control)()
            If placement Is Nothing Then Return made

            If placement.Kind = PlacedFieldKind.BlankLine Then Return made

            If placement.Kind = PlacedFieldKind.Divider Then
                made.Add(New Label() With {
                    .Name = "Label_Divider" & placement.PlaceholderNumber.ToString(),
                    .AutoSize = False,
                    .Text = String.Empty,
                    .Location = New Point(placement.Left, placement.Top),
                    .Size = New Size(MaintenanceLayout.ColumnWidth, 2),
                    .BackColor = SystemColors.ControlDark
                })
                Return made
            End If

            made.Add(CreateLabel(placement))
            made.Add(CreateField(placement, columnLengths, referenceFont))
            Return made
        End Function

        Private Function CreateLabel(placement As PlacedField) As Label
            ' One formatter for display text, the same one the page uses.
            Dim caption = DisplayNameFormatter.ToDisplayName(placement.Field, stripFrameworkPrefix:=False)
            If placement.Required Then caption &= " *"

            Dim label As New Label() With {
                .Name = "Label_" & placement.Field,
                .Text = caption,
                .Location = New Point(placement.Left, placement.Top),
                .Size = New Size(MaintenanceLayout.LabelWidth, MaintenanceLayout.FieldHeight),
                .TextAlign = ContentAlignment.MiddleLeft
            }

            ' The exact blue the page paints an App Admin required field. Borrowed rather than
            ' matched by eye: ShouldSkipBrRequiredStyling reads this ARGB to decide who owns
            ' required, and a preview showing a different blue would teach the wrong colour.
            If placement.Required Then label.BackColor = FW_Base_U.AppAdminRequiredBackColor

            Return label
        End Function

        Private Function CreateField(placement As PlacedField,
                                     columnLengths As Dictionary(Of String, Integer),
                                     referenceFont As Font) As Control
            Dim controlLeft = placement.Left + MaintenanceLayout.ControlOffset

            Select Case placement.Kind
                Case PlacedFieldKind.CheckBox
                    Return New CheckBox() With {
                        .Name = "CheckBox_" & placement.Field,
                        .Text = String.Empty,
                        .Location = New Point(controlLeft, placement.Top),
                        .Size = New Size(24, MaintenanceLayout.FieldHeight),
                        .TabStop = False
                    }

                Case PlacedFieldKind.DateTimePicker
                    Return New DateTimePicker() With {
                        .Name = "DateTimePicker_" & placement.Field,
                        .Location = New Point(controlLeft, placement.Top),
                        .Size = New Size(If(placement.ShowTime, 200, 130) + If(placement.Nullable, 22, 0), MaintenanceLayout.FieldHeight),
                        .Format = If(placement.ShowTime, DateTimePickerFormat.Long, DateTimePickerFormat.Short),
                        .ShowCheckBox = placement.Nullable,
                        .Checked = Not placement.Nullable,
                        .TabStop = False
                    }

                Case PlacedFieldKind.ComboBox
                    Dim combo As New ComboBox() With {
                        .Name = "ComboBox_" & placement.Field,
                        .Location = New Point(controlLeft, placement.Top),
                        .Size = New Size(If(placement.Width > 0, placement.Width, MaintenanceLayout.FieldWidth), MaintenanceLayout.FieldHeight),
                        .DropDownStyle = ComboBoxStyle.DropDownList,
                        .TabStop = False
                    }
                    ' A list is never wider than its box, as ComboWidth.Narrow enforces everywhere
                    ' else, so the preview cannot show a shape the real page will not produce.
                    combo.DropDownWidth = combo.Width
                    combo.Items.Add(SampleValues.ForCombo(placement.Field))
                    combo.SelectedIndex = 0
                    Return combo

                Case Else
                    Dim width = If(placement.Width > 0, placement.Width, MaintenanceLayout.FieldWidth)
                    Dim box As New TextBox() With {
                        .Name = "TextBox_" & placement.Field,
                        .Location = New Point(controlLeft, placement.Top),
                        .Size = New Size(width, MaintenanceLayout.FieldHeight),
                        .BorderStyle = BorderStyle.FixedSingle,
                        .BackColor = SystemColors.Window,
                        .TabStop = False,
                        .UseSystemPasswordChar = SampleValues.IsSecret(placement.Field),
                        .Text = SampleValues.ForField(placement.Field)
                    }

                    ' Narrowed to what the column holds, by the same rule the page applies at load.
                    ' Without it a State that takes two characters is as wide as a Notes that takes
                    ' 255, and the preview shows a row of boxes the real page never produces.
                    Dim maxLength = MaintenanceLayout.ResolveColumnMaxLength(box.Name, Nothing, columnLengths)
                    box.Width = MaintenanceLayout.SizedFieldWidth(maxLength,
                                                                  If(referenceFont, box.Font),
                                                                  box.Width,
                                                                  multiline:=False)
                    If maxLength > 0 Then box.MaxLength = maxLength

                    Return box
            End Select
        End Function

    End Module
End Namespace
