Imports System
Imports System.ComponentModel
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms

Friend NotInheritable Class KeyboardSender
    Private Sub New()
    End Sub

    Private Const INPUT_KEYBOARD As UInteger = 1UI
    Private Const KEYEVENTF_KEYUP As UInteger = 2UI
    Private Shared ReadOnly HeldSync As New Object()
    Private Shared ReadOnly HeldDeadlines As New Dictionary(Of Integer, DateTime)()
    Private Shared ReadOnly HeldWatchdog As New System.Threading.Timer(AddressOf ReleaseExpiredKeys, Nothing, 250, 250)

    <StructLayout(LayoutKind.Sequential)>
    Private Structure INPUT
        Public Type As UInteger
        Public Data As INPUTUNION
    End Structure

    <StructLayout(LayoutKind.Explicit)>
    Private Structure INPUTUNION
        <FieldOffset(0)> Public Keyboard As KEYBDINPUT
        <FieldOffset(0)> Public Mouse As MOUSEINPUT
        <FieldOffset(0)> Public Hardware As HARDWAREINPUT
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MOUSEINPUT
        Public X As Integer
        Public Y As Integer
        Public MouseData As UInteger
        Public Flags As UInteger
        Public Time As UInteger
        Public ExtraInfo As UIntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure HARDWAREINPUT
        Public Message As UInteger
        Public ParameterLow As UShort
        Public ParameterHigh As UShort
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure KEYBDINPUT
        Public VirtualKey As UShort
        Public ScanCode As UShort
        Public Flags As UInteger
        Public Time As UInteger
        Public ExtraInfo As UIntPtr
    End Structure

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SendInput(inputCount As UInteger, inputs() As INPUT, size As Integer) As UInteger
    End Function

    Public Shared Sub Press(key As Keys)
        PressCombination(New List(Of Keys) From {key})
    End Sub

    Public Shared Sub PressCombination(keySequence As IList(Of Keys))
        If keySequence Is Nothing OrElse keySequence.Count = 0 Then Throw New ArgumentException("A combinação não possui teclas.", "keySequence")
        Dim data((keySequence.Count * 2) - 1) As INPUT
        For index = 0 To keySequence.Count - 1
            data(index).Type = INPUT_KEYBOARD
            data(index).Data.Keyboard.VirtualKey = CUShort(CInt(keySequence(index)) And CInt(Keys.KeyCode))
            Dim releaseIndex = (keySequence.Count * 2) - 1 - index
            data(releaseIndex).Type = INPUT_KEYBOARD
            data(releaseIndex).Data.Keyboard.VirtualKey = data(index).Data.Keyboard.VirtualKey
            data(releaseIndex).Data.Keyboard.Flags = KEYEVENTF_KEYUP
        Next

        Dim expected = CUInt(data.Length)
        If SendInput(expected, data, Marshal.SizeOf(GetType(INPUT))) <> expected Then
            Throw New Win32Exception(Marshal.GetLastWin32Error())
        End If
    End Sub

    Public Shared Function HoldCombination(keySequence As IList(Of Keys)) As Boolean
        ValidateSequence(keySequence)
        Dim keysToPress As New List(Of Keys)()
        SyncLock HeldSync
            Dim deadline = DateTime.UtcNow.AddMilliseconds(1500)
            For Each key In keySequence
                Dim keyCode = CInt(key) And CInt(Keys.KeyCode)
                If Not HeldDeadlines.ContainsKey(keyCode) Then keysToPress.Add(CType(keyCode, Keys))
                HeldDeadlines(keyCode) = deadline
            Next
            SendKeyBatch(keysToPress, False)
        End SyncLock
        Return keysToPress.Count > 0
    End Function

    Public Shared Function ReleaseCombination(keySequence As IList(Of Keys)) As Boolean
        ValidateSequence(keySequence)
        Dim keysToRelease As New List(Of Keys)()
        SyncLock HeldSync
            For index = keySequence.Count - 1 To 0 Step -1
                Dim keyCode = CInt(keySequence(index)) And CInt(Keys.KeyCode)
                If HeldDeadlines.Remove(keyCode) Then keysToRelease.Add(CType(keyCode, Keys))
            Next
            SendKeyBatch(keysToRelease, True)
        End SyncLock
        Return keysToRelease.Count > 0
    End Function

    Public Shared Sub ReleaseAll()
        Dim keysToRelease As New List(Of Keys)()
        SyncLock HeldSync
            For Each keyCode In HeldDeadlines.Keys
                keysToRelease.Add(CType(keyCode, Keys))
            Next
            HeldDeadlines.Clear()
            SendKeyBatch(keysToRelease, True)
        End SyncLock
    End Sub

    Private Shared Sub ReleaseExpiredKeys(state As Object)
        Try
            Dim expired As New List(Of Keys)()
            SyncLock HeldSync
                Dim now = DateTime.UtcNow
                Dim expiredCodes As New List(Of Integer)()
                For Each pair In HeldDeadlines
                    If pair.Value <= now Then expiredCodes.Add(pair.Key)
                Next
                For Each keyCode In expiredCodes
                    HeldDeadlines.Remove(keyCode)
                    expired.Add(CType(keyCode, Keys))
                Next
                SendKeyBatch(expired, True)
            End SyncLock
        Catch
        End Try
    End Sub

    Private Shared Sub ValidateSequence(keySequence As IList(Of Keys))
        If keySequence Is Nothing OrElse keySequence.Count = 0 Then Throw New ArgumentException("A combinação não possui teclas.", "keySequence")
    End Sub

    Private Shared Sub SendKeyBatch(keySequence As IList(Of Keys), release As Boolean)
        If keySequence.Count = 0 Then Return
        Dim data(keySequence.Count - 1) As INPUT
        For index = 0 To keySequence.Count - 1
            data(index).Type = INPUT_KEYBOARD
            data(index).Data.Keyboard.VirtualKey = CUShort(CInt(keySequence(index)) And CInt(Keys.KeyCode))
            If release Then data(index).Data.Keyboard.Flags = KEYEVENTF_KEYUP
        Next
        Dim expected = CUInt(data.Length)
        If SendInput(expected, data, Marshal.SizeOf(GetType(INPUT))) <> expected Then Throw New Win32Exception(Marshal.GetLastWin32Error())
    End Sub

    Public Shared Function TryParseShortcut(value As String, ByRef keySequence As List(Of Keys), ByRef errorMessage As String) As Boolean
        keySequence = New List(Of Keys)()
        errorMessage = Nothing
        If String.IsNullOrWhiteSpace(value) Then Return True

        Dim usedCodes As New HashSet(Of Integer)()
        For Each rawPart In value.Split("+"c)
            Dim part = rawPart.Trim()
            Dim parsed As Keys
            Select Case part.ToUpperInvariant()
                Case "CTRL", "CONTROL"
                    parsed = Keys.ControlKey
                Case "ALT"
                    parsed = Keys.Menu
                Case "SHIFT"
                    parsed = Keys.ShiftKey
                Case "WIN", "WINDOWS"
                    parsed = Keys.LWin
                Case "ESC"
                    parsed = Keys.Escape
                Case "DEL"
                    parsed = Keys.Delete
                Case "PGUP"
                    parsed = Keys.PageUp
                Case "PGDN"
                    parsed = Keys.PageDown
                Case Else
                    If part.Length = 1 AndAlso Char.IsDigit(part(0)) Then
                        parsed = CType(CInt(Keys.D0) + (Convert.ToInt32(part(0)) - Convert.ToInt32("0"c)), Keys)
                    ElseIf part.Length = 0 OrElse Char.IsDigit(part(0)) OrElse Not [Enum].TryParse(part, True, parsed) Then
                        errorMessage = "Tecla desconhecida na macro: " & If(part.Length = 0, "(vazia)", part)
                        Return False
                    End If
            End Select
            Dim keyCode = CInt(parsed) And CInt(Keys.KeyCode)
            If keyCode <= 0 OrElse keyCode > 255 Then
                errorMessage = "Tecla inválida na macro: " & part
                Return False
            End If
            If usedCodes.Add(keyCode) Then keySequence.Add(CType(keyCode, Keys))
        Next

        Dim hasCtrl = keySequence.Contains(Keys.ControlKey)
        Dim hasAlt = keySequence.Contains(Keys.Menu)
        Dim hasDelete = keySequence.Contains(Keys.Delete)
        If hasCtrl AndAlso hasAlt AndAlso hasDelete Then
            errorMessage = "Ctrl+Alt+Del é reservado pelo Windows e não pode ser enviado por um aplicativo comum."
            Return False
        End If
        Return keySequence.Count > 0
    End Function
End Class
