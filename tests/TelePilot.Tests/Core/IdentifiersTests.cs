using TelePilot.Core.Models;

namespace TelePilot.Tests.Core;

public sealed class IdentifiersTests
{
    [Fact]
    public void CreateStable_ReturnsSameIdForSameSourceKeyAndOccurrence()
    {
        var source = new PlaylistSourceId(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        var first = ChannelId.CreateStable(source, "tvg-id:example", 0);
        var second = ChannelId.CreateStable(source, "tvg-id:example", 0);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateStable_DistinguishesDuplicatesAndSources()
    {
        var firstSource = new PlaylistSourceId(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var secondSource = new PlaylistSourceId(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        var first = ChannelId.CreateStable(firstSource, "same", 0);
        var duplicate = ChannelId.CreateStable(firstSource, "same", 1);
        var otherSource = ChannelId.CreateStable(secondSource, "same", 0);

        Assert.NotEqual(first, duplicate);
        Assert.NotEqual(first, otherSource);
    }

    [Fact]
    public void EmptyIdentifiersAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ChannelId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new PlaylistSourceId(Guid.Empty));
    }
}
