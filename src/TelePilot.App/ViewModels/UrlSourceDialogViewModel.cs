using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelePilot.App.Models;

namespace TelePilot.App.ViewModels;

public sealed class UrlSourceDialogViewModel : ObservableObject
{
    private string sourceName = string.Empty;
    private string address = string.Empty;
    private bool refreshOnStartup = true;
    private string validationMessage = string.Empty;

    public UrlSourceDialogViewModel()
    {
        SubmitCommand = new RelayCommand(Submit);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke());
    }

    public event Action<PlaylistSourceInput>? Accepted;

    public event Action? Cancelled;

    public string SourceName
    {
        get => sourceName;
        set => SetProperty(ref sourceName, value ?? string.Empty);
    }

    public string Address
    {
        get => address;
        set
        {
            if (SetProperty(ref address, value ?? string.Empty))
            {
                ValidationMessage = string.Empty;
            }
        }
    }

    public bool RefreshOnStartup
    {
        get => refreshOnStartup;
        set => SetProperty(ref refreshOnStartup, value);
    }

    public string ValidationMessage
    {
        get => validationMessage;
        private set => SetProperty(ref validationMessage, value);
    }

    public IRelayCommand SubmitCommand { get; }

    public IRelayCommand CancelCommand { get; }

    private void Submit()
    {
        if (!Uri.TryCreate(Address.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ValidationMessage = "Wpisz pełny adres rozpoczynający się od http:// lub https://.";
            return;
        }

        var name = string.IsNullOrWhiteSpace(SourceName)
            ? uri.Host
            : SourceName.Trim();

        ValidationMessage = string.Empty;
        Accepted?.Invoke(new PlaylistSourceInput(name, uri, RefreshOnStartup));
    }
}
