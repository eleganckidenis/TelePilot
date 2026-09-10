namespace TelePilot.Epg.Parsing;

internal sealed class PrefixReadStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _prefix;
    private int _position;
    private bool _disposed;

    public PrefixReadStream(byte[] prefix, Stream inner)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(inner);

        _prefix = prefix;
        _inner = inner;
    }

    public override bool CanRead => !_disposed && _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The requested range lies outside the buffer.", nameof(count));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int copied = CopyPrefix(buffer);
        if (copied == buffer.Length)
        {
            return copied;
        }

        return copied + _inner.Read(buffer[copied..]);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        int copied = CopyPrefix(buffer.Span);
        if (copied == buffer.Length)
        {
            return copied;
        }

        return copied + await _inner.ReadAsync(buffer[copied..], cancellationToken).ConfigureAwait(false);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        _disposed = true;
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private int CopyPrefix(Span<byte> destination)
    {
        int available = _prefix.Length - _position;
        int count = Math.Min(available, destination.Length);
        if (count > 0)
        {
            _prefix.AsSpan(_position, count).CopyTo(destination);
            _position += count;
        }

        return count;
    }
}
