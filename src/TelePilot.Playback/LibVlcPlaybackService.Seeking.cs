using TelePilot.Core.Models;
using System.Diagnostics;
using VlcState = LibVLCSharp.Shared.VLCState;

namespace TelePilot.Playback;

public sealed partial class LibVlcPlaybackService
{
    private long _playbackSessionGeneration;
    private long _seekResumeGeneration;
    private long _lastSeekResumeGeneration;
    private long _lastSeekSessionGeneration;
    private long _lastSeekTarget;
    private long _lastSeekTimestamp;
    private bool _lastSeekWasPaused;
    private bool _seekEventsSubscribed;

    public Task<TimeSpan?> GetPositionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = Volatile.Read(ref _playbackSessionGeneration);
        return EnqueueAsync<TimeSpan?>(() =>
        {
            if (_disposed || generation != Volatile.Read(ref _playbackSessionGeneration)
                || !_nativeResources.IsValueCreated
                || Current.State is not (PlaybackState.Playing or PlaybackState.Paused))
                return null;
            var time = _mediaPlayer.Time;
            var elapsed = _lastSeekTimestamp == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(_lastSeekTimestamp);
            time = ResolveSeekBaseTime(time, _lastSeekTarget, elapsed, _mediaPlayer.State == VlcState.Paused,
                _lastSeekWasPaused, _lastSeekSessionGeneration == generation
                && _lastSeekResumeGeneration == Volatile.Read(ref _seekResumeGeneration));
            return time < 0 ? null : TimeSpan.FromMilliseconds(time);
        }, cancellationToken);
    }

    public Task<PlaybackCommandResult> SeekRelativeAsync(
        TimeSpan offset,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Bind the command to the session seen by its caller. A preceding queued
        // Stop/Play must never apply this seek to a replacement stream.
        var generation = Volatile.Read(ref _playbackSessionGeneration);
        var channel = Volatile.Read(ref _channel);
        if (channel is null || Current.State is not (PlaybackState.Playing or PlaybackState.Paused))
        {
            return Task.FromResult(PlaybackCommandResult.Rejected("Najpierw rozpocznij odtwarzanie audycji."));
        }

        return EnqueueAsync(
            () =>
            {
                if (_disposed
                    || generation != Volatile.Read(ref _playbackSessionGeneration)
                    || !ReferenceEquals(channel, _channel)
                    || Current.State is not (PlaybackState.Playing or PlaybackState.Paused))
                {
                    return PlaybackCommandResult.Rejected("Odtwarzana audycja została zmieniona lub zatrzymana.");
                }

                if (!_nativeResources.IsValueCreated
                    || _mediaPlayer.State is not (VlcState.Playing or VlcState.Paused)
                    || !_mediaPlayer.IsSeekable)
                {
                    return PlaybackCommandResult.Unsupported("Ten strumień nie obsługuje przewijania.");
                }

                if (!_seekEventsSubscribed)
                {
                    // Native Time can briefly revert to the former input clock
                    // after SetTime. Keep consecutive requests together until
                    // that asynchronous seek settles, including while paused.
                    _mediaPlayer.Playing += OnSeekPlaybackResumed;
                    _seekEventsSubscribed = true;
                }

                var currentTime = _mediaPlayer.Time;
                var paused = _mediaPlayer.State == VlcState.Paused;
                var resumeGeneration = Volatile.Read(ref _seekResumeGeneration);
                var elapsed = _lastSeekTimestamp == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(_lastSeekTimestamp);
                currentTime = ResolveSeekBaseTime(currentTime, _lastSeekTarget, elapsed, paused, _lastSeekWasPaused,
                    _lastSeekSessionGeneration == generation && _lastSeekResumeGeneration == resumeGeneration);
                if (currentTime < 0)
                {
                    return PlaybackCommandResult.Unsupported("Pozycja odtwarzania nie jest jeszcze dostępna.");
                }

                var target = ClampSeekTime(currentTime, _mediaPlayer.Length, offset);
                _mediaPlayer.Time = target;
                _lastSeekTarget = target;
                _lastSeekTimestamp = Stopwatch.GetTimestamp();
                _lastSeekSessionGeneration = generation;
                _lastSeekResumeGeneration = resumeGeneration;
                _lastSeekWasPaused = paused;
                return new PlaybackCommandResult(
                    PlaybackCommandStatus.Succeeded,
                    $"Pozycja odtwarzania: {target / 1_000} s.");
            },
            cancellationToken);
    }

    private void OnSeekPlaybackResumed(object? sender, EventArgs e) => Interlocked.Increment(ref _seekResumeGeneration);

    internal static long ResolveSeekBaseTime(
        long nativeTime, long lastTarget, TimeSpan elapsed, bool paused, bool lastSeekWasPaused, bool samePlaybackClock)
    {
        // A playing input gets at most one second to replace the cached clock.
        // A paused input cannot advance, so its requested position stays valid
        // until Playing changes the resume generation (or Play/Stop the session).
        if (!samePlaybackClock || (!(paused && lastSeekWasPaused) && elapsed >= TimeSpan.FromSeconds(1)))
        {
            return nativeTime;
        }

        return lastSeekWasPaused ? lastTarget : ClampSeekTime(lastTarget, 0, elapsed);
    }

    internal static long ClampSeekTime(long currentTime, long length, TimeSpan offset)
    {
        // Decimal arithmetic also handles an extreme native timestamp combined
        // with TimeSpan.MaxValue without wrapping around to the beginning.
        var target = (decimal)Math.Max(0, currentTime) + offset.Ticks / TimeSpan.TicksPerMillisecond;
        // Seeking exactly to Length can end playback and discard player focus.
        var maximum = length > 0 ? length - 1 : long.MaxValue;
        return (long)Math.Clamp(target, 0, maximum);
    }
}
