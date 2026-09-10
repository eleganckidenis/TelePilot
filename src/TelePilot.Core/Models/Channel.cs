using System.Collections.ObjectModel;

namespace TelePilot.Core.Models;

public sealed class Channel
{
    public Channel(
        ChannelId id,
        PlaylistSourceId playlistSourceId,
        string name,
        string streamUrl,
        string? tvgId = null,
        string? tvgName = null,
        string? logoUrl = null,
        string? groupTitle = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamUrl);

        Id = id;
        PlaylistSourceId = playlistSourceId;
        Name = name.Trim();
        StreamUrl = streamUrl.Trim();
        TvgId = NormalizeOptional(tvgId);
        TvgName = NormalizeOptional(tvgName);
        LogoUrl = NormalizeOptional(logoUrl);
        GroupTitle = NormalizeOptional(groupTitle);
        Attributes = CopyAttributes(attributes);
    }

    public ChannelId Id { get; }

    public PlaylistSourceId PlaylistSourceId { get; }

    public string Name { get; }

    public string StreamUrl { get; }

    public string? TvgId { get; }

    public string? TvgName { get; }

    public string? LogoUrl { get; }

    public string? GroupTitle { get; }

    public IReadOnlyDictionary<string, string> Attributes { get; }

    public override string ToString() => Name;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ReadOnlyDictionary<string, string> CopyAttributes(
        IReadOnlyDictionary<string, string>? attributes)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (attributes is not null)
        {
            foreach (var pair in attributes)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    copy[pair.Key] = pair.Value ?? string.Empty;
                }
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
