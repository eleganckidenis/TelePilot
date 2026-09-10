using System.ComponentModel;
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

public sealed class MainWindowSourceRemovalTests
{
    [AvaloniaFact]
    public async Task AddFileSources_WhenSecondImportFailsKeepsFirstSelectedAndReportsSafeSummary()
    {
        const string firstPath = "C:\\prywatne\\pierwsza.m3u";
        const string failingPath = "C:\\prywatne\\sekretna-druga.m3u";
        var store = new MutablePlaylistSourceStore
        {
            FailingImportPath = failingPath,
        };
        var status = new RecordingAccessibleStatusService();
        var windowService = new ConfirmationWindowService(
            confirmResult: false,
            pickedFiles:
            [
                new LocalPlaylistFileInput("Pierwsza.m3u", firstPath),
                new LocalPlaylistFileInput("Druga.m3u", failingPath),
            ]);

        using var viewModel = new MainWindowViewModel(
            new SourceAwarePlaylistService(),
            store,
            new NoOpPlaybackService(),
            new EmptyEpgSourceManager(),
            windowService,
            status,
            new FocusCoordinator());

        var exception = await Record.ExceptionAsync(() => viewModel.AddFileSourceCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Single(store.ImportedFiles);
        Assert.Single(viewModel.Sources);
        Assert.Equal("Pierwsza", viewModel.SelectedSource?.Name);
        Assert.Equal("Pierwsza", viewModel.Sources[0].Name);
        Assert.Contains("Dodano playlisty: 1", status.AlertMessage, StringComparison.Ordinal);
        Assert.Contains("Nie udało się dodać: 1", status.AlertMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(firstPath, status.AlertMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(failingPath, status.AlertMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task AddFileSources_WhenEveryImportFailsKeepsExistingSelectionAndChannels()
    {
        const string failingPath = "C:\\prywatne\\niedostępna.m3u";
        var existing = new PlaylistSource(
            PlaylistSourceId.New(),
            "Istniejące źródło",
            PlaylistSourceKind.Url,
            "https://example.test/list.m3u");
        var store = new MutablePlaylistSourceStore(existing)
        {
            FailingImportPath = failingPath,
        };
        var status = new RecordingAccessibleStatusService();
        using var viewModel = new MainWindowViewModel(
            new SourceAwarePlaylistService(),
            store,
            new NoOpPlaybackService(),
            new EmptyEpgSourceManager(),
            new ConfirmationWindowService(
                confirmResult: false,
                pickedFiles: [new LocalPlaylistFileInput("Błędna.m3u", failingPath)]),
            status,
            new FocusCoordinator());
        await viewModel.InitializeAsync();
        var channelBeforeImport = Assert.Single(viewModel.Channels);

        var exception = await Record.ExceptionAsync(() => viewModel.AddFileSourceCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Equal(existing.Id, viewModel.SelectedSource?.Source.Id);
        Assert.Single(viewModel.Sources);
        Assert.Same(channelBeforeImport, Assert.Single(viewModel.Channels));
        Assert.Contains("Nie udało się dodać żadnej", status.AlertMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(failingPath, status.AlertMessage, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task RefreshSource_RefreshesManagedFileBeforeReloadingPlaylist()
    {
        var source = new PlaylistSource(
            PlaylistSourceId.New(),
            "Lista domowa",
            PlaylistSourceKind.File,
            "C:\\TelePilot\\managed.m3u",
            relativeReferenceBaseLocation: "C:\\Playlisty\\oryginal.m3u");
        var operations = new List<string>();
        var store = new MutablePlaylistSourceStore(source)
        {
            RefreshAction = _ => operations.Add("refresh"),
        };
        var playlistService = new SourceAwarePlaylistService
        {
            LoadAction = _ => operations.Add("load"),
        };
        using var viewModel = new MainWindowViewModel(
            playlistService,
            store,
            new NoOpPlaybackService(),
            new EmptyEpgSourceManager(),
            new ConfirmationWindowService(confirmResult: false),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator());
        await viewModel.InitializeAsync();
        operations.Clear();

        await viewModel.RefreshSourceCommand.ExecuteAsync(null);

        Assert.Equal(["refresh", "load"], operations);
        Assert.Equal([source.Id], store.RefreshedSourceIds);
        Assert.Equal([source.Id, source.Id], playlistService.LoadedSourceIds);
    }

    [AvaloniaFact]
    public async Task RefreshSource_WhenManagedCopyRefreshFailsKeepsChannelsAndReportsExistingError()
    {
        var source = new PlaylistSource(
            PlaylistSourceId.New(),
            "Lista domowa",
            PlaylistSourceKind.File,
            "C:\\TelePilot\\managed.m3u",
            relativeReferenceBaseLocation: "C:\\Playlisty\\oryginal.m3u");
        var store = new MutablePlaylistSourceStore(source)
        {
            RefreshException = new IOException("Testowy błąd ponownego kopiowania."),
        };
        var playlistService = new SourceAwarePlaylistService();
        var windowService = new ConfirmationWindowService(confirmResult: false);
        var status = new RecordingAccessibleStatusService();
        using var viewModel = new MainWindowViewModel(
            playlistService,
            store,
            new NoOpPlaybackService(),
            new EmptyEpgSourceManager(),
            windowService,
            status,
            new FocusCoordinator());
        await viewModel.InitializeAsync();
        var channelBeforeRefresh = Assert.Single(viewModel.Channels);

        var exception = await Record.ExceptionAsync(
            () => viewModel.RefreshSourceCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Same(channelBeforeRefresh, Assert.Single(viewModel.Channels));
        Assert.Single(playlistService.LoadedSourceIds);
        Assert.Contains("Nie udało się wczytać źródła", status.AlertMessage, StringComparison.Ordinal);
        Assert.Contains("Testowy błąd ponownego kopiowania", windowService.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task RemoveSource_AfterConfirmationRemovesOnlyEntryLoadsNextAndRequestsSelectorFocus()
    {
        var first = new PlaylistSource(
            PlaylistSourceId.New(),
            "Pierwsze źródło",
            PlaylistSourceKind.Url,
            "https://example.test/list.m3u?token=sekret");
        var second = new PlaylistSource(
            PlaylistSourceId.New(),
            "Drugie źródło",
            PlaylistSourceKind.File,
            "C:\\playlisty\\druga.m3u");
        var store = new MutablePlaylistSourceStore(first, second);
        var playlistService = new SourceAwarePlaylistService();
        var windowService = new ConfirmationWindowService(confirmResult: true);
        var focusCoordinator = new FocusCoordinator();
        var focusRequests = new List<FocusRequest>();
        focusCoordinator.FocusRequested += (_, request) => focusRequests.Add(request);

        using var viewModel = new MainWindowViewModel(
            playlistService,
            store,
            new NoOpPlaybackService(),
            new EmptyEpgSourceManager(),
            windowService,
            new RecordingAccessibleStatusService(),
            focusCoordinator);

        await viewModel.InitializeAsync();
        Assert.Equal(first.Id, viewModel.SelectedSource?.Source.Id);

        await viewModel.RemoveSourceCommand.ExecuteAsync(null);

        Assert.Equal([first.Id], store.RemovedSourceIds);
        Assert.DoesNotContain(first.Location, windowService.ConfirmationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sekret", windowService.ConfirmationMessage, StringComparison.Ordinal);
        Assert.Single(viewModel.Sources);
        Assert.Equal(second.Id, viewModel.SelectedSource?.Source.Id);
        Assert.Equal([first.Id, second.Id], playlistService.LoadedSourceIds);
        Assert.Single(viewModel.Channels);
        Assert.Contains("Drugie źródło", viewModel.Channels[0].Name, StringComparison.Ordinal);
        Assert.Equal(FocusTarget.PlaylistSourceSelector, focusRequests.Last().Target);
    }

    [AvaloniaFact]
    public async Task RemoveSource_WhenConfirmationIsCancelledLeavesSourceAndPlaylistUntouched()
    {
        var source = new PlaylistSource(
            PlaylistSourceId.New(),
            "Domowa",
            PlaylistSourceKind.File,
            "C:\\playlisty\\domowa.m3u");
        var store = new MutablePlaylistSourceStore(source);
        var playlistService = new SourceAwarePlaylistService();

        using var viewModel = new MainWindowViewModel(
            playlistService,
            store,
            new NoOpPlaybackService(),
            new EmptyEpgSourceManager(),
            new ConfirmationWindowService(confirmResult: false),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator());

        await viewModel.InitializeAsync();
        await viewModel.RemoveSourceCommand.ExecuteAsync(null);

        Assert.Empty(store.RemovedSourceIds);
        Assert.Single(viewModel.Sources);
        Assert.Equal(source.Id, viewModel.SelectedSource?.Source.Id);
        Assert.Single(playlistService.LoadedSourceIds);
    }

    [AvaloniaFact]
    public async Task RemoveSource_WhenNextSourceFailsClearsRemovedChannelsAndKeepsPlaybackRunning()
    {
        var first = new PlaylistSource(
            PlaylistSourceId.New(),
            "Pierwsze źródło",
            PlaylistSourceKind.File,
            "C:\\playlisty\\pierwsza.m3u");
        var second = new PlaylistSource(
            PlaylistSourceId.New(),
            "Uszkodzone źródło",
            PlaylistSourceKind.File,
            "C:\\playlisty\\uszkodzona.m3u");
        var playlistService = new SourceAwarePlaylistService(second.Id);
        var playbackService = new NoOpPlaybackService();
        var epgService = new EmptyEpgSourceManager();

        using var viewModel = new MainWindowViewModel(
            playlistService,
            new MutablePlaylistSourceStore(first, second),
            playbackService,
            epgService,
            new ConfirmationWindowService(confirmResult: true),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator());

        await viewModel.InitializeAsync();
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsPlayerViewVisible);

        await viewModel.RemoveSourceCommand.ExecuteAsync(null);

        Assert.Equal(second.Id, viewModel.SelectedSource?.Source.Id);
        Assert.Empty(viewModel.Channels);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal([MainWindowViewModel.AllGroupsLabel], viewModel.Groups);
        Assert.Equal([first.Id, second.Id], playlistService.LoadedSourceIds);
        Assert.Empty(epgService.SetSourceCalls.Last());
        Assert.Equal(0, playbackService.StopCallCount);
        Assert.True(viewModel.IsPlayerViewVisible);
        Assert.True(viewModel.IsChannelListViewVisible);
    }

    [AvaloniaFact]
    public async Task RemoveSource_WhenLastSourceIsRemovedClearsChannelsAndEpg()
    {
        var source = new PlaylistSource(
            PlaylistSourceId.New(),
            "Ostatnie źródło",
            PlaylistSourceKind.File,
            "C:\\playlisty\\ostatnia.m3u");
        var epgService = new EmptyEpgSourceManager();

        using var viewModel = new MainWindowViewModel(
            new SourceAwarePlaylistService(),
            new MutablePlaylistSourceStore(source),
            new NoOpPlaybackService(),
            epgService,
            new ConfirmationWindowService(confirmResult: true),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator());

        await viewModel.InitializeAsync();
        Assert.Single(viewModel.Channels);
        Assert.Single(epgService.SetSourceCalls);
        Assert.NotEmpty(epgService.SetSourceCalls[0]);

        await viewModel.RemoveSourceCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Sources);
        Assert.Null(viewModel.SelectedSource);
        Assert.Empty(viewModel.Channels);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Equal([MainWindowViewModel.AllGroupsLabel], viewModel.Groups);
        Assert.Equal(2, epgService.SetSourceCalls.Count);
        Assert.Empty(epgService.SetSourceCalls[^1]);
        Assert.False(viewModel.RemoveSourceCommand.CanExecute(null));
    }

    private sealed class MutablePlaylistSourceStore(params PlaylistSource[] sources) : IPlaylistSourceStore
    {
        private readonly List<PlaylistSource> sourceItems = [.. sources];

        public List<PlaylistSourceId> RemovedSourceIds { get; } = [];

        public List<LocalPlaylistSourceImport> ImportedFiles { get; } = [];

        public List<PlaylistSourceId> RefreshedSourceIds { get; } = [];

        public string? FailingImportPath { get; init; }

        public Action<PlaylistSource>? RefreshAction { get; init; }

        public Exception? RefreshException { get; init; }

        public Task<IReadOnlyList<PlaylistSource>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaylistSource>>([.. sourceItems]);

        public Task<PlaylistSource> ImportFileAsync(
            LocalPlaylistSourceImport import,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(import.OriginalPath, FailingImportPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Testowy błąd dla {import.OriginalPath}");
            }

            ImportedFiles.Add(import);
            var source = new PlaylistSource(
                import.SourceId,
                import.DisplayName,
                PlaylistSourceKind.File,
                Path.Combine(Path.GetTempPath(), "TelePilot.Tests", $"{import.SourceId.Value:N}.m3u"),
                import.RefreshOnStartup,
                relativeReferenceBaseLocation: import.OriginalPath);
            sourceItems.Add(source);
            return Task.FromResult(source);
        }

        public Task RefreshFileAsync(
            PlaylistSource source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshedSourceIds.Add(source.Id);
            RefreshAction?.Invoke(source);
            return RefreshException is null
                ? Task.CompletedTask
                : Task.FromException(RefreshException);
        }

        public Task SaveAsync(PlaylistSource source, CancellationToken cancellationToken = default)
        {
            sourceItems.RemoveAll(candidate => candidate.Id == source.Id);
            sourceItems.Add(source);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(PlaylistSourceId sourceId, CancellationToken cancellationToken = default)
        {
            var removed = sourceItems.RemoveAll(source => source.Id == sourceId) > 0;
            if (removed)
            {
                RemovedSourceIds.Add(sourceId);
            }

            return Task.FromResult(removed);
        }
    }

    private sealed class SourceAwarePlaylistService(PlaylistSourceId? failingSourceId = null) : IPlaylistService
    {
        public List<PlaylistSourceId> LoadedSourceIds { get; } = [];

        public Action<PlaylistSource>? LoadAction { get; init; }

        public Task<PlaylistLoadResult> LoadAsync(
            PlaylistSource source,
            CancellationToken cancellationToken = default)
        {
            LoadedSourceIds.Add(source.Id);
            LoadAction?.Invoke(source);
            if (failingSourceId == source.Id)
            {
                return Task.FromResult(PlaylistLoadResult.Failure(new PlaylistLoadError(
                    PlaylistLoadErrorCode.InvalidSource,
                    "Testowy błąd wczytywania następnego źródła.")));
            }

            var channel = new Channel(
                ChannelId.New(),
                source.Id,
                $"Kanał {source.Name}",
                "https://stream.example.test/live");
            var playlist = new PlaylistParseResult(
                new PlaylistHeader(epgUrls: ["https://epg.example.test/guide.xml"]),
                [channel],
                [],
                "UTF-8",
                0);
            return Task.FromResult(PlaylistLoadResult.Success(playlist));
        }
    }

    private sealed class ConfirmationWindowService(
        bool confirmResult,
        IReadOnlyList<LocalPlaylistFileInput>? pickedFiles = null) : IWindowService
    {
        public string ConfirmationMessage { get; private set; } = string.Empty;

        public string Message { get; private set; } = string.Empty;

        public Task<IReadOnlyList<LocalPlaylistFileInput>> PickPlaylistFilesAsync() =>
            Task.FromResult(pickedFiles ?? (IReadOnlyList<LocalPlaylistFileInput>)[]);

        public Task<PlaylistSourceInput?> PromptForUrlSourceAsync() =>
            Task.FromResult<PlaylistSourceInput?>(null);

        public Task<bool> ShowConfirmationAsync(
            string title,
            string message,
            string confirmButtonText,
            string cancelButtonText = "Anuluj")
        {
            ConfirmationMessage = message;
            return Task.FromResult(confirmResult);
        }

        public Task<int?> SelectTrackAsync(TrackSelectionViewModel viewModel) =>
            Task.FromResult<int?>(null);

        public Task ShowKeyboardShortcutsAsync() => Task.CompletedTask;

        public Task ShowProgramGuideAsync(ProgramGuideViewModel viewModel) => Task.CompletedTask;

        public Task ShowMessageAsync(string title, string message, string buttonText = "OK")
        {
            Message = message;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpPlaybackService : IPlaybackService
    {
        public PlaybackSnapshot Current => PlaybackSnapshot.Initial;

        public int StopCallCount { get; private set; }

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

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            return Task.CompletedTask;
        }

        public Task<PlaybackCommandResult> TogglePauseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaybackCommandResult.Success());

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default) => Task.CompletedTask;

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

    private sealed class EmptyEpgSourceManager : IEpgSourceManager
    {
        public List<IReadOnlyList<string>> SetSourceCalls { get; } = [];

        public string Source => string.Empty;

        public IReadOnlyList<string> Sources => [];

        public EpgServiceStatus Status { get; } = new(EpgServiceState.Empty);

        public Task SetSourcesAsync(
            IEnumerable<string> sources,
            Uri? baseUri = null,
            CancellationToken cancellationToken = default)
        {
            SetSourceCalls.Add(sources.ToArray());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Programme>> GetProgrammesAsync(
            Channel channel,
            DateOnly scheduleDate,
            string? search = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Programme>>([]);

        public Programme? GetCurrentProgramme(Channel channel, DateTimeOffset at) => null;

        public Task<EpgRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EpgRefreshResult.NoSources);
    }

    private sealed class RecordingAccessibleStatusService : IAccessibleStatusService
    {
        public string StatusMessage { get; private set; } = string.Empty;

        public string AlertMessage { get; private set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void ReportStatus(string message)
        {
            StatusMessage = message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        }

        public void ReportError(string message)
        {
            StatusMessage = message;
            AlertMessage = message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlertMessage)));
        }
    }
}
