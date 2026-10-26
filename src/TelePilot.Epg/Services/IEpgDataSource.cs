namespace TelePilot.Epg.Services;

public interface IEpgDataSource
{
    string Description { get; }

    ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}

public sealed class FileEpgDataSource : IEpgDataSource
{
    private readonly string _path;

    public FileEpgDataSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = Path.GetFullPath(path);
        Description = Path.GetFileName(_path);
    }

    public string Description { get; }

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            _path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        return ValueTask.FromResult(stream);
    }
}

public sealed class MemoryEpgDataSource : IEpgDataSource
{
    private readonly byte[] _content;

    public MemoryEpgDataSource(ReadOnlySpan<byte> content, string description = "in-memory XMLTV")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        _content = content.ToArray();
        Description = description.Trim();
    }

    public string Description { get; }

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Stream>(new MemoryStream(_content, writable: false));
    }
}

public sealed class HttpEpgDataSource : IEpgDataSource
{
    private readonly HttpClient _httpClient;
    private readonly Uri _uri;

    public HttpEpgDataSource(HttpClient httpClient, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The EPG address must be an absolute HTTP or HTTPS URI.", nameof(uri));
        }

        _httpClient = httpClient;
        _uri = uri;
        // XMLTV paths can contain credentials as well as query parameters.
        // Descriptions flow into status/cache diagnostics, so expose no URL.
        Description = "HTTP XMLTV";
    }

    public string Description { get; }

    public async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await _httpClient.GetAsync(
                _uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            response.EnsureSuccessStatusCode();
            Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseReadStream(content, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private sealed class HttpResponseReadStream : Stream
    {
        private readonly Stream _content;
        private readonly HttpResponseMessage _response;
        private bool _disposed;

        public HttpResponseReadStream(Stream content, HttpResponseMessage response)
        {
            _content = content;
            _response = response;
        }

        public override bool CanRead => !_disposed && _content.CanRead;

        public override bool CanSeek => !_disposed && _content.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _content.Length;

        public override long Position
        {
            get => _content.Position;
            set => _content.Position = value;
        }

        public override void Flush() => _content.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _content.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _content.Read(buffer);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            _content.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _content.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => _content.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _content.Dispose();
                _response.Dispose();
            }

            _disposed = true;
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await _content.DisposeAsync().ConfigureAwait(false);
                _response.Dispose();
                _disposed = true;
            }

            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
