using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static TelePilot.Core.Localization.Localizer;

namespace TelePilot.App.ViewModels;

public sealed class UpdateProgressViewModel : ObservableObject
{
    private string message = T("Sprawdzanie aktualizacji…", "Checking for updates…");
    private bool isBusy = true, canRetry, canOpenPage;
    private int percent;
    private bool isIndeterminate = true;
    public UpdateProgressViewModel(Action cancel, Action retry, Action openPage, Action close)
    {
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
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public bool IsBusy { get => isBusy; private set { if (SetProperty(ref isBusy, value)) OnPropertyChanged(nameof(IsComplete)); } }
    public bool IsComplete => !IsBusy;
    public bool CanRetry { get => canRetry; private set => SetProperty(ref canRetry, value); }
    public bool CanOpenPage { get => canOpenPage; private set => SetProperty(ref canOpenPage, value); }
    public int Percent { get => percent; private set => SetProperty(ref percent, value); }
    public bool IsIndeterminate { get => isIndeterminate; private set => SetProperty(ref isIndeterminate, value); }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand RetryCommand { get; }
    public IRelayCommand OpenPageCommand { get; }
    public IRelayCommand CloseCommand { get; }
    internal void Busy(string text, int? progress = null)
    {
        Message = text; IsBusy = true; CanRetry = CanOpenPage = false;
        IsIndeterminate = progress is null; Percent = progress ?? 0;
    }
    internal void Complete(string text, bool retry = false, bool openPage = false)
    {
        Message = text; IsBusy = false; CanRetry = retry; CanOpenPage = openPage; IsIndeterminate = false;
    }
}
