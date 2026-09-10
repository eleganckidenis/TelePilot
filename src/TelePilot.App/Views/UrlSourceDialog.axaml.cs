using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TelePilot.App.Models;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class UrlSourceDialog : Window
{
    private UrlSourceDialogViewModel? subscribedViewModel;

    public UrlSourceDialog()
    {
        InitializeComponent();
        SourceNameTextBox.PreviousTabTarget = SubmitButton;
        SourceNameTextBox.NextTabTarget = AddressTextBox;
        AddressTextBox.PreviousTabTarget = SourceNameTextBox;
        AddressTextBox.NextTabTarget = RefreshOnStartupCheckBox;
        SubmitButton.AddHandler(
            InputElement.KeyDownEvent,
            OnSubmitButtonKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnSubmitButtonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        Dispatcher.UIThread.Post(
            () => SourceNameTextBox.FocusEditor(),
            DispatcherPriority.Input);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        AddressTextBox.FocusEditor();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        subscribedViewModel = DataContext as UrlSourceDialogViewModel;
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.Accepted += OnAccepted;
            subscribedViewModel.Cancelled += OnCancelled;
        }
    }

    private void OnAccepted(PlaylistSourceInput input)
    {
        Close(input);
    }

    private void OnCancelled()
    {
        Close(null);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Unsubscribe();
        DataContextChanged -= OnDataContextChanged;
        Opened -= OnOpened;
        Closed -= OnClosed;
    }

    private void Unsubscribe()
    {
        if (subscribedViewModel is null)
        {
            return;
        }

        subscribedViewModel.Accepted -= OnAccepted;
        subscribedViewModel.Cancelled -= OnCancelled;
        subscribedViewModel = null;
    }
}
