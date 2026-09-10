using TelePilot.App.Accessibility;
using TelePilot.App.ViewModels;

namespace TelePilot.Tests.App;

public sealed class ChannelPrefixSearchTests
{
    private static ChannelListItemViewModel[] CreateChannels() =>
    [
        new("1", "Canal+", "Test"),
        new("2", "Paramount", "Test"),
        new("3", "Polsat", "Test"),
        new("4", "Polsat News", "Test"),
        new("5", "Polo TV", "Test"),
        new("6", "Łódź TV", "Test"),
        new("7", "TVP 1", "Test"),
        new("8", "TVP 2", "Test"),
        new("9", "4fun", "Test"),
    ];

    [Fact]
    public void SuccessiveLetters_NarrowPrefixAndKeepMatchingSelectedRow()
    {
        var search = new ChannelPrefixSearch();
        var channels = CreateChannels();
        var selected = search.Find("p", channels, channels[0]);
        Assert.Equal("Paramount", selected?.Name);
        selected = search.Find("o", channels, selected);
        Assert.Equal("Polsat", selected?.Name);
        selected = search.Find("l", channels, selected);
        Assert.Equal("Polsat", selected?.Name);
        selected = search.Find("o", channels, selected);
        Assert.Equal("Polo TV", selected?.Name);
    }

    [Fact]
    public void RepeatedLetter_CyclesAndWraps()
    {
        var search = new ChannelPrefixSearch();
        var channels = CreateChannels();
        var selected = channels[0];
        foreach (var expected in new[] { "Paramount", "Polsat", "Polsat News", "Polo TV", "Paramount" })
        {
            selected = search.Find("P", channels, selected);
            Assert.Equal(expected, selected?.Name);
        }
    }

    [Theory]
    [InlineData("ŁÓ", "Łódź TV")]
    [InlineData("lo", "Łódź TV")]
    [InlineData("4", "4fun")]
    [InlineData("TVP 2", "TVP 2")]
    public void TextInput_SupportsAccentsDigitsSpacesAndMultipleCharacters(string text, string expected)
    {
        var channels = CreateChannels();
        Assert.Equal(expected, new ChannelPrefixSearch().Find(text, channels, channels[0])?.Name);
    }

    [Fact]
    public void Miss_DoesNotJumpToUnrelatedChannelAndBackspaceCorrectsPrefix()
    {
        var search = new ChannelPrefixSearch();
        var channels = CreateChannels();
        var selected = search.Find("pol", channels, channels[0]);
        Assert.Null(search.Find("x", channels, selected));
        Assert.Same(selected, search.Find(string.Empty, channels, selected, backspace: true));
        Assert.Equal("Polo TV", search.Find("o", channels, selected)?.Name);
    }

    [Fact]
    public void Timeout_Reset_AndManualSelection_StartNewSearch()
    {
        var clock = new SearchClock();
        var search = new ChannelPrefixSearch(clock);
        var channels = CreateChannels();
        var selected = search.Find("p", channels, channels[0]);
        clock.Advance(ChannelPrefixSearch.Timeout);
        selected = search.Find("l", channels, selected);
        Assert.Equal("Łódź TV", selected?.Name);
        search.Reset();
        selected = search.Find("p", channels, selected);
        Assert.Equal("Paramount", selected?.Name);
        Assert.Equal("Łódź TV", search.Find("l", channels, channels[3])?.Name);
    }

    [Fact]
    public void Search_UsesOnlyVisibleItemsAndHandlesEmptyList()
    {
        var channels = CreateChannels();
        var search = new ChannelPrefixSearch();
        Assert.Null(search.Find("p", [], null));
        search.Reset();
        Assert.Equal("Polsat News", search.Find("pol", [channels[3]], null)?.Name);
    }

    private sealed class SearchClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
    }
}
