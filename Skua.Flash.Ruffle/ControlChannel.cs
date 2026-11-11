using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Skua.Flash.Ruffle;

/// <summary>
/// The WebSocket link between the page hosting Ruffle and the bot.
/// Calls into the SWF block the calling thread until the page answers, which works from any
/// thread (including the UI thread) because the page lives in a separate WebView/renderer.
/// </summary>
public sealed class ControlChannel : IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string?>> _pending = new();
    private readonly BlockingCollection<(string Name, string ArgsJson)> _events = new();
    private readonly Thread _eventThread;
    private WebSocket? _socket;
    private int _nextId;
    private bool _disposed;

    public ControlChannel()
    {
        _eventThread = new Thread(DispatchEvents) { IsBackground = true, Name = "Ruffle ExternalInterface" };
        _eventThread.Start();
    }

    /// <summary>
    /// Raised (on a dedicated thread, in order) for every ExternalInterface.call made by the SWF.
    /// The second argument is the raw JSON array of arguments.
    /// </summary>
    public event Action<string, string>? ExternalCall;

    public event Action? Connected;
    public event Action? Disconnected;

    /// <summary>
    /// Raised for log lines the page sends (Ruffle panics, bridge errors, etc.).
    /// </summary>
    public event Action<string>? PageLog;

    /// <summary>
    /// Raised when a Flash text field takes focus and the page wants the on-screen keyboard shown.
    /// </summary>
    public event Action? KeyboardRequested;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(15);

    internal async Task RunAsync(WebSocket socket, CancellationToken ct)
    {
        WebSocket? previous = Interlocked.Exchange(ref _socket, socket);
        if (previous is not null)
        {
            FailPending();
            try { previous.Abort(); } catch { }
        }

        Connected?.Invoke();
        byte[] buffer = new byte[64 * 1024];
        using MemoryStream message = new();
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;

                HandleMessage(message.GetBuffer().AsSpan(0, (int)message.Length));
                message.SetLength(0);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _socket, null, socket) == socket)
            {
                FailPending();
                Disconnected?.Invoke();
            }
        }
    }

    private void HandleMessage(ReadOnlySpan<byte> json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json.ToArray());
            JsonElement root = doc.RootElement;
            switch (root.GetProperty("t").GetString())
            {
                case "ret":
                    int id = root.GetProperty("id").GetInt32();
                    if (!_pending.TryRemove(id, out TaskCompletionSource<string?>? tcs))
                        return;
                    if (root.TryGetProperty("e", out JsonElement err) && err.ValueKind == JsonValueKind.String)
                        tcs.TrySetException(new FlashCallException(err.GetString()!));
                    else if (root.TryGetProperty("v", out JsonElement v))
                        tcs.TrySetResult(v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : v.GetRawText());
                    else
                        tcs.TrySetResult(null);
                    break;

                case "ext":
                    string name = root.GetProperty("n").GetString() ?? string.Empty;
                    string args = root.TryGetProperty("a", out JsonElement a) ? a.GetRawText() : "[]";
                    _events.Add((name, args));
                    break;

                case "log":
                    string line = root.TryGetProperty("m", out JsonElement m) ? m.ToString() : string.Empty;
                    Trace.WriteLine($"[Ruffle] {line}");
                    PageLog?.Invoke(line);
                    break;

                case "keyboard":
                    KeyboardRequested?.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Ruffle] Bad control message: {ex.Message}");
        }
    }

    /// <summary>
    /// Calls an ExternalInterface callback registered by the SWF and returns the raw JSON of the
    /// result (null for null/undefined). Returns null without waiting when the page is not connected.
    /// </summary>
    /// <summary>
    /// Set by the Android host: true on the app's main (UI) thread. The page answers calls only between
    /// game frames, and the WebView's frames are driven through that same thread, so a call that waits
    /// there can never be answered: it stalled for the whole timeout (the Options page froze the app,
    /// "MVS isn't responding", 2026-11-12). From that thread a call is sent without waiting (setters still
    /// reach the game) and returns null at once.
    /// </summary>
    public static Func<bool>? IsUiThread { get; set; }

    private static readonly ConcurrentDictionary<string, byte> _uiThreadCallsSeen = new();

    public string? Call(string function, string argsJson, CancellationToken ct = default)
    {
        WebSocket? socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
            return null;

        if (IsUiThread?.Invoke() == true)
        {
            if (_uiThreadCallsSeen.TryAdd(function, 0))
                Trace.WriteLine($"[Ruffle] '{function}' called on the UI thread: sent without waiting for its answer.");
            int noWaitId = Interlocked.Increment(ref _nextId);
            string noWait = $"{{\"t\":\"call\",\"id\":{noWaitId},\"f\":{JsonSerializer.Serialize(function)},\"a\":{argsJson}}}";
            _ = Task.Run(() => { try { Send(socket, noWait); } catch { } });
            return null;
        }

        int id = Interlocked.Increment(ref _nextId);
        TaskCompletionSource<string?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            string payload = $"{{\"t\":\"call\",\"id\":{id},\"f\":{JsonSerializer.Serialize(function)},\"a\":{argsJson}}}";
            Send(socket, payload);
            if (!tcs.Task.Wait(CallTimeout, ct))
                throw new TimeoutException($"Flash call '{function}' timed out.");
            return tcs.Task.Result;
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Makes several read calls in one round trip (the page answers them together, between two game
    /// frames). Returns each call's raw result, or null for the whole batch when the page is not
    /// connected; a call that failed comes back with Ok false.
    /// </summary>
    public (bool Ok, string? Raw)[]? CallBatch(IReadOnlyList<(string Function, string ArgsJson)> calls)
    {
        WebSocket? socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open || calls.Count == 0 || IsUiThread?.Invoke() == true)
            return null;

        int id = Interlocked.Increment(ref _nextId);
        TaskCompletionSource<string?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            StringBuilder sb = new();
            sb.Append("{\"t\":\"batch\",\"id\":").Append(id).Append(",\"c\":[");
            for (int i = 0; i < calls.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append("{\"f\":").Append(JsonSerializer.Serialize(calls[i].Function)).Append(",\"a\":").Append(calls[i].ArgsJson).Append('}');
            }
            sb.Append("]}");
            Send(socket, sb.ToString());
            if (!tcs.Task.Wait(CallTimeout))
                throw new TimeoutException("Flash batch timed out.");
            string? raw = tcs.Task.Result;
            if (raw is null)
                return null;
            using JsonDocument doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            var results = new (bool Ok, string? Raw)[calls.Count];
            int n = 0;
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (n >= results.Length)
                    break;
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("v", out JsonElement v))
                    results[n] = (true, v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : v.GetRawText());
                else
                    results[n] = (false, null);
                n++;
            }
            return results;
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Sends a fire-and-forget command to the page (for example <c>{"t":"reload"}</c>).
    /// </summary>
    public bool Post(string json)
    {
        WebSocket? socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
            return false;
        try
        {
            Send(socket, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Send(WebSocket socket, string payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        _sendLock.Wait();
        try
        {
            socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void FailPending()
    {
        foreach (int id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out TaskCompletionSource<string?>? tcs))
                tcs.TrySetResult(null);
        }
    }

    private void DispatchEvents()
    {
        foreach ((string name, string args) in _events.GetConsumingEnumerable())
        {
            try
            {
                ExternalCall?.Invoke(name, args);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Ruffle] ExternalInterface handler '{name}' threw: {ex}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _events.CompleteAdding();
        FailPending();
        try { _socket?.Abort(); } catch { }
        _sendLock.Dispose();
    }
}

public sealed class FlashCallException : Exception
{
    public FlashCallException(string message) : base(message)
    {
    }
}
