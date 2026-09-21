Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The two dates of a Between search.
    '''
    ''' A dialog rather than a second column in the search grid. A fourth column would cost every
    ''' row its width for a case that is rare, and two pickers side by side in cells barely fit;
    ''' here they are full size with room for a calendar.
    '''
    ''' **The order is checked here and nowhere else.** A range whose first day is after its last
    ''' cannot leave this window, which is why no caller needs to guard against one. Returning an
    ''' impossible range would find nothing, and finding nothing is indistinguishable from a
    ''' search that simply did not match.
    '''
    ''' Both dates are required. A half-open Between is Greater Than Or Equal, and that operator
    ''' is already in the list.
    ''' </summary>
    Public Class QbeDateRangeDialog
        Inherits Form

        Private ReadOnly fromPicker As DateTimePicker
        Private ReadOnly toPicker As DateTimePicker

        ''' <summary>The first day of the range, inclusive.</summary>
        Public ReadOnly Property FromDate As Date
            Get
                Return fromPicker.Value.Date
            End Get
        End Property

        ''' <summary>The last day of the range, inclusive. The whole of it, not its midnight.</summary>
        Public ReadOnly Property ToDate As Date
            Get
                Return toPicker.Value.Date
            End Get
        End Property

        Public Sub New(fieldCaption As String, initialFrom As Date?, initialTo As Date?)
            Dim pattern = DisplayFormats.DatePattern()

            Me.Text = "Between - " & If(String.IsNullOrWhiteSpace(fieldCaption), "Date", fieldCaption)
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.StartPosition = FormStartPosition.CenterParent
            Me.ClientSize = New Size(320, 150)
            Me.Font = New Font("Segoe UI", 9.0F)

            Dim fromLabel As New Label() With {
                .Text = "From", .Location = New Point(20, 25), .Size = New Size(60, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            fromPicker = New DateTimePicker() With {
                .Location = New Point(90, 22), .Size = New Size(200, 26),
                .Format = DateTimePickerFormat.Custom, .CustomFormat = pattern,
                .Value = If(initialFrom.HasValue, initialFrom.Value, Date.Today)
            }

            Dim toLabel As New Label() With {
                .Text = "To", .Location = New Point(20, 62), .Size = New Size(60, 20),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            toPicker = New DateTimePicker() With {
                .Location = New Point(90, 59), .Size = New Size(200, 26),
                .Format = DateTimePickerFormat.Custom, .CustomFormat = pattern,
                .Value = If(initialTo.HasValue, initialTo.Value, If(initialFrom.HasValue, initialFrom.Value, Date.Today))
            }

            Dim okButton As New Button() With {
                .Text = "OK", .Location = New Point(134, 105), .Size = New Size(75, 28)
            }
            Dim cancelButton As New Button() With {
                .Text = "Cancel", .Location = New Point(215, 105), .Size = New Size(75, 28),
                .DialogResult = DialogResult.Cancel
            }

            AddHandler okButton.Click, AddressOf OkButton_Click

            Me.Controls.AddRange(New Control() {fromLabel, fromPicker, toLabel, toPicker, okButton, cancelButton})
            Me.AcceptButton = okButton
            Me.CancelButton = cancelButton
        End Sub

        Private Sub OkButton_Click(sender As Object, e As EventArgs)
            If fromPicker.Value.Date > toPicker.Value.Date Then
                MessageBox.Show(Me,
                    "The first date is after the last one." & Environment.NewLine & Environment.NewLine &
                    "A range that runs backwards finds nothing, which looks the same as a search " &
                    "that did not match. Swap them, or change one.",
                    "CHECK THE DATES", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                fromPicker.Focus()
                Return
            End If

            Me.DialogResult = DialogResult.OK
            Me.Close()
        End Sub

    End Class

End Namespace
