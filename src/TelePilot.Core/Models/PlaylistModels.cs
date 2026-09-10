using System.Collections.ObjectModel;

namespace TelePilot.Core.Models;

public enum PlaylistDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public enum PlaylistDiagnosticCode
{
    MissingExtendedHeader,
    MalformedHeader,
    MalformedAttribute,
    MalformedExtInf,
    EntryWithoutStream,
    RelativeUrlWithoutBase,
    InvalidStreamUrl,
    GeneratedChannelName,
    InvalidEpgUrl,
    InvalidDeclaredEncoding,
    FallbackEncodingUsed,
}

public sealed record PlaylistDiagnostic(
    PlaylistDiagnosticCode Code,
    PlaylistDiagnosticSeverity Severity,
    int? LineNumber,
    string Message);

public sealed class PlaylistHeader
{
    public PlaylistHeader(
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<string>? epgUrls = null)
    {
        Attributes = CopyAttributes(attributes);
        EpgUrls = Array.AsReadOnly(epgUrls?.ToArray() ?? []);
    }

    public IReadOnlyDictionary<string, string> Attributes { get; }

    public IReadOnlyList<string> EpgUrls { get; }

    public override string ToString() => $"Nagłówek playlisty, źródła EPG: {EpgUrls.Count}";

    private static ReadOnlyDictionary<string, string> CopyAttributes(
        IReadOnlyDictionary<string, string>? attributes)
    {
        var copy = attributes is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
        return new ReadOnlyDictionary<string, string>(copy);
    }
}

public sealed record PlaylistParseContext(
    PlaylistSourceId SourceId,
    Uri? BaseUri = null,
    string? DeclaredEncoding = null);

public sealed class PlaylistParseResult
{
    public PlaylistParseResult(
        PlaylistHeader header,
        IReadOnlyList<Channel> channels,
        IReadOnlyList<PlaylistDiagnostic> diagnostics,
        string detectedEncoding,
        int skippedEntryCount)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectedEncoding);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedEntryCount);

        Header = header;
        Channels = Array.AsReadOnly(channels.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        DetectedEncoding = detectedEncoding;
        SkippedEntryCount = skippedEntryCount;
    }

    public PlaylistHeader Header { get; }

    public IReadOnlyList<Channel> Channels { get; }

    public IReadOnlyList<PlaylistDiagnostic> Diagnostics { get; }

    public string DetectedEncoding { get; }

    public int SkippedEntryCount { get; }

    public bool HasErrors => Diagnostics.Any(static item =>
        item.Severity == PlaylistDiagnosticSeverity.Error);

    public override string ToString() =>
        $"Kanały: {Channels.Count}, pominięte wpisy: {SkippedEntryCount}, diagnostyka: {Diagnostics.Count}";
}

public enum PlaylistLoadErrorCode
{
    InvalidSource,
    FileNotFound,
    AccessDenied,
    ReadFailure,
    NetworkFailure,
    HttpFailure,
    InsecureRedirect,
    Timeout,
    ContentTooLarge,
}

public sealed record PlaylistLoadError(
    PlaylistLoadErrorCode Code,
    string Message,
    int? HttpStatusCode = null);

public sealed class PlaylistLoadResult
{
    private PlaylistLoadResult(PlaylistParseResult? playlist, PlaylistLoadError? error)
    {
        Playlist = playlist;
        Error = error;
    }

    public bool IsSuccess => Playlist is not null;

    public PlaylistParseResult? Playlist { get; }

    public PlaylistLoadError? Error { get; }

    public static PlaylistLoadResult Success(PlaylistParseResult playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        return new PlaylistLoadResult(playlist, null);
    }

    public static PlaylistLoadResult Failure(PlaylistLoadError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new PlaylistLoadResult(null, error);
    }
}
