using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TelePilot.App.ViewModels;

public sealed class ProgramGuideViewModel : ObservableObject, IDisposable
{
    private readonly Func<string, DateOnly, string?, CancellationToken, Task<IReadOnlyList<ProgramItemViewModel>>> loadPrograms;
    private readonly Func<string, CancellationToken, Task> playChannel;
    private readonly Func<CancellationToken, Task> refreshGuide;
    private readonly Func<string, ProgramItemViewModel?, Task>? scheduleRecording;
    private readonly AsyncRelayCommand loadCommand;
    private readonly AsyncRelayCommand refreshCommand;
    private readonly AsyncRelayCommand playChannelCommand;
    private readonly AsyncRelayCommand previousDayCommand;
    private readonly AsyncRelayCommand nextDayCommand;
    private readonly AsyncRelayCommand goToNowCommand;
    private readonly AsyncRelayCommand scheduleProgrammeCommand;
    private readonly AsyncRelayCommand scheduleChannelCommand;
    private CancellationTokenSource? activeLoad;
    private ChannelListItemViewModel? selectedChannel;
    private ProgramItemViewModel? selectedProgram;
    private DateTime? selectedDate;
    private string searchText = string.Empty;
    private string statusMessage = "Wybierz kanał i datę.";
    private bool disposed;

    public ProgramGuideViewModel(
        IEnumerable<ChannelListItemViewModel> channels,
        Func<string, DateOnly, string?, CancellationToken, Task<IReadOnlyList<ProgramItemViewModel>>> loadPrograms,
        Func<string, CancellationToken, Task> playChannel,
        Func<CancellationToken, Task> refreshGuide,
        DateTimeOffset? initialDate = null,
        string? initialChannelId = null,
        Func<string, ProgramItemViewModel?, Task>? scheduleRecording = null)
    {
        ArgumentNullException.ThrowIfNull(channels);
        this.loadPrograms = loadPrograms ?? throw new ArgumentNullException(nameof(loadPrograms));
        this.playChannel = playChannel ?? throw new ArgumentNullException(nameof(playChannel));
        this.refreshGuide = refreshGuide ?? throw new ArgumentNullException(nameof(refreshGuide));
        this.scheduleRecording = scheduleRecording;

        Channels = new ObservableCollection<ChannelListItemViewModel>(channels);
        Programs = [];
        selectedDate = initialDate?.ToLocalTime().Date ?? DateTime.Today;
        loadCommand = new AsyncRelayCommand(LoadAsync, CanLoad);
        refreshCommand = new AsyncRelayCommand(RefreshAsync);
        playChannelCommand = new AsyncRelayCommand(PlaySelectedChannelAsync, () => SelectedChannel is not null);
        previousDayCommand = new AsyncRelayCommand(() => ChangeDayAsync(-1), CanLoad);
        nextDayCommand = new AsyncRelayCommand(() => ChangeDayAsync(1), CanLoad);
        goToNowCommand = new AsyncRelayCommand(GoToNowAsync, CanLoad);
        scheduleProgrammeCommand = new AsyncRelayCommand(
            ScheduleSelectedProgrammeAsync,
            () => this.scheduleRecording is not null && SelectedChannel is not null && SelectedProgram is not null);
        scheduleChannelCommand = new AsyncRelayCommand(
            ScheduleSelectedChannelAsync,
            () => this.scheduleRecording is not null && SelectedChannel is not null);
        SelectedChannel = string.IsNullOrWhiteSpace(initialChannelId)
            ? Channels.FirstOrDefault()
            : Channels.FirstOrDefault(channel => channel.Id == initialChannelId) ?? Channels.FirstOrDefault();
    }

    public ObservableCollection<ChannelListItemViewModel> Channels { get; }

    public ObservableCollection<ProgramItemViewModel> Programs { get; }

    public ChannelListItemViewModel? SelectedChannel
    {
        get => selectedChannel;
        set
        {
            if (SetProperty(ref selectedChannel, value))
            {
                OnPropertyChanged(nameof(ChannelSelectorAccessibleName));
                loadCommand.NotifyCanExecuteChanged();
                playChannelCommand.NotifyCanExecuteChanged();
                NotifyDateCommandStates();
                scheduleProgrammeCommand.NotifyCanExecuteChanged();
                scheduleChannelCommand.NotifyCanExecuteChanged();
                _ = LoadAsync();
            }
        }
    }

    public ProgramItemViewModel? SelectedProgram
    {
        get => selectedProgram;
        set
        {
            if (SetProperty(ref selectedProgram, value))
            {
                OnPropertyChanged(nameof(SelectedProgramDetails));
                scheduleProgrammeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DateTime? SelectedDate
    {
        get => selectedDate;
        set
        {
            var date = value?.Date;
            if (SetProperty(ref selectedDate, date))
            {
                OnPropertyChanged(nameof(DateSelectorAccessibleName));
                NotifyDateCommandStates();
            }
        }
    }

    public string SearchText
    {
        get => searchText;
        set => SetProperty(ref searchText, value ?? string.Empty);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string SelectedProgramDetails => SelectedProgram?.Details ?? "Wybierz audycję, aby poznać szczegóły.";

    public string ChannelSelectorAccessibleName => SelectedChannel is null
        ? "Kanał programu telewizyjnego, nie wybrano"
        : $"Kanał programu telewizyjnego, wybrano: {SelectedChannel.Name}, grupa: {SelectedChannel.Group}";

    public string DateSelectorAccessibleName => SelectedDate is null
        ? "Data programu telewizyjnego, nie wpisano prawidłowej daty"
        : $"Data programu telewizyjnego, wybrano: {SelectedDate.Value:dddd, d MMMM yyyy}";

    public IAsyncRelayCommand LoadCommand => loadCommand;

    public IAsyncRelayCommand RefreshCommand => refreshCommand;

    public IAsyncRelayCommand PlayChannelCommand => playChannelCommand;

    public IAsyncRelayCommand PreviousDayCommand => previousDayCommand;

    public IAsyncRelayCommand NextDayCommand => nextDayCommand;

    public IAsyncRelayCommand GoToNowCommand => goToNowCommand;

    public IAsyncRelayCommand ScheduleProgrammeCommand => scheduleProgrammeCommand;

    public IAsyncRelayCommand ScheduleChannelCommand => scheduleChannelCommand;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        activeLoad?.Cancel();
        activeLoad?.Dispose();
        activeLoad = null;
    }

    private bool CanLoad()
    {
        return SelectedChannel is not null && SelectedDate is not null;
    }

    private async Task LoadAsync()
    {
        if (disposed || SelectedChannel is null || SelectedDate is null)
        {
            return;
        }

        activeLoad?.Cancel();
        activeLoad?.Dispose();
        activeLoad = new CancellationTokenSource();
        var token = activeLoad.Token;
        var channelId = SelectedChannel.Id;
        var date = DateOnly.FromDateTime(SelectedDate.Value);
        var query = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

        try
        {
            StatusMessage = "Wczytywanie programu…";
            var result = await loadPrograms(channelId, date, query, token);
            token.ThrowIfCancellationRequested();

            Programs.Clear();
            foreach (var program in result)
            {
                Programs.Add(program);
            }

            SelectedProgram = Programs.FirstOrDefault(program => program.IsNow) ?? Programs.FirstOrDefault();
            var context = $"{SelectedDate.Value:dddd, d MMMM yyyy}, kanał {SelectedChannel.Name}";
            StatusMessage = Programs.Count == 0
                ? $"{context}: brak audycji."
                : $"{context}: wczytano {Programs.Count} audycji.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            StatusMessage = "Nie udało się wczytać programu. Spróbuj ponownie lub odśwież dane EPG.";
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusMessage = "Odświeżanie programu…";
            await refreshGuide(CancellationToken.None);
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Anulowano odświeżanie programu.";
        }
        catch (Exception)
        {
            StatusMessage = "Nie udało się odświeżyć programu. Sprawdź źródło EPG i spróbuj ponownie.";
        }
    }

    private async Task PlaySelectedChannelAsync()
    {
        if (SelectedChannel is null)
        {
            return;
        }

        try
        {
            await playChannel(SelectedChannel.Id, CancellationToken.None);
            StatusMessage = $"Rozpoczęto odtwarzanie kanału {SelectedChannel.Name}.";
        }
        catch (Exception)
        {
            StatusMessage = $"Nie udało się odtworzyć kanału {SelectedChannel.Name}. Wróć do listy i spróbuj ponownie.";
        }
    }

    private async Task GoToNowAsync()
    {
        SelectedDate = DateTime.Today;
        await LoadAsync();
        SelectedProgram = Programs.FirstOrDefault(program => program.IsNow);
        if (SelectedProgram is null && SelectedChannel is not null)
        {
            StatusMessage = $"Kanał {SelectedChannel.Name}: brak aktualnie emitowanej audycji.";
        }
    }

    private async Task ChangeDayAsync(int difference)
    {
        if (SelectedDate is null)
        {
            return;
        }

        SelectedDate = SelectedDate.Value.AddDays(difference);
        await LoadAsync();
    }

    private async Task ScheduleSelectedProgrammeAsync()
    {
        if (scheduleRecording is null || SelectedChannel is null || SelectedProgram is null)
        {
            return;
        }

        await scheduleRecording(SelectedChannel.Id, SelectedProgram);
        StatusMessage = $"Otwarto planowanie nagrania audycji {SelectedProgram.Title}.";
    }

    private async Task ScheduleSelectedChannelAsync()
    {
        if (scheduleRecording is null || SelectedChannel is null)
        {
            return;
        }

        await scheduleRecording(SelectedChannel.Id, null);
        StatusMessage = $"Otwarto planowanie nagrania kanału {SelectedChannel.Name}.";
    }

    private void NotifyDateCommandStates()
    {
        loadCommand.NotifyCanExecuteChanged();
        previousDayCommand.NotifyCanExecuteChanged();
        nextDayCommand.NotifyCanExecuteChanged();
        goToNowCommand.NotifyCanExecuteChanged();
        scheduleProgrammeCommand.NotifyCanExecuteChanged();
        scheduleChannelCommand.NotifyCanExecuteChanged();
    }
}
