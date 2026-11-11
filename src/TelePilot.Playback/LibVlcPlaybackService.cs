using System.Threading.Channels;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;
using CoreMediaTrack = TelePilot.Core.Models.MediaTrack;
using CoreChannel = TelePilot.Core.Models.Channel;
using VlcMediaTrack = LibVLCSharp.Shared.MediaTrack;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.Playback;

public sealed partial class LibVlcPlaybackService : IPlaybackService, ILibVlcMediaPlayerProvider
{
    private readonly Lazy<NativePlayerResources> _nativeResources;
    private readonly ILogger<LibVlcPlaybackService> _logger;
    private readonly Channel<Func<Task>> _commandQueue;
    private readonly Task _commandWorker;
    private readonly Lock _stateLock = new();
    private readonly NativePlaybackAttemptSignals _nativeAttemptSignals = new();
    private EventHandler<EventArgs>? _attemptErrorHandler;
    private EventHandler<EventArgs>? _attemptEndHandler;
    private CancellationTokenSource _retryCancellation = new();
    private PlaybackSnapshot _current = PlaybackSnapshot.Initial;
    private CoreChannel? _channel;
    private PlaybackOptions _options = new();
    private int _metadataRefreshQueued;
    private long _retryGeneration;
    private volatile bool _disposed;
    private PlaybackRecoveryPolicy? _recovery;
    private Timer? _healthTimer;
    private int _healthQueued;
    private long _requestGeneration;
    private bool _restoringPause;
    private double _resumePosition;
    private Uri? _recoverySourceUri;
    private double _archiveResumeOffset;
    private readonly Dictionary<MediaTrackKind, (int Id, string Name)> _rememberedTracks = [];
    private readonly HashSet<MediaTrackKind> _tracksToRestore = [];
    private static double MonotonicNow => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    public LibVlcPlaybackService(ILogger<LibVlcPlaybackService> logger)
        : this(logger, LibVlcNativeBootstrapper.Initialize)
    {
    }

    internal LibVlcPlaybackService(ILogger<LibVlcPlaybackService> logger, Action initializeNativeLibraries)
    {
        _logger = logger;
        _nativeResources = new Lazy<NativePlayerResources>(() => CreateNativeResources(initializeNativeLibraries));
        _commandQueue = System.Threading.Channels.Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _commandWorker = Task.Run(ProcessCommandsAsync);
    }

    private LibVLC _libVlc => _nativeResources.Value.LibVlc;

    private MediaPlayer _mediaPlayer => _nativeResources.Value.MediaPlayer;

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public event EventHandler<MediaTracksChangedEventArgs>? TracksChanged;

    public PlaybackSnapshot Current
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    public MediaPlayer MediaPlayer => PreparedNativeResources.MediaPlayer;

    public string LibVlcVersion => PreparedNativeResources.LibVlc.Version;

    private NativePlayerResources PreparedNativeResources => _nativeResources.IsValueCreated
        ? _nativeResources.Value
        : throw new InvalidOperationException(T("Najpierw przygotuj silnik odtwarzania przez PrepareAsync.", "Prepare the player with PrepareAsync first."));

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Plugin discovery can take seconds on a cold installation. Keep it on
        // the same worker as playback commands, after the window is visible.
        await EnqueueAsync(() => { _ = _nativeResources.Value; }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlaybackCommandResult> PlayAsync(
        CoreChannel channel,
        PlaybackOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (!Uri.TryCreate(channel.StreamUrl, UriKind.Absolute, out var streamUri))
        {
            return PlaybackCommandResult.Rejected(T("Kanał ma nieprawidłowy adres strumienia.", "The channel has an invalid stream address."));
        }

        var requestGeneration = Interlocked.Increment(ref _requestGeneration);
        var resolved = await HlsVariantResolver.ResolveAsync(streamUri,
            options.Buffering?.Normalize().VariantMaxMbps ?? 0, cancellationToken).ConfigureAwait(false);
        return await EnqueueAsync(
            () =>
            {
                if (requestGeneration != Volatile.Read(ref _requestGeneration))
                    return PlaybackCommandResult.Rejected(T("Żądanie odtwarzania jest już nieaktualne.", "The playback request is no longer current."));
                ResetRetryCancellation();
                _channel = channel; _options = options; _recoverySourceUri = streamUri;
                _restoringPause = false; _resumePosition = _archiveResumeOffset = 0;
                _rememberedTracks.Clear(); _tracksToRestore.Clear();
                _recovery = new PlaybackRecoveryPolicy(options, streamUri,
                    channel.Attributes.TryGetValue("_dispatcharr", out var marker) && marker == "1", MonotonicNow);
                _healthTimer ??= new Timer(_ => QueueHealthCheck(), null, 500, 500);
                return PlayInternal(channel, resolved, options, quickStart: options.IsLive);
            }, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _requestGeneration);
        await EnqueueAsync(
            () =>
            {
                ResetRetryCancellation();
                _recovery = null;
                _nativeAttemptSignals.BeginAttempt(Interlocked.Increment(ref _playbackSessionGeneration));
                UpdateCurrent(Current with
                {
                    State = PlaybackState.Stopping,
                    StatusMessage = T("Zatrzymywanie odtwarzania.", "Stopping playback."),
                });
                _mediaPlayer.Stop();
                _channel = null;
                UpdateCurrent(PlaybackSnapshot.Initial with
                {
                    Volume = ReadKnownVolume(),
                    IsMuted = _mediaPlayer.Mute,
                    StatusMessage = T("Odtwarzanie zatrzymane.", "Playback stopped."),
                });
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<PlaybackCommandResult> TogglePauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnqueueAsync(
            () =>
            {
                var snapshot = Current;
                if (snapshot.State is not (PlaybackState.Playing or PlaybackState.Paused))
                {
                    return PlaybackCommandResult.Rejected(T("Najpierw rozpocznij odtwarzanie kanału.", "Start playing a channel first."));
                }

                if (!_mediaPlayer.CanPause)
                {
                    return PlaybackCommandResult.Unsupported(T("Ten strumień na żywo nie obsługuje pauzy.", "This live stream does not support pausing."));
                }

                _mediaPlayer.SetPause(snapshot.State == PlaybackState.Playing);
                return PlaybackCommandResult.Success();
            },
            cancellationToken);
    }

    public async Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 100);

        await EnqueueAsync(
            () =>
            {
                _mediaPlayer.Volume = volume;
                UpdateCurrent(Current with { Volume = volume });
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnqueueAsync(
            () =>
            {
                _mediaPlayer.Mute = isMuted;
                UpdateCurrent(Current with { IsMuted = isMuted });
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<CoreMediaTrack>> GetTracksAsync(
        MediaTrackKind kind,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnqueueAsync<IReadOnlyList<CoreMediaTrack>>(() => ReadTracks(kind), cancellationToken);
    }

    public Task<PlaybackCommandResult> SelectTrackAsync(
        MediaTrackKind kind,
        int engineId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EnqueueAsync(
            () =>
            {
                var succeeded = kind switch
                {
                    MediaTrackKind.Audio => _mediaPlayer.SetAudioTrack(engineId),
                    MediaTrackKind.Subtitle => _mediaPlayer.SetSpu(engineId),
                    _ => false,
                };

                if (!succeeded)
                {
                    return PlaybackCommandResult.Rejected(T("Silnik nie przyjął wyboru ścieżki.", "The player did not accept the selected track."));
                }

                var chosen = ReadTracks(kind).FirstOrDefault(track => track.EngineId == engineId);
                _rememberedTracks[kind] = (engineId, chosen?.Description ?? string.Empty);
                _tracksToRestore.Remove(kind);
                _recovery?.ResetObservation(MonotonicNow);
                PublishTracks(kind);
                return PlaybackCommandResult.Success();
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _requestGeneration);
        _healthTimer?.Dispose();
        CancelRetryCancellation();
        try
        {
            await EnqueueAsync(
                () =>
                {
                    if (_nativeResources.IsValueCreated)
                    {
                        UnsubscribePlayerEvents();
                        if (_mediaPlayer.IsPlaying)
                        {
                            _mediaPlayer.Stop();
                        }
                    }
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _commandQueue.Writer.TryComplete();
            await _commandWorker.ConfigureAwait(false);
            _retryCancellation.Dispose();
            if (_nativeResources.IsValueCreated)
            {
                _mediaPlayer.Dispose();
                _libVlc.Dispose();
            }
        }
    }

    private NativePlayerResources CreateNativeResources(Action initializeNativeLibraries)
    {
        initializeNativeLibraries();
        var libVlc = new LibVLC("--no-video-title-show", "--quiet");
        MediaPlayer? player = null;
        try
        {
            player = new MediaPlayer(libVlc)
            {
                // Avalonia owns keyboard and mouse navigation, including after
                // a stopped player is shown again.
                EnableKeyInput = false,
                EnableMouseInput = false,
            };
            SubscribePlayerEvents(player);
            return new NativePlayerResources(libVlc, player);
        }
        catch
        {
            player?.Dispose();
            libVlc.Dispose();
            throw;
        }
    }

    private sealed record NativePlayerResources(LibVLC LibVlc, MediaPlayer MediaPlayer);

    private PlaybackCommandResult PlayInternal(CoreChannel channel, Uri streamUri, PlaybackOptions options, bool quickStart = false)
    {
        // Stop drains the previous native input before new callback identities
        // or latches can be associated with its replacement.
        _mediaPlayer.Stop();
        var attemptGeneration = Interlocked.Increment(ref _playbackSessionGeneration);
        _nativeAttemptSignals.BeginAttempt(attemptGeneration);
        BindNativeAttemptEvents(attemptGeneration);
        UpdateCurrent(new PlaybackSnapshot(
            PlaybackState.Connecting,
            channel.Id,
            channel.Name,
            ReadKnownVolume(),
            _mediaPlayer.Mute,
            false,
            T($"Łączenie z kanałem {channel.Name}.", $"Connecting to {channel.Name}.")));

        var appliedCache = _recovery?.Target ?? 2;
        _recovery?.BeginAttempt(MonotonicNow, appliedCache);

        using var media = new Media(_libVlc, streamUri);
        foreach (var option in PlaybackBufferSettings.MediaOptions(appliedCache, options.IsLive, quickStart)) media.AddOption(option);
        media.AddOption($":http-user-agent={HlsVariantResolver.UserAgent}");
        if (_resumePosition > 0 && !options.IsLive) media.AddOption(":start-time=" + _resumePosition.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!_mediaPlayer.Play(media))
        {
            var error = new PlaybackError(channel.Name, T("Silnik odtwarzania odrzucił strumień.", "The player rejected the stream."), "play-rejected");
            EvaluateRecovery(new RecoverySample(7));
            return _recovery is { GaveUp: false } ? PlaybackCommandResult.Success() : PlaybackCommandResult.Rejected(error.Message);
        }

        LogConnecting(_logger, channel.Name, channel.Id);
        return PlaybackCommandResult.Success();
    }

    private int ReadKnownVolume()
    {
        var nativeVolume = _mediaPlayer.Volume;
        return nativeVolume is >= 0 and <= 100
            ? nativeVolume
            : Current.Volume;
    }

    private void SubscribePlayerEvents(MediaPlayer player)
    {
        player.Playing += OnPlaying;
        player.Paused += OnPaused;
        player.Stopped += OnStopped;
        player.ESAdded += OnTracksChanged;
        player.ESDeleted += OnTracksChanged;
        player.ESSelected += OnTracksChanged;
    }

    private void UnsubscribePlayerEvents()
    {
        _mediaPlayer.Playing -= OnSeekPlaybackResumed;
        _seekEventsSubscribed = false;
        _mediaPlayer.Playing -= OnPlaying;
        _mediaPlayer.Paused -= OnPaused;
        _mediaPlayer.Stopped -= OnStopped;
        UnbindNativeAttemptEvents();
        _mediaPlayer.ESAdded -= OnTracksChanged;
        _mediaPlayer.ESDeleted -= OnTracksChanged;
        _mediaPlayer.ESSelected -= OnTracksChanged;
    }

    private void OnPlaying(object? sender, EventArgs eventArgs) => QueuePlaybackEvent(ApplyPlaying);

    private void ApplyPlaying()
    {
        var snapshot = Current;
        if (_mediaPlayer.State != VLCState.Playing
            || _channel is null
            || snapshot.ChannelId != _channel.Id)
        {
            return;
        }

        if (_restoringPause && _mediaPlayer.CanPause)
        {
            _restoringPause = false; _mediaPlayer.SetPause(true);
        }
        UpdateCurrent(snapshot with
        {
            State = PlaybackState.Playing,
            // Wywołania API LibVLC z jego własnego callbacku mogą się
            // zakleszczyć. Właściwa wartość jest odczytywana niżej przez
            // szeregowaną kolejkę poleceń.
            CanPause = false,
            StatusMessage = snapshot.ChannelName is null ? T("Połączono.", "Connected.") : T($"Odtwarzanie kanału {snapshot.ChannelName}.", $"Playing {snapshot.ChannelName}."),
            Error = null,
        });
        QueuePlayerMetadataRefresh();
    }

    private void OnPaused(object? sender, EventArgs eventArgs) => QueuePlaybackEvent(ApplyPaused);

    private void ApplyPaused()
    {
        if (_mediaPlayer.State != VLCState.Paused
            || _channel is null
            || Current.ChannelId != _channel.Id)
        {
            return;
        }

        UpdateCurrent(Current with { State = PlaybackState.Paused, StatusMessage = T("Odtwarzanie wstrzymane.", "Playback paused.") });
    }

    private void OnStopped(object? sender, EventArgs eventArgs) => QueuePlaybackEvent(ApplyStopped);

    private void ApplyStopped()
    {
        if (_mediaPlayer.State == VLCState.Stopped && Current.State == PlaybackState.Stopping)
        {
            UpdateCurrent(Current with { State = PlaybackState.Idle, CanPause = false, StatusMessage = T("Odtwarzanie zatrzymane.", "Playback stopped.") });
        }
    }

    private void BindNativeAttemptEvents(long generation)
    {
        UnbindNativeAttemptEvents();
        _attemptErrorHandler = (_, _) =>
        {
            if (_disposed || !_nativeAttemptSignals.RecordError(generation)) return;
            QueuePlaybackEvent(ApplyEncounteredError, generation);
        };
        _attemptEndHandler = (_, _) =>
        {
            if (_disposed || !_nativeAttemptSignals.RecordEnd(generation)) return;
            QueuePlaybackEvent(ApplyEndReached, generation);
        };
        _mediaPlayer.EncounteredError += _attemptErrorHandler;
        _mediaPlayer.EndReached += _attemptEndHandler;
    }

    private void UnbindNativeAttemptEvents()
    {
        if (_attemptErrorHandler is not null) _mediaPlayer.EncounteredError -= _attemptErrorHandler;
        if (_attemptEndHandler is not null) _mediaPlayer.EndReached -= _attemptEndHandler;
        _attemptErrorHandler = _attemptEndHandler = null;
    }

    private void ApplyEndReached()
    {
        if (_channel is null || Current.ChannelId != _channel.Id) return;
        PollPlaybackHealth();
    }

    private void ApplyEncounteredError()
    {
        var channel = _channel;
        if (channel is null || Current.ChannelId != channel.Id) return;
        LogPlaybackError(_logger, channel.Name, channel.Id);
        // Read fresh facts for this attempt. A queued callback must never
        // replay an old failure against a replacement input.
        PollPlaybackHealth();
    }

    private void OnTracksChanged(object? sender, EventArgs eventArgs) => QueuePlayerMetadataRefresh();

    private void QueuePlaybackEvent(Action operation, long? capturedGeneration = null)
    {
        if (_disposed)
        {
            return;
        }

        var sessionGeneration = capturedGeneration ?? Volatile.Read(ref _playbackSessionGeneration);
        _commandQueue.Writer.TryWrite(() =>
        {
            if (_disposed || sessionGeneration != Volatile.Read(ref _playbackSessionGeneration))
            {
                return Task.CompletedTask;
            }

            operation();
            return Task.CompletedTask;
        });
    }

    private void QueuePlayerMetadataRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _metadataRefreshQueued, 1) == 1)
        {
            return;
        }

        if (!_commandQueue.Writer.TryWrite(() =>
        {
            try
            {
                if (_disposed)
                {
                    return Task.CompletedTask;
                }

                var snapshot = Current;
                if (snapshot.State is PlaybackState.Playing or PlaybackState.Paused)
                {
                    var canPause = _mediaPlayer.CanPause;
                    if (snapshot.CanPause != canPause)
                    {
                        UpdateCurrent(snapshot with { CanPause = canPause });
                    }
                }

                RestoreSelectedTracks();
                PublishTracks(MediaTrackKind.Audio);
                PublishTracks(MediaTrackKind.Subtitle);
                return Task.CompletedTask;
            }
            finally
            {
                Interlocked.Exchange(ref _metadataRefreshQueued, 0);
            }
        }))
        {
            Interlocked.Exchange(ref _metadataRefreshQueued, 0);
        }
    }

    private CoreMediaTrack[] ReadTracks(MediaTrackKind kind)
    {
        var descriptions = kind == MediaTrackKind.Audio
            ? _mediaPlayer.AudioTrackDescription
            : _mediaPlayer.SpuDescription;
        var selectedId = kind == MediaTrackKind.Audio ? _mediaPlayer.AudioTrack : _mediaPlayer.Spu;

        var details = ReadMediaTrackDetails(kind);
        return descriptions
            .Select(description =>
            {
                details.TryGetValue(description.Id, out var detail);
                var name = !string.IsNullOrWhiteSpace(detail.Description)
                    ? detail.Description
                    : !string.IsNullOrWhiteSpace(description.Name)
                        ? description.Name
                        : kind == MediaTrackKind.Audio ? T("Ścieżka dźwiękowa", "Audio track") : "Napisy";
                return new CoreMediaTrack(description.Id, kind, name, detail.Language, description.Id == selectedId);
            })
            .ToArray();
    }

    private Dictionary<int, (string? Description, string? Language)> ReadMediaTrackDetails(MediaTrackKind kind)
    {
        using var media = _mediaPlayer.Media;
        if (media is null)
        {
            return [];
        }

        var expectedType = kind == MediaTrackKind.Audio ? TrackType.Audio : TrackType.Text;
        return media.Tracks
            .Where(track => track.TrackType == expectedType)
            .GroupBy(track => unchecked((int)track.Id))
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    VlcMediaTrack track = group.First();
                    return (track.Description, track.Language);
                });
    }

    private void PublishTracks(MediaTrackKind kind)
    {
        var tracks = ReadTracks(kind);
        TracksChanged?.Invoke(this, new MediaTracksChangedEventArgs(kind, tracks));
    }

    private void UpdateCurrent(PlaybackSnapshot snapshot)
    {
        lock (_stateLock)
        {
            _current = snapshot;
        }

        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(snapshot));
    }

    private void ResetRetryCancellation()
    {
        var previous = Interlocked.Exchange(ref _retryCancellation, new CancellationTokenSource());
        Interlocked.Increment(ref _retryGeneration);
        previous.Cancel();
        previous.Dispose();
    }

    private void CancelRetryCancellation()
    {
        Interlocked.Increment(ref _retryGeneration);
        _retryCancellation.Cancel();
    }

    private async Task ProcessCommandsAsync()
    {
        await foreach (var command in _commandQueue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await command().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Zdarzenia ścieżek pochodzą z kodu natywnego i mogą zbiegać
                // się ze zmianą strumienia. Pojedynczy błąd nie może zakończyć
                // workera, bo wszystkie późniejsze skróty czekałyby bez końca.
                LogCommandWorkerFailure(_logger, exception);
            }
        }
    }

    private Task<object?> EnqueueAsync(Action operation, CancellationToken cancellationToken)
    {
        return EnqueueAsync<object?>(
            () =>
            {
                operation();
                return null;
            },
            cancellationToken);
    }

    private async Task<T> EnqueueAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commandQueue.Writer.WriteAsync(
            () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(operation());
                }
                catch (OperationCanceledException exception)
                {
                    completion.TrySetCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }

                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 3001,
        Level = Microsoft.Extensions.Logging.LogLevel.Information,
        Message = "Started connecting to channel {ChannelName} ({ChannelId}).")]
    private static partial void LogConnecting(ILogger logger, string channelName, ChannelId channelId);

    [LoggerMessage(
        EventId = 3002,
        Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Playback error for channel {ChannelName} ({ChannelId}).")]
    private static partial void LogPlaybackError(ILogger logger, string channelName, ChannelId channelId);

    [LoggerMessage(
        EventId = 3003,
        Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Ignored an unexpected error in the player command queue. The queue is still active.")]
    private static partial void LogCommandWorkerFailure(ILogger logger, Exception exception);
}
