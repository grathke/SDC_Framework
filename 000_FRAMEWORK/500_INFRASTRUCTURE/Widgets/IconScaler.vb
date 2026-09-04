Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Loads an icon from assets\images at the size the surface needs.
    '''
    ''' A Button draws its Image at native size and clips the rest - it does not scale - so a large
    ''' source lands as its own top left corner. Scaling here means one authored file works on a
    ''' dashboard tile, a menu tile or a header button.
    '''
    ''' It only ever scales **down**. Enlarging a small icon makes it soft, so an icon smaller than
    ''' the target is left exactly as it is: close.png at 24 stays sharp at 24 rather than being
    ''' stretched to fill a tile.
    ''' </summary>
    Public NotInheritable Class IconScaler

        Private Sub New()
        End Sub

        Public Shared Function Load(fileName As String, target As Integer, fallback As Image) As Image
            Dim fullPath = Resolve(fileName)
            If fullPath = String.Empty Then Return fallback

            Try
                ' Through a stream so the file is not locked for the life of the process.
                Using stream As New FileStream(fullPath, FileMode.Open, FileAccess.Read)
                    Using source = Image.FromStream(stream)
                        Return Fit(source, target)
                    End Using
                End Using
            Catch
                Return fallback
            End Try
        End Function

        ''' Returns a copy no larger than target on either side, keeping the aspect ratio. An image
        ''' already within the target is copied unchanged.
        Public Shared Function Fit(source As Image, target As Integer) As Image
            If source Is Nothing Then Return Nothing
            If target <= 0 OrElse (source.Width <= target AndAlso source.Height <= target) Then
                Return New Bitmap(source)
            End If

            Dim scale = Math.Min(target / CDbl(source.Width), target / CDbl(source.Height))
            Dim width = Math.Max(1, CInt(Math.Round(source.Width * scale)))
            Dim height = Math.Max(1, CInt(Math.Round(source.Height * scale)))

            Dim scaled As New Bitmap(width, height)
            Using g = Graphics.FromImage(scaled)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.SmoothingMode = SmoothingMode.HighQuality
                g.CompositingQuality = CompositingQuality.HighQuality
                g.DrawImage(source, New Rectangle(0, 0, width, height))
            End Using
            Return scaled
        End Function

        Private Shared Function Resolve(fileName As String) As String
            If String.IsNullOrWhiteSpace(fileName) Then Return String.Empty

            Dim candidates As String() = {
                Path.Combine(Application.StartupPath, "assets", "images", fileName),
                Path.Combine(Application.StartupPath, "..", "..", "..", "assets", "images", fileName),
                Path.Combine(Application.StartupPath, "..", "..", "..", "..", "assets", "images", fileName)
            }

            For Each candidate In candidates
                Dim fullPath = Path.GetFullPath(candidate)
                If File.Exists(fullPath) Then Return fullPath
            Next

            Return String.Empty
        End Function

    End Class

End Namespace