using TelePilot.App.ViewModels;

namespace TelePilot.Tests.App;

public sealed class ChannelListItemViewModelTests
{
    [Fact]
    public void AccessibleName_PutsCurrentProgramBeforeGroupAndKeepsGroupLast()
    {
        ChannelListItemViewModel channel = new(
            "news-1",
            "Kanał informacyjny",
            "Informacyjne")
        {
            CurrentProgram = "Wiadomości dnia",
            PlaybackState = "odtwarzany",
        };

        Assert.Equal(
            "Kanał informacyjny, teraz: Wiadomości dnia, odtwarzany, grupa: Informacyjne",
            channel.AccessibleName);
    }
}
