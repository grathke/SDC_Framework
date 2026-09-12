Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Where the repository's images live at run time, and how to load one.
    '''
    ''' The folder is not simply next to the executable. `assets\images\**` is copied to the output
    ''' directory by the project file, so a published build finds it there - but a developer
    ''' running from source has the same files three or four levels up, and the icon picker has
    ''' always walked back to find them. That walk lived privately inside IconPicker until the main
    ''' menu's region needed a picture too; one copy, here, rather than two that can disagree about
    ''' where the images are.
    ''' </summary>
    Public Module AssetImages

        ''' <summary>
        ''' The images root, or an empty string if none of the candidates exist - a build with the
        ''' assets missing should show no picture rather than throw.
        ''' </summary>
        Public Function Folder() As String
            Dim candidates As String() = {
                Path.Combine(Application.StartupPath, "assets", "images"),
                Path.Combine(Application.StartupPath, "..", "..", "..", "assets", "images"),
                Path.Combine(Application.StartupPath, "..", "..", "..", "..", "assets", "images")
            }

            For Each candidate In candidates
                Dim fullPath = Path.GetFullPath(candidate)
                If Directory.Exists(fullPath) Then Return fullPath
            Next

            Return String.Empty
        End Function

        ''' <summary>
        ''' Loads an image by its path under the images root - "QDesk\QDeskAdministration.png".
        '''
        ''' Returns Nothing rather than throwing when the file is absent or unreadable: a missing
        ''' decorative image is not a reason to fail the control that wanted it, and the caller can
        ''' simply leave the space empty.
        '''
        ''' Read through a stream and copied, so the file is not locked for the life of the image -
        ''' Image.FromFile holds the handle open, which stops anyone replacing the picture while
        ''' the application is running.
        ''' </summary>
        Public Function Load(relativePath As String) As Image
            If String.IsNullOrWhiteSpace(relativePath) Then Return Nothing

            Dim root = Folder()
            If root.Length = 0 Then Return Nothing

            Dim fullPath = Path.Combine(root, relativePath)
            If Not File.Exists(fullPath) Then Return Nothing

            Try
                Using source = Image.FromStream(New MemoryStream(File.ReadAllBytes(fullPath)))
                    Return New Bitmap(source)
                End Using
            Catch
                Return Nothing
            End Try
        End Function

    End Module

End Namespace
