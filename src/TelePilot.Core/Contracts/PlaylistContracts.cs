using TelePilot.Core.Models;

namespace TelePilot.Core.Contracts;

public interface IM3uPlaylistParser
{
    PlaylistParseResult Parse(
        ReadOnlyMemory<byte> content,
        PlaylistParseContext context,
        CancellationToken cancellationToken = default);
}

public interface IPlaylistLoader
{
    Task<PlaylistLoadResult> LoadAsync(
        PlaylistSource source,
        CancellationToken cancellationToken = default);
}

public interface IPlaylistService
{
    Task<PlaylistLoadResult> LoadAsync(
        PlaylistSource source,
        CancellationToken cancellationToken = default);
}

public interface IPlaylistSourceStore
{
    Task<IReadOnlyList<PlaylistSource>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<PlaylistSource> ImportFileAsync(
        LocalPlaylistSourceImport import,
        CancellationToken cancellationToken = default);

    Task RefreshFileAsync(
        PlaylistSource source,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PlaylistSource source,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        PlaylistSourceId sourceId,
        CancellationToken cancellationToken = default);
}
