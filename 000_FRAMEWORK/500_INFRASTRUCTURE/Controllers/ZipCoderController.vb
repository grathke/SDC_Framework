Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework
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

        ''' <summary>
        ''' Opens the lookup, seeded with whatever the page already has, and writes the chosen
        ''' place back into the three boxes.
        '''
        ''' The single owner of the lookup for every page that hosts the button, which is what the
        ''' TODO here asked for before FW_ZipCodes was connected on 2026-09-11. Do not add a
        ''' page-local click handler: the controller holds the three text boxes precisely so this
        ''' can be done once.
        '''
        ''' Only the fields the search actually returns are written. Cancel writes nothing.
        ''' </summary>
        Private Sub ZipCoderButton_Click(sender As Object, e As EventArgs)
            Dim cityText = If(cityTextBox Is Nothing, String.Empty, cityTextBox.Text)
            Dim stateText = If(stateTextBox Is Nothing, String.Empty, stateTextBox.Text)
            Dim zipText = If(zipTextBox Is Nothing, String.Empty, zipTextBox.Text)

            Using lookup As New ZipCodeLookupDialog(cityText, stateText, zipText)
                If lookup.ShowDialog(zipCoderButton.FindForm()) <> DialogResult.OK Then
                    Return
                End If

                If cityTextBox IsNot Nothing Then cityTextBox.Text = lookup.SelectedCity
                If stateTextBox IsNot Nothing Then stateTextBox.Text = lookup.SelectedState
                If zipTextBox IsNot Nothing Then zipTextBox.Text = lookup.SelectedZip
            End Using
        End Sub
    End Class
End Namespace
