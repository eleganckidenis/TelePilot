namespace TelePilot.Core.Models;

public enum PlaylistSourceKind
{
    File,
    Url,
}

public sealed class PlaylistSource
{
    public static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromSeconds(30);

    public PlaylistSource(
        PlaylistSourceId id,
        string name,
        PlaylistSourceKind kind,
        string location,
        bool refreshOnStartup = true,
        TimeSpan? downloadTimeout = null,
        string? relativeReferenceBaseLocation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        var effectiveTimeout = downloadTimeout ?? DefaultDownloadTimeout;
        if (effectiveTimeout <= TimeSpan.Zero || effectiveTimeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(downloadTimeout),
                "Limit czasu musi być większy od zera i nie może przekraczać godziny.");
        }

        Id = id;
        Name = name.Trim();
        Kind = kind;
        Location = location.Trim();
        RelativeReferenceBaseLocation = string.IsNullOrWhiteSpace(relativeReferenceBaseLocation)
            ? null
            : relativeReferenceBaseLocation.Trim();
        RefreshOnStartup = refreshOnStartup;
        DownloadTimeout = effectiveTimeout;
    }

    public PlaylistSourceId Id { get; }

    public string Name { get; }

    public PlaylistSourceKind Kind { get; }

    public string Location { get; }

    public string? RelativeReferenceBaseLocation { get; }

    public bool RefreshOnStartup { get; }

    public TimeSpan DownloadTimeout { get; }

    public Uri? GetResolutionBaseUri()
    {
        var candidate = RelativeReferenceBaseLocation ?? Location;
        if (Kind == PlaylistSourceKind.Url)
        {
            return Uri.TryCreate(candidate, UriKind.Absolute, out var remoteUri)
                ? remoteUri
                : null;
        }

        var resolved = TryCreateFileUri(candidate);
        return resolved ?? (RelativeReferenceBaseLocation is null ? null : TryCreateFileUri(Location));
    }

    public override string ToString() => Name;

    private static Uri? TryCreateFileUri(string path)
    {
        try
        {
            return new Uri(Path.GetFullPath(path), UriKind.Absolute);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
