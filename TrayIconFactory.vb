Imports System
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices

Friend Enum TrayState
    Inactive
    Active
    KeySent
End Enum

Friend NotInheritable Class TrayIconFactory
    Private Sub New()
    End Sub

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function DestroyIcon(handle As IntPtr) As Boolean
    End Function

    Public Shared Function Create(state As TrayState) As Icon
        Using bitmap As New Bitmap(32, 32)
            Using graphics = System.Drawing.Graphics.FromImage(bitmap)
                graphics.SmoothingMode = SmoothingMode.AntiAlias
                graphics.Clear(Color.Transparent)
            Dim baseColor As Color
            Select Case state
                Case TrayState.Active : baseColor = Color.FromArgb(38, 176, 92)
                Case TrayState.KeySent : baseColor = Color.FromArgb(37, 99, 235)
                Case Else : baseColor = Color.FromArgb(115, 125, 140)
            End Select
            Using shadow As New SolidBrush(Color.FromArgb(70, 0, 0, 0))
                graphics.FillEllipse(shadow, 3, 4, 27, 27)
            End Using
            Using brush As New SolidBrush(baseColor)
                graphics.FillEllipse(brush, 2, 2, 27, 27)
            End Using
            Using pen As New Pen(Color.White, 3.0F)
                pen.StartCap = LineCap.Round
                pen.EndCap = LineCap.Round
                Select Case state
                    Case TrayState.Active
                        graphics.DrawLine(pen, 10, 16, 14, 20)
                        graphics.DrawLine(pen, 14, 20, 22, 11)
                    Case TrayState.KeySent
                        Using bolt As New SolidBrush(Color.FromArgb(255, 214, 64))
                            graphics.FillPolygon(bolt, New Point() {New Point(17, 6), New Point(9, 18), New Point(15, 18), New Point(13, 27), New Point(24, 14), New Point(18, 14)})
                        End Using
                    Case Else
                        graphics.DrawLine(pen, 9, 16, 22, 16)
                End Select
            End Using
            End Using
            Dim handle = bitmap.GetHicon()
            Try
                Using temporary = Icon.FromHandle(handle)
                    Return DirectCast(temporary.Clone(), Icon)
                End Using
            Finally
                DestroyIcon(handle)
            End Try
        End Using
    End Function
End Class
