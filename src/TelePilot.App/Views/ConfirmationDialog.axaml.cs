using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TelePilot.App.Views;

public sealed partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
        Opened += OnOpened;
        KeyDown += OnKeyDown;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Anulowanie jest bezpieczną akcją domyślną dla operacji usuwania.
        CancelButton.Focus(NavigationMethod.Unspecified, KeyModifiers.None);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close(false);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
