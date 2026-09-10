using TelePilot.Core.Models;

namespace TelePilot.Core.Channels;

public enum ChannelSortMode
{
    SourceOrder,
    Name,
    GroupThenName,
}

public sealed record ChannelFilterCriteria(
    string? SearchText = null,
    string? GroupTitle = null,
    ChannelSortMode SortMode = ChannelSortMode.SourceOrder);

public static class ChannelFilter
{
    public static IReadOnlyList<Channel> Apply(
        IEnumerable<Channel> channels,
        ChannelFilterCriteria? criteria = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        criteria ??= new ChannelFilterCriteria();

        var search = ChannelTextNormalizer.NormalizeForSearch(criteria.SearchText);
        var group = ChannelTextNormalizer.NormalizeForSearch(criteria.GroupTitle);

        var filtered = channels.Where(channel =>
            MatchesGroup(channel, group) && MatchesSearch(channel, search));

        filtered = criteria.SortMode switch
        {
            ChannelSortMode.SourceOrder => filtered,
            ChannelSortMode.Name => filtered.OrderBy(
                static channel => ChannelTextNormalizer.NormalizeForSearch(channel.Name),
                StringComparer.Ordinal),
            ChannelSortMode.GroupThenName => filtered
                .OrderBy(
                    static channel => ChannelTextNormalizer.NormalizeForSearch(channel.GroupTitle),
                    StringComparer.Ordinal)
                .ThenBy(
                    static channel => ChannelTextNormalizer.NormalizeForSearch(channel.Name),
                    StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(
                nameof(criteria),
                criteria.SortMode,
                "Nieznany sposób sortowania kanałów."),
        };

        return Array.AsReadOnly(filtered.ToArray());
    }

    private static bool MatchesGroup(Channel channel, string normalizedGroup) =>
        normalizedGroup.Length == 0 ||
        string.Equals(
            ChannelTextNormalizer.NormalizeForSearch(channel.GroupTitle),
            normalizedGroup,
            StringComparison.Ordinal);

    private static bool MatchesSearch(Channel channel, string normalizedSearch)
    {
        if (normalizedSearch.Length == 0)
        {
            return true;
        }

        return Contains(channel.Name, normalizedSearch) ||
               Contains(channel.TvgName, normalizedSearch) ||
               Contains(channel.TvgId, normalizedSearch) ||
               Contains(channel.GroupTitle, normalizedSearch);
    }

    private static bool Contains(string? candidate, string normalizedSearch) =>
        ChannelTextNormalizer.NormalizeForSearch(candidate)
            .Contains(normalizedSearch, StringComparison.Ordinal);
}
