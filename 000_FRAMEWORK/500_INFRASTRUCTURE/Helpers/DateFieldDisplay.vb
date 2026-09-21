Option Strict On
Option Explicit On

Imports System
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' How a date control says it holds no date.
    '''
    ''' Lifted out of FW_Base_U on 2026-09-21, unchanged, because the browse page's search rows
    ''' need the identical behaviour and copying five lines is how two controls end up disagreeing
    ''' about what an empty date looks like. FW_Base_U still owns which of its fields are nullable
    ''' and what format each one shows; this owns only what an unticked box does.
    '''
    ''' Both rules here were paid for. Neither is obvious from the control's documentation.
    ''' </summary>
    Public Module DateFieldDisplay

        ''' <summary>The blank an unticked DateTimePicker shows. A space, because an empty custom
        ''' format is ignored and the control falls back to the short date.</summary>
        Public Const EmptyDateFormat As String = " "

        ''' <summary>
        ''' Shows or hides a date control's value according to its check box.
        '''
        ''' An unticked date shows nothing at all rather than a greyed-out date. A greyed date
        ''' still reads as a value - somebody looking at a Termination Date of 09/14/2026 has to
        ''' notice a tick to know the person has not left - and the grey is easy to miss next to a
        ''' field that is legitimately read-only.
        '''
        ''' A DateTimePicker cannot be empty, so the display is emptied instead: the format is
        ''' swapped for a blank one while the box is unticked, and put back when it is ticked. The
        ''' caller holds the real format, because the control has nowhere to keep it.
        ''' </summary>
        Public Sub Refresh(picker As DateTimePicker, realFormat As String)
            If picker Is Nothing OrElse String.IsNullOrEmpty(realFormat) Then Return

            Dim showsNothing = picker.ShowCheckBox AndAlso Not picker.Checked
            Dim wanted = If(showsNothing, EmptyDateFormat, realFormat)
            If Not String.Equals(picker.CustomFormat, wanted, StringComparison.Ordinal) Then
                picker.CustomFormat = wanted
            End If
        End Sub

        ''' <summary>
        ''' Ticks or unticks a date control's check box so that it survives the control being shown.
        '''
        ''' DateTimePicker.Checked does not stick before the window handle exists. A page binds
        ''' from its constructor, long before the form is displayed, so setting it there looked
        ''' right and did nothing: at handle creation the control initialises itself from Value and
        ''' comes up ticked. Every nullable date therefore opened as though it held today's date,
        ''' and a null Termination Date read as "terminated today" - wrong in the most alarming
        ''' possible direction, and it would have been saved that way on the next Save.
        '''
        ''' Set now for the case where the handle already exists, and again when it is created.
        ''' The handler removes itself, so reopening a page cannot accumulate them.
        ''' </summary>
        Public Sub SetChecked(picker As DateTimePicker, isChecked As Boolean, realFormat As String)
            If picker Is Nothing Then Return

            picker.Checked = isChecked
            Refresh(picker, realFormat)
            If picker.IsHandleCreated Then Return

            Dim reapply As EventHandler = Nothing
            reapply = Sub(sender As Object, e As EventArgs)
                          RemoveHandler picker.HandleCreated, reapply
                          picker.Checked = isChecked
                          Refresh(picker, realFormat)
                      End Sub
            AddHandler picker.HandleCreated, reapply
        End Sub

    End Module

End Namespace
