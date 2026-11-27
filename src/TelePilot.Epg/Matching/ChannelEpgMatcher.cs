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
    public ChannelEpgMapping? Match(
        Channel channel,
        IEnumerable<EpgChannel> epgChannels,
        IReadOnlyDictionary<ChannelId, string>? manualMappings = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(epgChannels);

        return Match(channel, new ChannelIndex(epgChannels), manualMappings);
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance API allows the matcher to be supplied to the EPG service.")]
    internal ChannelEpgMapping? Match(
        Channel channel,
        ChannelIndex epgChannels,
        IReadOnlyDictionary<ChannelId, string>? manualMappings)
    {
        if (channel.TvgId is { } tvgId && epgChannels.FindUniqueId(tvgId) is { } byId)
        {
            return new ChannelEpgMapping(channel, byId, EpgMatchMethod.ExactTvgId);
        }

        if (epgChannels.FindUniqueName(channel.TvgName, channel.Name) is { } byName)
        {
            return new ChannelEpgMapping(channel, byName, EpgMatchMethod.ExactNormalizedName);
        }

        if (manualMappings is not null &&
            manualMappings.TryGetValue(channel.Id, out string? mappedId) &&
            !string.IsNullOrWhiteSpace(mappedId) &&
            epgChannels.FindUniqueId(mappedId) is { } manual)
        {
            return new ChannelEpgMapping(channel, manual, EpgMatchMethod.Manual);
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

        var availableChannels = new ChannelIndex(epgChannels);
        List<ChannelEpgMapping> result = [];
        foreach (Channel channel in channels)
        {
            ArgumentNullException.ThrowIfNull(channel);
            ChannelEpgMapping? mapping = Match(channel, availableChannels, manualMappings);
            if (mapping is not null)
            {
                result.Add(mapping);
            }
        }

        return result.AsReadOnly();
    }

    internal sealed class ChannelIndex
    {
        private readonly Dictionary<string, EpgChannel?> byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (EpgChannel? Channel, int Position)> byName = new(StringComparer.Ordinal);

        public ChannelIndex(IEnumerable<EpgChannel> channels, CancellationToken cancellationToken = default)
        {
            int position = 0;
            foreach (EpgChannel channel in channels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!byId.TryAdd(channel.Id, channel))
                {
                    byId[channel.Id] = null;
                }

                foreach (EpgLocalizedText displayName in channel.DisplayNames)
                {
                    string name = EpgNameNormalizer.Normalize(displayName.Value);
                    if (name.Length > 0 && !byName.TryAdd(name, (channel, position)) && byName[name].Position != position)
                    {
                        // Different channel entries make a name ambiguous. Aliases from one entry do not.
                        byName[name] = (null, -1);
                    }
                }

                position++;
            }
        }

        public EpgChannel? FindUniqueId(string id) => byId.GetValueOrDefault(id);

        public EpgChannel? FindUniqueName(string? tvgName, string name)
        {
            bool hasTvgMatch = byName.TryGetValue(EpgNameNormalizer.Normalize(tvgName), out var tvgMatch);
            bool hasNameMatch = byName.TryGetValue(EpgNameNormalizer.Normalize(name), out var nameMatch);
            if (hasTvgMatch && hasNameMatch && !ReferenceEquals(tvgMatch.Channel, nameMatch.Channel))
            {
                return null;
            }

            return hasTvgMatch ? tvgMatch.Channel : nameMatch.Channel;
        }
    }
}
