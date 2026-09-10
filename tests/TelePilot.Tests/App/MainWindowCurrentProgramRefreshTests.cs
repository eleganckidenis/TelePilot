using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Headless.XUnit;
using TelePilot.App.Accessibility;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;
using TelePilot.Epg.Models;
using TelePilot.Epg.Services;

namespace TelePilot.Tests.App;

public sealed class MainWindowCurrentProgramRefreshTests
{
    [AvaloniaFact]
    public async Task CurrentProgramRefresh_UpdatesItemInPlaceWithoutSelectionFocusOrStatusNoise()
    {
        using var fixture = new Fixture("Pierwsza audycja");
        await fixture.ViewModel.InitializeAsync();

        ChannelListItemViewModel item = Assert.Single(fixture.ViewModel.Channels);
        Assert.Equal("Pierwsza audycja", item.CurrentProgram);
        Assert.Same(item, fixture.ViewModel.SelectedChannel);

        var itemChanges = new List<string?>();
        var collectionChanges = 0;
        item.PropertyChanged += (_, eventArgs) => itemChanges.Add(eventArgs.PropertyName);
        fixture.ViewModel.Channels.CollectionChanged += (_, _) => collectionChanges++;
        fixture.Status.Messages.Clear();
        fixture.FocusRequests.Clear();

        fixture.Epg.CurrentTitle = "Druga audycja";
        InvokeCurrentProgramRefresh(fixture.ViewModel);

        Assert.Same(item, Assert.Single(fixture.ViewModel.Channels));
        Assert.Same(item, fixture.ViewModel.SelectedChannel);
        Assert.Equal("Druga audycja", item.CurrentProgram);
        Assert.Equal([nameof(item.CurrentProgram), nameof(item.AccessibleName)], itemChanges);
        Assert.Equal(0, collectionChanges);
        Assert.Empty(fixture.FocusRequests);
        Assert.Empty(fixture.Status.Messages);

        InvokeCurrentProgramRefresh(fixture.ViewModel);

        Assert.Equal(2, itemChanges.Count);
        Assert.Equal(0, collectionChanges);
        Assert.Empty(fixture.FocusRequests);
        Assert.Empty(fixture.Status.Messages);
    }

    [AvaloniaFact]
    public async Task ProgramGuideRefresh_SynchronizesCurrentProgramOnMainList()
    {
        using var fixture = new Fixture("Stara audycja")
        {
            RefreshedTitle = "Nowa audycja",
        };
        await fixture.ViewModel.InitializeAsync();
        ChannelListItemViewModel item = Assert.Single(fixture.ViewModel.Channels);
        var collectionChanges = 0;
        fixture.ViewModel.Channels.CollectionChanged += (_, _) => collectionChanges++;

        await fixture.ViewModel.OpenProgramGuideCommand.ExecuteAsync(null);
        ProgramGuideViewModel guide = Assert.IsType<ProgramGuideViewModel>(fixture.Window.ProgramGuide);
        fixture.FocusRequests.Clear();

        await guide.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Epg.RefreshCallCount);
        Assert.Equal("Nowa audycja", item.CurrentProgram);
        Assert.Same(item, Assert.Single(fixture.ViewModel.Channels));
        Assert.Same(item, fixture.ViewModel.SelectedChannel);
        Assert.Equal(0, collectionChanges);
        Assert.Empty(fixture.FocusRequests);
    }

    [AvaloniaFact]
    public async Task ChannelFilter_LargeReorderRaisesSingleResetAndPreservesSelectionById()
    {
        using var fixture = new Fixture("Current show", channelCount: 2_000);
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        fixture.ViewModel.Channels.CollectionChanged +=
            (_, eventArgs) => collectionChanges.Add(eventArgs.Action);

        await fixture.ViewModel.InitializeAsync();

        Assert.Equal(2_000, fixture.ViewModel.Channels.Count);
        Assert.Equal([NotifyCollectionChangedAction.Reset], collectionChanges);
        ChannelListItemViewModel selected = fixture.ViewModel.Channels[1_500];
        fixture.ViewModel.SelectedChannel = selected;
        collectionChanges.Clear();

        fixture.ViewModel.SelectedSort = fixture.ViewModel.SortOptions[1];

        Assert.Equal([NotifyCollectionChangedAction.Reset], collectionChanges);
        Assert.Same(selected, fixture.ViewModel.SelectedChannel);
        Assert.Same(
            selected,
            Assert.Single(fixture.ViewModel.Channels, channel => channel.Id == selected.Id));
    }

    private static void InvokeCurrentProgramRefresh(MainWindowViewModel viewModel)
    {
        MethodInfo method = typeof(MainWindowViewModel).GetMethod(
            "RefreshCurrentProgramItems",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Current-program refresh method was not found.");
        method.Invoke(viewModel, [DateTimeOffset.Now]);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(string currentTitle, int channelCount = 1)
        {
            var sourceId = PlaylistSourceId.New();
            var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
            Channel[] channels = Enumerable.Range(0, channelCount)
                .Select(index => new Channel(
                    ChannelId.New(),
                    sourceId,
                    channelCount == 1 ? "Test channel" : $"Test channel {channelCount - index:D4}",
                    $"https://example.test/live/{index}",
                    groupTitle: "Test group"))
                .ToArray();
            var playlist = new PlaylistParseResult(
                new PlaylistHeader(),
                channels,
                [],
                "UTF-8",
                0);

            Epg = new MutableEpgSourceManager(currentTitle);
            Window = new CapturingWindowService();
            Status = new RecordingAccessibleStatusService();
            var focusCoordinator = new FocusCoordinator();
            focusCoordinator.FocusRequested += (_, request) => FocusRequests.Add(request);
            ViewModel = new MainWindowViewModel(
                new FixedPlaylistService(PlaylistLoadResult.Success(playlist)),
                new FixedPlaylistSourceStore(source),
                new NoOpPlaybackService(),
                Epg,
                Window,
                Status,
                focusCoordinator);
        }

        public MainWindowViewModel ViewModel { get; }

        public MutableEpgSourceManager Epg { get; }

        public CapturingWindowService Window { get; }

        public RecordingAccessibleStatusService Status { get; }

        public List<FocusRequest> FocusRequests { get; } = [];

        public string RefreshedTitle
        {
            set => Epg.RefreshedTitle = value;
        }

        public void Dispose()
        {
            Window.ProgramGuide?.Dispose();
            ViewModel.Dispose();
        }
    }

    private sealed class FixedPlaylistService(PlaylistLoadResult result) : IPlaylistService
    {
        public Task<PlaylistLoadResult> LoadAsync(
            PlaylistSource source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FixedPlaylistSourceStore(PlaylistSource source) : IPlaylistSourceStore
    {
        public Task<IReadOnlyList<PlaylistSource>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaylistSource>>([source]);

        public Task<PlaylistSource> ImportFileAsync(
            LocalPlaylistSourceImport import,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RefreshFileAsync(
            PlaylistSource source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(PlaylistSource source, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RemoveAsync(
            PlaylistSourceId sourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class MutableEpgSourceManager(string currentTitle) : IEpgSourceManager
    {
        public string CurrentTitle { get; set; } = currentTitle;

        public string RefreshedTitle { get; set; } = currentTitle;

        public int RefreshCallCount { get; private set; }

        public string Source => "test.xml";

        public IReadOnlyList<string> Sources => [Source];

        public EpgServiceStatus Status { get; } = new(EpgServiceState.Ready);

        public Task SetSourcesAsync(
            IEnumerable<string> sources,
            Uri? baseUri = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Programme>> GetProgrammesAsync(
            Channel channel,
            DateOnly scheduleDate,
            string? search = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Programme>>([CreateProgramme(channel)]);

        public Programme? GetCurrentProgramme(Channel channel, DateTimeOffset at) =>
            CreateProgramme(channel);

        public Task<EpgRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCallCount++;
            CurrentTitle = RefreshedTitle;
            return Task.FromResult(EpgRefreshResult.Success);
        }

        private Programme CreateProgramme(Channel channel) =>
            new(
                channel.Id.ToString(),
                DateTimeOffset.MinValue,
                null,
                [new EpgLocalizedText(CurrentTitle)]);
    }

    private sealed class CapturingWindowService : IWindowService
    {
        public ProgramGuideViewModel? ProgramGuide { get; private set; }

        public Task<IReadOnlyList<LocalPlaylistFileInput>> PickPlaylistFilesAsync() =>
            Task.FromResult<IReadOnlyList<LocalPlaylistFileInput>>([]);

        public Task<PlaylistSourceInput?> PromptForUrlSourceAsync() =>
            Task.FromResult<PlaylistSourceInput?>(null);

        public Task<bool> ShowConfirmationAsync(
            string title,
            string message,
            string confirmButtonText,
            string cancelButtonText = "Anuluj") =>
            Task.FromResult(false);

        public Task<int?> SelectTrackAsync(TrackSelectionViewModel viewModel) =>
            Task.FromResult<int?>(null);

        public Task ShowKeyboardShortcutsAsync() => Task.CompletedTask;

        public Task ShowProgramGuideAsync(ProgramGuideViewModel viewModel)
        {
            ProgramGuide = viewModel;
            return Task.CompletedTask;
        }

        public Task ShowMessageAsync(string title, string message, string buttonText = "OK") =>
            Task.CompletedTask;
    }

    private sealed class NoOpPlaybackService : IPlaybackService
    {
        public PlaybackSnapshot Current => PlaybackSnapshot.Initial;

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<MediaTracksChangedEventArgs>? TracksChanged
        {
            add { }
            remove { }
        }

        public Task<PlaybackCommandResult> PlayAsync(
            Channel channel,
            PlaybackOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaybackCommandResult.Success());

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PlaybackCommandResult> TogglePauseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaybackCommandResult.Success());

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MediaTrack>> GetTracksAsync(
            MediaTrackKind kind,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaTrack>>([]);

        public Task<PlaybackCommandResult> SelectTrackAsync(
            MediaTrackKind kind,
            int engineId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaybackCommandResult.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingAccessibleStatusService : IAccessibleStatusService
    {
        public string StatusMessage { get; private set; } = "Ready";

        public string AlertMessage { get; private set; } = string.Empty;

        public List<string> Messages { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public void ReportStatus(string message)
        {
            StatusMessage = message;
            Messages.Add(message);
        }

        public void ReportError(string message)
        {
            StatusMessage = message;
            AlertMessage = message;
            Messages.Add(message);
        }
    }
}
