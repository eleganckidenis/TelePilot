using LibVLCSharp.Shared;

namespace TelePilot.Playback;

public interface ILibVlcMediaPlayerProvider
{
    MediaPlayer MediaPlayer { get; }

    string LibVlcVersion { get; }
}
