using LibVLCSharp.Shared;

namespace TelePilot.Playback;

public interface ILibVlcMediaPlayerProvider
{
    Task PrepareAsync(CancellationToken cancellationToken = default);

    MediaPlayer MediaPlayer { get; }

    void SetVideoViewport(int width, int height);

    string LibVlcVersion { get; }
}
