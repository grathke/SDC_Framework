Option Strict On
Option Explicit On

Imports System
Imports System.ComponentModel
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' A search row's date box: the same control a maintenance page uses, inside a grid cell.
    '''
    ''' **What it holds and what it shows are different things.** The cell's value is ISO -
    ''' 2026-09-14 - because that value travels into a filter key and into a saved search, where it
    ''' has to keep meaning the same day after an administrator changes how the company writes
    ''' dates. What the cell draws is that day in the company's pattern. Nothing anywhere parses a
    ''' date out of a display string.
    '''
    ''' **The tick is how a row says it is not searching.** Unticked shows nothing at all and the
    ''' cell's value is empty, which is exactly what an untouched search row means. DateFieldDisplay
    ''' owns that rule and the handle-creation trap underneath it, shared with FW_Base_U.
    '''
    ''' **A Between row uses the same cell, read-only.** Its value is the two days joined by "..",
    ''' and it draws them as a range. Clicking it reopens the dialog rather than editing in place,
    ''' because two dates will not fit in one cell and half a range is not a search.
    ''' </summary>
    Public Class QbeDateCell
        Inherits DataGridViewTextBoxCell

        ''' <summary>Joins the two days of a Between row inside one cell value. ISO dates carry no
        ''' dots, so there is nothing for this to collide with.</summary>
        Public Const RangeSeparator As String = ".."

        Public Sub New()
            Me.ValueType = GetType(String)
        End Sub

        Public Overrides ReadOnly Property EditType As Type
            Get
                Return GetType(QbeDateEditingControl)
            End Get
        End Property

        Public Overrides ReadOnly Property ValueType As Type
            Get
                Return GetType(String)
            End Get
        End Property

        Public Overrides ReadOnly Property DefaultNewRowValue As Object
            Get
                Return String.Empty
            End Get
        End Property

        ''' <summary>
        ''' Draws the stored day, or the stored range, in the company's pattern.
        '''
        ''' A value that will not parse is drawn as it is rather than blanked. A saved search made
        ''' before dates were typed can hold anything, and showing it is what lets somebody see
        ''' why their search is not working.
        ''' </summary>
        Protected Overrides Function GetFormattedValue(value As Object,
                                                       rowIndex As Integer,
                                                       ByRef cellStyle As DataGridViewCellStyle,
                                                       valueTypeConverter As ComponentModel.TypeConverter,
                                                       formattedValueTypeConverter As ComponentModel.TypeConverter,
                                                       context As DataGridViewDataErrorContexts) As Object
            Dim raw = Convert.ToString(value, Globalization.CultureInfo.InvariantCulture)
            If String.IsNullOrWhiteSpace(raw) Then
                Return MyBase.GetFormattedValue(String.Empty, rowIndex, cellStyle,
                                                valueTypeConverter, formattedValueTypeConverter, context)
            End If

            Dim shown = Describe(raw)
            Return MyBase.GetFormattedValue(shown, rowIndex, cellStyle,
                                            valueTypeConverter, formattedValueTypeConverter, context)
        End Function

        ''' <summary>A stored day or range, written the way the company writes dates.</summary>
        Public Shared Function Describe(storedValue As String) As String
            If String.IsNullOrWhiteSpace(storedValue) Then Return String.Empty

            Dim pattern = DisplayFormats.DatePattern()
            Dim halves = storedValue.Split(New String() {RangeSeparator}, StringSplitOptions.None)
            Dim shown As New Collections.Generic.List(Of String)()

            For Each half In halves
                Dim parsed As Date
                If QbeDateBounds.TryParseFilterValue(half, parsed) Then
                    shown.Add(parsed.ToString(pattern, Globalization.CultureInfo.InvariantCulture))
                Else
                    shown.Add(half.Trim())
                End If
            Next

            Return String.Join(" - ", shown)
        End Function

    End Class

    ''' <summary>
    ''' The control that appears when a date search cell is edited.
    '''
    ''' A DateTimePicker rather than a text box, which is what removes the format question
    ''' altogether: a control that only holds a date cannot be given an ambiguous string. The
    ''' keyboard still works, into the day, month and year sections.
    ''' </summary>
    Public Class QbeDateEditingControl
        Inherits DateTimePicker
        Implements IDataGridViewEditingControl

        Private owningGrid As DataGridView
        Private rowIndex_ As Integer
        Private hasPendingChange As Boolean
        Private ReadOnly realFormat As String

        Public Sub New()
            realFormat = DisplayFormats.DatePattern()
            Me.Format = DateTimePickerFormat.Custom
            Me.CustomFormat = realFormat
            Me.ShowCheckBox = True
            Me.Checked = False
            DateFieldDisplay.Refresh(Me, realFormat)
        End Sub

        Protected Overrides Sub OnValueChanged(eventargs As EventArgs)
            hasPendingChange = True
            DateFieldDisplay.Refresh(Me, realFormat)
            If owningGrid IsNot Nothing Then owningGrid.NotifyCurrentCellDirty(True)
            MyBase.OnValueChanged(eventargs)
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property EditingControlFormattedValue As Object Implements IDataGridViewEditingControl.EditingControlFormattedValue
            Get
                If Not Me.Checked Then Return String.Empty
                Return QbeDateBounds.ToFilterValue(Me.Value)
            End Get
            Set(value As Object)
                Dim raw = Convert.ToString(value, Globalization.CultureInfo.InvariantCulture)
                Dim parsed As Date

                If QbeDateBounds.TryParseFilterValue(raw, parsed) Then
                    Me.Value = parsed
                    DateFieldDisplay.SetChecked(Me, True, realFormat)
                Else
                    Me.Value = Date.Today
                    DateFieldDisplay.SetChecked(Me, False, realFormat)
                End If
            End Set
        End Property

        Public Function GetEditingControlFormattedValue(context As DataGridViewDataErrorContexts) As Object _
            Implements IDataGridViewEditingControl.GetEditingControlFormattedValue
            Return EditingControlFormattedValue
        End Function

        Public Sub ApplyCellStyleToEditingControl(dataGridViewCellStyle As DataGridViewCellStyle) _
            Implements IDataGridViewEditingControl.ApplyCellStyleToEditingControl
            If dataGridViewCellStyle Is Nothing Then Return
            Me.Font = dataGridViewCellStyle.Font
            Me.CalendarForeColor = dataGridViewCellStyle.ForeColor
            Me.CalendarMonthBackground = dataGridViewCellStyle.BackColor
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property EditingControlRowIndex As Integer Implements IDataGridViewEditingControl.EditingControlRowIndex
            Get
                Return rowIndex_
            End Get
            Set(value As Integer)
                rowIndex_ = value
            End Set
        End Property

        ''' <summary>
        ''' The arrow keys belong to the picker, not to the grid.
        '''
        ''' Left and right move between the day, month and year sections, and up and down change
        ''' the part under the cursor. Letting the grid have them would move the selection out of a
        ''' half-entered date.
        ''' </summary>
        Public Function EditingControlWantsInputKey(keyData As Keys, dataGridViewWantsInputKey As Boolean) As Boolean _
            Implements IDataGridViewEditingControl.EditingControlWantsInputKey
            Select Case keyData And Keys.KeyCode
                Case Keys.Left, Keys.Up, Keys.Down, Keys.Right, Keys.Home, Keys.End, Keys.PageDown, Keys.PageUp, Keys.Space
                    Return True
                Case Else
                    Return Not dataGridViewWantsInputKey
            End Select
        End Function

        Public Sub PrepareEditingControlForEdit(selectAll As Boolean) _
            Implements IDataGridViewEditingControl.PrepareEditingControlForEdit
            ' Nothing to select. The control shows the sections it has.
        End Sub

        Public ReadOnly Property RepositionEditingControlOnValueChange As Boolean _
            Implements IDataGridViewEditingControl.RepositionEditingControlOnValueChange
            Get
                Return False
            End Get
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property EditingControlDataGridView As DataGridView Implements IDataGridViewEditingControl.EditingControlDataGridView
            Get
                Return owningGrid
            End Get
            Set(value As DataGridView)
                owningGrid = value
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property EditingControlValueChanged As Boolean Implements IDataGridViewEditingControl.EditingControlValueChanged
            Get
                Return hasPendingChange
            End Get
            Set(value As Boolean)
                hasPendingChange = value
            End Set
        End Property

        Public ReadOnly Property EditingPanelCursor As Cursor Implements IDataGridViewEditingControl.EditingPanelCursor
            Get
                Return MyBase.Cursor
            End Get
        End Property

    End Class

End Namespace
