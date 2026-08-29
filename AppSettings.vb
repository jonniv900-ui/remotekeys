Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Windows.Forms
Imports System.Xml

Friend NotInheritable Class AppSettings
    Public Property Port As Integer = 8787
    Public Property ScreenSharingEnabled As Boolean = False
    Public ReadOnly Property KeyMap As Dictionary(Of String, Keys)
    Public ReadOnly Property MacroMap As Dictionary(Of String, String)
    Public ReadOnly Property VisibleMap As Dictionary(Of String, Boolean)
    Public ReadOnly Property DisplayNameMap As Dictionary(Of String, String)
    Public ReadOnly Property ColorMap As Dictionary(Of String, String)
    Public ReadOnly Property LayoutOrder As List(Of String)
    Private ReadOnly _pairedTokens As New HashSet(Of String)(StringComparer.Ordinal)
    Private ReadOnly _sync As New Object()

    Private Shared ReadOnly LegacySettingsFolder As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wtec", "ControleRemotoLAN")
    Private Shared ReadOnly LegacySettingsFile As String = Path.Combine(LegacySettingsFolder, "config.ini")
    Private Shared ReadOnly XmlSettingsFile As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ControleRemotoLAN.config.xml")

    Public Sub New()
        KeyMap = New Dictionary(Of String, Keys)(StringComparer.OrdinalIgnoreCase) From {
            {"up", Keys.Up}, {"down", Keys.Down}, {"left", Keys.Left}, {"right", Keys.Right},
            {"pageup", Keys.PageUp}, {"pagedown", Keys.PageDown}, {"escape", Keys.Escape}, {"enter", Keys.Enter}
        }
        MacroMap = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        VisibleMap = New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        DisplayNameMap = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        ColorMap = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        LayoutOrder = New List(Of String)()
    End Sub

    Public Shared Function LoadSettings() As AppSettings
        Dim settings As New AppSettings()
        If File.Exists(XmlSettingsFile) Then
            Try
                LoadXmlSettings(settings)
                settings.EnsureCatalog()
                Return settings
            Catch
            End Try
        End If
        If Not File.Exists(LegacySettingsFile) Then
            settings.EnsureCatalog()
            Try
                settings.Save()
            Catch
            End Try
            Return settings
        End If
        Try
            For Each line In File.ReadAllLines(LegacySettingsFile, Encoding.UTF8)
                Dim separator = line.IndexOf("="c)
                If separator <= 0 Then Continue For
                Dim name = line.Substring(0, separator).Trim()
                Dim value = line.Substring(separator + 1).Trim()
                If name.Equals("Port", StringComparison.OrdinalIgnoreCase) Then
                    Dim parsedPort As Integer
                    If Integer.TryParse(value, parsedPort) AndAlso parsedPort >= 1024 AndAlso parsedPort <= 65535 Then settings.Port = parsedPort
                ElseIf name.Equals("ScreenSharingEnabled", StringComparison.OrdinalIgnoreCase) Then
                    Dim parsedScreenSharing As Boolean
                    If Boolean.TryParse(value, parsedScreenSharing) Then settings.ScreenSharingEnabled = parsedScreenSharing
                ElseIf name.Equals("LayoutOrder", StringComparison.OrdinalIgnoreCase) Then
                    settings.LayoutOrder.Clear()
                    For Each actionId In value.Split(","c)
                        If actionId.Length > 0 AndAlso Not settings.LayoutOrder.Contains(actionId) Then settings.LayoutOrder.Add(actionId)
                    Next
                ElseIf (name.Equals("SessionToken", StringComparison.OrdinalIgnoreCase) OrElse name.Equals("Pair.Token", StringComparison.OrdinalIgnoreCase)) AndAlso value.Length >= 32 Then
                    settings._pairedTokens.Add(value)
                ElseIf name.StartsWith("Key.", StringComparison.OrdinalIgnoreCase) Then
                    Dim parsedKey As Keys
                    If [Enum].TryParse(value, True, parsedKey) Then settings.KeyMap(name.Substring(4)) = parsedKey
                ElseIf name.StartsWith("Macro.", StringComparison.OrdinalIgnoreCase) Then
                    settings.MacroMap(name.Substring(6)) = value
                ElseIf name.StartsWith("Visible.", StringComparison.OrdinalIgnoreCase) Then
                    Dim parsedVisible As Boolean
                    If Boolean.TryParse(value, parsedVisible) Then settings.VisibleMap(name.Substring(8)) = parsedVisible
                ElseIf name.StartsWith("Enabled.", StringComparison.OrdinalIgnoreCase) Then
                    Dim parsedEnabled As Boolean
                    If Boolean.TryParse(value, parsedEnabled) Then settings.VisibleMap(name.Substring(8)) = parsedEnabled
                ElseIf name.StartsWith("Name.", StringComparison.OrdinalIgnoreCase) Then
                    settings.DisplayNameMap(name.Substring(5)) = value
                ElseIf name.StartsWith("Color.", StringComparison.OrdinalIgnoreCase) AndAlso IsValidColor(value) Then
                    settings.ColorMap(name.Substring(6)) = value.ToUpperInvariant()
                End If
            Next
        Catch
        End Try
        settings.EnsureCatalog()
        Try
            settings.Save()
        Catch
        End Try
        Return settings
    End Function

    Public Sub Save()
        SyncLock _sync
            SaveXmlUnlocked()
        End SyncLock
    End Sub

    Public Function AddPairedDevice() As String
        Dim token = CreateToken()
        SyncLock _sync
            _pairedTokens.Add(token)
            SaveUnlocked()
        End SyncLock
        Return token
    End Function

    Public Function IsPairedToken(token As String) As Boolean
        If String.IsNullOrEmpty(token) Then Return False
        SyncLock _sync
            Return _pairedTokens.Contains(token)
        End SyncLock
    End Function

    Public ReadOnly Property PairedDeviceCount As Integer
        Get
            SyncLock _sync
                Return _pairedTokens.Count
            End SyncLock
        End Get
    End Property

    Private Sub SaveUnlocked()
        SaveXmlUnlocked()
    End Sub

    Private Sub SaveXmlUnlocked()
        Dim temporaryFile = XmlSettingsFile & ".tmp"
        Dim writerSettings As New XmlWriterSettings With {.Indent = True, .Encoding = New UTF8Encoding(False)}
        Using writer = XmlWriter.Create(temporaryFile, writerSettings)
            writer.WriteStartDocument()
            writer.WriteStartElement("ControleRemotoLAN")
            writer.WriteAttributeString("version", "1")
            writer.WriteStartElement("Server")
            writer.WriteAttributeString("port", Port.ToString())
            writer.WriteAttributeString("screenSharingEnabled", ScreenSharingEnabled.ToString())
            writer.WriteEndElement()
            writer.WriteStartElement("Layout")
            For Each actionId In LayoutOrder
                If Not KeyMap.ContainsKey(actionId) Then Continue For
                writer.WriteStartElement("Action")
                writer.WriteAttributeString("id", actionId)
                writer.WriteAttributeString("keyCode", (CInt(KeyMap(actionId)) And CInt(Keys.KeyCode)).ToString())
                writer.WriteAttributeString("macro", If(MacroMap.ContainsKey(actionId), MacroMap(actionId), String.Empty))
                writer.WriteAttributeString("enabled", If(VisibleMap.ContainsKey(actionId), VisibleMap(actionId), False).ToString())
                writer.WriteAttributeString("name", If(DisplayNameMap.ContainsKey(actionId), DisplayNameMap(actionId), actionId))
                writer.WriteAttributeString("color", If(ColorMap.ContainsKey(actionId), ColorMap(actionId), "#29364D"))
                writer.WriteEndElement()
            Next
            writer.WriteEndElement()
            writer.WriteStartElement("Pairings")
            For Each token In _pairedTokens
                writer.WriteStartElement("Token")
                writer.WriteAttributeString("value", token)
                writer.WriteEndElement()
            Next
            writer.WriteEndElement()
            writer.WriteEndElement()
            writer.WriteEndDocument()
        End Using
        If File.Exists(XmlSettingsFile) Then
            Try
                File.Replace(temporaryFile, XmlSettingsFile, Nothing)
            Catch ex As PlatformNotSupportedException
                File.Copy(temporaryFile, XmlSettingsFile, True)
                File.Delete(temporaryFile)
            Catch ex As IOException
                File.Copy(temporaryFile, XmlSettingsFile, True)
                File.Delete(temporaryFile)
            End Try
        Else
            File.Move(temporaryFile, XmlSettingsFile)
        End If
    End Sub

    Private Shared Sub LoadXmlSettings(settings As AppSettings)
        Dim document As New XmlDocument()
        document.Load(XmlSettingsFile)
        Dim serverNode = document.SelectSingleNode("/ControleRemotoLAN/Server")
        If serverNode IsNot Nothing Then
            Dim parsedPort As Integer
            If Integer.TryParse(GetAttribute(serverNode, "port"), parsedPort) AndAlso parsedPort >= 1024 AndAlso parsedPort <= 65535 Then settings.Port = parsedPort
            Dim parsedScreenSharing As Boolean
            If Boolean.TryParse(GetAttribute(serverNode, "screenSharingEnabled"), parsedScreenSharing) Then settings.ScreenSharingEnabled = parsedScreenSharing
        End If

        settings.LayoutOrder.Clear()
        For Each actionNode As XmlNode In document.SelectNodes("/ControleRemotoLAN/Layout/Action")
            Dim actionId = GetAttribute(actionNode, "id")
            If String.IsNullOrWhiteSpace(actionId) Then Continue For
            Dim parsedKey As Keys
            If [Enum].TryParse(GetAttribute(actionNode, "keyCode"), True, parsedKey) Then settings.KeyMap(actionId) = parsedKey
            settings.MacroMap(actionId) = GetAttribute(actionNode, "macro")
            Dim parsedEnabled As Boolean
            If Boolean.TryParse(GetAttribute(actionNode, "enabled"), parsedEnabled) Then settings.VisibleMap(actionId) = parsedEnabled
            Dim displayName = GetAttribute(actionNode, "name")
            If displayName.Length > 0 Then settings.DisplayNameMap(actionId) = displayName
            Dim color = GetAttribute(actionNode, "color")
            If IsValidColor(color) Then settings.ColorMap(actionId) = color.ToUpperInvariant()
            If Not settings.LayoutOrder.Contains(actionId) Then settings.LayoutOrder.Add(actionId)
        Next

        For Each tokenNode As XmlNode In document.SelectNodes("/ControleRemotoLAN/Pairings/Token")
            Dim token = GetAttribute(tokenNode, "value")
            If token.Length >= 32 Then settings._pairedTokens.Add(token)
        Next
    End Sub

    Private Shared Function GetAttribute(node As XmlNode, attributeName As String) As String
        If node Is Nothing OrElse node.Attributes Is Nothing Then Return String.Empty
        Dim attribute = node.Attributes(attributeName)
        Return If(attribute Is Nothing, String.Empty, attribute.Value)
    End Function

    Private Sub EnsureCatalog()
        For Each definition In KeyboardCatalog.Create()
            If Not KeyMap.ContainsKey(definition.ActionId) Then KeyMap(definition.ActionId) = definition.DefaultKey
            If Not MacroMap.ContainsKey(definition.ActionId) Then MacroMap(definition.ActionId) = String.Empty
            If Not VisibleMap.ContainsKey(definition.ActionId) Then VisibleMap(definition.ActionId) = definition.DefaultVisible
            If Not DisplayNameMap.ContainsKey(definition.ActionId) Then DisplayNameMap(definition.ActionId) = DefaultWebName(definition)
            If Not ColorMap.ContainsKey(definition.ActionId) Then ColorMap(definition.ActionId) = If(definition.IsPrimary, "#2563EB", "#29364D")
            If Not LayoutOrder.Contains(definition.ActionId) Then LayoutOrder.Add(definition.ActionId)
        Next
    End Sub

    Private Shared Function DefaultWebName(definition As RemoteKeyDefinition) As String
        Select Case definition.ActionId
            Case "up"
                Return "▲"
            Case "down"
                Return "▼"
            Case "left"
                Return "◀"
            Case "right"
                Return "▶"
            Case "enter"
                Return "Enter"
            Case "escape"
                Return "Esc"
            Case Else
                Return definition.Label
        End Select
    End Function

    Public Function IsKnownAction(actionId As String) As Boolean
        SyncLock _sync
            Return actionId IsNot Nothing AndAlso KeyMap.ContainsKey(actionId)
        End SyncLock
    End Function

    Public Sub UpdateAction(actionId As String, key As Keys, macro As String, enabled As Boolean, displayName As String, color As String)
        SyncLock _sync
            KeyMap(actionId) = key
            MacroMap(actionId) = If(macro, String.Empty).Trim()
            VisibleMap(actionId) = enabled
            DisplayNameMap(actionId) = If(String.IsNullOrWhiteSpace(displayName), actionId, displayName.Trim())
            ColorMap(actionId) = If(IsValidColor(color), color.ToUpperInvariant(), "#29364D")
        End SyncLock
    End Sub

    Public Function TryGetMappedKey(actionId As String, ByRef key As Keys) As Boolean
        SyncLock _sync
            Return KeyMap.TryGetValue(actionId, key)
        End SyncLock
    End Function

    Public Function TryGetMacro(actionId As String, ByRef macro As String) As Boolean
        SyncLock _sync
            If Not MacroMap.TryGetValue(actionId, macro) Then Return False
            Return Not String.IsNullOrWhiteSpace(macro)
        End SyncLock
    End Function

    Public Function GetLayoutJson() As String
        Dim json As New StringBuilder("[")
        Dim first As Boolean = True
        SyncLock _sync
            Dim definitions = KeyboardCatalog.Create()
            definitions.Sort(Function(left, right) LayoutOrder.IndexOf(left.ActionId).CompareTo(LayoutOrder.IndexOf(right.ActionId)))
            For Each definition In definitions
                Dim visible As Boolean
                If Not VisibleMap.TryGetValue(definition.ActionId, visible) OrElse Not visible Then Continue For
                If Not first Then json.Append(",")
                first = False
                json.Append("{""id"":""").Append(JsonEscape(definition.ActionId)).Append(""",""label"":""")
                Dim displayName = DisplayNameMap(definition.ActionId)
                Dim color = ColorMap(definition.ActionId)
                json.Append(JsonEscape(displayName)).Append(""",""color"":""").Append(JsonEscape(color))
                json.Append(""",""primary"":").Append(If(definition.IsPrimary, "true", "false")).Append("}")
            Next
        End SyncLock
        json.Append("]")
        Return json.ToString()
    End Function

    Public Sub UpdateLayoutOrder(serializedOrder As String)
        If serializedOrder Is Nothing Then Return
        SyncLock _sync
            Dim updated As New List(Of String)()
            For Each actionId In serializedOrder.Split(","c)
                If KeyMap.ContainsKey(actionId) AndAlso Not updated.Contains(actionId) Then updated.Add(actionId)
            Next
            For Each existing In LayoutOrder
                If Not updated.Contains(existing) Then updated.Add(existing)
            Next
            LayoutOrder.Clear()
            LayoutOrder.AddRange(updated)
            SaveUnlocked()
        End SyncLock
    End Sub

    Private Shared Function JsonEscape(value As String) As String
        Return value.Replace("\", "\\").Replace("""", "\""")
    End Function

    Private Shared Function IsValidColor(value As String) As Boolean
        If value Is Nothing OrElse value.Length <> 7 OrElse value(0) <> "#"c Then Return False
        For index = 1 To 6
            If Not Uri.IsHexDigit(value(index)) Then Return False
        Next
        Return True
    End Function

    Private Shared Function CreateToken() As String
        Dim data(31) As Byte
        Using rng = RandomNumberGenerator.Create()
            rng.GetBytes(data)
        End Using
        Dim result As New StringBuilder(64)
        For Each value In data
            result.Append(value.ToString("x2"))
        Next
        Return result.ToString()
    End Function
End Class
