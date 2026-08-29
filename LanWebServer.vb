Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend NotInheritable Class LanWebServer
    Implements IDisposable

    Private Shared ReadOnly HttpNewLine As String = Convert.ToChar(13).ToString() & Convert.ToChar(10).ToString()
    Private ReadOnly _port As Integer
    Private ReadOnly _pin As String
    Private ReadOnly _validateToken As Func(Of String, Boolean)
    Private ReadOnly _createToken As Func(Of String)
    Private ReadOnly _isKnownAction As Func(Of String, Boolean)
    Private ReadOnly _getLayoutJson As Func(Of String)
    Private ReadOnly _isScreenSharingEnabled As Func(Of Boolean)
    Private _listener As TcpListener
    Private _cancel As CancellationTokenSource
    Private _loopTask As Task

    Public Event CommandReceived(command As String, keyState As String)
    Public Event ServerError(message As String)
    Public Event DevicePaired()
    Public Event MouseInputReceived()

    Public Sub New(port As Integer, pin As String, validateToken As Func(Of String, Boolean), createToken As Func(Of String), isKnownAction As Func(Of String, Boolean), getLayoutJson As Func(Of String), isScreenSharingEnabled As Func(Of Boolean))
        _port = port
        _pin = pin
        _validateToken = validateToken
        _createToken = createToken
        _isKnownAction = isKnownAction
        _getLayoutJson = getLayoutJson
        _isScreenSharingEnabled = isScreenSharingEnabled
    End Sub

    Public Sub Start()
        If _listener IsNot Nothing Then Return
        _cancel = New CancellationTokenSource()
        _listener = New TcpListener(IPAddress.Any, _port)
        _listener.Start()
        _loopTask = Task.Run(Function() AcceptLoopAsync(_cancel.Token))
    End Sub

    Public Sub [Stop]()
        If _listener Is Nothing Then Return
        _cancel.Cancel()
        _listener.Stop()
        _listener = Nothing
    End Sub

    Private Async Function AcceptLoopAsync(token As CancellationToken) As Task
        While Not token.IsCancellationRequested
            Try
                Dim client = Await _listener.AcceptTcpClientAsync().ConfigureAwait(False)
                Dim ignored = Task.Run(Function() HandleClientAsync(client))
            Catch ex As ObjectDisposedException
                Exit While
            Catch ex As SocketException
                If Not token.IsCancellationRequested Then RaiseEvent ServerError(ex.Message)
            Catch ex As Exception
                RaiseEvent ServerError(ex.Message)
            End Try
        End While
    End Function

    Private Async Function HandleClientAsync(client As TcpClient) As Task
        Using client
            client.ReceiveTimeout = 5000
            client.SendTimeout = 5000
            Using stream = client.GetStream()
                Dim request = Await ReadRequestAsync(stream).ConfigureAwait(False)
                If request Is Nothing Then Return

                If request.Method = "GET" AndAlso request.Path = "/" Then
                    Await WriteResponseAsync(stream, 200, "text/html; charset=utf-8", BuildPage()).ConfigureAwait(False)
                ElseIf request.Method = "GET" AndAlso request.Path = "/screen" Then
                    If IsAuthenticated(request) Then
                        Await WriteResponseAsync(stream, 200, "text/html; charset=utf-8", BuildScreenPage()).ConfigureAwait(False)
                    Else
                        Await WriteResponseAsync(stream, 401, "text/plain; charset=utf-8", "Pareamento necessário").ConfigureAwait(False)
                    End If
                ElseIf request.Method = "GET" AndAlso request.Path = "/manifest.webmanifest" Then
                    Await WriteResponseAsync(stream, 200, "application/manifest+json; charset=utf-8", ManifestJson).ConfigureAwait(False)
                ElseIf request.Method = "GET" AndAlso request.Path = "/sw.js" Then
                    Await WriteResponseAsync(stream, 200, "application/javascript; charset=utf-8", ServiceWorkerScript).ConfigureAwait(False)
                ElseIf request.Method = "GET" AndAlso (request.Path = "/icon-192.png" OrElse request.Path = "/icon-512.png") Then
                    Dim size = If(request.Path.Contains("512"), 512, 192)
                    Await WriteBinaryResponseAsync(stream, 200, "image/png", BuildIconPng(size)).ConfigureAwait(False)
                ElseIf request.Method = "GET" AndAlso request.Path = "/api/auth" Then
                    If IsAuthenticated(request) Then
                        Await WriteResponseAsync(stream, 204, "text/plain", "").ConfigureAwait(False)
                    Else
                        Await WriteResponseAsync(stream, 401, "text/plain; charset=utf-8", "Pareamento necessário").ConfigureAwait(False)
                    End If
                ElseIf request.Method = "GET" AndAlso request.Path = "/api/layout" Then
                    If IsAuthenticated(request) Then
                        Await WriteResponseAsync(stream, 200, "application/json; charset=utf-8", _getLayoutJson()).ConfigureAwait(False)
                    Else
                        Await WriteResponseAsync(stream, 401, "text/plain; charset=utf-8", "Pareamento necessário").ConfigureAwait(False)
                    End If
                ElseIf request.Method = "GET" AndAlso request.Path = "/api/capabilities" Then
                    If IsAuthenticated(request) Then
                        Await WriteResponseAsync(stream, 200, "application/json; charset=utf-8", "{""screen"":" & If(_isScreenSharingEnabled(), "true", "false") & "}").ConfigureAwait(False)
                    Else
                        Await WriteResponseAsync(stream, 401, "text/plain; charset=utf-8", "Pareamento necessário").ConfigureAwait(False)
                    End If
                ElseIf request.Method = "GET" AndAlso request.Path = "/api/screen" Then
                    If Not IsAuthenticated(request) Then
                        Await WriteResponseAsync(stream, 401, "text/plain; charset=utf-8", "Pareamento necessário").ConfigureAwait(False)
                    ElseIf Not _isScreenSharingEnabled() Then
                        Await WriteResponseAsync(stream, 403, "text/plain; charset=utf-8", "Visualização da tela desabilitada").ConfigureAwait(False)
                    Else
                        Await WriteBinaryResponseAsync(stream, 200, "image/jpeg", ScreenCapture.CaptureJpeg(), "no-store").ConfigureAwait(False)
                    End If
                ElseIf request.Method = "POST" AndAlso request.Path = "/api/mouse" Then
                    If Not IsAuthenticated(request) Then
                        Await WriteResponseAsync(stream, 401, "text/plain; charset=utf-8", "Pareamento necessário").ConfigureAwait(False)
                    Else
                        Dim values = ParseForm(request.Body)
                        Dim action As String = Nothing
                        values.TryGetValue("action", action)
                        HandleMouseAction(action, values)
                        RaiseEvent MouseInputReceived()
                        Await WriteResponseAsync(stream, 204, "text/plain", "").ConfigureAwait(False)
                    End If
                ElseIf request.Method = "POST" AndAlso request.Path = "/api/pair" Then
                    Dim values = ParseForm(request.Body)
                    Dim suppliedPin As String = Nothing
                    values.TryGetValue("pin", suppliedPin)
                    If Not FixedTimeEquals(suppliedPin, _pin) Then
                        Await WriteResponseAsync(stream, 403, "text/plain; charset=utf-8", "PIN inválido").ConfigureAwait(False)
                    Else
                        Dim cookie = "crlan_session=" & _createToken() & "; Path=/; Max-Age=31536000; HttpOnly; SameSite=Strict"
                        Await WriteResponseAsync(stream, 204, "text/plain", "", "Set-Cookie: " & cookie).ConfigureAwait(False)
                        RaiseEvent DevicePaired()
                    End If
                ElseIf request.Method = "POST" AndAlso request.Path = "/api/key" Then
                    Dim values = ParseForm(request.Body)
                    Dim command As String = Nothing
                    Dim keyState As String = "press"
                    values.TryGetValue("key", command)
                    values.TryGetValue("state", keyState)
                    keyState = If(keyState, "press").ToLowerInvariant()

                    If Not IsAuthenticated(request) Then
                        Await WriteResponseAsync(stream, 401, "text/plain; charset=utf-8", "Pareamento necessário").ConfigureAwait(False)
                    ElseIf Not _isKnownAction(command) Then
                        Await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", "Comando inválido").ConfigureAwait(False)
                    ElseIf keyState <> "press" AndAlso keyState <> "down" AndAlso keyState <> "up" Then
                        Await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", "Estado de tecla inválido").ConfigureAwait(False)
                    Else
                        RaiseEvent CommandReceived(command, keyState)
                        Await WriteResponseAsync(stream, 204, "text/plain", "").ConfigureAwait(False)
                    End If
                Else
                    Await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Não encontrado").ConfigureAwait(False)
                End If
            End Using
        End Using
    End Function

    Private Shared Sub HandleMouseAction(action As String, values As Dictionary(Of String, String))
        Select Case If(action, "").ToLowerInvariant()
            Case "move"
                MouseSender.MoveBy(GetLimitedInteger(values, "dx", -300, 300), GetLimitedInteger(values, "dy", -300, 300))
            Case "left"
                MouseSender.LeftClick()
            Case "double"
                MouseSender.DoubleClick()
            Case "right"
                MouseSender.RightClick()
            Case "scroll"
                MouseSender.Scroll(GetLimitedInteger(values, "amount", -10, 10))
            Case Else
                Throw New InvalidOperationException("Ação de mouse inválida.")
        End Select
    End Sub

    Private Shared Function GetLimitedInteger(values As Dictionary(Of String, String), name As String, minimum As Integer, maximum As Integer) As Integer
        Dim textValue As String = Nothing
        Dim result As Integer
        If Not values.TryGetValue(name, textValue) OrElse Not Integer.TryParse(textValue, result) Then Return 0
        Return Math.Max(minimum, Math.Min(maximum, result))
    End Function

    Private NotInheritable Class HttpRequestData
        Public Property Method As String
        Public Property Path As String
        Public Property Body As String
        Public Property Headers As Dictionary(Of String, String)
    End Class

    Private Shared Async Function ReadRequestAsync(stream As NetworkStream) As Task(Of HttpRequestData)
        Dim buffer(8191) As Byte
        Dim received As Integer = 0
        Dim headerEnd As Integer = -1

        While received < buffer.Length
            Dim count = Await stream.ReadAsync(buffer, received, buffer.Length - received).ConfigureAwait(False)
            If count = 0 Then Return Nothing
            received += count
            headerEnd = FindHeaderEnd(buffer, received)
            If headerEnd >= 0 Then Exit While
        End While
        If headerEnd < 0 Then Return Nothing

        Dim headersText = Encoding.ASCII.GetString(buffer, 0, headerEnd)
        Dim lines = headersText.Split(New String() {HttpNewLine}, StringSplitOptions.None)
        Dim first = lines(0).Split(" "c)
        If first.Length < 2 Then Return Nothing

        Dim contentLength As Integer = 0
        Dim requestHeaders As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For i = 1 To lines.Length - 1
            Dim separator = lines(i).IndexOf(":"c)
            If separator > 0 Then
                Dim headerName = lines(i).Substring(0, separator).Trim()
                Dim headerValue = lines(i).Substring(separator + 1).Trim()
                requestHeaders(headerName) = headerValue
                If headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) Then Integer.TryParse(headerValue, contentLength)
            End If
        Next
        If contentLength < 0 OrElse contentLength > 4096 Then Return Nothing

        Dim bodyStart = headerEnd + 4
        Dim bodyBytes As New MemoryStream()
        If received > bodyStart Then bodyBytes.Write(buffer, bodyStart, received - bodyStart)
        While bodyBytes.Length < contentLength
            Dim remaining = CInt(contentLength - bodyBytes.Length)
            Dim temp(Math.Min(remaining, 1024) - 1) As Byte
            Dim count = Await stream.ReadAsync(temp, 0, temp.Length).ConfigureAwait(False)
            If count = 0 Then Exit While
            bodyBytes.Write(temp, 0, count)
        End While

        Return New HttpRequestData With {
            .Method = first(0).ToUpperInvariant(),
            .Path = first(1).Split("?"c)(0),
            .Body = Encoding.UTF8.GetString(bodyBytes.ToArray(), 0, Math.Min(contentLength, CInt(bodyBytes.Length))),
            .Headers = requestHeaders
        }
    End Function

    Private Function IsAuthenticated(request As HttpRequestData) As Boolean
        Dim cookies As String = Nothing
        If request.Headers Is Nothing OrElse Not request.Headers.TryGetValue("Cookie", cookies) Then Return False
        For Each item In cookies.Split(";"c)
            Dim parts = item.Trim().Split(New Char() {"="c}, 2)
            If parts.Length = 2 AndAlso parts(0).Equals("crlan_session", StringComparison.OrdinalIgnoreCase) Then
                Return _validateToken(parts(1))
            End If
        Next
        Return False
    End Function

    Private Shared Function FindHeaderEnd(data() As Byte, length As Integer) As Integer
        For i = 0 To length - 4
            If data(i) = 13 AndAlso data(i + 1) = 10 AndAlso data(i + 2) = 13 AndAlso data(i + 3) = 10 Then Return i
        Next
        Return -1
    End Function

    Private Shared Function ParseForm(body As String) As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each item In body.Split("&"c)
            Dim parts = item.Split(New Char() {"="c}, 2)
            Dim key = Uri.UnescapeDataString(parts(0).Replace("+", " "))
            Dim value = If(parts.Length > 1, Uri.UnescapeDataString(parts(1).Replace("+", " ")), "")
            result(key) = value
        Next
        Return result
    End Function

    Private Shared Function IsAllowedCommand(command As String) As Boolean
        If command Is Nothing Then Return False
        Select Case command.ToLowerInvariant()
            Case "up", "down", "left", "right", "pageup", "pagedown", "escape", "enter"
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function FixedTimeEquals(left As String, right As String) As Boolean
        If left Is Nothing OrElse right Is Nothing Then Return False
        Dim difference As Integer = left.Length Xor right.Length
        Dim count = Math.Max(left.Length, right.Length)
        For i = 0 To count - 1
            Dim a = If(i < left.Length, Convert.ToInt32(left(i)), 0)
            Dim b = If(i < right.Length, Convert.ToInt32(right(i)), 0)
            difference = difference Or (a Xor b)
        Next
        Return difference = 0
    End Function

    Private Shared Async Function WriteResponseAsync(stream As NetworkStream, status As Integer, contentType As String, body As String, Optional extraHeader As String = Nothing) As Task
        Dim payload = Encoding.UTF8.GetBytes(body)
        Dim statusText = If(status = 200, "OK", If(status = 204, "No Content", If(status = 400, "Bad Request", If(status = 401, "Unauthorized", If(status = 403, "Forbidden", "Not Found")))))
        Dim header = "HTTP/1.1 " & status.ToString() & " " & statusText & HttpNewLine &
                     "Content-Type: " & contentType & HttpNewLine &
                     "Content-Length: " & payload.Length.ToString() & HttpNewLine &
                     "Cache-Control: no-store" & HttpNewLine &
                     "X-Content-Type-Options: nosniff" & HttpNewLine &
                     If(String.IsNullOrEmpty(extraHeader), "", extraHeader & HttpNewLine) &
                     "Connection: close" & HttpNewLine & HttpNewLine
        Dim headerBytes = Encoding.ASCII.GetBytes(header)
        Await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(False)
        If payload.Length > 0 Then Await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(False)
    End Function

    Private Shared Async Function WriteBinaryResponseAsync(stream As NetworkStream, status As Integer, contentType As String, payload() As Byte, Optional cacheControl As String = "public, max-age=86400") As Task
        Dim header = "HTTP/1.1 " & status.ToString() & " OK" & HttpNewLine & "Content-Type: " & contentType & HttpNewLine &
                     "Content-Length: " & payload.Length.ToString() & HttpNewLine & "Cache-Control: " & cacheControl & HttpNewLine &
                     "X-Content-Type-Options: nosniff" & HttpNewLine & "Connection: close" & HttpNewLine & HttpNewLine
        Dim headerBytes = Encoding.ASCII.GetBytes(header)
        Await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(False)
        Await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(False)
    End Function

    Private Shared Function BuildIconPng(size As Integer) As Byte()
        Using bitmap As New Bitmap(size, size)
            Using graphics = System.Drawing.Graphics.FromImage(bitmap)
                graphics.SmoothingMode = SmoothingMode.AntiAlias
                graphics.Clear(Color.FromArgb(15, 23, 42))
                Using brush As New LinearGradientBrush(New Rectangle(0, 0, size, size), Color.FromArgb(59, 130, 246), Color.FromArgb(124, 58, 237), 45.0F)
                    graphics.FillEllipse(brush, size * 0.12F, size * 0.12F, size * 0.76F, size * 0.76F)
                End Using
                Using font As New Font("Segoe UI Symbol", size * 0.34F, FontStyle.Bold, GraphicsUnit.Pixel), textBrush As New SolidBrush(Color.White)
                    Dim format As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                    graphics.DrawString("⌁", font, textBrush, New RectangleF(0, 0, size, size), format)
                End Using
            End Using
            Using stream As New MemoryStream()
                bitmap.Save(stream, ImageFormat.Png)
                Return stream.ToArray()
            End Using
        End Using
    End Function

    Private Shared Function BuildPage() As String
        Dim page = PageTemplate.Replace("</style>", FeatureCss & "</style>")
        page = page.Replace("</style>", ButtonImageCss & "</style>")
        page = page.Replace("aria-label='Cima'>▲</button>", "aria-label='Cima' data-label='▲'></button>")
        page = page.Replace("aria-label='Esquerda'>◀</button>", "aria-label='Esquerda' data-label='◀'></button>")
        page = page.Replace("data-key='enter'>OK</button>", "data-key='enter' aria-label='OK' data-label='OK'></button>")
        page = page.Replace("aria-label='Direita'>▶</button>", "aria-label='Direita' data-label='▶'></button>")
        page = page.Replace("aria-label='Baixo'>▼</button>", "aria-label='Baixo' data-label='▼'></button>")
        page = page.Replace("data-key='pageup'>Page Up</button>", "data-key='pageup' aria-label='Page Up' data-label='Page Up'></button>")
        page = page.Replace("data-key='pagedown'>Page Down</button>", "data-key='pagedown' aria-label='Page Down' data-label='Page Down'></button>")
        page = page.Replace("data-key='escape'>Esc</button>", "data-key='escape' aria-label='Esc' data-label='Esc'></button>")
        page = page.Replace("data-key='enter'>Enter</button>", "data-key='enter' aria-label='Enter' data-label='Enter'></button>")
        page = page.Replace("setInterval(()=>send(b.dataset.key),150)", "setInterval(()=>send(b.dataset.key),35)")
        page = page.Replace("function stop(){clearInterval(timer);timer=null;document.querySelectorAll('.btn.on').forEach(x=>x.classList.remove('on'))}", "async function sendState(k,state){try{const r=await fetch('/api/key',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'key='+encodeURIComponent(k)+'&state='+state});if(r.status===401){paired(false);return}if(!r.ok)throw new Error(await r.text())}catch(e){s.textContent=e.message||'Falha na conexão';s.className='status bad'}}const heldKeys=new Map();function keyDown(k){if(heldKeys.has(k))return;const first=sendState(k,'down'),heartbeat=setInterval(()=>sendState(k,'down'),500);heldKeys.set(k,{first:first,heartbeat:heartbeat})}function keyUp(k){const held=heldKeys.get(k);if(!held)return;heldKeys.delete(k);clearInterval(held.heartbeat);held.first.finally(()=>sendState(k,'up'))}function stop(){clearInterval(timer);timer=null;document.querySelectorAll('.btn.on[data-key]').forEach(x=>{keyUp(x.dataset.key);x.classList.remove('on')})}")
        page = page.Replace("send(b.dataset.key);if(b.classList.contains('repeat'))timer=setInterval(()=>send(b.dataset.key),35)", "keyDown(b.dataset.key)")
        page = page.Replace("<div id='controls' class='controls'>", "<div id='controls' class='controls'>" & FeatureToolbar & "<div id='keysPanel' class='feature-panel on'>")
        page = page.Replace("</section></div><div id='status'", "</section><section id='extraKeys' class='key-grid'></section></div>" & FeaturePanels & "</div><div id='status'")
        Dim clientScript = FeatureScript.Replace("organize.onclick=()=>{organizing=!organizing;kp.classList.toggle('organizing',organizing);organize.classList.toggle('on',organizing);box.querySelectorAll('.btn').forEach(b=>b.draggable=organizing);s.textContent=organizing?'Arraste ou toque em dois botões para trocar':'Ordem salva neste aparelho'};", "")
        clientScript = clientScript.Replace("setInterval(()=>send(b.dataset.key),150)", "setInterval(()=>send(b.dataset.key),35)")
        clientScript = clientScript.Replace("send(b.dataset.key);timer=setInterval(()=>send(b.dataset.key),35)", "keyDown(b.dataset.key)")
        clientScript = clientScript.Replace("function saveOrder(){localStorage.setItem('controleLanOrder',JSON.stringify([...box.children].map(b=>b.dataset.key)))}", "function saveOrder(){}")
        clientScript = clientScript.Replace("let items=await(await fetch('/api/layout',{cache:'no-store'})).json(),order=JSON.parse(localStorage.getItem('controleLanOrder')||'[]'),rank=new Map(order.map((id,i)=>[id,i]));items.sort((a,b)=>(rank.has(a.id)?rank.get(a.id):9999)-(rank.has(b.id)?rank.get(b.id):9999));", "let items=await(await fetch('/api/layout',{cache:'no-store'})).json();")
        clientScript = clientScript.Replace("document.querySelectorAll('.pad,.actions').forEach(x=>x.style.display='none');box.innerHTML='';items.forEach(x=>{const b=document.createElement('button');b.className='btn repeat';b.dataset.key=x.id;b.textContent=x.label;b.style.background=x.color;wire(b);box.appendChild(b)})", "const dirIds=new Set(['up','down','left','right']),dirMap=new Map(items.filter(x=>dirIds.has(x.id)).map(x=>[x.id,x]));document.querySelector('.pad').style.display='grid';document.querySelector('.actions').style.display='none';document.querySelectorAll('.pad [data-key]').forEach(b=>{const x=dirMap.get(b.dataset.key);if(x){b.style.visibility='visible';b.textContent=x.label;b.style.background=x.color}else{b.style.visibility='hidden'}});box.innerHTML='';items.filter(x=>!dirIds.has(x.id)).forEach(x=>{const b=document.createElement('button');b.className='btn repeat';b.dataset.key=x.id;b.textContent=x.label;b.style.background=x.color;wire(b);box.appendChild(b)})")
        clientScript = clientScript.Replace("b.textContent=x.label", "b.textContent='';b.dataset.label=x.label;b.setAttribute('aria-label',x.label)")
        clientScript = clientScript.Replace("b.addEventListener('pointerleave',stop);", "b.addEventListener('pointerleave',stop);b.addEventListener('contextmenu',e=>e.preventDefault());")
        clientScript = clientScript.Replace("async function startScreen(){", "const screenStage=document.getElementById('screenStage'),screenView=document.getElementById('screenView'),screenClose=document.getElementById('screenClose');let screenLast=null,screenMoved=0,screenWasExpanded=false;function screenExpanded(){return screenStage.classList.contains('expanded')||document.fullscreenElement===screenStage}function openScreenFull(){screenStage.classList.add('expanded');if(screenStage.requestFullscreen&&!document.fullscreenElement)screenStage.requestFullscreen().catch(()=>{})}function closeScreenFull(){screenStage.classList.remove('expanded');if(document.fullscreenElement===screenStage&&document.exitFullscreen)document.exitFullscreen().catch(()=>{})}screenClose.onclick=e=>{e.stopPropagation();closeScreenFull()};screenView.onpointerdown=e=>{e.preventDefault();screenWasExpanded=screenExpanded();screenLast={x:e.clientX,y:e.clientY};screenMoved=0;screenView.setPointerCapture(e.pointerId)};screenView.onpointermove=e=>{if(!screenLast)return;let dx=Math.round((e.clientX-screenLast.x)*1.6),dy=Math.round((e.clientY-screenLast.y)*1.6);screenLast={x:e.clientX,y:e.clientY};screenMoved+=Math.abs(dx)+Math.abs(dy);if(screenWasExpanded&&(dx||dy))mouse('move','&dx='+dx+'&dy='+dy)};screenView.onpointerup=()=>{if(!screenWasExpanded&&screenMoved<8)openScreenFull();screenLast=null};screenView.onpointercancel=()=>{screenLast=null};screenView.oncontextmenu=e=>e.preventDefault();document.addEventListener('fullscreenchange',()=>{if(document.fullscreenElement!==screenStage)screenStage.classList.remove('expanded')});async function startScreen(){")
        clientScript = clientScript.Replace("function openScreenFull(){screenStage.classList.add('expanded');if(screenStage.requestFullscreen&&!document.fullscreenElement)screenStage.requestFullscreen().catch(()=>{})}", "function openScreenFull(){window.open('/screen','controleLanScreen','popup=yes,fullscreen=yes')}")
        page = page.Replace("check();</script>", clientScript & "check();loadLayout();</script>")
        page = page.Replace("pin.value='';paired(true)", "pin.value='';paired(true);loadLayout()")
        page = page.Replace("</main>", "<footer class='copyright'>© 2026 Wtec Sistemas</footer></main>")
        Return page
    End Function

    Private Shared Function BuildScreenPage() As String
        Const originalPointerUp As String = "img.onpointerup=()=>{last=null};"
        Const pointerUpWithClick As String = "async function clickMouse(){try{await fetch('/api/mouse',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'action=left'})}catch(e){}}img.onpointerup=()=>{if(moved<8)clickMouse();last=null};"
        Return ScreenPage.Replace(originalPointerUp, pointerUpWithClick)
    End Function

    Private Const FeatureCss As String = ".toolbar{display:flex;gap:6px;margin-bottom:14px}.tool{flex:1;border:1px solid #3b82f6;border-radius:10px;background:#172033;color:#bfdbfe;padding:9px 3px;font-weight:650}.tool.on{background:#2563eb;color:#fff}.feature-panel{display:none}.feature-panel.on{display:block}.key-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin-top:18px}.key-grid .btn{font-size:14px;min-height:54px;padding:8px}.organizing .btn{outline:2px dashed #facc15;cursor:grab}.dragging{opacity:.35}.selected-move{outline:3px solid #facc15!important}.touchpad{height:260px;border:1px solid #475569;border-radius:20px;background:linear-gradient(145deg,#1e293b,#0f172a);touch-action:none;display:grid;place-items:center;color:#64748b;user-select:none}.mouse-actions{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin-top:10px}.mouse-actions button{padding:12px 4px;border:0;border-radius:11px;background:#29364d;color:#fff}.screen-stage{position:relative;background:#05080e;border-radius:14px;overflow:hidden}.screen-view{width:100%;background:#05080e;display:block;min-height:160px;object-fit:contain;touch-action:none;user-select:none;-webkit-user-drag:none}.screen-close{display:none;position:absolute;z-index:3;right:max(14px,env(safe-area-inset-right));top:max(14px,env(safe-area-inset-top));width:48px;height:48px;border:1px solid rgba(255,255,255,.35);border-radius:50%;background:rgba(15,23,42,.82);color:#fff;font-size:26px}.screen-stage.expanded,.screen-stage:fullscreen{position:fixed;inset:0;z-index:9999;width:100vw;height:100dvh;border-radius:0;background:#000;display:grid;place-items:center}.screen-stage.expanded .screen-view,.screen-stage:fullscreen .screen-view{width:100%;height:100%;min-height:0;border-radius:0}.screen-stage.expanded .screen-close,.screen-stage:fullscreen .screen-close{display:block}.screen-note{text-align:center;color:#94a3b8;font-size:13px;margin:10px}.copyright{text-align:center;color:#64748b;font-size:12px;margin-top:14px}.btn.on,.btn:active{filter:brightness(1.22)}@media(max-width:360px){.key-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}"
    Private Const ButtonImageCss As String = "button{-webkit-user-select:none!important;user-select:none!important;-webkit-touch-callout:none!important}.btn{color:transparent!important;overflow:hidden}.btn::before{content:attr(data-label);display:grid;place-items:center;width:100%;height:100%;color:#fff;pointer-events:none;white-space:pre-wrap;text-align:center;font:inherit;font-weight:inherit;-webkit-user-select:none;user-select:none}"
    Private Const FeatureToolbar As String = "<nav class='toolbar'><button id='keysTab' class='tool on'>Teclas</button><button id='touchTab' class='tool'>Touchpad</button><button id='screenTab' class='tool'>Tela</button></nav>"
    Private Const FeaturePanels As String = "<div id='touchPanel' class='feature-panel'><div id='touchpad' class='touchpad'>Deslize para mover</div><div class='mouse-actions'><button data-mouse='left'>Clique</button><button data-mouse='double'>Duplo</button><button data-mouse='right'>Direito</button><button data-mouse='scroll'>Rolar</button></div></div><div id='screenPanel' class='feature-panel'><div id='screenStage' class='screen-stage'><img id='screenView' class='screen-view' alt='Tela remota' draggable='false'><button id='screenClose' class='screen-close' type='button' aria-label='Fechar tela cheia'>×</button></div><p id='screenNote' class='screen-note'>Toque na captura para abrir em tela cheia.</p></div>"
    Private Const FeatureScript As String = "let organizing=false,dragged=null,selected=null,screenTimer=null;const box=document.getElementById('extraKeys'),kp=document.getElementById('keysPanel'),tpn=document.getElementById('touchPanel'),sp=document.getElementById('screenPanel');function tab(panel,button){[kp,tpn,sp].forEach(x=>x.classList.toggle('on',x===panel));document.querySelectorAll('.toolbar .tool').forEach(x=>x.classList.remove('on'));button.classList.add('on')}keysTab.onclick=()=>tab(kp,keysTab);touchTab.onclick=()=>tab(tpn,touchTab);screenTab.onclick=()=>{tab(sp,screenTab);startScreen()};organize.onclick=()=>{organizing=!organizing;kp.classList.toggle('organizing',organizing);organize.classList.toggle('on',organizing);box.querySelectorAll('.btn').forEach(b=>b.draggable=organizing);s.textContent=organizing?'Arraste ou toque em dois botões para trocar':'Ordem salva neste aparelho'};function saveOrder(){localStorage.setItem('controleLanOrder',JSON.stringify([...box.children].map(b=>b.dataset.key)))}function wire(b){b.addEventListener('pointerdown',e=>{if(organizing){e.preventDefault();if(!selected){selected=b;b.classList.add('selected-move')}else if(selected!==b){const mark=document.createElement('i');box.insertBefore(mark,selected);box.insertBefore(selected,b);box.insertBefore(b,mark);mark.remove();selected.classList.remove('selected-move');selected=null;saveOrder()}return}e.preventDefault();stop();b.classList.add('on');send(b.dataset.key);timer=setInterval(()=>send(b.dataset.key),150)});b.addEventListener('pointerup',stop);b.addEventListener('pointercancel',stop);b.addEventListener('pointerleave',stop);b.addEventListener('dragstart',()=>{dragged=b;b.classList.add('dragging')});b.addEventListener('dragend',()=>{b.classList.remove('dragging');saveOrder()});b.addEventListener('dragover',e=>e.preventDefault());b.addEventListener('drop',e=>{e.preventDefault();if(dragged&&dragged!==b)box.insertBefore(dragged,b)})}async function loadLayout(){try{let items=await(await fetch('/api/layout',{cache:'no-store'})).json(),order=JSON.parse(localStorage.getItem('controleLanOrder')||'[]'),rank=new Map(order.map((id,i)=>[id,i]));items.sort((a,b)=>(rank.has(a.id)?rank.get(a.id):9999)-(rank.has(b.id)?rank.get(b.id):9999));document.querySelectorAll('.pad,.actions').forEach(x=>x.style.display='none');box.innerHTML='';items.forEach(x=>{const b=document.createElement('button');b.className='btn repeat';b.dataset.key=x.id;b.textContent=x.label;b.style.background=x.color;wire(b);box.appendChild(b)})}catch(e){s.textContent='Falha ao carregar o mapa';s.className='status bad'}}async function mouse(a,d=''){try{await fetch('/api/mouse',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'action='+a+d})}catch(e){}}const touch=document.getElementById('touchpad');let last=null,moved=0;touch.onpointerdown=e=>{last={x:e.clientX,y:e.clientY};moved=0;touch.setPointerCapture(e.pointerId)};touch.onpointermove=e=>{if(!last)return;let dx=Math.round((e.clientX-last.x)*1.6),dy=Math.round((e.clientY-last.y)*1.6);last={x:e.clientX,y:e.clientY};moved+=Math.abs(dx)+Math.abs(dy);if(dx||dy)mouse('move','&dx='+dx+'&dy='+dy)};touch.onpointerup=()=>{if(moved<8)mouse('left');last=null};document.querySelectorAll('[data-mouse]').forEach(b=>b.onclick=()=>mouse(b.dataset.mouse,b.dataset.mouse==='scroll'?'&amount=-3':''));async function startScreen(){clearInterval(screenTimer);let r=await fetch('/api/capabilities'),c=r.ok?await r.json():{screen:false},img=document.getElementById('screenView'),note=document.getElementById('screenNote');if(!c.screen){img.removeAttribute('src');note.textContent='Habilite a visualização da tela no PC.';return}note.textContent='Atualização aproximada: 2 quadros por segundo';const refresh=()=>{if(sp.classList.contains('on'))img.src='/api/screen?t='+Date.now()};refresh();screenTimer=setInterval(refresh,500)};"

    Private Const ScreenPage As String = "<!doctype html><html lang='pt-BR'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1,user-scalable=no,viewport-fit=cover'><meta name='theme-color' content='#000000'><title>Tela remota - Controle LAN</title><style>*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#000;color:#fff;font-family:system-ui,-apple-system,Segoe UI,Arial,sans-serif}.stage{position:fixed;inset:0;display:grid;place-items:center}.screen{width:100%;height:100%;object-fit:contain;touch-action:none;user-select:none;-webkit-user-drag:none}.bar{position:fixed;z-index:2;top:max(12px,env(safe-area-inset-top));right:max(12px,env(safe-area-inset-right));display:flex;gap:9px}.bar button{width:48px;height:48px;border:1px solid rgba(255,255,255,.4);border-radius:50%;background:rgba(15,23,42,.82);color:#fff;font-size:24px}.status{position:fixed;z-index:2;left:50%;top:50%;transform:translate(-50%,-50%);padding:10px 14px;border-radius:10px;background:rgba(0,0,0,.7);color:#cbd5e1;text-align:center}.copyright{position:fixed;z-index:2;left:50%;bottom:max(8px,env(safe-area-inset-bottom));transform:translateX(-50%);color:rgba(255,255,255,.62);font-size:12px;white-space:nowrap}</style></head><body><main id='stage' class='stage'><img id='screen' class='screen' alt='Tela remota' draggable='false'><div class='bar'><button id='full' aria-label='Tela cheia'>⛶</button><button id='close' aria-label='Fechar'>×</button></div><div id='status' class='status'>Conectando...</div><footer class='copyright'>© 2026 Wtec Sistemas</footer></main><script>const img=document.getElementById('screen'),status=document.getElementById('status');let last=null,moved=0,timer=null;async function mouse(dx,dy){try{await fetch('/api/mouse',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'action=move&dx='+dx+'&dy='+dy})}catch(e){}}img.onpointerdown=e=>{e.preventDefault();last={x:e.clientX,y:e.clientY};moved=0;img.setPointerCapture(e.pointerId)};img.onpointermove=e=>{if(!last)return;const dx=Math.round((e.clientX-last.x)*1.6),dy=Math.round((e.clientY-last.y)*1.6);last={x:e.clientX,y:e.clientY};moved+=Math.abs(dx)+Math.abs(dy);if(dx||dy)mouse(dx,dy)};img.onpointerup=()=>{last=null};img.onpointercancel=()=>{last=null};img.oncontextmenu=e=>e.preventDefault();document.getElementById('close').onclick=()=>window.close();document.getElementById('full').onclick=()=>{if(document.fullscreenElement){document.exitFullscreen()}else if(document.documentElement.requestFullscreen){document.documentElement.requestFullscreen().catch(()=>{})}};async function boot(){try{const auth=await fetch('/api/auth',{cache:'no-store'});if(!auth.ok){status.textContent='Pareamento necessário';return}const capability=await fetch('/api/capabilities',{cache:'no-store'}),data=capability.ok?await capability.json():{screen:false};if(!data.screen){status.textContent='Visualização desabilitada no PC';return}const refresh=()=>{img.src='/api/screen?t='+Date.now()};img.onload=()=>{status.style.display='none'};img.onerror=()=>{status.style.display='block';status.textContent='Falha ao atualizar a tela'};refresh();timer=setInterval(refresh,500)}catch(e){status.textContent='Falha na conexão'}}boot();</script></body></html>"

    Private Const ManifestJson As String = "{""name"":""Controle Remoto LAN"",""short_name"":""Controle LAN"",""description"":""Controle remoto de teclas pela rede local"",""start_url"":""/"",""scope"":""/"",""display"":""standalone"",""orientation"":""portrait"",""background_color"":""#0f172a"",""theme_color"":""#2563eb"",""icons"":[{""src"":""/icon-192.png"",""sizes"":""192x192"",""type"":""image/png"",""purpose"":""any maskable""},{""src"":""/icon-512.png"",""sizes"":""512x512"",""type"":""image/png"",""purpose"":""any maskable""}]}"
    Private Const ServiceWorkerScript As String = "const C='controle-lan-v23';self.addEventListener('install',e=>e.waitUntil(caches.open(C).then(c=>c.addAll(['/','/manifest.webmanifest','/icon-192.png','/icon-512.png']))));self.addEventListener('activate',e=>e.waitUntil(caches.keys().then(a=>Promise.all(a.filter(x=>x!==C).map(x=>caches.delete(x))))));self.addEventListener('fetch',e=>{if(e.request.method==='GET'&&!e.request.url.includes('/api/'))e.respondWith(fetch(e.request).then(r=>{const x=r.clone();caches.open(C).then(c=>c.put(e.request,x));return r}).catch(()=>caches.match(e.request)))})"

    Private Const PageTemplate As String = "<!doctype html><html lang='pt-BR'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1,user-scalable=no,viewport-fit=cover'><meta name='theme-color' content='#2563eb'><meta name='mobile-web-app-capable' content='yes'><meta name='apple-mobile-web-app-capable' content='yes'><meta name='apple-mobile-web-app-status-bar-style' content='black-translucent'><meta name='apple-mobile-web-app-title' content='Controle LAN'><link rel='manifest' href='/manifest.webmanifest'><link rel='icon' sizes='192x192' href='/icon-192.png'><link rel='apple-touch-icon' href='/icon-192.png'><title>Controle Remoto LAN</title><style>" &
        ":root{--primary:#3b82f6;--surface:#172033;--surface2:#202b40;--muted:#94a3b8;--success:#4ade80}*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}body{margin:0;min-height:100dvh;background:radial-gradient(circle at top,#1e3a5f 0,#0f172a 48%,#090e1a 100%);color:#f8fafc;font-family:system-ui,-apple-system,Segoe UI,Arial,sans-serif;display:grid;place-items:center;padding:max(12px,env(safe-area-inset-top)) 12px max(12px,env(safe-area-inset-bottom))}.app{width:min(100%,430px);background:rgba(15,23,42,.82);border:1px solid rgba(148,163,184,.18);border-radius:28px;padding:22px;box-shadow:0 24px 70px rgba(0,0,0,.45);backdrop-filter:blur(18px)}.brand{display:flex;align-items:center;justify-content:center;gap:12px}.logo{width:46px;height:46px;border-radius:14px;background:linear-gradient(135deg,#3b82f6,#7c3aed);display:grid;place-items:center;font-size:25px;box-shadow:0 8px 24px rgba(59,130,246,.3)}.title{margin:0;font-size:23px}.sub{text-align:center;color:var(--muted);margin:9px 0 18px}.install{display:none;width:100%;margin:0 0 14px;padding:10px;border:1px solid #3b82f6;border-radius:12px;background:transparent;color:#93c5fd;font-weight:650}.install.show{display:block}.pair{display:none;background:var(--surface);border:1px solid #334155;border-radius:18px;padding:18px;margin-bottom:18px}.pair.show{display:block}.pair input{width:100%;font-size:22px;text-align:center;letter-spacing:5px;padding:13px;border:1px solid #475569;border-radius:13px;color:#fff;background:#0f172a;margin-bottom:10px;outline:none}.pair input:focus{border-color:var(--primary);box-shadow:0 0 0 3px rgba(59,130,246,.2)}.pair button{width:100%;padding:13px;border:0;border-radius:12px;background:linear-gradient(135deg,#3b82f6,#2563eb);color:#fff;font-weight:700}.controls{display:none}.controls.show{display:block}.pad{display:grid;grid-template-columns:repeat(3,1fr);gap:11px}.btn{min-height:76px;border:1px solid rgba(148,163,184,.12);border-radius:18px;background:linear-gradient(145deg,#29364d,#1b2538);color:#fff;font-size:25px;font-weight:750;box-shadow:0 6px 0 #0a1020,0 10px 22px rgba(0,0,0,.22);user-select:none;touch-action:none}.btn:active,.btn.on{transform:translateY(5px);box-shadow:0 1px 0 #0a1020;background:linear-gradient(145deg,#3b82f6,#2563eb)}.blank{visibility:hidden}.actions{display:grid;grid-template-columns:1fr 1fr;gap:11px;margin-top:20px}.actions .btn{font-size:17px;min-height:62px}.status{text-align:center;min-height:24px;margin-top:18px;color:var(--success);font-size:14px}.bad{color:#fb7185}@media(max-height:650px){.app{padding:15px}.btn{min-height:62px}.actions{margin-top:13px}.sub{margin-bottom:12px}}@media(min-width:700px) and (orientation:landscape){.app{max-width:620px}.controls.show{display:grid;grid-template-columns:1.25fr .75fr;gap:18px}.actions{margin-top:0;align-content:center}}</style></head><body><main class='app'><div class='brand'><div class='logo'>⌁</div><h1 class='title'>Controle Remoto</h1></div><p class='sub'>Conectado ao PC pela rede local</p><button id='install' class='install'>Instalar no celular</button><div id='pair' class='pair'><input id='pin' inputmode='numeric' maxlength='6' autocomplete='one-time-code' placeholder='PIN exibido no PC'><button id='pairBtn'>Parear aparelho</button></div><div id='controls' class='controls'><section class='pad'><span class='blank'></span><button class='btn repeat' data-key='up' aria-label='Cima'>▲</button><span class='blank'></span><button class='btn repeat' data-key='left' aria-label='Esquerda'>◀</button><button class='btn' data-key='enter'>OK</button><button class='btn repeat' data-key='right' aria-label='Direita'>▶</button><span class='blank'></span><button class='btn repeat' data-key='down' aria-label='Baixo'>▼</button><span class='blank'></span></section><section class='actions'><button class='btn' data-key='pageup'>Page Up</button><button class='btn' data-key='pagedown'>Page Down</button><button class='btn' data-key='escape'>Esc</button><button class='btn' data-key='enter'>Enter</button></section></div><div id='status' class='status'>Verificando pareamento...</div></main><script>" &
        "const s=document.getElementById('status'),pair=document.getElementById('pair'),controls=document.getElementById('controls'),pin=document.getElementById('pin'),install=document.getElementById('install');let timer=null,busy=false,installPrompt=null;function paired(ok){pair.classList.toggle('show',!ok);controls.classList.toggle('show',ok);s.textContent=ok?'Pareado e pronto':'Digite o PIN para parear';s.className='status'}async function check(){try{paired((await fetch('/api/auth',{cache:'no-store'})).ok)}catch(e){s.textContent='Falha na conexão';s.className='status bad'}}document.getElementById('pairBtn').onclick=async()=>{try{const r=await fetch('/api/pair',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'pin='+encodeURIComponent(pin.value)});if(!r.ok)throw new Error(await r.text());pin.value='';paired(true)}catch(e){s.textContent=e.message||'Falha ao parear';s.className='status bad'}};async function send(k){if(busy)return;busy=true;try{const r=await fetch('/api/key',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'key='+encodeURIComponent(k)});if(r.status===401){paired(false);return}if(!r.ok)throw new Error(await r.text());s.textContent='Enviado: '+k;s.className='status'}catch(e){s.textContent=e.message||'Falha na conexão';s.className='status bad'}finally{busy=false}}function stop(){clearInterval(timer);timer=null;document.querySelectorAll('.btn.on').forEach(x=>x.classList.remove('on'))}document.querySelectorAll('[data-key]').forEach(b=>{const start=e=>{e.preventDefault();stop();b.classList.add('on');send(b.dataset.key);if(b.classList.contains('repeat'))timer=setInterval(()=>send(b.dataset.key),150)};b.addEventListener('pointerdown',start);b.addEventListener('pointerup',stop);b.addEventListener('pointercancel',stop);b.addEventListener('pointerleave',stop);b.addEventListener('contextmenu',e=>e.preventDefault())});addEventListener('blur',stop);addEventListener('beforeinstallprompt',e=>{e.preventDefault();installPrompt=e;install.classList.add('show')});install.onclick=async()=>{if(installPrompt){installPrompt.prompt();await installPrompt.userChoice;installPrompt=null;install.classList.remove('show')}};addEventListener('appinstalled',()=>install.classList.remove('show'));if('serviceWorker'in navigator)navigator.serviceWorker.register('/sw.js').catch(()=>{});check();</script></body></html>"

    Public Sub Dispose() Implements IDisposable.Dispose
        [Stop]()
        If _cancel IsNot Nothing Then _cancel.Dispose()
    End Sub
End Class
