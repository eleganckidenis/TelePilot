namespace TelePilot.Epg.Models;

public enum EpgRefreshResult
{
    NoSources,
    Success,
    NotModified,
    Failed,
}

public sealed record EpgCacheMetadata
{
    public EpgCacheMetadata(
        string source,
        DateTimeOffset lastDownloadedAt,
        DateTimeOffset nextRefreshAt,
        EpgRefreshResult result,
        int channelCount,
        int programmeCount,
        string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (nextRefreshAt < lastDownloadedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextRefreshAt),
                nextRefreshAt,
                "Next refresh cannot precede the last download.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(channelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(programmeCount);

        Source = source.Trim();
        LastDownloadedAt = lastDownloadedAt;
        NextRefreshAt = nextRefreshAt;
        Result = result;
        ChannelCount = channelCount;
        ProgrammeCount = programmeCount;
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
    }

    public string Source { get; }

    public DateTimeOffset LastDownloadedAt { get; }

    public DateTimeOffset NextRefreshAt { get; }

    public EpgRefreshResult Result { get; }

    public int ChannelCount { get; }

    public int ProgrammeCount { get; }

    public string? Message { get; }
}
