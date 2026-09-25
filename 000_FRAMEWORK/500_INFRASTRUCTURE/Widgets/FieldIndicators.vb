Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The green border on the field that has the focus, and the red one on a required field that
    ''' has been visited and left empty - for any window, not only a maintenance page.
    '''
    ''' Lifted out of FW_Base_U on 2026-09-25, when the message Compose window needed the same
    ''' borders on Subject and Message and could not reach them: the logic lived in about thirty
    ''' private members of the base class. Roles_U and PageGeneration_U each still carry an older
    ''' copy of their own; they move here when they are next worked on, and the maintenance
    ''' regression script fails if the focus colour appears anywhere else.
    '''
    ''' **The rules, unchanged from FW_Base_U** - FRAMEWORK_NOTES.md "Required-field styling":
    '''
    '''   - focus: green #37B469, on the field with the focus
    '''   - red: a required field that is empty and has been *visited* (entered, left or edited),
    '''     or is empty with the mouse over it. The window's own opening focus is not a visit -
    '''     see SuppressTouch - so a blank record opens with no red.
    '''   - a failed save marks every required field visited, so the red matches the message
    '''   - hover background: the App Admin blue, only while the field does not have the focus
    '''
    ''' The green ring and the red ring are one panel behind the control. A field is required when
    ''' its Tag is "Required" - the same test DataAccess.ValidateRequiredControls uses.
    ''' </summary>
    Public NotInheritable Class FieldIndicators

        ''' <summary>The focus border. Also the value the regression script looks for.</summary>
        Public Shared ReadOnly FocusColor As Color = Color.FromArgb(55, 180, 105)
        Public Shared ReadOnly WarningColor As Color = Color.Red

        Private ReadOnly hoverBackColor As Color
        Private ReadOnly isLoading As Func(Of Boolean)
        Private ReadOnly excludeFromFocus As Func(Of Control, Boolean)

        Private ReadOnly requiredBorderMap As New Dictionary(Of Control, Panel)()
        Private ReadOnly focusBorders As New Dictionary(Of Control, Panel)()
        Private ReadOnly originalBackColors As New Dictionary(Of Control, Color)()

        ''' <summary>Required fields the user has been in. A visit is not withdrawn by leaving.</summary>
        Private ReadOnly touched As New HashSet(Of Control)()

        ''' <summary>Required fields the mouse is over. Withdrawn on mouse-out, unlike a visit.</summary>
        Private ReadOnly hovered As New HashSet(Of Control)()

        ''' <param name="hoverBackColor">The background a field takes under the mouse.</param>
        ''' <param name="isLoading">True while the window is filling its fields, when a change is not a visit.</param>
        ''' <param name="excludeFromFocus">Controls that keep their standard look - a page's Save and Cancel.</param>
        Public Sub New(hoverBackColor As Color,
                       Optional isLoading As Func(Of Boolean) = Nothing,
                       Optional excludeFromFocus As Func(Of Control, Boolean) = Nothing)
            Me.hoverBackColor = hoverBackColor
            Me.isLoading = If(isLoading, Function() False)
            Me.excludeFromFocus = If(excludeFromFocus, Function(c As Control) False)
        End Sub

        ''' <summary>
        ''' True while the window moves the focus itself. The page putting the cursor somewhere is
        ''' not the user visiting the field, so it must not turn a blank required field red.
        ''' </summary>
        Public Property SuppressTouch As Boolean

        Public ReadOnly Property RequiredBorders As IReadOnlyDictionary(Of Control, Panel)
            Get
                Return requiredBorderMap
            End Get
        End Property

        ''' <summary>
        ''' Makes a field required: tags it, puts a hidden border panel behind it in
        ''' <paramref name="host"/>, and watches it. The field must already be in the host.
        ''' </summary>
        Public Function AddRequired(field As Control, caption As String, host As Control) As Panel
            If field Is Nothing OrElse host Is Nothing Then Return Nothing

            field.Tag = "Required"

            Dim existing As Panel = Nothing
            If requiredBorderMap.TryGetValue(field, existing) Then Return existing

            Dim borderPanel As New Panel() With {
                .BackColor = SystemColors.Control,
                .Location = New Point(field.Left - 1, field.Top - 1),
                .Size = New Size(field.Width + 2, field.Height + 2),
                .Tag = "LocalRequiredBorder_" & caption
            }

            host.Controls.Add(borderPanel)
            borderPanel.Visible = False
            borderPanel.BringToFront()
            field.BringToFront()

            Track(field, borderPanel)
            Return borderPanel
        End Function

        ''' <summary>
        ''' Takes charge of a border panel something else made - the ones ApplyControlUpdates
        ''' creates from FW_RoleFields - so every required field follows the one rule.
        ''' </summary>
        Public Sub Adopt(field As Control, borderPanel As Panel)
            If field Is Nothing OrElse borderPanel Is Nothing OrElse requiredBorderMap.ContainsKey(field) Then Return
            Track(field, borderPanel)
        End Sub

        ''' <summary>Watches a required field: hover, and every change that can fill or empty it.</summary>
        Private Sub Track(field As Control, borderPanel As Panel)
            requiredBorderMap(field) = borderPanel

            AddHandler field.MouseEnter, Sub()
                                             hovered.Add(field)
                                             Refresh()
                                         End Sub
            AddHandler field.MouseLeave, Sub()
                                             hovered.Remove(field)
                                             Refresh()
                                         End Sub

            Dim changed = Sub(sender As Object, e As EventArgs)
                              If Not isLoading() Then MarkTouched(field)
                              Refresh()
                          End Sub

            Dim picker = TryCast(field, DateTimePicker)
            If picker IsNot Nothing Then
                AddHandler picker.ValueChanged, changed
                Return
            End If

            AddHandler field.TextChanged, changed
            Dim combo = TryCast(field, ComboBox)
            If combo IsNot Nothing Then AddHandler combo.SelectedIndexChanged, changed
        End Sub

        ''' <summary>
        ''' No longer required: the visit and the hover are forgotten with the requirement, or the
        ''' field would come back red the moment it is required again without being touched.
        ''' </summary>
        Public Sub Forget(field As Control)
            If field Is Nothing Then Return
            touched.Remove(field)
            hovered.Remove(field)
        End Sub

        ''' <summary>Shows or hides every required border, and seats each behind its field.</summary>
        Public Sub Refresh()
            For Each pair In requiredBorderMap
                Dim field = pair.Key
                Dim border = pair.Value

                ' Follow the control. A window is free to move and resize its fields.
                If field.Parent Is border.Parent Then
                    border.Location = New Point(field.Left - 2, field.Top - 2)
                    border.Size = New Size(field.Width + 4, field.Height + 4)
                End If

                ' The focused field's border is the focus ring, which ApplyFocus owns.
                If field.Focused AndAlso focusBorders.ContainsKey(field) Then Continue For

                Dim showWarning = ShouldShowWarning(field)
                border.BackColor = If(showWarning, WarningColor, SystemColors.Control)
                border.Visible = showWarning
            Next
        End Sub

        ''' <summary>Marks every required field visited - a failed save, so the red matches the message.</summary>
        Public Sub MarkAllRequiredTouched()
            For Each field In requiredBorderMap.Keys
                touched.Add(field)
            Next
            Refresh()
        End Sub

        Public Sub MarkTouched(control As Control)
            If control Is Nothing OrElse SuppressTouch OrElse Not IsRequired(control) Then Return
            touched.Add(control)
        End Sub

        ''' <summary>
        ''' Red needs the field empty, and either visited or under the mouse. Red never appears on
        ''' a field that has a value, so seeing red always means something needs doing.
        ''' </summary>
        Public Function ShouldShowWarning(control As Control) As Boolean
            If Not IsEmptyRequired(control) Then Return False
            Return touched.Contains(control) OrElse hovered.Contains(control)
        End Function

        Public Shared Function IsRequired(control As Control) As Boolean
            Return control IsNot Nothing AndAlso
                   String.Equals(If(control.Tag, String.Empty).ToString(), "Required", StringComparison.OrdinalIgnoreCase)
        End Function

        Public Shared Function IsEmptyRequired(control As Control) As Boolean
            If Not IsRequired(control) Then Return False
            If TypeOf control Is ComboBox Then Return DataAccess.IsEmptyComboSelection(DirectCast(control, ComboBox))
            Return String.IsNullOrWhiteSpace(control.Text)
        End Function

        ''' <summary>
        ''' Gives every field in the container - recursively - its focus border and handlers. Safe
        ''' to call again: a control already wired is skipped.
        ''' </summary>
        Public Sub Wire(container As Control)
            If container Is Nothing Then Return

            Dim children As New List(Of Control)()
            For Each child As Control In container.Controls
                children.Add(child)
            Next

            For Each control In children
                If IsFocusIndicatorControl(control) AndAlso Not excludeFromFocus(control) AndAlso
                   Not focusBorders.ContainsKey(control) Then
                    ' A button keeps no remembered colour: assigning BackColor to a themed button
                    ' turns its visual style off, and the green border already says where focus is.
                    If Not TypeOf control Is Button Then originalBackColors(control) = control.BackColor

                    HostFlowChild(control)

                    Dim borderPanel As Panel = Nothing
                    If Not requiredBorderMap.TryGetValue(control, borderPanel) Then
                        borderPanel = FindExistingRequiredBorderPanel(control)
                    End If
                    If borderPanel Is Nothing Then
                        borderPanel = New Panel() With {
                            .Name = "FocusBorder_" & control.Name,
                            .BackColor = FocusColor,
                            .Visible = False,
                            .TabStop = False
                        }
                        control.Parent.Controls.Add(borderPanel)
                    End If

                    borderPanel.Location = New Point(Math.Max(0, control.Left - 2), Math.Max(0, control.Top - 2))
                    borderPanel.Size = New Size(control.Width + 4, control.Height + 4)
                    borderPanel.SendToBack()
                    control.BringToFront()
                    focusBorders(control) = borderPanel

                    AddHandler control.Enter, AddressOf Field_Enter
                    AddHandler control.Leave, AddressOf Field_Leave
                    AddHandler control.MouseEnter, AddressOf Field_MouseEnter
                    AddHandler control.MouseLeave, AddressOf Field_MouseLeave
                    If TypeOf control Is TextBoxBase Then
                        AddHandler control.TextChanged, AddressOf Field_ValueChanged
                    ElseIf TypeOf control Is ComboBox Then
                        AddHandler control.TextChanged, AddressOf Field_ValueChanged
                        AddHandler DirectCast(control, ComboBox).SelectedIndexChanged, AddressOf Field_ValueChanged
                    End If
                End If

                If control.HasChildren Then Wire(control)
            Next
        End Sub

        ''' <summary>Shows the ring on a field: green, or red when it is required, empty and visited.</summary>
        Public Sub ApplyFocus(control As Control)
            If control Is Nothing Then Return
            Dim borderPanel As Panel = Nothing
            If Not focusBorders.TryGetValue(control, borderPanel) Then Return

            borderPanel.BackColor = If(ShouldShowWarning(control), WarningColor, FocusColor)
            borderPanel.Visible = True
            borderPanel.BringToFront()
            control.BringToFront()
        End Sub

        ''' <summary>
        ''' Buttons are included so a command sitting among the fields shows the same focus ring
        ''' when tabbed to.
        ''' </summary>
        Private Shared Function IsFocusIndicatorControl(control As Control) As Boolean
            Return TypeOf control Is TextBoxBase OrElse TypeOf control Is ComboBox OrElse
                   TypeOf control Is CheckBox OrElse TypeOf control Is DateTimePicker OrElse
                   TypeOf control Is NumericUpDown OrElse TypeOf control Is Button
        End Function

        Private Sub Field_Enter(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return
            RestoreBackColor(control)
            MarkTouched(control)
            ApplyFocus(control)

            ' Arriving at a field selects its text, so typing replaces rather than appends. A
            ' click still places the caret - it sets its own selection after this - and the
            ' window's own opening focus is excluded, so it does not open with text highlighted.
            If SuppressTouch Then Return
            Dim editable = TryCast(control, TextBoxBase)
            If editable IsNot Nothing AndAlso Not editable.ReadOnly Then editable.SelectAll()
        End Sub

        Private Sub Field_Leave(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return
            RestoreBackColor(control)
            MarkTouched(control)

            Dim borderPanel As Panel = Nothing
            If Not focusBorders.TryGetValue(control, borderPanel) Then Return

            ' Leaving drops the focus ring, but a required field left empty keeps its red one.
            If ShouldShowWarning(control) Then
                borderPanel.BackColor = WarningColor
                borderPanel.Visible = True
                borderPanel.BringToFront()
                control.BringToFront()
            Else
                borderPanel.Visible = False
            End If
        End Sub

        Private Sub Field_ValueChanged(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing OrElse Not control.Focused Then Return

            ' Editing counts as a visit. Without it, a field the window put the cursor in at open
            ' would not turn red until the user tabbed away.
            If Not isLoading() Then MarkTouched(control)
            ApplyFocus(control)
        End Sub

        ''' <summary>
        ''' Buttons are left alone: Windows paints their hover, and repainting a themed button's
        ''' face turns its visual style off.
        ''' </summary>
        Private Sub Field_MouseEnter(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing OrElse control.Focused OrElse TypeOf control Is Button Then Return
            control.BackColor = hoverBackColor
        End Sub

        Private Sub Field_MouseLeave(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing OrElse control.Focused OrElse TypeOf control Is Button Then Return
            RestoreBackColor(control)
        End Sub

        Private Sub RestoreBackColor(control As Control)
            Dim original As Color
            If originalBackColors.TryGetValue(control, original) Then control.BackColor = original
        End Sub

        ''' <summary>
        ''' A FlowLayoutPanel reads child index as flow position, so a border beside a flow child
        ''' becomes a gap in the row and the z-order calls reorder the row. The control is hosted in
        ''' a plain panel at the same flow index first, where z-order means z-order.
        ''' </summary>
        Private Shared Sub HostFlowChild(control As Control)
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

        ''' <summary>A required-border panel already beside the control, found by its tag.</summary>
        Private Shared Function FindExistingRequiredBorderPanel(control As Control) As Panel
            If control.Parent Is Nothing Then Return Nothing

            Dim expectedLocalTag = String.Empty
            Dim underscore = control.Name.IndexOf("_"c)
            If underscore > 0 AndAlso
               (control.Name.StartsWith("TextBox_", StringComparison.OrdinalIgnoreCase) OrElse
                control.Name.StartsWith("ComboBox_", StringComparison.OrdinalIgnoreCase) OrElse
                control.Name.StartsWith("CheckBox_", StringComparison.OrdinalIgnoreCase)) Then
                expectedLocalTag = "LocalRequiredBorder_" & control.Name.Substring(underscore + 1)
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

    End Class

End Namespace
