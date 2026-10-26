using LibVLCSharp.Shared;

namespace TelePilot.Playback;

public interface ILibVlcMediaPlayerProvider
{
    Task PrepareAsync(CancellationToken cancellationToken = default);

    MediaPlayer MediaPlayer { get; }

    string LibVlcVersion { get; }
}
