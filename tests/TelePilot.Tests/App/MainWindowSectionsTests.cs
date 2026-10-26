using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TelePilot.App.Accessibility;
using TelePilot.App.Controls;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.App.ViewModels;
using TelePilot.App.Views;
using TelePilot.Core.Channels;
using TelePilot.Core.CatchUp;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;
using TelePilot.Epg.Models;
using TelePilot.Infrastructure;
using TelePilot.Playback;
using TelePilot.Recording;

namespace TelePilot.Tests.App;

public sealed class MainWindowSectionsTests
{
    [AvaloniaFact]
    public async Task SectionToolbars_OnlyLiveHasPlaylistManagementAndSearchNamesMatchTabs()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var model = fixture.ViewModel;
        var window = new MainWindow { DataContext = model };
        window.Initialize(model, fixture.Focus, new InactivePlayerProvider());
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var source = Assert.IsType<AccessibleComboBox>(window.FindControl<AccessibleComboBox>("SourceSelector"));
            var search = Assert.IsType<AccessibleSearchBox>(window.FindControl<AccessibleSearchBox>("SearchBox"));
            var archive = Assert.IsType<ArchiveView>(window.FindControl<ArchiveView>("ArchivePane"));
            Assert.Empty(archive.GetVisualDescendants().OfType<AccessibleSearchBox>());
            var archiveChannel = Assert.IsType<AccessibleComboBox>(archive.FindControl<AccessibleComboBox>("ChannelPicker"));
            var tabs = Assert.IsType<TabControl>(window.FindControl<TabControl>("SectionTabs"));
            var controls = new Control[] { source,
                window.FindControl<Button>("AddFileSourceButton")!, window.FindControl<Button>("AddUrlSourceButton")!,
                window.FindControl<Button>("RefreshSourceButton")!, window.FindControl<Button>("RemoveSourceButton")! };

            for (int section = 0; section < 3; section++)
            {
                model.SelectedSectionIndex = section;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.All(controls, control => Assert.Equal(section == 0, control.IsEffectivelyVisible));
                Assert.Equal(section != 2, search.IsEffectivelyVisible);
                Assert.Equal(section == 2, archive.GetVisualAncestors().OfType<Border>().First().IsVisible);
                if (section == 2)
                {
                    Assert.True(archive.FocusInitialControl());
                    Assert.True(archiveChannel.IsKeyboardFocusWithin);
                }
                else
                {
                    Assert.Equal(section == 0 ? "Szukaj kanałów" : "Szukaj ulubionych",
                        AutomationProperties.GetName(search));
                    Assert.True(search.FocusEditor());
                }
                PressKey(window, Key.Tab);
                Assert.False(source.IsKeyboardFocusWithin);
                Assert.False(window.FindControl<Button>("AddFileSourceButton")!.IsKeyboardFocusWithin);
            }

            model.SelectedSectionIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.True(search.FocusEditor());
            PressKey(window, Key.Tab, RawInputModifiers.Shift);
            Assert.True(((TabItem)tabs.SelectedItem!).IsKeyboardFocusWithin);
            fixture.Focus.Request(FocusTarget.PlaylistSourceSelector);
            Dispatcher.UIThread.RunJobs();
            Assert.True(search.IsKeyboardFocusWithin);
            model.SelectedSectionIndex = 2;
            Dispatcher.UIThread.RunJobs();
            fixture.Focus.Request(FocusTarget.SearchBox);
            Dispatcher.UIThread.RunJobs();
            Assert.True(archiveChannel.IsKeyboardFocusWithin);
            Assert.False(model.Archive.ShowChannelsWithoutArchive);
            Assert.All(model.Archive.VisibleChannels, channel => Assert.True(CatchUpPlayback.IsSupported(channel)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FavoritesSearch_FiltersSavedChannelsFromOtherSourcesWithItsOwnAccessibleLabel()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSectionIndex = 1;
        var window = new MainWindow { DataContext = fixture.ViewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var search = Assert.IsType<AccessibleSearchBox>(window.FindControl<AccessibleSearchBox>("SearchBox"));
            Assert.Equal("Szukaj ulubionych", AutomationProperties.GetName(search));
            search.Text = "Kanał B";
            await Task.Delay(400, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Kanał B", Assert.Single(fixture.ViewModel.Channels).Name);
            Assert.Single(fixture.Playlists.LoadedSources);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_ShortcutsMoveThirtySecondsAndKeepPlayerFocus()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        var window = new MainWindow { DataContext = fixture.ViewModel };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var pause = Assert.IsType<Button>(window.FindControl<Button>("PauseResumeButton"));
            Assert.True(pause.Focus());

            PressKey(window, Key.Left, RawInputModifiers.Control);
            PressKey(window, Key.Right, RawInputModifiers.Control);
            Assert.Equal(new[] { TimeSpan.FromSeconds(-30), TimeSpan.FromSeconds(30) }, fixture.Playback.SeekOffsets);
            Assert.Same(pause, window.FocusManager?.GetFocusedElement());
            Assert.Contains("Pozycja odtwarzania", fixture.Status.StatusMessage, StringComparison.Ordinal);

            PressKey(window, Key.Left);
            PressKey(window, Key.Right, RawInputModifiers.Control | RawInputModifiers.Shift);
            PressKey(window, Key.Up, RawInputModifiers.Control);
            Assert.Equal(2, fixture.Playback.SeekOffsets.Count);

            fixture.ViewModel.ToggleChannelListCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var archive = Assert.IsType<ArchiveView>(window.FindControl<ArchiveView>("ArchivePane"));
            var date = Assert.IsType<AccessibleComboBox>(archive.FindControl<AccessibleComboBox>("ArchiveDatePicker"));
            Assert.True(date.FocusSelector());
            PressKey(window, Key.Left, RawInputModifiers.Control);
            Assert.Equal(2, fixture.Playback.SeekOffsets.Count);

            var pending = new TaskCompletionSource<PlaybackCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Playback.SeekTask = pending.Task;
            Assert.True(pause.Focus());
            PressKey(window, Key.Right, RawInputModifiers.Control);
            PressKey(window, Key.Right, RawInputModifiers.Control);
            Assert.Equal(3, fixture.Playback.SeekOffsets.Count);
            pending.SetResult(PlaybackCommandResult.Success());
            await fixture.ViewModel.SeekForwardCommand.ExecutionTask!;
            Assert.Equal(4, fixture.Playback.SeekOffsets.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_IsUnavailableOnLiveAndStopAndReportsUnavailablePositionWithoutDialog()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        await fixture.ViewModel.PlaySelectedCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.SeekForwardCommand.CanExecute(null));
        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        Assert.Empty(fixture.Playback.SeekOffsets);

        await StartArchiveAsync(fixture, initialize: false);
        Assert.True(fixture.ViewModel.SeekForwardCommand.CanExecute(null));
        fixture.Playback.SeekResult = PlaybackCommandResult.Unsupported("Ten strumień nie obsługuje przewijania.");
        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Pozycja odtwarzania nie jest jeszcze dostępna", fixture.Status.AlertMessage, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Playback.PlayedChannels.Count);
        Assert.Empty(fixture.Windows.Messages);

        await fixture.ViewModel.StopCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.SeekBackwardCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_LateResultAfterStopDoesNotReplaceStopAnnouncement()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        var completion = new TaskCompletionSource<PlaybackCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Playback.SeekTask = completion.Task;
        var seek = fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        await fixture.ViewModel.StopCommand.ExecuteAsync(null);
        completion.SetResult(new PlaybackCommandResult(PlaybackCommandStatus.Succeeded, "Pozycja odtwarzania: stary wynik."));
        await seek;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("stary wynik", fixture.Status.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_NativeSuccessDoesNotRestartOrRequestFallbackPosition()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        fixture.Playback.Position = TimeSpan.FromMinutes(4);

        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);

        Assert.Single(fixture.Playback.PlayedChannels);
        Assert.Equal(1, fixture.Playback.PreparationCount);
        Assert.Equal(0, fixture.Playback.PositionReadCount);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(fixture.Playback.SeekOffsets));
        Assert.Equal(PlaybackState.Playing, fixture.Playback.Current.State);
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_UnsupportedRebuildsAbsoluteStartAndRemainingDurationAcrossRepeatedSeeks()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        var programme = Assert.Single(fixture.Epg.Programmes);
        fixture.Playback.SeekResult = PlaybackCommandResult.Unsupported("Przewijanie natywne niedostępne.");
        fixture.Playback.Position = TimeSpan.FromSeconds(120);

        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);

        Assert.Equal(2, fixture.Playback.PlayedChannels.Count);
        Assert.Equal($"https://archive.example.test/replay/{programme.Start.AddSeconds(150).ToUnixTimeSeconds()}/3450",
            fixture.Playback.PlayedChannels[^1].StreamUrl);
        fixture.Playback.Position = TimeSpan.FromSeconds(12);

        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, fixture.Playback.PlayedChannels.Count);
        Assert.Equal($"https://archive.example.test/replay/{programme.Start.AddSeconds(192).ToUnixTimeSeconds()}/3408",
            fixture.Playback.PlayedChannels[^1].StreamUrl);
        Assert.Equal(2, fixture.Playback.PositionReadCount);
        Assert.All(fixture.Playback.PlayedChannels, channel => Assert.Equal(fixture.FirstChannel.Id, channel.Id));
        Assert.All(fixture.Playback.PlayOptions, options => Assert.Equal(0, options.MaximumReconnectAttempts));
        Assert.True(fixture.ViewModel.IsArchivePlayback);
        Assert.Equal(programme.Title, fixture.ViewModel.CurrentProgramName);
        Assert.Contains("192 s", fixture.Status.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_FallbackPreservesPausedStateAndSplitLayoutWithoutForcingFocus()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        await fixture.ViewModel.PauseResumeCommand.ExecuteAsync(null);
        fixture.ViewModel.ToggleChannelListCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PlaybackState.Paused, fixture.Playback.Current.State);
        Assert.True(fixture.ViewModel.IsArchiveViewVisible);
        Assert.True(fixture.ViewModel.IsPlayerViewVisible);
        var focusRequests = new List<FocusRequest>();
        fixture.Focus.FocusRequested += (_, request) => focusRequests.Add(request);
        fixture.Playback.SeekResult = PlaybackCommandResult.Unsupported("Przewijanie natywne niedostępne.");
        fixture.Playback.Position = TimeSpan.FromSeconds(60);

        await fixture.ViewModel.SeekBackwardCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var programme = Assert.Single(fixture.Epg.Programmes);
        Assert.Equal($"https://archive.example.test/replay/{programme.Start.AddSeconds(30).ToUnixTimeSeconds()}/3570",
            fixture.Playback.PlayedChannels[^1].StreamUrl);
        Assert.Equal(PlaybackState.Paused, fixture.Playback.Current.State);
        Assert.Equal(2, fixture.Playback.PauseToggleCount);
        Assert.True(fixture.ViewModel.IsArchiveViewVisible);
        Assert.True(fixture.ViewModel.IsPlayerViewVisible);
        Assert.True(fixture.ViewModel.IsChannelListViewVisible);
        Assert.Equal(2, fixture.ViewModel.SelectedSectionIndex);
        Assert.All(focusRequests, request =>
        {
            Assert.True(request.PreserveExistingFocus);
            Assert.Equal(FocusTarget.Archive, request.Target);
        });
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_UnsupportedResultAfterStopNeverRestartsArchive()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        var pendingSeek = new TaskCompletionSource<PlaybackCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Playback.SeekTask = pendingSeek.Task;
        fixture.Playback.Position = TimeSpan.FromSeconds(60);
        var seek = fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);

        await fixture.ViewModel.StopCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var stopMessage = fixture.Status.StatusMessage;
        pendingSeek.SetResult(PlaybackCommandResult.Unsupported("Stary wynik."));
        await seek;
        Dispatcher.UIThread.RunJobs();

        Assert.Single(fixture.Playback.PlayedChannels);
        Assert.Equal(0, fixture.Playback.PositionReadCount);
        Assert.Equal(PlaybackState.Idle, fixture.Playback.Current.State);
        Assert.False(fixture.ViewModel.IsArchivePlayback);
        Assert.Equal(stopMessage, fixture.Status.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_LatePositionAfterNewLivePlayCannotRestartArchive()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        var pendingPosition = new TaskCompletionSource<TimeSpan?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Playback.SeekResult = PlaybackCommandResult.Unsupported("Przewijanie natywne niedostępne.");
        fixture.Playback.PositionTask = pendingPosition.Task;
        var seek = fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        Assert.False(seek.IsCompleted);
        Assert.Equal(1, fixture.Playback.PositionReadCount);

        await fixture.ViewModel.StopCommand.ExecuteAsync(null);
        fixture.ViewModel.SelectedSectionIndex = 0;
        fixture.ViewModel.SelectedChannel = fixture.ViewModel.Channels.Single(item => item.Id == fixture.FirstChannel.Id.ToString());
        await fixture.ViewModel.PlaySelectedCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var liveMessage = fixture.Status.StatusMessage;
        pendingPosition.SetResult(TimeSpan.FromSeconds(60));
        await seek;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, fixture.Playback.PlayedChannels.Count);
        Assert.Same(fixture.FirstChannel, fixture.Playback.PlayedChannels[^1]);
        Assert.False(fixture.ViewModel.IsArchivePlayback);
        Assert.Equal(PlaybackState.Playing, fixture.Playback.Current.State);
        Assert.Equal(liveMessage, fixture.Status.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_QueuedOldRequestCannotSeekReplacementArchiveAfterStop()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        var pendingSeek = new TaskCompletionSource<PlaybackCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Playback.SeekTask = pendingSeek.Task;
        var firstSeek = fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        var queuedSeek = fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        Assert.Single(fixture.Playback.SeekOffsets);

        await fixture.ViewModel.StopCommand.ExecuteAsync(null);
        await StartArchiveAsync(fixture, initialize: false);
        fixture.Playback.Position = TimeSpan.FromSeconds(60);
        var replacementMessage = fixture.Status.StatusMessage;
        pendingSeek.SetResult(PlaybackCommandResult.Unsupported("Stary wynik przewijania."));
        await Task.WhenAll(firstSeek, queuedSeek);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(fixture.Playback.SeekOffsets);
        Assert.Equal(0, fixture.Playback.PositionReadCount);
        Assert.Equal(2, fixture.Playback.PlayedChannels.Count);
        Assert.Equal(PlaybackState.Playing, fixture.Playback.Current.State);
        Assert.True(fixture.ViewModel.IsArchivePlayback);
        Assert.Equal(replacementMessage, fixture.Status.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_RejectedUriRestartThatBecomesIdleAnnouncesSafeError()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        fixture.Playback.SeekResult = PlaybackCommandResult.Unsupported("Przewijanie natywne niedostępne.");
        fixture.Playback.Position = TimeSpan.FromSeconds(60);
        fixture.Playback.PlayResult = PlaybackCommandResult.Rejected("synthetic-private-detail");
        fixture.Playback.PlayStateOverride = PlaybackState.Idle;

        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, fixture.Playback.PlayedChannels.Count);
        Assert.Equal(PlaybackState.Idle, fixture.Playback.Current.State);
        Assert.Contains("Nie udało się przewinąć audycji archiwalnej", fixture.Status.AlertMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private-detail", fixture.Status.AlertMessage, StringComparison.Ordinal);
        Assert.Empty(fixture.Windows.Messages);
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_NativeSuccessAfterUriRestartAnnouncesProgrammeRelativePosition()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        fixture.Playback.SeekResult = PlaybackCommandResult.Unsupported("Przewijanie natywne niedostępne.");
        fixture.Playback.Position = TimeSpan.FromSeconds(120);
        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        fixture.Playback.SeekResult = new PlaybackCommandResult(PlaybackCommandStatus.Succeeded, "Pozycja odtwarzania: 45 s.");
        fixture.Playback.Position = TimeSpan.FromSeconds(45);

        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, fixture.Playback.PlayedChannels.Count);
        Assert.Contains("195 s", fixture.Status.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("45 s", fixture.Status.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ArchiveSeek_FailedPauseRestorationAnnouncesFailureWithoutFalsePausedState()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture);
        await fixture.ViewModel.PauseResumeCommand.ExecuteAsync(null);
        fixture.Playback.PauseResult = PlaybackCommandResult.Rejected("Pauza niedostępna.");
        fixture.Playback.SeekResult = PlaybackCommandResult.Unsupported("Przewijanie natywne niedostępne.");
        fixture.Playback.Position = TimeSpan.FromSeconds(60);

        await fixture.ViewModel.SeekForwardCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, fixture.Playback.PlayedChannels.Count);
        Assert.Equal(PlaybackState.Playing, fixture.Playback.Current.State);
        Assert.Contains("nie udało się przywrócić pauzy", fixture.Status.AlertMessage, StringComparison.Ordinal);
        Assert.Empty(fixture.Windows.Messages);
    }

    [AvaloniaFact]
    public async Task ArchivePlayback_DoesNotAnnounceSuccessWhenAcceptedStreamEndsBeforePlaying()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture, play: false);
        fixture.Playback.AutoStart = false;
        var play = fixture.ViewModel.Archive.PlayCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(play.IsCompleted);
        Assert.DoesNotContain("Uruchomiono archiwum", fixture.ViewModel.Archive.StatusMessage, StringComparison.Ordinal);

        fixture.Playback.EmitState(PlaybackState.Idle);
        await play;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Nie udało się odtworzyć archiwum", fixture.ViewModel.Archive.StatusMessage, StringComparison.Ordinal);
        Assert.False(fixture.ViewModel.IsPlayerViewVisible);
    }

    [AvaloniaFact]
    public async Task ArchivePlayback_WaitsForNativePlayingBeforeAnnouncingSuccess()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture, play: false);
        fixture.Playback.AutoStart = false;
        var play = fixture.ViewModel.Archive.PlayCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(play.IsCompleted);
        fixture.Playback.EmitState(PlaybackState.Playing);
        await play;
        Assert.Contains("Uruchomiono archiwum", fixture.ViewModel.Archive.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ArchivePlayback_ChangingDateStopsPendingNativeConnection()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture, play: false);
        fixture.Playback.AutoStart = false;
        var play = fixture.ViewModel.Archive.PlayCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PlaybackState.Connecting, fixture.Playback.Current.State);

        fixture.ViewModel.Archive.SelectedDate = DateTime.Today.AddDays(-1);
        await play;
        Assert.Equal(PlaybackState.Idle, fixture.Playback.Current.State);
        Assert.DoesNotContain("Uruchomiono archiwum", fixture.ViewModel.Archive.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ArchivePlayback_TransientPlayingThenEndIsNotReportedAsSuccessful()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture, play: false);
        fixture.Playback.AutoStart = false;
        var play = fixture.ViewModel.Archive.PlayCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        fixture.Playback.EmitState(PlaybackState.Playing);
        fixture.Playback.EmitState(PlaybackState.Idle);
        await play;
        Assert.Contains("Nie udało się odtworzyć archiwum", fixture.ViewModel.Archive.StatusMessage, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ArchivePlayback_ChangingDateDuringStartupConfirmationStopsPlayingStream()
    {
        using var fixture = new Fixture();
        await StartArchiveAsync(fixture, play: false);
        fixture.Playback.AutoStart = false;
        var play = fixture.ViewModel.Archive.PlayCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        fixture.Playback.EmitState(PlaybackState.Playing);
        fixture.ViewModel.Archive.SelectedDate = DateTime.Today.AddDays(-1);
        await play;
        Assert.Equal(PlaybackState.Idle, fixture.Playback.Current.State);
        Assert.DoesNotContain("Uruchomiono archiwum", fixture.ViewModel.Archive.StatusMessage, StringComparison.Ordinal);
    }

    private static async Task StartArchiveAsync(Fixture fixture, bool initialize = true, bool play = true)
    {
        var now = DateTimeOffset.Now;
        var programme = new Programme("epg-channel", now.AddHours(-2), now.AddHours(-1), [new EpgLocalizedText("Audycja do przewijania")]);
        fixture.Epg.Programmes = [programme];
        if (initialize) { await fixture.ViewModel.InitializeAsync(); }
        fixture.ViewModel.SelectedSectionIndex = 2;
        fixture.ViewModel.Archive.SelectedDate = programme.Start.LocalDateTime.Date;
        await fixture.ViewModel.Archive.LoadAsync();
        if (play) { await fixture.ViewModel.Archive.PlayCommand.ExecuteAsync(null); }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task ClosingProgramGuideFromArchive_RestoresArchiveFocus()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSectionIndex = 2;
        var requests = new List<FocusRequest>();
        fixture.Focus.FocusRequested += (_, request) => requests.Add(request);

        await fixture.ViewModel.OpenProgramGuideCommand.ExecuteAsync(null);

        Assert.Equal(FocusTarget.Archive, requests[^1].Target);
        Assert.Equal(2, fixture.ViewModel.SelectedSectionIndex);
    }

    [AvaloniaFact]
    public async Task SectionTabs_ExposeThreeNamedTabItemsAndKeyboardShortcutsSelectThem()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var window = new MainWindow { DataContext = fixture.ViewModel };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var tabs = Assert.IsType<TabControl>(window.FindControl<TabControl>("SectionTabs"));
            var items = tabs.Items.OfType<TabItem>().ToArray();
            Assert.Equal(3, items.Length);
            Assert.Equal(AutomationControlType.Tab, ControlAutomationPeer.CreatePeerForElement(tabs).GetAutomationControlType());
            string[] names = ["TV na żywo", "Ulubione", "Archiwum"];
            string[] identifiers = ["tab-live", "tab-favorites", "tab-archive"];
            for (var index = 0; index < items.Length; index++)
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(items[index]);
                Assert.Equal(AutomationControlType.TabItem, peer.GetAutomationControlType());
                Assert.Equal(names[index], peer.GetName());
                Assert.Equal(identifiers[index], peer.GetAutomationId());
            }

            var mountedList = window.FindControl<ListBox>("ChannelList");
            await fixture.ViewModel.PlaySelectedCommand.ExecuteAsync(null);
            Assert.False(fixture.ViewModel.IsChannelListViewVisible);
            PressKey(window, Key.D1, RawInputModifiers.Control);
            Assert.True(fixture.ViewModel.IsChannelListViewVisible);
            Assert.Same(items[0], window.FocusManager?.GetFocusedElement());
            var search = Assert.IsType<AccessibleSearchBox>(window.FindControl<AccessibleSearchBox>("SearchBox"));
            Assert.True(search.FocusEditor());
            (Key Key, RawInputModifiers Modifiers, int Expected)[] gestures =
            [
                (Key.Tab, RawInputModifiers.Control, 1),
                (Key.Tab, RawInputModifiers.Control, 2),
                (Key.Tab, RawInputModifiers.Control, 0),
                (Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift, 2),
                (Key.D1, RawInputModifiers.Control, 0),
                (Key.D2, RawInputModifiers.Control, 1),
                (Key.D3, RawInputModifiers.Control, 2),
            ];
            foreach (var (key, modifiers, expected) in gestures)
            {
                PressKey(window, key, modifiers);
                Assert.Equal(expected, fixture.ViewModel.SelectedSectionIndex);
                Assert.Equal(expected, tabs.SelectedIndex);
                var focused = window.FocusManager?.GetFocusedElement();
                Assert.True(ReferenceEquals(items[expected], focused),
                    $"Gesture {modifiers}+{key}, section {expected}: expected selected header focus, got {focused?.GetType().Name} ({(focused as Control)?.Name}).");
                Assert.Same(mountedList, window.FindControl<ListBox>("ChannelList"));
                Assert.Same(window, TopLevel.GetTopLevel(mountedList!));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SectionTabs_TabAndShiftTabConnectOnlyTheControlsVisibleInEachSection()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var window = new MainWindow { DataContext = fixture.ViewModel };
        try
        {
            window.Show();
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var tabs = Assert.IsType<TabControl>(window.FindControl<TabControl>("SectionTabs"));
            var source = Assert.IsType<AccessibleComboBox>(window.FindControl<AccessibleComboBox>("SourceSelector"));
            var search = Assert.IsType<AccessibleSearchBox>(window.FindControl<AccessibleSearchBox>("SearchBox"));
            var play = Assert.IsType<Button>(window.FindControl<Button>("PlaySelectedChannelButton"));
            var addFile = Assert.IsType<Button>(window.FindControl<Button>("AddFileSourceButton"));
            var mountedList = Assert.IsType<ListBox>(window.FindControl<ListBox>("ChannelList"));
            for (var section = 0; section < 2; section++)
            {
                fixture.ViewModel.SelectedSectionIndex = section;
                Dispatcher.UIThread.RunJobs();
                var selectedTab = Assert.IsType<TabItem>(tabs.SelectedItem);
                Assert.True(selectedTab.Focus());
                PressKey(window, Key.Tab);
                Assert.True(section == 0 ? source.IsKeyboardFocusWithin : search.IsKeyboardFocusWithin);
                PressKey(window, Key.Tab, RawInputModifiers.Shift);
                Assert.Same(selectedTab, window.FocusManager?.GetFocusedElement());

                Assert.True(play.Focus());
                PressKey(window, Key.Tab);
                Assert.Same(selectedTab, window.FocusManager?.GetFocusedElement());
                PressKey(window, Key.Tab);
                Assert.True(section == 0 ? source.IsKeyboardFocusWithin : search.IsKeyboardFocusWithin);
                PressKey(window, Key.Tab);
                if (section == 0) Assert.Same(addFile, window.FocusManager?.GetFocusedElement());
                else Assert.True(mountedList.IsKeyboardFocusWithin);
                PressKey(window, Key.Tab, RawInputModifiers.Shift);
                Assert.True(section == 0 ? source.IsKeyboardFocusWithin : search.IsKeyboardFocusWithin);
                Assert.Same(mountedList, window.FindControl<ListBox>("ChannelList"));
                Assert.Same(window, TopLevel.GetTopLevel(mountedList));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Sections_SelectLiveFavoritesAndArchiveAndRetainLiveGroupFilter()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var model = fixture.ViewModel;
        Assert.Equal(0, model.SelectedSectionIndex);
        Assert.Equal(2, model.Channels.Count);
        Assert.False(model.IsFavoritesOnly);

        model.SelectedGroup = "Wiadomości";
        Assert.Equal("Kanał A", Assert.Single(model.Channels).Name);
        model.SelectedSectionIndex = 1;
        Assert.True(model.IsFavoritesOnly);
        Assert.True(model.IsFavoritesSection);
        Assert.True(model.IsChannelsViewEnabled);
        Assert.Equal(3, model.Channels.Count);
        Assert.All(model.Channels, item => Assert.True(item.IsFavorite));

        model.SelectedSectionIndex = 2;
        Assert.True(model.IsArchiveSection);
        Assert.True(model.IsArchiveViewVisible);
        Assert.False(model.IsChannelsViewEnabled);
        Assert.False(model.IsFavoritesOnly);

        model.SelectedSectionIndex = 0;
        Assert.False(model.IsArchiveViewVisible);
        Assert.True(model.IsChannelsViewEnabled);
        Assert.Equal("Wiadomości", model.SelectedGroup);
        Assert.Equal("Kanał A", Assert.Single(model.Channels).Name);
        model.SelectedSectionIndex = -1;
        model.SelectedSectionIndex = 3;
        Assert.Equal(0, model.SelectedSectionIndex);
    }

    [AvaloniaFact]
    public async Task FavoritesSection_ShowsAllSourcesAndUnavailableSavedEntriesWithNameSearch()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var model = fixture.ViewModel;

        model.SelectedSectionIndex = 1;

        Assert.Equal(["Kanał A", "Kanał B", "Zapisany nieobecny"], model.Channels.Select(item => item.Name));
        Assert.Equal(fixture.FirstSource.Id.Value, model.SelectedSource!.Source.Id.Value);
        Assert.DoesNotContain(fixture.SecondSource.Id, fixture.Playlists.LoadedSources);

        model.SearchText = "kanał b";
        // Section changes apply the pending search synchronously without waiting
        // for the normal typing debounce.
        model.SelectedSectionIndex = 0;
        Assert.Empty(model.Channels);
        model.SelectedSectionIndex = 1;
        Assert.Equal("Kanał B", Assert.Single(model.Channels).Name);
    }

    [AvaloniaFact]
    public async Task FavoritesSection_PlaySelectedLoadsTheSavedSourceAndKeepsAllFavorites()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var model = fixture.ViewModel;
        model.SelectedSectionIndex = 1;
        model.SelectedChannel = model.Channels.Single(item => item.Name == "Kanał B");

        await model.PlaySelectedCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(fixture.SecondSource.Id, model.SelectedSource!.Source.Id);
        Assert.Equal(new[] { fixture.FirstSource.Id, fixture.SecondSource.Id }, fixture.Playlists.LoadedSources);
        Assert.Same(fixture.SecondChannel, Assert.Single(fixture.Playback.PlayedChannels));
        Assert.Equal(1, model.SelectedSectionIndex);
        Assert.True(model.IsPlayerViewVisible);
        Assert.Equal(3, model.Channels.Count);
        Assert.Equal(fixture.SecondChannel.Id.ToString(), model.SelectedChannel!.Id);

        await model.StopCommand.ExecuteAsync(null);
        Assert.True(model.IsChannelListViewVisible);
        Assert.Equal(1, model.SelectedSectionIndex);
        Assert.Equal(3, model.Channels.Count);
        Assert.Equal(fixture.SecondChannel.Id.ToString(), model.SelectedChannel!.Id);
    }

    [AvaloniaFact]
    public async Task ArchiveSourceProfiles_ApplyOnlyToMatchingSourceAndLeaveLiveChannelsUnmodified()
    {
        using var fixture = new Fixture((_, second) =>
        [
            new ArchiveSourcePreference(second.Id.Value, new CatchUpSourceSettings
            {
                Mode = CatchUpSourceMode.Shift,
                RetentionDays = 3,
            }),
        ]);
        await fixture.ViewModel.InitializeAsync();
        Assert.Same(fixture.FirstChannel, Assert.Single(fixture.ViewModel.Archive.Channels));
        Assert.Equal("default", fixture.FirstChannel.Attributes["catchup"]);
        Assert.Equal("7", fixture.FirstChannel.Attributes["catchup-days"]);

        fixture.ViewModel.SelectedSource = fixture.ViewModel.Sources.Single(item => item.Source.Id == fixture.SecondSource.Id);
        await fixture.ViewModel.RefreshSourceCommand.ExecuteAsync(null);

        var configured = Assert.Single(fixture.ViewModel.Archive.Channels);
        Assert.Equal(fixture.SecondChannel.Id, configured.Id);
        Assert.Equal(fixture.SecondSource.Id, configured.PlaylistSourceId);
        Assert.Equal("shift", configured.Attributes["catchup"]);
        Assert.Equal("3", configured.Attributes["catchup-days"]);
        Assert.Empty(fixture.SecondChannel.Attributes);
        Assert.Equal(fixture.SecondChannel.StreamUrl, configured.StreamUrl);

        fixture.ViewModel.SelectedSectionIndex = 0;
        fixture.ViewModel.SelectedChannel = Assert.Single(fixture.ViewModel.Channels);
        await fixture.ViewModel.PlaySelectedCommand.ExecuteAsync(null);

        Assert.Same(fixture.SecondChannel, Assert.Single(fixture.Playback.PlayedChannels));
        Assert.False(fixture.ViewModel.IsArchivePlayback);
        Assert.DoesNotContain("catchup", fixture.Playback.PlayedChannels[^1].Attributes.Keys);
    }

    [AvaloniaFact]
    public async Task FavoritesSaves_PreserveArchiveProfilesForLoadedAndUnlistedSources()
    {
        var unlistedId = Guid.NewGuid();
        using var fixture = new Fixture((first, _) =>
        [
            new ArchiveSourcePreference(first.Id.Value, new CatchUpSourceSettings
            {
                Mode = CatchUpSourceMode.Shift, RetentionDays = 4, OverridePlaylist = true,
            }),
            new ArchiveSourcePreference(unlistedId, new CatchUpSourceSettings { Mode = CatchUpSourceMode.Disabled }),
        ]);
        await fixture.ViewModel.InitializeAsync();
        var expected = fixture.Preferences.Current.ArchiveSources.ToArray();
        fixture.ViewModel.SelectedChannel = fixture.ViewModel.Channels.Single(item => item.Id == fixture.FirstChannel.Id.ToString());

        await fixture.ViewModel.ToggleFavoriteCommand.ExecuteAsync(null);

        Assert.Equal(expected, fixture.Store.Settings.ArchiveSources);
        Assert.DoesNotContain(fixture.Store.Settings.Favorites, favorite => favorite.ChannelId == fixture.FirstChannel.Id.Value);
        fixture.ViewModel.SelectedSectionIndex = 1;
        fixture.Windows.Manage = manager =>
        {
            manager.Items.Single(item => item.ChannelName == "Kanał B").IsSelected = true;
            manager.RemoveSelectedCommand.Execute(null);
            return Task.CompletedTask;
        };

        await fixture.ViewModel.ManageFavoritesCommand.ExecuteAsync(null);

        Assert.Equal(expected, fixture.Store.Settings.ArchiveSources);
        Assert.Equal(expected, fixture.Preferences.Current.ArchiveSources);
        Assert.DoesNotContain(fixture.Store.Settings.Favorites, favorite => favorite.ChannelId == fixture.SecondChannel.Id.Value);
    }

    [AvaloniaFact]
    public async Task ManageFavorites_MultiRemovalPersistsAndRefreshesListWithoutLosingOtherSettings()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSectionIndex = 1;
        var previous = fixture.Preferences.Current;
        fixture.Windows.Manage = model =>
        {
            model.Items.Single(item => item.ChannelName == "Kanał A").IsSelected = true;
            model.Items.Single(item => item.ChannelName == "Zapisany nieobecny").IsSelected = true;
            model.RemoveSelectedCommand.Execute(null);
            return Task.CompletedTask;
        };

        await fixture.ViewModel.ManageFavoritesCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Store.SaveCount);
        Assert.Equal(fixture.SecondChannel.Id.Value, Assert.Single(fixture.Store.Settings.Favorites).ChannelId);
        Assert.Equal("Kanał B", Assert.Single(fixture.ViewModel.Channels).Name);
        Assert.Single(fixture.ViewModel.FavoriteChannels);
        Assert.Equal(previous.ChannelSortMode, fixture.Store.Settings.ChannelSortMode);
        Assert.Equal(previous.ExternalEpgUrl, fixture.Store.Settings.ExternalEpgUrl);
        AssertRecordingPreferencesEqual(previous.Recording, fixture.Store.Settings.Recording);

        fixture.ViewModel.SelectedSectionIndex = 0;
        Assert.False(fixture.ViewModel.Channels.Single(item => item.Name == "Kanał A").IsFavorite);
    }

    [AvaloniaFact]
    public async Task ManageFavorites_CancelKeepsPreferencesAndVisibleFavorites()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSectionIndex = 1;
        var previous = fixture.Preferences.Current;
        fixture.Windows.Manage = model =>
        {
            model.Items[0].IsSelected = true;
            model.CancelCommand.Execute(null);
            return Task.CompletedTask;
        };

        await fixture.ViewModel.ManageFavoritesCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.Store.SaveCount);
        Assert.Same(previous, fixture.Preferences.Current);
        Assert.Equal(3, fixture.ViewModel.Channels.Count);
        Assert.Equal(3, fixture.ViewModel.FavoriteChannels.Count);
    }

    [AvaloniaFact]
    public async Task ManageFavorites_SaveFailureLeavesPersistedAndVisibleFavoritesIntact()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.SelectedSectionIndex = 1;
        var previous = fixture.Preferences.Current;
        var persisted = fixture.Store.Settings;
        fixture.Store.FailSave = true;
        fixture.Windows.Manage = model =>
        {
            model.Items[0].IsSelected = true;
            model.RemoveSelectedCommand.Execute(null);
            return Task.CompletedTask;
        };

        await fixture.ViewModel.ManageFavoritesCommand.ExecuteAsync(null);

        Assert.Same(previous, fixture.Preferences.Current);
        Assert.Same(persisted, fixture.Store.Settings);
        Assert.Equal(3, fixture.ViewModel.Channels.Count);
        Assert.Equal(3, fixture.ViewModel.FavoriteChannels.Count);
        Assert.Contains("Nie udało się zapisać ulubionych", Assert.Single(fixture.Windows.Messages), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ManageFavorites_PreservesPreferencesAndFavoritesAddedWhileDialogIsOpen()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var addedMeanwhile = new FavoriteChannelPreference(Guid.NewGuid(), Guid.NewGuid(), "Dodany później");
        var archiveProfile = new ArchiveSourcePreference(fixture.SecondSource.Id.Value,
            new CatchUpSourceSettings { Mode = CatchUpSourceMode.Shift, RetentionDays = 5 });
        fixture.Windows.Manage = async model =>
        {
            model.Items.Single(item => item.ChannelName == "Kanał A").IsSelected = true;
            var current = fixture.Preferences.Current;
            await fixture.Preferences.SaveAsync(new AppPreferences
            {
                ChannelSortMode = ChannelSortMode.GroupThenName,
                ExternalEpgUrl = "https://epg.example.test/changed.xml",
                ArchiveSources = [archiveProfile],
                Recording = current.Recording,
                Favorites = [.. current.Favorites, addedMeanwhile],
            });
            model.RemoveSelectedCommand.Execute(null);
        };

        await fixture.ViewModel.ManageFavoritesCommand.ExecuteAsync(null);

        Assert.Contains(addedMeanwhile, fixture.Preferences.Current.Favorites);
        Assert.DoesNotContain(fixture.Preferences.Current.Favorites, item => item.ChannelId == fixture.FirstChannel.Id.Value);
        Assert.Equal("https://epg.example.test/changed.xml", fixture.Preferences.Current.ExternalEpgUrl);
        Assert.Equal(ChannelSortMode.GroupThenName, fixture.Preferences.Current.ChannelSortMode);
        Assert.Equal(archiveProfile, Assert.Single(fixture.Preferences.Current.ArchiveSources));
        Assert.Equal(3, fixture.ViewModel.FavoriteChannels.Count);
    }

    [AvaloniaFact]
    public async Task ArchivePlayback_UsesResolvedUrlAndProgrammeTitleThenLivePlaybackResetsHeading()
    {
        using var fixture = new Fixture();
        var now = DateTimeOffset.Now;
        var programme = new Programme("epg-channel", now.AddHours(-2), now.AddHours(-1), [new EpgLocalizedText("Wczorajsza audycja")]);
        fixture.Epg.Programmes = [programme];
        await fixture.ViewModel.InitializeAsync();
        var model = fixture.ViewModel;
        model.SelectedSectionIndex = 2;
        model.Archive.SelectedDate = programme.Start.LocalDateTime.Date;
        await model.Archive.LoadAsync();
        Assert.True(model.Archive.PlayCommand.CanExecute(null));

        await model.Archive.PlayCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var archived = Assert.Single(fixture.Playback.PlayedChannels);
        Assert.Equal(fixture.FirstChannel.Id, archived.Id);
        Assert.Equal($"https://archive.example.test/replay/{programme.Start.ToUnixTimeSeconds()}/3600", archived.StreamUrl);
        Assert.True(model.IsArchivePlayback);
        Assert.Equal("Odtwarzanie archiwum", model.PlaybackHeading);
        Assert.Equal(programme.Title, model.CurrentProgramName);
        Assert.Equal(0, Assert.Single(fixture.Playback.PlayOptions).MaximumReconnectAttempts);

        await model.StopCommand.ExecuteAsync(null);
        model.SelectedSectionIndex = 0;
        model.SelectedChannel = model.Channels.Single(item => item.Id == fixture.FirstChannel.Id.ToString());
        await model.PlaySelectedCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(model.IsArchivePlayback);
        Assert.Equal("Odtwarzanie na żywo", model.PlaybackHeading);
        Assert.Equal(fixture.FirstChannel.StreamUrl, fixture.Playback.PlayedChannels[^1].StreamUrl);
    }

    [AvaloniaFact]
    public async Task ProgramGuideLivePlayback_AfterArchiveClearsArchiveTitleAndHeading()
    {
        using var fixture = new Fixture();
        var now = DateTimeOffset.Now;
        var programme = new Programme("epg-channel", now.AddHours(-2), now.AddHours(-1), [new EpgLocalizedText("Audycja archiwalna")]);
        fixture.Epg.Programmes = [programme];
        await fixture.ViewModel.InitializeAsync();
        var model = fixture.ViewModel;
        model.SelectedSectionIndex = 2;
        model.Archive.SelectedDate = programme.Start.LocalDateTime.Date;
        await model.Archive.LoadAsync();
        await model.Archive.PlayCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(model.IsArchivePlayback);
        Assert.Equal(programme.Title, model.CurrentProgramName);
        fixture.Windows.ProgramGuideInteraction = guide => guide.PlayChannelCommand.ExecuteAsync(null);

        await model.OpenProgramGuideCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(fixture.Windows.ProgramGuide);
        Assert.Equal(fixture.FirstChannel.Id.ToString(), fixture.Windows.ProgramGuide.SelectedChannel!.Id);
        Assert.Equal(2, fixture.Playback.PlayedChannels.Count);
        Assert.Same(fixture.FirstChannel, fixture.Playback.PlayedChannels[^1]);
        Assert.Equal(3, fixture.Playback.PlayOptions[^1].MaximumReconnectAttempts);
        Assert.False(model.IsArchivePlayback);
        Assert.Equal("Odtwarzanie na żywo", model.PlaybackHeading);
        Assert.Equal("Brak danych o bieżącej audycji", model.CurrentProgramName);
    }

    [AvaloniaFact]
    public async Task StopDuringPendingArchivePreparation_CancelsNativePlayAndSuccessAnnouncement()
    {
        using var fixture = new Fixture();
        var now = DateTimeOffset.Now;
        var programme = new Programme("epg-channel", now.AddHours(-2), now.AddHours(-1), [new EpgLocalizedText("Przerwana audycja")]);
        fixture.Epg.Programmes = [programme];
        await fixture.ViewModel.InitializeAsync();
        var model = fixture.ViewModel;
        model.SelectedSectionIndex = 2;
        model.Archive.SelectedDate = programme.Start.LocalDateTime.Date;
        await model.Archive.LoadAsync();
        var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Playback.PreparationTask = preparation.Task;
        var play = model.Archive.PlayCommand.ExecuteAsync(null);
        Assert.False(play.IsCompleted);
        Assert.Equal(1, fixture.Playback.PreparationCount);
        Assert.Empty(fixture.Playback.PlayedChannels);

        await model.StopCommand.ExecuteAsync(null);
        Assert.True(fixture.Playback.LastPreparationToken.IsCancellationRequested);
        preparation.SetResult();
        await play;
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(fixture.Playback.PlayedChannels);
        Assert.False(model.IsPlayerViewVisible);
        Assert.True(model.IsArchiveViewVisible);
        Assert.False(model.IsArchivePlayback);
        Assert.DoesNotContain("Uruchomiono archiwum", model.Archive.StatusMessage, StringComparison.Ordinal);
    }

    private static void AssertRecordingPreferencesEqual(RecordingPreferences expected, RecordingPreferences actual)
    {
        Assert.Equal(expected.OutputDirectory, actual.OutputDirectory);
        Assert.Equal(expected.FfmpegPath, actual.FfmpegPath);
        Assert.Equal(expected.AudioFormat, actual.AudioFormat);
        Assert.Equal(expected.AudioBitrateKbps, actual.AudioBitrateKbps);
        Assert.Equal(expected.FileNameTemplate, actual.FileNameTemplate);
        Assert.Equal(expected.WakeComputerForScheduledRecording, actual.WakeComputerForScheduledRecording);
    }

    private static void PressKey(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class InactivePlayerProvider : ILibVlcMediaPlayerProvider
    {
        public Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public LibVLCSharp.Shared.MediaPlayer MediaPlayer => throw new InvalidOperationException("Toolbar test must not create video.");
        public string LibVlcVersion => "synthetic";
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(Func<PlaylistSource, PlaylistSource, IReadOnlyList<ArchiveSourcePreference>>? archiveProfiles = null)
        {
            FirstChannel = new Channel(ChannelId.New(), FirstSource.Id, "Kanał A", "https://stream.example.test/a", groupTitle: "Wiadomości",
                attributes: new Dictionary<string, string>
                {
                    ["catchup"] = "default", ["catchup-days"] = "7",
                    ["catchup-source"] = "https://archive.example.test/replay/{utc}/{duration}",
                });
            SecondChannel = new Channel(ChannelId.New(), SecondSource.Id, "Kanał B", "https://stream.example.test/b", groupTitle: "Sport");
            var other = new Channel(ChannelId.New(), FirstSource.Id, "Pozostały kanał", "https://stream.example.test/other", groupTitle: "Sport");
            Playlists = new PlaylistService(new Dictionary<PlaylistSourceId, IReadOnlyList<Channel>>
            {
                [FirstSource.Id] = [FirstChannel, other], [SecondSource.Id] = [SecondChannel],
            });
            Store = new PreferenceStore(new AppPreferences
            {
                ChannelSortMode = ChannelSortMode.Name,
                ExternalEpgUrl = "https://epg.example.test/guide.xml",
                ArchiveSources = [.. (archiveProfiles?.Invoke(FirstSource, SecondSource) ?? [])],
                Recording = new RecordingPreferences
                {
                    OutputDirectory = Path.Combine(Path.GetTempPath(), "telepilot-test-recordings"),
                    FfmpegPath = Path.Combine(Path.GetTempPath(), "test-ffmpeg.exe"),
                    AudioFormat = AudioOutputFormat.Opus, AudioBitrateKbps = 256,
                    FileNameTemplate = "{channel}-{date}", WakeComputerForScheduledRecording = true,
                },
                Favorites =
                [
                    new(FirstSource.Id.Value, FirstChannel.Id.Value, FirstChannel.Name),
                    new(SecondSource.Id.Value, SecondChannel.Id.Value, SecondChannel.Name),
                    new(Guid.NewGuid(), Guid.NewGuid(), "Zapisany nieobecny"),
                ],
            });
            Preferences = new AppPreferencesService(Store);
            ViewModel = new MainWindowViewModel(Playlists, new SourceStore(FirstSource, SecondSource), Playback, Epg,
                Windows, Status, Focus, Preferences);
        }

        public PlaylistSource FirstSource { get; } = new(PlaylistSourceId.New(), "Pierwsza", PlaylistSourceKind.Url, "https://playlist.example.test/first.m3u");
        public PlaylistSource SecondSource { get; } = new(PlaylistSourceId.New(), "Druga", PlaylistSourceKind.Url, "https://playlist.example.test/second.m3u");
        public Channel FirstChannel { get; }
        public Channel SecondChannel { get; }
        public PlaylistService Playlists { get; }
        public PlaybackService Playback { get; } = new();
        public ArchiveViewModelTests.FakeEpg Epg { get; } = new();
        public ManagerWindowService Windows { get; } = new();
        public AccessibleStatusService Status { get; } = new();
        public FocusCoordinator Focus { get; } = new();
        public PreferenceStore Store { get; }
        public AppPreferencesService Preferences { get; }
        public MainWindowViewModel ViewModel { get; }

        public void Dispose()
        {
            Windows.ProgramGuide?.Dispose();
            ViewModel.Dispose();
            Status.Dispose();
        }
    }

    private sealed class PreferenceStore(AppPreferences initial) : ISettingsStore<AppPreferences>
    {
        public AppPreferences Settings { get; private set; } = initial;
        public int SaveCount { get; private set; }
        public bool FailSave { get; set; }
        public Task<AppPreferences> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);
        public Task SaveAsync(AppPreferences value, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (FailSave)
            {
                throw new IOException("Testowy błąd zapisu ustawień.");
            }
            Settings = value;
            return Task.CompletedTask;
        }
    }

    private sealed class PlaylistService(IReadOnlyDictionary<PlaylistSourceId, IReadOnlyList<Channel>> channels) : IPlaylistService
    {
        public List<PlaylistSourceId> LoadedSources { get; } = [];
        public Task<PlaylistLoadResult> LoadAsync(PlaylistSource source, CancellationToken cancellationToken = default)
        {
            LoadedSources.Add(source.Id);
            return Task.FromResult(PlaylistLoadResult.Success(new PlaylistParseResult(new PlaylistHeader(), channels[source.Id], [], "UTF-8", 0)));
        }
    }

    private sealed class SourceStore(params PlaylistSource[] sources) : IPlaylistSourceStore
    {
        public Task<IReadOnlyList<PlaylistSource>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlaylistSource>>(sources);
        public Task<PlaylistSource> ImportFileAsync(LocalPlaylistSourceImport import, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RefreshFileAsync(PlaylistSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(PlaylistSource source, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> RemoveAsync(PlaylistSourceId sourceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class ManagerWindowService : IWindowService
    {
        public Func<FavoritesManagerViewModel, Task>? Manage { get; set; }
        public Func<ProgramGuideViewModel, Task>? ProgramGuideInteraction { get; set; }
        public ProgramGuideViewModel? ProgramGuide { get; private set; }
        public List<string> Messages { get; } = [];
        public async Task<IReadOnlyList<FavoriteChannelPreference>?> ShowFavoritesManagerAsync(FavoritesManagerViewModel viewModel)
        {
            IReadOnlyList<FavoriteChannelPreference>? result = null;
            EventHandler<IReadOnlyList<FavoriteChannelPreference>?> close = (_, remaining) => result = remaining;
            viewModel.CloseRequested += close;
            try
            {
                if (Manage is not null)
                {
                    await Manage(viewModel);
                }
                return result;
            }
            finally
            {
                viewModel.CloseRequested -= close;
            }
        }
        public Task<IReadOnlyList<LocalPlaylistFileInput>> PickPlaylistFilesAsync() => Task.FromResult<IReadOnlyList<LocalPlaylistFileInput>>([]);
        public Task<PlaylistSourceInput?> PromptForUrlSourceAsync() => Task.FromResult<PlaylistSourceInput?>(null);
        public Task<bool> ShowConfirmationAsync(string title, string message, string confirmButtonText, string cancelButtonText = "Anuluj") => Task.FromResult(false);
        public Task<int?> SelectTrackAsync(TrackSelectionViewModel viewModel) => Task.FromResult<int?>(null);
        public Task ShowKeyboardShortcutsAsync() => Task.CompletedTask;
        public Task ShowProgramGuideAsync(ProgramGuideViewModel viewModel)
        {
            ProgramGuide = viewModel;
            return ProgramGuideInteraction?.Invoke(viewModel) ?? Task.CompletedTask;
        }
        public Task ShowMessageAsync(string title, string message, string buttonText = "OK")
        {
            Messages.Add(title);
            return Task.CompletedTask;
        }
    }

    private sealed class PlaybackService : IPlaybackService
    {
        public bool AutoStart { get; set; } = true;
        public PlaybackCommandResult PlayResult { get; set; } = PlaybackCommandResult.Success();
        public PlaybackState? PlayStateOverride { get; set; }
        public PlaybackCommandResult PauseResult { get; set; } = PlaybackCommandResult.Success();
        public void EmitState(PlaybackState state)
        {
            Current = Current with { State = state };
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
        }
        public List<TimeSpan> SeekOffsets { get; } = [];
        public PlaybackCommandResult SeekResult { get; set; } = new(PlaybackCommandStatus.Succeeded, "Pozycja odtwarzania: 30 s.");
        public Task<PlaybackCommandResult>? SeekTask { get; set; }
        public TimeSpan? Position { get; set; }
        public Task<TimeSpan?>? PositionTask { get; set; }
        public int PositionReadCount { get; private set; }
        public int PauseToggleCount { get; private set; }
        public Task<TimeSpan?> GetPositionAsync(CancellationToken cancellationToken = default)
        {
            PositionReadCount++;
            return PositionTask ?? Task.FromResult(Position);
        }
        public Task<PlaybackCommandResult> SeekRelativeAsync(TimeSpan offset, CancellationToken cancellationToken = default)
        {
            SeekOffsets.Add(offset);
            return SeekTask ?? Task.FromResult(SeekResult);
        }
        public Task? PreparationTask { get; set; }
        public CancellationToken LastPreparationToken { get; private set; }
        public int PreparationCount { get; private set; }
        public PlaybackSnapshot Current { get; private set; } = PlaybackSnapshot.Initial;
        public List<Channel> PlayedChannels { get; } = [];
        public List<PlaybackOptions> PlayOptions { get; } = [];
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
        public event EventHandler<MediaTracksChangedEventArgs>? TracksChanged { add { } remove { } }
        public Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            LastPreparationToken = cancellationToken;
            PreparationCount++;
            return PreparationTask ?? Task.CompletedTask;
        }
        public Task<PlaybackCommandResult> PlayAsync(Channel channel, PlaybackOptions options, CancellationToken cancellationToken = default)
        {
            PlayedChannels.Add(channel);
            PlayOptions.Add(options);
            Position = null;
            Current = new PlaybackSnapshot(PlayStateOverride ?? (AutoStart ? PlaybackState.Playing : PlaybackState.Connecting), channel.Id, channel.Name, 100, false, true);
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            return Task.FromResult(PlayResult);
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Current = PlaybackSnapshot.Initial;
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(Current));
            return Task.CompletedTask;
        }
        public Task<PlaybackCommandResult> TogglePauseAsync(CancellationToken cancellationToken = default)
        {
            PauseToggleCount++;
            if (PauseResult.IsSuccess)
            {
                EmitState(Current.State == PlaybackState.Paused ? PlaybackState.Playing : PlaybackState.Paused);
            }
            return Task.FromResult(PauseResult);
        }
        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetMutedAsync(bool isMuted, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MediaTrack>> GetTracksAsync(MediaTrackKind kind, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaTrack>>([]);
        public Task<PlaybackCommandResult> SelectTrackAsync(MediaTrackKind kind, int engineId, CancellationToken cancellationToken = default) => Task.FromResult(PlaybackCommandResult.Success());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
