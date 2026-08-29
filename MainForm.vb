Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Security.Cryptography
Imports System.Windows.Forms

Public NotInheritable Class MainForm
    Inherits Form

    Private NotInheritable Class KeyChoice
        Public ReadOnly Property Code As Integer
        Public ReadOnly Property Name As String

        Public Sub New(code As Integer, name As String)
            Me.Code = code
            Me.Name = name
        End Sub
    End Class

    Private ReadOnly lblStatus As New Label(), txtAddress As New TextBox(), lblPin As New Label()
    Private ReadOnly nudPort As New NumericUpDown(), btnStart As New Button(), btnOpen As New Button(), btnNewPin As New Button(), btnSaveMap As New Button()
    Private ReadOnly btnMoveUp As New Button(), btnMoveDown As New Button()
    Private ReadOnly mapGrid As New DataGridView(), logBox As New ListBox(), trayIcon As New NotifyIcon()
    Private ReadOnly chkScreenSharing As New CheckBox()
    Private ReadOnly settings As AppSettings
    Private ReadOnly trayFlashTimer As New Timer With {.Interval = 450}
    Private _iconInactive As Icon
    Private _iconActive As Icon
    Private _iconKeySent As Icon
    Private _server As LanWebServer
    Private _pin As String
    Private _exitRequested As Boolean
    Private _loadingMap As Boolean
    Private _dragRowIndex As Integer = -1
    Private _dragStartPoint As Point

    Private Shared ReadOnly Actions As KeyValuePair(Of String, String)() = {
        New KeyValuePair(Of String, String)("up", "Direcional para cima"), New KeyValuePair(Of String, String)("down", "Direcional para baixo"),
        New KeyValuePair(Of String, String)("left", "Direcional para esquerda"), New KeyValuePair(Of String, String)("right", "Direcional para direita"),
        New KeyValuePair(Of String, String)("pageup", "Page Up"), New KeyValuePair(Of String, String)("pagedown", "Page Down"),
        New KeyValuePair(Of String, String)("escape", "Esc"), New KeyValuePair(Of String, String)("enter", "Enter / OK")}

    Public Sub New()
        settings = AppSettings.LoadSettings()
        Text = "Controle Remoto LAN"
        Try
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        Catch
        End Try
        StartPosition = FormStartPosition.CenterScreen
        MinimumSize = New Size(900, 680)
        ClientSize = New Size(940, 700)
        Font = New Font("Segoe UI", 9.0F)
        BackColor = Color.FromArgb(245, 247, 250)
        BuildInterface()
        ConfigureTray()
        GeneratePin()
        nudPort.Value = settings.Port
        LoadMappingGrid()
        chkScreenSharing.Checked = settings.ScreenSharingEnabled
        UpdateAddress()
    End Sub

    Private Sub BuildInterface()
        Dim title As New Label With {.Text = "Controle Remoto LAN", .Font = New Font("Segoe UI Semibold", 20.0F), .AutoSize = True, .Location = New Point(25, 20)}
        lblStatus.SetBounds(29, 67, 250, 22)
        lblStatus.Text = "● Servidor parado"
        lblStatus.ForeColor = Color.Firebrick
        Dim addressCaption As New Label With {.Text = "Endereço no celular", .AutoSize = True, .Location = New Point(28, 103)}
        txtAddress.SetBounds(28, 125, 465, 25)
        txtAddress.ReadOnly = True
        txtAddress.Font = New Font("Consolas", 10.0F)
        btnOpen.SetBounds(503, 123, 75, 29)
        btnOpen.Text = "Abrir"
        AddHandler btnOpen.Click, AddressOf OpenBrowser
        Dim portCaption As New Label With {.Text = "Porta", .AutoSize = True, .Location = New Point(588, 103)}
        nudPort.SetBounds(588, 125, 100, 25)
        nudPort.Minimum = 1024
        nudPort.Maximum = 65535
        AddHandler nudPort.ValueChanged, Sub() UpdateAddress()
        Dim pinCaption As New Label With {.Text = "PIN para novos aparelhos", .AutoSize = True, .Location = New Point(28, 169)}
        lblPin.Font = New Font("Consolas", 25.0F, FontStyle.Bold)
        lblPin.AutoSize = True
        lblPin.Location = New Point(25, 189)
        lblPin.ForeColor = Color.FromArgb(35, 92, 185)
        btnNewPin.SetBounds(195, 198, 125, 30)
        btnNewPin.Text = "Gerar novo PIN"
        AddHandler btnNewPin.Click, AddressOf NewPinClicked
        btnStart.SetBounds(525, 189, 163, 43)
        btnStart.Text = "Iniciar servidor"
        btnStart.Font = New Font("Segoe UI Semibold", 10.0F)
        btnStart.BackColor = Color.FromArgb(42, 117, 220)
        btnStart.ForeColor = Color.White
        btnStart.FlatStyle = FlatStyle.Flat
        btnStart.FlatAppearance.BorderSize = 0
        AddHandler btnStart.Click, AddressOf ToggleServer
        chkScreenSharing.SetBounds(720, 198, 195, 34)
        chkScreenSharing.Text = "Permitir visualização da tela"
        chkScreenSharing.AutoSize = True
        AddHandler chkScreenSharing.CheckedChanged, AddressOf ScreenSharingChanged

        Dim mapCaption As New Label With {.Text = "Mapa de teclas — macros: Ctrl+J, Ctrl+Shift+S, Alt+F4...", .Font = New Font("Segoe UI Semibold", 10.0F), .AutoSize = True, .Location = New Point(28, 260)}
        mapGrid.SetBounds(28, 286, 884, 230)
        mapGrid.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        mapGrid.AllowUserToAddRows = False
        mapGrid.AllowUserToDeleteRows = False
        mapGrid.AllowUserToResizeRows = False
        mapGrid.RowHeadersVisible = False
        mapGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        mapGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        mapGrid.MultiSelect = False
        mapGrid.AllowDrop = True
        Dim actionColumn As New DataGridViewTextBoxColumn With {.Name = "Action", .HeaderText = "Botão remoto", .ReadOnly = True, .FillWeight = 24}
        Dim keyColumn As New DataGridViewComboBoxColumn With {.Name = "Key", .HeaderText = "Tecla simples", .FlatStyle = FlatStyle.Flat, .FillWeight = 15}
        Dim macroColumn As New DataGridViewComboBoxColumn With {.Name = "Macro", .HeaderText = "Atalho / macro", .FlatStyle = FlatStyle.Flat, .FillWeight = 18}
        Dim visibleColumn As New DataGridViewCheckBoxColumn With {.Name = "Visible", .HeaderText = "Habilitar", .FillWeight = 11}
        Dim nameColumn As New DataGridViewTextBoxColumn With {.Name = "DisplayName", .HeaderText = "Nome no app", .FillWeight = 22}
        Dim colorColumn As New DataGridViewButtonColumn With {.Name = "Color", .HeaderText = "Cor", .FillWeight = 10, .FlatStyle = FlatStyle.Flat, .UseColumnTextForButtonValue = False}
        keyColumn.DisplayMember = "Name"
        keyColumn.ValueMember = "Code"
        keyColumn.DataSource = GetAvailableKeys()
        macroColumn.DataSource = GetMacroPresets()
        mapGrid.Columns.Add(actionColumn)
        mapGrid.Columns.Add(keyColumn)
        mapGrid.Columns.Add(macroColumn)
        mapGrid.Columns.Add(visibleColumn)
        mapGrid.Columns.Add(nameColumn)
        mapGrid.Columns.Add(colorColumn)
        AddHandler mapGrid.DataError, AddressOf MapGridDataError
        AddHandler mapGrid.CellContentClick, AddressOf MapGridCellContentClick
        AddHandler mapGrid.CellValueChanged, AddressOf MapGridValueChanged
        AddHandler mapGrid.CurrentCellDirtyStateChanged, AddressOf MapGridCurrentCellDirtyStateChanged
        AddHandler mapGrid.MouseDown, AddressOf MapGridMouseDown
        AddHandler mapGrid.MouseMove, AddressOf MapGridMouseMove
        AddHandler mapGrid.DragOver, AddressOf MapGridDragOver
        AddHandler mapGrid.DragDrop, AddressOf MapGridDragDrop
        btnMoveUp.SetBounds(512, 524, 100, 32)
        btnMoveUp.Text = "Mover acima"
        AddHandler btnMoveUp.Click, Sub() MoveSelectedRow(-1)
        btnMoveDown.SetBounds(620, 524, 104, 32)
        btnMoveDown.Text = "Mover abaixo"
        AddHandler btnMoveDown.Click, Sub() MoveSelectedRow(1)
        btnSaveMap.SetBounds(732, 524, 180, 32)
        btnSaveMap.Text = "Salvar mapa de teclas"
        AddHandler btnSaveMap.Click, AddressOf SaveMapping
        Dim logCaption As New Label With {.Text = "Atividade", .AutoSize = True, .Location = New Point(28, 570)}
        logBox.SetBounds(28, 594, 884, 75)
        logBox.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        Dim copyrightLabel As New Label With {.Text = "© 2026 Wtec Sistemas", .AutoSize = True, .ForeColor = Color.DimGray, .Location = New Point(772, 676), .Anchor = AnchorStyles.Bottom Or AnchorStyles.Right}
        Controls.AddRange(New Control() {title, lblStatus, addressCaption, txtAddress, btnOpen, portCaption, nudPort, pinCaption, lblPin, btnNewPin, btnStart, chkScreenSharing, mapCaption, mapGrid, btnMoveUp, btnMoveDown, btnSaveMap, logCaption, logBox, copyrightLabel})
        AddHandler FormClosing, AddressOf FormIsClosing
        AddHandler Resize, AddressOf FormResized
    End Sub

    Private Sub ConfigureTray()
        Dim menu As New ContextMenuStrip()
        menu.Items.Add("Abrir", Nothing, Sub() RestoreWindow())
        menu.Items.Add("Iniciar / parar servidor", Nothing, Sub() ToggleServer(Nothing, EventArgs.Empty))
        menu.Items.Add(New ToolStripSeparator())
        menu.Items.Add("Sair", Nothing, AddressOf ExitApplication)
        _iconInactive = TrayIconFactory.Create(TrayState.Inactive)
        _iconActive = TrayIconFactory.Create(TrayState.Active)
        _iconKeySent = TrayIconFactory.Create(TrayState.KeySent)
        trayIcon.Icon = _iconInactive
        trayIcon.Text = "Controle Remoto LAN"
        trayIcon.ContextMenuStrip = menu
        trayIcon.Visible = True
        AddHandler trayIcon.DoubleClick, Sub() RestoreWindow()
        AddHandler trayFlashTimer.Tick, AddressOf TrayFlashTimerTick
    End Sub

    Private Sub LoadMappingGrid()
        _loadingMap = True
        Try
            mapGrid.Rows.Clear()
            Dim definitions = KeyboardCatalog.Create()
            definitions.Sort(Function(left, right) settings.LayoutOrder.IndexOf(left.ActionId).CompareTo(settings.LayoutOrder.IndexOf(right.ActionId)))
            For Each definition In definitions
                Dim keyCode = CInt(settings.KeyMap(definition.ActionId)) And CInt(Keys.KeyCode)
                Dim rowIndex = mapGrid.Rows.Add(definition.Label, keyCode, settings.MacroMap(definition.ActionId), settings.VisibleMap(definition.ActionId), settings.DisplayNameMap(definition.ActionId), settings.ColorMap(definition.ActionId))
                mapGrid.Rows(rowIndex).Tag = definition.ActionId
                ApplyColorToCell(mapGrid.Rows(rowIndex).Cells("Color"), settings.ColorMap(definition.ActionId))
            Next
        Finally
            _loadingMap = False
        End Try
    End Sub

    Private Sub MoveSelectedRow(direction As Integer)
        If mapGrid.SelectedRows.Count = 0 Then Return
        Dim sourceIndex = mapGrid.SelectedRows(0).Index
        Dim targetIndex = sourceIndex + direction
        MoveGridRow(sourceIndex, targetIndex)
    End Sub

    Private Sub MoveGridRow(sourceIndex As Integer, targetIndex As Integer)
        If sourceIndex < 0 OrElse sourceIndex >= mapGrid.Rows.Count Then Return
        targetIndex = Math.Max(0, Math.Min(mapGrid.Rows.Count - 1, targetIndex))
        If sourceIndex = targetIndex Then Return
        mapGrid.EndEdit()
        Dim row = mapGrid.Rows(sourceIndex)
        mapGrid.Rows.RemoveAt(sourceIndex)
        mapGrid.Rows.Insert(targetIndex, row)
        row.Selected = True
        mapGrid.CurrentCell = row.Cells("Action")
        PersistGridOrder()
    End Sub

    Private Sub PersistGridOrder()
        Dim order As New List(Of String)()
        For Each row As DataGridViewRow In mapGrid.Rows
            Dim actionId = TryCast(row.Tag, String)
            If actionId IsNot Nothing Then order.Add(actionId)
        Next
        settings.UpdateLayoutOrder(String.Join(",", order.ToArray()))
        AddLog("Ordem das teclas salva no servidor")
    End Sub

    Private Sub MapGridMouseDown(sender As Object, e As MouseEventArgs)
        _dragStartPoint = e.Location
        _dragRowIndex = mapGrid.HitTest(e.X, e.Y).RowIndex
    End Sub

    Private Sub MapGridMouseMove(sender As Object, e As MouseEventArgs)
        If e.Button <> MouseButtons.Left OrElse _dragRowIndex < 0 Then Return
        Dim dragSize = SystemInformation.DragSize
        Dim dragRectangle As New Rectangle(_dragStartPoint.X - dragSize.Width \ 2, _dragStartPoint.Y - dragSize.Height \ 2, dragSize.Width, dragSize.Height)
        If Not dragRectangle.Contains(e.Location) Then mapGrid.DoDragDrop(_dragRowIndex, DragDropEffects.Move)
    End Sub

    Private Sub MapGridDragOver(sender As Object, e As DragEventArgs)
        e.Effect = If(e.Data.GetDataPresent(GetType(Integer)), DragDropEffects.Move, DragDropEffects.None)
    End Sub

    Private Sub MapGridDragDrop(sender As Object, e As DragEventArgs)
        If Not e.Data.GetDataPresent(GetType(Integer)) Then Return
        Dim clientPoint = mapGrid.PointToClient(New Point(e.X, e.Y))
        Dim targetIndex = mapGrid.HitTest(clientPoint.X, clientPoint.Y).RowIndex
        If targetIndex < 0 Then targetIndex = mapGrid.Rows.Count - 1
        MoveGridRow(CInt(e.Data.GetData(GetType(Integer))), targetIndex)
        _dragRowIndex = -1
    End Sub

    Private Shared Function GetAvailableKeys() As List(Of KeyChoice)
        Dim result As New List(Of KeyChoice)()
        Dim usedValues As New HashSet(Of Integer)()
        For Each key As Keys In [Enum].GetValues(GetType(Keys))
            Dim keyCode = CInt(key) And CInt(Keys.KeyCode)
            If keyCode > 0 AndAlso keyCode <= 255 AndAlso usedValues.Add(keyCode) Then
                Dim normalized = CType(keyCode, Keys)
                result.Add(New KeyChoice(keyCode, KeyboardCatalog.FriendlyName(normalized)))
            End If
        Next
        Return result
    End Function

    Private Function GetMacroPresets() As List(Of String)
        Dim result As New List(Of String) From {String.Empty}
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {String.Empty}
        Dim addPreset As Action(Of String) =
            Sub(value As String)
                If Not String.IsNullOrWhiteSpace(value) AndAlso seen.Add(value) Then result.Add(value)
            End Sub

        For keyCode = CInt(Keys.A) To CInt(Keys.Z)
            Dim letter = CType(keyCode, Keys).ToString()
            addPreset("Ctrl+" & letter)
        Next
        For keyCode = CInt(Keys.A) To CInt(Keys.Z)
            Dim letter = CType(keyCode, Keys).ToString()
            addPreset("Alt+" & letter)
        Next
        For number = 0 To 9
            addPreset(number.ToString())
            addPreset("Ctrl+" & number.ToString())
            addPreset("Alt+" & number.ToString())
        Next

        Dim commonShortcuts As String() = {
            "Ctrl+Shift+Esc", "Ctrl+Shift+T", "Ctrl+Shift+N", "Ctrl+Shift+S", "Ctrl+Shift+Enter",
            "Ctrl+Alt+End", "Alt+Tab", "Alt+F4", "Alt+Enter", "Win+D", "Win+E", "Win+L", "Win+R", "Win+Tab",
            "Shift+Delete", "Ctrl+Home", "Ctrl+End", "Ctrl+PgUp", "Ctrl+PgDn", "Ctrl+Space", "Ctrl+Enter", "Shift+Enter",
            "Ctrl+F4", "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+Back", "Alt+Left", "Alt+Right", "Alt+Home"}
        For Each shortcut In commonShortcuts
            addPreset(shortcut)
        Next
        For functionNumber = 1 To 12
            addPreset("F" & functionNumber.ToString())
        Next
        For Each savedMacro In settings.MacroMap.Values
            addPreset(savedMacro)
        Next
        Return result
    End Function

    Private Sub MapGridDataError(sender As Object, e As DataGridViewDataErrorEventArgs)
        e.ThrowException = False
        If e.RowIndex >= 0 AndAlso e.RowIndex < mapGrid.Rows.Count Then
            Dim action = TryCast(mapGrid.Rows(e.RowIndex).Tag, String)
            Dim mappedKey As Keys
            If action IsNot Nothing AndAlso settings.KeyMap.TryGetValue(action, mappedKey) Then
                mapGrid.Rows(e.RowIndex).Cells("Key").Value = CInt(mappedKey) And CInt(Keys.KeyCode)
            End If
        End If
    End Sub

    Private Sub MapGridCellContentClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 OrElse e.ColumnIndex <> mapGrid.Columns("Color").Index Then Return
        Dim cell = mapGrid.Rows(e.RowIndex).Cells("Color")
        Using dialog As New ColorDialog()
            Try
                dialog.Color = ColorTranslator.FromHtml(Convert.ToString(cell.Value))
            Catch
                dialog.Color = Color.FromArgb(41, 54, 77)
            End Try
            dialog.FullOpen = True
            If dialog.ShowDialog(Me) = DialogResult.OK Then
                Dim htmlColor = String.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B)
                cell.Value = htmlColor
                ApplyColorToCell(cell, htmlColor)
            End If
        End Using
    End Sub

    Private Shared Sub ApplyColorToCell(cell As DataGridViewCell, htmlColor As String)
        Try
            Dim selectedColor = ColorTranslator.FromHtml(htmlColor)
            cell.Style.BackColor = selectedColor
            cell.Style.ForeColor = If(selectedColor.GetBrightness() < 0.55F, Color.White, Color.Black)
            cell.Style.SelectionBackColor = selectedColor
            cell.Style.SelectionForeColor = cell.Style.ForeColor
        Catch
        End Try
    End Sub

    Private Sub SaveMapping(sender As Object, e As EventArgs)
        PersistMapping(True)
    End Sub

    Private Sub ScreenSharingChanged(sender As Object, e As EventArgs)
        settings.ScreenSharingEnabled = chkScreenSharing.Checked
        settings.Save()
        AddLog(If(chkScreenSharing.Checked, "Visualização da tela habilitada", "Visualização da tela desabilitada"))
    End Sub

    Private Function PersistMapping(showConfirmation As Boolean) As Boolean
        Try
            For Each row As DataGridViewRow In mapGrid.Rows
                Dim parsed As Keys
                Dim action = DirectCast(row.Tag, String)
                Dim rawValue = row.Cells("Key").Value
                If TypeOf rawValue Is Integer Then
                    parsed = CType(CInt(rawValue), Keys)
                ElseIf TypeOf rawValue Is Keys Then
                    parsed = CType(rawValue, Keys)
                ElseIf Not [Enum].TryParse(Convert.ToString(rawValue), True, parsed) Then
                    Throw New InvalidOperationException("Tecla inválida para " & row.Cells("Action").Value.ToString())
                End If
                Dim displayName = Convert.ToString(row.Cells("DisplayName").Value)
                Dim colorValue = Convert.ToString(row.Cells("Color").Value)
                Dim macroValue = Convert.ToString(row.Cells("Macro").Value).Trim()
                If macroValue.Length > 0 Then
                    Dim macroKeys As List(Of Keys) = Nothing
                    Dim macroError As String = Nothing
                    If Not KeyboardSender.TryParseShortcut(macroValue, macroKeys, macroError) Then
                        Throw New InvalidOperationException("Macro inválida para " & row.Cells("Action").Value.ToString() & ": " & macroError)
                    End If
                End If
                settings.UpdateAction(action, parsed, macroValue, Convert.ToBoolean(row.Cells("Visible").Value), displayName, colorValue)
            Next
            settings.Port = CInt(nudPort.Value)
            settings.Save()
            If showConfirmation Then AddLog("Mapa de teclas salvo")
            Return True
        Catch ex As Exception
            If showConfirmation Then
                MessageBox.Show("Não foi possível salvar o mapa:" & Environment.NewLine & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Else
                AddLog("Falha ao salvar automaticamente: " & ex.Message)
            End If
            Return False
        End Try
    End Function

    Private Sub MapGridCurrentCellDirtyStateChanged(sender As Object, e As EventArgs)
        If mapGrid.IsCurrentCellDirty Then mapGrid.CommitEdit(DataGridViewDataErrorContexts.Commit)
    End Sub

    Private Sub MapGridValueChanged(sender As Object, e As DataGridViewCellEventArgs)
        If _loadingMap OrElse e.RowIndex < 0 Then Return
        PersistMapping(False)
    End Sub

    Private Sub ToggleServer(sender As Object, e As EventArgs)
        If _server Is Nothing Then
            Try
                SaveMapping(Nothing, EventArgs.Empty)
                _server = New LanWebServer(CInt(nudPort.Value), _pin, AddressOf settings.IsPairedToken, AddressOf settings.AddPairedDevice, AddressOf settings.IsKnownAction, AddressOf settings.GetLayoutJson, Function() settings.ScreenSharingEnabled)
                AddHandler _server.CommandReceived, AddressOf ExecuteCommand
                AddHandler _server.MouseInputReceived, AddressOf IndicateKeySent
                AddHandler _server.ServerError, AddressOf ServerError
                AddHandler _server.DevicePaired, Sub() AddLog("Novo aparelho pareado. Total: " & settings.PairedDeviceCount.ToString())
                _server.Start()
                nudPort.Enabled = False
                btnNewPin.Enabled = False
                btnStart.Text = "Parar servidor"
                btnStart.BackColor = Color.FromArgb(190, 55, 55)
                lblStatus.Text = "● Servidor ativo"
                lblStatus.ForeColor = Color.ForestGreen
                trayIcon.Text = "Controle Remoto LAN - ativo"
                SetTrayState(TrayState.Active)
                AddLog("Servidor iniciado em " & txtAddress.Text)
            Catch ex As Exception
                _server = Nothing
                MessageBox.Show("Não foi possível iniciar o servidor:" & Environment.NewLine & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        Else
            StopServer()
        End If
    End Sub

    Private Sub StopServer()
        If _server Is Nothing Then Return
        KeyboardSender.ReleaseAll()
        _server.Dispose()
        _server = Nothing
        nudPort.Enabled = True
        btnNewPin.Enabled = True
        btnStart.Text = "Iniciar servidor"
        btnStart.BackColor = Color.FromArgb(42, 117, 220)
        lblStatus.Text = "● Servidor parado"
        lblStatus.ForeColor = Color.Firebrick
        trayIcon.Text = "Controle Remoto LAN"
        SetTrayState(TrayState.Inactive)
        AddLog("Servidor parado")
    End Sub

    Private Sub ExecuteCommand(command As String, keyState As String)
        Dim key As Keys
        If Not settings.TryGetMappedKey(command, key) Then Return
        Try
            Dim macroValue As String = Nothing
            Dim keySequence As List(Of Keys)
            If settings.TryGetMacro(command, macroValue) Then
                keySequence = Nothing
                Dim macroError As String = Nothing
                If Not KeyboardSender.TryParseShortcut(macroValue, keySequence, macroError) Then Throw New InvalidOperationException(macroError)
            Else
                keySequence = New List(Of Keys) From {key}
            End If

            Dim stateChanged As Boolean = True
            Select Case keyState
                Case "down"
                    stateChanged = KeyboardSender.HoldCombination(keySequence)
                Case "up"
                    stateChanged = KeyboardSender.ReleaseCombination(keySequence)
                Case Else
                    KeyboardSender.PressCombination(keySequence)
            End Select
            If stateChanged Then
                IndicateKeySent()
                AddLog(command & " → " & If(String.IsNullOrWhiteSpace(macroValue), key.ToString(), macroValue) & " (" & keyState & ")")
            End If
        Catch ex As Exception
            AddLog("Falha ao enviar " & command & ": " & ex.Message)
        End Try
    End Sub

    Private Sub ServerError(message As String)
        AddLog("Erro do servidor: " & message)
    End Sub

    Private Sub SetTrayState(state As TrayState)
        If InvokeRequired Then
            BeginInvoke(New Action(Of TrayState)(AddressOf SetTrayState), state)
            Return
        End If
        Select Case state
            Case TrayState.Active
                trayIcon.Icon = _iconActive
            Case TrayState.KeySent
                trayIcon.Icon = _iconKeySent
            Case Else
                trayIcon.Icon = _iconInactive
        End Select
    End Sub

    Private Sub IndicateKeySent()
        If InvokeRequired Then
            BeginInvoke(New Action(AddressOf IndicateKeySent))
            Return
        End If
        If _server Is Nothing Then Return
        trayFlashTimer.Stop()
        SetTrayState(TrayState.KeySent)
        trayIcon.Text = "Controle Remoto LAN - tecla enviada"
        trayFlashTimer.Start()
    End Sub

    Private Sub TrayFlashTimerTick(sender As Object, e As EventArgs)
        trayFlashTimer.Stop()
        If _server IsNot Nothing Then
            SetTrayState(TrayState.Active)
            trayIcon.Text = "Controle Remoto LAN - ativo"
        Else
            SetTrayState(TrayState.Inactive)
            trayIcon.Text = "Controle Remoto LAN"
        End If
    End Sub

    Private Sub AddLog(message As String)
        If InvokeRequired Then
            BeginInvoke(New Action(Of String)(AddressOf AddLog), message)
            Return
        End If
        logBox.Items.Insert(0, DateTime.Now.ToString("HH:mm:ss") & "  " & message)
        While logBox.Items.Count > 100
            logBox.Items.RemoveAt(logBox.Items.Count - 1)
        End While
    End Sub

    Private Sub NewPinClicked(sender As Object, e As EventArgs)
        GeneratePin()
    End Sub

    Private Sub GeneratePin()
        Dim bytes(3) As Byte
        Using rng = RandomNumberGenerator.Create()
            rng.GetBytes(bytes)
        End Using
        _pin = (BitConverter.ToUInt32(bytes, 0) Mod 1000000UI).ToString("000000")
        lblPin.Text = _pin
    End Sub

    Private Sub UpdateAddress()
        If nudPort.Value > 0 Then
            txtAddress.Text = "http://" & FindLanAddress() & ":" & CInt(nudPort.Value).ToString() & "/"
        End If
    End Sub

    Private Shared Function FindLanAddress() As String
        Dim fallback As String = Nothing
        For Each adapter In NetworkInterface.GetAllNetworkInterfaces()
            If adapter.OperationalStatus <> OperationalStatus.Up OrElse adapter.NetworkInterfaceType = NetworkInterfaceType.Loopback Then Continue For
            For Each info In adapter.GetIPProperties().UnicastAddresses
                If info.Address.AddressFamily <> AddressFamily.InterNetwork OrElse IPAddress.IsLoopback(info.Address) Then Continue For
                If adapter.NetworkInterfaceType = NetworkInterfaceType.Wireless80211 Then Return info.Address.ToString()
                If fallback Is Nothing Then fallback = info.Address.ToString()
            Next
        Next
        Return If(fallback, "127.0.0.1")
    End Function

    Private Sub OpenBrowser(sender As Object, e As EventArgs)
        Try
            Process.Start(txtAddress.Text)
        Catch ex As Exception
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub FormResized(sender As Object, e As EventArgs)
        If WindowState = FormWindowState.Minimized Then
            Hide()
        End If
    End Sub

    Private Sub RestoreWindow()
        Show()
        WindowState = FormWindowState.Normal
        Activate()
    End Sub

    Private Sub ExitApplication(sender As Object, e As EventArgs)
        _exitRequested = True
        Close()
    End Sub

    Private Sub FormIsClosing(sender As Object, e As FormClosingEventArgs)
        If Not _exitRequested AndAlso e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            Hide()
            trayIcon.ShowBalloonTip(1800, "Controle Remoto LAN", "O servidor continua disponível na bandeja do sistema.", ToolTipIcon.Info)
            Return
        End If
        PersistMapping(False)
        StopServer()
        trayFlashTimer.Stop()
        trayFlashTimer.Dispose()
        trayIcon.Visible = False
        trayIcon.Dispose()
        If _iconInactive IsNot Nothing Then _iconInactive.Dispose()
        If _iconActive IsNot Nothing Then _iconActive.Dispose()
        If _iconKeySent IsNot Nothing Then _iconKeySent.Dispose()
    End Sub
End Class
