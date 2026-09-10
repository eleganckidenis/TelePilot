using TelePilot.Core.Models;
using TelePilot.Epg.Models;

namespace TelePilot.Epg.Services;

public enum EpgServiceState
{
    Empty,
    Refreshing,
    Ready,
    Failed,
}

public sealed record EpgServiceStatus(
    EpgServiceState State,
    EpgCacheMetadata? CacheMetadata = null,
    string? ErrorMessage = null);

public interface IEpgService
{
    string Source { get; }

    EpgServiceStatus Status { get; }

    Task<IReadOnlyList<Programme>> GetProgrammesAsync(
        Channel channel,
        DateOnly scheduleDate,
        string? search = null,
        CancellationToken cancellationToken = default);

    Programme? GetCurrentProgramme(Channel channel, DateTimeOffset at);

    Task<EpgRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
}

public interface IEpgSourceManager : IEpgService
{
    IReadOnlyList<string> Sources { get; }

    Task SetSourcesAsync(
        IEnumerable<string> sources,
        Uri? baseUri = null,
        CancellationToken cancellationToken = default);
}
