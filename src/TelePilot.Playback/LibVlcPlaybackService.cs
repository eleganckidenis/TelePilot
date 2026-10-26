using System.Threading.Channels;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;
using CoreMediaTrack = TelePilot.Core.Models.MediaTrack;
using CoreChannel = TelePilot.Core.Models.Channel;
using VlcMediaTrack = LibVLCSharp.Shared.MediaTrack;

namespace TelePilot.Playback;

public sealed partial class LibVlcPlaybackService : IPlaybackService, ILibVlcMediaPlayerProvider
{
    private readonly Lazy<NativePlayerResources> _nativeResources;
    private readonly ILogger<LibVlcPlaybackService> _logger;
    private readonly Channel<Func<Task>> _commandQueue;
    private readonly Task _commandWorker;
    private readonly Lock _stateLock = new();
    private CancellationTokenSource _retryCancellation = new();
    private PlaybackSnapshot _current = PlaybackSnapshot.Initial;
    private CoreChannel? _channel;
    private PlaybackOptions _options = new();
    private int _reconnectAttempt;
    private int _metadataRefreshQueued;
    private long _retryGeneration;
    private long _nextReconnectLease;
    private long _activeReconnectLease;
    private volatile bool _disposed;

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
        : throw new InvalidOperationException("Najpierw przygotuj silnik odtwarzania przez PrepareAsync.");

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
            return PlaybackCommandResult.Rejected("Kanał ma nieprawidłowy adres strumienia.");
        }

        return await EnqueueAsync(
            () =>
            {
                // The active session belongs to the same serialized operation as
                // the native Play call. Otherwise a queued Stop can clear a newer
                // channel that was assigned by a following PlayAsync call.
                ResetRetryCancellation();
                _channel = channel;
                _options = options;
                Interlocked.Exchange(ref _reconnectAttempt, 0);
                return PlayInternal(channel, streamUri, options);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnqueueAsync(
            () =>
            {
                ResetRetryCancellation();
                Interlocked.Increment(ref _playbackSessionGeneration);
                UpdateCurrent(Current with
                {
                    State = PlaybackState.Stopping,
                    StatusMessage = "Zatrzymywanie odtwarzania.",
                });
                _mediaPlayer.Stop();
                _channel = null;
                Interlocked.Exchange(ref _reconnectAttempt, 0);
                UpdateCurrent(PlaybackSnapshot.Initial with
                {
                    Volume = ReadKnownVolume(),
                    IsMuted = _mediaPlayer.Mute,
                    StatusMessage = "Odtwarzanie zatrzymane.",
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
                    return PlaybackCommandResult.Rejected("Najpierw rozpocznij odtwarzanie kanału.");
                }

                if (!_mediaPlayer.CanPause)
                {
                    return PlaybackCommandResult.Unsupported("Ten strumień na żywo nie obsługuje pauzy.");
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
                    return PlaybackCommandResult.Rejected("Silnik nie przyjął wyboru ścieżki.");
                }

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

    private PlaybackCommandResult PlayInternal(CoreChannel channel, Uri streamUri, PlaybackOptions options)
    {
        Interlocked.Increment(ref _playbackSessionGeneration);
        UpdateCurrent(new PlaybackSnapshot(
            PlaybackState.Connecting,
            channel.Id,
            channel.Name,
            ReadKnownVolume(),
            _mediaPlayer.Mute,
            false,
            $"Łączenie z kanałem {channel.Name}."));

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Stop();
        }

        using var media = new Media(_libVlc, streamUri);
        media.AddOption($":network-caching={options.NetworkCachingMilliseconds}");
        media.AddOption(":input-repeat=0");
        if (!_mediaPlayer.Play(media))
        {
            var error = new PlaybackError(channel.Name, "Silnik odtwarzania odrzucił strumień.", "play-rejected");
            UpdateCurrent(Current with
            {
                State = PlaybackState.Error,
                StatusMessage = error.Message,
                Error = error,
            });
            return PlaybackCommandResult.Rejected(error.Message);
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
        player.EndReached += OnEndReached;
        player.EncounteredError += OnEncounteredError;
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
        _mediaPlayer.EndReached -= OnEndReached;
        _mediaPlayer.EncounteredError -= OnEncounteredError;
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

        // A confirmed Playing state ends the preceding error cycle. Cancel any
        // delayed reconnect that may still be outstanding so it cannot interrupt
        // this healthy stream later.
        ResetRetryCancellation();
        Interlocked.Exchange(ref _reconnectAttempt, 0);
        UpdateCurrent(snapshot with
        {
            State = PlaybackState.Playing,
            // Wywołania API LibVLC z jego własnego callbacku mogą się
            // zakleszczyć. Właściwa wartość jest odczytywana niżej przez
            // szeregowaną kolejkę poleceń.
            CanPause = false,
            StatusMessage = snapshot.ChannelName is null ? "Połączono." : $"Odtwarzanie kanału {snapshot.ChannelName}.",
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

        UpdateCurrent(Current with { State = PlaybackState.Paused, StatusMessage = "Odtwarzanie wstrzymane." });
    }

    private void OnStopped(object? sender, EventArgs eventArgs) => QueuePlaybackEvent(ApplyStopped);

    private void ApplyStopped()
    {
        if (_mediaPlayer.State == VLCState.Stopped && Current.State == PlaybackState.Stopping)
        {
            UpdateCurrent(Current with { State = PlaybackState.Idle, CanPause = false, StatusMessage = "Odtwarzanie zatrzymane." });
        }
    }

    private void OnEndReached(object? sender, EventArgs eventArgs) => QueuePlaybackEvent(ApplyEndReached);

    private void ApplyEndReached()
    {
        if (_mediaPlayer.State != VLCState.Ended
            || _channel is null
            || Current.ChannelId != _channel.Id)
        {
            return;
        }

        UpdateCurrent(Current with { State = PlaybackState.Idle, CanPause = false, StatusMessage = "Strumień został zakończony." });
    }

    private void OnEncounteredError(object? sender, EventArgs eventArgs) => QueuePlaybackEvent(ApplyEncounteredError);

    private void ApplyEncounteredError()
    {
        var channel = _channel;
        if (_mediaPlayer.State != VLCState.Error
            || channel is null
            || Current.ChannelId != channel.Id)
        {
            return;
        }

        var lease = Interlocked.Increment(ref _nextReconnectLease);
        if (Interlocked.CompareExchange(ref _activeReconnectLease, lease, 0) != 0)
        {
            // LibVLC can report the same failure more than once. Only one delay
            // and one retry may belong to a single error cycle.
            return;
        }

        var error = new PlaybackError(channel.Name, "Utracono połączenie ze strumieniem.", "libvlc-error");
        UpdateCurrent(Current with { State = PlaybackState.Error, CanPause = false, Error = error, StatusMessage = error.Message });
        LogPlaybackError(_logger, channel.Name, channel.Id);
        _ = ScheduleReconnectAsync(
            channel,
            _options,
            Volatile.Read(ref _retryGeneration),
            lease,
            _retryCancellation.Token);
    }

    private void OnTracksChanged(object? sender, EventArgs eventArgs) => QueuePlayerMetadataRefresh();

    private void QueuePlaybackEvent(Action operation)
    {
        if (_disposed)
        {
            return;
        }

        _commandQueue.Writer.TryWrite(() =>
        {
            if (_disposed)
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

    private async Task ScheduleReconnectAsync(
        CoreChannel channel,
        PlaybackOptions options,
        long generation,
        long lease,
        CancellationToken cancellationToken)
    {
        var releaseLease = true;
        try
        {
            if (!IsCurrentReconnect(generation, lease, cancellationToken))
            {
                return;
            }

            var attempt = Interlocked.Increment(ref _reconnectAttempt);
            if (attempt > options.MaximumReconnectAttempts)
            {
                // Keep the lease until Play/Stop starts a new session. Repeated
                // native error callbacks must not repeat the terminal message.
                releaseLease = false;
                UpdateCurrent(Current with { StatusMessage = "Nie udało się ponownie połączyć. Możesz ponowić ręcznie." });
                return;
            }

            UpdateCurrent(Current with { StatusMessage = $"Ponowne łączenie, próba {attempt} z {options.MaximumReconnectAttempts}." });
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 10)), cancellationToken).ConfigureAwait(false);
            if (!IsCurrentReconnect(generation, lease, cancellationToken)
                || !Uri.TryCreate(channel.StreamUrl, UriKind.Absolute, out var uri))
            {
                return;
            }

            await EnqueueAsync(
                () =>
                {
                    if (!IsCurrentReconnect(generation, lease, cancellationToken))
                    {
                        return PlaybackCommandResult.Rejected("Próba ponownego połączenia jest już nieaktualna.");
                    }

                    // Release this unique lease before native Play. A synchronous
                    // error callback from that attempt can then schedule exactly
                    // one following retry; this task's finally cannot clear it.
                    Interlocked.CompareExchange(ref _activeReconnectLease, 0, lease);
                    return PlayInternal(channel, uri, options);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException) when (_disposed)
        {
        }
        catch (Exception exception)
        {
            LogCommandWorkerFailure(_logger, exception);
        }
        finally
        {
            if (releaseLease)
            {
                Interlocked.CompareExchange(ref _activeReconnectLease, 0, lease);
            }
        }
    }

    private bool IsCurrentReconnect(long generation, long lease, CancellationToken cancellationToken) =>
        !_disposed
        && !cancellationToken.IsCancellationRequested
        && Volatile.Read(ref _retryGeneration) == generation
        && Volatile.Read(ref _activeReconnectLease) == lease;

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
                        : kind == MediaTrackKind.Audio ? "Ścieżka dźwiękowa" : "Napisy";
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
        Interlocked.Exchange(ref _activeReconnectLease, 0);
        previous.Cancel();
        previous.Dispose();
    }

    private void CancelRetryCancellation()
    {
        Interlocked.Increment(ref _retryGeneration);
        Interlocked.Exchange(ref _activeReconnectLease, 0);
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
        Message = "Rozpoczęto łączenie z kanałem {ChannelName} ({ChannelId}).")]
    private static partial void LogConnecting(ILogger logger, string channelName, ChannelId channelId);

    [LoggerMessage(
        EventId = 3002,
        Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Błąd odtwarzania kanału {ChannelName} ({ChannelId}).")]
    private static partial void LogPlaybackError(ILogger logger, string channelName, ChannelId channelId);

    [LoggerMessage(
        EventId = 3003,
        Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "Pominięto nieoczekiwany błąd w kolejce poleceń odtwarzacza; kolejka działa dalej.")]
    private static partial void LogCommandWorkerFailure(ILogger logger, Exception exception);
}
