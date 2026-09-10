using TelePilot.Core.Models;

namespace TelePilot.Tests.Core;

public sealed class PlaybackModelsTests
{
    [Theory]
    [InlineData(-1, 3)]
    [InlineData(60_001, 3)]
    [InlineData(1_500, -1)]
    [InlineData(1_500, 21)]
    public void PlaybackOptions_RejectsOutOfRangeValues(int caching, int attempts)
    {
        var options = new PlaybackOptions(caching, attempts);

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void MediaTrackAccessibleName_ContainsLanguageIdAndSelection()
    {
        var track = new MediaTrack(7, MediaTrackKind.Audio, "Dźwięk oryginalny", "pol", true);

        Assert.Equal(
            "Dźwięk oryginalny, język pol, identyfikator 7, wybrana",
            track.AccessibleName);
    }
}
