using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TelePilot.App.Views;

public sealed partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
        Opened += (_, _) => PrimaryButton.Focus();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
