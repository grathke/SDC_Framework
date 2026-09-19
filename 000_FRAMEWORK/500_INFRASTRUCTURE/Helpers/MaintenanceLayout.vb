Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The measurements a maintenance page is laid out on, in one place.
    '''
    ''' They were literals in two files that had to agree and could not be made to: FW_Base_U's
    ''' field helpers applied 120, 10, 320 and 26 as bare numbers, while the page generator
    ''' separately declared a 450 wide column - three of those numbers restated in a file that
    ''' never applies them. A layout preview would have been the third copy.
    '''
    ''' Whoever draws a maintenance page reads these: the helpers that create the controls, the
    ''' generator that writes the calls, and the preview that shows the result without generating
    ''' anything. Derived figures are derived here rather than written out again.
    ''' </summary>
    Public Module MaintenanceLayout

        ''' <summary>The first row's Top.</summary>
        Public Const FirstRow As Integer = 20

        ''' <summary>
        ''' The vertical distance between rows.
        '''
        ''' Load-bearing, and not only a spacing choice. FW_Base_U decides what a row *is* by
        ''' comparing Top, and CollapseHiddenFieldRows consumes RowTop(next) - RowTop(current) when
        ''' a row above is hidden. A control placed off the pitch to look better centred stops being
        ''' on its row, and permission-driven hiding then closes the wrong gap.
        '''
        ''' Nothing that measures an existing layout may read this. Those paths measure the controls
        ''' in front of them on purpose - see HideFieldAndCloseGap - so that a page laid out some
        ''' other way still collapses correctly.
        ''' </summary>
        Public Const RowPitch As Integer = 42

        ''' <summary>The left edge of the first column.</summary>
        Public Const ColumnLeft As Integer = 20

        Public Const LabelWidth As Integer = 120
        Public Const LabelGap As Integer = 10
        Public Const FieldWidth As Integer = 320
        Public Const FieldHeight As Integer = 26

        ''' <summary>How far right of its label a field sits.</summary>
        Public Const ControlOffset As Integer = LabelWidth + LabelGap

        ''' <summary>A whole column: the label, the gap to its control, and the control.</summary>
        Public Const ColumnWidth As Integer = LabelWidth + LabelGap + FieldWidth

        ''' <summary>
        ''' The gap between the two columns when column one holds Zip. The Zip Coder button sits
        ''' past the right edge of the Zip box, so that column needs the room.
        ''' </summary>
        Public Const ZipColumnGap As Integer = 110

        ''' <summary>The gap between the two columns otherwise.</summary>
        Public Const PlainColumnGap As Integer = 40

        ''' <summary>
        ''' How far ApplySharedPageCaption moves the fields down to make room for the page title.
        '''
        ''' A generated page is emitted with its first row at FirstRow and then shifted by this on
        ''' Shown, so the finished page sits one row lower than the source says. A preview that
        ''' leaves it out disagrees with the real page by exactly one row.
        ''' </summary>
        Public Const CaptionShift As Integer = 42

        ''' <summary>Narrower than this and a box is not worth narrowing further.</summary>
        Public Const MinimumSizedFieldWidth As Integer = 44

        ''' <summary>
        ''' How wide a text box should be for the column behind it, or the width it already has
        ''' when there is no reason to change it.
        '''
        ''' Measured rather than multiplied by a constant, so it follows the page's font and DPI
        ''' instead of assuming one machine's. Capped at 40 characters because anything longer
        ''' already exceeds the width the page gave the box and would be clamped away. The sample
        ''' is deliberately a middling character: the font is proportional, so a measured average is
        ''' right for ordinary text and wrong for a field full of Ws.
        '''
        ''' It only ever shrinks. Growing a box could push it over the Zip Coder button, past the
        ''' edge of a two-column page, or over whatever a hand-written page put beside it - and the
        ''' width the page chose is a deliberate statement this cannot know better than.
        '''
        ''' Multiline boxes are left alone. Their size says how many lines to show, which has
        ''' nothing to do with how many characters the column holds.
        ''' </summary>
        Public Function SizedFieldWidth(maxLength As Integer,
                                        controlFont As Font,
                                        currentWidth As Integer,
                                        multiline As Boolean) As Integer
            If maxLength <= 0 OrElse multiline OrElse currentWidth <= MinimumSizedFieldWidth Then Return currentWidth

            Dim sample As New String("n"c, Math.Min(maxLength, 40))
            Dim measured = TextRenderer.MeasureText(sample, controlFont).Width + 12

            Return Math.Max(MinimumSizedFieldWidth, Math.Min(measured, currentWidth))
        End Function

        ''' <summary>
        ''' How many characters the column behind a control holds, or zero when nothing says.
        '''
        ''' The page's own control-to-column map is asked first; the control's name is only a
        ''' fallback, for a page whose map does not carry it.
        ''' </summary>
        Public Function ResolveColumnMaxLength(controlName As String,
                                               controlMap As Dictionary(Of String, String),
                                               columnLengths As Dictionary(Of String, Integer)) As Integer
            If columnLengths Is Nothing Then Return 0

            Dim mappedColumn As String = Nothing
            If controlMap IsNot Nothing AndAlso controlMap.TryGetValue(controlName, mappedColumn) AndAlso
               Not String.IsNullOrWhiteSpace(mappedColumn) Then
                If columnLengths.ContainsKey(mappedColumn) Then
                    Return columnLengths(mappedColumn)
                End If
            End If

            Dim fallbackColumn = InferColumnNameFromControlName(controlName)
            If fallbackColumn <> String.Empty AndAlso columnLengths.ContainsKey(fallbackColumn) Then
                Return columnLengths(fallbackColumn)
            End If

            Return 0
        End Function

        ''' <summary>The column a text control is named for, by the naming convention.</summary>
        Public Function InferColumnNameFromControlName(controlName As String) As String
            If String.IsNullOrWhiteSpace(controlName) Then
                Return String.Empty
            End If

            Dim prefixes = New String() {"TextBox_", "MaskedTextBox_", "RichTextBox_"}
            For Each prefix In prefixes
                If controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return controlName.Substring(prefix.Length)
                End If
            Next

            Return String.Empty
        End Function

        ''' <summary>A page with one column is this wide whatever it holds.</summary>
        Public Const SingleColumnPageWidth As Integer = 600

        ''' <summary>Room past the right column for the Zip Coder button when Zip is over there.</summary>
        Public Const ZipButtonMargin As Integer = 140

        ''' <summary>Room past the right column otherwise.</summary>
        Public Const PageMargin As Integer = 30

        ''' <summary>A page is never shorter than this, however few fields it has.</summary>
        Public Const MinimumPageHeight As Integer = 120

        ''' <summary>
        ''' OK and Cancel, as a widget with a footprint rather than three magic numbers.
        '''
        ''' The generator emitted Width - 270, Width - 135 and Height - 46, which is this
        ''' arithmetic written out longhand - the same way 450 was 120 + 10 + 320 until this
        ''' morning. Derived, the emitted numbers are identical and the page can be asked whether
        ''' it is wide enough to hold its own buttons.
        ''' </summary>
        Public Const ButtonWidth As Integer = 120
        Public Const ButtonHeight As Integer = 36
        Public Const ButtonGap As Integer = 15
        Public Const ButtonRightGap As Integer = 15
        Public Const ButtonBottomGap As Integer = 10

        ''' <summary>From OK's left edge to the right edge of the page. The emitted 270.</summary>
        Public Const ButtonRowWidth As Integer = ButtonWidth + ButtonGap + ButtonWidth + ButtonRightGap

        ''' <summary>The band below the fields that holds OK and Cancel. The emitted 55.</summary>
        Public Const ButtonRowHeight As Integer = ButtonHeight + 19

        ''' <summary>The gap between the last field row and whatever a companion puts below it.</summary>
        Public Const ReservedGap As Integer = 8

        ''' <summary>
        ''' Where the second column starts. Wider apart when column one holds Zip, because the Zip
        ''' Coder button sits past the right edge of that box and needs somewhere to be.
        ''' </summary>
        Public Function ColumnTwoLeft(wantsZipCoder As Boolean, zipOnTheRight As Boolean) As Integer
            Return ColumnLeft + ColumnWidth + If(wantsZipCoder AndAlso Not zipOnTheRight, ZipColumnGap, PlainColumnGap)
        End Function

        ''' <summary>
        ''' How wide the page is - never narrower than what it has to hold.
        '''
        ''' A single column was a flat 600 whatever was on the page, so a page reserving room for
        ''' the 616 wide roles panel was built around a widget wider than itself, and the panel ran
        ''' off the right edge. The button floor has never bitten, and is here so a narrow page
        ''' cannot be generated with its own buttons hanging off either.
        ''' </summary>
        Public Function PageWidth(twoColumns As Boolean,
                                  columnTwoLeft As Integer,
                                  zipOnTheRight As Boolean,
                                  reservedWidth As Integer) As Integer
            Dim natural = If(twoColumns,
                             columnTwoLeft + ColumnWidth + If(zipOnTheRight, ZipButtonMargin, PageMargin),
                             SingleColumnPageWidth)

            Dim floor = ColumnLeft + ButtonRowWidth
            If reservedWidth > 0 Then floor = Math.Max(floor, ColumnLeft + reservedWidth + PageMargin)

            Return Math.Max(natural, floor)
        End Function

        ''' <summary>
        ''' How tall the page is, before the caption shift. A companion that hosts something under
        ''' the fields asks for the room through extraBelowFields - the generator cannot know what
        ''' a companion will add, and a page that resizes itself afterwards flickers on every open.
        ''' </summary>
        Public Function PageHeight(rowsDown As Integer,
                                   extraBelowFields As Integer,
                                   fieldsBottom As Integer,
                                   reservedHeight As Integer) As Integer
            Dim natural = Math.Max(MinimumPageHeight, ButtonRowHeight + rowsDown * RowPitch) + extraBelowFields
            If reservedHeight <= 0 Then Return natural

            ' The reserved band is 16 taller than the panel in it, and the button row needs 55. A
            ' page sized only by the natural figure put OK's top three pixels inside the panel on
            ' every employee page - invisible while the grids inside stopped short of the panel's
            ' own bottom, and plain the moment nothing else was there to hide it.
            Return Math.Max(natural, fieldsBottom + ReservedGap + reservedHeight + ButtonRowHeight)
        End Function

        ''' <summary>Where the generated fields stop, for whatever a companion puts underneath.</summary>
        Public Function FieldsBottom(rowsDown As Integer) As Integer
            Return FirstRow + rowsDown * RowPitch
        End Function

    End Module
End Namespace
