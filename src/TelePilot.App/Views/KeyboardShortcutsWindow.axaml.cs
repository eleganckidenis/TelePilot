using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TelePilot.App.Views;

public sealed partial class KeyboardShortcutsWindow : Window
{
    public KeyboardShortcutsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ShortcutList.Focus();
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Avalonia.Input.Key.Escape)
            {
                eventArgs.Handled = true;
                Close();
            }
        };
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
