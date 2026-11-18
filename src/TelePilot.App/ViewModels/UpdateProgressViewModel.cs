using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.ViewModels;

public sealed class UpdateProgressViewModel : ObservableObject
{
    private enum Stage { Busy, Offer, Complete }

    private string message = T("Sprawdzanie aktualizacji…", "Checking for updates…");
    private string releaseNotes = string.Empty;
    private Stage stage;
    private bool canRetry, canOpenPage;
    private int percent;
    private bool isIndeterminate = true;
    public UpdateProgressViewModel(Action download, Action cancel, Action retry, Action openPage, Action close)
    {
        DownloadCommand = new RelayCommand(download, () => IsOffering);
        CancelCommand = new RelayCommand(cancel);
        RetryCommand = new RelayCommand(retry);
        OpenPageCommand = new RelayCommand(openPage);
        CloseCommand = new RelayCommand(close);
    }
    public string Title { get; } = T("Aktualizacja TelePilot", "TelePilot update");
    public string CancelLabel { get; } = T("Anuluj", "Cancel");
    public string RetryLabel { get; } = T("Spróbuj ponownie", "Retry");
    public string DownloadPageLabel { get; } = T("Otwórz oficjalną stronę pobierania", "Open official download page");
    public string CloseLabel { get; } = T("Zamknij", "Close");
    public string ReleaseNotesLabel { get; } = T("Historia zmian", "Release notes");
    public string ReleaseNotesHelp { get; } = T("Pole tylko do odczytu. Użyj klawiszy strzałek, aby czytać.", "Read-only field. Use the arrow keys to read.");
    public string DownloadLabel { get; } = T("Pobierz", "Download");
    public string LaterLabel { get; } = T("Później", "Later");
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public string ReleaseNotes { get => releaseNotes; private set => SetProperty(ref releaseNotes, value); }
    public bool IsBusy => stage == Stage.Busy;
    public bool IsOffering => stage == Stage.Offer;
    public bool IsComplete => stage == Stage.Complete;
    public bool CanRetry { get => canRetry; private set => SetProperty(ref canRetry, value); }
    public bool CanOpenPage { get => canOpenPage; private set => SetProperty(ref canOpenPage, value); }
    public int Percent { get => percent; private set => SetProperty(ref percent, value); }
    public bool IsIndeterminate { get => isIndeterminate; private set => SetProperty(ref isIndeterminate, value); }
    public IRelayCommand DownloadCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand RetryCommand { get; }
    public IRelayCommand OpenPageCommand { get; }
    public IRelayCommand CloseCommand { get; }
    internal void Offer(Version version, string? notes)
    {
        Message = T($"Dostępna jest wersja {version}. Po pobraniu TelePilot zamknie się, zainstaluje aktualizację i uruchomi się ponownie. Windows może poprosić o zgodę administratora.",
            $"Version {version} is available. After downloading, TelePilot will close, install the update and reopen. Windows may ask for administrator approval.");
        ReleaseNotes = notes?.ReplaceLineEndings(Environment.NewLine)
            ?? T("Historia zmian tego wydania jest niedostępna.", "Release notes for this version are unavailable.");
        SetStage(Stage.Offer);
    }

    internal void Busy(string text, int? progress = null)
    {
        Message = text; CanRetry = CanOpenPage = false;
        IsIndeterminate = progress is null; Percent = progress ?? 0;
        SetStage(Stage.Busy);
    }
    internal void Complete(string text, bool retry = false, bool openPage = false)
    {
        Message = text; CanRetry = retry; CanOpenPage = openPage; IsIndeterminate = false;
        SetStage(Stage.Complete);
    }

    private void SetStage(Stage value)
    {
        if (stage == value) return;
        stage = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsOffering));
        OnPropertyChanged(nameof(IsComplete));
        DownloadCommand.NotifyCanExecuteChanged();
    }
}
