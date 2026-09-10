using Microsoft.Extensions.Logging;
using TelePilot.App.Models;
using TelePilot.Core.Models;
using TelePilot.Recording;

namespace TelePilot.App.Services;

public interface IRecordingCoordinator : IAsyncDisposable
{
    event EventHandler<RecordingProgress>? ProgressChanged;

    bool IsRecording { get; }

    Task<string> StartAsync(
        Channel channel,
        string? programmeTitle,
        RecordingMode mode,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<ScheduledRecordingEntry> ScheduleAsync(
        Channel channel,
        RecordingScheduleRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduledRecordingEntry>> GetScheduledAsync(
        CancellationToken cancellationToken = default);

    Task CancelScheduledAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class RecordingCoordinator : IRecordingCoordinator
{
    private static readonly Action<ILogger, Exception?> LogRecordingShutdownFailure = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(3101, nameof(LogRecordingShutdownFailure)),
        "Nie udało się poprawnie zamknąć aktywnego nagrywania.");

    private readonly IFfmpegLocator ffmpegLocator;
    private readonly FfmpegRecordingService recordingService;
    private readonly IAppPreferencesService preferencesService;
    private readonly IScheduledRecordingStore scheduledStore;
    private readonly ICurrentUserDataProtector dataProtector;
    private readonly IRecordingTaskScheduler taskScheduler;
    private readonly ILogger<RecordingCoordinator> logger;
    private bool disposed;

    public RecordingCoordinator(
        IFfmpegLocator ffmpegLocator,
        FfmpegRecordingService recordingService,
        IAppPreferencesService preferencesService,
        IScheduledRecordingStore scheduledStore,
        ICurrentUserDataProtector dataProtector,
        IRecordingTaskScheduler taskScheduler,
        ILogger<RecordingCoordinator> logger)
    {
        this.ffmpegLocator = ffmpegLocator ?? throw new ArgumentNullException(nameof(ffmpegLocator));
        this.recordingService = recordingService ?? throw new ArgumentNullException(nameof(recordingService));
        this.preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        this.scheduledStore = scheduledStore ?? throw new ArgumentNullException(nameof(scheduledStore));
        this.dataProtector = dataProtector ?? throw new ArgumentNullException(nameof(dataProtector));
        this.taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        recordingService.ProgressChanged += OnProgressChanged;
    }

    public event EventHandler<RecordingProgress>? ProgressChanged;

    public bool IsRecording => recordingService.IsRecording;

    public async Task<string> StartAsync(
        Channel channel,
        string? programmeTitle,
        RecordingMode mode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(channel);
        if (!Uri.TryCreate(channel.StreamUrl, UriKind.Absolute, out var streamUri))
        {
            throw new InvalidOperationException("Adres strumienia wybranego kanału jest nieprawidłowy.");
        }

        var preferences = preferencesService.Current.Recording;
        var ffmpeg = await ffmpegLocator.FindAsync(
            preferences.FfmpegPath,
            AppContext.BaseDirectory,
            cancellationToken).ConfigureAwait(false);
        if (ffmpeg is null)
        {
            throw new InvalidOperationException(
                "Nie znaleziono FFmpeg. Zainstaluj ponownie TelePilot albo wskaż ffmpeg.exe w Ustawieniach.");
        }

        var request = CreateRequest(channel, streamUri, programmeTitle, mode, preferences, DateTimeOffset.Now);
        return await recordingService.StartAsync(ffmpeg.ExecutablePath, request, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return recordingService.StopAsync(cancellationToken);
    }

    public async Task<ScheduledRecordingEntry> ScheduleAsync(
        Channel channel,
        RecordingScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Planowanie nagrań przez Harmonogram zadań jest dostępne tylko w Windows.");
        }

        if (request.End <= request.Start)
        {
            throw new ArgumentException("Czas zakończenia nagrania musi być późniejszy niż czas rozpoczęcia.", nameof(request));
        }

        if (request.Start <= DateTimeOffset.Now.AddSeconds(15))
        {
            throw new ArgumentException("Czas rozpoczęcia nagrania musi przypadać co najmniej 15 sekund w przyszłości.", nameof(request));
        }

        if (!Uri.TryCreate(channel.StreamUrl, UriKind.Absolute, out var streamUri))
        {
            throw new InvalidOperationException("Adres strumienia wybranego kanału jest nieprawidłowy.");
        }

        var preferences = preferencesService.Current.Recording;
        var entry = new ScheduledRecordingEntry(
            Guid.NewGuid(),
            channel.PlaylistSourceId.Value,
            channel.Id.Value,
            channel.Name,
            request.ProgrammeTitle,
            request.Start,
            request.End,
            request.Mode,
            dataProtector.Protect(streamUri.AbsoluteUri),
            preferences.OutputDirectory,
            preferences.AudioFormat,
            preferences.AudioBitrateKbps,
            preferences.FileNameTemplate);

        await scheduledStore.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
        try
        {
            var applicationPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(applicationPath))
            {
                throw new InvalidOperationException("Nie udało się ustalić ścieżki programu TelePilot.");
            }

            await taskScheduler.RegisterAsync(
                entry,
                applicationPath,
                preferences.WakeComputerForScheduledRecording,
                cancellationToken).ConfigureAwait(false);
            return entry;
        }
        catch
        {
            await scheduledStore.RemoveAsync(entry.Id, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<ScheduledRecordingEntry>> GetScheduledAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var entries = await scheduledStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return entries
            .Where(static entry => entry.Status is ScheduledRecordingStatus.Scheduled or ScheduledRecordingStatus.Recording)
            .ToArray();
    }

    public async Task CancelScheduledAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var entry = await scheduledStore.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return;
        }

        if (entry.Status == ScheduledRecordingStatus.Recording)
        {
            throw new InvalidOperationException("Nie można anulować zadania, które już nagrywa.");
        }

        await taskScheduler.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await scheduledStore.RemoveAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        recordingService.ProgressChanged -= OnProgressChanged;
        try
        {
            await recordingService.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogRecordingShutdownFailure(logger, exception);
        }
    }

    internal static RecordingRequest CreateRequest(
        Channel channel,
        Uri streamUri,
        string? programmeTitle,
        RecordingMode mode,
        RecordingPreferences preferences,
        DateTimeOffset startedAt) => new(
            streamUri,
            channel.Name,
            programmeTitle,
            startedAt,
            preferences.OutputDirectory,
            mode,
            Container: StreamContainer.MpegTransportStream,
            AudioFormat: preferences.AudioFormat,
            LossyBitrateKbps: preferences.AudioBitrateKbps,
            FileNameTemplate: preferences.FileNameTemplate);

    private void OnProgressChanged(object? sender, RecordingProgress progress)
    {
        ProgressChanged?.Invoke(this, progress);
    }
}

internal sealed class UnavailableRecordingCoordinator : IRecordingCoordinator
{
    public event EventHandler<RecordingProgress>? ProgressChanged
    {
        add { }
        remove { }
    }

    public bool IsRecording => false;

    public Task<string> StartAsync(
        Channel channel,
        string? programmeTitle,
        RecordingMode mode,
        CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new InvalidOperationException("Nagrywanie nie zostało skonfigurowane."));

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ScheduledRecordingEntry> ScheduleAsync(
        Channel channel,
        RecordingScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ScheduledRecordingEntry>(new InvalidOperationException("Planowanie nagrań nie zostało skonfigurowane."));

    public Task<IReadOnlyList<ScheduledRecordingEntry>> GetScheduledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScheduledRecordingEntry>>([]);

    public Task CancelScheduledAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
