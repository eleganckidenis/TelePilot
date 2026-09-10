using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelePilot.App.Accessibility;
using TelePilot.App.Collections;
using TelePilot.App.Models;
using TelePilot.App.Services;
using TelePilot.Core.Channels;
using TelePilot.Core.Contracts;
using TelePilot.Core.Models;
using TelePilot.Epg.Models;
using TelePilot.Epg.Services;
using TelePilot.Recording;

namespace TelePilot.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    public const string AllGroupsLabel = "Wszystkie grupy";

    private static readonly TimeSpan CurrentProgramRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly IPlaylistService playlistService;
    private readonly IPlaylistSourceStore sourceStore;
    private readonly IPlaybackService playbackService;
    private readonly IEpgSourceManager epgService;
    private readonly IWindowService windowService;
    private readonly IAccessibleStatusService accessibleStatus;
    private readonly IFocusCoordinator focusCoordinator;
    private readonly IAppPreferencesService preferencesService;
    private readonly IRecordingCoordinator recordingCoordinator;
    private readonly DispatcherTimer currentProgramRefreshTimer;
    private readonly object playbackSnapshotGate = new();
    private readonly List<Channel> allChannels = [];
    private readonly Dictionary<string, Channel> channelsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChannelListItemViewModel> channelItemsById = new(StringComparer.Ordinal);
    private CancellationTokenSource? playlistLoadCancellation;
    private CancellationTokenSource? searchFilterCancellation;
    private PlaylistSourceItemViewModel? selectedSource;
    private ChannelListItemViewModel? selectedChannel;
    private string searchText = string.Empty;
    private string selectedGroup = AllGroupsLabel;
    private ChannelSortOption selectedSort;
    private string currentChannelName = "Nic nie jest odtwarzane";
    private string currentProgramName = "Brak danych o bieżącej audycji";
    private string playbackStateText = "Zatrzymano";
    private int volume = 100;
    private bool isMuted;
    private bool isLoading;
    private bool isPlayerViewVisible;
    private bool isChannelListViewVisible = true;
    private bool isFavoritesOnly;
    private bool isRecording;
    private string recordingStatusText = "Nagrywanie nieaktywne";
    private int playerViewActivationGeneration;
    private string? lastSelectedChannelId;
    private string? playbackChannelItemId;
    private PlaybackSnapshot? lastReceivedPlaybackSnapshot;
    private bool disposed;

    public MainWindowViewModel(
        IPlaylistService playlistService,
        IPlaylistSourceStore sourceStore,
        IPlaybackService playbackService,
        IEpgSourceManager epgService,
        IWindowService windowService,
        IAccessibleStatusService accessibleStatus,
        IFocusCoordinator focusCoordinator,
        IAppPreferencesService? preferencesService = null,
        IRecordingCoordinator? recordingCoordinator = null)
    {
        this.playlistService = playlistService ?? throw new ArgumentNullException(nameof(playlistService));
        this.sourceStore = sourceStore ?? throw new ArgumentNullException(nameof(sourceStore));
        this.playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        this.epgService = epgService ?? throw new ArgumentNullException(nameof(epgService));
        this.windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
        this.accessibleStatus = accessibleStatus ?? throw new ArgumentNullException(nameof(accessibleStatus));
        this.focusCoordinator = focusCoordinator ?? throw new ArgumentNullException(nameof(focusCoordinator));
        this.preferencesService = preferencesService ?? new InMemoryAppPreferencesService();
        this.recordingCoordinator = recordingCoordinator ?? new UnavailableRecordingCoordinator();
        currentProgramRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = CurrentProgramRefreshInterval,
        };
        currentProgramRefreshTimer.Tick += OnCurrentProgramRefreshTimerTick;

        Sources = [];
        Channels = [];
        Groups = [AllGroupsLabel];
        FavoriteChannels = [];
        SortOptions =
        [
            new ChannelSortOption(ChannelSortMode.SourceOrder, "Kolejność z playlisty"),
            new ChannelSortOption(ChannelSortMode.Name, "Nazwa kanału"),
            new ChannelSortOption(ChannelSortMode.GroupThenName, "Grupa, potem nazwa"),
        ];
        selectedSort = SortOptions[0];

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        AddFileSourceCommand = new AsyncRelayCommand(AddFileSourcesAsync);
        AddUrlSourceCommand = new AsyncRelayCommand(AddUrlSourceAsync);
        RefreshSourceCommand = new AsyncRelayCommand(RefreshSelectedSourceAsync, () => SelectedSource is not null && !IsLoading);
        RemoveSourceCommand = new AsyncRelayCommand(RemoveSelectedSourceAsync, () => SelectedSource is not null && !IsLoading);
        CancelLoadingCommand = new RelayCommand(CancelLoading, () => IsLoading);
        PlaySelectedCommand = new AsyncRelayCommand(PlaySelectedAsync, () => SelectedChannel is not null && !IsLoading);
        StopCommand = new AsyncRelayCommand(StopAsync);
        PauseResumeCommand = new AsyncRelayCommand(PauseResumeAsync);
        ToggleMuteCommand = new AsyncRelayCommand(ToggleMuteAsync);
        VolumeUpCommand = new AsyncRelayCommand(() => ChangeVolumeAsync(5));
        VolumeDownCommand = new AsyncRelayCommand(() => ChangeVolumeAsync(-5));
        PreviousChannelCommand = new AsyncRelayCommand(() => PlayAdjacentChannelAsync(-1));
        NextChannelCommand = new AsyncRelayCommand(() => PlayAdjacentChannelAsync(1));
        OpenAudioTracksCommand = new AsyncRelayCommand(() => OpenTracksAsync(MediaTrackKind.Audio));
        OpenSubtitleTracksCommand = new AsyncRelayCommand(() => OpenTracksAsync(MediaTrackKind.Subtitle));
        ReturnToChannelListCommand = new RelayCommand(ReturnToChannelList);
        ToggleChannelListCommand = new RelayCommand(ToggleChannelList);
        OpenProgramGuideCommand = new AsyncRelayCommand(OpenProgramGuideAsync);
        OpenKeyboardShortcutsCommand = new AsyncRelayCommand(windowService.ShowKeyboardShortcutsAsync);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleSelectedFavoriteAsync, () => SelectedChannel is not null);
        AddCurrentToFavoritesCommand = new AsyncRelayCommand(AddCurrentChannelToFavoritesAsync);
        ToggleFavoritesFilterCommand = new RelayCommand(ToggleFavoritesFilter);
        StartVideoRecordingCommand = new AsyncRelayCommand(
            () => StartRecordingAsync(RecordingMode.StreamCopy),
            () => !IsRecording);
        StartAudioRecordingCommand = new AsyncRelayCommand(
            () => StartRecordingAsync(RecordingMode.AudioOnly),
            () => !IsRecording);
        StopRecordingCommand = new AsyncRelayCommand(StopRecordingAsync, () => IsRecording);
        ScheduleRecordingCommand = new AsyncRelayCommand(ScheduleSelectedRecordingAsync, () => SelectedChannel is not null);
        ShowScheduledRecordingsCommand = new AsyncRelayCommand(ShowScheduledRecordingsAsync);
        ToggleFullscreenCommand = new RelayCommand(() => FullscreenRequested?.Invoke(this, EventArgs.Empty));
        ExitFullscreenCommand = new RelayCommand(() => ExitFullscreenRequested?.Invoke(this, EventArgs.Empty));
        CloseApplicationCommand = new AsyncRelayCommand(RequestCloseAsync);

        playbackService.StateChanged += OnPlaybackStateChanged;
        this.recordingCoordinator.ProgressChanged += OnRecordingProgressChanged;
    }

    public event EventHandler? FullscreenRequested;

    public event EventHandler? ExitFullscreenRequested;

    public event EventHandler? PlayerViewRequested;

    public event EventHandler? CloseRequested;

    public ObservableCollection<PlaylistSourceItemViewModel> Sources { get; }

    public ResettableObservableCollection<ChannelListItemViewModel> Channels { get; }

    public ObservableCollection<string> Groups { get; }

    public ObservableCollection<FavoriteChannelPreference> FavoriteChannels { get; }

    public ObservableCollection<ChannelSortOption> SortOptions { get; }

    public IAccessibleStatusService AccessibleStatus => accessibleStatus;

    public PlaylistSourceItemViewModel? SelectedSource
    {
        get => selectedSource;
        set
        {
            if (SetProperty(ref selectedSource, value))
            {
                OnPropertyChanged(nameof(SourceSelectorAccessibleName));
                RefreshCommandStates();
            }
        }
    }

    public ChannelListItemViewModel? SelectedChannel
    {
        get => selectedChannel;
        set
        {
            if (SetProperty(ref selectedChannel, value))
            {
                lastSelectedChannelId = value?.Id ?? lastSelectedChannelId;
                OnPropertyChanged(nameof(SelectedFavoriteMenuText));
                RefreshCommandStates();
            }
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                ScheduleChannelFilter();
            }
        }
    }

    public string SelectedGroup
    {
        get => selectedGroup;
        set
        {
            var effective = string.IsNullOrWhiteSpace(value) ? AllGroupsLabel : value;
            if (SetProperty(ref selectedGroup, effective))
            {
                OnPropertyChanged(nameof(GroupSelectorAccessibleName));
                ApplyChannelFilter();
            }
        }
    }

    public ChannelSortOption SelectedSort
    {
        get => selectedSort;
        set
        {
            if (value is not null && SetProperty(ref selectedSort, value))
            {
                ApplyChannelFilter();
            }
        }
    }

    public bool IsFavoritesOnly
    {
        get => isFavoritesOnly;
        private set
        {
            if (SetProperty(ref isFavoritesOnly, value))
            {
                OnPropertyChanged(nameof(FavoritesButtonAccessibleName));
            }
        }
    }

    public bool IsRecording
    {
        get => isRecording;
        private set
        {
            if (SetProperty(ref isRecording, value))
            {
                OnPropertyChanged(nameof(RecordingButtonText));
                RefreshCommandStates();
            }
        }
    }

    public string RecordingStatusText
    {
        get => recordingStatusText;
        private set => SetProperty(ref recordingStatusText, value);
    }

    public string CurrentChannelName
    {
        get => currentChannelName;
        private set => SetProperty(ref currentChannelName, value);
    }

    public string CurrentProgramName
    {
        get => currentProgramName;
        private set => SetProperty(ref currentProgramName, value);
    }

    public string PlaybackStateText
    {
        get => playbackStateText;
        private set => SetProperty(ref playbackStateText, value);
    }

    public int Volume
    {
        get => volume;
        private set
        {
            if (SetProperty(ref volume, value))
            {
                OnPropertyChanged(nameof(VolumeText));
            }
        }
    }

    public bool IsMuted
    {
        get => isMuted;
        private set
        {
            if (SetProperty(ref isMuted, value))
            {
                OnPropertyChanged(nameof(VolumeText));
                OnPropertyChanged(nameof(MuteButtonText));
            }
        }
    }

    public bool IsLoading
    {
        get => isLoading;
        private set
        {
            if (SetProperty(ref isLoading, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public bool IsPlayerViewVisible
    {
        get => isPlayerViewVisible;
        private set
        {
            if (SetProperty(ref isPlayerViewVisible, value))
            {
                NotifyContentLayoutChanged();
            }
        }
    }

    public bool IsChannelListViewVisible
    {
        get => isChannelListViewVisible;
        private set
        {
            if (SetProperty(ref isChannelListViewVisible, value))
            {
                NotifyContentLayoutChanged();
            }
        }
    }

    public int ChannelListColumnSpan => IsPlayerViewVisible ? 1 : 2;

    public int PlayerColumn => IsChannelListViewVisible ? 1 : 0;

    public int PlayerColumnSpan => IsChannelListViewVisible ? 1 : 2;

    public string SourceSelectorAccessibleName => SelectedSource is null
        ? "Źródło playlisty, nie wybrano"
        : $"Źródło playlisty, wybrano: {SelectedSource.AccessibleName}";

    public string GroupSelectorAccessibleName => $"Grupa kanałów, wybrano: {SelectedGroup}";

    public string FavoritesButtonAccessibleName => IsFavoritesOnly
        ? "Ulubione, pokazano tylko ulubione kanały; naciśnij, aby pokazać wszystkie"
        : "Ulubione, naciśnij, aby pokazać tylko ulubione kanały";

    public string SelectedFavoriteMenuText => SelectedChannel?.IsFavorite == true
        ? "Usuń z ulubionych"
        : "Dodaj do ulubionych";

    public string RecordingButtonText => IsRecording ? "Nagrywanie — zatrzymaj lub wybierz opcję…" : "Nagrywanie…";

    public string VolumeText => IsMuted ? $"Głośność {Volume} procent, wyciszona" : $"Głośność {Volume} procent";

    public string MuteButtonText => IsMuted ? "Włącz dźwięk" : "Wycisz";

    public string ChannelCountText => Channels.Count switch
    {
        0 => "Brak kanałów",
        1 => "1 kanał",
        _ => $"{Channels.Count} kanałów",
    };

    public IAsyncRelayCommand InitializeCommand { get; }

    public IAsyncRelayCommand AddFileSourceCommand { get; }

    public IAsyncRelayCommand AddUrlSourceCommand { get; }

    public IAsyncRelayCommand RefreshSourceCommand { get; }

    public IAsyncRelayCommand RemoveSourceCommand { get; }

    public IRelayCommand CancelLoadingCommand { get; }

    public IAsyncRelayCommand PlaySelectedCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }

    public IAsyncRelayCommand PauseResumeCommand { get; }

    public IAsyncRelayCommand ToggleMuteCommand { get; }

    public IAsyncRelayCommand VolumeUpCommand { get; }

    public IAsyncRelayCommand VolumeDownCommand { get; }

    public IAsyncRelayCommand PreviousChannelCommand { get; }

    public IAsyncRelayCommand NextChannelCommand { get; }

    public IAsyncRelayCommand OpenAudioTracksCommand { get; }

    public IAsyncRelayCommand OpenSubtitleTracksCommand { get; }

    public IRelayCommand ReturnToChannelListCommand { get; }

    public IRelayCommand ToggleChannelListCommand { get; }

    public IAsyncRelayCommand OpenProgramGuideCommand { get; }

    public IAsyncRelayCommand OpenKeyboardShortcutsCommand { get; }

    public IAsyncRelayCommand OpenSettingsCommand { get; }

    public IAsyncRelayCommand ToggleFavoriteCommand { get; }

    public IAsyncRelayCommand AddCurrentToFavoritesCommand { get; }

    public IRelayCommand ToggleFavoritesFilterCommand { get; }

    public IAsyncRelayCommand StartVideoRecordingCommand { get; }

    public IAsyncRelayCommand StartAudioRecordingCommand { get; }

    public IAsyncRelayCommand StopRecordingCommand { get; }

    public IAsyncRelayCommand ScheduleRecordingCommand { get; }

    public IAsyncRelayCommand ShowScheduledRecordingsCommand { get; }

    public IRelayCommand ToggleFullscreenCommand { get; }

    public IRelayCommand ExitFullscreenCommand { get; }

    public IAsyncRelayCommand CloseApplicationCommand { get; }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsRecording)
        {
            return true;
        }

        var confirmed = await windowService.ShowConfirmationAsync(
            "Nagrywanie trwa",
            "Zamknięcie TelePilot zatrzyma bieżące nagrywanie i zapisze dotychczasowy materiał. Czy chcesz kontynuować?",
            "Zatrzymaj nagrywanie i zamknij",
            "Pozostaw program otwarty");
        if (!confirmed)
        {
            return false;
        }

        try
        {
            await recordingCoordinator.StopAsync();
            return true;
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się zakończyć nagrywania", exception.Message);
            return false;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await preferencesService.LoadAsync();
            SelectedSort = SortOptions.FirstOrDefault(
                option => option.Mode == preferencesService.Current.ChannelSortMode) ?? SortOptions[0];
            ReloadFavoriteChannels();

            var sources = await sourceStore.GetAllAsync();
            Sources.Clear();
            foreach (var source in sources)
            {
                Sources.Add(new PlaylistSourceItemViewModel(source));
            }

            SelectedSource = Sources.FirstOrDefault();
            if (SelectedSource is null)
            {
                accessibleStatus.ReportStatus("Dodaj playlistę z pliku lub adresu URL, aby wyświetlić kanały.");
                return;
            }

            await LoadSourceAsync(SelectedSource.Source);
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się uruchomić aplikacji", exception.Message);
        }
    }

    public ChannelListItemViewModel? SelectNextChannelStartingWith(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        var normalizedPrefix = ChannelTextNormalizer.NormalizeForSearch(prefix);
        if (normalizedPrefix.Length == 0 || Channels.Count == 0)
        {
            return null;
        }

        var selectedIndex = SelectedChannel is null ? -1 : Channels.IndexOf(SelectedChannel);
        for (var offset = 1; offset <= Channels.Count; offset++)
        {
            var candidate = Channels[(selectedIndex + offset) % Channels.Count];
            if (!ChannelTextNormalizer.NormalizeForSearch(candidate.Name)
                    .StartsWith(normalizedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            SelectedChannel = candidate;
            return candidate;
        }

        return null;
    }

    public async Task PlayFavoriteAsync(Guid playlistSourceId, Guid channelId)
    {
        var sourceItem = Sources.FirstOrDefault(item => item.Source.Id.Value == playlistSourceId);
        if (sourceItem is null)
        {
            await windowService.ShowMessageAsync(
                "Ulubiony kanał niedostępny",
                "Źródło tego ulubionego kanału nie jest już dostępne.");
            return;
        }

        if (SelectedSource?.Source.Id.Value != playlistSourceId)
        {
            SelectedSource = sourceItem;
            await LoadSourceAsync(sourceItem.Source);
        }

        var channelIdText = channelId.ToString("D");
        if (!channelsById.TryGetValue(channelIdText, out var channel)
            || !channelItemsById.TryGetValue(channelIdText, out var channelItem))
        {
            await windowService.ShowMessageAsync(
                "Ulubiony kanał niedostępny",
                "Kanał nie występuje już w wybranym źródle. Możesz usunąć go z ulubionych.");
            return;
        }

        IsFavoritesOnly = false;
        ApplyChannelFilter();
        SelectedChannel = Channels.FirstOrDefault(item => item.Id == channelIdText) ?? channelItem;
        await PlayChannelAsync(channelItem, channel, requestFocus: true);
    }

    private async Task OpenSettingsAsync()
    {
        var result = await windowService.ShowSettingsAsync(
            new SettingsWindowViewModel(preferencesService.Current));
        if (result is null)
        {
            return;
        }

        try
        {
            await preferencesService.SaveAsync(result);
            SelectedSort = SortOptions.First(option => option.Mode == preferencesService.Current.ChannelSortMode);
            ReloadFavoriteChannels();
            ApplyChannelFilter();
            accessibleStatus.ReportStatus("Zapisano ustawienia.");
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się zapisać ustawień", exception.Message);
        }
    }

    private async Task RequestCloseAsync()
    {
        if (await ConfirmCloseAsync())
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ToggleSelectedFavoriteAsync()
    {
        if (SelectedChannel is null || !channelsById.TryGetValue(SelectedChannel.Id, out var channel))
        {
            accessibleStatus.ReportError("Wybierz kanał, który chcesz dodać do ulubionych.");
            return;
        }

        await ToggleFavoriteAsync(channel);
    }

    private async Task AddCurrentChannelToFavoritesAsync()
    {
        var currentId = playbackService.Current.ChannelId?.ToString() ?? playbackChannelItemId;
        if (currentId is null || !channelsById.TryGetValue(currentId, out var channel))
        {
            accessibleStatus.ReportError("Żaden kanał nie jest obecnie odtwarzany.");
            return;
        }

        if (IsFavorite(channel))
        {
            accessibleStatus.ReportStatus($"Kanał {channel.Name} jest już w ulubionych.");
            return;
        }

        await SetFavoriteAsync(channel, isFavorite: true);
    }

    private Task ToggleFavoriteAsync(Channel channel) =>
        SetFavoriteAsync(channel, !IsFavorite(channel));

    private async Task SetFavoriteAsync(Channel channel, bool isFavorite)
    {
        try
        {
            var current = preferencesService.Current;
            var favorites = current.Favorites
                .Where(favorite => favorite.PlaylistSourceId != channel.PlaylistSourceId.Value
                    || favorite.ChannelId != channel.Id.Value)
                .ToList();
            if (isFavorite)
            {
                favorites.Add(new FavoriteChannelPreference(
                    channel.PlaylistSourceId.Value,
                    channel.Id.Value,
                    channel.Name));
            }

            await preferencesService.SaveAsync(
                new AppPreferences
                {
                    ChannelSortMode = current.ChannelSortMode,
                    Recording = current.Recording,
                    Favorites = favorites,
                });
            if (channelItemsById.TryGetValue(channel.Id.ToString(), out var item))
            {
                item.IsFavorite = isFavorite;
                if (ReferenceEquals(item, SelectedChannel))
                {
                    OnPropertyChanged(nameof(SelectedFavoriteMenuText));
                }
            }

            ReloadFavoriteChannels();
            if (IsFavoritesOnly)
            {
                ApplyChannelFilter();
            }

            accessibleStatus.AnnounceStatus(isFavorite
                ? $"Dodano kanał {channel.Name} do ulubionych."
                : $"Usunięto kanał {channel.Name} z ulubionych.");
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się zmienić ulubionych", exception.Message);
        }
    }

    private void ToggleFavoritesFilter()
    {
        IsFavoritesOnly = !IsFavoritesOnly;
        ApplyChannelFilter();
        accessibleStatus.AnnounceStatus(IsFavoritesOnly
            ? $"Pokazano ulubione kanały. {ChannelCountText}."
            : $"Pokazano wszystkie kanały. {ChannelCountText}.");
        focusCoordinator.Request(FocusTarget.ChannelList, SelectedChannel?.Id);
    }

    private void ReloadFavoriteChannels()
    {
        FavoriteChannels.Clear();
        foreach (var favorite in preferencesService.Current.Favorites
                     .OrderBy(static favorite => favorite.ChannelName, StringComparer.CurrentCultureIgnoreCase))
        {
            FavoriteChannels.Add(favorite);
        }
    }

    private bool IsFavorite(Channel channel) => preferencesService.Current.Favorites.Any(favorite =>
        favorite.PlaylistSourceId == channel.PlaylistSourceId.Value
        && favorite.ChannelId == channel.Id.Value);

    private async Task StartRecordingAsync(RecordingMode mode)
    {
        var channel = GetCurrentOrSelectedChannel();
        if (channel is null)
        {
            accessibleStatus.ReportError("Wybierz lub odtwórz kanał, który chcesz nagrać.");
            return;
        }

        try
        {
            var programme = epgService.GetCurrentProgramme(channel, DateTimeOffset.Now)?.Title;
            var outputPath = await recordingCoordinator.StartAsync(channel, programme, mode);
            accessibleStatus.AnnounceStatus(
                $"Rozpoczęto nagrywanie {(mode == RecordingMode.StreamCopy ? "wideo" : "audio")} kanału {channel.Name}. Plik: {Path.GetFileName(outputPath)}.");
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się rozpocząć nagrywania", exception.Message);
        }
    }

    private async Task StopRecordingAsync()
    {
        try
        {
            await recordingCoordinator.StopAsync();
            accessibleStatus.AnnounceStatus("Nagrywanie zatrzymane. Plik został zapisany.");
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się zatrzymać nagrywania", exception.Message);
        }
    }

    private async Task ScheduleSelectedRecordingAsync()
    {
        if (SelectedChannel is null)
        {
            accessibleStatus.ReportError("Wybierz kanał do zaplanowania nagrania.");
            return;
        }

        await ScheduleRecordingAsync(SelectedChannel.Id, null);
    }

    private async Task ScheduleRecordingAsync(string channelId, ProgramItemViewModel? programme)
    {
        if (!channelsById.TryGetValue(channelId, out var channel))
        {
            await windowService.ShowMessageAsync(
                "Kanał niedostępny",
                "Wybrany kanał nie jest już dostępny.");
            return;
        }

        var channelItems = allChannels.Select(item => channelItemsById[item.Id.ToString()]).ToArray();
        // The scheduling dialog stores time with one-minute precision. Start at
        // least two whole minutes ahead so trimming seconds can never turn the
        // suggested value into a time that has already passed.
        var localNow = DateTime.Now;
        var minimumStart = new DateTimeOffset(
            new DateTime(
                localNow.Year,
                localNow.Month,
                localNow.Day,
                localNow.Hour,
                localNow.Minute,
                0,
                DateTimeKind.Local).AddMinutes(2));
        var start = programme is null || programme.Start <= minimumStart ? minimumStart : programme.Start;
        var end = programme?.End ?? start.AddHours(1);
        if (end <= start)
        {
            end = start.AddHours(1);
        }

        var dialog = new ScheduleRecordingViewModel(
            channelItems,
            channelId,
            start,
            end,
            programme?.Title);
        var request = await windowService.ShowScheduleRecordingAsync(dialog);
        if (request is null)
        {
            return;
        }

        if (!channelsById.TryGetValue(request.ChannelId, out channel))
        {
            await windowService.ShowMessageAsync("Kanał niedostępny", "Wybrany kanał nie jest już dostępny.");
            return;
        }

        try
        {
            var entry = await recordingCoordinator.ScheduleAsync(channel, request);
            accessibleStatus.AnnounceStatus(
                $"Zaplanowano nagranie kanału {entry.ChannelName} od {entry.Start.ToLocalTime():g} do {entry.End.ToLocalTime():g}.");
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się zaplanować nagrania", exception.Message);
        }
    }

    private async Task ShowScheduledRecordingsAsync()
    {
        try
        {
            var entries = await recordingCoordinator.GetScheduledAsync();
            await windowService.ShowScheduledRecordingsAsync(
                new ScheduledRecordingsViewModel(
                    entries,
                    id => recordingCoordinator.CancelScheduledAsync(id)));
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się odczytać zaplanowanych nagrań", exception.Message);
        }
    }

    private Channel? GetCurrentOrSelectedChannel()
    {
        var currentId = playbackService.Current.ChannelId?.ToString() ?? playbackChannelItemId ?? SelectedChannel?.Id;
        return currentId is not null && channelsById.TryGetValue(currentId, out var channel)
            ? channel
            : null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        playlistLoadCancellation?.Cancel();
        playlistLoadCancellation?.Dispose();
        playlistLoadCancellation = null;
        Interlocked.Exchange(ref searchFilterCancellation, null)?.Cancel();
        currentProgramRefreshTimer.Stop();
        currentProgramRefreshTimer.Tick -= OnCurrentProgramRefreshTimerTick;
        playbackService.StateChanged -= OnPlaybackStateChanged;
        recordingCoordinator.ProgressChanged -= OnRecordingProgressChanged;
    }

    private async Task AddFileSourcesAsync()
    {
        var files = await windowService.PickPlaylistFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        PlaylistSourceItemViewModel? firstAdded = null;
        var addedCount = 0;
        var failedCount = 0;
        foreach (var file in files)
        {
            try
            {
                var import = new LocalPlaylistSourceImport(
                    PlaylistSourceId.New(),
                    Path.GetFileNameWithoutExtension(file.DisplayName),
                    file.Path);
                var source = await sourceStore.ImportFileAsync(import);
                var item = new PlaylistSourceItemViewModel(source);
                Sources.Add(item);
                firstAdded ??= item;
                addedCount++;
            }
            catch (Exception)
            {
                failedCount++;
            }
        }

        if (firstAdded is not null)
        {
            SelectedSource = firstAdded;
            await LoadSourceAsync(firstAdded.Source);
        }

        if (failedCount > 0)
        {
            var summary = addedCount > 0
                ? $"Dodano playlisty: {addedCount}. Nie udało się dodać: {failedCount}."
                : $"Nie udało się dodać żadnej z wybranych playlist. Liczba błędów: {failedCount}.";
            accessibleStatus.ReportError(summary);
        }
    }

    private async Task AddUrlSourceAsync()
    {
        var input = await windowService.PromptForUrlSourceAsync();
        if (input is null)
        {
            return;
        }

        var source = new PlaylistSource(
            PlaylistSourceId.New(),
            input.Name,
            PlaylistSourceKind.Url,
            input.Address.AbsoluteUri,
            input.RefreshOnStartup);
        await sourceStore.SaveAsync(source);
        var item = new PlaylistSourceItemViewModel(source);
        Sources.Add(item);
        SelectedSource = item;
        await LoadSourceAsync(source);
    }

    private async Task RemoveSelectedSourceAsync()
    {
        var sourceToRemove = SelectedSource;
        if (sourceToRemove is null)
        {
            return;
        }

        var confirmed = await windowService.ShowConfirmationAsync(
            "Usuń źródło playlisty",
            "Czy usunąć wybrane źródło playlisty z listy? " +
            "Oryginalny plik wybrany przez użytkownika ani dane dostępne pod adresem URL nie zostaną usunięte. " +
            "W przypadku źródła z pliku TelePilot usunie tylko swoją wewnętrzną kopię.",
            "Usuń źródło");
        if (!confirmed)
        {
            return;
        }

        try
        {
            var removedIndex = Sources.IndexOf(sourceToRemove);
            if (!await sourceStore.RemoveAsync(sourceToRemove.Source.Id))
            {
                await ReportFailureAsync(
                    "Nie udało się usunąć źródła",
                    "Wybrane źródło nie istnieje już w zapisanych ustawieniach.");
                return;
            }

            if (removedIndex >= 0)
            {
                Sources.RemoveAt(removedIndex);
            }
            else
            {
                Sources.Remove(sourceToRemove);
            }

            var nextIndex = Math.Min(Math.Max(removedIndex, 0), Sources.Count - 1);
            SelectedSource = nextIndex >= 0 ? Sources[nextIndex] : null;

            // Kanały należą do usuniętego źródła. Czyścimy je przed próbą
            // wczytania następnego wpisu, aby błąd nowej playlisty nie zostawił
            // na ekranie nieaktualnej listy. Nie zatrzymuje to aktywnego
            // odtwarzania; natywny odtwarzacz zachowuje bieżący strumień.
            await ClearLoadedSourceAsync();

            if (SelectedSource is { } nextSource)
            {
                accessibleStatus.ReportStatus("Usunięto źródło. Wczytywanie następnego źródła playlisty.");
                await LoadSourceAsync(nextSource.Source);
            }
            else
            {
                accessibleStatus.ReportStatus("Usunięto źródło. Lista źródeł playlisty jest pusta.");
            }

            IsChannelListViewVisible = true;
            focusCoordinator.Request(FocusTarget.PlaylistSourceSelector);
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się usunąć źródła", exception.Message);
        }
    }

    private async Task ClearLoadedSourceAsync()
    {
        currentProgramRefreshTimer.Stop();
        allChannels.Clear();
        channelsById.Clear();
        channelItemsById.Clear();
        playbackChannelItemId = null;
        lastSelectedChannelId = null;
        SelectedChannel = null;
        Channels.Clear();
        OnPropertyChanged(nameof(ChannelCountText));

        Groups.Clear();
        Groups.Add(AllGroupsLabel);
        SelectedGroup = AllGroupsLabel;

        try
        {
            await epgService.SetSourcesAsync([]);
        }
        catch (Exception)
        {
            // Usunięcie lokalnego wpisu nie może zostać cofnięte tylko dlatego,
            // że zewnętrzna usługa EPG nie zdołała wyczyścić swojej pamięci.
        }
    }

    private Task RefreshSelectedSourceAsync()
    {
        return SelectedSource is null
            ? Task.CompletedTask
            : LoadSourceAsync(SelectedSource.Source, refreshLocalFile: true);
    }

    private async Task LoadSourceAsync(PlaylistSource source, bool refreshLocalFile = false)
    {
        playlistLoadCancellation?.Cancel();
        playlistLoadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        playlistLoadCancellation = cancellation;
        var token = cancellation.Token;

        IsLoading = true;
        accessibleStatus.ReportStatus($"Wczytywanie źródła {source.Name}.");
        try
        {
            if (refreshLocalFile)
            {
                await sourceStore.RefreshFileAsync(source, token);
            }

            var result = await playlistService.LoadAsync(source, token);
            token.ThrowIfCancellationRequested();
            if (!result.IsSuccess || result.Playlist is null)
            {
                var message = result.Error?.Message ?? "Nieznany błąd wczytywania playlisty.";
                await ReportFailureAsync($"Nie udało się wczytać źródła {source.Name}", message);
                return;
            }

            var epgWarning = string.Empty;
            try
            {
                await epgService.SetSourcesAsync(
                    result.Playlist.Header.EpgUrls,
                    CreateSourceBaseUri(source),
                    token);
                if (epgService.Status.State == EpgServiceState.Failed)
                {
                    epgWarning = " Nie udało się wczytać programu telewizyjnego; kanały są nadal dostępne.";
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                epgWarning = " Nie udało się wczytać programu telewizyjnego; kanały są nadal dostępne.";
            }

            token.ThrowIfCancellationRequested();
            allChannels.Clear();
            allChannels.AddRange(result.Playlist.Channels);
            RebuildChannelItems();
            RebuildGroups();
            ApplyChannelFilter();
            currentProgramRefreshTimer.Start();

            var skipped = result.Playlist.SkippedEntryCount == 0
                ? string.Empty
                : $" Pominięto wpisy: {result.Playlist.SkippedEntryCount}.";
            accessibleStatus.ReportStatus($"Wczytano {allChannels.Count} kanałów ze źródła {source.Name}.{skipped}{epgWarning}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (ReferenceEquals(playlistLoadCancellation, cancellation) && !disposed)
            {
                accessibleStatus.ReportStatus("Anulowano wczytywanie playlisty.");
            }
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
            {
                await ReportFailureAsync($"Nie udało się wczytać źródła {source.Name}", exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(playlistLoadCancellation, cancellation))
            {
                IsLoading = false;
            }
        }
    }

    private void CancelLoading()
    {
        playlistLoadCancellation?.Cancel();
    }

    private static Uri? CreateSourceBaseUri(PlaylistSource source)
        => source.GetResolutionBaseUri();

    private void RebuildChannelItems()
    {
        channelsById.Clear();
        channelItemsById.Clear();
        foreach (var channel in allChannels)
        {
            var id = channel.Id.ToString();
            channelsById[id] = channel;
            channelItemsById[id] = new ChannelListItemViewModel(
                id,
                channel.Name,
                channel.GroupTitle ?? "Bez grupy")
            {
                IsFavorite = IsFavorite(channel),
            };
            var currentProgramme = epgService.GetCurrentProgramme(channel, DateTimeOffset.Now);
            if (currentProgramme is not null)
            {
                channelItemsById[id].CurrentProgram = currentProgramme.Title;
            }
        }
    }

    private void RefreshCurrentProgramItems(DateTimeOffset now)
    {
        foreach ((string id, ChannelListItemViewModel item) in channelItemsById)
        {
            if (!channelsById.TryGetValue(id, out var channel))
            {
                continue;
            }

            item.CurrentProgram = epgService.GetCurrentProgramme(channel, now)?.Title ?? string.Empty;
        }

        if (playbackChannelItemId is not null
            && channelsById.TryGetValue(playbackChannelItemId, out var playbackChannel))
        {
            CurrentProgramName = epgService.GetCurrentProgramme(playbackChannel, now)?.Title
                ?? "Brak danych o bieżącej audycji";
        }
    }

    private void OnCurrentProgramRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!disposed)
        {
            RefreshCurrentProgramItems(DateTimeOffset.Now);
        }
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        Groups.Add(AllGroupsLabel);
        foreach (var group in allChannels
                     .Select(channel => channel.GroupTitle ?? "Bez grupy")
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
        {
            Groups.Add(group);
        }

        if (!Groups.Contains(SelectedGroup))
        {
            SelectedGroup = AllGroupsLabel;
        }
    }

    private void ApplyChannelFilter()
    {
        var selectedId = SelectedChannel?.Id ?? lastSelectedChannelId;
        var channelsToFilter = SelectedGroup == "Bez grupy"
            ? allChannels.Where(channel => string.IsNullOrWhiteSpace(channel.GroupTitle))
            : allChannels;
        var group = SelectedGroup is AllGroupsLabel or "Bez grupy" ? null : SelectedGroup;
        var filtered = ChannelFilter.Apply(
            channelsToFilter,
            new ChannelFilterCriteria(SearchText, group, SelectedSort.Mode));
        if (IsFavoritesOnly)
        {
            filtered = filtered.Where(IsFavorite).ToArray();
        }

        var previousCount = Channels.Count;
        ChannelListItemViewModel[] filteredItems = filtered
            .Select(channel => channelItemsById[channel.Id.ToString()])
            .ToArray();
        Channels.ReplaceAll(filteredItems);

        SelectedChannel = selectedId is not null
            ? Channels.FirstOrDefault(item => item.Id == selectedId)
            : null;
        SelectedChannel ??= Channels.FirstOrDefault();
        if (previousCount != Channels.Count)
        {
            OnPropertyChanged(nameof(ChannelCountText));
        }
    }

    private void ScheduleChannelFilter()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref searchFilterCancellation, cancellation);
        previous?.Cancel();
        _ = ApplyChannelFilterAfterDelayAsync(cancellation);
    }

    private async Task ApplyChannelFilterAfterDelayAsync(CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (!cancellationToken.IsCancellationRequested && !disposed)
                    {
                        ApplyChannelFilter();
                    }
                },
                DispatcherPriority.Background);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Kolejny znak zastąpił to filtrowanie. Dzięki temu czytnik ekranu
            // nie dostaje przebudowy całej listy po każdym naciśnięciu klawisza.
        }
        finally
        {
            Interlocked.CompareExchange(ref searchFilterCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private Task PlaySelectedAsync()
    {
        if (SelectedChannel is null || !channelsById.TryGetValue(SelectedChannel.Id, out var channel))
        {
            accessibleStatus.ReportError("Wybierz kanał do odtworzenia.");
            return Task.CompletedTask;
        }

        return PlayChannelAsync(SelectedChannel, channel, requestFocus: true);
    }

    private async Task PlayAdjacentChannelAsync(int direction)
    {
        if (direction is not (-1 or 1) || Channels.Count == 0)
        {
            return;
        }

        var currentId = playbackService.Current.ChannelId?.ToString()
            ?? playbackChannelItemId
            ?? SelectedChannel?.Id;
        var currentItem = currentId is null
            ? null
            : Channels.FirstOrDefault(channel => channel.Id == currentId);
        var currentIndex = currentItem is null ? -1 : Channels.IndexOf(currentItem);
        var targetIndex = currentIndex < 0
            ? direction > 0 ? 0 : Channels.Count - 1
            : currentIndex + direction;
        if (targetIndex < 0 || targetIndex >= Channels.Count)
        {
            accessibleStatus.ReportStatus(direction < 0
                ? "To pierwszy kanał na liście."
                : "To ostatni kanał na liście.");
            return;
        }

        var targetItem = Channels[targetIndex];
        if (!channelsById.TryGetValue(targetItem.Id, out var targetChannel))
        {
            accessibleStatus.ReportError("Wybrany kanał nie jest już dostępny.");
            return;
        }

        SelectedChannel = targetItem;
        await PlayChannelAsync(targetItem, targetChannel, requestFocus: false);
    }

    private async Task PlayChannelAsync(
        ChannelListItemViewModel channelItem,
        Channel channel,
        bool requestFocus)
    {
        var activationGeneration = Interlocked.Increment(ref playerViewActivationGeneration);
        lastSelectedChannelId = channelItem.Id;
        await ShowPlayerViewAsync(requestFocus, activationGeneration);
        if (!IsCurrentPlayerActivation(activationGeneration))
        {
            return;
        }

        accessibleStatus.ReportStatus($"Łączenie z kanałem {channel.Name}.");
        try
        {
            var result = await playbackService.PlayAsync(channel, new PlaybackOptions());
            if (!IsCurrentPlayerActivation(activationGeneration))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                await ReportFailureAsync(
                    $"Nie udało się odtworzyć kanału {channel.Name}",
                    result.Message ?? "Silnik odtwarzania odrzucił polecenie.");
                ShowChannelListOnly();
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrentPlayerActivation(activationGeneration))
            {
                return;
            }

            await ReportFailureAsync($"Nie udało się odtworzyć kanału {channel.Name}", exception.Message);
            ShowChannelListOnly();
        }
    }

    private async Task StopAsync()
    {
        var stoppedActivationGeneration = Interlocked.Increment(ref playerViewActivationGeneration);
        Task stopOperation;
        try
        {
            // Queue native Stop before yielding a clickable channel list. A new
            // Enter can then only enqueue Play after this Stop operation.
            stopOperation = playbackService.StopAsync();
        }
        catch (Exception exception)
        {
            ReturnToChannelList();
            if (IsCurrentPlayerActivation(stoppedActivationGeneration))
            {
                await ReportFailureAsync("Nie udało się zatrzymać odtwarzania", exception.Message);
            }

            return;
        }

        ReturnToChannelList();
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

        try
        {
            await stopOperation;
            if (!IsCurrentPlayerActivation(stoppedActivationGeneration))
            {
                // A newer Play owns the view, status and focus now.
                return;
            }

            // LibVLC can move native Win32 focus while Stop is completing.
            // Recover it while the player is still marked visible, because the
            // focus coordinator only accepts a same-process video HWND in that
            // state. Otherwise Avalonia can retain logical list focus while
            // NVDA remains attached to the stale rendering window.
            focusCoordinator.Request(FocusTarget.ChannelList, lastSelectedChannelId);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            if (!IsCurrentPlayerActivation(stoppedActivationGeneration))
            {
                return;
            }

            IsPlayerViewVisible = false;
            // Verify the platform focus after hiding the native surface without
            // manufacturing a second logical UIA transition when the selected
            // channel already owns focus. A real recovery is still performed
            // if the native focus moved while the HWND was being hidden.
            focusCoordinator.Request(
                FocusTarget.ChannelList,
                lastSelectedChannelId,
                preserveExistingFocus: true);
            string selectedChannelAnnouncement = SelectedChannel is { } selected
                ? $"Odtwarzanie zatrzymane. Zaznaczony kanał: {selected.AccessibleName}."
                : "Odtwarzanie zatrzymane. Lista kanałów.";
            accessibleStatus.AnnounceStatus(selectedChannelAnnouncement);
        }
        catch (Exception exception)
        {
            if (IsCurrentPlayerActivation(stoppedActivationGeneration))
            {
                await ReportFailureAsync("Nie udało się zatrzymać odtwarzania", exception.Message);
            }
        }
    }

    private async Task PauseResumeAsync()
    {
        try
        {
            var result = await playbackService.TogglePauseAsync();
            if (!result.IsSuccess)
            {
                accessibleStatus.ReportError(result.Message ?? "Nie można zmienić stanu pauzy.");
                await windowService.ShowMessageAsync("Pauza niedostępna", result.Message ?? "Ten strumień nie obsługuje pauzy.");
            }
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się zmienić stanu pauzy", exception.Message);
        }
    }

    private async Task ToggleMuteAsync()
    {
        var target = !IsMuted;
        try
        {
            await playbackService.SetMutedAsync(target);
            accessibleStatus.ReportStatus(target ? "Dźwięk wyciszony." : "Dźwięk włączony.");
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się zmienić wyciszenia", exception.Message);
        }
    }

    public Task SetVolumeAsync(int requestedVolume)
    {
        return SetVolumeCoreAsync(Math.Clamp(requestedVolume, 0, 100));
    }

    private Task ChangeVolumeAsync(int difference)
    {
        return SetVolumeCoreAsync(Math.Clamp(Volume + difference, 0, 100));
    }

    private async Task SetVolumeCoreAsync(int target)
    {
        if (target == Volume)
        {
            accessibleStatus.ReportStatus(VolumeText);
            return;
        }

        try
        {
            await playbackService.SetVolumeAsync(target);
            accessibleStatus.ReportStatus($"Głośność {target} procent.");
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się zmienić głośności", exception.Message);
        }
    }

    private async Task OpenTracksAsync(MediaTrackKind kind)
    {
        try
        {
            var tracks = await playbackService.GetTracksAsync(kind);
            if (tracks.Count == 0)
            {
                await windowService.ShowMessageAsync(
                    kind == MediaTrackKind.Audio ? "Ścieżki dźwiękowe" : "Napisy",
                    kind == MediaTrackKind.Audio
                        ? "Odtwarzany strumień nie udostępnia listy ścieżek dźwiękowych."
                        : "Odtwarzany strumień nie udostępnia napisów.");
                return;
            }

            var title = kind == MediaTrackKind.Audio ? "Ścieżki dźwiękowe" : "Napisy";
            var description = kind == MediaTrackKind.Audio
                ? "Wybierz ścieżkę dźwiękową odtwarzanego kanału."
                : "Wybierz ścieżkę napisów. Pozycja „Wyłączone” wyłącza napisy, jeżeli udostępnia ją strumień.";
            var viewModel = new TrackSelectionViewModel(
                title,
                description,
                tracks.Select(track => new TrackOptionViewModel(
                    track.EngineId,
                    track.Description,
                    track.Language,
                    track.IsSelected)));
            var selectedId = await windowService.SelectTrackAsync(viewModel);
            if (selectedId is null)
            {
                return;
            }

            var result = await playbackService.SelectTrackAsync(kind, selectedId.Value);
            if (result.IsSuccess)
            {
                accessibleStatus.ReportStatus(kind == MediaTrackKind.Audio
                    ? "Zmieniono ścieżkę dźwiękową."
                    : "Zmieniono ustawienie napisów.");
            }
            else
            {
                await ReportFailureAsync("Nie udało się zmienić ścieżki", result.Message ?? "Silnik odrzucił wybór.");
            }
        }
        catch (Exception exception)
        {
            await ReportFailureAsync("Nie udało się pobrać ścieżek", exception.Message);
        }
    }

    private void ToggleChannelList()
    {
        if (IsPlayerViewVisible && IsChannelListViewVisible)
        {
            IsChannelListViewVisible = false;
            focusCoordinator.Request(FocusTarget.PlayerControls);
            return;
        }

        ReturnToChannelList();
    }

    private void ReturnToChannelList()
    {
        // Podczas aktywnego odtwarzania nie ukrywamy natywnego VideoView.
        // Lista pojawia się obok niego, więc fokus nie traci rodzica i LibVLC
        // nie musi przełączać aktywnego uchwytu HWND.
        IsChannelListViewVisible = true;
        focusCoordinator.Request(FocusTarget.ChannelList, lastSelectedChannelId);
    }

    private void ShowChannelListOnly()
    {
        IsChannelListViewVisible = true;
        IsPlayerViewVisible = false;
        focusCoordinator.Request(FocusTarget.ChannelList, lastSelectedChannelId);
    }

    private async Task ShowPlayerViewAsync(bool requestFocus, int activationGeneration)
    {
        IsPlayerViewVisible = true;
        // The logical list view is hidden, but the standard ListBox itself stays
        // mounted underneath the opaque player and is only disabled. Reusing the
        // same ListBoxItem/SelectionItem providers avoids the stale UIA objects
        // that previously caused intermittent silence in NVDA after Stop.
        IsChannelListViewVisible = false;
        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (IsCurrentPlayerActivation(activationGeneration))
                {
                    PlayerViewRequested?.Invoke(this, EventArgs.Empty);
                }
            },
            DispatcherPriority.Loaded);
        if (requestFocus && !IsChannelListViewVisible && IsCurrentPlayerActivation(activationGeneration))
        {
            // Ctrl+L may have opened the list while the player was being laid
            // out. Its explicit focus choice takes precedence over this start.
            focusCoordinator.Request(FocusTarget.PlayerControls);
        }
    }

    private bool IsCurrentPlayerActivation(int generation) =>
        Volatile.Read(ref playerViewActivationGeneration) == generation;

    private async Task OpenProgramGuideAsync()
    {
        var channelItems = allChannels
            .Select(channel => channelItemsById[channel.Id.ToString()])
            .ToArray();
        var viewModel = new ProgramGuideViewModel(
            channelItems,
            LoadProgrammesAsync,
            PlayChannelByIdAsync,
            async cancellationToken =>
            {
                var result = await epgService.RefreshAsync(cancellationToken);
                if (result == EpgRefreshResult.Failed)
                {
                    throw new InvalidOperationException(
                        epgService.Status.ErrorMessage ?? "Nie udało się odświeżyć danych EPG.");
                }

                RefreshCurrentProgramItems(DateTimeOffset.Now);
            },
            initialChannelId: SelectedChannel?.Id,
            scheduleRecording: ScheduleRecordingAsync);
        await windowService.ShowProgramGuideAsync(viewModel);
        if (IsChannelListViewVisible)
        {
            focusCoordinator.Request(FocusTarget.ChannelList, lastSelectedChannelId);
        }
        else if (IsPlayerViewVisible)
        {
            focusCoordinator.Request(FocusTarget.PlayerControls);
        }
        else
        {
            ShowChannelListOnly();
        }
    }

    private async Task<IReadOnlyList<ProgramItemViewModel>> LoadProgrammesAsync(
        string channelId,
        DateOnly date,
        string? search,
        CancellationToken cancellationToken)
    {
        if (!channelsById.TryGetValue(channelId, out var channel))
        {
            return [];
        }

        var programmes = await epgService.GetProgrammesAsync(channel, date, search, cancellationToken);
        var now = DateTimeOffset.Now;
        var currentProgramme = epgService.GetCurrentProgramme(channel, now);
        return programmes
            .Select(programme => ToProgramItem(
                programme,
                currentProgramme is not null
                && currentProgramme.ChannelId == programme.ChannelId
                && currentProgramme.Start == programme.Start))
            .ToArray();
    }

    private async Task PlayChannelByIdAsync(string channelId, CancellationToken cancellationToken)
    {
        if (!channelsById.TryGetValue(channelId, out var channel))
        {
            throw new InvalidOperationException("Wybrany kanał nie jest już dostępny.");
        }

        var activationGeneration = Interlocked.Increment(ref playerViewActivationGeneration);
        lastSelectedChannelId = channelId;
        SelectedChannel = Channels.FirstOrDefault(item => item.Id == channelId) ?? SelectedChannel;
        var wasPlayerViewVisible = IsPlayerViewVisible;
        var wasChannelListViewVisible = IsChannelListViewVisible;
        try
        {
            await ShowPlayerViewAsync(requestFocus: false, activationGeneration);
            if (!IsCurrentPlayerActivation(activationGeneration))
            {
                return;
            }

            var result = await playbackService.PlayAsync(channel, new PlaybackOptions(), cancellationToken);
            if (!IsCurrentPlayerActivation(activationGeneration))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(result.Message ?? "Silnik odtwarzania odrzucił polecenie.");
            }
        }
        catch
        {
            if (!IsCurrentPlayerActivation(activationGeneration))
            {
                return;
            }

            IsChannelListViewVisible = wasChannelListViewVisible;
            IsPlayerViewVisible = wasPlayerViewVisible;
            throw;
        }
    }

    private void NotifyContentLayoutChanged()
    {
        OnPropertyChanged(nameof(ChannelListColumnSpan));
        OnPropertyChanged(nameof(PlayerColumn));
        OnPropertyChanged(nameof(PlayerColumnSpan));
    }

    private static ProgramItemViewModel ToProgramItem(Programme programme, bool isNow)
    {
        var id = $"{programme.ChannelId}-{programme.Start.UtcDateTime.Ticks}";
        return new ProgramItemViewModel(
            id,
            programme.Start.ToLocalTime(),
            programme.Stop?.ToLocalTime(),
            programme.Title,
            programme.Descriptions.Count == 0 ? string.Empty : programme.Descriptions[0].Value,
            programme.Categories.Count == 0 ? null : programme.Categories[0].Value,
            programme.EpisodeNumbers.Count == 0 ? null : programme.EpisodeNumbers[0].Value,
            programme.ProductionYear,
            isNow);
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        bool announceStatus;
        lock (playbackSnapshotGate)
        {
            announceStatus = ShouldAnnouncePlaybackStatus(lastReceivedPlaybackSnapshot, e.Snapshot);
            lastReceivedPlaybackSnapshot = e.Snapshot;
        }

        Dispatcher.UIThread.Post(() => ApplyPlaybackSnapshot(e.Snapshot, announceStatus));
    }

    private void OnRecordingProgressChanged(object? sender, RecordingProgress progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsRecording = progress.State is RecordingState.Preparing or RecordingState.Recording or RecordingState.Stopping;
            RecordingStatusText = progress.State switch
            {
                RecordingState.Preparing => "Przygotowywanie nagrywania",
                RecordingState.Recording => $"Nagrywanie trwa: {progress.Elapsed:hh\\:mm\\:ss}",
                RecordingState.Stopping => "Zatrzymywanie nagrywania",
                RecordingState.Completed => "Nagrywanie zakończone",
                RecordingState.Cancelled => "Nagrywanie anulowane",
                RecordingState.Error => "Błąd nagrywania",
                _ => "Nagrywanie nieaktywne",
            };
        });
    }

    private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot, bool announceStatus)
    {
        CurrentChannelName = snapshot.ChannelName ?? "Nic nie jest odtwarzane";
        CurrentProgramName = snapshot.ChannelId is { } currentChannelId
            && channelsById.TryGetValue(currentChannelId.ToString(), out var currentChannel)
            && epgService.GetCurrentProgramme(currentChannel, DateTimeOffset.Now) is { } currentProgramme
                ? currentProgramme.Title
                : "Brak danych o bieżącej audycji";
        PlaybackStateText = snapshot.State switch
        {
            PlaybackState.Idle => "Zatrzymano",
            PlaybackState.Connecting => "Łączenie",
            PlaybackState.Playing => "Odtwarzanie",
            PlaybackState.Paused => "Pauza",
            PlaybackState.Stopping => "Zatrzymywanie",
            PlaybackState.Error => "Błąd odtwarzania",
            _ => snapshot.State.ToString(),
        };
        // LibVLC reports -1 while the native audio output is being created.
        // Keep the last valid value instead of exposing "minus one percent"
        // through the UI and its live accessibility text.
        if (snapshot.Volume is >= 0 and <= 100)
        {
            Volume = snapshot.Volume;
        }

        IsMuted = snapshot.IsMuted;

        var currentPlaybackChannelId = snapshot.ChannelId?.ToString();
        if (playbackChannelItemId is not null
            && playbackChannelItemId != currentPlaybackChannelId
            && channelItemsById.TryGetValue(playbackChannelItemId, out var previousChannelItem))
        {
            previousChannelItem.PlaybackState = string.Empty;
        }

        if (currentPlaybackChannelId is not null
            && channelItemsById.TryGetValue(currentPlaybackChannelId, out var currentChannelItem))
        {
            currentChannelItem.PlaybackState = PlaybackStateText;
        }

        playbackChannelItemId = currentPlaybackChannelId;

        if (announceStatus && !string.IsNullOrWhiteSpace(snapshot.StatusMessage))
        {
            if (snapshot.State == PlaybackState.Error)
            {
                accessibleStatus.ReportError(snapshot.StatusMessage);
            }
            else
            {
                accessibleStatus.ReportStatus(snapshot.StatusMessage);
            }
        }

        if (IsPlayerViewVisible
            && snapshot == playbackService.Current
            && snapshot.State is PlaybackState.Connecting or PlaybackState.Playing)
        {
            // Utworzenie natywnego hosta obrazu VLC może nastąpić dopiero po
            // rozpoczęciu strumienia i odebrać fokus już po pierwszym pokazaniu
            // widoku. Weryfikacja zachowuje kontrolkę wybraną przez użytkownika,
            // a odzyskuje fokus tylko wtedy, gdy trafił on do hosta wideo.
            // Nie ponawiamy jej dla starego stanu oczekującego w kolejce UI,
            // gdy silnik zdążył już przełączyć kanał lub wykonać Stop.
            focusCoordinator.Request(
                IsChannelListViewVisible ? FocusTarget.ChannelList : FocusTarget.PlayerControls,
                IsChannelListViewVisible ? SelectedChannel?.Id : null,
                preserveExistingFocus: true);
        }
    }

    private static bool ShouldAnnouncePlaybackStatus(
        PlaybackSnapshot? previous,
        PlaybackSnapshot current)
    {
        if (string.IsNullOrWhiteSpace(current.StatusMessage))
        {
            return false;
        }

        return previous is null
            || previous.State != current.State
            || previous.ChannelId != current.ChannelId
            || previous.Error != current.Error
            || !string.Equals(previous.StatusMessage, current.StatusMessage, StringComparison.Ordinal);
    }

    private async Task ReportFailureAsync(string title, string message)
    {
        var safeMessage = string.IsNullOrWhiteSpace(message) ? "Wystąpił nieznany błąd." : message.Trim();
        accessibleStatus.ReportError($"{title}. {safeMessage}");
        await windowService.ShowMessageAsync(title, safeMessage);
    }

    private void RefreshCommandStates()
    {
        (RefreshSourceCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (RemoveSourceCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (CancelLoadingCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PlaySelectedCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ToggleFavoriteCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ScheduleRecordingCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (StartVideoRecordingCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (StartAudioRecordingCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (StopRecordingCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }
}
