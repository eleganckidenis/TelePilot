namespace TelePilot.Core.Models;

public sealed class LocalPlaylistSourceImport
{
    public LocalPlaylistSourceImport(
        PlaylistSourceId sourceId,
        string displayName,
        string originalPath,
        bool refreshOnStartup = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        SourceId = sourceId;
        DisplayName = displayName.Trim();
        OriginalPath = originalPath.Trim();
        RefreshOnStartup = refreshOnStartup;
    }

    public PlaylistSourceId SourceId { get; }

    public string DisplayName { get; }

    public string OriginalPath { get; }

    public bool RefreshOnStartup { get; }
}
