using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelePilot.App.Collections;
using TelePilot.Core.CatchUp;
using TelePilot.Core.Models;
using TelePilot.Epg.Models;
using TelePilot.Epg.Services;

namespace TelePilot.App.ViewModels;

public sealed class ArchiveViewModel : ObservableObject, IDisposable
{
    private readonly IEpgSourceManager epg;
    private readonly Func<Channel, Programme, CancellationToken, Task> playArchive;
    private readonly Func<DateTimeOffset> clock;
    private readonly CancellationTokenSource lifetime = new();
    private readonly AsyncRelayCommand loadCommand;
    private readonly AsyncRelayCommand refreshCommand;
    private readonly AsyncRelayCommand playCommand;
    private readonly AsyncRelayCommand previousDayCommand;
    private readonly AsyncRelayCommand nextDayCommand;
    private Channel[] allChannels = [];
    private CancellationTokenSource? activeLoad;
    private CancellationTokenSource? activePlay;
    private Channel? selectedChannel;
    private ArchiveProgramItemViewModel? selectedProgram;
    private DateTime? selectedDate;
    private ArchiveDateOption? selectedDateOption;
    private string statusMessage = "Dodaj playlistę, aby wybrać kanał i sprawdzić informacje o jego archiwum.";
    private bool updatingChannels;
    private bool updatingDates;
    private bool showChannelsWithoutArchive;
    private bool announceChannelSummary;
    private bool disposed;

    public ArchiveViewModel(
        IEpgSourceManager epg,
        Func<Channel, Programme, CancellationToken, Task> playArchive,
        Func<DateTimeOffset>? clock = null)
    {
        this.epg = epg ?? throw new ArgumentNullException(nameof(epg));
        this.playArchive = playArchive ?? throw new ArgumentNullException(nameof(playArchive));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        selectedDate = this.clock().LocalDateTime.Date;
        loadCommand = new AsyncRelayCommand(LoadAsync, CanLoad);
        refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !disposed && SelectedChannel is not null && CatchUpPlayback.IsSupported(SelectedChannel));
        playCommand = new AsyncRelayCommand(PlayAsync, CanPlay);
        previousDayCommand = new AsyncRelayCommand(() => ChangeDayAsync(-1), CanGoBackward);
        nextDayCommand = new AsyncRelayCommand(() => ChangeDayAsync(1), CanGoForward);
    }

    public ResettableObservableCollection<Channel> Channels { get; } = [];
    public ResettableObservableCollection<Channel> VisibleChannels { get; } = [];
    public ResettableObservableCollection<ArchiveDateOption> AvailableDates { get; } = [];
    public ResettableObservableCollection<ArchiveProgramItemViewModel> Programs { get; } = [];

    public Channel? SelectedChannel
    {
        get => selectedChannel;
        set
        {
            if (updatingChannels || disposed || (value is not null && !VisibleChannels.Contains(value)))
            {
                return;
            }

            if (SetProperty(ref selectedChannel, value))
            {
                CancelPlay();
                ClearPrograms();
                RefreshAvailableDates();
                OnPropertyChanged(nameof(ChannelSelectorAccessibleName));
                OnPropertyChanged(nameof(AvailabilityMessage));
                NotifyCommands();
                _ = LoadAsync();
            }
        }
    }

    public bool ShowChannelsWithoutArchive
    {
        get => showChannelsWithoutArchive;
        set
        {
            if (disposed || !SetProperty(ref showChannelsWithoutArchive, value))
            {
                return;
            }

            RefreshVisibleChannels();
        }
    }

    public DateTime? SelectedDate
    {
        get => selectedDate;
        set
        {
            if (!disposed && SetProperty(ref selectedDate, value?.Date))
            {
                CancelLoad();
                CancelPlay();
                ClearPrograms();
                SynchronizeDateOption();
                StatusMessage = value is null ? "Wybierz dzień archiwum z listy." : "Wybierz Pokaż dzień, aby wczytać audycje z tej daty.";
                OnPropertyChanged(nameof(DateSelectorAccessibleName));
                NotifyCommands();
            }
        }
    }

    public ArchiveDateOption? SelectedDateOption
    {
        get => selectedDateOption;
        set
        {
            // Replacing the selector's items can temporarily publish a null item.
            // Only a date offered by the current channel is a user selection.
            if (disposed || updatingDates || value is null || !AvailableDates.Contains(value)
                || ReferenceEquals(value, selectedDateOption))
            {
                return;
            }

            SelectedDate = value.Date;
            _ = LoadAsync();
        }
    }

    public ArchiveProgramItemViewModel? SelectedProgram
    {
        get => selectedProgram;
        set
        {
            if (SetProperty(ref selectedProgram, value))
            {
                OnPropertyChanged(nameof(SelectedProgramDetails));
                playCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            bool includeSummary = announceChannelSummary && value is not "Wczytywanie archiwum…" and not "Odświeżanie EPG archiwum…";
            string message = includeSummary ? $"{ChannelSummary} {value}" : value;
            if (includeSummary)
            {
                announceChannelSummary = false;
            }
            SetProperty(ref statusMessage, message);
        }
    }

    public string SelectedProgramDetails => SelectedProgram?.Details ?? "Wybierz zakończoną audycję, aby poznać szczegóły i odtworzyć ją z archiwum.";
    public string ChannelSelectorAccessibleName => SelectedChannel is null ? "Kanał archiwum, nie wybrano" : $"Kanał archiwum, wybrano: {SelectedChannel.Name}";
    public string DateSelectorAccessibleName => SelectedDateOption is null ? "Data archiwum, nie wybrano" : $"Data archiwum, wybrano: {SelectedDate:dddd, d MMMM yyyy}";
    public string ChannelSummary => $"Kanały ze skonfigurowanym archiwum: {Channels.Count} z {allChannels.Length}. Na liście: {VisibleChannels.Count}.";
    public string AvailabilityMessage => SelectedChannel is not null && CatchUpPlayback.IsSupported(SelectedChannel) && CatchUpPlayback.TryGetRetentionDays(SelectedChannel, out double days)
        ? IsManualProfile(SelectedChannel)
            ? $"Archiwum skonfigurowane ręcznie: do {days:0.##} dni. Ustawienia nie potwierdzają dostępności u dostawcy. Lista obejmuje zakończone audycje z pobranego EPG."
            : $"Archiwum według danych playlisty: do {days:0.##} dni. Lista obejmuje zakończone audycje z pobranego EPG; ich dostępność zależy od dostawcy."
        : SelectedChannel is not null ? GetUnavailableMessage(SelectedChannel)
        : "Wybierz kanał, aby sprawdzić informacje o jego archiwum. Sam program EPG nie potwierdza dostępności nagrań u dostawcy.";

    private const string MissingArchiveInformation = "Playlista nie deklaruje archiwum dla tego kanału. Sam program EPG nie potwierdza dostępności nagrań. Jeśli dostawca potwierdza archiwum, skonfiguruj je dla tego źródła w ustawieniach.";

    private static bool IsManualProfile(Channel channel) =>
        channel.Attributes.GetValueOrDefault("telepilot-catchup-profile") == "manual";

    private static string GetUnavailableMessage(Channel channel) => CatchUpPlayback.GetSupportStatus(channel) switch
    {
        CatchUpSupportStatus.Disabled when IsManualProfile(channel) && channel.Attributes.GetValueOrDefault("catchup") == "none" =>
            "Archiwum dla tej playlisty jest wyłączone w ustawieniach.",
        CatchUpSupportStatus.Disabled => "Playlista oznacza archiwum tego kanału jako wyłączone.",
        CatchUpSupportStatus.MissingRetention => "Informacje o archiwum nie określają liczby dostępnych dni. Sprawdź ją u dostawcy i uzupełnij ustawienia archiwum tego źródła.",
        CatchUpSupportStatus.InvalidRetention => "Informacje o archiwum zawierają nieprawidłową liczbę dostępnych dni. Sprawdź dane playlisty lub ustawienia archiwum tego źródła.",
        CatchUpSupportStatus.UnsupportedMode => "Ten format archiwum nie jest obsługiwany przez TelePilot. Sprawdź format udostępniany przez dostawcę i ustawienia archiwum tego źródła.",
        CatchUpSupportStatus.InvalidTemplate => "Szablon adresu archiwum jest nieprawidłowy lub nieobsługiwany. Poproś dostawcę o zgodną playlistę archiwalną.",
        CatchUpSupportStatus.InvalidCorrection => "Korekta czasu archiwum jest nieprawidłowa. Sprawdź dane playlisty lub ustawienia archiwum tego źródła.",
        CatchUpSupportStatus.UnsupportedStreamUrl => "Nie można zbudować adresu archiwum z adresu tego kanału. Poproś dostawcę o zgodną playlistę archiwalną.",
        _ => MissingArchiveInformation,
    };

    public IAsyncRelayCommand LoadCommand => loadCommand;
    public IAsyncRelayCommand RefreshCommand => refreshCommand;
    public IAsyncRelayCommand PlayCommand => playCommand;
    public IAsyncRelayCommand PreviousDayCommand => previousDayCommand;
    public IAsyncRelayCommand NextDayCommand => nextDayCommand;

    public void UpdateChannels(IEnumerable<Channel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (disposed)
        {
            return;
        }

        CancelLoad();
        CancelPlay();
        ClearPrograms();
        allChannels = channels.DistinctBy(channel => channel.Id).ToArray();
        updatingChannels = true;
        try
        {
            Channels.ReplaceAll(allChannels.Where(CatchUpPlayback.IsSupported));
        }
        finally
        {
            updatingChannels = false;
        }

        RefreshVisibleChannels(reload: true);
    }

    private void RefreshVisibleChannels(bool reload = false)
    {
        announceChannelSummary = true;
        Channel? previous = SelectedChannel;
        updatingChannels = true;
        try
        {
            VisibleChannels.ReplaceAll(ShowChannelsWithoutArchive ? allChannels : Channels);
        }
        finally
        {
            updatingChannels = false;
        }

        OnPropertyChanged(nameof(ChannelSummary));
        Channel? replacement = VisibleChannels.FirstOrDefault(channel => channel.Id == previous?.Id)
            ?? VisibleChannels.FirstOrDefault(CatchUpPlayback.IsSupported) ?? VisibleChannels.FirstOrDefault();
        if (ReferenceEquals(replacement, SelectedChannel))
        {
            if (reload)
            {
                RefreshAvailableDates();
                _ = LoadAsync();
            }
            else
            {
                StatusMessage = SelectedChannel is null ? GetEmptyChannelMessage() : AvailabilityMessage;
            }
        }
        else
        {
            SelectedChannel = replacement;
        }

        NotifyCommands();
    }

    public Task LoadAsync() => LoadCoreAsync(refresh: false);
    public Task RefreshAsync()
    {
        if (!disposed)
        {
            DateTime? previousDate = SelectedDate;
            RefreshAvailableDates();
            if (SelectedDate != previousDate)
            {
                CancelPlay();
                ClearPrograms();
            }
            NotifyCommands();
        }
        return LoadCoreAsync(refresh: true);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        CancelLoad();
        CancelPlay();
        lifetime.Dispose();
        NotifyCommands();
    }

    private bool CanLoad() => !disposed && SelectedChannel is not null && CatchUpPlayback.IsSupported(SelectedChannel) && SelectedDate is not null;
    private string GetEmptyChannelMessage() => allChannels.Length == 0
        ? "Dodaj playlistę, aby wybrać kanał i sprawdzić informacje o jego archiwum."
        : "Brak kanałów ze skonfigurowanym archiwum. Zaznacz opcję pokazywania kanałów bez informacji o archiwum, aby sprawdzić każdy kanał.";
    private bool CanGoBackward() => CanLoad() && AvailableDates.Count > 0 && SelectedDate!.Value > AvailableDates[^1].Date;
    private bool CanGoForward() => CanLoad() && AvailableDates.Count > 0 && SelectedDate!.Value < AvailableDates[0].Date;

    private void RefreshAvailableDates()
    {
        updatingDates = true;
        try
        {
            DateTime today = clock().LocalDateTime.Date;
            int days = SelectedChannel is not null && CatchUpPlayback.IsSupported(SelectedChannel) && CatchUpPlayback.TryGetRetentionDays(SelectedChannel, out double retention)
                ? (int)Math.Min(Math.Ceiling(retention), (today - DateTime.MinValue.Date).TotalDays)
                : -1;
            // Catch-up metadata accepts at most 36500 days, bounding even malformed
            // provider declarations. Include the earliest partial calendar day;
            // programme availability still checks its exact start time.
            AvailableDates.ReplaceAll(days < 0
                ? []
                : Enumerable.Range(0, days + 1).Select(offset => new ArchiveDateOption(today.AddDays(-offset))));
            if (AvailableDates.Count > 0)
            {
                DateTime chosen = SelectedDate ?? today;
                chosen = chosen < AvailableDates[^1].Date ? AvailableDates[^1].Date : chosen > today ? today : chosen;
                SetProperty(ref selectedDate, chosen, nameof(SelectedDate));
            }
            SynchronizeDateOption();
            OnPropertyChanged(nameof(DateSelectorAccessibleName));
        }
        finally
        {
            updatingDates = false;
        }
    }

    private void SynchronizeDateOption() => SetProperty(ref selectedDateOption,
        AvailableDates.FirstOrDefault(option => option.Date == SelectedDate), nameof(SelectedDateOption));

    private bool CanPlay() => !disposed && SelectedChannel is not null && CatchUpPlayback.IsSupported(SelectedChannel) && SelectedProgram is not null
        && Programs.Contains(SelectedProgram) && IsAvailable(SelectedChannel, SelectedProgram.Programme, clock());

    private static bool IsAvailable(Channel channel, Programme programme, DateTimeOffset now) =>
        programme.Stop is { } stop && CatchUpPlayback.TryCreatePlaybackChannel(channel, programme.Start, stop, now, out _, out _);

    private async Task LoadCoreAsync(bool refresh)
    {
        CancelLoad();
        if (!CanLoad())
        {
            if (!disposed)
            {
                StatusMessage = SelectedChannel is not null && !CatchUpPlayback.IsSupported(SelectedChannel)
                    ? GetUnavailableMessage(SelectedChannel)
                    : SelectedChannel is null
                        ? GetEmptyChannelMessage()
                        : "Wybierz dzień archiwum z listy.";
            }
            return;
        }

        Channel channel = SelectedChannel!;
        DateTime date = SelectedDate!.Value;
        DateTimeOffset? previousStart = SelectedProgram?.Programme.Start;
        using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        activeLoad = operation;
        CancellationToken token = operation.Token;
        try
        {
            StatusMessage = refresh ? "Odświeżanie EPG archiwum…" : "Wczytywanie archiwum…";
            if (refresh)
            {
                EpgRefreshResult refreshResult = await epg.RefreshAsync(token);
                token.ThrowIfCancellationRequested();
                if (refreshResult == EpgRefreshResult.Failed)
                {
                    ClearPrograms();
                    StatusMessage = "Nie udało się odświeżyć EPG archiwum. Sprawdź źródło EPG i spróbuj ponownie.";
                    return;
                }
            }
            IReadOnlyList<Programme> result = await epg.GetProgrammesAsync(channel, DateOnly.FromDateTime(date), cancellationToken: token);
            token.ThrowIfCancellationRequested();
            DateTimeOffset now = clock();
            ArchiveProgramItemViewModel[] available = result
                .Where(programme => IsAvailable(channel, programme, now))
                .OrderBy(programme => programme.Start)
                .Select(programme => new ArchiveProgramItemViewModel(programme))
                .ToArray();
            Programs.ReplaceAll(available);
            SelectedProgram = Programs.FirstOrDefault(programme => programme.Programme.Start == previousStart) ?? Programs.FirstOrDefault();
            StatusMessage = Programs.Count == 0
                ? $"{channel.Name}, {date:d}: brak zakończonych audycji w zasięgu archiwum. EPG może nie zawierać wcześniejszych danych. Wybierz inny dzień lub odśwież EPG."
                : $"{channel.Name}, {date:d}: dostępnych audycji: {Programs.Count}. Enter odtwarza wybraną audycję z archiwum.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
            {
                ClearPrograms();
                StatusMessage = "Nie udało się wczytać archiwum. Sprawdź źródło EPG i spróbuj ponownie.";
            }
        }
        finally
        {
            if (ReferenceEquals(activeLoad, operation))
            {
                activeLoad = null;
            }
            NotifyCommands();
        }
    }

    private async Task PlayAsync()
    {
        if (!CanPlay())
        {
            StatusMessage = "Wybrana audycja nie jest już dostępna w archiwum. Odśwież listę audycji.";
            playCommand.NotifyCanExecuteChanged();
            return;
        }

        Channel channel = SelectedChannel!;
        Programme programme = SelectedProgram!.Programme;
        using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        activePlay = operation;
        CancellationToken token = operation.Token;
        try
        {
            await playArchive(channel, programme, token);
            if (!token.IsCancellationRequested && !disposed && ReferenceEquals(channel, SelectedChannel))
            {
                StatusMessage = $"Uruchomiono archiwum: {channel.Name}, {programme.Title}.";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (ArchivePlaybackException exception)
        {
            if (!token.IsCancellationRequested && !disposed)
            {
                StatusMessage = exception.Message;
            }
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && !disposed)
            {
                StatusMessage = "Nie udało się odtworzyć archiwum. Sprawdź dostępność audycji u dostawcy i spróbuj ponownie.";
            }
        }
        finally
        {
            if (ReferenceEquals(activePlay, operation))
            {
                activePlay = null;
            }
        }
    }

    private async Task ChangeDayAsync(int difference)
    {
        if ((difference < 0 && CanGoBackward()) || (difference > 0 && CanGoForward()))
        {
            SelectedDate = SelectedDate!.Value.AddDays(difference);
            await LoadAsync();
        }
    }

    private void ClearPrograms()
    {
        Programs.ReplaceAll([]);
        SelectedProgram = null;
    }

    private void CancelLoad()
    {
        activeLoad?.Cancel();
        activeLoad = null;
    }

    public void CancelPendingPlayback() => CancelPlay();

    private void CancelPlay()
    {
        activePlay?.Cancel();
        activePlay = null;
    }

    private void NotifyCommands()
    {
        loadCommand.NotifyCanExecuteChanged();
        refreshCommand.NotifyCanExecuteChanged();
        playCommand.NotifyCanExecuteChanged();
        previousDayCommand.NotifyCanExecuteChanged();
        nextDayCommand.NotifyCanExecuteChanged();
    }
}

public sealed class ArchiveDateOption(DateTime date)
{
    public DateTime Date { get; } = date.Date;
    public string DisplayName => Date.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    public override string ToString() => DisplayName;
}

public sealed class ArchiveProgramItemViewModel
{
    private readonly ProgramItemViewModel display;

    public ArchiveProgramItemViewModel(Programme programme)
    {
        Programme = programme ?? throw new ArgumentNullException(nameof(programme));
        display = new ProgramItemViewModel(
            programme.Start.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            programme.Start.ToLocalTime(), programme.Stop?.ToLocalTime(), programme.Title,
            string.Join(Environment.NewLine, programme.Descriptions.Select(text => text.Value)),
            string.Join(", ", programme.Categories.Select(text => text.Value)),
            null, programme.ProductionYear, false);
    }

    public Programme Programme { get; }
    public string AccessibleName => display.AccessibleName;
    public string AutomationId => $"archive-{display.Id}";
    public string Details => display.Details;
}
