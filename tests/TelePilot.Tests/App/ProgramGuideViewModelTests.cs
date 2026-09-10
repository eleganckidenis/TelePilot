using System.Globalization;
using TelePilot.App.ViewModels;

namespace TelePilot.Tests.App;

public sealed class ProgramGuideViewModelTests
{
    [Fact]
    public void Constructor_SelectsChannelSpecifiedByInitialChannelId()
    {
        ChannelListItemViewModel first = new("first", "Kanał pierwszy", "Testowe");
        ChannelListItemViewModel expected = new("expected", "Kanał właściwy", "Testowe");
        LoadRecorder loader = new();

        using ProgramGuideViewModel viewModel = CreateViewModel(
            [first, expected],
            loader,
            initialChannelId: expected.Id);

        Assert.Same(expected, viewModel.SelectedChannel);
    }

    [Fact]
    public async Task SelectedDate_DoesNotLoadUntilLoadCommandAndPassesDateOnlyOnce()
    {
        ChannelListItemViewModel channel = new("channel-1", "Kanał testowy", "Testowe");
        LoadRecorder loader = new();
        using ProgramGuideViewModel viewModel = CreateViewModel(
            [channel],
            loader,
            initialDate: LocalDate(2026, 7, 30));
        loader.Reset();

        viewModel.SelectedDate = new DateTime(2026, 8, 2);

        Assert.Equal(0, loader.CallCount);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, loader.CallCount);
        Assert.Equal(channel.Id, loader.LastChannelId);
        Assert.Equal(new DateOnly(2026, 8, 2), loader.LastDate);
    }

    [Fact]
    public async Task PreviousAndNextDayCommands_LoadExactlyOncePerCommand()
    {
        ChannelListItemViewModel channel = new("channel-1", "Kanał testowy", "Testowe");
        LoadRecorder loader = new();
        using ProgramGuideViewModel viewModel = CreateViewModel(
            [channel],
            loader,
            initialDate: LocalDate(2026, 7, 30));
        loader.Reset();

        await viewModel.PreviousDayCommand.ExecuteAsync(null);

        Assert.Equal(1, loader.CallCount);
        Assert.Equal(new DateOnly(2026, 7, 29), loader.LastDate);

        loader.Reset();
        await viewModel.NextDayCommand.ExecuteAsync(null);

        Assert.Equal(1, loader.CallCount);
        Assert.Equal(new DateOnly(2026, 7, 30), loader.LastDate);
    }

    [Fact]
    public async Task GoToNow_DoesNotLeaveFirstProgrammeSelectedWhenNoneIsCurrent()
    {
        ChannelListItemViewModel channel = new("channel-1", "Kanał testowy", "Testowe");
        LoadRecorder loader = new(
        [
            CreateProgramme("first", "Pierwsza audycja", isNow: false),
            CreateProgramme("second", "Druga audycja", isNow: false),
        ]);
        using ProgramGuideViewModel viewModel = CreateViewModel(
            [channel],
            loader,
            initialDate: LocalDate(2026, 7, 29));
        Assert.NotNull(viewModel.SelectedProgram);
        loader.Reset();

        await viewModel.GoToNowCommand.ExecuteAsync(null);

        Assert.Equal(1, loader.CallCount);
        Assert.Null(viewModel.SelectedProgram);
        Assert.Contains(
            "brak aktualnie emitowanej audycji",
            viewModel.StatusMessage,
            StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public async Task LoadCommand_StatusContainsSelectedDateAndChannelName()
    {
        ChannelListItemViewModel channel = new("channel-1", "Kanał informacyjny", "Testowe");
        LoadRecorder loader = new([CreateProgramme("programme", "Serwis", isNow: false)]);
        using ProgramGuideViewModel viewModel = CreateViewModel(
            [channel],
            loader,
            initialDate: LocalDate(2026, 7, 30));
        loader.Reset();

        await viewModel.LoadCommand.ExecuteAsync(null);

        string expectedDate = new DateTime(2026, 7, 30)
            .ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
        Assert.Contains(expectedDate, viewModel.StatusMessage, StringComparison.CurrentCulture);
        Assert.Contains(channel.Name, viewModel.StatusMessage, StringComparison.CurrentCulture);
    }

    private static ProgramGuideViewModel CreateViewModel(
        IEnumerable<ChannelListItemViewModel> channels,
        LoadRecorder loader,
        DateTimeOffset? initialDate = null,
        string? initialChannelId = null) =>
        new(
            channels,
            loader.LoadAsync,
            static (_, _) => Task.CompletedTask,
            static _ => Task.CompletedTask,
            initialDate,
            initialChannelId);

    private static ProgramItemViewModel CreateProgramme(string id, string title, bool isNow) =>
        new(
            id,
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 30, 13, 0, 0, TimeSpan.Zero),
            title,
            string.Empty,
            null,
            null,
            null,
            isNow);

    private static DateTimeOffset LocalDate(int year, int month, int day) =>
        new(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local));

    private sealed class LoadRecorder
    {
        private readonly IReadOnlyList<ProgramItemViewModel> programmes;

        public LoadRecorder(IReadOnlyList<ProgramItemViewModel>? programmes = null)
        {
            this.programmes = programmes ?? Array.Empty<ProgramItemViewModel>();
        }

        public int CallCount { get; private set; }

        public string? LastChannelId { get; private set; }

        public DateOnly? LastDate { get; private set; }

        public Task<IReadOnlyList<ProgramItemViewModel>> LoadAsync(
            string channelId,
            DateOnly date,
            string? search,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastChannelId = channelId;
            LastDate = date;
            return Task.FromResult(programmes);
        }

        public void Reset()
        {
            CallCount = 0;
            LastChannelId = null;
            LastDate = null;
        }
    }
}
