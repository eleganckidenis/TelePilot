using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TelePilot.Infrastructure;

public interface ISettingsStore<TSettings>
    where TSettings : class
{
    Task<TSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonSettingsStore<TSettings> : ISettingsStore<TSettings>, IDisposable
    where TSettings : class
{
    private static readonly Action<ILogger, Exception?> LogSettingsLoadFailure = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(2001, nameof(LogSettingsLoadFailure)),
        "Nie udało się odczytać ustawień. Zostaną użyte wartości domyślne.");

    private readonly string _filePath;
    private readonly Func<TSettings> _defaultFactory;
    private readonly ILogger<JsonSettingsStore<TSettings>> _logger;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public string FilePath => _filePath;

    public JsonSettingsStore(
        string filePath,
        Func<TSettings> defaultFactory,
        ILogger<JsonSettingsStore<TSettings>> logger,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _defaultFactory = defaultFactory ?? throw new ArgumentNullException(nameof(defaultFactory));
        _logger = logger;
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
    }

    public async Task<TSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return _defaultFactory();
            }

            try
            {
                await using var stream = new FileStream(
                    _filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await JsonSerializer.DeserializeAsync<TSettings>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false)
                    ?? _defaultFactory();
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                LogSettingsLoadFailure(_logger, exception);
                return _defaultFactory();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(TSettings settings, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("Plik ustawień nie ma katalogu nadrzędnego.");
            Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 16 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, _serializerOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
