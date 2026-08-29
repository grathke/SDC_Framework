Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    ''' <summary>
    ''' Owns the Zip Coder button on maintenance pages that edit City / State / Zip.
    ''' The button is the manual alternative to the Smarty type-ahead, so it is shown only
    ''' while Smarty embedded-key lookup is off for the active session, and hidden while it
    ''' is on. Pages must not build their own Zip Coder button or repeat the visibility test.
    ''' </summary>
    Public NotInheritable Class ZipCoderController
        Public Const ButtonName As String = "Button_ZipCoder"
        Public Const ButtonCaption As String = "Zip Coder"

        Private Const GapFromZip As Integer = 10
        Private Const ButtonWidth As Integer = 90
        Private Const ButtonHeight As Integer = 26

        Private ReadOnly zipCoderButton As Button
        Private ReadOnly zipTextBox As TextBox
        Private ReadOnly cityTextBox As TextBox
        Private ReadOnly stateTextBox As TextBox

        ''' <summary>
        ''' Creates the button, places it immediately to the right of the Zip text box and
        ''' applies the current visibility rule. Call from the page constructor after the
        ''' Zip text box has its final size and position.
        ''' </summary>
        Public Sub New(owner As Form,
                       cityTextBox As TextBox,
                       stateTextBox As TextBox,
                       zipTextBox As TextBox)
            If owner Is Nothing Then Throw New ArgumentNullException(NameOf(owner))
            If zipTextBox Is Nothing Then Throw New ArgumentNullException(NameOf(zipTextBox))

            Me.zipTextBox = zipTextBox
            Me.cityTextBox = cityTextBox
            Me.stateTextBox = stateTextBox

            zipCoderButton = New Button() With {
                .Name = ButtonName,
                .Text = ButtonCaption,
                .Size = New Size(ButtonWidth, ButtonHeight),
                .Location = New Point(zipTextBox.Right + GapFromZip, zipTextBox.Top - 1),
                .TabStop = True,
                .UseVisualStyleBackColor = True
            }

            AddHandler zipCoderButton.Click, AddressOf ZipCoderButton_Click
            owner.Controls.Add(zipCoderButton)
            zipCoderButton.BringToFront()

            Dim zipCoderToolTip As New ToolTip()
            zipCoderToolTip.SetToolTip(zipCoderButton, "Look up City and State from the Zip code")

            RefreshVisibility()
        End Sub

        ''' <summary>
        ''' The button itself, so a page can place it in its manual tab order.
        ''' </summary>
        Public ReadOnly Property ZipCoderButton_Control As Button
            Get
                Return zipCoderButton
            End Get
        End Property

        ''' <summary>
        ''' Re-applies the visibility rule. Call after anything that can change whether the
        ''' session is using Smarty.
        ''' </summary>
        Public Sub RefreshVisibility()
            zipCoderButton.Visible = Not SmartyAddressLookupController.IsSessionLookupEnabled()
        End Sub

        Private Sub ZipCoderButton_Click(sender As Object, e As EventArgs)
            ' TODO: no zip-code data source exists yet. When one is added, this handler is the
            ' single owner of the lookup for every page that hosts the button. Do not add a
            ' page-local click handler.
            Dim zipText = If(zipTextBox Is Nothing, String.Empty, zipTextBox.Text.Trim())
            Dim message = If(String.IsNullOrEmpty(zipText),
                             "Zip Coder is not connected to a zip-code source yet." & Environment.NewLine &
                             "Enter a Zip code first.",
                             "Zip Coder is not connected to a zip-code source yet." & Environment.NewLine &
                             "Zip entered: " & zipText)

            MessageBox.Show(message, ButtonCaption, MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub
    End Class
End Namespace
