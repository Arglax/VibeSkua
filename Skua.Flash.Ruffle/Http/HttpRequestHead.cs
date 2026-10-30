namespace Skua.Flash.Ruffle.Http;

internal sealed class HttpRequestHead
{
    public required string Method { get; init; }
    public required string Target { get; init; }
    public required string Version { get; init; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Path
    {
        get
        {
            int q = Target.IndexOf('?');
            return q < 0 ? Target : Target[..q];
        }
    }

    public string Query
    {
        get
        {
            int q = Target.IndexOf('?');
            return q < 0 ? string.Empty : Target[(q + 1)..];
        }
    }

    public string? this[string header] => Headers.TryGetValue(header, out string? v) ? v : null;

    public bool KeepAlive
    {
        get
        {
            string? connection = this["Connection"];
            if (connection is null)
                return Version == "HTTP/1.1";
            return connection.Contains("keep-alive", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsWebSocketUpgrade =>
        (this["Upgrade"]?.Equals("websocket", StringComparison.OrdinalIgnoreCase) ?? false)
        && this["Sec-WebSocket-Key"] is not null;

    public string? QueryValue(string key)
    {
        foreach (string pair in Query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string k = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
            if (!k.Equals(key, StringComparison.Ordinal))
                continue;
            return eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
        }
        return null;
    }

    public static async Task<HttpRequestHead?> ReadAsync(LineBufferedStream stream, CancellationToken ct)
    {
        string? requestLine;
        do
        {
            requestLine = await stream.ReadLineAsync(8192, ct).ConfigureAwait(false);
            if (requestLine is null)
                return null;
        } while (requestLine.Length == 0);

        string[] parts = requestLine.Split(' ');
        if (parts.Length != 3)
            throw new InvalidDataException($"Bad request line: {requestLine}");

        HttpRequestHead head = new() { Method = parts[0], Target = parts[1], Version = parts[2] };
        int count = 0;
        while (true)
        {
            string? line = await stream.ReadLineAsync(16384, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line))
                break;
            if (++count > 200)
                throw new InvalidDataException("Too many headers.");
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            head.Headers[name] = head.Headers.TryGetValue(name, out string? existing) ? existing + ", " + value : value;
        }
        return head;
    }
}
