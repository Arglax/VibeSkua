using System.Text;

namespace Skua.Flash.Ruffle.Http;

/// <summary>
/// Read-buffered wrapper around a network stream that can read CRLF-terminated lines and
/// then hand the remaining bytes to a body reader or a WebSocket.
/// </summary>
internal sealed class LineBufferedStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    public LineBufferedStream(Stream inner)
    {
        _inner = inner;
    }

    public async Task<string?> ReadLineAsync(int maxLength, CancellationToken ct)
    {
        StringBuilder? sb = null;
        while (true)
        {
            for (int i = _start; i < _end; i++)
            {
                if (_buffer[i] != (byte)'\n')
                    continue;

                int len = i - _start;
                if (len > 0 && _buffer[i - 1] == (byte)'\r')
                    len--;
                string part = Encoding.Latin1.GetString(_buffer, _start, len);
                _start = i + 1;
                return sb is null ? part : sb.Append(part).ToString();
            }

            sb ??= new StringBuilder();
            sb.Append(Encoding.Latin1.GetString(_buffer, _start, _end - _start));
            if (sb.Length > maxLength)
                throw new InvalidDataException("Header line too long.");
            _start = _end = 0;

            int read = await _inner.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                return sb.Length == 0 ? null : sb.ToString();
            _end = read;
        }
    }

    public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
    {
        byte[] result = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await ReadAsync(result.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
        return result;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (_start < _end)
        {
            int n = Math.Min(destination.Length, _end - _start);
            _buffer.AsMemory(_start, n).CopyTo(destination);
            _start += n;
            return n;
        }
        return await _inner.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_start < _end)
        {
            int n = Math.Min(count, _end - _start);
            Buffer.BlockCopy(_buffer, _start, buffer, offset, n);
            _start += n;
            return n;
        }
        return _inner.Read(buffer, offset, count);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        => _inner.WriteAsync(source, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override void Flush() => _inner.Flush();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
