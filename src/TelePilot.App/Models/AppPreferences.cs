using TelePilot.Core.Channels;
using TelePilot.Recording;

namespace TelePilot.App.Models;

public sealed class AppPreferences
{
    public ChannelSortMode ChannelSortMode { get; init; } = ChannelSortMode.SourceOrder;

    public RecordingPreferences Recording { get; init; } = new();

    public List<FavoriteChannelPreference> Favorites { get; init; } = [];

    public static AppPreferences CreateDefault() => new()
    {
        Recording = RecordingPreferences.CreateDefault(),
    };
}

public sealed class RecordingPreferences
{
    public string OutputDirectory { get; init; } = string.Empty;

    public string? FfmpegPath { get; init; }

    public AudioOutputFormat AudioFormat { get; init; } = AudioOutputFormat.Mp3;

    public int AudioBitrateKbps { get; init; } = 192;

    public string FileNameTemplate { get; init; } = SafeFileNameGenerator.DefaultTemplate;

    public bool WakeComputerForScheduledRecording { get; init; }

    public static RecordingPreferences CreateDefault()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var outputDirectory = string.IsNullOrWhiteSpace(videos)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "TelePilot")
            : Path.Combine(videos, "TelePilot");
        return new RecordingPreferences { OutputDirectory = outputDirectory };
    }
}

public sealed record FavoriteChannelPreference(
    Guid PlaylistSourceId,
    Guid ChannelId,
    string ChannelName);
