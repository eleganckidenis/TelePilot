using Microsoft.Extensions.Logging;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;

namespace TelePilot.Infrastructure;

public sealed class PlaybackAudioSettingsStore(
    string filePath,
    ILogger<JsonSettingsStore<PlaybackAudioSettings>> logger) : IPlaybackAudioSettingsStore, IDisposable
{
    private readonly JsonSettingsStore<PlaybackAudioSettings> store =
        new(filePath, static () => new PlaybackAudioSettings(), logger);

    public Task<PlaybackAudioSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        store.LoadAsync(cancellationToken);

    public Task SaveAsync(PlaybackAudioSettings settings, CancellationToken cancellationToken = default) =>
        store.SaveAsync(settings, cancellationToken);

    public void Dispose() => store.Dispose();
}
