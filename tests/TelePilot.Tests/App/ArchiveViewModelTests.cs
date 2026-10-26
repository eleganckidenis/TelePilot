using TelePilot.App.ViewModels;
using TelePilot.Core.Models;
using TelePilot.Core.CatchUp;
using TelePilot.Playlists;
using TelePilot.Epg.Models;
using TelePilot.Epg.Services;

namespace TelePilot.Tests.App;

public sealed class ArchiveViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("3")]
    [InlineData("5")]
    public async Task FlussonicTypeAlias_ParsedChannelOffersProgrammeAndBuildsFiniteArchive(string days)
    {
        var parsed = new M3uPlaylistParser().Parse($"""
            #EXTM3U
            #EXTINF:-1 catchup-type="flussonic" catchup-days="{days}",Archive fixture
            https://example.invalid/a/channel/video.m3u8?token=fixture%2Bvalue
            """, new PlaylistParseContext(PlaylistSourceId.New()));
        Channel channel = Assert.Single(parsed.Channels);
        Programme programme = CreateProgramme();
        Channel? played = null;
        using ArchiveViewModel viewModel = new(new FakeEpg { Programmes = [programme] }, (selected, entry, cancellationToken) =>
        {
            Assert.True(CatchUpPlayback.TryCreatePlaybackChannel(selected, entry.Start,
                entry.Stop!.Value, Now, out played, out _));
            return Task.CompletedTask;
        }, () => Now);

        viewModel.UpdateChannels(parsed.Channels);
        await viewModel.LoadAsync();
        Assert.Same(channel, Assert.Single(viewModel.Channels));
        Assert.Same(programme, Assert.Single(viewModel.Programs).Programme);
        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.NotNull(played);
        Assert.Equal($"https://example.invalid/a/channel/video-{programme.Start.ToUnixTimeSeconds()}-{(long)(programme.Stop!.Value - programme.Start).TotalSeconds}.m3u8?token=fixture%2Bvalue", played.StreamUrl);
    }

    [Fact]
    public void UpdateChannels_DefaultsToConfiguredArchiveAndClearsUnsupportedOnlyPlaylist()
    {
        Channel supported = CreateChannel("Archiwum");
        Channel liveOnly = new(ChannelId.New(), PlaylistSourceId.New(), "Na żywo", "https://live.example.test/stream");
        using ArchiveViewModel viewModel = CreateViewModel(new FakeEpg());

        viewModel.UpdateChannels([liveOnly, supported]);

        Assert.Same(supported, Assert.Single(viewModel.Channels));
        Assert.False(viewModel.ShowChannelsWithoutArchive);
        Assert.Same(supported, Assert.Single(viewModel.VisibleChannels));
        Assert.Same(supported, viewModel.SelectedChannel);
        Assert.Contains("7 dni", viewModel.AvailabilityMessage, StringComparison.Ordinal);
        viewModel.UpdateChannels([liveOnly]);
        Assert.Empty(viewModel.Channels);
        Assert.Null(viewModel.SelectedChannel);
        Assert.Empty(viewModel.VisibleChannels);
        Assert.Contains("Brak kanałów ze skonfigurowanym archiwum", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.PlayCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("5")]
    public async Task RetentionOnlyDeclaration_OffersChannelAndCompletedProgramme(string days)
    {
        Channel channel = new(ChannelId.New(), PlaylistSourceId.New(), "Archiwum HLS",
            "https://live.example.test/a/b/playlist.m3u8?token=fixture",
            attributes: new Dictionary<string, string> { ["catchup-days"] = days });
        Channel liveOnly = new(ChannelId.New(), PlaylistSourceId.New(), "Na żywo", "https://live.example.test/stream");
        Channel optedOut = new(ChannelId.New(), PlaylistSourceId.New(), "Bez archiwum", "https://live.example.test/stream",
            attributes: new Dictionary<string, string> { ["catchup-days"] = days, ["catchup"] = "none" });
        Programme programme = CreateProgramme();
        Channel? playedChannel = null;
        Programme? playedProgramme = null;
        using ArchiveViewModel viewModel = new(new FakeEpg { Programmes = [programme] }, (selected, entry, _) =>
        {
            playedChannel = selected;
            playedProgramme = entry;
            return Task.CompletedTask;
        }, () => Now);

        viewModel.UpdateChannels([liveOnly, optedOut, channel]);
        await viewModel.LoadAsync();

        Assert.Same(channel, Assert.Single(viewModel.Channels));
        Assert.Same(channel, viewModel.SelectedChannel);
        Assert.Contains($"{days} dni", viewModel.AvailabilityMessage, StringComparison.Ordinal);
        Assert.Same(programme, Assert.Single(viewModel.Programs).Programme);
        Assert.True(viewModel.PlayCommand.CanExecute(null));
        await viewModel.PlayCommand.ExecuteAsync(null);
        Assert.Same(channel, playedChannel);
        Assert.Same(programme, playedProgramme);
    }

    [Fact]
    public async Task OptionalAllChannels_DoesNotInventArchiveAndFilterRestoresSupportedSelection()
    {
        Channel supported = CreateChannel();
        Channel liveOnly = new(ChannelId.New(), PlaylistSourceId.New(), "Bez deklaracji", "https://example.invalid/live");
        Channel optedOut = new(ChannelId.New(), PlaylistSourceId.New(), "Wyłączone archiwum", "https://example.invalid/live",
            attributes: new Dictionary<string, string> { ["catchup-days"] = "7", ["catchup"] = "none" });
        FakeEpg epg = new() { Programmes = [CreateProgramme()] };
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([liveOnly, supported, optedOut, supported]);

        Assert.False(viewModel.ShowChannelsWithoutArchive);
        Assert.Same(supported, Assert.Single(viewModel.VisibleChannels));
        viewModel.ShowChannelsWithoutArchive = true;
        Assert.True(viewModel.ShowChannelsWithoutArchive);
        Assert.Same(supported, Assert.Single(viewModel.Channels));
        Assert.Equal(new[] { liveOnly, supported, optedOut }, viewModel.VisibleChannels);
        Assert.Contains("1 z 3", viewModel.ChannelSummary, StringComparison.Ordinal);
        Assert.Contains(viewModel.ChannelSummary, viewModel.StatusMessage, StringComparison.Ordinal);
        int loads = epg.LoadCount;
        Assert.Same(supported, viewModel.SelectedChannel);
        Assert.Equal(loads, epg.LoadCount);
        viewModel.SelectedChannel = liveOnly;

        Assert.Equal(loads, epg.LoadCount);
        Assert.Empty(viewModel.Programs);
        Assert.Empty(viewModel.AvailableDates);
        Assert.Null(viewModel.SelectedDateOption);
        Assert.False(viewModel.LoadCommand.CanExecute(null));
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.PlayCommand.CanExecute(null));
        Assert.Contains("Playlista nie deklaruje", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Playlista nie deklaruje", viewModel.AvailabilityMessage, StringComparison.Ordinal);
        await viewModel.LoadAsync();
        await viewModel.RefreshAsync();
        Assert.Equal(loads, epg.LoadCount);
        Assert.Equal(0, epg.RefreshCount);
        viewModel.SelectedChannel = optedOut;
        Assert.Empty(viewModel.AvailableDates);
        Assert.Equal(loads, epg.LoadCount);

        viewModel.ShowChannelsWithoutArchive = false;

        Assert.Same(supported, viewModel.SelectedChannel);
        Assert.Same(supported, Assert.Single(viewModel.VisibleChannels));
        Assert.Equal(loads + 1, epg.LoadCount);
        Assert.NotEmpty(viewModel.AvailableDates);
        Assert.True(viewModel.PlayCommand.CanExecute(null));
    }

    [Fact]
    public void AllChannels_WithNoSupportedChannelExplainsMissingInformationAndClearsOnHiding()
    {
        Channel liveOnly = new(ChannelId.New(), PlaylistSourceId.New(), "Bez deklaracji", "https://example.invalid/live");
        FakeEpg epg = new();
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([liveOnly]);
        Assert.Empty(viewModel.VisibleChannels);
        viewModel.ShowChannelsWithoutArchive = true;
        Assert.Same(liveOnly, Assert.Single(viewModel.VisibleChannels));
        Assert.Same(liveOnly, viewModel.SelectedChannel);
        Assert.Contains("Playlista nie deklaruje", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.AvailableDates);
        viewModel.ShowChannelsWithoutArchive = false;
        Assert.Null(viewModel.SelectedChannel);
        Assert.Empty(viewModel.VisibleChannels);
        Assert.Equal(0, epg.LoadCount);
    }

    [Fact]
    public void LargePlaylist_DefaultsToFiveArchiveDeclarationsAndOptionCanShowEveryChannel()
    {
        Channel[] channels = Enumerable.Range(0, 162).Select(index => index < 5
            ? CreateChannel($"Archiwum {index}")
            : new Channel(ChannelId.New(), PlaylistSourceId.New(), $"Kanał {index}", "https://example.invalid/live")).ToArray();
        FakeEpg epg = new() { Programmes = [CreateProgramme()] };
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels(channels);

        Assert.Equal(5, viewModel.VisibleChannels.Count);
        viewModel.ShowChannelsWithoutArchive = true;
        Assert.Equal(162, viewModel.VisibleChannels.Count);
        Assert.Equal(5, viewModel.Channels.Count);
        Assert.Contains("5 z 162", viewModel.ChannelSummary, StringComparison.Ordinal);
        Assert.Contains("Na liście: 162", viewModel.ChannelSummary, StringComparison.Ordinal);
        viewModel.SelectedChannel = channels[161];
        Assert.False(viewModel.PlayCommand.CanExecute(null));
        Assert.Empty(viewModel.Programs);
        Assert.Contains("EPG nie potwierdza", viewModel.AvailabilityMessage, StringComparison.Ordinal);

        viewModel.ShowChannelsWithoutArchive = false;
        Assert.Equal(5, viewModel.VisibleChannels.Count);
        Assert.Contains("Na liście: 5", viewModel.ChannelSummary, StringComparison.Ordinal);
        viewModel.SelectedChannel = channels[3];
        viewModel.SelectedDateOption = viewModel.AvailableDates[2];
        DateTime? chosen = viewModel.SelectedDate;
        int loads = epg.LoadCount;
        viewModel.UpdateChannels(channels);

        Assert.Same(channels[3], viewModel.SelectedChannel);
        Assert.Equal(chosen, viewModel.SelectedDate);
        Assert.Equal(loads + 1, epg.LoadCount);
        viewModel.ShowChannelsWithoutArchive = true;
        Assert.Equal(162, viewModel.VisibleChannels.Count);
        Assert.Same(channels[3], viewModel.SelectedChannel);
        Assert.Equal(chosen, viewModel.SelectedDate);
        Assert.Equal(loads + 1, epg.LoadCount);
        Assert.Contains("Na liście: 162", viewModel.StatusMessage, StringComparison.Ordinal);
        viewModel.ShowChannelsWithoutArchive = false;
        Assert.Contains("Na liście: 5.", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Na liście: 162", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(loads + 1, epg.LoadCount);
    }

    [Theory]
    [InlineData(CatchUpSupportStatus.NotDeclared, "Playlista nie deklaruje")]
    [InlineData(CatchUpSupportStatus.Disabled, "Playlista oznacza archiwum tego kanału jako wyłączone")]
    [InlineData(CatchUpSupportStatus.MissingRetention, "nie określają liczby dostępnych dni")]
    [InlineData(CatchUpSupportStatus.InvalidRetention, "nieprawidłową liczbę dostępnych dni")]
    [InlineData(CatchUpSupportStatus.UnsupportedMode, "format archiwum nie jest obsługiwany")]
    [InlineData(CatchUpSupportStatus.InvalidTemplate, "Szablon adresu archiwum")]
    [InlineData(CatchUpSupportStatus.InvalidCorrection, "Korekta czasu archiwum")]
    [InlineData(CatchUpSupportStatus.UnsupportedStreamUrl, "Nie można zbudować adresu archiwum")]
    public async Task UnsupportedSelection_ExplainsSpecificReasonWithoutLoadingEpgOrExposingAttributes(CatchUpSupportStatus expected, string message)
    {
        Dictionary<string, string> attributes = new() { ["catchup"] = "shift", ["catchup-days"] = "7" };
        string url = "https://example.invalid/live?token=synthetic-secret";
        switch (expected)
        {
            case CatchUpSupportStatus.NotDeclared: attributes.Clear(); break;
            case CatchUpSupportStatus.Disabled: attributes["catchup"] = "none"; break;
            case CatchUpSupportStatus.MissingRetention: attributes.Remove("catchup-days"); break;
            case CatchUpSupportStatus.InvalidRetention: attributes["catchup-days"] = "synthetic-secret"; break;
            case CatchUpSupportStatus.UnsupportedMode: attributes["catchup"] = "synthetic-secret"; break;
            case CatchUpSupportStatus.InvalidTemplate: attributes["catchup-source"] = "https://example.invalid/{synthetic-secret}"; break;
            case CatchUpSupportStatus.InvalidCorrection: attributes["catchup-correction"] = "synthetic-secret"; break;
            case CatchUpSupportStatus.UnsupportedStreamUrl: url = "udp://example.invalid/live"; break;
        }
        Channel channel = new(ChannelId.New(), PlaylistSourceId.New(), "Kanał", url, attributes: attributes);
        FakeEpg epg = new() { Programmes = [CreateProgramme()] };
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.ShowChannelsWithoutArchive = true;
        viewModel.UpdateChannels([channel]);
        await viewModel.LoadAsync();
        await viewModel.RefreshAsync();

        Assert.Equal(expected, CatchUpPlayback.GetSupportStatus(channel));
        Assert.Contains(message, viewModel.AvailabilityMessage, StringComparison.Ordinal);
        Assert.Contains(message, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-secret", viewModel.AvailabilityMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-secret", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.Programs);
        Assert.Empty(viewModel.AvailableDates);
        Assert.False(viewModel.PlayCommand.CanExecute(null));
        Assert.Equal(0, epg.LoadCount);
        Assert.Equal(0, epg.RefreshCount);
    }

    [Fact]
    public void Availability_DistinguishesManualConfigurationAndManualDisableFromProviderMetadata()
    {
        using ArchiveViewModel viewModel = CreateViewModel(new FakeEpg());
        Channel declared = CreateChannel();
        viewModel.UpdateChannels([declared]);
        Assert.Contains("według danych playlisty", viewModel.AvailabilityMessage, StringComparison.Ordinal);

        Channel manual = CatchUpPlayback.ApplySourceSettings(declared,
            new CatchUpSourceSettings { Mode = CatchUpSourceMode.Shift, RetentionDays = 5, OverridePlaylist = true });
        viewModel.UpdateChannels([manual]);
        Assert.Contains("skonfigurowane ręcznie", viewModel.AvailabilityMessage, StringComparison.Ordinal);
        Assert.Contains("nie potwierdzają dostępności", viewModel.AvailabilityMessage, StringComparison.Ordinal);

        Channel disabled = CatchUpPlayback.ApplySourceSettings(declared, new CatchUpSourceSettings { Mode = CatchUpSourceMode.Disabled });
        viewModel.ShowChannelsWithoutArchive = true;
        viewModel.UpdateChannels([disabled]);
        Assert.Contains("wyłączone w ustawieniach", viewModel.AvailabilityMessage, StringComparison.Ordinal);
        Assert.Contains("wyłączone w ustawieniach", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.PlayCommand.CanExecute(null));
    }

    [Fact]
    public async Task Load_ExcludesCurrentFutureExpiredMissingEndAndZeroDurationProgrammes()
    {
        Programme valid = CreateProgramme("Zakończona", Now.AddHours(-2), Now.AddHours(-1));
        FakeEpg epg = new()
        {
            Programmes = [
                valid,
                CreateProgramme("Trwa", Now.AddMinutes(-15), Now.AddMinutes(45)),
                CreateProgramme("Przyszła", Now.AddHours(1), Now.AddHours(2)),
                CreateProgramme("Wygasła", Now.AddDays(-8), Now.AddDays(-8).AddHours(1)),
                CreateProgramme("Bez końca", Now.AddHours(-4), null),
                CreateProgramme("Pusta", Now.AddHours(-3), Now.AddHours(-3)),
            ],
        };
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([CreateChannel()]);

        await viewModel.LoadAsync();

        Assert.Same(valid, Assert.Single(viewModel.Programs).Programme);
        Assert.True(viewModel.PlayCommand.CanExecute(null));
    }

    [Fact]
    public async Task Play_PassesOriginalChannelAndProgrammeToArchiveCallback()
    {
        Channel channel = CreateChannel();
        Programme programme = CreateProgramme();
        Channel? playedChannel = null;
        Programme? playedProgramme = null;
        using ArchiveViewModel viewModel = new(new FakeEpg { Programmes = [programme] }, (selected, entry, token) =>
        {
            token.ThrowIfCancellationRequested();
            playedChannel = selected;
            playedProgramme = entry;
            return Task.CompletedTask;
        }, () => Now);
        viewModel.UpdateChannels([channel]);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.Same(channel, playedChannel);
        Assert.Same(programme, playedProgramme);
    }

    [Fact]
    public async Task Play_RechecksRetentionWhenTimeHasAdvanced()
    {
        DateTimeOffset clock = Now;
        int playCount = 0;
        using ArchiveViewModel viewModel = new(new FakeEpg { Programmes = [CreateProgramme()] }, (_, _, _) =>
        {
            playCount++;
            return Task.CompletedTask;
        }, () => clock);
        viewModel.UpdateChannels([CreateChannel()]);
        Assert.True(viewModel.PlayCommand.CanExecute(null));
        clock = Now.AddDays(8);

        Assert.False(viewModel.PlayCommand.CanExecute(null));
        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.Equal(0, playCount);
        Assert.Contains("nie jest już dostępna", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedDate_ClearsOldSelectionAndLoadsOnlyOnRequest()
    {
        FakeEpg epg = new() { Programmes = [CreateProgramme()] };
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([CreateChannel()]);
        int loads = epg.LoadCount;

        viewModel.SelectedDate = Now.LocalDateTime.Date.AddDays(-1);

        Assert.Empty(viewModel.Programs);
        Assert.Null(viewModel.SelectedProgram);
        Assert.False(viewModel.PlayCommand.CanExecute(null));
        Assert.Equal(loads, epg.LoadCount);
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.Equal(loads + 1, epg.LoadCount);
        Assert.Equal(DateOnly.FromDateTime(Now.LocalDateTime.Date.AddDays(-1)), epg.LastDate);
    }

    [Fact]
    public async Task DateOptions_UseRetentionWindowAndSelectionLoadsOnceWhileDayButtonsStaySynchronized()
    {
        FakeEpg epg = new();
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([CreateChannel()]);
        DateTime today = Now.LocalDateTime.Date;
        Assert.Equal(Enumerable.Range(0, 8).Select(days => today.AddDays(-days)), viewModel.AvailableDates.Select(option => option.Date));
        Assert.Same(viewModel.AvailableDates[0], viewModel.SelectedDateOption);
        Assert.Equal(today.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture), viewModel.AvailableDates[0].DisplayName);
        int loads = epg.LoadCount;

        viewModel.SelectedDateOption = viewModel.AvailableDates[2];

        Assert.Equal(loads + 1, epg.LoadCount);
        Assert.Equal(today.AddDays(-2), viewModel.SelectedDate);
        Assert.Equal(DateOnly.FromDateTime(today.AddDays(-2)), epg.LastDate);
        viewModel.SelectedDateOption = viewModel.AvailableDates[2];
        Assert.Equal(loads + 1, epg.LoadCount);
        await viewModel.PreviousDayCommand.ExecuteAsync(null);
        Assert.Equal(loads + 2, epg.LoadCount);
        Assert.Same(viewModel.AvailableDates[3], viewModel.SelectedDateOption);
        await viewModel.NextDayCommand.ExecuteAsync(null);
        Assert.Equal(loads + 3, epg.LoadCount);
        Assert.Same(viewModel.AvailableDates[2], viewModel.SelectedDateOption);
        viewModel.SelectedDateOption = viewModel.AvailableDates[^1];
        Assert.False(viewModel.PreviousDayCommand.CanExecute(null));
        viewModel.SelectedDateOption = viewModel.AvailableDates[0];
        Assert.False(viewModel.NextDayCommand.CanExecute(null));
    }

    [Fact]
    public void DateOptions_ChannelChangesRetainValidDateClampExpiredDateAndClearWithoutChannels()
    {
        Channel longArchive = CreateChannel();
        Channel shortArchive = new(ChannelId.New(), PlaylistSourceId.New(), "Krótka retencja", "https://example.invalid/stream",
            attributes: new Dictionary<string, string> { ["catchup-days"] = "2.5" });
        FakeEpg epg = new();
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([longArchive, shortArchive]);
        viewModel.SelectedDateOption = viewModel.AvailableDates[2];
        int loads = epg.LoadCount;

        viewModel.SelectedChannel = shortArchive;

        Assert.Equal(loads + 1, epg.LoadCount);
        Assert.Equal(4, viewModel.AvailableDates.Count);
        Assert.Equal(Now.LocalDateTime.Date.AddDays(-2), viewModel.SelectedDateOption?.Date);
        viewModel.SelectedChannel = longArchive;
        viewModel.SelectedDateOption = viewModel.AvailableDates[6];
        loads = epg.LoadCount;
        viewModel.SelectedChannel = shortArchive;
        Assert.Equal(loads + 1, epg.LoadCount);
        Assert.Equal(Now.LocalDateTime.Date.AddDays(-3), viewModel.SelectedDate);
        Assert.Same(viewModel.AvailableDates[^1], viewModel.SelectedDateOption);
        viewModel.SelectedDate = null;
        viewModel.UpdateChannels([shortArchive]);
        Assert.Same(viewModel.AvailableDates[0], viewModel.SelectedDateOption);
        Assert.Equal(Now.LocalDateTime.Date, viewModel.SelectedDate);
        viewModel.UpdateChannels([]);
        Assert.Empty(viewModel.AvailableDates);
        Assert.Null(viewModel.SelectedDateOption);
        Assert.False(viewModel.PreviousDayCommand.CanExecute(null));
        Assert.False(viewModel.NextDayCommand.CanExecute(null));
    }

    [Fact]
    public void DateOptions_IgnoreForeignAndTransientNullSelections()
    {
        FakeEpg epg = new();
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([CreateChannel()]);
        ArchiveDateOption? selected = viewModel.SelectedDateOption;
        int loads = epg.LoadCount;

        viewModel.SelectedDateOption = null;
        viewModel.SelectedDateOption = new ArchiveDateOption(Now.LocalDateTime.Date.AddDays(1));

        Assert.Same(selected, viewModel.SelectedDateOption);
        Assert.Equal(loads, epg.LoadCount);
    }

    [Fact]
    public async Task LateLoadFromRemovedSource_CannotReplaceNewArchive()
    {
        TaskCompletionSource<IReadOnlyList<Programme>> oldResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Programme current = CreateProgramme("Nowa playlista");
        FakeEpg epg = new() { Loader = (channel, _) => channel.Name == "Pierwszy" ? oldResult.Task : Task.FromResult<IReadOnlyList<Programme>>([current]) };
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([CreateChannel("Pierwszy")]);
        Task oldLoad = viewModel.LoadAsync();

        Channel replacement = CreateChannel("Drugi");
        viewModel.UpdateChannels([replacement]);
        oldResult.SetResult([CreateProgramme("Stara playlista")]);
        await oldLoad;

        Assert.Same(replacement, viewModel.SelectedChannel);
        Assert.Same(current, Assert.Single(viewModel.Programs).Programme);
    }

    [Fact]
    public async Task Refresh_PreservesSelectedProgrammeAndHandlesFailureWithoutSecrets()
    {
        Programme first = CreateProgramme("Pierwsza", Now.AddHours(-4), Now.AddHours(-3));
        Programme second = CreateProgramme("Druga");
        FakeEpg epg = new() { Programmes = [first, second] };
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([CreateChannel()]);
        viewModel.SelectedProgram = viewModel.Programs[1];

        await viewModel.RefreshAsync();

        Assert.Equal(1, epg.RefreshCount);
        Assert.Same(second, viewModel.SelectedProgram?.Programme);
        epg.RefreshFailure = true;
        await viewModel.RefreshAsync();
        Assert.Empty(viewModel.Programs);
        Assert.Contains("Nie udało", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-secret", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_AfterDateChangesOffersNewTodayAndRetainsValidSelectedDateWithoutExtraLoad()
    {
        DateTimeOffset currentTime = Now;
        FakeEpg epg = new();
        using ArchiveViewModel viewModel = new(epg, (_, _, _) => Task.CompletedTask, () => currentTime);
        viewModel.UpdateChannels([CreateChannel()]);
        DateTime? previousDate = viewModel.SelectedDate;
        int loads = epg.LoadCount;
        currentTime = Now.AddDays(1);

        await viewModel.RefreshAsync();

        Assert.Equal(currentTime.LocalDateTime.Date, viewModel.AvailableDates[0].Date);
        Assert.Equal(previousDate, viewModel.SelectedDate);
        Assert.Same(viewModel.AvailableDates[1], viewModel.SelectedDateOption);
        Assert.Equal(loads + 1, epg.LoadCount);
        Assert.Equal(1, epg.RefreshCount);
        Assert.Equal(DateOnly.FromDateTime(previousDate!.Value), epg.LastDate);
        Assert.True(viewModel.NextDayCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dispose_CancelsOutstandingLoadAndDoesNotPublishLateResult()
    {
        TaskCompletionSource<IReadOnlyList<Programme>> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeEpg epg = new() { Loader = (_, _) => pending.Task };
        ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([CreateChannel()]);
        Task load = viewModel.LoadAsync();
        CancellationToken token = epg.LastToken;

        viewModel.Dispose();
        pending.SetResult([CreateProgramme()]);
        await load;

        Assert.True(token.IsCancellationRequested);
        Assert.Empty(viewModel.Programs);
        Assert.False(viewModel.LoadCommand.CanExecute(null));
    }

    [Fact]
    public async Task DayNavigation_QueriesChosenDateAndPreventsNextDayFromToday()
    {
        FakeEpg epg = new();
        using ArchiveViewModel viewModel = CreateViewModel(epg);
        viewModel.UpdateChannels([CreateChannel()]);
        Assert.False(viewModel.NextDayCommand.CanExecute(null));

        await viewModel.PreviousDayCommand.ExecuteAsync(null);

        Assert.Equal(DateOnly.FromDateTime(Now.LocalDateTime.Date.AddDays(-1)), epg.LastDate);
        Assert.True(viewModel.NextDayCommand.CanExecute(null));
        await viewModel.NextDayCommand.ExecuteAsync(null);
        Assert.Equal(DateOnly.FromDateTime(Now.LocalDateTime.Date), epg.LastDate);
        Assert.False(viewModel.NextDayCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task PendingPlay_IsCancelledWhenArchiveContextChanges(int change)
    {
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken playbackToken = default;
        Channel first = CreateChannel("Pierwszy");
        Channel second = CreateChannel("Drugi");
        using ArchiveViewModel viewModel = new(new FakeEpg { Programmes = [CreateProgramme()] }, (_, _, token) =>
        {
            playbackToken = token;
            return ready.Task;
        }, () => Now);
        viewModel.UpdateChannels([first, second]);
        Task playback = viewModel.PlayCommand.ExecuteAsync(null);
        Assert.False(playbackToken.IsCancellationRequested);

        switch (change)
        {
            case 0:
                viewModel.SelectedDate = Now.LocalDateTime.Date.AddDays(-1);
                break;
            case 1:
                viewModel.SelectedChannel = second;
                break;
            case 2:
                // Even reusing the selected channel object represents replaced source data.
                viewModel.UpdateChannels([first]);
                break;
            default:
                viewModel.Dispose();
                break;
        }
        Assert.True(playbackToken.IsCancellationRequested);
        ready.SetResult();
        await playback;

        Assert.DoesNotContain("Uruchomiono archiwum", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DateNavigation_DoesNotOverflowAtMinimumDateOrAdvanceBeyondToday()
    {
        using ArchiveViewModel viewModel = CreateViewModel(new FakeEpg());
        viewModel.UpdateChannels([CreateChannel()]);
        viewModel.SelectedDate = DateTime.MinValue;

        Assert.False(viewModel.PreviousDayCommand.CanExecute(null));
        await viewModel.PreviousDayCommand.ExecuteAsync(null);
        Assert.Equal(DateTime.MinValue, viewModel.SelectedDate);
        viewModel.SelectedDate = Now.LocalDateTime.Date;
        await viewModel.NextDayCommand.ExecuteAsync(null);
        Assert.Equal(Now.LocalDateTime.Date, viewModel.SelectedDate);
    }

    internal static Channel CreateChannel(string name = "Kanał archiwalny") => new(
        ChannelId.New(), PlaylistSourceId.New(), name, "https://live.example.test/stream", attributes: new Dictionary<string, string>
        {
            ["catchup"] = "default",
            ["catchup-days"] = "7",
            ["catchup-source"] = "https://archive.example.test/replay/{utc}/{duration}",
        });

    internal static Programme CreateProgramme(string title = "Audycja", DateTimeOffset? start = null, DateTimeOffset? stop = null) =>
        new("epg-channel", start ?? Now.AddHours(-2), stop ?? (start is null ? Now.AddHours(-1) : null), [new EpgLocalizedText(title)]);

    private static ArchiveViewModel CreateViewModel(FakeEpg epg) => new(epg, (_, _, _) => Task.CompletedTask, () => Now);

    internal sealed class FakeEpg : IEpgSourceManager
    {
        public IReadOnlyList<Programme> Programmes { get; set; } = [];
        public Func<Channel, CancellationToken, Task<IReadOnlyList<Programme>>>? Loader { get; set; }
        public int LoadCount { get; private set; }
        public int RefreshCount { get; private set; }
        public bool RefreshFailure { get; set; }
        public DateOnly LastDate { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public string Source => "https://epg.example.test/guide.xml";
        public IReadOnlyList<string> Sources => [Source];
        public EpgServiceStatus Status => new(EpgServiceState.Ready);
        public Task SetSourcesAsync(IEnumerable<string> sources, Uri? baseUri = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Programme? GetCurrentProgramme(Channel channel, DateTimeOffset at) => null;

        public Task<IReadOnlyList<Programme>> GetProgrammesAsync(Channel channel, DateOnly scheduleDate, string? search = null, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            LastDate = scheduleDate;
            LastToken = cancellationToken;
            return Loader?.Invoke(channel, cancellationToken) ?? Task.FromResult(Programmes);
        }

        public Task<EpgRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return RefreshFailure ? throw new IOException("synthetic-secret") : Task.FromResult(EpgRefreshResult.Success);
        }
    }
}
