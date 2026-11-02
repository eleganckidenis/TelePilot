using LibVLCSharp.Shared;
using TelePilot.Core.CatchUp;
using TelePilot.Core.Models;

namespace TelePilot.Playback;

public sealed partial class LibVlcPlaybackService
{
    private void QueueHealthCheck()
    {
        if (_disposed || Interlocked.Exchange(ref _healthQueued, 1) != 0) return;
        if (!_commandQueue.Writer.TryWrite(() =>
        {
            try { if (!_disposed) PollPlaybackHealth(); }
            finally { Interlocked.Exchange(ref _healthQueued, 0); }
            return Task.CompletedTask;
        })) Interlocked.Exchange(ref _healthQueued, 0);
    }

    private void PollPlaybackHealth()
    {
        if (_channel is null || _recovery is null || !_nativeResources.IsValueCreated || Current.State == PlaybackState.Stopping) return;
        using var media = _mediaPlayer.Media;
        PlaybackProgress? progress = null;
        if (media is not null)
        {
            var stats = media.Statistics;
            progress = new(stats.DecodedVideo, stats.DisplayedPictures, stats.DecodedAudio, stats.PlayedAudioBuffers);
        }
        var radio = _channel.Attributes.TryGetValue("radio", out var radioHint) &&
            radioHint.Trim().ToLowerInvariant() is "true" or "1" or "yes";
        EvaluateRecovery(new RecoverySample((int)_mediaPlayer.State, _mediaPlayer.Time / 1000d, _mediaPlayer.Length / 1000d,
            progress, radio || _mediaPlayer.VideoTrackCount == 0));
        RestoreSelectedTracks();
    }

    private void EvaluateRecovery(RecoverySample sample)
    {
        if (_recovery is null || _channel is null) return;
        var action = _recovery.Evaluate(sample, MonotonicNow, _restoringPause || Current.State == PlaybackState.Paused);
        switch (action.Kind)
        {
            case RecoveryKind.None: break;
            case RecoveryKind.Finished:
                _recovery = null;
                UpdateCurrent(Current with { State = PlaybackState.Idle, CanPause = false, StatusMessage = "Strumień został zakończony." });
                break;
            case RecoveryKind.Lost:
                _mediaPlayer.Stop();
                var error = new PlaybackError(_channel.Name, "Nie udało się odzyskać transmisji. Użyj Stop i uruchom kanał ponownie.", "stream-lost");
                UpdateCurrent(Current with { State = PlaybackState.Error, CanPause = false, Error = error, StatusMessage = error.Message });
                break;
            case RecoveryKind.Retry:
                _restoringPause = _restoringPause || Current.State == PlaybackState.Paused;
                if (!_options.IsLive && sample.Position > 0) _resumePosition = sample.Position;
                _tracksToRestore.UnionWith(_rememberedTracks.Keys);
                UpdateCurrent(Current with { State = PlaybackState.Connecting, CanPause = false, Error = null,
                    StatusMessage = action.QuickStart ? "Odświeżanie transmisji." : $"Ponowne łączenie, próba {_recovery.Attempts} z {_options.MaximumReconnectAttempts}." });
                _ = RecoverAsync(action, _retryGeneration, Volatile.Read(ref _requestGeneration), _retryCancellation.Token);
                break;
        }
    }

    private async Task RecoverAsync(RecoveryAction action, long generation, long requestGeneration, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(action.Delay), cancellationToken).ConfigureAwait(false);
            // Resolve each retry from its source, since a master may issue fresh
            // signed variant URLs. HTTP never holds up the native owner queue.
            var sourceUri = await EnqueueAsync<Uri?>(() =>
            {
                if (!IsCurrentRecovery(generation, requestGeneration) || _channel is null) return null;
                var uri = _recoverySourceUri;
                if (uri is null && !Uri.TryCreate(_channel.StreamUrl, UriKind.Absolute, out uri)) return null;
                if (!_options.IsLive && _resumePosition > 0 && _options.ArchiveSource is { } source &&
                    _options.ArchiveStart is { } start && _options.ArchiveEnd is { } end &&
                    CatchUpPlayback.TryCreatePlaybackChannel(source, start + TimeSpan.FromSeconds(_archiveResumeOffset + _resumePosition), end,
                        DateTimeOffset.Now, out var resumed, out _) && Uri.TryCreate(resumed!.StreamUrl, UriKind.Absolute, out var resumedUri))
                {
                    uri = resumedUri; _archiveResumeOffset += _resumePosition; _resumePosition = 0; _recoverySourceUri = uri;
                }
                return uri;
            }, cancellationToken).ConfigureAwait(false);
            if (sourceUri is null) return;
            var resolved = await HlsVariantResolver.ResolveAsync(sourceUri,
                _options.Buffering?.Normalize().VariantMaxMbps ?? 0, cancellationToken).ConfigureAwait(false);
            await EnqueueAsync(() =>
            {
                if (!IsCurrentRecovery(generation, requestGeneration) || _channel is null) return;
                PlayInternal(_channel, resolved, _options, action.QuickStart);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (System.Threading.Channels.ChannelClosedException) when (_disposed) { }
        catch (Exception exception) { LogCommandWorkerFailure(_logger, exception); }
    }

    private bool IsCurrentRecovery(long generation, long requestGeneration) =>
        !_disposed && generation == _retryGeneration && requestGeneration == Volatile.Read(ref _requestGeneration)
        && _recovery is { GaveUp: false };

    internal static TelePilot.Core.Models.MediaTrack? FindRememberedTrack(
        IReadOnlyList<TelePilot.Core.Models.MediaTrack> tracks, int id, string name)
    {
        var named = tracks.Where(track => track.Description == name).ToArray();
        // A unique name can survive engine-ID changes. Repeated generic names
        // must retain the selected ID rather than silently picking the first.
        return named.Length == 1 ? named[0] : tracks.FirstOrDefault(track => track.EngineId == id) ?? named.FirstOrDefault();
    }

    private void RestoreSelectedTracks()
    {
        if (_mediaPlayer.State is not (VLCState.Playing or VLCState.Paused)) return;
        foreach (var kind in _tracksToRestore.ToArray())
        {
            var wanted = _rememberedTracks[kind];
            var tracks = ReadTracks(kind);
            var match = FindRememberedTrack(tracks, wanted.Id, wanted.Name);
            if (match is null) continue;
            var ok = kind == MediaTrackKind.Audio ? _mediaPlayer.SetAudioTrack(match.EngineId) : _mediaPlayer.SetSpu(match.EngineId);
            if (ok) _tracksToRestore.Remove(kind);
        }
    }
}
