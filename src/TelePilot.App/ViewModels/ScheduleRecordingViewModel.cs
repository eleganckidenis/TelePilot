using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelePilot.App.Models;
using TelePilot.Recording;

namespace TelePilot.App.ViewModels;

public sealed class ScheduleRecordingViewModel : ObservableObject
{
    private ChannelListItemViewModel selectedChannel;
    private DateTime? startDate;
    private DateTime? endDate;
    private string startTime;
    private string endTime;
    private RecordingModeOption selectedMode;
    private string validationMessage = string.Empty;

    public ScheduleRecordingViewModel(
        IEnumerable<ChannelListItemViewModel> channels,
        string initialChannelId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? programmeTitle = null)
    {
        Channels = channels?.ToArray() ?? throw new ArgumentNullException(nameof(channels));
        if (Channels.Count == 0)
        {
            throw new ArgumentException("Do zaplanowania nagrania potrzebny jest co najmniej jeden kanał.", nameof(channels));
        }

        Modes =
        [
            new RecordingModeOption(RecordingMode.StreamCopy, "Wideo w oryginalnym formacie TS"),
            new RecordingModeOption(RecordingMode.AudioOnly, "Tylko audio"),
        ];
        selectedChannel = Channels.FirstOrDefault(channel => channel.Id == initialChannelId) ?? Channels[0];
        var localStart = start.ToLocalTime();
        var localEnd = end.ToLocalTime();
        startDate = localStart.Date;
        endDate = localEnd.Date;
        startTime = localStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        endTime = localEnd.ToString("HH:mm", CultureInfo.InvariantCulture);
        selectedMode = Modes[0];
        ProgrammeTitle = string.IsNullOrWhiteSpace(programmeTitle) ? null : programmeTitle.Trim();
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, null));
    }

    public event EventHandler<RecordingScheduleRequest?>? CloseRequested;

    public IReadOnlyList<ChannelListItemViewModel> Channels { get; }

    public IReadOnlyList<RecordingModeOption> Modes { get; }

    public string? ProgrammeTitle { get; }

    public string ProgrammeLabel => ProgrammeTitle is null ? "Nagranie ręczne" : $"Audycja: {ProgrammeTitle}";

    public ChannelListItemViewModel SelectedChannel
    {
        get => selectedChannel;
        set => SetProperty(ref selectedChannel, value);
    }

    public DateTime? StartDate
    {
        get => startDate;
        set => SetProperty(ref startDate, value?.Date);
    }

    public DateTime? EndDate
    {
        get => endDate;
        set => SetProperty(ref endDate, value?.Date);
    }

    public string StartTime
    {
        get => startTime;
        set => SetProperty(ref startTime, value ?? string.Empty);
    }

    public string EndTime
    {
        get => endTime;
        set => SetProperty(ref endTime, value ?? string.Empty);
    }

    public RecordingModeOption SelectedMode
    {
        get => selectedMode;
        set => SetProperty(ref selectedMode, value);
    }

    public string ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private void Save()
    {
        if (!TryCreateLocalDateTime(StartDate, StartTime, out var startValue))
        {
            ValidationMessage = "Wpisz prawidłową datę i godzinę rozpoczęcia w formacie GG:mm.";
            return;
        }

        if (!TryCreateLocalDateTime(EndDate, EndTime, out var endValue))
        {
            ValidationMessage = "Wpisz prawidłową datę i godzinę zakończenia w formacie GG:mm.";
            return;
        }

        if (startValue <= DateTimeOffset.Now.AddSeconds(15))
        {
            ValidationMessage = "Nagranie musi rozpocząć się co najmniej 15 sekund w przyszłości.";
            return;
        }

        if (endValue <= startValue)
        {
            ValidationMessage = "Zakończenie nagrania musi przypadać po jego rozpoczęciu.";
            return;
        }

        ValidationMessage = string.Empty;
        CloseRequested?.Invoke(
            this,
            new RecordingScheduleRequest(
                SelectedChannel.Id,
                SelectedChannel.Name,
                startValue,
                endValue,
                SelectedMode.Mode,
                ProgrammeTitle));
    }

    private static bool TryCreateLocalDateTime(DateTime? date, string timeText, out DateTimeOffset value)
    {
        value = default;
        if (date is null
            || !TimeOnly.TryParseExact(
                timeText?.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            return false;
        }

        var local = DateTime.SpecifyKind(date.Value.Date + time.ToTimeSpan(), DateTimeKind.Local);
        value = new DateTimeOffset(local);
        return true;
    }
}

public sealed record RecordingModeOption(RecordingMode Mode, string Name)
{
    public override string ToString() => Name;
}
