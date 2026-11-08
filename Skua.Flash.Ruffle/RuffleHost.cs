using Skua.Flash.Ruffle.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Skua.Flash.Ruffle;

/// <summary>
/// Loopback web server that serves the Ruffle page and gives it what a browser sandbox lacks:
/// a CORS-free proxy for game HTTP traffic, WebSocket-to-TCP bridges for the game sockets and
/// the control channel used for ExternalInterface.
/// </summary>
/// <remarks>
/// Everything lives under a random path token so other apps on the device cannot use the
/// proxy or socket bridge without knowing it.
/// </remarks>
public sealed partial class RuffleHost : IDisposable
{
    // "ccid" and "token" are AQW's own: the game's api/char calls (the bank's item list, saving a house
    // room) send the character ID and session token in them, and fail without.
    private static readonly string[] _forwardedRequestHeaders =
    {
        "Accept", "Accept-Language", "Content-Type", "If-None-Match", "If-Modified-Since", "Range", "ccid", "token"
    };

    private static readonly string[] _forwardedResponseHeaders =
    {
        "Cache-Control", "ETag", "Last-Modified", "Expires", "Content-Range", "Accept-Ranges"
    };

    private static readonly (string Host, int Port)[] _fallbackSockets = BuildFallbackSockets();

    private readonly RuffleHostOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, byte[]> _resourceCache = new();
    private readonly HttpClient _http;
    private TcpListener? _listener;
    private List<(string Host, int Port)>? _serverList;

    public RuffleHost(RuffleHostOptions? options = null)
    {
        _options = options ?? new RuffleHostOptions();
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        SocketsHttpHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20)
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
    }

    public ControlChannel Control { get; } = new();

    public RuffleHostOptions Options => _options;

    public string Token { get; }

    public int Port { get; private set; }

    public bool IsRunning => _listener is not null;

    /// <summary>
    /// URL of the page that hosts the Ruffle player.
    /// </summary>
    public string PageUrl => $"http://127.0.0.1:{Port}/{Token}/";

    public void Start()
    {
        if (_listener is not null)
            return;

        TcpListener listener = new(IPAddress.Loopback, _options.Port);
        try
        {
            listener.Start(64);
        }
        catch (SocketException ex) when (_options.Port != 0)
        {
            Trace.WriteLine($"[RuffleHost] port {_options.Port} unavailable ({ex.SocketErrorCode}), using a free port");
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(64);
        }
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(() => AcceptLoop(listener, _cts.Token));
        _ = Task.Run(RefreshServerListAsync);
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                Trace.WriteLine($"[RuffleHost] accept failed: {ex.Message}");
                continue;
            }

            _ = Task.Run(() => HandleClient(client, ct));
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        bool upgraded = false;
        LineBufferedStream stream = new(client.GetStream());
        try
        {
            while (!ct.IsCancellationRequested)
            {
                HttpRequestHead? head = await HttpRequestHead.ReadAsync(stream, ct).ConfigureAwait(false);
                if (head is null)
                    return;

                byte[] body = Array.Empty<byte>();
                if (int.TryParse(head["Content-Length"], out int length) && length > 0)
                    body = await stream.ReadExactlyAsync(length, ct).ConfigureAwait(false);

                if (head.IsWebSocketUpgrade)
                {
                    upgraded = true;
                    await HandleWebSocket(head, stream, ct).ConfigureAwait(false);
                    return;
                }

                await Route(head, body, stream, ct).ConfigureAwait(false);
                if (!head.KeepAlive)
                    return;
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or EndOfStreamException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[RuffleHost] connection error: {ex}");
        }
        finally
        {
            if (!upgraded)
                stream.Dispose();
            client.Dispose();
        }
    }

    private bool TryStripToken(string path, out string rest)
    {
        string prefix = "/" + Token + "/";
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            rest = path[prefix.Length..];
            return true;
        }
        rest = string.Empty;
        return false;
    }

    private async Task Route(HttpRequestHead head, byte[] body, Stream stream, CancellationToken ct)
    {
        if (head.Method == "OPTIONS")
        {
            await WriteResponse(stream, 204, "No Content", null, Array.Empty<byte>(), head.KeepAlive, ct).ConfigureAwait(false);
            return;
        }

        if (!TryStripToken(head.Path, out string path))
        {
            await WriteText(stream, 404, "Not Found", "text/plain", "not found", head.KeepAlive, ct).ConfigureAwait(false);
            return;
        }

        if (path.StartsWith("p/", StringComparison.Ordinal))
        {
            await Proxy(head, path[2..], body, stream, ct).ConfigureAwait(false);
            return;
        }

        switch (path)
        {
            case "":
            case "index.html":
                await WriteResource(stream, "web/index.html", head.KeepAlive, ct).ConfigureAwait(false);
                return;

            case "config.json":
                await WriteText(stream, 200, "OK", "application/json", BuildConfigJson(), head.KeepAlive, ct).ConfigureAwait(false);
                return;

            case "skua.swf":
                byte[] swf = _options.SwfProvider?.Invoke() ?? GetResource("web/skua.swf")!;
                await WriteResponse(stream, 200, "OK", "application/x-shockwave-flash", swf, head.KeepAlive, ct).ConfigureAwait(false);
                return;
        }

        if (!path.Contains("..", StringComparison.Ordinal) && GetResource("web/" + path) is not null)
        {
            await WriteResource(stream, "web/" + path, head.KeepAlive, ct).ConfigureAwait(false);
            return;
        }

        await WriteText(stream, 404, "Not Found", "text/plain", "not found", head.KeepAlive, ct).ConfigureAwait(false);
    }

    #region Proxy

    private async Task Proxy(HttpRequestHead head, string target, byte[] body, Stream stream, CancellationToken ct)
    {
        // target = "{scheme}/{host[:port]}/{path}"
        int firstSlash = target.IndexOf('/');
        int secondSlash = firstSlash < 0 ? -1 : target.IndexOf('/', firstSlash + 1);
        string scheme = firstSlash < 0 ? string.Empty : target[..firstSlash];
        if ((scheme != "http" && scheme != "https") || firstSlash < 0)
        {
            await WriteText(stream, 400, "Bad Request", "text/plain", "bad proxy target", head.KeepAlive, ct).ConfigureAwait(false);
            return;
        }

        string host = secondSlash < 0 ? target[(firstSlash + 1)..] : target[(firstSlash + 1)..secondSlash];
        string rest = secondSlash < 0 ? "/" : target[secondSlash..];
        string query = head.Query.Length > 0 ? "?" + head.Query : string.Empty;
        if (!Uri.TryCreate($"{scheme}://{host}{rest}{query}", UriKind.Absolute, out Uri? uri))
        {
            await WriteText(stream, 400, "Bad Request", "text/plain", "bad proxy url", head.KeepAlive, ct).ConfigureAwait(false);
            return;
        }

        using HttpRequestMessage request = new(new HttpMethod(head.Method), uri);
        if (body.Length > 0 || head.Method is "POST" or "PUT" or "PATCH")
            request.Content = new ByteArrayContent(body);

        foreach (string name in _forwardedRequestHeaders)
        {
            string? value = head[name];
            if (value is null)
                continue;
            if (name == "Content-Type")
            {
                if (request.Content is not null && MediaTypeHeaderValue.TryParse(value, out MediaTypeHeaderValue? mt))
                    request.Content.Headers.ContentType = mt;
                continue;
            }
            request.Headers.TryAddWithoutValidation(name, value);
        }

        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://game.aq.com/game/");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "ShockwaveFlash/32.0.0.465");

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            byte[] content = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            List<KeyValuePair<string, string>> headers = new();
            foreach (string name in _forwardedResponseHeaders)
            {
                if (response.Headers.TryGetValues(name, out IEnumerable<string>? values) || response.Content.Headers.TryGetValues(name, out values))
                    headers.Add(new(name, string.Join(", ", values)));
            }

            string contentType = response.Content.Headers.ContentType?.ToString() ?? GuessContentType(uri.AbsolutePath);
            await WriteResponse(stream, (int)response.StatusCode, response.ReasonPhrase ?? "OK", contentType, content, head.KeepAlive, ct, headers).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Trace.WriteLine($"[RuffleHost] proxy {uri} failed: {ex.Message}");
            await WriteText(stream, 502, "Bad Gateway", "text/plain", ex.Message, head.KeepAlive, ct).ConfigureAwait(false);
        }
    }

    #endregion

    #region WebSockets

    private async Task HandleWebSocket(HttpRequestHead head, LineBufferedStream stream, CancellationToken ct)
    {
        if (!TryStripToken(head.Path, out string path) || (path != "ws/ctl" && path != "ws/sock"))
        {
            await WriteText(stream, 404, "Not Found", "text/plain", "not found", false, ct).ConfigureAwait(false);
            return;
        }

        string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(head["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        StringBuilder response = new StringBuilder()
            .Append("HTTP/1.1 101 Switching Protocols\r\n")
            .Append("Upgrade: websocket\r\n")
            .Append("Connection: Upgrade\r\n")
            .Append("Sec-WebSocket-Accept: ").Append(accept).Append("\r\n");
        string? protocol = head["Sec-WebSocket-Protocol"]?.Split(',')[0].Trim();
        if (!string.IsNullOrEmpty(protocol))
            response.Append("Sec-WebSocket-Protocol: ").Append(protocol).Append("\r\n");
        response.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response.ToString()), ct).ConfigureAwait(false);

        using WebSocket socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
        {
            IsServer = true,
            SubProtocol = string.IsNullOrEmpty(protocol) ? null : protocol,
            KeepAliveInterval = TimeSpan.FromSeconds(20)
        });

        if (path == "ws/ctl")
        {
            await Control.RunAsync(socket, ct).ConfigureAwait(false);
            return;
        }

        string? host = head.QueryValue("h");
        if (string.IsNullOrEmpty(host) || !int.TryParse(head.QueryValue("p"), out int port))
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "missing target", ct).ConfigureAwait(false);
            return;
        }

        await BridgeSocket(socket, host, port, ct).ConfigureAwait(false);
    }

    private static async Task BridgeSocket(WebSocket socket, string host, int port, CancellationToken ct)
    {
        using TcpClient tcp = new() { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[RuffleHost] socket connect {host}:{port} failed: {ex.Message}");
            try { await socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "connect failed", ct).ConfigureAwait(false); } catch { }
            return;
        }

        Trace.WriteLine($"[RuffleHost] socket bridged to {host}:{port}");
        NetworkStream net = tcp.GetStream();
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        Task upstream = Task.Run(async () =>
        {
            byte[] buffer = new byte[32 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, linked.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                await net.WriteAsync(buffer.AsMemory(0, result.Count), linked.Token).ConfigureAwait(false);
            }
        }, linked.Token);

        Task downstream = Task.Run(async () =>
        {
            byte[] buffer = new byte[32 * 1024];
            int read;
            while ((read = await net.ReadAsync(buffer, linked.Token).ConfigureAwait(false)) > 0)
                await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, true, linked.Token).ConfigureAwait(false);
        }, linked.Token);

        try
        {
            await Task.WhenAny(upstream, downstream).ConfigureAwait(false);
        }
        finally
        {
            linked.Cancel();
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            Trace.WriteLine($"[RuffleHost] socket {host}:{port} closed");
        }
    }

    #endregion

    #region Config

    private async Task RefreshServerListAsync()
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, _options.ServerListUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
            using HttpResponseMessage response = await _http.SendAsync(request, _cts.Token).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            List<(string, int)> list = new();
            foreach (JsonElement server in doc.RootElement.EnumerateArray())
            {
                if (server.TryGetProperty("sIP", out JsonElement ip) && server.TryGetProperty("iPort", out JsonElement port) && port.TryGetInt32(out int p))
                    list.Add((ip.GetString() ?? string.Empty, p));
            }
            _serverList = list;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[RuffleHost] server list fetch failed: {ex.Message}");
        }
    }

    private static (string Host, int Port)[] BuildFallbackSockets()
    {
        List<(string, int)> list = new();
        for (int i = 1; i <= 20; i++)
        {
            for (int port = 5588; port <= 5600; port++)
                list.Add(($"sock{i}.aq.com", port));
        }
        return list.ToArray();
    }

    private string BuildConfigJson()
    {
        if (_serverList is null)
            RefreshServerListAsync().Wait(TimeSpan.FromSeconds(8));

        HashSet<(string, int)> sockets = new(_fallbackSockets);
        if (_serverList is not null)
            sockets.UnionWith(_serverList);

        using MemoryStream ms = new();
        using (Utf8JsonWriter w = new(ms))
        {
            w.WriteStartObject();
            w.WriteString("quality", _options.Quality);
            w.WriteNumber("frameRate", _options.FrameRate);
            w.WriteNumber("maxPixelRatio", _options.MaxPixelRatio);
            w.WriteString("preferredRenderer", _options.PreferredRenderer);
            w.WriteNumber("touchRowH", _options.TouchListRowHeight);
            w.WriteStartArray("socketProxy");
            foreach ((string host, int port) in sockets)
            {
                w.WriteStartObject();
                w.WriteString("host", host);
                w.WriteNumber("port", port);
                w.WriteString("proxyUrl", $"ws://127.0.0.1:{Port}/{Token}/ws/sock?h={Uri.EscapeDataString(host)}&p={port}");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    #endregion

    #region Resources and responses

    private byte[]? GetResource(string name)
    {
        if (_resourceCache.TryGetValue(name, out byte[]? cached))
            return cached;

        // RecursiveDir puts '\' into the logical name when built on Windows.
        Assembly assembly = typeof(RuffleHost).Assembly;
        string? actual = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == name);
        using Stream? s = actual is null ? null : assembly.GetManifestResourceStream(actual);
        if (s is null)
            return null;
        using MemoryStream ms = new();
        s.CopyTo(ms);
        byte[] data = ms.ToArray();
        if (name == "web/ruffle/ruffle.js")
            data = PatchRuffle(data);
        _resourceCache[name] = data;
        return data;
    }

    /// <summary>
    /// Ruffle evaluates ExternalInterface.call names as JavaScript ("(name)(...args)"), which breaks
    /// names such as "pre-load". Route every call through the bridge instead. The on-screen keyboard
    /// calls go to the bridge too, which keeps Ruffle's hidden text input focused on touch screens.
    /// </summary>
    private static byte[] PatchRuffle(byte[] data)
    {
        string js = Encoding.UTF8.GetString(data);
        string patched = ExternalCallRegex().Replace(js, m =>
            $"return window.__skuaExt?window.__skuaExt({m.Groups[1].Value},{m.Groups[2].Value}):new Function(`return (${{{m.Groups[1].Value}}})(...arguments);`)(...{m.Groups[2].Value})");
        if (ReferenceEquals(patched, js) || patched == js)
            Trace.WriteLine("[RuffleHost] WARNING: ExternalInterface patch did not apply; names that are not JS identifiers will fail.");

        foreach (string method in new[] { "openVirtualKeyboard", "closeVirtualKeyboard" })
        {
            string definition = method + "(){";
            if (!patched.Contains(definition, StringComparison.Ordinal))
                Trace.WriteLine($"[RuffleHost] WARNING: {method} patch did not apply; typing into game text fields may not work.");
            patched = patched.Replace(definition, $"{definition}if(window.__skuaKeyboard)return window.__skuaKeyboard.{method}(this);", StringComparison.Ordinal);
        }

        // MVS keeps the game page "visible" while the app is in the background (a hidden page is
        // suspended by Chromium after a few minutes: the game and the bot stop). The app says when it
        // is out of sight (window.__mvsBackground, then a visibilitychange event), and Ruffle then
        // ticks from its background worker as it would for a hidden tab: screen refreshes, which
        // drive the normal loop, stop when the screen is off.
        const string hiddenCheck = "document.hidden?(this.lastActivePlayingState";
        if (patched.Contains(hiddenCheck, StringComparison.Ordinal))
            patched = patched.Replace(hiddenCheck, "(document.hidden||window.__mvsBackground)?(this.lastActivePlayingState", StringComparison.Ordinal);
        else
            Trace.WriteLine("[RuffleHost] WARNING: background tick patch did not apply; the game may stop in the background.");

        // The background worker waited a whole frame after each tick finished (about 22 game frames a
        // second instead of 30); keep to the frame rate by waiting only for what is left of the frame.
        const string workerWait = "self.onmessage = () => {\n                setTimeout(() => self.postMessage(\"tick\"), intervalMs);\n            };";
        if (patched.Contains(workerWait, StringComparison.Ordinal))
            patched = patched.Replace(workerWait, "let next = Date.now() + intervalMs;\n            self.onmessage = () => {\n                next = Math.max(next + intervalMs, Date.now());\n                setTimeout(() => self.postMessage(\"tick\"), Math.max(0, next - Date.now()));\n            };", StringComparison.Ordinal);
        else
            Trace.WriteLine("[RuffleHost] WARNING: background tick pacing patch did not apply.");
        return Encoding.UTF8.GetBytes(patched);
    }

    [GeneratedRegex(@"return new Function\(`return \(\$\{(\w+)\}\)\(\.\.\.arguments\);`\)\(\.\.\.(\w+)\)")]
    private static partial Regex ExternalCallRegex();

    private async Task WriteResource(Stream stream, string name, bool keepAlive, CancellationToken ct)
    {
        byte[]? data = GetResource(name);
        if (data is null)
        {
            await WriteText(stream, 404, "Not Found", "text/plain", "not found", keepAlive, ct).ConfigureAwait(false);
            return;
        }
        List<KeyValuePair<string, string>> headers = new() { new("Cache-Control", name.EndsWith(".wasm") ? "max-age=31536000, immutable" : "no-cache") };
        await WriteResponse(stream, 200, "OK", GuessContentType(name), data, keepAlive, ct, headers).ConfigureAwait(false);
    }

    private static string GuessContentType(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            ".swf" => "application/x-shockwave-flash",
            ".xml" => "text/xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".mp3" => "audio/mpeg",
            _ => "application/octet-stream"
        };
    }

    private static Task WriteText(Stream stream, int status, string reason, string contentType, string text, bool keepAlive, CancellationToken ct)
        => WriteResponse(stream, status, reason, contentType, Encoding.UTF8.GetBytes(text), keepAlive, ct);

    private static async Task WriteResponse(Stream stream, int status, string reason, string? contentType, byte[] body, bool keepAlive, CancellationToken ct, IEnumerable<KeyValuePair<string, string>>? extraHeaders = null)
    {
        StringBuilder sb = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Headers: *\r\n")
            .Append("Access-Control-Allow-Methods: GET, POST, PUT, OPTIONS\r\n")
            .Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n");
        if (contentType is not null)
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        if (extraHeaders is not null)
        {
            foreach (KeyValuePair<string, string> h in extraHeaders)
                sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
        }
        sb.Append("\r\n");

        await stream.WriteAsync(Encoding.Latin1.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    #endregion

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        Control.Dispose();
        _http.Dispose();
        _cts.Dispose();
    }
}
