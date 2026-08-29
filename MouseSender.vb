Imports System
Imports System.Runtime.InteropServices

Friend NotInheritable Class MouseSender
    Private Sub New()
    End Sub

    Private Const MOUSEEVENTF_MOVE As UInteger = &H1UI
    Private Const MOUSEEVENTF_LEFTDOWN As UInteger = &H2UI
    Private Const MOUSEEVENTF_LEFTUP As UInteger = &H4UI
    Private Const MOUSEEVENTF_RIGHTDOWN As UInteger = &H8UI
    Private Const MOUSEEVENTF_RIGHTUP As UInteger = &H10UI
    Private Const MOUSEEVENTF_WHEEL As UInteger = &H800UI

    <DllImport("user32.dll")>
    Private Shared Sub mouse_event(flags As UInteger, dx As Integer, dy As Integer, data As UInteger, extraInfo As UIntPtr)
    End Sub

    Public Shared Sub MoveBy(dx As Integer, dy As Integer)
        mouse_event(MOUSEEVENTF_MOVE, Math.Max(-300, Math.Min(300, dx)), Math.Max(-300, Math.Min(300, dy)), 0UI, UIntPtr.Zero)
    End Sub

    Public Shared Sub LeftClick()
        mouse_event(MOUSEEVENTF_LEFTDOWN Or MOUSEEVENTF_LEFTUP, 0, 0, 0UI, UIntPtr.Zero)
    End Sub

    Public Shared Sub DoubleClick()
        LeftClick()
        LeftClick()
    End Sub

    Public Shared Sub RightClick()
        mouse_event(MOUSEEVENTF_RIGHTDOWN Or MOUSEEVENTF_RIGHTUP, 0, 0, 0UI, UIntPtr.Zero)
    End Sub

    Public Shared Sub Scroll(amount As Integer)
        Dim signedData = CLng(Math.Max(-10, Math.Min(10, amount))) * 120L
        Dim wheelData = CUInt(signedData And &HFFFFFFFFL)
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, wheelData, UIntPtr.Zero)
    End Sub
End Class
