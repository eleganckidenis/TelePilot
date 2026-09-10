using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelePilot.App.Models;
using TelePilot.Recording;

namespace TelePilot.App.ViewModels;

public sealed class ScheduledRecordingsViewModel : ObservableObject
{
    private readonly Func<Guid, Task> cancelRecording;
    private ScheduledRecordingItemViewModel? selectedRecording;
    private string statusMessage;

    public ScheduledRecordingsViewModel(
        IEnumerable<ScheduledRecordingEntry> recordings,
        Func<Guid, Task> cancelRecording)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        this.cancelRecording = cancelRecording ?? throw new ArgumentNullException(nameof(cancelRecording));
        Recordings = new ObservableCollection<ScheduledRecordingItemViewModel>(
            recordings.Select(static entry => new ScheduledRecordingItemViewModel(entry)));
        selectedRecording = Recordings.FirstOrDefault();
        statusMessage = Recordings.Count == 0
            ? "Brak zaplanowanych nagrań."
            : $"Zaplanowane nagrania: {Recordings.Count}.";
        CancelSelectedCommand = new AsyncRelayCommand(CancelSelectedAsync, () => SelectedRecording is not null);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<ScheduledRecordingItemViewModel> Recordings { get; }

    public ScheduledRecordingItemViewModel? SelectedRecording
    {
        get => selectedRecording;
        set
        {
            if (SetProperty(ref selectedRecording, value))
            {
                CancelSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public IAsyncRelayCommand CancelSelectedCommand { get; }

    public IRelayCommand CloseCommand { get; }

    private async Task CancelSelectedAsync()
    {
        var selected = SelectedRecording;
        if (selected is null)
        {
            return;
        }

        try
        {
            await cancelRecording(selected.Id);
            Recordings.Remove(selected);
            SelectedRecording = Recordings.FirstOrDefault();
            StatusMessage = $"Anulowano nagranie kanału {selected.ChannelName}. Pozostało: {Recordings.Count}.";
        }
        catch (Exception)
        {
            StatusMessage = "Nie udało się anulować wybranego nagrania. Jeśli już trwa, zatrzymaj je po zakończeniu zadania.";
        }
    }
}

public sealed class ScheduledRecordingItemViewModel
{
    public ScheduledRecordingItemViewModel(ScheduledRecordingEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    public ScheduledRecordingEntry Entry { get; }

    public Guid Id => Entry.Id;

    public string ChannelName => Entry.ChannelName;

    public string AccessibleName =>
        $"{Entry.ChannelName}, {Entry.Start.ToLocalTime():g} do {Entry.End.ToLocalTime():g}, "
        + (Entry.Mode == RecordingMode.StreamCopy ? "wideo TS" : "audio");

    public string AutomationId => $"scheduled-recording-{Id:D}";
}
