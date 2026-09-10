using System.ComponentModel;
using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TelePilot.App.Accessibility;
using TelePilot.App.Controls;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;
using TelePilot.Epg.Models;
using TelePilot.Epg.Services;
using TelePilot.Infrastructure;
using TelePilot.Recording;

namespace TelePilot.Tests.App;

public sealed class MainWindowPlaybackNavigationTests
{
    [AvaloniaFact]
    public async Task ControlL_DuringPlayerActivationRetainsListFocusAfterPlaybackStarts()
    {
        var coordinator = new FocusCoordinator();
        using var model = CreateSearchViewModel(coordinator);
        var window = new MainWindow { DataContext = model };
        var focusHandler = typeof(MainWindow).GetMethod("OnFocusRequested", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<EventHandler<FocusRequest>>(window);
        coordinator.FocusRequested += focusHandler;
        try
        {
            window.Show();
            window.Activate();
            await model.InitializeAsync();
            var play = model.PlaySelectedCommand.ExecuteAsync(null);
            model.ToggleChannelListCommand.Execute(null);
            await play;
            Dispatcher.UIThread.RunJobs();

            Assert.True(model.IsChannelListViewVisible);
            Assert.True(model.IsPlayerViewVisible);
            Assert.True(window.FindControl<ListBox>("ChannelList")!.IsKeyboardFocusWithin);
        }
        finally
        {
            coordinator.FocusRequested -= focusHandler;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RapidPageNavigationAndVideoOutputChangesKeepPlayerControlsFocused()
    {
        var coordinator = new FocusCoordinator();
        using var model = CreateSearchViewModel(coordinator);
        var window = new MainWindow { DataContext = model };
        var focusHandler = typeof(MainWindow).GetMethod("OnFocusRequested", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<EventHandler<FocusRequest>>(window);
        var videoOutputChanged = typeof(MainWindow).GetMethod("QueueVideoOutputFocusRecovery", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<Action>(window);
        coordinator.FocusRequested += focusHandler;
        try
        {
            window.Show();
            window.Activate();
            await model.InitializeAsync();
            await model.PlaySelectedCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            for (var cycle = 0; cycle < 20; cycle++)
            {
                var key = cycle % 2 == 0 ? Key.PageDown : Key.PageUp;
                window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                var command = cycle % 2 == 0 ? model.NextChannelCommand : model.PreviousChannelCommand;
                await command.ExecutionTask!;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(cycle % 2 == 0 ? "Paramount" : "Canal+", model.CurrentChannelName);

                // A burst from VLC after changing channels must preserve the
                // control reached by Tab and Shift+Tab, including its UIA peer.
                var modifiers = cycle % 2 == 0 ? RawInputModifiers.None : RawInputModifiers.Shift;
                window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, null);
                window.KeyRelease(Key.Tab, modifiers, PhysicalKey.Tab, null);
                var chosen = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
                var peer = ControlAutomationPeer.CreatePeerForElement(chosen);
                for (var output = 0; output < 5; output++)
                {
                    videoOutputChanged();
                }
                Dispatcher.UIThread.RunJobs();
                Assert.Same(chosen, window.FocusManager.GetFocusedElement());
                Assert.True(peer.HasKeyboardFocus());
                Assert.True(window.FindControl<Grid>("PlayerView")!.IsKeyboardFocusWithin);
            }

            // A native event can arrive long after the last status checkpoint.
            await Task.Delay(350, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            window.FocusManager!.Focus(null);
            videoOutputChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(window.FindControl<Button>("PauseResumeButton"), window.FocusManager.GetFocusedElement());

            // A queued native event must respect a newer Ctrl+L view change.
            videoOutputChanged();
            model.ToggleChannelListCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<ListBox>("ChannelList")!.IsKeyboardFocusWithin);
            Assert.True(model.IsChannelListViewVisible);
            model.ToggleChannelListCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Grid>("PlayerView")!.IsKeyboardFocusWithin);
        }
        finally
        {
            coordinator.FocusRequested -= focusHandler;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ControlL_TogglesListWithoutRestartingPlaybackAndPreservesListContainers()
    {
        var coordinator = new FocusCoordinator();
        using var model = CreateSearchViewModel(coordinator);
        var window = new MainWindow { DataContext = model };
        var focusHandler = typeof(MainWindow).GetMethod("OnFocusRequested", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<EventHandler<FocusRequest>>(window);
        coordinator.FocusRequested += focusHandler;
        try
        {
            window.Show();
            window.Activate();
            await model.InitializeAsync();
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("ChannelList")!;
            var channel = model.SelectedChannel!;
            var row = list.ContainerFromItem(channel);
            var playerActivations = 0;
            model.PlayerViewRequested += (_, _) => playerActivations++;

            PressControlL(window);
            Assert.True(model.IsChannelListViewVisible);
            Assert.False(model.IsPlayerViewVisible);
            await model.PlaySelectedCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            for (var cycle = 0; cycle < 3; cycle++)
            {
                PressControlL(window);
                Assert.True(model.IsChannelListViewVisible);
                Assert.True(model.IsPlayerViewVisible);
                Assert.Same(row, window.FocusManager!.GetFocusedElement());
                PressControlL(window);
                Assert.False(model.IsChannelListViewVisible);
                Assert.True(model.IsPlayerViewVisible);
                Assert.Same(window.FindControl<Button>("PauseResumeButton"), window.FocusManager!.GetFocusedElement());
                PressWindowTab(window);
                Assert.True(window.FindControl<Grid>("PlayerView")!.IsKeyboardFocusWithin);
            }

            Assert.Equal(1, playerActivations);
            Assert.Same(row, list.ContainerFromItem(channel));
            await model.StopCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            PressControlL(window);
            Assert.True(model.IsChannelListViewVisible);
            Assert.False(model.IsPlayerViewVisible);
        }
        finally
        {
            coordinator.FocusRequested -= focusHandler;
            window.Close();
        }
    }

    private static void PressControlL(Window window)
    {
        window.KeyPress(Key.L, RawInputModifiers.Control, PhysicalKey.L, "l");
        window.KeyRelease(Key.L, RawInputModifiers.Control, PhysicalKey.L, "l");
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("fokus", 1)]
    [InlineData("polsat sport 1", 1)]
    [InlineData("polsat", 2)]
    public async Task FilteredChannel_EnterAfterTabAndAfterStopFocusesPlayerAndPreservesNavigation(string query, int count)
    {
        var coordinator = new FocusCoordinator();
        using var model = CreateSearchViewModel(coordinator, "Fokus", "Polsat Sport 1", "Polsat Sport 2", "TVP 1");
        var window = new MainWindow { DataContext = model };
        var focusHandler = typeof(MainWindow).GetMethod("OnFocusRequested", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<EventHandler<FocusRequest>>(window);
        coordinator.FocusRequested += focusHandler;
        try
        {
            window.Show();
            window.Activate();
            await model.InitializeAsync();
            Dispatcher.UIThread.RunJobs();
            var search = window.FindControl<AccessibleSearchBox>("SearchBox")!;
            Assert.True(search.FocusEditor());
            window.KeyTextInput(query);
            await Task.Delay(400, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(count, model.Channels.Count);

            PressWindowTab(window);
            PressWindowTab(window);
            var list = window.FindControl<ListBox>("ChannelList")!;
            var pause = window.FindControl<Button>("PauseResumeButton")!;
            for (var cycle = 0; cycle < 2; cycle++)
            {
                Assert.True(list.IsKeyboardFocusWithin);
                var channel = model.SelectedChannel!;
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(channel));
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await model.PlaySelectedCommand.ExecutionTask!;
                Dispatcher.UIThread.RunJobs();

                Assert.Same(pause, window.FocusManager?.GetFocusedElement());
                Assert.True(ControlAutomationPeer.CreatePeerForElement(pause).HasKeyboardFocus());
                Assert.True(model.IsPlayerViewVisible);
                PressWindowTab(window);
                var nextControl = window.FocusManager?.GetFocusedElement();
                Assert.NotSame(pause, nextControl);
                Assert.IsType<Button>(nextControl);
                await Task.Delay(400, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(nextControl, window.FocusManager?.GetFocusedElement());

                await model.StopCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(row, window.FocusManager?.GetFocusedElement());
                Assert.Same(row, list.ContainerFromItem(channel));
                Assert.Equal(query, model.SearchText);
                Assert.Equal(query, search.Text);
                Assert.Equal(count, model.Channels.Count);
                if (cycle == 0 && count > 1)
                {
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    Dispatcher.UIThread.RunJobs();
                    Assert.NotSame(channel, model.SelectedChannel);
                }
            }
        }
        finally
        {
            coordinator.FocusRequested -= focusHandler;
            window.Close();
        }
    }

    private static void PressWindowTab(Window window)
    {
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task ChannelTextInput_FocusesMatchOutsideRealizedRowsEvenAfterUnmatchedCharacter()
    {
        string[] names = [.. Enumerable.Range(1, 150).Select(index => $"Kanał {index}"), "Polsat"];
        using var model = CreateSearchViewModel(names);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show();
            window.Activate();
            await model.InitializeAsync();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var list = window.FindControl<ListBox>("ChannelList")!;
            Assert.Null(list.ContainerFromItem(model.Channels[^1]));
            Assert.True(((ListBoxItem)list.ContainerFromItem(model.SelectedChannel!)!).Focus());
            window.KeyTextInput("pol");
            window.KeyTextInput("x");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Polsat", model.SelectedChannel?.Name);
            Assert.True(((ListBoxItem)list.ContainerFromItem(model.SelectedChannel!)!).IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ChannelTextInput_NarrowsPrefixFocusesStandardRowAndDoesNotStealTabFocus()
    {
        using var model = CreateSearchViewModel();
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show();
            window.Activate();
            await model.InitializeAsync();
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("ChannelList")!;
            window.UpdateLayout();
            Assert.True(((ListBoxItem)list.ContainerFromItem(model.SelectedChannel!)!).Focus());
            Assert.True(list.IsKeyboardFocusWithin);
            window.KeyTextInput("p");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Paramount", model.SelectedChannel?.Name);
            window.KeyTextInput("o");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Polsat", model.SelectedChannel?.Name);
            window.KeyTextInput("l");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Polsat", model.SelectedChannel?.Name);
            var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(model.SelectedChannel!));
            Assert.True(row.IsFocused);
            var peer = ControlAutomationPeer.CreatePeerForElement(row)!;
            Assert.True(peer.GetProvider<ISelectionItemProvider>()!.IsSelected);
            Assert.StartsWith("Polsat", peer.GetName(), StringComparison.Ordinal);

            // Queue a new match and leave the list before its focus callback runs.
            window.KeyTextInput("o");
            var button = window.FindControl<Grid>("ChannelActionsPanel")!.Children.OfType<Button>().First();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsFocused);
            Assert.Equal("Polo TV", model.SelectedChannel?.Name);

            Assert.True(((ListBoxItem)list.ContainerFromItem(model.SelectedChannel!)!).Focus());
            window.KeyTextInput("pol");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Polsat", model.SelectedChannel?.Name);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SupersededPlaylistLoad_CannotPublishOldChannelsOrClearNewLoadingState()
    {
        var source = new PlaylistSource(PlaylistSourceId.New(), "Test", PlaylistSourceKind.File, "test.m3u");
        var loader = new DelayedPlaylistService();
        var status = new RecordingAccessibleStatusService();
        using var model = new MainWindowViewModel(loader, new FixedPlaylistSourceStore(source),
            new ImmediatePlaybackService(), new EmptyEpgSourceManager(), new NoOpWindowService(), status, new FocusCoordinator());
        var firstLoad = model.InitializeAsync();
        var secondLoad = model.InitializeAsync();
        Assert.Equal(2, loader.Loads.Count);
        loader.Loads[0].SetResult(CreatePlaylist(source, "Stary kanał"));
        await firstLoad;
        Assert.True(model.IsLoading);
        Assert.Empty(model.Channels);
        Assert.DoesNotContain("Anulowano", status.StatusMessage, StringComparison.Ordinal);
        loader.Loads[1].SetResult(CreatePlaylist(source, "Nowy kanał"));
        await secondLoad;
        Assert.False(model.IsLoading);
        Assert.Equal("Nowy kanał", Assert.Single(model.Channels).Name);
    }

    private static PlaylistLoadResult CreatePlaylist(PlaylistSource source, params string[] names) =>
        PlaylistLoadResult.Success(new PlaylistParseResult(new PlaylistHeader(),
            names.Select(name => new Channel(ChannelId.New(), source.Id, name, "https://example.invalid/live")).ToArray(),
            [], "UTF-8", 0));

    private static MainWindowViewModel CreateSearchViewModel(params string[] names)
        => CreateSearchViewModel(new FocusCoordinator(), names);

    private static MainWindowViewModel CreateSearchViewModel(IFocusCoordinator coordinator, params string[] names)
    {
        var source = new PlaylistSource(PlaylistSourceId.New(), "Test", PlaylistSourceKind.File, "test.m3u");
        return new MainWindowViewModel(new FixedPlaylistService(CreatePlaylist(source,
            names.Length > 0 ? names : ["Canal+", "Paramount", "Polsat", "Polsat News", "Polo TV"])),
            new FixedPlaylistSourceStore(source), new ImmediatePlaybackService(), new EmptyEpgSourceManager(),
            new NoOpWindowService(), new RecordingAccessibleStatusService(), coordinator);
    }

    private sealed class DelayedPlaylistService : IPlaylistService
    {
        public List<TaskCompletionSource<PlaylistLoadResult>> Loads { get; } = [];
        public Task<PlaylistLoadResult> LoadAsync(PlaylistSource source, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<PlaylistLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Loads.Add(completion);
            return completion.Task;
        }
    }

    [AvaloniaFact]
    public Task ClosingDuringRecording_CancelKeepsRecordingActive() =>
        VerifyRecordingCloseAsync(confirmationResult: false, expectedStopCalls: 0);

    [AvaloniaFact]
    public Task ClosingDuringRecording_ConfirmationStopsRecording() =>
        VerifyRecordingCloseAsync(confirmationResult: true, expectedStopCalls: 1);

    private static async Task VerifyRecordingCloseAsync(
        bool confirmationResult,
        int expectedStopCalls)
    {
        var recording = new ControllableRecordingCoordinator();
        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(new PlaylistParseResult(
                new PlaylistHeader(),
                [],
                [],
                "UTF-8",
                0))),
            new FixedPlaylistSourceStore(new PlaylistSource(
                PlaylistSourceId.New(),
                "Test playlist",
                PlaylistSourceKind.File,
                "test.m3u")),
            new ImmediatePlaybackService(),
            new EmptyEpgSourceManager(),
            new NoOpWindowService(confirmationResult),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator(),
            recordingCoordinator: recording);
        recording.Publish(RecordingState.Recording);
        Dispatcher.UIThread.RunJobs();

        var canClose = await viewModel.ConfirmCloseAsync();

        Assert.Equal(confirmationResult, canClose);
        Assert.Equal(expectedStopCalls, recording.StopCallCount);
    }

    [AvaloniaFact]
    public async Task Favorites_AreLoadedPersistedAndFilterTheExistingChannelList()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        var favoriteChannel = new Channel(ChannelId.New(), sourceId, "TTV", "https://example.test/1");
        var otherChannel = new Channel(ChannelId.New(), sourceId, "TVP 1", "https://example.test/2");
        var playlist = new PlaylistParseResult(
            new PlaylistHeader(),
            [favoriteChannel, otherChannel],
            [],
            "UTF-8",
            0);
        var preferenceStore = new MemoryPreferenceStore(new AppPreferences
        {
            Favorites =
            [
                new FavoriteChannelPreference(
                    sourceId.Value,
                    favoriteChannel.Id.Value,
                    favoriteChannel.Name),
            ],
        });

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(playlist)),
            new FixedPlaylistSourceStore(source),
            new ImmediatePlaybackService(),
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator(),
            new AppPreferencesService(preferenceStore));

        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.Channels.Count);
        Assert.True(Assert.Single(viewModel.Channels, item => item.Id == favoriteChannel.Id.ToString()).IsFavorite);
        viewModel.ToggleFavoritesFilterCommand.Execute(null);
        Assert.Equal(favoriteChannel.Id.ToString(), Assert.Single(viewModel.Channels).Id);

        await viewModel.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Channels);
        Assert.Empty(preferenceStore.Settings.Favorites);
    }

    [AvaloniaFact]
    public async Task SearchText_DebouncesChannelListRebuilds()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        var channel = new Channel(
            ChannelId.New(),
            sourceId,
            "Test channel",
            "https://example.test/live",
            groupTitle: "Test group");
        var playlist = new PlaylistParseResult(
            new PlaylistHeader(),
            [channel],
            [],
            "UTF-8",
            0);

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(playlist)),
            new FixedPlaylistSourceStore(source),
            new ControllablePlaybackService(),
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator());

        await viewModel.InitializeAsync();
        Assert.Single(viewModel.Channels);

        viewModel.SearchText = "brak takiego kanału";

        // Wpisywanie nie przebudowuje listy synchronicznie po każdym znaku.
        Assert.Single(viewModel.Channels);

        await Task.Delay(TimeSpan.FromMilliseconds(400));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(viewModel.Channels);
    }

    [AvaloniaFact]
    public async Task ChannelLetterJump_SelectsNextMatchingChannelAndWraps()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        Channel[] channels =
        [
            new(ChannelId.New(), sourceId, "Canal+", "https://example.test/1"),
            new(ChannelId.New(), sourceId, "TTV", "https://example.test/2"),
            new(ChannelId.New(), sourceId, "TVP 1", "https://example.test/3"),
            new(ChannelId.New(), sourceId, "TTV 2", "https://example.test/4"),
            new(ChannelId.New(), sourceId, "Łódź TV", "https://example.test/5"),
        ];

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(
                new PlaylistParseResult(new PlaylistHeader(), channels, [], "UTF-8", 0))),
            new FixedPlaylistSourceStore(source),
            new ImmediatePlaybackService(),
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator());

        await viewModel.InitializeAsync();
        viewModel.SelectedChannel = Assert.Single(viewModel.Channels, channel => channel.Name == "Canal+");

        Assert.Equal("TTV", viewModel.SelectNextChannelStartingWith("t")?.Name);
        Assert.Equal("TVP 1", viewModel.SelectNextChannelStartingWith("T")?.Name);
        Assert.Equal("TTV 2", viewModel.SelectNextChannelStartingWith("t")?.Name);
        Assert.Equal("TTV", viewModel.SelectNextChannelStartingWith("t")?.Name);
        Assert.Equal("Łódź TV", viewModel.SelectNextChannelStartingWith("l")?.Name);

        ChannelListItemViewModel selectedBeforeMiss = viewModel.SelectedChannel!;
        Assert.Null(viewModel.SelectNextChannelStartingWith("x"));
        Assert.Same(selectedBeforeMiss, viewModel.SelectedChannel);
    }

    [AvaloniaFact]
    public async Task ReturnToChannelList_WhilePlaybackCommandIsPending_RestoresFocusAndKeepsControlsResponsive()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        var channel = new Channel(
            ChannelId.New(),
            sourceId,
            "Test channel",
            "https://example.test/live",
            groupTitle: "Test group");
        var playlist = new PlaylistParseResult(
            new PlaylistHeader(),
            [channel],
            [],
            "UTF-8",
            0);
        var playback = new ControllablePlaybackService();
        var focusCoordinator = new FocusCoordinator();
        var focusRequests = new List<FocusRequest>();
        var accessibleStatus = new RecordingAccessibleStatusService();
        focusCoordinator.FocusRequested += (_, request) => focusRequests.Add(request);

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(playlist)),
            new FixedPlaylistSourceStore(source),
            playback,
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            accessibleStatus,
            focusCoordinator);

        await viewModel.InitializeAsync();
        Assert.Equal(channel.Id.ToString(), viewModel.SelectedChannel?.Id);

        var playTask = viewModel.PlaySelectedCommand.ExecuteAsync(null);
        await playback.PlayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsPlayerViewVisible);
        Assert.False(viewModel.IsChannelListViewVisible);
        Assert.Equal(PlaybackState.Playing, playback.Current.State);
        Assert.Contains(
            focusRequests,
            request => request.Target == FocusTarget.PlayerControls
                && request.PreserveExistingFocus);

        viewModel.ReturnToChannelListCommand.Execute(null);

        Assert.True(viewModel.IsChannelListViewVisible);
        Assert.True(viewModel.IsPlayerViewVisible);
        Assert.Equal(1, viewModel.ChannelListColumnSpan);
        Assert.Equal(1, viewModel.PlayerColumn);
        Assert.Equal(1, viewModel.PlayerColumnSpan);
        var focusRequest = Assert.IsType<FocusRequest>(focusRequests.LastOrDefault());
        Assert.Equal(FocusTarget.ChannelList, focusRequest.Target);
        Assert.Equal(channel.Id.ToString(), focusRequest.ItemId);
        Assert.Equal(0, playback.StopCallCount);
        Assert.Equal(PlaybackState.Playing, playback.Current.State);

        await viewModel.VolumeDownCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([60], playback.SetVolumeCalls);
        Assert.Equal("Głośność 60 procent.", accessibleStatus.StatusMessage);

        playback.CompletePlay();
        await playTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [AvaloniaFact]
    public async Task PreviousAndNextChannelCommandsUseVisibleListOrderAndDoNotWrap()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        Channel[] channels =
        [
            new(ChannelId.New(), sourceId, "Kanał pierwszy", "https://example.test/1"),
            new(ChannelId.New(), sourceId, "Kanał drugi", "https://example.test/2"),
            new(ChannelId.New(), sourceId, "Kanał trzeci", "https://example.test/3"),
        ];
        var playlist = new PlaylistParseResult(new PlaylistHeader(), channels, [], "UTF-8", 0);
        var playback = new ImmediatePlaybackService();
        var status = new RecordingAccessibleStatusService();

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(playlist)),
            new FixedPlaylistSourceStore(source),
            playback,
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            status,
            new FocusCoordinator());

        await viewModel.InitializeAsync();
        viewModel.SelectedChannel = viewModel.Channels[1];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        await viewModel.NextChannelCommand.ExecuteAsync(null);

        Assert.Equal("Kanał trzeci", viewModel.SelectedChannel?.Name);
        Assert.Equal(["Kanał drugi", "Kanał trzeci"], playback.PlayedChannelNames);

        await viewModel.NextChannelCommand.ExecuteAsync(null);
        Assert.Equal(["Kanał drugi", "Kanał trzeci"], playback.PlayedChannelNames);
        Assert.Equal("To ostatni kanał na liście.", status.StatusMessage);

        await viewModel.PreviousChannelCommand.ExecuteAsync(null);
        Assert.Equal("Kanał drugi", viewModel.SelectedChannel?.Name);
        Assert.Equal(["Kanał drugi", "Kanał trzeci", "Kanał drugi"], playback.PlayedChannelNames);
    }

    [AvaloniaFact]
    public async Task StopThenPlayAnotherChannelRequestsACompleteSecondPlayerActivation()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        Channel[] channels =
        [
            new(ChannelId.New(), sourceId, "Kanał pierwszy", "https://example.test/1"),
            new(ChannelId.New(), sourceId, "Kanał drugi", "https://example.test/2"),
        ];
        var playlist = new PlaylistParseResult(new PlaylistHeader(), channels, [], "UTF-8", 0);
        var playback = new ImmediatePlaybackService();
        var focusCoordinator = new FocusCoordinator();
        var focusRequests = new List<FocusRequest>();
        var playerViewActivationCount = 0;
        focusCoordinator.FocusRequested += (_, request) => focusRequests.Add(request);

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(playlist)),
            new FixedPlaylistSourceStore(source),
            playback,
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            new RecordingAccessibleStatusService(),
            focusCoordinator);
        viewModel.PlayerViewRequested += (_, _) => playerViewActivationCount++;

        await viewModel.InitializeAsync();
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        await viewModel.StopCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsChannelListViewVisible);
        Assert.False(viewModel.IsPlayerViewVisible);

        viewModel.SelectedChannel = viewModel.Channels[1];
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, playerViewActivationCount);
        Assert.Equal(["Kanał pierwszy", "Kanał drugi"], playback.PlayedChannelNames);
        Assert.True(viewModel.IsPlayerViewVisible);
        Assert.False(viewModel.IsChannelListViewVisible);

        var forcedPlayerRequests = focusRequests
            .Select((request, index) => (request, index))
            .Where(entry => entry.request.Target == FocusTarget.PlayerControls
                && !entry.request.PreserveExistingFocus)
            .ToArray();
        Assert.Equal(2, forcedPlayerRequests.Length);
        Assert.Contains(
            focusRequests
                .Skip(forcedPlayerRequests[0].index + 1)
                .Take(forcedPlayerRequests[1].index - forcedPlayerRequests[0].index - 1),
            request => request.Target == FocusTarget.ChannelList);
    }

    [AvaloniaFact]
    public async Task RepeatedStopsRecoverThenVerifyFocusWithoutDuplicateLogicalTransitionAndAnnounceChannel()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        var channel = new Channel(
            ChannelId.New(),
            sourceId,
            "Kanał pierwszy",
            "https://example.test/1");
        var playback = new ImmediatePlaybackService();
        var focusCoordinator = new FocusCoordinator();
        var status = new RecordingAccessibleStatusService();
        MainWindowViewModel? viewModel = null;
        var stopFocusStates = new List<bool>();
        var stopFocusRequests = new List<FocusRequest>();
        focusCoordinator.FocusRequested += (_, request) =>
        {
            if (request.Target == FocusTarget.ChannelList && viewModel is not null)
            {
                stopFocusStates.Add(viewModel.IsPlayerViewVisible);
                stopFocusRequests.Add(request);
            }
        };

        using (viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(new PlaylistParseResult(
                new PlaylistHeader(),
                [channel],
                [],
                "UTF-8",
                0))),
            new FixedPlaylistSourceStore(source),
            playback,
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            status,
            focusCoordinator))
        {
            await viewModel.InitializeAsync();

            for (var cycle = 0; cycle < 2; cycle++)
            {
                await viewModel.PlaySelectedCommand.ExecuteAsync(null);
                stopFocusStates.Clear();
                stopFocusRequests.Clear();
                await viewModel.StopCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal([true, true, false], stopFocusStates);
                Assert.False(stopFocusRequests[0].PreserveExistingFocus);
                Assert.False(stopFocusRequests[1].PreserveExistingFocus);
                Assert.True(stopFocusRequests[2].PreserveExistingFocus);
            }

            string expectedAnnouncement =
                $"Odtwarzanie zatrzymane. Zaznaczony kanał: {viewModel.SelectedChannel!.AccessibleName}.";
            Assert.Equal([expectedAnnouncement, expectedAnnouncement], status.Announcements);
        }
    }

    [AvaloniaFact]
    public async Task StopThenQuickPlay_QueuesStopFirstAndKeepsTheNewPlayerView()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        Channel[] channels =
        [
            new(ChannelId.New(), sourceId, "Kanał pierwszy", "https://example.test/1"),
            new(ChannelId.New(), sourceId, "Kanał drugi", "https://example.test/2"),
        ];
        var playback = new OrderedStopPlaybackService();
        var focusCoordinator = new FocusCoordinator();
        var focusRequests = new List<FocusRequest>();
        var status = new RecordingAccessibleStatusService();
        focusCoordinator.FocusRequested += (_, request) => focusRequests.Add(request);

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(
                new PlaylistParseResult(new PlaylistHeader(), channels, [], "UTF-8", 0))),
            new FixedPlaylistSourceStore(source),
            playback,
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            status,
            focusCoordinator);

        await viewModel.InitializeAsync();
        await viewModel.PlaySelectedCommand.ExecuteAsync(null);

        var stopTask = viewModel.StopCommand.ExecuteAsync(null);
        viewModel.SelectedChannel = viewModel.Channels[1];
        var playTask = viewModel.PlaySelectedCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        await playback.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await playback.SecondPlayQueued.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(["Play:Kanał pierwszy", "Stop", "Play:Kanał drugi"], playback.GetOperations());

        playback.ReleaseStop();
        await Task.WhenAll(stopTask, playTask).WaitAsync(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsPlayerViewVisible);
        Assert.False(viewModel.IsChannelListViewVisible);
        Assert.NotEqual("Odtwarzanie zatrzymane.", status.StatusMessage);
        Assert.Equal(FocusTarget.PlayerControls, focusRequests.Last().Target);
    }

    [AvaloniaFact]
    public async Task PlayWaitingForPlayerViewThenStop_DoesNotStartTheStaleChannel()
    {
        var sourceId = PlaylistSourceId.New();
        var source = new PlaylistSource(sourceId, "Test playlist", PlaylistSourceKind.File, "test.m3u");
        var channel = new Channel(ChannelId.New(), sourceId, "Kanał pierwszy", "https://example.test/1");
        var playback = new OrderedStopPlaybackService();
        var focusCoordinator = new FocusCoordinator();
        var focusRequests = new List<FocusRequest>();
        var playerViewActivationCount = 0;
        focusCoordinator.FocusRequested += (_, request) => focusRequests.Add(request);

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(
                new PlaylistParseResult(new PlaylistHeader(), [channel], [], "UTF-8", 0))),
            new FixedPlaylistSourceStore(source),
            playback,
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            new RecordingAccessibleStatusService(),
            focusCoordinator);
        viewModel.PlayerViewRequested += (_, _) => playerViewActivationCount++;

        await viewModel.InitializeAsync();

        var playTask = viewModel.PlaySelectedCommand.ExecuteAsync(null);
        var stopTask = viewModel.StopCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        await playback.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await playTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(["Stop"], playback.GetOperations());

        playback.ReleaseStop();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsChannelListViewVisible);
        Assert.False(viewModel.IsPlayerViewVisible);
        Assert.Equal(FocusTarget.ChannelList, focusRequests.Last().Target);
        Assert.Equal(0, playerViewActivationCount);
    }

    [AvaloniaFact]
    public void UnknownNativeVolume_PreservesTheLastAccessibleVolume()
    {
        var source = new PlaylistSource(
            PlaylistSourceId.New(),
            "Test playlist",
            PlaylistSourceKind.File,
            "test.m3u");
        var playback = new ControllablePlaybackService();

        using var viewModel = new MainWindowViewModel(
            new FixedPlaylistService(PlaylistLoadResult.Success(new PlaylistParseResult(
                new PlaylistHeader(),
                [],
                [],
                "UTF-8",
                0))),
            new FixedPlaylistSourceStore(source),
            playback,
            new EmptyEpgSourceManager(),
            new NoOpWindowService(),
            new RecordingAccessibleStatusService(),
            new FocusCoordinator());

        playback.PublishVolume(75);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(75, viewModel.Volume);
        Assert.Equal("Głośność 75 procent", viewModel.VolumeText);

        playback.PublishVolume(-1);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(75, viewModel.Volume);
        Assert.Equal("Głośność 75 procent", viewModel.VolumeText);
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

    private sealed class ControllablePlaybackService : IPlaybackService
    {
        private readonly TaskCompletionSource<PlaybackCommandResult> playCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackSnapshot Current { get; private set; } = PlaybackSnapshot.Initial;

        public TaskCompletionSource PlayStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<int> SetVolumeCalls { get; } = [];

        public int StopCallCount { get; private set; }

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

        public event EventHandler<MediaTracksChangedEventArgs>? TracksChanged
        {
            add { }
            remove { }
        }

        public Task<PlaybackCommandResult> PlayAsync(
            Channel channel,
            PlaybackOptions options,
            CancellationToken cancellationToken = default)
        {
            Current = new PlaybackSnapshot(
                PlaybackState.Playing,
                channel.Id,
                channel.Name,
                65,
                false,
                true,
                $"Odtwarzanie kanału {channel.Name}.");
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            PlayStarted.TrySetResult();
            return playCompletion.Task;
        }

        public void CompletePlay() => playCompletion.TrySetResult(PlaybackCommandResult.Success());

        public void PublishVolume(int volume)
        {
            Current = Current with { Volume = volume };
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            return Task.CompletedTask;
        }

        public Task<PlaybackCommandResult> TogglePauseAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaybackCommandResult.Success());

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
        {
            SetVolumeCalls.Add(volume);
            Current = Current with { Volume = volume };
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            return Task.CompletedTask;
        }

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

    private sealed class ImmediatePlaybackService : IPlaybackService
    {
        public PlaybackSnapshot Current { get; private set; } = PlaybackSnapshot.Initial;

        public List<string> PlayedChannelNames { get; } = [];

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

        public event EventHandler<MediaTracksChangedEventArgs>? TracksChanged
        {
            add { }
            remove { }
        }

        public Task<PlaybackCommandResult> PlayAsync(
            Channel channel,
            PlaybackOptions options,
            CancellationToken cancellationToken = default)
        {
            PlayedChannelNames.Add(channel.Name);
            Current = new PlaybackSnapshot(
                PlaybackState.Playing,
                channel.Id,
                channel.Name,
                Current.Volume,
                Current.IsMuted,
                true,
                $"Odtwarzanie kanału {channel.Name}.");
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            return Task.FromResult(PlaybackCommandResult.Success());
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Current = PlaybackSnapshot.Initial with
            {
                Volume = Current.Volume,
                IsMuted = Current.IsMuted,
                StatusMessage = "Odtwarzanie zatrzymane.",
            };
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            return Task.CompletedTask;
        }

        public Task<PlaybackCommandResult> TogglePauseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PlaybackCommandResult.Success());

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
        {
            Current = Current with { Volume = volume };
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            return Task.CompletedTask;
        }

        public Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default)
        {
            Current = Current with { IsMuted = isMuted };
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            return Task.CompletedTask;
        }

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

    private sealed class OrderedStopPlaybackService : IPlaybackService
    {
        private readonly object operationsGate = new();
        private readonly List<string> operations = [];
        private readonly TaskCompletionSource stopRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource stopFinished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int playCallCount;
        private int stopInFlight;

        public PlaybackSnapshot Current { get; private set; } = PlaybackSnapshot.Initial;

        public TaskCompletionSource StopStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondPlayQueued { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

        public event EventHandler<MediaTracksChangedEventArgs>? TracksChanged
        {
            add { }
            remove { }
        }

        public async Task<PlaybackCommandResult> PlayAsync(
            Channel channel,
            PlaybackOptions options,
            CancellationToken cancellationToken = default)
        {
            AddOperation($"Play:{channel.Name}");
            if (Interlocked.Increment(ref playCallCount) == 2)
            {
                SecondPlayQueued.TrySetResult();
            }

            if (Volatile.Read(ref stopInFlight) == 1)
            {
                await stopFinished.Task.WaitAsync(cancellationToken);
            }

            Current = new PlaybackSnapshot(
                PlaybackState.Playing,
                channel.Id,
                channel.Name,
                Current.Volume,
                Current.IsMuted,
                true,
                $"Odtwarzanie kanału {channel.Name}.");
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            return PlaybackCommandResult.Success();
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            AddOperation("Stop");
            Volatile.Write(ref stopInFlight, 1);
            StopStarted.TrySetResult();
            await stopRelease.Task.WaitAsync(cancellationToken);

            Current = PlaybackSnapshot.Initial with
            {
                Volume = Current.Volume,
                IsMuted = Current.IsMuted,
                StatusMessage = "Odtwarzanie zatrzymane.",
            };
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            Volatile.Write(ref stopInFlight, 0);
            stopFinished.TrySetResult();
        }

        public void ReleaseStop() => stopRelease.TrySetResult();

        public string[] GetOperations()
        {
            lock (operationsGate)
            {
                return [.. operations];
            }
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

        private void AddOperation(string operation)
        {
            lock (operationsGate)
            {
                operations.Add(operation);
            }
        }
    }

    private sealed class EmptyEpgSourceManager : IEpgSourceManager
    {
        public string Source => string.Empty;

        public IReadOnlyList<string> Sources => [];

        public EpgServiceStatus Status { get; } = new(EpgServiceState.Empty);

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
            Task.FromResult<IReadOnlyList<Programme>>([]);

        public Programme? GetCurrentProgramme(Channel channel, DateTimeOffset at) => null;

        public Task<EpgRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EpgRefreshResult.NoSources);
    }

    private sealed class NoOpWindowService(bool confirmationResult = false) : IWindowService
    {
        public Task<IReadOnlyList<LocalPlaylistFileInput>> PickPlaylistFilesAsync() =>
            Task.FromResult<IReadOnlyList<LocalPlaylistFileInput>>([]);

        public Task<PlaylistSourceInput?> PromptForUrlSourceAsync() =>
            Task.FromResult<PlaylistSourceInput?>(null);

        public Task<bool> ShowConfirmationAsync(
            string title,
            string message,
            string confirmButtonText,
            string cancelButtonText = "Anuluj") =>
            Task.FromResult(confirmationResult);

        public Task<int?> SelectTrackAsync(TrackSelectionViewModel viewModel) =>
            Task.FromResult<int?>(null);

        public Task ShowKeyboardShortcutsAsync() => Task.CompletedTask;

        public Task ShowProgramGuideAsync(ProgramGuideViewModel viewModel) => Task.CompletedTask;

        public Task ShowMessageAsync(string title, string message, string buttonText = "OK") =>
            Task.CompletedTask;
    }

    private sealed class ControllableRecordingCoordinator : IRecordingCoordinator
    {
        public event EventHandler<RecordingProgress>? ProgressChanged;

        public bool IsRecording { get; private set; }

        public int StopCallCount { get; private set; }

        public Task<string> StartAsync(
            Channel channel,
            string? programmeTitle,
            RecordingMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCallCount++;
            Publish(RecordingState.Completed);
            return Task.CompletedTask;
        }

        public Task<ScheduledRecordingEntry> ScheduleAsync(
            Channel channel,
            RecordingScheduleRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ScheduledRecordingEntry>> GetScheduledAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScheduledRecordingEntry>>([]);

        public Task CancelScheduledAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Publish(RecordingState state)
        {
            IsRecording = state is RecordingState.Preparing or RecordingState.Recording or RecordingState.Stopping;
            ProgressChanged?.Invoke(
                this,
                new RecordingProgress(state, TimeSpan.Zero, null, state.ToString()));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingAccessibleStatusService : IAccessibleStatusService
    {
        public string StatusMessage { get; private set; } = "Ready";

        public string AlertMessage { get; private set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public List<string> Announcements { get; } = [];

        public void ReportStatus(string message)
        {
            StatusMessage = message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        }

        public void AnnounceStatus(string message)
        {
            Announcements.Add(message);
            ReportStatus(message);
        }

        public void ReportError(string message)
        {
            StatusMessage = message;
            AlertMessage = message;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AlertMessage)));
        }
    }

    private sealed class MemoryPreferenceStore(AppPreferences settings) : ISettingsStore<AppPreferences>
    {
        public AppPreferences Settings { get; private set; } = settings;

        public Task<AppPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task SaveAsync(AppPreferences value, CancellationToken cancellationToken = default)
        {
            Settings = value;
            return Task.CompletedTask;
        }
    }
}
