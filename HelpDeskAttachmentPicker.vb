Option Strict On
Option Explicit On

Imports System
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class HelpDeskAttachmentUpload
        Public Property FileName As String
        Public Property ContentType As String
        Public Property FileSize As Long
        Public Property FileData As Byte()
    End Class

    Public Interface IHelpDeskAttachmentPicker
        Function Pick(owner As IWin32Window) As HelpDeskAttachmentUpload
    End Interface

    Public Class WindowsHelpDeskAttachmentPicker
        Implements IHelpDeskAttachmentPicker

        Public Function Pick(owner As IWin32Window) As HelpDeskAttachmentUpload Implements IHelpDeskAttachmentPicker.Pick
            Using dialog As New OpenFileDialog() With {
                .Title = "Attach a file",
                .Filter = "All files (*.*)|*.*",
                .CheckFileExists = True,
                .Multiselect = False
            }
                If dialog.ShowDialog(owner) <> DialogResult.OK Then
                    Return Nothing
                End If

                Dim fileInfo As New FileInfo(dialog.FileName)
                Return New HelpDeskAttachmentUpload With {
                    .FileName = fileInfo.Name,
                    .ContentType = ResolveContentType(fileInfo.Extension),
                    .FileSize = fileInfo.Length,
                    .FileData = File.ReadAllBytes(fileInfo.FullName)
                }
            End Using
        End Function

        Private Shared Function ResolveContentType(extension As String) As String
            Select Case If(extension, String.Empty).ToLowerInvariant()
                Case ".png" : Return "image/png"
                Case ".jpg", ".jpeg" : Return "image/jpeg"
                Case ".gif" : Return "image/gif"
                Case ".pdf" : Return "application/pdf"
                Case ".txt" : Return "text/plain"
                Case Else : Return "application/octet-stream"
            End Select
        End Function
    End Class

    Public Class ThinfinityHelpDeskAttachmentPicker
        Implements IHelpDeskAttachmentPicker

        Private ReadOnly picker As Func(Of IWin32Window, HelpDeskAttachmentUpload)

        Public Sub New(customPicker As Func(Of IWin32Window, HelpDeskAttachmentUpload))
            If customPicker Is Nothing Then
                Throw New ArgumentNullException(NameOf(customPicker))
            End If

            picker = customPicker
        End Sub

        Public Function Pick(owner As IWin32Window) As HelpDeskAttachmentUpload Implements IHelpDeskAttachmentPicker.Pick
            Return picker(owner)
        End Function
    End Class
End Namespace
