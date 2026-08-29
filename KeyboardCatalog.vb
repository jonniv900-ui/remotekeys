Imports System
Imports System.Collections.Generic
Imports System.Windows.Forms

Friend NotInheritable Class RemoteKeyDefinition
    Public ReadOnly Property ActionId As String
    Public ReadOnly Property Label As String
    Public ReadOnly Property DefaultKey As Keys
    Public ReadOnly Property DefaultVisible As Boolean
    Public ReadOnly Property IsPrimary As Boolean

    Public Sub New(actionId As String, label As String, defaultKey As Keys, defaultVisible As Boolean, isPrimary As Boolean)
        Me.ActionId = actionId
        Me.Label = label
        Me.DefaultKey = defaultKey
        Me.DefaultVisible = defaultVisible
        Me.IsPrimary = isPrimary
    End Sub
End Class

Friend NotInheritable Class KeyboardCatalog
    Private Sub New()
    End Sub

    Public Shared Function Create() As List(Of RemoteKeyDefinition)
        Dim result As New List(Of RemoteKeyDefinition) From {
            New RemoteKeyDefinition("up", "Direcional para cima", Keys.Up, True, True),
            New RemoteKeyDefinition("down", "Direcional para baixo", Keys.Down, True, True),
            New RemoteKeyDefinition("left", "Direcional para esquerda", Keys.Left, True, True),
            New RemoteKeyDefinition("right", "Direcional para direita", Keys.Right, True, True),
            New RemoteKeyDefinition("pageup", "Page Up", Keys.PageUp, True, True),
            New RemoteKeyDefinition("pagedown", "Page Down", Keys.PageDown, True, True),
            New RemoteKeyDefinition("escape", "Esc", Keys.Escape, True, True),
            New RemoteKeyDefinition("enter", "Enter / OK", Keys.Enter, True, True)}

        For index = 1 To 12
            Dim functionKey = CType(CInt(Keys.F13) + index - 1, Keys)
            result.Add(New RemoteKeyDefinition("macro_" & index.ToString(), "Macro " & index.ToString(), functionKey, False, False))
        Next

        Dim primaryCodes As New HashSet(Of Integer)()
        For Each definition In result
            primaryCodes.Add(CInt(definition.DefaultKey) And CInt(Keys.KeyCode))
        Next

        Dim usedCodes As New HashSet(Of Integer)()
        For Each key As Keys In [Enum].GetValues(GetType(Keys))
            Dim keyCode = CInt(key) And CInt(Keys.KeyCode)
            If keyCode <= 0 OrElse keyCode > 255 OrElse primaryCodes.Contains(keyCode) OrElse Not usedCodes.Add(keyCode) Then Continue For
            Dim normalized = CType(keyCode, Keys)
            result.Add(New RemoteKeyDefinition("key_" & keyCode.ToString(), FriendlyName(normalized), normalized, False, False))
        Next
        Return result
    End Function

    Friend Shared Function FriendlyName(key As Keys) As String
        Select Case key
            Case Keys.Back : Return "Backspace"
            Case Keys.Capital : Return "Caps Lock"
            Case Keys.Space : Return "Espaço"
            Case Keys.Prior : Return "PgUp"
            Case Keys.Next : Return "PgDn"
            Case Keys.Return : Return "Enter"
            Case Keys.LWin : Return "Windows esquerda"
            Case Keys.RWin : Return "Windows direita"
            Case Keys.NumLock : Return "Num Lock"
            Case Keys.Scroll : Return "Scroll Lock"
            Case Else : Return key.ToString()
        End Select
    End Function
End Class
