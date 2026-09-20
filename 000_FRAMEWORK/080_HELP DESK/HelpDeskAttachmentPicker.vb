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

    ''' <summary>
    ''' An attachment as the list shows it - everything but the bytes.
    ''' </summary>
    Public Class HelpDeskAttachmentSummary
        Public Property AttachmentID As Integer
        Public Property FileName As String
        Public Property ContentType As String
        Public Property FileSize As Long
        Public Property CreatedOn As DateTime
    End Class

    Public Interface IHelpDeskAttachmentPicker
        Function Pick(owner As IWin32Window) As HelpDeskAttachmentUpload
    End Interface

    ''' <summary>
    ''' Shared by both pickers, because a file is the same file whichever dialog chose it.
    ''' </summary>
    Friend Module AttachmentContentType
        Friend Function ForExtension(extension As String) As String
            Select Case If(extension, String.Empty).ToLowerInvariant()
                Case ".png" : Return "image/png"
                Case ".jpg", ".jpeg" : Return "image/jpeg"
                Case ".gif" : Return "image/gif"
                Case ".pdf" : Return "application/pdf"
                Case ".txt" : Return "text/plain"
                Case Else : Return "application/octet-stream"
            End Select
        End Function
    End Module

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
                    .ContentType = AttachmentContentType.ForExtension(fileInfo.Extension),
                    .FileSize = fileInfo.Length,
                    .FileData = File.ReadAllBytes(fileInfo.FullName)
                }
            End Using
        End Function

    End Class

    ''' <summary>
    ''' Picks a file from the *user's* machine when the application is being watched in a browser.
    '''
    ''' OpenFileDialog cannot do this. The process runs on the server, so that dialog browses the
    ''' server's drives - a machine the user has never seen and has no files on. VirtualUI's picker
    ''' shows the browser's file system instead, copies the chosen file to a directory on the
    ''' server, and hands back that path.
    '''
    ''' The copy is a stepping stone, not storage. The bytes go into the database like any other
    ''' field, and the file is deleted on the way out - otherwise the server accumulates a copy of
    ''' every attachment ever added.
    ''' </summary>
    Public Class ThinfinityHelpDeskAttachmentPicker
        Implements IHelpDeskAttachmentPicker

        Private ReadOnly session As Cybele.Thinfinity.VirtualUI

        Public Sub New(virtualUISession As Cybele.Thinfinity.VirtualUI)
            If virtualUISession Is Nothing Then
                Throw New ArgumentNullException(NameOf(virtualUISession))
            End If

            session = virtualUISession
        End Sub

        Public Function Pick(owner As IWin32Window) As HelpDeskAttachmentUpload Implements IHelpDeskAttachmentPicker.Pick
            Dim uploadedPath As String = Nothing
            If Not session.UploadFileEx(uploadedPath) Then
                Return Nothing
            End If

            If String.IsNullOrWhiteSpace(uploadedPath) OrElse Not File.Exists(uploadedPath) Then
                Return Nothing
            End If

            Try
                Dim fileInfo As New FileInfo(uploadedPath)
                Return New HelpDeskAttachmentUpload With {
                    .FileName = fileInfo.Name,
                    .ContentType = AttachmentContentType.ForExtension(fileInfo.Extension),
                    .FileSize = fileInfo.Length,
                    .FileData = File.ReadAllBytes(fileInfo.FullName)
                }
            Finally
                ' Swallowed deliberately. The attachment is already read by this point, and a file
                ' that will not delete - antivirus still holding it, most likely - is not a reason
                ' to fail an attachment the user has successfully chosen.
                Try
                    File.Delete(uploadedPath)
                Catch telemetryEx As Exception
                    Telemetry.Error(telemetryEx, "HelpDeskAttachmentPicker.Pick")
                End Try
            End Try
        End Function
    End Class

    ''' <summary>
    ''' Getting an attachment out of the database and onto the user's machine - the reverse of the
    ''' picker, and split the same way for the same reason.
    ''' </summary>
    Public Interface IHelpDeskAttachmentDelivery
        Sub Deliver(owner As IWin32Window, attachment As HelpDeskAttachmentUpload)
    End Interface

    ''' <summary>
    ''' Desktop: ask where to put it, and write it there.
    ''' </summary>
    Public Class WindowsHelpDeskAttachmentDelivery
        Implements IHelpDeskAttachmentDelivery

        Public Sub Deliver(owner As IWin32Window, attachment As HelpDeskAttachmentUpload) Implements IHelpDeskAttachmentDelivery.Deliver
            If attachment Is Nothing OrElse attachment.FileData Is Nothing Then Return

            Using dialog As New SaveFileDialog() With {
                .Title = "Save attachment",
                .FileName = attachment.FileName,
                .Filter = "All files (*.*)|*.*",
                .OverwritePrompt = True
            }
                If dialog.ShowDialog(owner) <> DialogResult.OK Then Return
                File.WriteAllBytes(dialog.FileName, attachment.FileData)
            End Using
        End Sub
    End Class

    ''' <summary>
    ''' Browser: write the bytes somewhere on the server, hand the path to VirtualUI, and let it
    ''' push the file to the browser as a download.
    '''
    ''' The temp file goes the same way as the picker's: deleted once VirtualUI has been given it.
    ''' DownloadFile is synchronous as far as the caller is concerned - it hands the file to the
    ''' session - so deleting afterwards is safe.
    ''' </summary>
    Public Class ThinfinityHelpDeskAttachmentDelivery
        Implements IHelpDeskAttachmentDelivery

        Private ReadOnly session As Cybele.Thinfinity.VirtualUI

        Public Sub New(virtualUISession As Cybele.Thinfinity.VirtualUI)
            If virtualUISession Is Nothing Then Throw New ArgumentNullException(NameOf(virtualUISession))
            session = virtualUISession
        End Sub

        Public Sub Deliver(owner As IWin32Window, attachment As HelpDeskAttachmentUpload) Implements IHelpDeskAttachmentDelivery.Deliver
            If attachment Is Nothing OrElse attachment.FileData Is Nothing Then Return

            ' A folder of our own under the temp path, so a name that collides with something else
            ' in use cannot overwrite it, and the file keeps its own name for the browser to save.
            Dim folder = Path.Combine(Path.GetTempPath(), "SDC_Attachments", Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(folder)
            Dim staged = Path.Combine(folder, SafeFileName(attachment.FileName))

            Try
                File.WriteAllBytes(staged, attachment.FileData)
                session.DownloadFile(staged, attachment.FileName, attachment.ContentType)
            Finally
                Try
                    Directory.Delete(folder, True)
                Catch telemetryEx As Exception
                    Telemetry.Error(telemetryEx, "HelpDeskAttachmentPicker.Deliver")
                End Try
            End Try
        End Sub

        Private Shared Function SafeFileName(name As String) As String
            Dim candidate = If(name, "attachment")
            For Each invalid In Path.GetInvalidFileNameChars()
                candidate = candidate.Replace(invalid, "_"c)
            Next
            Return If(candidate.Trim().Length = 0, "attachment", candidate)
        End Function
    End Class

    ''' <summary>
    ''' Which implementation this run should use. One question asked in one place, so a second page
    ''' that attaches or downloads a file cannot answer it differently.
    ''' </summary>
    Public Module HelpDeskAttachmentPickers
        Public Function ForCurrentSession() As IHelpDeskAttachmentPicker
            If InBrowser() Then Return New ThinfinityHelpDeskAttachmentPicker(Program.VirtualUISession)
            Return New WindowsHelpDeskAttachmentPicker()
        End Function

        Public Function DeliveryForCurrentSession() As IHelpDeskAttachmentDelivery
            If InBrowser() Then Return New ThinfinityHelpDeskAttachmentDelivery(Program.VirtualUISession)
            Return New WindowsHelpDeskAttachmentDelivery()
        End Function

        Private Function InBrowser() As Boolean
            Return Program.InBrowserSession AndAlso Program.VirtualUISession IsNot Nothing
        End Function
    End Module
End Namespace
