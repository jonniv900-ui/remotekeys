Imports System
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

Friend NotInheritable Class ScreenCapture
    Private Shared ReadOnly SyncRoot As New Object()
    Private Const CURSOR_SHOWING As Integer = 1
    Private Const DI_NORMAL As UInteger = 3UI

    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINT
        Public X As Integer
        Public Y As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure CURSORINFO
        Public Size As Integer
        Public Flags As Integer
        Public CursorHandle As IntPtr
        Public ScreenPosition As POINT
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure ICONINFO
        <MarshalAs(UnmanagedType.Bool)> Public IsIcon As Boolean
        Public HotspotX As UInteger
        Public HotspotY As UInteger
        Public MaskBitmap As IntPtr
        Public ColorBitmap As IntPtr
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function GetCursorInfo(ByRef cursorInfo As CURSORINFO) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetIconInfo(iconHandle As IntPtr, ByRef iconInfo As ICONINFO) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function DrawIconEx(deviceContext As IntPtr, x As Integer, y As Integer, iconHandle As IntPtr, width As Integer, height As Integer, stepIndex As UInteger, flickerFreeBrush As IntPtr, flags As UInteger) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(handle As IntPtr) As Boolean
    End Function
    Private Sub New()
    End Sub

    Public Shared Function CaptureJpeg() As Byte()
        SyncLock SyncRoot
            Dim bounds = Screen.PrimaryScreen.Bounds
            Using source As New Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb)
                Using graphics = System.Drawing.Graphics.FromImage(source)
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy)
                    DrawMouseCursor(graphics, bounds)
                End Using
                Dim targetWidth = Math.Min(1280, source.Width)
                Dim targetHeight = CInt(source.Height * (targetWidth / CDbl(source.Width)))
                Using target As New Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb)
                    Using graphics = System.Drawing.Graphics.FromImage(target)
                        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear
                        graphics.DrawImage(source, 0, 0, targetWidth, targetHeight)
                    End Using
                    Using stream As New MemoryStream()
                        Dim jpegCodec = GetJpegEncoder()
                        Using parameters As New EncoderParameters(1)
                            parameters.Param(0) = New EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 55L)
                            target.Save(stream, jpegCodec, parameters)
                        End Using
                        Return stream.ToArray()
                    End Using
                End Using
            End Using
        End SyncLock
    End Function

    Private Shared Sub DrawMouseCursor(graphics As Graphics, screenBounds As Rectangle)
        Dim cursorInfo As New CURSORINFO With {.Size = Marshal.SizeOf(GetType(CURSORINFO))}
        If Not GetCursorInfo(cursorInfo) OrElse (cursorInfo.Flags And CURSOR_SHOWING) = 0 OrElse cursorInfo.CursorHandle = IntPtr.Zero Then Return

        Dim iconInfo As New ICONINFO()
        Dim hotspotX As Integer = 0
        Dim hotspotY As Integer = 0
        Try
            If GetIconInfo(cursorInfo.CursorHandle, iconInfo) Then
                hotspotX = CInt(iconInfo.HotspotX)
                hotspotY = CInt(iconInfo.HotspotY)
            End If
            Dim drawX = cursorInfo.ScreenPosition.X - screenBounds.Left - hotspotX
            Dim drawY = cursorInfo.ScreenPosition.Y - screenBounds.Top - hotspotY
            Dim deviceContext = graphics.GetHdc()
            Try
                DrawIconEx(deviceContext, drawX, drawY, cursorInfo.CursorHandle, 0, 0, 0UI, IntPtr.Zero, DI_NORMAL)
            Finally
                graphics.ReleaseHdc(deviceContext)
            End Try
        Finally
            If iconInfo.MaskBitmap <> IntPtr.Zero Then DeleteObject(iconInfo.MaskBitmap)
            If iconInfo.ColorBitmap <> IntPtr.Zero Then DeleteObject(iconInfo.ColorBitmap)
        End Try
    End Sub

    Private Shared Function GetJpegEncoder() As ImageCodecInfo
        For Each codec In ImageCodecInfo.GetImageEncoders()
            If codec.FormatID = ImageFormat.Jpeg.Guid Then Return codec
        Next
        Throw New InvalidOperationException("Codificador JPEG não encontrado.")
    End Function
End Class
