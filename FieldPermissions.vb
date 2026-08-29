Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows.Forms

Namespace HelloWorld
    ''' <summary>
    ''' Applies field-level permissions from FW_RoleFields to individual controls.
    '''
    ''' Owns the obfuscation bookkeeping so a masked value can never reach the database:
    ''' the real value is held here while the mask is displayed, and is restored around the
    ''' save build so page code reads the true value, then re-masked.
    '''
    ''' Single owner for this behavior. Do not mask or re-enable controls page-locally.
    ''' </summary>
    Public Module FieldPermissions

        ''' <summary>
        ''' Fixed-width mask shown when a field cannot be read. Fixed rather than
        ''' length-preserving so the hidden value's length is not disclosed.
        ''' </summary>
        Public Const MaskText As String = "••••••"

        Private ReadOnly maskedValues As New Dictionary(Of Control, String)()

        ''' <summary>
        ''' Hides a control and its Label_ partner. TabStop is cleared as well so the control
        ''' stays out of the tab order even if it is made visible again after the tab-order
        ''' pass has run; WinForms already skips invisible controls on its own.
        ''' </summary>
        Public Sub HideField(ctrl As Control, label As Control)
            If ctrl IsNot Nothing Then
                FreezeBoundValue(ctrl)
                ctrl.Visible = False
                ctrl.TabStop = False
            End If
            If label IsNot Nothing Then label.Visible = False
        End Sub

        ''' <summary>
        ''' Detaches a hidden field's data bindings, keeping the value it was loaded with.
        '''
        ''' A control hidden before the form is created never gets a window handle, but its
        ''' binding still takes part in validation. On the first focus change WinForms pulls an
        ''' empty value out of the handle-less control, writes that into the bound record, then
        ''' pushes the blank back to the control - so the loaded value is lost and a save would
        ''' write the empty value over the real one. Freezing the value here stops that: the
        ''' field is not editable anyway, so it has nothing left to contribute to the binding.
        ''' </summary>
        Private Sub FreezeBoundValue(ctrl As Control)
            If ctrl.DataBindings.Count = 0 Then Return

            Dim textValue = ctrl.Text
            Dim checkBox = TryCast(ctrl, CheckBox)
            Dim combo = TryCast(ctrl, ComboBox)
            Dim picker = TryCast(ctrl, DateTimePicker)
            Dim numeric = TryCast(ctrl, NumericUpDown)

            Dim checkedValue = checkBox IsNot Nothing AndAlso checkBox.Checked
            Dim selectedIndex = If(combo Is Nothing, -1, combo.SelectedIndex)
            Dim dateValue = If(picker Is Nothing, Date.MinValue, picker.Value)
            Dim numericValue = If(numeric Is Nothing, 0D, numeric.Value)

            ctrl.DataBindings.Clear()

            If checkBox IsNot Nothing Then
                checkBox.Checked = checkedValue
            ElseIf combo IsNot Nothing Then
                combo.SelectedIndex = selectedIndex
            ElseIf picker IsNot Nothing Then
                picker.Value = dateValue
            ElseIf numeric IsNot Nothing Then
                numeric.Value = numericValue
            Else
                ctrl.Text = textValue
            End If
        End Sub

        ''' <summary>
        ''' Blocks entry without hiding the value. Used when Can_Create is false on a new
        ''' record or Can_Update is false on an existing one.
        ''' </summary>
        Public Sub SetNoEntry(ctrl As Control)
            If ctrl Is Nothing Then Return

            If TypeOf ctrl Is TextBoxBase Then
                DirectCast(ctrl, TextBoxBase).ReadOnly = True
            Else
                ctrl.Enabled = False
            End If

            ctrl.TabStop = False
        End Sub

        ''' <summary>
        ''' Replaces the displayed value with the mask and remembers the real value.
        ''' The control is also blocked from entry, since a masked value must not be edited.
        ''' </summary>
        Public Sub Mask(ctrl As Control)
            If ctrl Is Nothing OrElse maskedValues.ContainsKey(ctrl) Then Return

            maskedValues(ctrl) = If(ctrl.Text, String.Empty)
            ctrl.Text = MaskText
            SetNoEntry(ctrl)
        End Sub

        Public Function IsMasked(ctrl As Control) As Boolean
            Return ctrl IsNot Nothing AndAlso maskedValues.ContainsKey(ctrl)
        End Function

        ''' <summary>
        ''' Restores real values on the form's masked controls. Call immediately before the
        ''' record is built for save so page code reads the true value, never the mask.
        ''' </summary>
        Public Sub UnmaskForSave(form As Form)
            For Each ctrl In ControlsFor(form)
                ctrl.Text = maskedValues(ctrl)
            Next
        End Sub

        ''' <summary>Re-applies the mask after the record has been built.</summary>
        Public Sub RemaskAfterSave(form As Form)
            For Each ctrl In ControlsFor(form)
                ctrl.Text = MaskText
            Next
        End Sub

        ''' <summary>Drops tracked state for a form. Call when the form closes.</summary>
        Public Sub Reset(form As Form)
            For Each ctrl In ControlsFor(form)
                maskedValues.Remove(ctrl)
            Next
        End Sub

        Private Function ControlsFor(form As Form) As List(Of Control)
            If form Is Nothing Then Return New List(Of Control)()

            Return maskedValues.Keys.
                Where(Function(ctrl) ctrl IsNot Nothing AndAlso ctrl.FindForm() Is form).
                ToList()
        End Function

    End Module
End Namespace
