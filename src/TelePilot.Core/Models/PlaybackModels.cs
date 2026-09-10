namespace TelePilot.Core.Models;

public enum PlaybackState
{
    Idle,
    Connecting,
    Playing,
    Paused,
    Stopping,
    Error,
}

public enum MediaTrackKind
{
    Audio,
    Subtitle,
}

public enum PlaybackCommandStatus
{
    Succeeded,
    Unsupported,
    Rejected,
}

public sealed record PlaybackOptions(
    int NetworkCachingMilliseconds = 1_500,
    int MaximumReconnectAttempts = 3)
{
    public void Validate()
    {
        if (NetworkCachingMilliseconds is < 0 or > 60_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(NetworkCachingMilliseconds),
                "Bufor sieciowy musi mieścić się w zakresie od 0 do 60000 ms.");
        }

        if (MaximumReconnectAttempts is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumReconnectAttempts),
                "Liczba ponownych prób musi mieścić się w zakresie od 0 do 20.");
        }
    }
}

public sealed record MediaTrack(
    int EngineId,
    MediaTrackKind Kind,
    string Description,
    string? Language,
    bool IsSelected)
{
    public string AccessibleName
    {
        get
        {
            var language = string.IsNullOrWhiteSpace(Language) ? null : Language.Trim();
            var selected = IsSelected ? ", wybrana" : string.Empty;
            return language is null
                ? $"{Description}, identyfikator {EngineId}{selected}"
                : $"{Description}, język {language}, identyfikator {EngineId}{selected}";
        }
    }
}

public sealed record PlaybackError(
    string ChannelName,
    string Message,
    string? TechnicalCode = null);

public sealed record PlaybackSnapshot(
    PlaybackState State,
    ChannelId? ChannelId,
    string? ChannelName,
    int Volume,
    bool IsMuted,
    bool CanPause,
    string? StatusMessage = null,
    PlaybackError? Error = null)
{
    public static PlaybackSnapshot Initial { get; } = new(
        PlaybackState.Idle,
        null,
        null,
        100,
        false,
        false);
}

public sealed record PlaybackCommandResult(
    PlaybackCommandStatus Status,
    string? Message = null)
{
    public bool IsSuccess => Status == PlaybackCommandStatus.Succeeded;

    public static PlaybackCommandResult Success() => new(PlaybackCommandStatus.Succeeded);

    public static PlaybackCommandResult Unsupported(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new PlaybackCommandResult(PlaybackCommandStatus.Unsupported, message);
    }

    public static PlaybackCommandResult Rejected(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new PlaybackCommandResult(PlaybackCommandStatus.Rejected, message);
    }
}

public sealed class PlaybackStateChangedEventArgs(PlaybackSnapshot snapshot) : EventArgs
{
    public PlaybackSnapshot Snapshot { get; } = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}

public sealed class MediaTracksChangedEventArgs(
    MediaTrackKind kind,
    IReadOnlyList<MediaTrack> tracks) : EventArgs
{
    public MediaTrackKind Kind { get; } = kind;

    public IReadOnlyList<MediaTrack> Tracks { get; } =
        Array.AsReadOnly((tracks ?? throw new ArgumentNullException(nameof(tracks))).ToArray());
}
