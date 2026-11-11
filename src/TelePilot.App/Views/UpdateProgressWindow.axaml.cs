using Avalonia.Controls;
using Avalonia.Input;
using TelePilot.App.ViewModels;

namespace TelePilot.App.Views;

public sealed partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
        Opened += (_, _) => { if (ShowActivated) FocusAction(); };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Close(); }
        };
    }
    internal void FocusAction()
    {
        if (DataContext is UpdateProgressViewModel { IsBusy: true }) CancelButton.Focus();
        else if (DataContext is UpdateProgressViewModel { CanRetry: true }) RetryButton.Focus();
        else CloseButton.Focus();
    }
}
