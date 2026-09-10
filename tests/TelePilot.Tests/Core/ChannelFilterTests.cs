using TelePilot.Core.Channels;
using TelePilot.Core.Models;

namespace TelePilot.Tests.Core;

public sealed class ChannelFilterTests
{
    private static readonly PlaylistSourceId SourceId = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void Apply_SearchesWithoutCaseOrPolishDiacritics()
    {
        var channels = new[]
        {
            CreateChannel(1, "Łódź Wiadomości", "Regionalne"),
            CreateChannel(2, "Kraków", "Regionalne"),
        };

        var result = ChannelFilter.Apply(
            channels,
            new ChannelFilterCriteria(SearchText: "LODZ"));

        var channel = Assert.Single(result);
        Assert.Equal("Łódź Wiadomości", channel.Name);
    }

    [Fact]
    public void Apply_SearchesTvgNameIdentifierAndGroup()
    {
        var channel = new Channel(
            ChannelId.New(),
            SourceId,
            "Kanał pierwszy",
            "https://example.invalid/stream",
            tvgId: "station-one.pl",
            tvgName: "Program Pierwszy HD",
            groupTitle: "Polskie");

        Assert.Single(ChannelFilter.Apply(
            [channel],
            new ChannelFilterCriteria(SearchText: "pierwszy hd")));
        Assert.Single(ChannelFilter.Apply(
            [channel],
            new ChannelFilterCriteria(SearchText: "station-one")));
        Assert.Single(ChannelFilter.Apply(
            [channel],
            new ChannelFilterCriteria(SearchText: "polskie")));
    }

    [Fact]
    public void Apply_FiltersGroupByNormalizedExactName()
    {
        var channels = new[]
        {
            CreateChannel(1, "A", "Dziecięce"),
            CreateChannel(2, "B", "Dzieciece Plus"),
        };

        var result = ChannelFilter.Apply(
            channels,
            new ChannelFilterCriteria(GroupTitle: "dzieciece"));

        var channel = Assert.Single(result);
        Assert.Equal("A", channel.Name);
    }

    [Fact]
    public void Apply_SortsByGroupThenNameAndDoesNotMutateInput()
    {
        var channels = new[]
        {
            CreateChannel(1, "Żuraw", "B"),
            CreateChannel(2, "Alfa", "A"),
            CreateChannel(3, "Beta", "A"),
        };

        var result = ChannelFilter.Apply(
            channels,
            new ChannelFilterCriteria(SortMode: ChannelSortMode.GroupThenName));

        Assert.Equal(["Alfa", "Beta", "Żuraw"], result.Select(static item => item.Name));
        Assert.Equal(["Żuraw", "Alfa", "Beta"], channels.Select(static item => item.Name));
    }

    private static Channel CreateChannel(int number, string name, string group) => new(
        ChannelId.CreateStable(SourceId, number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        SourceId,
        name,
        $"https://example.invalid/{number}",
        groupTitle: group);
}
