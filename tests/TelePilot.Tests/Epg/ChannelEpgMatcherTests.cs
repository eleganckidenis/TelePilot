using TelePilot.Core.Models;
using TelePilot.Epg.Matching;
using TelePilot.Epg.Models;

namespace TelePilot.Tests.Epg;

public sealed class ChannelEpgMatcherTests
{
    [Fact]
    public void Match_ExactTvgIdWinsBeforeNameAndManualMapping()
    {
        Channel channel = CreateChannel("Shared name", tvgId: "by-id");
        EpgChannel byId = CreateEpgChannel("by-id", "Different name");
        EpgChannel byName = CreateEpgChannel("by-name", "Shared name");
        EpgChannel manual = CreateEpgChannel("manual", "Manual");
        Dictionary<ChannelId, string> mappings = new() { [channel.Id] = manual.Id };

        ChannelEpgMapping? result = new ChannelEpgMatcher().Match(
            channel,
            [byName, manual, byId],
            mappings);

        Assert.NotNull(result);
        Assert.Same(byId, result.EpgChannel);
        Assert.Equal(EpgMatchMethod.ExactTvgId, result.Method);
    }

    [Fact]
    public void Match_UsesOnlyExactNormalizedName()
    {
        Channel channel = CreateChannel("ŻÓŁĆ TV-1");
        EpgChannel expected = CreateEpgChannel("guide-one", "zolc tv 1");
        EpgChannel similar = CreateEpgChannel("guide-two", "zolc tv 10");

        ChannelEpgMapping? result = new ChannelEpgMatcher().Match(channel, [similar, expected]);

        Assert.NotNull(result);
        Assert.Same(expected, result.EpgChannel);
        Assert.Equal(EpgMatchMethod.ExactNormalizedName, result.Method);
    }

    [Fact]
    public void Match_RefusesAmbiguousNormalizedName()
    {
        Channel channel = CreateChannel("News 24");
        EpgChannel first = CreateEpgChannel("first", "News-24");
        EpgChannel second = CreateEpgChannel("second", "NEWS 24");

        ChannelEpgMapping? result = new ChannelEpgMatcher().Match(channel, [first, second]);

        Assert.Null(result);
    }

    [Fact]
    public void Match_DoesNotUseSubstringSimilarity()
    {
        Channel channel = CreateChannel("TVP 1");
        EpgChannel onlyPartial = CreateEpgChannel("guide", "TVP 1 HD");

        ChannelEpgMapping? result = new ChannelEpgMatcher().Match(channel, [onlyPartial]);

        Assert.Null(result);
    }

    [Fact]
    public void Match_UsesValidManualMappingAfterAutomaticStrategiesFail()
    {
        Channel channel = CreateChannel("Playlist name");
        EpgChannel expected = CreateEpgChannel("guide-id", "Guide name");
        Dictionary<ChannelId, string> mappings = new() { [channel.Id] = expected.Id };

        ChannelEpgMapping? result = new ChannelEpgMatcher().Match(channel, [expected], mappings);

        Assert.NotNull(result);
        Assert.Same(expected, result.EpgChannel);
        Assert.Equal(EpgMatchMethod.Manual, result.Method);
    }

    [Fact]
    public void Match_TreatsTvgIdAsCaseSensitive()
    {
        Channel channel = CreateChannel("No name match", tvgId: "Channel.One");
        EpgChannel differentCase = CreateEpgChannel("channel.one", "Different");

        ChannelEpgMapping? result = new ChannelEpgMatcher().Match(channel, [differentCase]);

        Assert.Null(result);
    }

    private static Channel CreateChannel(string name, string? tvgId = null, string? tvgName = null) =>
        new(
            ChannelId.New(),
            PlaylistSourceId.New(),
            name,
            "https://stream.example.invalid/live",
            tvgId,
            tvgName);

    private static EpgChannel CreateEpgChannel(string id, string name) =>
        new(id, [new EpgLocalizedText(name)]);
}
