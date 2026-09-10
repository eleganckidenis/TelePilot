using TelePilot.Core.Models;
using TelePilot.Epg.Models;
using System.Diagnostics.CodeAnalysis;

namespace TelePilot.Epg.Matching;

public enum EpgMatchMethod
{
    ExactTvgId,
    ExactNormalizedName,
    Manual,
}

public sealed record ChannelEpgMapping(
    Channel Channel,
    EpgChannel EpgChannel,
    EpgMatchMethod Method);

public sealed class ChannelEpgMatcher
{
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance API allows this stateless matcher to be registered and replaced as an application service.")]
    public ChannelEpgMapping? Match(
        Channel channel,
        IEnumerable<EpgChannel> epgChannels,
        IReadOnlyDictionary<ChannelId, string>? manualMappings = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(epgChannels);

        EpgChannel[] availableChannels = [.. epgChannels];

        EpgChannel? byId = FindUnique(
            availableChannels.Where(candidate =>
                channel.TvgId is not null &&
                candidate.Id.Equals(channel.TvgId, StringComparison.Ordinal)));
        if (byId is not null)
        {
            return new ChannelEpgMapping(channel, byId, EpgMatchMethod.ExactTvgId);
        }

        HashSet<string> normalizedNames = new(StringComparer.Ordinal);
        AddNormalizedName(normalizedNames, channel.TvgName);
        AddNormalizedName(normalizedNames, channel.Name);

        if (normalizedNames.Count > 0)
        {
            EpgChannel? byName = FindUnique(
                availableChannels.Where(candidate => candidate.DisplayNames.Any(displayName =>
                    normalizedNames.Contains(EpgNameNormalizer.Normalize(displayName.Value)))));
            if (byName is not null)
            {
                return new ChannelEpgMapping(channel, byName, EpgMatchMethod.ExactNormalizedName);
            }
        }

        if (manualMappings is not null &&
            manualMappings.TryGetValue(channel.Id, out string? mappedId) &&
            !string.IsNullOrWhiteSpace(mappedId))
        {
            EpgChannel? manual = FindUnique(
                availableChannels.Where(candidate =>
                    candidate.Id.Equals(mappedId, StringComparison.Ordinal)));
            if (manual is not null)
            {
                return new ChannelEpgMapping(channel, manual, EpgMatchMethod.Manual);
            }
        }

        return null;
    }

    public IReadOnlyList<ChannelEpgMapping> MatchAll(
        IEnumerable<Channel> channels,
        IEnumerable<EpgChannel> epgChannels,
        IReadOnlyDictionary<ChannelId, string>? manualMappings = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(epgChannels);

        EpgChannel[] availableChannels = [.. epgChannels];
        List<ChannelEpgMapping> result = [];
        foreach (Channel channel in channels)
        {
            ChannelEpgMapping? mapping = Match(channel, availableChannels, manualMappings);
            if (mapping is not null)
            {
                result.Add(mapping);
            }
        }

        return result.AsReadOnly();
    }

    private static EpgChannel? FindUnique(IEnumerable<EpgChannel> candidates)
    {
        using IEnumerator<EpgChannel> enumerator = candidates.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return null;
        }

        EpgChannel result = enumerator.Current;
        return enumerator.MoveNext() ? null : result;
    }

    private static void AddNormalizedName(HashSet<string> names, string? value)
    {
        string normalized = EpgNameNormalizer.Normalize(value);
        if (normalized.Length > 0)
        {
            names.Add(normalized);
        }
    }
}
