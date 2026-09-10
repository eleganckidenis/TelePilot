using TelePilot.Core.Models;

namespace TelePilot.Core.Contracts;

public interface IPlaybackService : IAsyncDisposable
{
    PlaybackSnapshot Current { get; }

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    event EventHandler<MediaTracksChangedEventArgs>? TracksChanged;

    Task<PlaybackCommandResult> PlayAsync(
        Channel channel,
        PlaybackOptions options,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<PlaybackCommandResult> TogglePauseAsync(
        CancellationToken cancellationToken = default);

    Task SetVolumeAsync(
        int volume,
        CancellationToken cancellationToken = default);

    Task SetMutedAsync(
        bool isMuted,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaTrack>> GetTracksAsync(
        MediaTrackKind kind,
        CancellationToken cancellationToken = default);

    Task<PlaybackCommandResult> SelectTrackAsync(
        MediaTrackKind kind,
        int engineId,
        CancellationToken cancellationToken = default);
}
